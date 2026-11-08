using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.EngChange;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.EngChangeDtos;

namespace Services.NasbNiro.EngChange
{
    /// <summary>The verdict of the effectivity rules for one production order.</summary>
    public sealed class EcOrderVerdict
    {
        /// <summary>The Persian reason the order cannot be released, or null.</summary>
        public string Problem { get; set; }

        /// <summary>The order is before the effectivity point of an order that changes its routing: the older routing revision it was drafted from is right and stays.</summary>
        public bool StaleRoutingAllowed { get; set; }

        /// <summary>The orders (numbers) that decide the case, for the message.</summary>
        public List<string> Orders { get; set; } = new List<string>();
    }

    /// <summary>
    /// The gates of Package A2: releasing a new BOM revision, approving a new OPC revision, releasing a new drawing revision of a part or approving a new MES routing revision of a part that already has one needs an APPROVED
    /// engineering change order that names that revision (a first revision needs none); making a BOM obsolete needs an approved order and an in-use check; and a production order is judged against the effectivity point of
    /// the approved orders that change its part.
    /// </summary>
    public static class EcGate
    {
        public static bool IsFinal(EcoStatus s) => s == EcoStatus.Approved || s == EcoStatus.Closed;

        /// <summary>The approved (or closed) order that names this revision with this action, or null.</summary>
        public static async Task<EcOrder> ApprovedOrderForAsync(IUnitOfWork uow, EcChangeKind kind, int targetId, EcChangeAction action, CancellationToken ct)
        {
            int orderId = await (from c in uow.Set<EcOrderChange>().AsNoTracking()
                                 join o in uow.Set<EcOrder>().AsNoTracking() on c.OrderId equals o.Id
                                 where !o.Deleted && (o.Status == EcoStatus.Approved || o.Status == EcoStatus.Closed) && c.Kind == kind && c.TargetId == targetId && c.Action == action
                                 orderby o.Id descending
                                 select o.Id).FirstOrDefaultAsync(ct);
            return orderId == 0 ? null : await uow.Set<EcOrder>().AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        }

        private static string Needed(string what, string where) =>
            $"{what} نسخه جدیدی از چیزی است که قبلاً منتشر شده است؛ برای آن دستور تغییر مهندسی تاییدشده (ECO) لازم است که همین نسخه را نام برده باشد (تایید مهندسی و تضمین کیفیت){where}. ابتدا درخواست تغییر (ECR) و دستور تغییر (ECO) را از بخش «تغییرات مهندسی» بگذرانید.";

        // ------------------------------------------------------------------------------------------ BOM

        public static async Task<string> BomReleaseProblemAsync(IUnitOfWork uow, PbBomHeader h, CancellationToken ct)
        {
            bool earlier = await uow.Set<PbBomHeader>().AsNoTracking().AnyAsync(x => !x.Deleted && x.Code == h.Code && x.Id != h.Id && x.Revision < h.Revision
                && (x.Status == PbBomStatus.Released || x.Status == PbBomStatus.Superseded || x.Status == PbBomStatus.Obsolete), ct);
            if (!earlier) return null;
            return await ApprovedOrderForAsync(uow, EcChangeKind.Bom, h.Id, EcChangeAction.NewRevision, ct) != null ? null : Needed($"انتشار BOM {h.Code} بازنگری {h.Revision:00}", "");
        }

