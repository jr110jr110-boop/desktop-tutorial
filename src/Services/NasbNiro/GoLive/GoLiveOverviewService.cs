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
    }

    /// <summary>Package G: the go-live overview (what is legacy and what was used), the pre-check for «EdmsRequireRevision» and the CEO inbox (read only; each decision is made one by one on its own page).</summary>
    public class GoLiveOverviewService : PbServiceBase, IGoLiveOverviewService
    {
        public GoLiveOverviewService(IUnitOfWork uow, IInventoryUserContext user) : base(uow, user) { }

        public static string KindText(GlLegacyKind k) => k == GlLegacyKind.PcaNotApplicable ? "ترخیص دستور کار قدیمی بدون ممیزی پیکربندی" : k == GlLegacyKind.QcAcceptWithoutList ? "پذیرش QC در حالت قدیمی (بدون مقدار اسمی و تلرانس)" : "نمونه اول خودکار باز نشد (قطعه قدیمی)";

        public async Task<ResponseDto<GlOverviewDto>> OverviewAsync(CancellationToken ct)
        {
            DateTimeOffset start = await CutOver.StartAsync(Uow, ct);
            var o = new GlOverviewDto { ControlStartAt = start > DateTimeOffset.MinValue ? start : (DateTimeOffset?)null, ControlStartText = CutOver.Text(start) };
            if (start > DateTimeOffset.MinValue)
            {
                HashSet<int> withBaseline = (await Uow.Set<Entities.Model.NasbNiro.ProductBank.PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent).Select(b => b.WorkOrderId).ToListAsync(ct)).ToHashSet();
                List<int> legacyWos = await Uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AsNoTracking().Where(w => !w.Deleted && w.CreateDate < start).Select(w => w.Id).ToListAsync(ct);
                o.LegacyWorkOrders = legacyWos.Count(id => !withBaseline.Contains(id));
                var revs = await Uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => !r.Deleted && (r.Status == PcDocStatus.Released || r.Status == PcDocStatus.AsBuilt)).Select(r => new { r.Id, r.ReleasedAt }).ToListAsync(ct);
                HashSet<int> withList = (await Uow.Set<Entities.Model.NasbNiro.Quality.QcCharacteristicList>().AsNoTracking().Where(l => !l.Deleted && l.Status == Entities.Model.NasbNiro.Quality.CharListStatus.Released).Select(l => l.RevisionId).ToListAsync(ct)).ToHashSet();
                o.LegacyDrawingsWithoutList = revs.Count(r => (!r.ReleasedAt.HasValue || r.ReleasedAt.Value < start) && !withList.Contains(r.Id));
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
            bool required = (await PcSettingKeys.LoadAsync(Uow, ct))[PcSettingKeys.EdmsRequireRevision] >= 1m;
            HashSet<int> withRevisions = (await Uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => !r.Deleted).Select(r => r.DeliverableId).Distinct().ToListAsync(ct)).ToHashSet();
            var docs = await Uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => !d.Deleted && d.ClientSubmittal).Select(d => new { d.Id, d.DocumentNumber, d.Title }).ToListAsync(ct);
            var rows = docs.Where(d => !withRevisions.Contains(d.Id)).Select(d => new GlRevisionPreCheckRowDto { Source = "document", RecordId = d.Id, Number = d.DocumentNumber, Title = d.Title, Problem = "سند فهرست کارفرما بازنگری ثبت‌شده ندارد؛ ارسال بعدی آن با تنظیم ۱ رد می‌شود." }).ToList();
            var dto = new GlRevisionPreCheckDto { CurrentlyRequired = required, TotalLines = docs.Count, WouldBreak = rows.Count, Rows = rows.Take(500).ToList() };
            dto.Summary = required ? "تنظیم هم‌اکنون ۱ است." : rows.Count == 0 ? "همه اسناد فهرست کارفرما بازنگری ثبت‌شده دارند؛ تنظیم بی‌خطر به ۱ تغییر می‌کند." : $"{rows.Count} سند بازنگری ثبت‌شده ندارد؛ پیش از تغییر تنظیم به ۱، بازنگری آن‌ها را ثبت کنید.";
            return ResponseDtoHelper.Ok(dto);
        }

        private static GlCeoInboxGroupDto Group(string key, string title, string route, List<GlCeoInboxItemDto> items) =>
            new GlCeoInboxGroupDto { Key = key, Title = title, Route = route, Count = items.Count, OldestDays = items.Count == 0 ? 0 : items.Max(i => i.AgeDays), Items = items.OrderByDescending(i => i.AgeDays).Take(100).ToList() };

        public async Task<ResponseDto<GlCeoInboxDto>> CeoInboxAsync(CancellationToken ct)
        {
            if (!InRole("CEO,PMO,AUDITOR")) return ResponseDtoHelper.BadRequest<GlCeoInboxDto>(GlText.NotAllowed);
            DateTimeOffset now = DateTimeOffset.Now;
            int Age(DateTimeOffset? at) => at.HasValue ? Math.Max(0, (int)(now - at.Value).TotalDays) : 0;
            var dto = new GlCeoInboxDto();

            var findings = await Uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && f.Status == AusFindingStatus.PmoProposed).OrderBy(f => f.Id).Take(300).ToListAsync(ct);
            dto.Groups.Add(Group("findings", "بستن یافته‌های ممیزی (مرحله سوم: مدیرعامل)", "/audit/findings", findings.Select(f => new GlCeoInboxItemDto { Ref = "finding:" + f.Id, RecordId = f.Id, Title = f.Number + " — " + f.Title, Detail = f.RuleCode, Since = f.ModifyDate, AgeDays = Age(f.ModifyDate), Route = "/audit/findings/" + f.Id }).ToList()));

            var acc = await Uow.Set<AusAcceptance>().AsNoTracking().Where(a => !a.Deleted && a.Status == AusAcceptanceStatus.PmoAgreed).OrderBy(a => a.Id).Take(300).ToListAsync(ct);
            dto.Groups.Add(Group("acceptances", "انحراف‌های پذیرفته‌شده موقت منتظر تایید مدیرعامل", "/audit/acceptances", acc.Select(a => new GlCeoInboxItemDto { Ref = "acceptance:" + a.Id, RecordId = a.Id, Title = a.RuleCode + " — " + a.ObjectNumber, Detail = a.Reason, Since = a.PmoAt, AgeDays = Age(a.PmoAt), Route = "/audit/acceptances" }).ToList()));

            var ex = await Uow.Set<OpsReadinessException>().AsNoTracking().Where(e => !e.Deleted && e.Status == OpsReadinessExceptionStatus.Pending).OrderBy(e => e.Id).Take(300).ToListAsync(ct);
            dto.Groups.Add(Group("readiness", "استثنای آمادگی مهندسی برای شروع پروژه", "/ops/readiness", ex.Select(e => new GlCeoInboxItemDto { Ref = "readiness:" + e.Id, RecordId = e.Id, Title = "دستور کار " + e.WorkOrderId + " — " + e.ItemCodes, Detail = e.Justification, Since = e.RequestedAt, AgeDays = Age(e.RequestedAt), Route = "/ops/readiness" }).ToList()));

            var sc = await Uow.Set<SafetySettingChange>().AsNoTracking().Where(c => !c.Deleted && c.Status == SafetyChangeStatus.Pending).OrderBy(c => c.Id).Take(300).ToListAsync(ct);
            dto.Groups.Add(Group("safety", "تغییر تنظیمات حساس ایمنی", "/integrity/safety", sc.Select(c => new GlCeoInboxItemDto { Ref = "safety:" + c.Id, RecordId = c.Id, Title = c.Key + (c.Weakening ? " (سست‌کننده)" : ""), Detail = c.OldValue + " ← " + c.NewValue + " — " + c.Reason, Since = c.RequestedAt, AgeDays = Age(c.RequestedAt), Route = "/integrity/safety" }).ToList()));

            var cb = await Uow.Set<ChainBreakAcceptance>().AsNoTracking().Where(c => !c.Deleted && c.Status == SafetyChangeStatus.Pending && c.QaUserId != null && c.PmoUserId != null && c.CeoUserId == null).OrderBy(c => c.Id).Take(300).ToListAsync(ct);
            dto.Groups.Add(Group("chain", "پذیرش گسست زنجیره سوابق (امضای سوم)", "/integrity", cb.Select(c => new GlCeoInboxItemDto { Ref = "chain:" + c.Id, RecordId = c.Id, Title = c.BreakEntityType + " #" + c.BreakRowId, Detail = c.Reason, Since = c.PmoAt, AgeDays = Age(c.PmoAt), Route = "/integrity" }).ToList()));

            var inbox = await Uow.Set<OfInboxItem>().AsNoTracking().Where(i => i.RecipientRole == "CEO" && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read) && i.Kind == OfInboxKind.Approval).OrderBy(i => i.Id).Take(300).ToListAsync(ct);
            dto.Groups.Add(Group("inbox", "درخواست‌های تایید در صندوق مدیرعامل", "/office/inbox", inbox.Select(i => new GlCeoInboxItemDto { Ref = "inbox:" + i.Id, RecordId = (int)i.Id, Title = i.Title, Detail = i.SourceModule, Since = i.CreatedAt, AgeDays = Age(i.CreatedAt), Route = string.IsNullOrEmpty(i.Link) ? "/office/inbox" : i.Link }).ToList()));

            dto.Total = dto.Groups.Sum(g => g.Count);
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
