using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Integrity;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.ProductBank;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.IntegrityDtos;
using ViewModels.NasbNiro.Pagination;
using Procurement = Services.NasbNiro.Procurement;

namespace Services.NasbNiro.Integrity
{
    public interface ISafetySettingsService
    {
        /// <summary>The strict profile with the value of every security-relevant setting now (the settings page).</summary>
        Task<ResponseDto<List<SafetyItemDto>>> GetProfileAsync(CancellationToken ct);

        /// <summary>Only the settings that differ from the strict profile (the check endpoint; the AI internal auditor reads it).</summary>
        Task<ResponseDto<SafetyDeviationsDto>> GetDeviationsAsync(CancellationToken ct);

        Task<ResponseDto<SafetyChangeDto>> RequestAsync(SafetyRequestDto dto, CancellationToken ct);
        Task<ResponseDto<SafetyChangeDto>> DecideAsync(SafetyDecisionDto dto, CancellationToken ct);
        Task<ResponseDto<SafetyChangeDto>> CancelAsync(int id, CancellationToken ct);
        Task<ResponseDto<PagedList<SafetyChangeDto>>> GetChangesAsync(SafetyChangeQuery query, CancellationToken ct);
    }

    /// <summary>
    /// Package D: the two-person change of a security-relevant setting. The requester names the setting, the new value and the reason; a second person (a PMO or CEO user who is not the requester, never an AI seat)
    /// approves, and only then is the value written. Both people, the reason and the old and new value are on the request, the setting row and the request are in the audit chain.
    /// The settings themselves (and the families other modules register) are listed in <see cref="SafetyProfile"/>.
    /// </summary>
    public class SafetySettingsService : ISafetySettingsService
    {
        public const string Approvers = "PMO,CEO";
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string SameUser = "تایید باید توسط شخص دیگری انجام شود (تفکیک وظایف)؛ درخواست‌دهنده تغییر را تایید نمی‌کند.";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public SafetySettingsService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify)
        {
            _uow = uow;
            _user = user;
            _notify = notify;
        }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);

        private static string RequestRoles(SafetyStore store)
        {
            switch (store)
            {
                case SafetyStore.ProjectControl: return "PMO,CEO";
                case SafetyStore.ProductBank: return PbRoles.Planning + ",CEO";
                case SafetyStore.Procurement: return ProcurementRolesText + ",CEO";
                case SafetyStore.Finance: return FinanceRoles.Managers + ",CEO";
                case SafetyStore.Quality: return QualityRoles.Audit + ",CEO";
                case SafetyStore.AiSeat: return AiRoles.Admins + ",CEO";
                default: return "PMO,CEO";
            }
        }

        private const string ProcurementRolesText = Procurement.ProcurementRoles.Management;

        private static string StoreName(SafetyStore s) => s.ToString();

        // ------------------------------------------------------------------------------------------ the values now

        /// <summary>The default values of a store (the strict profile shows a value even when the table has no row yet).</summary>
        private static Dictionary<string, string> Defaults(SafetyStore store)
        {
            string N(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            switch (store)
            {
                case SafetyStore.ProjectControl: return PcSettingKeys.Defaults().ToDictionary(d => d.Key, d => N(d.Value));
                case SafetyStore.ProductBank: return PbSettingKeys.Defaults().ToDictionary(d => d.Key, d => d.Value);
                case SafetyStore.Procurement: return Procurement.ProcurementSettingKeys.Defaults().ToDictionary(d => d.Key, d => N(d.Value));
                case SafetyStore.Finance: return FinanceSeed.SettingSeeds().ToDictionary(d => d.Key, d => d.Value);
                case SafetyStore.Quality: return QualitySettingKeys.Defaults().ToDictionary(d => d.Key, d => N(d.Value));
                default: return new Dictionary<string, string>();
            }
        }

        /// <summary>Key -> value of what is stored (the last row of a key wins), for one store.</summary>
        private async Task<Dictionary<string, string>> StoredAsync(SafetyStore store, CancellationToken ct)
        {
            string N(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            switch (store)
            {
                case SafetyStore.ProjectControl: foreach (var r in (await _uow.Set<PcSetting>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct))) result[r.Key] = N(r.Value); break;
                case SafetyStore.ProductBank: foreach (var r in (await _uow.Set<PbSetting>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct))) if (r.Value != null) result[r.Key] = r.Value; break;
                case SafetyStore.Procurement: foreach (var r in (await _uow.Set<ProcurementSetting>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct))) result[r.Key] = N(r.Value); break;
                case SafetyStore.Finance: foreach (var r in (await _uow.Set<FnSetting>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct))) if (r.Value != null) result[r.Key] = r.Value; break;
                case SafetyStore.Quality: foreach (var r in (await _uow.Set<QualitySetting>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct))) result[r.Key] = N(r.Value); break;
                case SafetyStore.AiSeat: foreach (var r in (await _uow.Set<AiSeat>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct))) result[r.Code] = r.Enabled ? "1" : "0"; break;
            }
            return result;
        }

        private async Task<List<SafetyItemDto>> BuildItemsAsync(CancellationToken ct)
        {
            var items = new List<SafetyItemDto>();
            List<SafetySettingSpec> specs = SafetyProfile.All().ToList();
            var pending = await _uow.Set<SafetySettingChange>().AsNoTracking().Where(c => c.Status == SafetyChangeStatus.Pending).ToListAsync(ct);
            foreach (SafetyStore store in specs.Select(s => s.Store).Distinct())
            {
                Dictionary<string, string> defaults = Defaults(store);
                Dictionary<string, string> stored = await StoredAsync(store, ct);
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (SafetySettingSpec spec in specs.Where(s => s.Store == store))
                {
                    IEnumerable<string> matched = spec.Key.Contains('*')
                        ? stored.Keys.Concat(defaults.Keys).Where(k => SafetyProfile.Find(store, k) == spec)
                        : new[] { spec.Key };
                    foreach (string key in matched.OrderBy(k => k, StringComparer.Ordinal))
                    {
                        if (!keys.Add(key)) continue;
                        string current = stored.TryGetValue(key, out string sv) ? sv : defaults.TryGetValue(key, out string dv) ? dv : null;
                        if (current == null && !spec.Key.Contains('*') && !defaults.ContainsKey(key) && !stored.ContainsKey(key)) continue;   // a registered key this installation does not have
                        bool deviates = current != null && !SafetyProfile.SameValue(current, spec.Strict);
                        items.Add(new SafetyItemDto
                        {
                            Store = StoreName(store), Key = key, Title = spec.Key.Contains('*') && store == SafetyStore.AiSeat ? "صندلی هوش مصنوعی " + key + ": فعال بماند" : spec.Title, Strict = spec.Strict, Current = current,
                            Deviates = deviates, Weaker = deviates && SafetyProfile.IsWeaker(spec, current), Note = spec.Note,
                            PendingRequestId = pending.Where(p => p.Store == store && string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Select(p => (int?)p.Id).FirstOrDefault(),
                        });
                    }
                }
            }
            return items.OrderBy(i => i.Store, StringComparer.Ordinal).ThenBy(i => i.Key, StringComparer.Ordinal).ToList();
        }


        public async Task<ResponseDto<List<SafetyItemDto>>> GetProfileAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(IntegrityRoles.Readers + ",PLANNING,PROCUREMENT")) return ResponseDtoHelper.Forbidden<List<SafetyItemDto>>(NotAllowed);
            return ResponseDtoHelper.Ok(await BuildItemsAsync(ct));
        }

        public async Task<ResponseDto<SafetyDeviationsDto>> GetDeviationsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(IntegrityRoles.Readers + ",AUDITOR")) return ResponseDtoHelper.Forbidden<SafetyDeviationsDto>(NotAllowed);
            List<SafetyItemDto> all = await BuildItemsAsync(ct);
            List<SafetyItemDto> deviating = all.Where(i => i.Deviates).ToList();
            return ResponseDtoHelper.Ok(new SafetyDeviationsDto { AsOf = DateTimeOffset.Now, Total = all.Count, Deviations = deviating.Count, Weaker = deviating.Count(i => i.Weaker), Items = deviating });
        }

        // ------------------------------------------------------------------------------------------ request, decide

        private async Task<bool> IsAiSeatUserAsync(int userId, CancellationToken ct)
        {
            string name = await _uow.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);
            return AiSeatNames.IsSeatUser(name);
        }

        public async Task<ResponseDto<SafetyChangeDto>> RequestAsync(SafetyRequestDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Key) || string.IsNullOrWhiteSpace(dto.Store) || !Enum.TryParse(dto.Store.Trim(), true, out SafetyStore store)) return Bad<SafetyChangeDto>("تنظیم نامشخص است.");
            if (!_user.IsInRole(RequestRoles(store))) return ResponseDtoHelper.Forbidden<SafetyChangeDto>(NotAllowed);
            if (await IsAiSeatUserAsync(_user.UserId, ct)) return ResponseDtoHelper.Forbidden<SafetyChangeDto>(NotAllowed);
            string key = dto.Key.Trim();
            SafetySettingSpec spec = SafetyProfile.Find(store, key);
            if (spec == null) return Bad<SafetyChangeDto>("این تنظیم در پروفایل سخت‌گیرانه نیست و مستقیم از صفحه خودش تغییر می‌کند.");
            string reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
            if (reason == null || reason.Length < 5) return Bad<SafetyChangeDto>("دلیل تغییر را بنویسید.");
            if (reason.Length > 1000) reason = reason.Substring(0, 1000);
            string value = SafetyProfile.Normalize(spec, dto.Value, out string error);
            if (value == null) return Bad<SafetyChangeDto>(error);

            Dictionary<string, string> stored = await StoredAsync(store, ct);
            string current = stored.TryGetValue(key, out string sv) ? sv : Defaults(store).TryGetValue(key, out string dv) ? dv : null;
            if (current == null) return ResponseDtoHelper.NotFound<SafetyChangeDto>("تنظیم پیدا نشد.");
            if (SafetyProfile.SameValue(current, value)) return Bad<SafetyChangeDto>("مقدار تازه با مقدار فعلی یکی است.");
            if (await _uow.Set<SafetySettingChange>().AnyAsync(c => c.Store == store && c.Key == key && c.Status == SafetyChangeStatus.Pending, ct))
                return Bad<SafetyChangeDto>("برای این تنظیم درخواست در انتظار تایید هست؛ ابتدا آن را تایید، رد یا لغو کنید.");

            var row = new SafetySettingChange
            {
                Store = store, Key = key, OldValue = current, NewValue = value, Reason = reason, RequestedByUserId = _user.UserId, RequestedAt = DateTimeOffset.Now, Status = SafetyChangeStatus.Pending,
                Weakening = SafetyProfile.IsWeaker(spec, value) && !SafetyProfile.IsWeaker(spec, current) || (SafetyProfile.IsWeaker(spec, value) && SafetyProfile.IsWeaker(spec, current) && WorseThan(spec, value, current)),
            };
            _uow.Set<SafetySettingChange>().Add(row);
            await _uow.SaveChangesAsync(ct);
            await _notify.NotifyAsync(new NotifyRequest
            {
                SourceKey = "safety:" + row.Id, Module = "integrity", SourceType = "SafetySettingChange", SourceId = row.Id, Link = "/integrity/safety",
                Title = "تایید تغییر تنظیم امنیتی «" + (spec.Key.Contains('*') ? key : spec.Title) + "» از " + current + " به " + value + " (دلیل: " + (reason.Length > 120 ? reason.Substring(0, 120) : reason) + ")",
                Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Approval, Priority = Entities.Model.NasbNiro.Office.OfUrgency.Urgent, Roles = Approvers,
            }, ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(row, ct));
        }

        private static bool WorseThan(SafetySettingSpec spec, string value, string than)
        {
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal a) || !decimal.TryParse(than, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal b)) return false;
            return spec.Kind == SafetyKind.NumberAtMost || spec.Kind == SafetyKind.FlagMustBeOff ? a > b : a < b;
        }

        public async Task<ResponseDto<SafetyChangeDto>> DecideAsync(SafetyDecisionDto dto, CancellationToken ct)
        {
            if (dto == null || dto.Id <= 0) return Bad<SafetyChangeDto>("درخواست نامشخص است.");
            if (!_user.IsInRole(Approvers)) return ResponseDtoHelper.Forbidden<SafetyChangeDto>(NotAllowed + " (تایید با PMO یا مدیرعامل است)");
            SafetySettingChange row = await _uow.Set<SafetySettingChange>().FirstOrDefaultAsync(c => c.Id == dto.Id, ct);
            if (row == null) return ResponseDtoHelper.NotFound<SafetyChangeDto>("درخواست پیدا نشد.");
            if (row.Status != SafetyChangeStatus.Pending) return Bad<SafetyChangeDto>("درخواست قبلاً تعیین تکلیف شده است.");
            if (row.RequestedByUserId == _user.UserId) return Bad<SafetyChangeDto>(SameUser);
            if (await IsAiSeatUserAsync(_user.UserId, ct)) return ResponseDtoHelper.Forbidden<SafetyChangeDto>(NotAllowed + " (هوش مصنوعی تایید نمی‌کند)");
            string note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            if (!dto.Approve && note == null) return Bad<SafetyChangeDto>("برای رد درخواست دلیل را بنویسید.");
            if (note != null && note.Length > 1000) note = note.Substring(0, 1000);

            row.DecidedByUserId = _user.UserId;
            row.DecidedAt = DateTimeOffset.Now;
            row.DecisionNote = note;
            if (dto.Approve)
            {
                SafetySettingSpec spec = SafetyProfile.Find(row.Store, row.Key);
                string value = spec == null ? null : SafetyProfile.Normalize(spec, row.NewValue, out string _);
                if (value == null || !await ApplyAsync(row.Store, row.Key, value, ct)) return Bad<SafetyChangeDto>("تنظیم دیگر وجود ندارد یا مقدار آن معتبر نیست؛ درخواست اعمال نشد.");
                row.Status = SafetyChangeStatus.Approved;
                row.AppliedAt = DateTimeOffset.Now;
            }
            else row.Status = SafetyChangeStatus.Rejected;
            await _uow.SaveChangesAsync(ct);
            await _notify.CloseAsync("safety:" + row.Id, dto.Approve ? "تایید شد" : "رد شد", ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(row, ct));
        }

        public async Task<ResponseDto<SafetyChangeDto>> CancelAsync(int id, CancellationToken ct)
        {
            SafetySettingChange row = await _uow.Set<SafetySettingChange>().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (row == null) return ResponseDtoHelper.NotFound<SafetyChangeDto>("درخواست پیدا نشد.");
            if (row.RequestedByUserId != _user.UserId) return ResponseDtoHelper.Forbidden<SafetyChangeDto>(NotAllowed + " (فقط درخواست‌دهنده آن را لغو می‌کند)");
            if (row.Status != SafetyChangeStatus.Pending) return Bad<SafetyChangeDto>("درخواست قبلاً تعیین تکلیف شده است.");
            row.Status = SafetyChangeStatus.Cancelled;
            row.DecidedByUserId = _user.UserId;
            row.DecidedAt = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            await _notify.CloseAsync("safety:" + row.Id, "لغو شد", ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(row, ct));
        }

        /// <summary>Writes the approved value into the table of its store (the same rows the save endpoints of the modules write).</summary>
        private async Task<bool> ApplyAsync(SafetyStore store, string key, string value, CancellationToken ct)
        {
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number);
            switch (store)
            {
                case SafetyStore.ProjectControl:
                {
                    PcSetting row = await _uow.Set<PcSetting>().Where(r => r.Key == key).OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
                    if (row == null)
                    {
                        PcSetting def = PcSettingKeys.Defaults().FirstOrDefault(d => d.Key == key);
                        if (def == null) return false;
                        row = new PcSetting { Key = def.Key, Title = def.Title, Source = def.Source };
                        _uow.Set<PcSetting>().Add(row);
                    }
                    row.Value = number;
                    return true;
                }
                case SafetyStore.ProductBank:
                {
                    PbSetting row = await _uow.Set<PbSetting>().FirstOrDefaultAsync(r => r.Key == key, ct);
                    if (row == null)
                    {
                        PbSetting def = PbSettingKeys.Defaults().FirstOrDefault(d => d.Key == key);
                        if (def == null) return false;
                        row = new PbSetting { Key = def.Key, Title = def.Title, Source = def.Source };
                        _uow.Set<PbSetting>().Add(row);
                    }
                    row.Value = value;
                    return true;
                }
                case SafetyStore.Procurement:
                {
                    await Procurement.ProcurementSettingKeys.LoadAsync(_uow, ct);   // makes the default rows
                    ProcurementSetting row = await _uow.Set<ProcurementSetting>().FirstOrDefaultAsync(r => r.Key == key, ct);
                    if (row == null) return false;
                    row.Value = number;
                    return true;
                }
                case SafetyStore.Finance:
                {
                    await FinanceSeed.EnsureAsync(_uow, ct);
                    await FinanceSeed.EnsureSettingsAsync(_uow, ct);
                    FnSetting row = await _uow.Set<FnSetting>().FirstOrDefaultAsync(r => r.Key == key, ct);
                    if (row == null) return false;
                    row.Value = value;
                    return true;
                }
                case SafetyStore.Quality:
                {
                    QualitySetting row = await _uow.Set<QualitySetting>().FirstOrDefaultAsync(r => r.Key == key, ct);
                    if (row == null)
                    {
                        QualitySetting def = QualitySettingKeys.Defaults().FirstOrDefault(d => d.Key == key);
                        if (def == null) return false;
                        row = new QualitySetting { Key = def.Key, Title = def.Title };
                        _uow.Set<QualitySetting>().Add(row);
                    }
                    row.Value = number; row.Source = "تعیین‌شده با تایید نفر دوم";
                    return true;
                }
                case SafetyStore.AiSeat:
                {
                    AiSeat seat = await _uow.Set<AiSeat>().FirstOrDefaultAsync(r => r.Code == key, ct);
                    if (seat == null) return false;
                    seat.Enabled = number >= 1;
                    return true;
                }
                default: return false;
            }
        }

        // ------------------------------------------------------------------------------------------ reading requests

        public async Task<ResponseDto<PagedList<SafetyChangeDto>>> GetChangesAsync(SafetyChangeQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(IntegrityRoles.Readers + ",PLANNING,PROCUREMENT")) return ResponseDtoHelper.Forbidden<PagedList<SafetyChangeDto>>(NotAllowed);
            query ??= new SafetyChangeQuery();
            IQueryable<SafetySettingChange> q = _uow.Set<SafetySettingChange>().AsNoTracking();
            if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse(query.Status.Trim(), true, out SafetyChangeStatus st)) q = q.Where(c => c.Status == st);
            int size = query.PageSize <= 0 ? 25 : Math.Min(query.PageSize, 200);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            int total = await q.CountAsync(ct);
            List<SafetySettingChange> rows = await q.OrderByDescending(c => c.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.RequestedByUserId, r.DecidedByUserId }), ct);
            return ResponseDtoHelper.Ok<PagedList<SafetyChangeDto>>(new IntegrityPage<SafetyChangeDto>(rows.Select(r => Map(r, names)).ToList(), total, number, size));
        }

        private async Task<SafetyChangeDto> ToDtoAsync(SafetySettingChange r, CancellationToken ct) =>
            Map(r, await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.RequestedByUserId, r.DecidedByUserId }, ct));

        private static SafetyChangeDto Map(SafetySettingChange r, Dictionary<int, string> names)
        {
            SafetySettingSpec spec = SafetyProfile.Find(r.Store, r.Key);
            return new SafetyChangeDto
            {
                Id = r.Id, Store = r.Store.ToString(), Key = r.Key, Title = spec == null || spec.Key.Contains('*') ? r.Key : spec.Title, OldValue = r.OldValue, NewValue = r.NewValue, Weakening = r.Weakening, Reason = r.Reason,
                RequestedByUserId = r.RequestedByUserId, RequestedByName = names.Name(r.RequestedByUserId), RequestedAtText = AuditIntegrityService.StampText(r.RequestedAt),
                Status = r.Status.ToString(), StatusText = r.Status == SafetyChangeStatus.Pending ? "در انتظار تایید نفر دوم" : r.Status == SafetyChangeStatus.Approved ? "تایید و اعمال شد" : r.Status == SafetyChangeStatus.Rejected ? "رد شد" : "لغو شد",
                DecidedByUserId = r.DecidedByUserId, DecidedByName = r.DecidedByUserId.HasValue ? names.Name(r.DecidedByUserId.Value) : null, DecidedAtText = r.DecidedAt.HasValue ? AuditIntegrityService.StampText(r.DecidedAt.Value) : null,
                DecisionNote = r.DecisionNote, AppliedAtText = r.AppliedAt.HasValue ? AuditIntegrityService.StampText(r.AppliedAt.Value) : null,
            };
        }
    }
}