        /// <summary>Making a released BOM obsolete: an approved order that names it, and no open order, stock or project baseline left without a disposition.</summary>
        public static async Task<string> BomObsoleteProblemAsync(IUnitOfWork uow, PbBomHeader h, CancellationToken ct)
        {
            EcOrder eco = await ApprovedOrderForAsync(uow, EcChangeKind.Bom, h.Id, EcChangeAction.Obsolete, ct);
            if (eco == null)
                return $"منسوخ کردن BOM {h.Code} بازنگری {h.Revision:00} دستور تغییر مهندسی تاییدشده (ECO) می‌خواهد که همین BOM را «منسوخ» نام برده باشد، و برای هر دستور تولید باز، موجودی و پایه پروژه‌ای که از آن استفاده می‌کند تکلیف مشخص شده باشد.";
            var input = new EcImpactInput { PartNumbers = new List<string> { h.AssemblyPartNumber }, BomHeaderIds = new List<int> { h.Id } };
            EcImpactBuilder.Snapshot live = await EcImpactBuilder.ComputeAsync(uow, input, ct);
            List<int> baselineIds = live.Items.Where(i => i.Category == EcImpactCategory.Baseline && i.RefId.HasValue).Select(i => i.RefId.Value).ToList();
            HashSet<int> onThis = baselineIds.Count == 0 ? new HashSet<int>() : (await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => baselineIds.Contains(b.Id) && b.BomHeaderId == h.Id).Select(b => b.Id).ToListAsync(ct)).ToHashSet();
            List<EcImpactItemDto> inUse = live.Items.Where(i => i.Category == EcImpactCategory.Order || i.Category == EcImpactCategory.Stock || (i.Category == EcImpactCategory.Baseline && i.RefId.HasValue && onThis.Contains(i.RefId.Value))).ToList();
            HashSet<string> decided = (await uow.Set<EcDispositionItem>().AsNoTracking().Where(d => d.OrderId == eco.Id && !d.Stale && d.Disposition != null).Select(d => d.ImpactKey).ToListAsync(ct)).ToHashSet();
            List<EcImpactItemDto> missing = inUse.Where(i => !decided.Contains(i.Key)).ToList();
            if (missing.Count == 0) return null;
            return $"BOM {h.Code} هنوز در حال استفاده است و در {eco.Number} برای آن تکلیف نیامده است: " + string.Join("؛ ", missing.Take(5).Select(i => i.Text)) + (missing.Count > 5 ? $" و {missing.Count - 5} مورد دیگر" : "") + ". تحلیل اثر دستور را تازه کنید و برای همه تکلیف بگذارید.";
        }

        // ------------------------------------------------------------------------------------------ OPC and MES routing

        public static async Task<string> OpcApproveProblemAsync(IUnitOfWork uow, PbOpcRouting r, CancellationToken ct)
        {
            bool earlier = await uow.Set<PbOpcRouting>().AsNoTracking().AnyAsync(x => !x.Deleted && x.PartNumber == r.PartNumber && x.Id != r.Id && x.Revision < r.Revision
                && (x.Status == PbRoutingStatus.Approved || x.Status == PbRoutingStatus.Superseded), ct);
            if (!earlier) return null;
            return await ApprovedOrderForAsync(uow, EcChangeKind.Opc, r.Id, EcChangeAction.NewRevision, ct) != null ? null : Needed($"تایید چارت OPC {r.PartNumber} بازنگری {r.Revision:00}", "");
        }

        public const string OpcNoteTag = "از چارت OPC";

