using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.Integrity;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.OpsData;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProductBank;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.GoLiveDtos;

namespace Services.NasbNiro.GoLive
{
    public interface IGoLiveOverviewService
    {
        Task<ResponseDto<GlOverviewDto>> OverviewAsync(CancellationToken ct);
        Task<ResponseDto<GlRevisionPreCheckDto>> RevisionPreCheckAsync(CancellationToken ct);
        Task<ResponseDto<GlCeoInboxDto>> CeoInboxAsync(CancellationToken ct);
        Task<ResponseDto<GlDriftRehearsalDto>> DriftRehearsalAsync(CancellationToken ct);
    }

    /// <summary>Package G: the go-live overview (what is legacy and what was used), the pre-check for «EdmsRequireRevision» and the CEO inbox (read only; each decision is made one by one on its own page).</summary>
    public class GoLiveOverviewService : PbServiceBase, IGoLiveOverviewService
    {
        public GoLiveOverviewService(IUnitOfWork uow, IInventoryUserContext user) : base(uow, user) { }

        public static string KindText(GlLegacyKind k) => k == GlLegacyKind.PcaNotApplicable ? "ترخیص دستور کار قدیمی بدون ممیزی پیکربندی" : k == GlLegacyKind.QcAcceptWithoutList ? "پذیرش QC در حالت قدیمی (بدون مقدار اسمی و تلرانس)" : "نمونه اول خودکار باز نشد (قطعه قدیمی)";

        private const string OverviewReaders = "CEO,PMO,PROJECTMANAGER,PLANNING,QA,QC,AUDITOR,ENGINEERING,ENGINEER,DCC,COMMERCIAL";

        public async Task<ResponseDto<GlOverviewDto>> OverviewAsync(CancellationToken ct)
        {
            if (!InRole(OverviewReaders)) return ResponseDtoHelper.BadRequest<GlOverviewDto>(GlText.NotAllowed);
            DateTimeOffset start = await CutOver.StartAsync(Uow, ct);
            var o = new GlOverviewDto { ControlStartAt = start > DateTimeOffset.MinValue ? start : (DateTimeOffset?)null, ControlStartText = CutOver.Text(start) };
            if (start > DateTimeOffset.MinValue)
            {
                o.LegacyWorkOrders = await Uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AsNoTracking().CountAsync(w => !w.Deleted && w.CreateDate < start && !Uow.Set<Entities.Model.NasbNiro.ProductBank.PbProjectBaseline>().Any(b => b.WorkOrderId == w.Id && !b.Deleted && b.IsCurrent), ct);
                o.LegacyDrawingsWithoutList = await Uow.Set<PcDocumentRevision>().AsNoTracking().CountAsync(r => !r.Deleted && (r.Status == PcDocStatus.Released || r.Status == PcDocStatus.AsBuilt) && (r.ReleasedAt == null || r.ReleasedAt < start)
                    && !Uow.Set<Entities.Model.NasbNiro.Quality.QcCharacteristicList>().Any(l => l.RevisionId == r.Id && !l.Deleted && l.Status == Entities.Model.NasbNiro.Quality.CharListStatus.Released), ct);
            }
            o.OpenCharAdoptions = await Uow.Set<GlCharAdoption>().AsNoTracking().CountAsync(a => !a.Deleted && a.Status == GlAdoptionStatus.Requested, ct);
            o.OpenConfigAdoptions = await Uow.Set<GlConfigAdoption>().AsNoTracking().CountAsync(a => !a.Deleted && (a.Status == GlAdoptionStatus.Requested || a.Status == GlAdoptionStatus.PartlyReviewed), ct);
            List<GlLegacyUse> uses = await Uow.Set<GlLegacyUse>().AsNoTracking().Where(u => !u.Deleted).OrderByDescending(u => u.Id).Take(200).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(Uow, uses.Select(u => u.UsedByUserId), ct);
            o.Uses = uses.Select(u => new GlLegacyUseDto { Id = u.Id, Kind = (int)u.Kind, KindText = KindText(u.Kind), ObjectKey = u.ObjectKey, Number = u.Number, Note = u.Note, UsedByName = names.Name(u.UsedByUserId), UsedAt = u.UsedAt }).ToList();
            return ResponseDtoHelper.Ok(o);
        }

