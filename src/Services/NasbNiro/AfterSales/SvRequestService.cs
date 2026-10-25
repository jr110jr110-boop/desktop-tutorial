using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Quality;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Quality;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ServiceDtos;

namespace Services.NasbNiro.AfterSales
{
    public interface ISvRequestService
    {
        Task<ResponseDto<List<SvSettingDto>>> GetSettingsAsync(CancellationToken ct);
        Task<ResponseDto<SvSettingDto>> SaveSettingAsync(SvSaveSettingDto dto, CancellationToken ct);
        Task<ResponseDto<List<SvSlaRuleDto>>> GetSlaRulesAsync(CancellationToken ct);
        Task<ResponseDto<SvSlaRuleDto>> SaveSlaRuleAsync(SvSaveSlaRuleDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<SvRequestDto>>> GetRequestsAsync(SvRequestQuery query, CancellationToken ct);
        Task<ResponseDto<SvRequestDto>> GetRequestAsync(int id, DateTime? asOf, CancellationToken ct);
        Task<ResponseDto<SvRequestDto>> SaveRequestAsync(SvSaveRequestDto dto, CancellationToken ct);
        Task<ResponseDto<SvRequestDto>> RequestActionAsync(SvRequestActionDto dto, CancellationToken ct);
    }

    /// <summary>Service requests (tickets) with the SLA table, and the settings of the module.</summary>
    public class SvRequestService : ISvRequestService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public SvRequestService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => SlText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => SlText.Bad<T>(m);

        // ================================================================================== settings and SLA

        public async Task<ResponseDto<List<SvSettingDto>>> GetSettingsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<List<SvSettingDto>>(SlText.NotAllowed);
            Dictionary<string, string> saved = await _uow.Set<SvSetting>().AsNoTracking().Where(s => !s.Deleted).ToDictionaryAsync(s => s.Key, s => s.Text, ct);
            string Effective(string key) => saved.TryGetValue(key, out string v) && Clean(v) != null ? v : key switch
            {
                SvSettingKeys.CostCenterCode => SvSettingKeys.DefaultCostCenter,
                SvSettingKeys.SurveyLeadDays => SvSettingKeys.DefaultSurveyLeadDays.ToString(),
                SvSettingKeys.VisitLeadDays => SvSettingKeys.DefaultVisitLeadDays.ToString(),
                _ => null,
            };
            return ResponseDtoHelper.Ok(SvSettingKeys.All.Select(k => new SvSettingDto { Key = k.Key, Title = k.Title, Text = saved.GetValueOrDefault(k.Key), Effective = Effective(k.Key) }).ToList());
        }