        /// <summary>A MES routing revision of a part that already has an approved one. A revision published from an OPC chart is covered by the gate of the chart itself.</summary>
        public static async Task<string> RoutingRevisionProblemAsync(IUnitOfWork uow, MfRoutingRevision r, CancellationToken ct)
        {
            if (!r.ItemId.HasValue || (r.Note ?? "").StartsWith(OpcNoteTag, StringComparison.Ordinal)) return null;
            string pn = await uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == r.ItemId.Value).Select(i => i.PartNumber).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(pn)) return null;
            bool earlier = await uow.Set<MfRoutingRevision>().AsNoTracking().AnyAsync(x => !x.Deleted && x.ItemId == r.ItemId && x.Id != r.Id && x.Revision < r.Revision && x.Status == MfRoutingStatus.Approved, ct);
            if (!earlier) return null;
            return await ApprovedOrderForAsync(uow, EcChangeKind.Routing, r.Id, EcChangeAction.NewRevision, ct) != null ? null : Needed($"تایید مسیر ساخت کالای {pn} بازنگری {r.Revision}", "");
        }

        // ------------------------------------------------------------------------------------------ drawings

        /// <summary>A drawing (a document linked to a part) that already has a released revision.</summary>
        public static async Task<string> DrawingReleaseProblemAsync(IUnitOfWork uow, PcEngineeringDeliverable d, PcDocumentRevision r, CancellationToken ct)
        {
            bool earlier = await uow.Set<PcDocumentRevision>().AsNoTracking().AnyAsync(x => !x.Deleted && x.DeliverableId == d.Id && x.Id != r.Id && x.Sequence < r.Sequence
                && (x.Status == PcDocStatus.Released || x.Status == PcDocStatus.AsBuilt || x.Status == PcDocStatus.Superseded), ct);
            if (!earlier) return null;
            bool ofPart = await uow.Set<PcDocumentLink>().AsNoTracking().AnyAsync(l => !l.Deleted && l.UnlinkReason == null && l.DeliverableId == d.Id
                && (l.Kind == PcDocLinkKind.Item || l.Kind == PcDocLinkKind.BomLine || l.Kind == PcDocLinkKind.PartNumber), ct);
            if (!ofPart) return null;
            return await ApprovedOrderForAsync(uow, EcChangeKind.Drawing, r.Id, EcChangeAction.NewRevision, ct) != null ? null : Needed($"انتشار نقشه {d.DocumentNumber} بازنگری {r.Code}", "");
        }

        // ------------------------------------------------------------------------------------------ effectivity of a production order

        private static List<string> Serials(string planned) =>
            (planned ?? "").Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

        /// <summary>The approved orders that change the part of an item and state an effectivity.</summary>
        public static async Task<List<EcOrder>> OrdersChangingPartAsync(IUnitOfWork uow, string partNumber, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(partNumber)) return new List<EcOrder>();
            string needle = "," + partNumber.Trim().ToUpperInvariant() + ",";
            return await uow.Set<EcOrder>().AsNoTracking().Where(o => !o.Deleted && (o.Status == EcoStatus.Approved || o.Status == EcoStatus.Closed) && o.AffectedParts != null && o.AffectedParts.Contains(needle)).OrderBy(o => o.Id).ToListAsync(ct);
        }

        /// <summary>
        /// Judges a production order against the effectivity of every approved order that changes its part: before the point the order keeps the old revision (the routing it was drafted from stays);
        /// at or after it the order must stand on the new BOM revision (its project baseline) and the new routing, unless the order says «complete on old revision» for the order or for its project baseline.
        /// </summary>
        public static async Task<EcOrderVerdict> OrderVerdictAsync(IUnitOfWork uow, MfProductionOrder order, InventoryItem item, DateTime today, CancellationToken ct)
        {
            var verdict = new EcOrderVerdict();
            if (item == null || string.IsNullOrWhiteSpace(item.PartNumber)) return verdict;
            List<EcOrder> ecos = await OrdersChangingPartAsync(uow, item.PartNumber, ct);
            if (ecos.Count == 0) return verdict;
            List<int> ids = ecos.Select(e => e.Id).ToList();
            List<EcOrderChange> changes = await uow.Set<EcOrderChange>().AsNoTracking().Where(c => ids.Contains(c.OrderId) && c.Action == EcChangeAction.NewRevision && c.Kind != EcChangeKind.Drawing).ToListAsync(ct);
            List<EcDispositionItem> dispositions = await uow.Set<EcDispositionItem>().AsNoTracking().Where(d => ids.Contains(d.OrderId) && !d.Stale && d.Disposition != null).ToListAsync(ct);
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent && b.WorkOrderId == order.WorkOrderId).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            List<string> serials = Serials(order.SerialsPlanned);
            var problems = new List<string>();
            foreach (EcOrder eco in ecos)
            {
                List<EcOrderChange> mine = changes.Where(c => c.OrderId == eco.Id).ToList();
                if (mine.Count == 0) continue;
                EcPosition pos = EcRules.Position(eco, order.Number, serials, today);
                if (pos == EcPosition.NotStated) continue;
                if (pos == EcPosition.Before) { verdict.StaleRoutingAllowed = true; continue; }
                if (pos == EcPosition.Straddle)
                {
                    problems.Add($"سریال‌های برنامه‌ریزی‌شده این دستور در دو سوی نقطه اجرای {eco.Number} ({EcRules.EffectivityText(eco)}) قرار دارند؛ دستور را به دو دستور تقسیم کنید");
                    verdict.Orders.Add(eco.Number);
                    continue;
                }
                // at or after the point
                bool Decided(string key, EcDisposition d) => dispositions.Any(x => x.OrderId == eco.Id && x.ImpactKey == key && x.Disposition == d);
                if (Decided(EcRules.Key("ORDER", order.Id), EcDisposition.CompleteOnOldRevision)) continue;
                foreach (EcOrderChange c in mine.Where(c => c.Kind == EcChangeKind.Bom))
                {
                    if (baseline == null || baseline.SourceBomCode != c.Code || !c.NewRevision.HasValue || baseline.SourceBomRevision >= c.NewRevision.Value) continue;
                    if (Decided(EcRules.Key("BASELINE", baseline.Id), EcDisposition.CompleteOnOldRevision)) continue;
                    problems.Add($"پایه پروژه این دستور روی BOM {c.Code} بازنگری {baseline.SourceBomRevision:00} است و از نقطه اجرای {eco.Number} ({EcRules.EffectivityText(eco)}) بازنگری {c.NewRevision.Value:00} لازم است؛ پایه پروژه را به بازنگری جدید ببرید یا در ECO برای این دستور تصمیم «تکمیل با بازنگری قدیم» ثبت شود");
                    verdict.Orders.Add(eco.Number);
                }
                foreach (EcOrderChange c in mine.Where(c => c.Kind == EcChangeKind.Opc))
                {
                    bool columnOfPart = await uow.Set<PbOpcStep>().AsNoTracking().AnyAsync(s => !s.Deleted && s.RoutingId == c.TargetId && s.PartNumber == item.PartNumber, ct);
                    if (!columnOfPart) continue;
                    string tag = $"{OpcNoteTag} {c.Code} نسخه {(c.NewRevision ?? 0):00}";
                    MfRoutingRevision published = (await uow.Set<MfRoutingRevision>().AsNoTracking().Where(r => !r.Deleted && r.ItemId == item.Id && r.Note != null && r.Note.Contains(tag)).OrderByDescending(r => r.Id).ToListAsync(ct)).FirstOrDefault();
                    if (published != null && published.Status == MfRoutingStatus.Approved && order.RoutingRevisionId == published.Id) continue;
                    problems.Add($"از نقطه اجرای {eco.Number} ({EcRules.EffectivityText(eco)}) دستور باید با مسیر ساخت چارت OPC {c.Code} بازنگری {(c.NewRevision ?? 0):00} ساخته شود" + (published == null ? " و این چارت هنوز به مسیر ساخت (MES) منتقل نشده است" : published.Status != MfRoutingStatus.Approved ? " و مسیر ساخت آن هنوز دو تایید MES را نگرفته است" : "؛ «به‌روزرسانی مسیر» را بزنید"));
                    verdict.Orders.Add(eco.Number);
                }
                foreach (EcOrderChange c in mine.Where(c => c.Kind == EcChangeKind.Routing))
                {
                    MfRoutingRevision target = await uow.Set<MfRoutingRevision>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == c.TargetId, ct);
                    if (target == null || target.ItemId != item.Id) continue;
                    if (target.Status == MfRoutingStatus.Approved && order.RoutingRevisionId == target.Id) continue;
                    problems.Add($"از نقطه اجرای {eco.Number} ({EcRules.EffectivityText(eco)}) دستور باید با مسیر ساخت بازنگری {target.Revision} ساخته شود" + (target.Status != MfRoutingStatus.Approved ? " که هنوز تایید نشده است" : "؛ «به‌روزرسانی مسیر» را بزنید"));
                    verdict.Orders.Add(eco.Number);
                }
            }
            if (problems.Count > 0) verdict.Problem = string.Join("؛ ", problems.Distinct());
            return verdict;
        }

        /// <summary>The «refresh routing» of a draft order that is still before the effectivity point of an order that changes its routing is refused: the order keeps the old revision.</summary>
        public static async Task<string> RefreshRoutingProblemAsync(IUnitOfWork uow, MfProductionOrder order, InventoryItem item, DateTime today, CancellationToken ct)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.PartNumber)) return null;
            List<EcOrder> ecos = await OrdersChangingPartAsync(uow, item.PartNumber, ct);
            if (ecos.Count == 0) return null;
            List<int> ids = ecos.Select(e => e.Id).ToList();
            HashSet<int> withRouting = (await uow.Set<EcOrderChange>().AsNoTracking().Where(c => ids.Contains(c.OrderId) && c.Action == EcChangeAction.NewRevision && (c.Kind == EcChangeKind.Opc || c.Kind == EcChangeKind.Routing)).Select(c => c.OrderId).ToListAsync(ct)).ToHashSet();
            List<string> serials = Serials(order.SerialsPlanned);
            foreach (EcOrder eco in ecos.Where(e => withRouting.Contains(e.Id)))
                if (EcRules.Position(eco, order.Number, serials, today) == EcPosition.Before)
                    return $"این دستور پیش از نقطه اجرای {eco.Number} ({EcRules.EffectivityText(eco)}) است و بازنگری قدیم مسیر ساخت را نگه می‌دارد؛ مسیر آن به‌روزرسانی نمی‌شود.";
            return null;
        }

        // ------------------------------------------------------------------------------------------ change coverage of components

        /// <summary>True when an approved order that changes the part of the item exists (a change of a component of a draft order may follow an approved order).</summary>
        public static async Task<EcOrder> OrderCoveringItemAsync(IUnitOfWork uow, InventoryItem item, CancellationToken ct)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.PartNumber)) return null;
            return (await OrdersChangingPartAsync(uow, item.PartNumber, ct)).FirstOrDefault();
        }
    }
}
