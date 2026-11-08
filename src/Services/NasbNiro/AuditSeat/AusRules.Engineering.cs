using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.EngChange;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.ProductBank;

namespace Services.NasbNiro.AuditSeat
{
    // Rules of the processes CHANGE, DESIGN, DOCUMENT (engineering) and PROJECT (baseline, EVM, risk).
    public static partial class AusCatalog
    {
        private static IEnumerable<AusRuleDef> EngineeringRules()
        {
            yield return Def("AUD-EC-001", "CHANGE", AusSeverity.Major, "دستور تغییر مهندسی تاییدشده بدون اثر محاسبه‌شده یا با مورد بی‌تکلیف", "ISO 10007:2017", "5.4 / 6.3", "تصمیم D13 (تغییر مهندسی: تکلیف هر مورد متاثر)",
                "EcOrder approved/closed with no stored impact (ImpactSha256 empty) or with a disposition item that has no Disposition", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Uow.Set<EcOrder>().AsNoTracking().Where(o => !o.Deleted && (o.Status == EcoStatus.Approved || o.Status == EcoStatus.Closed) && o.ModifyDate >= since
                            && (o.ImpactSha256 == null || o.ImpactSha256 == "" || c.Uow.Set<EcDispositionItem>().Any(d => d.OrderId == o.Id && d.Disposition == null))).OrderBy(o => o.Id)
                        .Select(o => new { o.Id, o.Number, o.ImpactSha256, o.CreatorUserId }));
                    return rows.Where(o => !c.FocusId.HasValue || o.Id == c.FocusId.Value).Select(o =>
                        Hit("EcOrder", o.Id, o.Number, string.IsNullOrEmpty(o.ImpactSha256) ? $"دستور تغییر {o.Number} تایید شده ولی اثر محاسبه‌شده‌ای ندارد." : $"دستور تغییر {o.Number} تایید شده ولی برای برخی موارد متاثر تکلیفی تعیین نشده است.", owner: o.CreatorUserId, link: "/eng-change/orders/" + o.Id)
                            .Fact("impactHash", o.ImpactSha256)).ToList();
                });

            yield return Def("AUD-EC-002", "CHANGE", AusSeverity.Minor, "وظیفه‌ی اجرای تغییر مهندسی از موعد گذشته و انجام نشده است", "ISO 10007:2017", "6.3", "تصمیم D13 (وظایف اجرای ECO)",
                "EcTask Open with DueDate before today", false, async c =>
                {
                    DateTime today = c.Today;
                    var rows = await c.BoundedAsync(c.Uow.Set<EcTask>().AsNoTracking().Where(t => !t.Deleted && t.Status == EcTaskStatus.Open && t.DueDate < today).OrderBy(t => t.Id)
                        .Select(t => new { t.Id, t.OrderId, t.Title, t.DueDate, t.OwnerUserId }));
                    return rows.Where(t => !c.FocusId.HasValue || t.Id == c.FocusId.Value).Select(t =>
                        Hit("EcTask", t.Id, t.Title, $"وظیفه‌ی «{t.Title}» از دستور تغییر {t.OrderId} سررسید {P(t.DueDate)} را گذرانده است.", owner: t.OwnerUserId, link: "/eng-change/orders/" + t.OrderId).Fact("order", t.OrderId).Fact("due", t.DueDate)).ToList();
                });

