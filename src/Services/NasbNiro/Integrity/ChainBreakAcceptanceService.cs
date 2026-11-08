using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Integrity;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.IntegrityDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Integrity
{
    /// <summary>The people of some roles (never an AI seat), for an alert that must not reach the requester.</summary>
    internal static class RoleUsers
    {
        public static async Task<List<int>> IdsAsync(IUnitOfWork uow, string roles, int? exclude, CancellationToken ct)
        {
            List<string> names = (roles ?? "").Split(',').Select(r => r.Trim().ToUpperInvariant()).Where(r => r.Length > 0).ToList();
            var rows = await (from ur in uow.Set<UserRole>().AsNoTracking()
                              join r in uow.Set<Role>().AsNoTracking() on ur.RoleId equals r.Id
                              join u in uow.Set<User>().AsNoTracking() on ur.UserId equals u.Id
                              where names.Contains(r.Name.ToUpper())
                              select new { u.Id, u.UserName }).ToListAsync(ct);
            return rows.Where(x => !AiSeatNames.IsSeatUser(x.UserName) && (!exclude.HasValue || x.Id != exclude.Value)).Select(x => x.Id).Distinct().ToList();
        }
    }

    public interface IChainBreakAcceptanceService
    {
        Task<ResponseDto<ChainBreakAcceptanceDto>> RequestAsync(ChainBreakAcceptanceRequestDto dto, CancellationToken ct);
        Task<ResponseDto<ChainBreakAcceptanceDto>> SignAsync(ChainBreakAcceptanceSignDto dto, CancellationToken ct);
        Task<ResponseDto<ChainBreakAcceptanceDto>> CancelAsync(int id, CancellationToken ct);
        Task<ResponseDto<PagedList<ChainBreakAcceptanceDto>>> GetAsync(PaginationParams query, CancellationToken ct);
    }

    /// <summary>
    /// Fix round (H2): «پذیرش شکست شناخته‌شده». A break that cannot be repaired is accepted only when the QA manager, the PMO and the CEO, three distinct persons (CEO decision D12), have each signed; an AI seat never signs.
    /// For a break of the chain the acceptance re-anchors the verification at the broken place (the walk continues after it from the seal the row carries then); for a record that differs from its trail a reconciling entry
    /// with the current values is added to the trail (sealed like any other). The status of the chain still clears only when a later FULL pass is Ok.
    /// </summary>
    public class ChainBreakAcceptanceService : IChainBreakAcceptanceService
    {
        public const string NotAllowed = "پذیرش شکست شناخته‌شده فقط با امضای مدیر کیفیت، PMO و مدیرعامل (سه نفر متفاوت) ممکن است.";
        private static readonly string[] Seats = { "QA", "PMO", "CEO" };

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public ChainBreakAcceptanceService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify)
        {
            _uow = uow;
            _user = user;
            _notify = notify;
        }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);

        private async Task<bool> IsAiAsync(int userId, CancellationToken ct)
        {
            string name = await _uow.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);
            return AiSeatNames.IsSeatUser(name);
        }

        public async Task<ResponseDto<ChainBreakAcceptanceDto>> RequestAsync(ChainBreakAcceptanceRequestDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(IntegrityRoles.AlertRoles)) return ResponseDtoHelper.Forbidden<ChainBreakAcceptanceDto>(NotAllowed);
            if (await IsAiAsync(_user.UserId, ct)) return ResponseDtoHelper.Forbidden<ChainBreakAcceptanceDto>(NotAllowed + " (هوش مصنوعی درخواست نمی‌دهد)");
            if (dto == null || dto.RunId <= 0) return Bad<ChainBreakAcceptanceDto>("بررسی شکسته نامشخص است.");
            string reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
            if (reason == null || reason.Length < 10) return Bad<ChainBreakAcceptanceDto>("دلیل پذیرش و توضیح شکست را بنویسید (دست‌کم ۱۰ نویسه).");
            if (reason.Length > 1000) reason = reason.Substring(0, 1000);
            AuditChainRun run = await _uow.Set<AuditChainRun>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.RunId, ct);
            if (run == null) return ResponseDtoHelper.NotFound<ChainBreakAcceptanceDto>("بررسی پیدا نشد.");
            if (run.Status != IntegrityRunStatus.Broken) return Bad<ChainBreakAcceptanceDto>("فقط شکستی که بررسی گزارش کرده پذیرفته می‌شود.");
            bool drift = run.BreakKind == AuditIntegrityService.RecordDriftKind;
            if (!drift && run.BreakSeq == null) return Bad<ChainBreakAcceptanceDto>("این نوع شکست (ردیف بدون مهر یا نبود کلید مهر) با پذیرش برطرف نمی‌شود؛ ردیف را مهر کنید یا کلید را برگردانید.");
            if (run.BreakKind == "Anchor" || run.BreakKind == "KeyMissing") return Bad<ChainBreakAcceptanceDto>("این نوع شکست با پذیرش برطرف نمی‌شود.");
            AuditChainRun latest = await _uow.Set<AuditChainRun>().AsNoTracking().Where(r => r.Status == IntegrityRunStatus.Broken).OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstAsync(ct);
            if (latest.Id != run.Id) return Bad<ChainBreakAcceptanceDto>("فقط آخرین شکست گزارش‌شده پذیرفته می‌شود.");
            if (await _uow.Set<ChainBreakAcceptance>().AnyAsync(a => a.Status == SafetyChangeStatus.Pending, ct)) return Bad<ChainBreakAcceptanceDto>("یک درخواست پذیرش در انتظار امضا هست؛ ابتدا آن را تعیین تکلیف کنید.");

            var row = new ChainBreakAcceptance
            {
                BreakRunId = run.Id, BreakKind = run.BreakKind, BreakSeq = run.BreakSeq, BreakRowId = run.BreakRowId, BreakEntityType = run.BreakEntityType, BreakColumn = run.BreakColumn,
                Reason = reason, RequestedByUserId = _user.UserId, RequestedAt = DateTimeOffset.Now, Status = SafetyChangeStatus.Pending,
            };
            _uow.Set<ChainBreakAcceptance>().Add(row);
            await _uow.SaveChangesAsync(ct);
            await _notify.NotifyAsync(new NotifyRequest
            {
                SourceKey = "integrity:accept:" + row.Id, Module = "integrity", SourceType = "ChainBreakAcceptance", SourceId = row.Id, Link = "/integrity",
                Title = "امضای پذیرش شکست شناخته‌شده سوابق (مدیر کیفیت، PMO و مدیرعامل، سه نفر متفاوت): " + (run.BreakKind ?? "-") + "، دلیل: " + (reason.Length > 120 ? reason.Substring(0, 120) : reason),
                Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Approval, Priority = Entities.Model.NasbNiro.Office.OfUrgency.Urgent, UserIds = await RoleUsers.IdsAsync(_uow, IntegrityRoles.AlertRoles, _user.UserId, ct),
            }, ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(row, ct));
        }

        public async Task<ResponseDto<ChainBreakAcceptanceDto>> SignAsync(ChainBreakAcceptanceSignDto dto, CancellationToken ct)
        {
            string seat = (dto?.Role ?? "").Trim().ToUpperInvariant();
            if (dto == null || dto.Id <= 0 || !Seats.Contains(seat)) return Bad<ChainBreakAcceptanceDto>("جایگاه امضا باید QA، PMO یا CEO باشد.");
            if (!_user.IsInRole(seat)) return ResponseDtoHelper.Forbidden<ChainBreakAcceptanceDto>(NotAllowed);
            if (await IsAiAsync(_user.UserId, ct)) return ResponseDtoHelper.Forbidden<ChainBreakAcceptanceDto>(NotAllowed + " (هوش مصنوعی امضا نمی‌کند)");
            ChainBreakAcceptance row = await _uow.Set<ChainBreakAcceptance>().FirstOrDefaultAsync(a => a.Id == dto.Id, ct);
            if (row == null) return ResponseDtoHelper.NotFound<ChainBreakAcceptanceDto>("درخواست پیدا نشد.");
            if (row.Status != SafetyChangeStatus.Pending) return Bad<ChainBreakAcceptanceDto>("درخواست قبلاً تعیین تکلیف شده است.");
            int me = _user.UserId;
            if (row.QaUserId == me || row.PmoUserId == me || row.CeoUserId == me) return Bad<ChainBreakAcceptanceDto>("هر نفر فقط یک امضا دارد؛ امضای دو جایگاه توسط یک نفر پذیرفته نیست (تفکیک وظایف).");
            DateTimeOffset now = DateTimeOffset.Now;
            if (seat == "QA") { if (row.QaUserId != null) return Bad<ChainBreakAcceptanceDto>("این جایگاه امضا شده است."); row.QaUserId = me; row.QaAt = now; }
            else if (seat == "PMO") { if (row.PmoUserId != null) return Bad<ChainBreakAcceptanceDto>("این جایگاه امضا شده است."); row.PmoUserId = me; row.PmoAt = now; }
            else { if (row.CeoUserId != null) return Bad<ChainBreakAcceptanceDto>("این جایگاه امضا شده است."); row.CeoUserId = me; row.CeoAt = now; }

            if (row.QaUserId != null && row.PmoUserId != null && row.CeoUserId != null)
            {
                string error = await ApplyAsync(row, ct);
                if (error != null) { _uow.Entry(row).State = EntityState.Detached; return Bad<ChainBreakAcceptanceDto>(error); }
                row.Status = SafetyChangeStatus.Approved;
                row.AppliedAt = now;
            }
            await _uow.SaveChangesAsync(ct);
            if (row.Status == SafetyChangeStatus.Approved)
            {
                await _notify.CloseAsync("integrity:accept:" + row.Id, "پذیرفته شد", ct);
                await _notify.NotifyAsync(new NotifyRequest
                {
                    SourceKey = "integrity:accepted:" + row.Id, Module = "integrity", SourceType = "ChainBreakAcceptance", SourceId = row.Id, Link = "/integrity", Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Alert,
                    Title = "شکست شناخته‌شده سوابق با امضای سه نفر پذیرفته شد؛ برای پاک شدن وضعیت یک بررسی کامل لازم است.", Priority = Entities.Model.NasbNiro.Office.OfUrgency.Normal, Roles = IntegrityRoles.AlertRoles,
                }, ct);
            }
            return ResponseDtoHelper.Ok(await ToDtoAsync(row, ct));
        }

        /// <summary>The effect of the third signature. Null when it worked, else the Persian reason it did not.</summary>
        private async Task<string> ApplyAsync(ChainBreakAcceptance row, CancellationToken ct)
        {
            if (row.BreakKind == AuditIntegrityService.RecordDriftKind)
            {
                Type type = AuditEntityLabels.TypeOf(row.BreakEntityType);
                if (type == null || row.BreakRowId == null) return "نوع رکورد شناخته نشد.";
                RecordDriftFinding f = await RecordDrift.FindInTypeAsync(_uow, type, ct);
                if (f != null && f.RowId == row.BreakRowId.Value && f.Differences.Count > 0)
                {
                    // the trail gets one more entry with the values the record has now: from here the record and the trail agree (and who accepted it is on the acceptance)
                    _uow.Set<FnAuditLog>().Add(new FnAuditLog
                    {
                        EntityType = row.BreakEntityType, EntityId = row.BreakRowId, Action = "Modified", Summary = "تطبیق سوابق با رکورد (پذیرش شکست شناخته‌شده شماره " + row.Id.ToString(CultureInfo.InvariantCulture) + ")",
                        AfterJson = JsonSerializer.Serialize(f.Differences), UserId = _user.UserId, At = DateTimeOffset.Now,
                    });
                }
                return null;
            }
            FnAuditLog at = await _uow.Set<FnAuditLog>().AsNoTracking().Where(a => a.Seq == row.BreakSeq).FirstOrDefaultAsync(ct);
            if (at == null || string.IsNullOrEmpty(at.Hash)) return "ردیف شکسته در زنجیره پیدا نشد؛ ابتدا یک بررسی کامل تازه انجام دهید.";
            row.AnchorSeq = at.Seq;
            row.AnchorHash = at.Hash;
            return null;
        }

        public async Task<ResponseDto<ChainBreakAcceptanceDto>> CancelAsync(int id, CancellationToken ct)
        {
            ChainBreakAcceptance row = await _uow.Set<ChainBreakAcceptance>().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (row == null) return ResponseDtoHelper.NotFound<ChainBreakAcceptanceDto>("درخواست پیدا نشد.");
            if (!_user.IsInRole(IntegrityRoles.AlertRoles)) return ResponseDtoHelper.Forbidden<ChainBreakAcceptanceDto>(NotAllowed);
            if (row.Status != SafetyChangeStatus.Pending) return Bad<ChainBreakAcceptanceDto>("درخواست قبلاً تعیین تکلیف شده است.");
            row.Status = SafetyChangeStatus.Cancelled;
            await _uow.SaveChangesAsync(ct);
            await _notify.CloseAsync("integrity:accept:" + row.Id, "لغو شد", ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(row, ct));
        }

        public async Task<ResponseDto<PagedList<ChainBreakAcceptanceDto>>> GetAsync(PaginationParams query, CancellationToken ct)
        {
            if (!_user.IsInRole(IntegrityRoles.Readers)) return ResponseDtoHelper.Forbidden<PagedList<ChainBreakAcceptanceDto>>(IntegrityRoles.NotAllowed);
            query ??= new PaginationParams();
            int size = query.PageSize <= 0 ? 25 : Math.Min(query.PageSize, 200);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            IQueryable<ChainBreakAcceptance> q = _uow.Set<ChainBreakAcceptance>().AsNoTracking();
            int total = await q.CountAsync(ct);
            List<ChainBreakAcceptance> rows = await q.OrderByDescending(a => a.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.RequestedByUserId, r.QaUserId, r.PmoUserId, r.CeoUserId }), ct);
            return ResponseDtoHelper.Ok<PagedList<ChainBreakAcceptanceDto>>(new IntegrityPage<ChainBreakAcceptanceDto>(rows.Select(r => Map(r, names)).ToList(), total, number, size));
        }

        private async Task<ChainBreakAcceptanceDto> ToDtoAsync(ChainBreakAcceptance r, CancellationToken ct) =>
            Map(r, await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.RequestedByUserId, r.QaUserId, r.PmoUserId, r.CeoUserId }, ct));

        private static ChainBreakAcceptanceDto Map(ChainBreakAcceptance r, Dictionary<int, string> names)
        {
            string N(int? id) => id.HasValue ? names.Name(id.Value) : null;
            string T(DateTimeOffset? at) => at.HasValue ? AuditIntegrityService.StampText(at.Value) : null;
            return new ChainBreakAcceptanceDto
            {
                Id = r.Id, BreakRunId = r.BreakRunId, BreakKind = r.BreakKind, BreakSeq = r.BreakSeq, BreakRowId = r.BreakRowId, BreakEntityType = r.BreakEntityType, BreakEntityTypeText = AuditEntityLabels.Of(r.BreakEntityType), BreakColumn = r.BreakColumn,
                Reason = r.Reason, RequestedByUserId = r.RequestedByUserId, RequestedByName = N(r.RequestedByUserId), RequestedAtText = T(r.RequestedAt),
                QaName = N(r.QaUserId), QaAtText = T(r.QaAt), PmoName = N(r.PmoUserId), PmoAtText = T(r.PmoAt), CeoName = N(r.CeoUserId), CeoAtText = T(r.CeoAt),
                Signatures = (r.QaUserId.HasValue ? 1 : 0) + (r.PmoUserId.HasValue ? 1 : 0) + (r.CeoUserId.HasValue ? 1 : 0),
                Status = r.Status.ToString(), StatusText = r.Status == SafetyChangeStatus.Pending ? "در انتظار امضا" : r.Status == SafetyChangeStatus.Approved ? "پذیرفته و اعمال شد" : "لغو شد",
                AppliedAtText = T(r.AppliedAt), AnchorSeq = r.AnchorSeq,
            };
        }
    }
}
