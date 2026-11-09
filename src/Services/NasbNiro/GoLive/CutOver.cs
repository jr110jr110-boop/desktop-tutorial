using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.GoLive;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.ProductBank;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;

namespace Services.NasbNiro.GoLive
{
    /// <summary>
    /// Package G: the one place that says what is legacy. The cut-over is an instant: the setting <c>Quality.ControlStartDate</c> (a Shamsi day, local midnight) when it is set, else the instant recorded once by the first start of the
    /// program after the update (<see cref="GlActivation"/>). Work created / released BEFORE it is legacy; everything at or after it is strict. While nothing is recorded yet (and the setting is 0) nothing is legacy: the gates are strict,
    /// never silently weak.
    /// </summary>
    public static class CutOver
    {
        /// <summary>The instant the control started; <see cref="DateTimeOffset.MinValue"/> when nothing is recorded (nothing is legacy).</summary>
        public static async Task<DateTimeOffset> StartAsync(IUnitOfWork uow, CancellationToken ct)
        {
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(uow, ct);
            return await StartAsync(uow, st, ct);
        }

        public static async Task<DateTimeOffset> StartAsync(IUnitOfWork uow, IReadOnlyDictionary<string, decimal> settings, CancellationToken ct)
        {
            DateTime? day = DesignGate.ShamsiDay(settings[GlSettingKeys.ControlStartDate]);
            if (day.HasValue) return new DateTimeOffset(DateTime.SpecifyKind(day.Value.Date, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(day.Value.Date));
            DateTimeOffset? recorded = await uow.Set<GlActivation>().AsNoTracking().OrderBy(a => a.StartAt).Select(a => (DateTimeOffset?)a.StartAt).FirstOrDefaultAsync(ct);   // the earliest ever recorded, a soft-deleted row included: deleting it cannot move the cut-over later
            return recorded ?? DateTimeOffset.MinValue;
        }

        /// <summary>Records the start once (the first start of the program after the update). Returns true when a row was written. Safe to call again.</summary>
        public static async Task<bool> EnsureRecordedAsync(IUnitOfWork uow, int? userId, CancellationToken ct)
        {
            if (await uow.Set<GlActivation>().AnyAsync(ct)) return false;   // never re-recorded, whatever happened to the first row
            DateTimeOffset now = DateTimeOffset.Now;
            uow.Set<GlActivation>().Add(new GlActivation { StartAt = now, RecordedByUserId = userId, Note = "ثبت خودکار در نخستین اجرای برنامه پس از به‌روزرسانی", CreateDate = now, ModifyDate = now, CreatorUserId = userId ?? 0, ModifierUserId = userId ?? 0 });
            try { await uow.SaveChangesAsync(ct); return true; }
            catch (DbUpdateException) { return false; }   // another instance recorded it at the same time
        }

        public static bool IsLegacy(DateTimeOffset? at, DateTimeOffset start) => at.HasValue && start > DateTimeOffset.MinValue && at.Value < start;

        public static string Text(DateTimeOffset start) => start <= DateTimeOffset.MinValue ? "—" : DesignGate.ShamsiText(start.LocalDateTime.Date);

        /// <summary>True when the work order was created before the cut-over.</summary>
        public static async Task<bool> WorkOrderIsLegacyAsync(IUnitOfWork uow, int workOrderId, DateTimeOffset start, CancellationToken ct)
        {
            if (start <= DateTimeOffset.MinValue) return false;
            DateTimeOffset? created = await uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AsNoTracking().Where(w => w.Id == workOrderId).Select(w => (DateTimeOffset?)w.CreateDate).FirstOrDefaultAsync(ct);
            return IsLegacy(created, start);
        }

        /// <summary>True when every drawing in force of the part was released before the cut-over (so the part is legacy; a revision in force with no release date is older than the record of dates and counts as legacy). A part with a drawing released after it is not.</summary>
        public static bool DrawingsAreLegacy(IReadOnlyList<PartDrawing> drawings, DateTimeOffset start) =>
            drawings != null && drawings.Count > 0 && start > DateTimeOffset.MinValue && drawings.All(d => !d.ReleasedAt.HasValue || d.ReleasedAt.Value < start);
    }