        /// <summary>What would break if «EdmsRequireRevision» were switched to 1: client-submittal documents with no revision record (the next outgoing transmittal of them is refused). The default is not changed here.</summary>
        public async Task<ResponseDto<GlRevisionPreCheckDto>> RevisionPreCheckAsync(CancellationToken ct)
        {
            if (!InRole(OverviewReaders)) return ResponseDtoHelper.BadRequest<GlRevisionPreCheckDto>(GlText.NotAllowed);
            bool required = (await PcSettingKeys.LoadAsync(Uow, ct))[PcSettingKeys.EdmsRequireRevision] >= 1m;
            HashSet<int> withRevisions = (await Uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => !r.Deleted).Select(r => r.DeliverableId).Distinct().ToListAsync(ct)).ToHashSet();
            var docs = await Uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => !d.Deleted && d.ClientSubmittal).Select(d => new { d.Id, d.DocumentNumber, d.Title }).ToListAsync(ct);
            var rows = docs.Where(d => !withRevisions.Contains(d.Id)).Select(d => new GlRevisionPreCheckRowDto { Source = "document", RecordId = d.Id, Number = d.DocumentNumber, Title = d.Title, Problem = "سند فهرست کارفرما بازنگری ثبت‌شده ندارد؛ ارسال بعدی آن با تنظیم ۱ رد می‌شود." }).ToList();
            var dto = new GlRevisionPreCheckDto { CurrentlyRequired = required, TotalLines = docs.Count, WouldBreak = rows.Count, Rows = rows.Take(500).ToList() };
            dto.Summary = required ? "تنظیم هم‌اکنون ۱ است." : rows.Count == 0 ? "همه اسناد فهرست کارفرما بازنگری ثبت‌شده دارند؛ تنظیم بی‌خطر به ۱ تغییر می‌کند." : $"{rows.Count} سند بازنگری ثبت‌شده ندارد؛ پیش از تغییر تنظیم به ۱، بازنگری آن‌ها را ثبت کنید.";
            return ResponseDtoHelper.Ok(dto);
        }

        /// <summary>The dry run of the record-integrity baseline (M1): nothing is written. For the rehearsal on a copy of the server database before the update goes live.</summary>
        public async Task<ResponseDto<GlDriftRehearsalDto>> DriftRehearsalAsync(CancellationToken ct)
        {
            if (!InRole("CEO,PMO,QA,AUDITOR,ADMIN")) return ResponseDtoHelper.BadRequest<GlDriftRehearsalDto>(GlText.NotAllowed);
            List<DriftBaselineRow> rows = await RecordDriftBaseline.PreviewAsync(Uow, ct);
            DateTimeOffset? at = await Uow.Set<GlActivation>().AsNoTracking().Select(a => a.DriftBaselineAt).FirstOrDefaultAsync(ct);
            var dto = new GlDriftRehearsalDto { BaselineDue = await RecordDriftBaseline.IsDueAsync(Uow, ct), BaselineAt = at, TotalDiffering = rows.Sum(r => r.Differing) };
            dto.Tables = rows.Select(r => new GlDriftRehearsalRowDto { EntityType = r.EntityType, Label = r.Label, Differing = r.Differing, PresentAfterDelete = r.Present, Missing = r.Missing }).ToList();
            return ResponseDtoHelper.Ok(dto);
        }

        private static GlCeoInboxGroupDto Group(string key, string title, string route, int count, List<GlCeoInboxItemDto> items) =>
            new GlCeoInboxGroupDto { Key = key, Title = title, Route = route, Count = count, OldestDays = items.Count == 0 ? 0 : items.Max(i => i.AgeDays), Items = items.OrderByDescending(i => i.AgeDays).Take(100).ToList() };

        public async Task<ResponseDto<GlCeoInboxDto>> CeoInboxAsync(CancellationToken ct)
        {
            if (!InRole("CEO")) return ResponseDtoHelper.BadRequest<GlCeoInboxDto>(GlText.NotAllowed);
            DateTimeOffset now = DateTimeOffset.Now;
            int Age(DateTimeOffset? at) => at.HasValue ? Math.Max(0, (int)(now - at.Value).TotalDays) : 0;
            var dto = new GlCeoInboxDto();

            // each group: the count is taken first (the whole queue), then the oldest 100 are shown
            IQueryable<AusFinding> fq = Uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.Status == AusFindingStatus.PmoProposed);
            var findings = await fq.OrderBy(f => f.PmoAt).Take(100).ToListAsync(ct);
            dto.Groups.Add(Group("findings", "بستن یافته‌های ممیزی (مرحله سوم: مدیرعامل)", "/audit/findings", await fq.CountAsync(ct), findings.Select(f => new GlCeoInboxItemDto { Ref = "finding:" + f.Id, RecordId = f.Id, Title = f.Number + " — " + f.Title, Detail = f.RuleCode, Since = f.PmoAt ?? f.ModifyDate, AgeDays = Age(f.PmoAt ?? f.ModifyDate), Route = "/audit/findings/" + f.Id }).ToList()));

            IQueryable<AusFinding> vq = Uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.VoidQaUserId != null && f.VoidPmoUserId != null && f.VoidCeoUserId == null && f.VoidedAt == null);
            var voids = await vq.OrderBy(f => f.Id).Take(100).ToListAsync(ct);
            dto.Groups.Add(Group("finding-void", "ابطال یافتهٔ ممیزی منتظر امضای مدیرعامل", "/audit/findings", await vq.CountAsync(ct), voids.Select(f => new GlCeoInboxItemDto { Ref = "finding-void:" + f.Id, RecordId = f.Id, Title = f.Number + " — " + f.Title, Detail = f.VoidReason, Since = f.ModifyDate, AgeDays = Age(f.ModifyDate), Route = "/audit/findings/" + f.Id }).ToList()));

            IQueryable<AusAcceptance> aq = Uow.Set<AusAcceptance>().AsNoTracking().Where(a => !a.Deleted && a.Status == AusAcceptanceStatus.PmoAgreed);
            var acc = await aq.OrderBy(a => a.Id).Take(100).ToListAsync(ct);
            dto.Groups.Add(Group("acceptances", "انحراف‌های پذیرفته‌شده موقت منتظر تایید مدیرعامل", "/audit/acceptances", await aq.CountAsync(ct), acc.Select(a => new GlCeoInboxItemDto { Ref = "acceptance:" + a.Id, RecordId = a.Id, Title = a.RuleCode + " — " + a.ObjectNumber, Detail = a.Reason, Since = a.PmoAt, AgeDays = Age(a.PmoAt), Route = "/audit/acceptances" }).ToList()));

            // the readiness exception waits for the CEO only after the PMO has approved
            IQueryable<OpsReadinessException> eq = Uow.Set<OpsReadinessException>().AsNoTracking().Where(e => !e.Deleted && e.Status == OpsReadinessExceptionStatus.Pending && e.PmoUserId != null && e.CeoUserId == null);
            var ex = await eq.OrderBy(e => e.Id).Take(100).ToListAsync(ct);
            dto.Groups.Add(Group("readiness", "استثنای آمادگی مهندسی برای شروع پروژه", "/ops/readiness", await eq.CountAsync(ct), ex.Select(e => new GlCeoInboxItemDto { Ref = "readiness:" + e.Id, RecordId = e.Id, Title = "دستور کار " + e.WorkOrderId + " — " + e.ItemCodes, Detail = e.Justification, Since = e.RequestedAt, AgeDays = Age(e.RequestedAt), Route = "/ops/readiness" }).ToList()));

            IQueryable<SafetySettingChange> sq = Uow.Set<SafetySettingChange>().AsNoTracking().Where(c => !c.Deleted && c.Status == SafetyChangeStatus.Pending);
            var sc = await sq.OrderBy(c => c.Id).Take(100).ToListAsync(ct);
            dto.Groups.Add(Group("safety", "تغییر تنظیمات حساس ایمنی", "/integrity/safety", await sq.CountAsync(ct), sc.Select(c => new GlCeoInboxItemDto { Ref = "safety:" + c.Id, RecordId = c.Id, Title = c.Key + (c.Weakening ? " (سست‌کننده)" : ""), Detail = c.OldValue + " ← " + c.NewValue + " — " + c.Reason, Since = c.RequestedAt, AgeDays = Age(c.RequestedAt), Route = "/integrity/safety" }).ToList()));

            IQueryable<ChainBreakAcceptance> cq = Uow.Set<ChainBreakAcceptance>().AsNoTracking().Where(c => !c.Deleted && c.Status == SafetyChangeStatus.Pending && c.QaUserId != null && c.PmoUserId != null && c.CeoUserId == null);
            var cb = await cq.OrderBy(c => c.Id).Take(100).ToListAsync(ct);
            dto.Groups.Add(Group("chain", "پذیرش گسست زنجیره سوابق (امضای سوم)", "/integrity", await cq.CountAsync(ct), cb.Select(c => new GlCeoInboxItemDto { Ref = "chain:" + c.Id, RecordId = c.Id, Title = c.BreakEntityType + " #" + c.BreakRowId, Detail = c.Reason, Since = c.PmoAt, AgeDays = Age(c.PmoAt), Route = "/integrity" }).ToList()));

            IQueryable<OfInboxItem> iq = Uow.Set<OfInboxItem>().AsNoTracking().Where(i => i.RecipientRole == "CEO" && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read) && i.Kind == OfInboxKind.Approval);
            var inbox = await iq.OrderBy(i => i.Id).Take(100).ToListAsync(ct);
            dto.Groups.Add(Group("inbox", "درخواست‌های تایید در صندوق مدیرعامل", "/office/inbox", await iq.CountAsync(ct), inbox.Select(i => new GlCeoInboxItemDto { Ref = "inbox:" + i.Id, RecordId = (int)i.Id, Title = i.Title, Detail = i.SourceModule, Since = i.CreatedAt, AgeDays = Age(i.CreatedAt), Route = string.IsNullOrEmpty(i.Link) ? "/office/inbox" : i.Link }).ToList()));

            dto.Total = dto.Groups.Sum(g => g.Count);
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