        public async Task<ResponseDto<SvSettingDto>> SaveSettingAsync(SvSaveSettingDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.SettingSetters)) return Bad<SvSettingDto>("این مقدار را فقط مدیرعامل تعیین می‌کند.");
            if (dto == null || Clean(dto.Key) == null) return Bad<SvSettingDto>(SlText.InvalidData);
            var def = SvSettingKeys.All.FirstOrDefault(k => k.Key == dto.Key);
            if (def.Key == null) return Bad<SvSettingDto>("کلید تنظیم نامعتبر است.");
            string text = Clean(dto.Text);
            string textTooLong = SvLimits.TooLong(text, SvLimits.Name, "مقدار تنظیم");
            if (textTooLong != null) return Bad<SvSettingDto>(textTooLong);
            if ((dto.Key == SvSettingKeys.SurveyLeadDays || dto.Key == SvSettingKeys.VisitLeadDays) && (text == null || !int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) || n < 0))
                return Bad<SvSettingDto>("تعداد روز باید عدد صحیح غیرمنفی باشد.");
            if (dto.Key == SvSettingKeys.LabourWorkCenterCode && text != null && !await _uow.Set<Entities.Model.NasbNiro.Production.MfWorkCenter>().AnyAsync(w => w.Code == text && !w.Deleted, ct))
                return Bad<SvSettingDto>("مرکز کاری با این کد پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            SvSetting row = await _uow.Set<SvSetting>().FirstOrDefaultAsync(s => s.Key == dto.Key && !s.Deleted, ct);
            if (row == null)
            {
                row = new SvSetting { Key = dto.Key, CreateDate = now, CreatorUserId = Me };
                _uow.Set<SvSetting>().Add(row);
            }
            row.Text = text; row.Title = def.Title; row.ModifyDate = now; row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new SvSettingDto { Key = row.Key, Title = row.Title, Text = row.Text, Effective = row.Text });
        }

        private static SvSlaRuleDto ToDto(SvSlaRule r) => new SvSlaRuleDto
        {
            Id = r.Id, Priority = r.Priority, PriorityText = SvText.Priority(r.Priority), ResponseHours = r.ResponseHours, ResolutionHours = r.ResolutionHours, IsActive = r.IsActive,
        };

        public async Task<ResponseDto<List<SvSlaRuleDto>>> GetSlaRulesAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<List<SvSlaRuleDto>>(SlText.NotAllowed);
            List<SvSlaRule> rows = await _uow.Set<SvSlaRule>().AsNoTracking().Where(r => !r.Deleted).OrderBy(r => r.Priority).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(ToDto).ToList());
        }

        public async Task<ResponseDto<SvSlaRuleDto>> SaveSlaRuleAsync(SvSaveSlaRuleDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole("SERVICE,CEO")) return Bad<SvSlaRuleDto>(SlText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(SvPriority), dto.Priority)) return Bad<SvSlaRuleDto>(SlText.InvalidData);
            if (dto.ResponseHours <= 0) return Bad<SvSlaRuleDto>("ساعت‌های پاسخ باید بیشتر از صفر باشد.");
            if (dto.ResolutionHours.HasValue && dto.ResolutionHours.Value < dto.ResponseHours) return Bad<SvSlaRuleDto>("ساعت‌های رفع نمی‌تواند از ساعت‌های پاسخ کمتر باشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            SvSlaRule row = await _uow.Set<SvSlaRule>().FirstOrDefaultAsync(r => r.Priority == dto.Priority && !r.Deleted, ct);
            if (row == null)
            {
                row = new SvSlaRule { Priority = dto.Priority, CreateDate = now, CreatorUserId = Me };
                _uow.Set<SvSlaRule>().Add(row);
            }
            row.ResponseHours = dto.ResponseHours; row.ResolutionHours = dto.ResolutionHours; row.IsActive = dto.IsActive ?? true; row.ModifyDate = now; row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(row));
        }

        // ================================================================================== requests

        private async Task<List<SvRequestDto>> DtosAsync(List<SvServiceRequest> rows, DateTime asOf, CancellationToken ct)
        {
            List<int> customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
            List<int> unitIds = rows.Where(r => r.UnitId.HasValue).Select(r => r.UnitId.Value).Distinct().ToList();
            List<int> ids = rows.Select(r => r.Id).ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            Dictionary<int, string> units = await _uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Tag, ct);
            // the complaint points to its request (one complaint per request); the request finds it by that key
            Dictionary<int, (int Id, string Number)> complaints = (await _uow.Set<CustomerComplaint>().AsNoTracking().Where(c => c.ServiceRequestId != null && ids.Contains(c.ServiceRequestId.Value))
                .Select(c => new { RequestId = c.ServiceRequestId.Value, c.Id, c.Number }).ToListAsync(ct)).ToDictionary(c => c.RequestId, c => (c.Id, c.Number));
            Dictionary<int, int> orderCounts = (await _uow.Set<SvServiceOrder>().AsNoTracking().Where(o => !o.Deleted && o.RequestId != null && ids.Contains(o.RequestId.Value)).GroupBy(o => o.RequestId.Value)
                .Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.N);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => r.AssignedToUserId), ct);
            return rows.Select(r =>
            {
                bool respB = SvSla.Breached(r.ResponseDue, r.FirstResponseAt, asOf);
                bool resB = SvSla.Breached(r.ResolutionDue, r.ResolvedAt, asOf);
                bool done = r.Status == SvRequestStatus.Closed || r.Status == SvRequestStatus.Resolved; // a resolved request no longer runs against the clock; its late resolution stays visible in ResolutionBreached
                return new SvRequestDto
                {
                    Id = r.Id, Number = r.Number, CustomerId = r.CustomerId, CustomerName = customers.GetValueOrDefault(r.CustomerId), ContactId = r.ContactId, UnitId = r.UnitId,
                    UnitTag = r.UnitId.HasValue ? units.GetValueOrDefault(r.UnitId.Value) : null, Channel = r.Channel, ReceivedAt = r.ReceivedAt, ReceivedAtText = SlText.Date(r.ReceivedAt) + " " + r.ReceivedAt.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    Description = r.Description, Type = r.Type, TypeText = SvText.RequestType(r.Type), Priority = r.Priority, PriorityText = SvText.Priority(r.Priority), ProductNonconformity = r.ProductNonconformity,
                    Status = r.Status, StatusText = SvText.RequestStatus(r.Status),
                    ResponseDue = r.ResponseDue, ResponseDueText = SvText.Stamp(r.ResponseDue), ResolutionDue = r.ResolutionDue, ResolutionDueText = SvText.Stamp(r.ResolutionDue), SlaMissing = !r.ResponseDue.HasValue,
                    AssignedToUserId = r.AssignedToUserId, AssignedToName = names.Name(r.AssignedToUserId), FirstResponseAt = r.FirstResponseAt, FirstResponseAtText = SvText.Stamp(r.FirstResponseAt),
                    ResolvedAt = r.ResolvedAt, ResolvedAtText = SvText.Stamp(r.ResolvedAt), Resolution = r.Resolution,
                    ResponseBreached = respB, ResolutionBreached = resB, Breached = !done && (respB || resB),
                    ComplaintId = complaints.TryGetValue(r.Id, out var cp) ? cp.Id : (int?)null, ComplaintNumber = complaints.TryGetValue(r.Id, out var cn) ? cn.Number : null,
                    OrderCount = orderCounts.GetValueOrDefault(r.Id), RowVersion = SlConcurrency.Text(r.RowVersion),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SvRequestDto>>> GetRequestsAsync(SvRequestQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<PagedList<SvRequestDto>>(SlText.NotAllowed);
            query ??= new SvRequestQuery();
            DateTime asOf = (query.AsOf ?? DateTime.Now);
            IQueryable<SvServiceRequest> q = _uow.Set<SvServiceRequest>().AsNoTracking().Where(r => !r.Deleted);
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            if (query.Priority.HasValue) q = q.Where(r => r.Priority == query.Priority.Value);
            if (query.CustomerId.HasValue) q = q.Where(r => r.CustomerId == query.CustomerId.Value);
            if (query.UnitId.HasValue) q = q.Where(r => r.UnitId == query.UnitId.Value);
            if (query.OpenOnly == true) q = q.Where(r => r.Status != SvRequestStatus.Closed);
            if (query.Breached == true)
                q = q.Where(r => r.Status != SvRequestStatus.Closed && r.Status != SvRequestStatus.Resolved && ((r.ResponseDue != null && (r.FirstResponseAt ?? asOf) > r.ResponseDue) || (r.ResolutionDue != null && (r.ResolvedAt ?? asOf) > r.ResolutionDue)));
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(r => r.Number.Contains(text) || r.Description.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(r => r.Id), query.PageNumber, query.PageSize, rows => DtosAsync(rows, asOf, ct), ct));
        }

        public async Task<ResponseDto<SvRequestDto>> GetRequestAsync(int id, DateTime? asOf, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<SvRequestDto>(SlText.NotAllowed);
            SvServiceRequest row = await _uow.Set<SvServiceRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && !r.Deleted, ct);
            if (row == null) return SlText.NotFound<SvRequestDto>("درخواست خدمات پیدا نشد.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<SvServiceRequest> { row }, asOf ?? DateTime.Now, ct))[0]);
        }

        public async Task<ResponseDto<SvRequestDto>> SaveRequestAsync(SvSaveRequestDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Writers)) return Bad<SvRequestDto>(SlText.NotAllowed);
            if (dto == null || Clean(dto.Description) == null) return Bad<SvRequestDto>("شرح درخواست را وارد کنید.");
            string tooLong = SvLimits.First((dto.Description, SvLimits.LongText, "شرح درخواست"), (dto.Channel, SvLimits.Name, "کانال دریافت"));
            if (tooLong != null) return Bad<SvRequestDto>(tooLong);
            if (!Enum.IsDefined(typeof(SvRequestType), dto.Type) || !Enum.IsDefined(typeof(SvPriority), dto.Priority)) return Bad<SvRequestDto>(SlText.InvalidData);
            if (!await _uow.Set<FnCustomer>().AnyAsync(c => c.Id == dto.CustomerId, ct)) return Bad<SvRequestDto>("مشتری پیدا نشد.");
            if (dto.ContactId.HasValue && !await _uow.Set<SlContact>().AnyAsync(c => c.Id == dto.ContactId.Value && c.CustomerId == dto.CustomerId && !c.Deleted, ct)) return Bad<SvRequestDto>("مخاطب پیدا نشد یا مال این مشتری نیست.");
            if (dto.UnitId.HasValue)
            {
                int? unitCustomer = await _uow.Set<SvInstalledUnit>().Where(u => u.Id == dto.UnitId.Value && !u.Deleted).Select(u => u.CustomerId).FirstOrDefaultAsync(ct);
                if (!await _uow.Set<SvInstalledUnit>().AnyAsync(u => u.Id == dto.UnitId.Value && !u.Deleted, ct)) return Bad<SvRequestDto>("واحد نصب‌شده پیدا نشد.");
                if (unitCustomer.HasValue && unitCustomer.Value != dto.CustomerId) return Bad<SvRequestDto>("این واحد مال مشتری انتخاب‌شده نیست.");
            }
            DateTimeOffset now = DateTimeOffset.Now;
            SvServiceRequest row;
            DateTime received;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SvServiceRequest>().FirstOrDefaultAsync(r => r.Id == dto.Id.Value && !r.Deleted, ct);
                if (row == null) return SlText.NotFound<SvRequestDto>("درخواست خدمات پیدا نشد.");
                if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SvRequestDto>(SlText.InvalidData);
                if (row.Status != SvRequestStatus.Open) return Bad<SvRequestDto>("درخواستی که واگذار شده ویرایش نمی‌شود.");
                // an edit without a received time keeps the original one (the SLA clock does not restart)
                received = dto.ReceivedAt ?? row.ReceivedAt;
            }
            else
            {
                received = dto.ReceivedAt ?? DateTime.Now;
                row = new SvServiceRequest { Number = await InventoryNumbers.NextNumberAsync(_uow, SvNumbers.Request, "SVR", received, ct), Status = SvRequestStatus.Open, CreateDate = now, CreatorUserId = Me };
                _uow.Set<SvServiceRequest>().Add(row);
            }
            row.CustomerId = dto.CustomerId; row.ContactId = dto.ContactId; row.UnitId = dto.UnitId; row.Channel = Clean(dto.Channel); row.ReceivedAt = received; row.Description = Clean(dto.Description);
            row.Type = dto.Type; row.Priority = dto.Priority; row.ProductNonconformity = dto.ProductNonconformity;
            SvSlaRule sla = await _uow.Set<SvSlaRule>().AsNoTracking().FirstOrDefaultAsync(s => s.Priority == dto.Priority && s.IsActive && !s.Deleted, ct);
            row.ResponseDue = SvSla.Due(received, sla?.ResponseHours);
            row.ResolutionDue = SvSla.Due(received, sla?.ResolutionHours);
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            row.ModifyDate = now; row.ModifierUserId = Me;

            // a complaint or a product nonconformity opens the quality complaint, linked both ways
            // (a request that has one is never given a second: the lookup is by the complaint's key to the request)
            bool needsComplaint = (dto.Type == SvRequestType.Complaint || dto.ProductNonconformity) && (!dto.Id.HasValue || !await _uow.Set<CustomerComplaint>().AnyAsync(c => c.ServiceRequestId == row.Id, ct));
            CustomerComplaint complaint = null;
            if (needsComplaint)
            {
                Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, ct);
                int? contractId = dto.UnitId.HasValue ? await _uow.Set<SvInstalledUnit>().Where(u => u.Id == dto.UnitId.Value).Select(u => u.ContractId).FirstOrDefaultAsync(ct) : null;
                int? workOrderId = dto.UnitId.HasValue ? await _uow.Set<SvInstalledUnit>().Where(u => u.Id == dto.UnitId.Value).Select(u => u.WorkOrderId).FirstOrDefaultAsync(ct) : null;
                complaint = new CustomerComplaint
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.Complaint, "CC", received, ct), Status = ComplaintStatus.Received, CreateDate = now, CreatorUserId = Me,
                    ContractId = contractId, WorkOrderId = workOrderId, ReceivedDate = received, Channel = row.Channel, Description = SvLimits.Cut($"[{row.Number}] {row.Description}", SvLimits.LongText),
                    Severity = Entities.Model.NasbNiro.Inventory.NcrSeverity.NotClassified, ResponseDue = received.AddDays((int)settings[QualitySettingKeys.ComplaintResponseDays]), ModifyDate = now, ModifierUserId = Me,
                    ServiceRequest = row,
                };
                _uow.Set<CustomerComplaint>().Add(complaint);
            }
            // one save: the complaint points to the request through its navigation, so the request and its complaint are written together
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvRequestDto>(SlText.Concurrency); }
            await SvGates.AssignAsync(_notify, row, ct);
            return await GetRequestAsync(row.Id, null, ct);
        }

        public async Task<ResponseDto<SvRequestDto>> RequestActionAsync(SvRequestActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Writers)) return Bad<SvRequestDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SvRequestDto>(SlText.InvalidData);
            SvServiceRequest row = await _uow.Set<SvServiceRequest>().FirstOrDefaultAsync(r => r.Id == dto.Id && !r.Deleted, ct);
            if (row == null) return SlText.NotFound<SvRequestDto>("درخواست خدمات پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SvRequestDto>(SlText.InvalidData);
            DateTime at = dto.At ?? DateTime.Now;
            DateTimeOffset now = DateTimeOffset.Now;
            if (row.Status == SvRequestStatus.Closed) return Bad<SvRequestDto>("درخواست بسته شده است.");
            switch ((dto.Action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "assign":
                    if (!dto.UserId.HasValue || dto.UserId.Value <= 0) return Bad<SvRequestDto>("کاربر مسئول را مشخص کنید.");
                    if (!await _uow.Set<Entities.Identity.User>().AnyAsync(u => u.Id == dto.UserId.Value, ct)) return Bad<SvRequestDto>("کاربر پیدا نشد.");
                    if (row.Status == SvRequestStatus.Resolved) return Bad<SvRequestDto>("درخواست رفع‌شده واگذار نمی‌شود.");
                    row.AssignedToUserId = dto.UserId; row.AssignedAt = now; row.FirstResponseAt ??= at;
                    if (row.Status == SvRequestStatus.Open) row.Status = SvRequestStatus.Assigned;
                    break;
                case "progress":
                    if (row.Status == SvRequestStatus.Resolved) return Bad<SvRequestDto>("درخواست رفع‌شده است.");
                    row.FirstResponseAt ??= at; row.Status = SvRequestStatus.InProgress;
                    break;
                case "wait":
                    if (row.Status == SvRequestStatus.Resolved) return Bad<SvRequestDto>("درخواست رفع‌شده است.");
                    row.FirstResponseAt ??= at; row.Status = SvRequestStatus.WaitingCustomer;
                    break;
                case "resolve":
                    if (Clean(dto.Resolution) == null) return Bad<SvRequestDto>("شرح راه‌حل را وارد کنید.");
                    string resolutionTooLong = SvLimits.TooLong(Clean(dto.Resolution), SvLimits.LongText, "شرح راه‌حل");
                    if (resolutionTooLong != null) return Bad<SvRequestDto>(resolutionTooLong);
                    if (row.Status == SvRequestStatus.Resolved) return Bad<SvRequestDto>("درخواست قبلاً رفع شده است.");
                    if (await _uow.Set<SvServiceOrder>().AnyAsync(o => o.RequestId == row.Id && !o.Deleted && o.Status != SvOrderStatus.Closed && o.Status != SvOrderStatus.Cancelled, ct))
                        return Bad<SvRequestDto>("این درخواست دستور خدماتِ باز دارد؛ ابتدا آن را ببندید یا لغو کنید.");
                    row.FirstResponseAt ??= at; row.ResolvedAt = at; row.Resolution = Clean(dto.Resolution); row.Status = SvRequestStatus.Resolved;
                    break;
                case "close":
                    if (row.Status != SvRequestStatus.Resolved) return Bad<SvRequestDto>("فقط درخواست رفع‌شده بسته می‌شود.");
                    row.Status = SvRequestStatus.Closed; row.ClosedAt = now;
                    break;
                default:
                    return Bad<SvRequestDto>("عملیات نامعتبر است.");
            }
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvRequestDto>(SlText.Concurrency); }
            await SvGates.AssignAsync(_notify, row, ct);
            await SvGates.BreachAsync(_notify, row, row.Status != SvRequestStatus.Closed && row.Status != SvRequestStatus.Resolved && (SvSla.Breached(row.ResponseDue, row.FirstResponseAt, DateTime.Now) || SvSla.Breached(row.ResolutionDue, row.ResolvedAt, DateTime.Now)), ct);
            return await GetRequestAsync(row.Id, null, ct);
        }
    }
}
