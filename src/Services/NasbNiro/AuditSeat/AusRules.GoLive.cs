using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.GoLive;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.GoLive;

namespace Services.NasbNiro.AuditSeat
{
    // Package G (go-live cut-over): the legacy exemptions that were used are observations of the AUDIT seat (never silent), and a purchase order line whose cited drawing revision was superseded is a finding until it is dealt with.
    public static partial class AusCatalog
    {
        private static IEnumerable<AusRuleDef> GoLiveRules()
        {
            yield return Def("AUD-CO-001", "DESIGN", AusSeverity.Observation, "دستور کار قدیمی بدون پایه‌ریزی از بانک محصول ترخیص شده است (ممیزی پیکربندی انجام نشده)", Iso9, "8.5.2 / ISO 10007 6.4", "برنامه بسته G (گذار به کنترل پیکربندی؛ پذیرش پیکربندی پروژه موجود)",
                "GlLegacyUse of kind PcaNotApplicable grouped by work order: one hit per work order that still has no current product-bank baseline", false, async c =>
                {
                    var uses = await c.BoundedAsync(c.Scope(c.Uow.Set<GlLegacyUse>().AsNoTracking()).Where(u => !u.Deleted && u.Kind == GlLegacyKind.PcaNotApplicable && u.WorkOrderId != null
                            && !c.Uow.Set<PbProjectBaseline>().Any(b => !b.Deleted && b.IsCurrent && b.WorkOrderId == u.WorkOrderId)).OrderBy(u => u.Id)
                        .Select(u => new { u.Id, u.WorkOrderId, u.Number, u.UsedAt }));
                    var groups = uses.GroupBy(u => u.WorkOrderId.Value).Where(g => !c.FocusId.HasValue || g.Key == c.FocusId.Value).OrderBy(g => g.Key).Take(c.Max).ToList();
                    List<int> ids = groups.Select(g => g.Key).ToList();
                    Dictionary<int, string> codes = await c.Uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AsNoTracking().Where(w => ids.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, c.Ct);
                    return groups.Select(g =>
                    {
                        string code = codes.TryGetValue(g.Key, out string cd) && !string.IsNullOrEmpty(cd) ? cd : "WO" + g.Key;
                        return Hit("WorkOrder", g.Key, code, $"دستور کار {code} در حالت قدیمی ({g.Count()} ترخیص) بدون پایه‌ریزی از بانک محصول ترخیص شده است؛ «پذیرش پیکربندی پروژه موجود» را ثبت کنید.", link: "/go-live/config-adoptions")
                            .On(g.Min(x => x.UsedAt)).Fact("uses", g.Count()).Fact("lastRelease", g.OrderBy(x => x.Id).Last().Number);
                    }).ToList();
                });

            yield return Def("AUD-CO-002", "QUALITY", AusSeverity.Observation, "پذیرش QC در حالت قدیمی انجام شده است (بدون فهرست مشخصه، مقدار اسمی و تلرانس ثبت نشده)", Iso9, "8.6 / 8.5.1", "برنامه بسته G (گذار به کنترل کیفیت؛ پذیرش مشخصه‌های نقشه موجود)",
                "GlLegacyUse of kind QcAcceptWithoutList grouped by drawing revision (else by part): one hit per revision that still has no released characteristic list", false, async c =>
                {
                    var uses = await c.BoundedAsync(c.Scope(c.Uow.Set<GlLegacyUse>().AsNoTracking()).Where(u => !u.Deleted && u.Kind == GlLegacyKind.QcAcceptWithoutList).OrderBy(u => u.Id)
                        .Select(u => new { u.Id, u.Number, u.ItemId, u.RevisionId, u.UsedAt }));
                    var byRevision = uses.Where(u => u.RevisionId.HasValue).GroupBy(u => u.RevisionId.Value).Where(g => !c.FocusId.HasValue || g.Key == c.FocusId.Value).OrderBy(g => g.Key).Take(c.Max).ToList();
                    List<int> revIds = byRevision.Select(g => g.Key).ToList();
                    HashSet<int> withList = (await c.Uow.Set<Entities.Model.NasbNiro.Quality.QcCharacteristicList>().AsNoTracking().Where(l => !l.Deleted && l.Status == Entities.Model.NasbNiro.Quality.CharListStatus.Released && revIds.Contains(l.RevisionId)).Select(l => l.RevisionId).ToListAsync(c.Ct)).ToHashSet();
                    return byRevision.Where(g => !withList.Contains(g.Key)).Select(g =>
                        Hit("PcDocumentRevision", g.Key, "REV" + g.Key, $"{g.Count()} پذیرش QC در حالت قدیمی روی نقشه‌ای ثبت شده که هنوز فهرست مشخصه منتشرشده ندارد؛ «پذیرش مشخصه‌های نقشه موجود» را ثبت کنید.", link: "/go-live/char-adoptions")
                            .On(g.Min(x => x.UsedAt)).Fact("uses", g.Count()).Fact("lastRecord", g.OrderBy(x => x.Id).Last().Number)).ToList();
                });

            yield return Def("AUD-CO-003", "PRODUCTION", AusSeverity.Observation, "نمونه اول (FAI) برای سفارش تولید قطعه قدیمی به‌صورت خودکار باز نشده است", Iso9, "8.5.1 / 8.6", "برنامه بسته G (گذار؛ FAI قطعات قدیمی)",
                "GlLegacyUse of kind FaiNotOpened grouped by part: one hit per part whose drawings in force are still all legacy and still have no released characteristic list", false, async c =>
                {
                    var uses = await c.BoundedAsync(c.Scope(c.Uow.Set<GlLegacyUse>().AsNoTracking()).Where(u => !u.Deleted && u.Kind == GlLegacyKind.FaiNotOpened && u.ItemId != null).OrderBy(u => u.Id)
                        .Select(u => new { u.Id, u.Number, u.ItemId, u.UsedAt }));
                    var groups = uses.GroupBy(u => u.ItemId.Value).Where(g => !c.FocusId.HasValue || g.Key == c.FocusId.Value).OrderBy(g => g.Key).Take(c.Max).ToList();
                    DateTimeOffset start = await CutOver.StartAsync(c.Uow, c.Ct);
                    Dictionary<int, string> codes = await c.Uow.Set<Entities.Model.NasbNiro.Inventory.InventoryItem>().AsNoTracking().Where(i => groups.Select(g => g.Key).Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, c.Ct);
                    var hits = new List<AusHit>();
                    foreach (var g in groups)
                    {
                        global::Services.NasbNiro.Quality.ResolvedCharacteristics r = await global::Services.NasbNiro.Quality.CharacteristicResolver.ForItemAsync(c.Uow, g.Key, null, c.Ct);
                        if (r.HasList || !CutOver.DrawingsAreLegacy(r.Drawings, start)) continue;
                        string code = codes.TryGetValue(g.Key, out string cd) ? cd : "ITEM" + g.Key;
                        hits.Add(Hit("InventoryItem", g.Key, code, $"برای {g.Count()} سفارش تولید قطعه {code} نمونه اول باز نشد (نقشه قدیمی بدون فهرست مشخصه)؛ با «پذیرش مشخصه‌های نقشه موجود» این معافیت پایان می‌یابد.", link: "/go-live/char-adoptions")
                            .On(g.Min(x => x.UsedAt)).Fact("uses", g.Count()));
                    }
                    return hits;
                });

            yield return Def("AUD-CO-004", "DESIGN", AusSeverity.Major, "نقشه‌ی قطعه پس از آغاز کنترل بدون فهرست مشخصه‌ی تاییدشده یا بدون تایید طراحی در جریان است (دور زدن دروازه‌های انتشار)", Iso9, "8.3.4 / 8.5.1", "برنامه بسته G (بستن راه پیوند پس از انتشار)",
                "In-force PcDocumentRevision released at or after the cut-over instant, of a deliverable linked to a part (not a client document), with no approved or released characteristic list, or, when verification is required, no accepted design verification", false, async c =>
                {
                    DateTimeOffset start = await CutOver.StartAsync(c.Uow, c.Ct);
                    if (start <= DateTimeOffset.MinValue) return new List<AusHit>();
                    bool verification = await global::Services.NasbNiro.ProductBank.DesignGate.VerificationRequiredAsync(c.Uow, c.Ct);
                    var rows = await c.BoundedAsync(
                        from r in c.Scope(c.Uow.Set<Entities.Model.NasbNiro.ProjectControl.PcDocumentRevision>().AsNoTracking())
                        join d in c.Uow.Set<Entities.Model.NasbNiro.ProjectControl.PcEngineeringDeliverable>().AsNoTracking() on r.DeliverableId equals d.Id
                        where !r.Deleted && (r.Status == Entities.Model.NasbNiro.ProjectControl.PcDocStatus.Released || r.Status == Entities.Model.NasbNiro.ProjectControl.PcDocStatus.AsBuilt) && !r.ReceivedFromClient && !d.ReceivedFromClient
                            && r.ReleasedAt != null && r.ReleasedAt >= start
                            && c.Uow.Set<Entities.Model.NasbNiro.ProjectControl.PcDocumentLink>().Any(l => l.DeliverableId == d.Id && !l.Deleted && l.UnlinkReason == null
                                && (l.Kind == Entities.Model.NasbNiro.ProjectControl.PcDocLinkKind.Item || l.Kind == Entities.Model.NasbNiro.ProjectControl.PcDocLinkKind.BomLine || l.Kind == Entities.Model.NasbNiro.ProjectControl.PcDocLinkKind.PartNumber))
                        orderby r.Id select new { r.Id, r.Code, d.DocumentNumber, r.ReleasedByUserId, r.ReleasedAt });
                    var hits = new List<AusHit>();
                    foreach (var r in rows.Where(x => !c.FocusId.HasValue || x.Id == c.FocusId.Value))
                    {
                        var missing = new List<string>();
                        if (await global::Services.NasbNiro.Quality.CharacteristicRelease.LinkProblemAsync(c.Uow, r.Id, c.Ct) != null) missing.Add("فهرست مشخصه‌ی تاییدشده");
                        if (verification && !(await global::Services.NasbNiro.ProductBank.DesignGate.DrawingCoverageAsync(c.Uow, r.Id, "نقشه " + r.DocumentNumber, c.Ct)).Passed) missing.Add("تایید طراحی (Verification)");
                        if (missing.Count == 0) continue;
                        hits.Add(Hit("PcDocumentRevision", r.Id, r.DocumentNumber, $"نقشه‌ی قطعه {r.DocumentNumber} (بازنگری {r.Code}) پس از آغاز کنترل منتشر شده ولی {string.Join(" و ", missing)} ندارد.", owner: r.ReleasedByUserId, link: "/quality/characteristics")
                            .On(r.ReleasedAt).Fact("revision", r.Code).Fact("missing", string.Join("+", missing)));
                        if (hits.Count >= c.Max) break;
                    }
                    return hits;
                });

            yield return Def("AUD-PO-004", "PROCUREMENT", AusSeverity.Major, "ردیف سفارش خرید باز به بازنگری منسوخ‌شده نقشه ارجاع دارد و تامین‌کننده بازنگری جدید را تایید نکرده است", Iso9, "8.4.3", "برنامه بسته G (ردیف سفارش خرید و بازنگری نقشه)",
                "PurchaseOrderLine of an open order whose cited drawing revision is no longer the revision in force and with no supplier confirmation of the new one", false, async c =>
                {
                    List<PoDrawingCitation.Stale> stale = await PoDrawingCitation.StaleAsync(c.Uow, null, c.Ct);
                    return stale.Where(s => !c.FocusId.HasValue || s.Line.Id == c.FocusId.Value).Take(c.Max).Select(s =>
                        Hit("PurchaseOrderLine", s.Line.Id, s.OrderNumber, $"سفارش {s.OrderNumber}: نقشه {s.Line.DrawingNumber} با بازنگری {s.Line.DrawingRevision} ارجاع شده ولی بازنگری {s.CurrentRevision} منتشر شده است؛ ردیف را اصلاح یا تایید تامین‌کننده را ثبت کنید.", link: "/procurement/orders/" + s.OrderId)
                            .Fact("order", s.OrderId).Fact("cited", s.Line.DrawingRevision).Fact("current", s.CurrentRevision)).ToList();
                });
        }
    }
}