            yield return Def("AUD-EC-003", "DESIGN", AusSeverity.Major, "BOM پس از آغاز کنترل طراحی بدون راستی‌آزمایی طراحی منتشر شده است", Iso9, "8.3.4", "برنامه بسته A1 (راستی‌آزمایی طراحی پیش از انتشار BOM)",
                "PbBomHeader Released on or after the design-control start for which DesignGate.BomCoverageAsync(Verification) is Required and not Passed (bounded to 200 per pass)", true, async c =>
                {
                    var hits = new List<AusHit>();
                    DateTime start = await DesignGate.ControlStartAsync(c.Uow, c.Ct);
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    DateTimeOffset startOffset = new DateTimeOffset(start, TimeZoneInfo.Local.GetUtcOffset(start));
                    var boms = await c.Uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.Status == PbBomStatus.Released && h.ReleasedAt >= startOffset && h.ModifyDate >= since).OrderBy(h => h.Id).Take(200).ToListAsync(c.Ct);
                    foreach (PbBomHeader h in boms)
                    {
                        if (c.FocusId.HasValue && h.Id != c.FocusId.Value) continue;
                        DesignCoverage cov = await DesignGate.BomCoverageAsync(c.Uow, h, DesignReviewType.Verification, c.Ct);
                        if (cov == null || !cov.Required || cov.Passed) continue;
                        hits.Add(Hit("PbBomHeader", h.Id, h.Code, $"BOM {h.Code} بازنگری {h.Revision} در {P(h.ReleasedAt)} منتشر شده ولی راستی‌آزمایی طراحی ندارد: {string.Join("؛ ", cov.Missing.Take(2))}.", "verification", owner: h.ReleasedByUserId, link: "/product-bank")
                            .Fact("revision", h.Revision).Fact("releasedAt", h.ReleasedAt).Fact("controlStart", start).Fact("missing", cov.Missing.FirstOrDefault()));
                    }
                    return hits;
                });

            yield return Def("AUD-LB-001", "DESIGN", AusSeverity.Major, "نوشتن در BOM قدیمی پس از انجماد آن (BOM محصول تنها مرجع است)", "ISO 10007:2017", "6.2", "برنامه بسته A2 (BOM مرجع یگانه)",
                "legacy Bom / EquipmentBom rows modified after the freeze day (setting Audit.T.LegacyBomFreezeDay, else the day of the first audit run) while EngChange.LegacyBomFrozen is on", true, async c =>
                {
                    var hits = new List<AusHit>();
                    var pb = await PbSettingKeys.LoadAsync(c.Uow, c.Ct);
                    if (pb[PbSettingKeys.LegacyBomFrozen] == "false") return hits;
                    DateTime? set = DesignGate.ShamsiDay(c.S(AusSettingKeys.LegacyBomFreezeDay));
                    DateTimeOffset? first = set.HasValue ? new DateTimeOffset(set.Value, TimeZoneInfo.Local.GetUtcOffset(set.Value)) : await c.Uow.Set<AusRun>().AsNoTracking().OrderBy(r => r.StartedAt).Select(r => (DateTimeOffset?)r.StartedAt).FirstOrDefaultAsync(c.Ct);
                    if (first == null) return hits;
                    DateTimeOffset from = c.Since.HasValue && c.Since.Value > first.Value ? c.Since.Value : first.Value;
                    var legacy = await c.BoundedAsync(c.Uow.Set<Bom>().AsNoTracking().Where(b => b.ModifyDate > from).OrderBy(b => b.Id).Select(b => new { b.Id, b.Title, b.ModifyDate, b.ModifierUserId }));
                    hits.AddRange(legacy.Where(b => !c.FocusId.HasValue || b.Id == c.FocusId.Value).Select(b =>
                        Hit("Bom", b.Id, b.Title, $"ردیف BOM قدیمی «{b.Title}» در {P(b.ModifyDate)} تغییر کرده در حالی که BOM قدیمی بسته است.", "bom", owner: b.ModifierUserId, link: "/product-bank").Fact("modifiedAt", b.ModifyDate)));
                    var eq = await c.BoundedAsync(c.Uow.Set<EquipmentBom>().AsNoTracking().Where(b => b.ModifyDate > from).OrderBy(b => b.Id).Select(b => new { b.Id, b.Title, b.ModifyDate, b.ModifierUserId }));
                    hits.AddRange(eq.Where(b => !c.FocusId.HasValue || b.Id == c.FocusId.Value).Select(b =>
                        Hit("EquipmentBom", b.Id, b.Title, $"ردیف BOM تجهیزات «{b.Title}» در {P(b.ModifyDate)} تغییر کرده در حالی که BOM قدیمی بسته است.", "eqbom", owner: b.ModifierUserId, link: "/product-bank").Fact("modifiedAt", b.ModifyDate)));
                    return hits;
                });

            yield return Def("AUD-DC-001", "DOCUMENT", AusSeverity.Major, "بازرسی بر پایه‌ی بازنگری نقشه‌ای ثبت شده که پیش از آن منسوخ شده بود", Iso9, "7.5.3.2", "Annex 9 EDMS (کنترل بازنگری نقشه)",
                "QcRecord created after its DrawingRevisionId was superseded/obsolete", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(
                        from r in c.Uow.Set<QcRecord>().AsNoTracking()
                        join d in c.Uow.Set<PcDocumentRevision>().AsNoTracking() on r.DrawingRevisionId.Value equals d.Id
                        where !r.Deleted && r.DrawingRevisionId != null && r.CreateDate >= since && (d.Status == PcDocStatus.Superseded || d.Status == PcDocStatus.Obsolete) && d.SupersededAt != null && d.SupersededAt < r.CreateDate
                        orderby r.Id select new { r.Id, r.Number, r.DrawingNumber, RevisionCode = d.Code, d.SupersededAt, r.InspectorUserId });
                    return rows.Where(r => !c.FocusId.HasValue || r.Id == c.FocusId.Value).Select(r =>
                        Hit("QcRecord", r.Id, r.Number, $"بازرسی {r.Number} به نقشه‌ی {r.DrawingNumber} بازنگری {r.RevisionCode} ارجاع می‌دهد که از {P(r.SupersededAt)} منسوخ بوده است.", "obsolete", owner: r.InspectorUserId, link: "/quality/records")
                            .Fact("drawing", r.DrawingNumber).Fact("revision", r.RevisionCode).Fact("supersededAt", r.SupersededAt)).ToList();
                });

            yield return Def("AUD-DC-002", "DOCUMENT", AusSeverity.Major, "در تولید از نقشه‌ی منتشرنشده (پیش‌نویس یا در بازبینی) استفاده شده است", Iso9, "8.5.1 / 7.5.3", "Annex 9 EDMS (انتشار نقشه پیش از تولید)",
                "QcRecord of a work order whose DrawingRevisionId is a Draft/UnderReview revision", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(
                        from r in c.Uow.Set<QcRecord>().AsNoTracking()
                        join d in c.Uow.Set<PcDocumentRevision>().AsNoTracking() on r.DrawingRevisionId.Value equals d.Id
                        where !r.Deleted && r.DrawingRevisionId != null && r.WorkOrderId != null && r.CreateDate >= since && (d.Status == PcDocStatus.Draft || d.Status == PcDocStatus.UnderReview)
                        orderby r.Id select new { r.Id, r.Number, r.DrawingNumber, RevisionCode = d.Code, d.Status, r.InspectorUserId });
                    return rows.Where(r => !c.FocusId.HasValue || r.Id == c.FocusId.Value).Select(r =>
                        Hit("QcRecord", r.Id, r.Number, $"بازرسی تولید {r.Number} از نقشه‌ی {r.DrawingNumber} بازنگری {r.RevisionCode} استفاده کرده که هنوز منتشر نشده ({r.Status}).", "unreleased", owner: r.InspectorUserId, link: "/quality/records")
                            .Fact("drawing", r.DrawingNumber).Fact("revision", r.RevisionCode).Fact("status", r.Status)).ToList();
                });
        }

        private static IEnumerable<AusRuleDef> ProjectRules()
        {
            yield return Def("AUD-BL-001", "PROJECT", AusSeverity.Major, "مبنای زمان‌بندی/هزینه (بازنگری مبنا) بدون تغییر تاییدشده تصویب شده است", "EIA-748", "GL28, GL30, GL31", "WI-PL-013 / FO-PO-011 (تغییرات)",
                "PcBaseline revision (Number > 1) approved/superseded with no PcChangeRequest decided as Approve", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Uow.Set<PcBaseline>().AsNoTracking().Where(b => !b.Deleted && b.Number > 1 && (b.Status == PcBaselineStatus.Approved || b.Status == PcBaselineStatus.Superseded) && b.ModifyDate >= since
                            && !c.Uow.Set<PcChangeRequest>().Any(r => r.Id == b.ChangeRequestId && !r.Deleted && r.Decision == PcChangeDecision.Approve && (r.Status == PcChangeStatus.Decided || r.Status == PcChangeStatus.Implementing || r.Status == PcChangeStatus.Closed))).OrderBy(b => b.Id)
                        .Select(b => new { b.Id, b.ContractId, b.Number, b.Kind, b.ChangeRequestId, b.ApprovedByUserId, b.ApprovedAt }));
                    return rows.Where(b => !c.FocusId.HasValue || b.Id == c.FocusId.Value).Select(b =>
                        Hit("PcBaseline", b.Id, b.Number.ToString(), $"مبنای شماره‌ی {b.Number} ({b.Kind}) پروژه‌ی {b.ContractId} تصویب شده بدون تغییر تاییدشده‌ای که آن را مجاز کند.", owner: b.ApprovedByUserId, link: "/project-control")
                            .Fact("contract", b.ContractId).Fact("number", b.Number).Fact("changeRequest", b.ChangeRequestId).Fact("approvedAt", b.ApprovedAt)).ToList();
                });

            yield return Def("AUD-EVM-001", "PROJECT", AusSeverity.Minor, "تاریخ داده‌ی EVM پروژه‌ی فعال کهنه است یا اسنپ‌شات پیشرفت ندارد", "EIA-748", "GL22", "WI-PL-009 (برنامه‌ریزی و کنترل پروژه)",
                "active ContractEntity with an approved PcBaseline whose latest PcProgressSnapshot.DataDate is older than Audit.T.EvmStaleDays (or none)", false, async c =>
                {
                    DateTime limit = c.Today.AddDays(-c.Days(AusSettingKeys.EvmStaleDays));
                    var rows = await c.BoundedAsync(c.Uow.Set<ContractEntity>().AsNoTracking().Where(k => !k.Deleted && k.Status == ContractStatus.Current && c.Uow.Set<PcBaseline>().Any(b => b.ContractId == k.Id && b.Status == PcBaselineStatus.Approved && !b.Deleted)).OrderBy(k => k.Id)
                        .Select(k => new { k.Id, k.Code, Last = c.Uow.Set<PcProgressSnapshot>().Where(s => s.ContractId == k.Id).Max(s => (DateTime?)s.DataDate) }));
                    return rows.Where(k => (!c.FocusId.HasValue || k.Id == c.FocusId.Value) && (k.Last == null || k.Last.Value < limit)).Select(k =>
                        Hit("ContractEntity", k.Id, k.Code, k.Last.HasValue ? $"تاریخ داده‌ی EVM پروژه‌ی {k.Code} ({P(k.Last)}) از {c.S(AusSettingKeys.EvmStaleDays):0} روز کهنه‌تر است." : $"پروژه‌ی {k.Code} مبنای تاییدشده دارد ولی هیچ اسنپ‌شات پیشرفت (EVM) ندارد.", link: "/project-control").Fact("dataDate", k.Last).Fact("maxDays", c.S(AusSettingKeys.EvmStaleDays))).ToList();
                });

            yield return Def("AUD-EVM-002", "PROJECT", AusSeverity.Minor, "هزینه‌ی واقعی پروژه بدون عنصر WBS ثبت شده است", "EIA-748", "GL16, GL17", "WI-PL-009 (هزینه روی WBS)",
                "CostEntry with a ContractId and no WbsNodeId (entries since the last pass)", true, async c =>
                {
                    DateTimeOffset since = c.Since ?? DateTimeOffset.MinValue;
                    var rows = await c.BoundedAsync(c.Uow.Set<CostEntry>().AsNoTracking().Where(e => e.ContractId != null && e.WbsNodeId == null && e.CreatedAt >= since).OrderBy(e => e.Id)
                        .Select(e => new { e.Id, e.ContractId, e.EntryDate, e.SourceType, e.SourceNumber, e.CreatedByUserId, e.LabourProtected }));
                    return rows.Where(e => !c.FocusId.HasValue || e.Id == c.FocusId.Value).Select(e =>
                        Hit("CostEntry", e.Id, e.SourceNumber, $"هزینه‌ی {e.SourceType} {e.SourceNumber} ({P(e.EntryDate)}) روی پروژه‌ی {e.ContractId} ثبت شده ولی عنصر WBS ندارد.", owner: e.CreatedByUserId, link: "/project-control")
                            .Fact("contract", e.ContractId).Fact("source", e.SourceType).Fact("number", e.SourceNumber).Fact("entryDate", e.EntryDate)).ToList();   // the amount is not copied: it can be a protected labour figure
                });

            yield return Def("AUD-RK-001", "PROJECT", AusSeverity.Minor, "ثبت ریسک پروژه کهنه است: موعد مرور گذشته یا مدت‌ها بدون هیچ تغییری", "ISO 21502:2020", "7.11", "Annex 2 PMBOK (ثبت ریسک)",
                "PcRisk not closed with ReviewDate before today, or with no ReviewDate and ModifyDate older than Audit.T.RiskStaleDays", false, async c =>
                {
                    DateTime today = c.Today;
                    DateTimeOffset staleLimit = c.Now.AddDays(-c.Days(AusSettingKeys.RiskStaleDays));
                    var rows = await c.BoundedAsync(c.Uow.Set<PcRisk>().AsNoTracking().Where(r => !r.Deleted && r.Status != PcRiskStatus.Closed && ((r.ReviewDate != null && r.ReviewDate < today) || (r.ReviewDate == null && r.ModifyDate < staleLimit))).OrderBy(r => r.Id)
                        .Select(r => new { r.Id, r.ContractId, r.Number, r.Title, r.ReviewDate, r.Score, r.IsOpportunity }));
                    return rows.Where(r => !c.FocusId.HasValue || r.Id == c.FocusId.Value).Select(r =>
                        Hit("PcRisk", r.Id, r.Number, r.ReviewDate.HasValue ? $"ریسک {r.Number} «{r.Title}» (پروژه‌ی {r.ContractId}) موعد مرور {P(r.ReviewDate)} را گذرانده است." : $"ریسک {r.Number} «{r.Title}» هیچ موعد مرور ندارد و مدت‌هاست تغییر نکرده است.", link: "/project-control")
                            .Fact("contract", r.ContractId).Fact("reviewDate", r.ReviewDate).Fact("score", r.Score).Fact("opportunity", r.IsOpportunity)).ToList();
                });
        }
    }
}