    /// <summary>
    /// Package G: closes the way round the release gates. A drawing that is linked to no part can be released without a characteristic list (Package B) and without a design verification (Package A1); linking it to a part afterwards would make it a
    /// released part drawing that never met either gate. So a link of a drawing to a part is checked like a release when the drawing already has a revision in force: a revision released at or after the cut-over instant needs both. A revision released
    /// before the cut-over is legacy and may be linked (the legacy rules and the AUDIT observations apply to it). A client document is not a design output and is not checked, as in the release path.
    /// </summary>
    public static class GlLinkGate
    {
        public static async Task<string> ProblemAsync(IUnitOfWork uow, int deliverableId, CancellationToken ct)
        {
            Entities.Model.NasbNiro.ProjectControl.PcEngineeringDeliverable d = await uow.Set<Entities.Model.NasbNiro.ProjectControl.PcEngineeringDeliverable>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == deliverableId && !x.Deleted, ct);
            if (d == null || d.ReceivedFromClient) return null;
            Entities.Model.NasbNiro.ProjectControl.PcDocumentRevision r = await uow.Set<Entities.Model.NasbNiro.ProjectControl.PcDocumentRevision>().AsNoTracking()
                .Where(x => !x.Deleted && x.DeliverableId == deliverableId && (x.Status == Entities.Model.NasbNiro.ProjectControl.PcDocStatus.Released || x.Status == Entities.Model.NasbNiro.ProjectControl.PcDocStatus.AsBuilt)).OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
            if (r == null || r.ReceivedFromClient) return null;   // nothing in force: the gates of the release apply when it is released
            DateTimeOffset start = await CutOver.StartAsync(uow, ct);
            if (start > DateTimeOffset.MinValue && (!r.ReleasedAt.HasValue || r.ReleasedAt.Value < start)) return null;   // legacy: released before the cut-over
            var missing = new List<string>();
            string list = await CharacteristicRelease.LinkProblemAsync(uow, r.Id, ct);
            if (list != null) missing.Add(list);
            string design = await DesignGate.LinkProblemAsync(uow, d, r, ct);
            if (design != null) missing.Add(design);
            if (missing.Count == 0) return null;
            return $"پیوند نقشه {d.DocumentNumber} به قطعه ممکن نیست: بازنگری {r.Code} پس از آغاز کنترل ({CutOver.Text(start)}) و بدون گذراندن دروازه‌های انتشار منتشر شده است. کمبود: " + string.Join("؛ ", missing) + ".";
        }
    }

    /// <summary>
    /// Package G: the register of legacy exemptions that were used (a Persian note and an observation of the AUDIT seat, never silent). One row per kind and object; the row is added to the caller's context and saved with the
    /// caller's own save.
    /// </summary>
    public static class GlLegacyRegister
    {
        public static async Task NoteAsync(IUnitOfWork uow, GlLegacyKind kind, string objectKey, int? workOrderId, int? itemId, int? revisionId, string number, string note, int? userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(objectKey)) return;
            string key = objectKey.Length > 100 ? objectKey.Substring(0, 100) : objectKey;
            if (uow.Set<GlLegacyUse>().Local.Any(x => x.Kind == kind && x.ObjectKey == key)) return;
            if (await uow.Set<GlLegacyUse>().AnyAsync(x => !x.Deleted && x.Kind == kind && x.ObjectKey == key, ct)) return;
            DateTimeOffset now = DateTimeOffset.Now;
            uow.Set<GlLegacyUse>().Add(new GlLegacyUse
            {
                Kind = kind, ObjectKey = key, WorkOrderId = workOrderId, ItemId = itemId, RevisionId = revisionId, Number = GlText.Cut(number, 100), Note = GlText.Cut(note, 1000) ?? "-",
                UsedByUserId = userId, UsedAt = now, CreateDate = now, ModifyDate = now, CreatorUserId = userId ?? 0, ModifierUserId = userId ?? 0,
            });
        }
    }
}
