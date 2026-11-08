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

        /// <summary>The Persian refusal when an order that is in approval or approved names this revision (a named draft revision is frozen with the approval; cancel / void the order to edit it), else null.</summary>
        public static async Task<string> FrozenByOrderAsync(IUnitOfWork uow, EcChangeKind kind, int targetId, CancellationToken ct)
        {
            string number = await (from c in uow.Set<EcOrderChange>().AsNoTracking()
                                   join o in uow.Set<EcOrder>().AsNoTracking() on c.OrderId equals o.Id
                                   where !o.Deleted && (o.Status == EcoStatus.InApproval || o.Status == EcoStatus.Approved) && c.Kind == kind && c.TargetId == targetId && c.Action == EcChangeAction.NewRevision
                                   orderby o.Id descending
                                   select o.Number).FirstOrDefaultAsync(ct);
            return number == null ? null : $"این بازنگری در دستور تغییر مهندسی {number} نام برده شده و با تایید یا ارسال آن قفل است؛ برای ویرایش، دستور را برگردانید یا باطل کنید.";
        }

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

        /// <summary>
        /// A MES routing revision of an item (or item group) that already has an approved one. The exemption is a structural fact, never a note: the first approved routing of the item / group needs no order, and a
        /// revision the server wrote from an approved OPC chart (SourceOpcRoutingId, set only by the OPC publication) is covered by the gate of the chart itself.
        /// </summary>
        public static async Task<string> RoutingRevisionProblemAsync(IUnitOfWork uow, MfRoutingRevision r, CancellationToken ct)
        {
            if (!r.ItemId.HasValue && !r.ItemGroupId.HasValue) return null;
            if (r.SourceOpcRoutingId.HasValue && await uow.Set<PbOpcRouting>().AsNoTracking().AnyAsync(o => o.Id == r.SourceOpcRoutingId.Value && !o.Deleted && (o.Status == PbRoutingStatus.Approved || o.Status == PbRoutingStatus.Superseded), ct)) return null;
            IQueryable<MfRoutingRevision> same = uow.Set<MfRoutingRevision>().AsNoTracking().Where(x => !x.Deleted && x.Id != r.Id && x.Revision < r.Revision && x.Status == MfRoutingStatus.Approved);
            same = r.ItemId.HasValue ? same.Where(x => x.ItemId == r.ItemId) : same.Where(x => x.ItemGroupId == r.ItemGroupId && x.ItemId == null);
            if (!await same.AnyAsync(ct)) return null;
            string what;
            if (r.ItemId.HasValue)
            {
                string pn = await uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == r.ItemId.Value).Select(i => i.Code).FirstOrDefaultAsync(ct);
                what = $"تایید مسیر ساخت کالای {pn} بازنگری {r.Revision}";
            }
            else
            {
                string group = await uow.Set<ItemGroup>().AsNoTracking().Where(g => g.Id == r.ItemGroupId.Value).Select(g => g.Title).FirstOrDefaultAsync(ct);
                what = $"تایید مسیر ساخت گروه کالای {group} بازنگری {r.Revision}";
            }
            return await ApprovedOrderForAsync(uow, EcChangeKind.Routing, r.Id, EcChangeAction.NewRevision, ct) != null ? null : Needed(what, "");
        }

        // ------------------------------------------------------------------------------------------ drawings

        private static readonly PcDocStatus[] EverReleased = { PcDocStatus.Released, PcDocStatus.AsBuilt, PcDocStatus.Superseded, PcDocStatus.Obsolete };

        /// <summary>The parts a drawing is (or ever was) linked to; the link history counts, so unlinking and relinking does not reset the gate.</summary>
        public static async Task<(List<string> Parts, List<int> ItemIds)> DrawingPartsAsync(IUnitOfWork uow, int deliverableId, bool history, CancellationToken ct)
        {
            var q = uow.Set<PcDocumentLink>().AsNoTracking().Where(l => l.DeliverableId == deliverableId && (l.Kind == PcDocLinkKind.Item || l.Kind == PcDocLinkKind.BomLine || l.Kind == PcDocLinkKind.PartNumber));
            if (!history) q = q.Where(l => !l.Deleted && l.UnlinkReason == null);
            var rows = await q.Select(l => new { l.PartNumber, l.ItemId }).ToListAsync(ct);
            List<int> itemIds = rows.Where(r => r.ItemId.HasValue).Select(r => r.ItemId.Value).Distinct().ToList();
            List<string> parts = rows.Where(r => !string.IsNullOrWhiteSpace(r.PartNumber)).Select(r => r.PartNumber.Trim()).ToList();
            if (itemIds.Count > 0) parts.AddRange(await uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id) && i.PartNumber != null).Select(i => i.PartNumber).ToListAsync(ct));
            return (parts.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), itemIds);
        }

        /// <summary>The latest earlier revision, of ANY status that was ever in force (released, as-built, superseded, obsolete), of this drawing or of any other document ever linked to the same parts; null = this is the first.</summary>
        public static async Task<PcDocumentRevision> EarlierDrawingRevisionAsync(IUnitOfWork uow, PcEngineeringDeliverable d, PcDocumentRevision r, CancellationToken ct)
        {
            var (parts, itemIds) = await DrawingPartsAsync(uow, d.Id, true, ct);
            if (parts.Count == 0 && itemIds.Count == 0) return null;
            List<int> sameParts = await uow.Set<PcDocumentLink>().AsNoTracking()
                .Where(l => l.DeliverableId != null && (l.Kind == PcDocLinkKind.Item || l.Kind == PcDocLinkKind.BomLine || l.Kind == PcDocLinkKind.PartNumber)
                    && ((l.ItemId != null && itemIds.Contains(l.ItemId.Value)) || (l.PartNumber != null && parts.Contains(l.PartNumber)))).Select(l => l.DeliverableId.Value).Distinct().ToListAsync(ct);
            sameParts.Add(d.Id);
            return await uow.Set<PcDocumentRevision>().AsNoTracking()
                .Where(x => !x.Deleted && sameParts.Contains(x.DeliverableId) && x.Id != r.Id && EverReleased.Contains(x.Status) && (x.DeliverableId != d.Id || x.Sequence < r.Sequence))
                .OrderByDescending(x => x.DeliverableId == d.Id).ThenByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        }

        /// <summary>A drawing (a document linked, now or ever, to a part) that already has a revision that was ever in force: the new revision needs an approved order that names it. A revision received from the client is judged the same way.</summary>
        public static async Task<string> DrawingReleaseProblemAsync(IUnitOfWork uow, PcEngineeringDeliverable d, PcDocumentRevision r, CancellationToken ct)
        {
            if (await EarlierDrawingRevisionAsync(uow, d, r, ct) == null) return null;
            return await ApprovedOrderForAsync(uow, EcChangeKind.Drawing, r.Id, EcChangeAction.NewRevision, ct) != null ? null : Needed($"انتشار نقشه {d.DocumentNumber} بازنگری {r.Code}", "");
        }

        /// <summary>
        /// A new version of the characteristic list of a drawing revision that is already in force is released at its approval: that needs an approved order that names the list. The first version of a revision needs none.
        /// </summary>
        public static async Task<string> CharListVersionProblemAsync(IUnitOfWork uow, int listId, int revisionId, int version, CancellationToken ct)
        {
            if (version <= 1) return null;
            var q = await uow.Set<Entities.Model.NasbNiro.Quality.QcCharacteristicList>().AsNoTracking().Where(x => !x.Deleted && x.RevisionId == revisionId && x.Id != listId && x.Version < version
                && (x.Status == Entities.Model.NasbNiro.Quality.CharListStatus.Released || x.Status == Entities.Model.NasbNiro.Quality.CharListStatus.Superseded)).AnyAsync(ct);
            if (!q) return null;
            return await ApprovedOrderForAsync(uow, EcChangeKind.CharList, listId, EcChangeAction.NewRevision, ct) != null ? null : Needed($"تایید نسخه {version} فهرست مشخصه‌های نقشه‌ای که منتشر شده", "");
        }

        // ------------------------------------------------------------------------------------------ effectivity of a production order

        private static List<string> Serials(string planned) =>
            (planned ?? "").Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

        /// <summary>The approved orders that change the part of an item (or a part it contains) and state an effectivity.</summary>
        public static async Task<List<EcOrder>> OrdersChangingPartAsync(IUnitOfWork uow, string partNumber, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(partNumber)) return new List<EcOrder>();
            string needle = "," + partNumber.Trim().ToUpperInvariant() + ",";
            return await uow.Set<EcOrder>().AsNoTracking().Where(o => !o.Deleted && (o.Status == EcoStatus.Approved || o.Status == EcoStatus.Closed) && o.AffectedParts != null && o.AffectedParts.Contains(needle)).OrderBy(o => o.Id).ToListAsync(ct);
        }

        /// <summary>The approved orders that list this production order with «change to the new revision» or «rework» and name a new BOM revision of the part: (order id, BOM header id of the new revision).</summary>
        public static async Task<List<(int OrderId, int BomHeaderId)>> ApprovedBomChangesForOrderAsync(IUnitOfWork uow, int productionOrderId, string impactKey, string partNumber, CancellationToken ct)
        {
            var rows = await (from d in uow.Set<EcDispositionItem>().AsNoTracking()
                              join o in uow.Set<EcOrder>().AsNoTracking() on d.OrderId equals o.Id
                              join c in uow.Set<EcOrderChange>().AsNoTracking() on o.Id equals c.OrderId
                              where !o.Deleted && o.Status == EcoStatus.Approved && !d.Stale && d.ImpactKey == impactKey
                                  && (d.Disposition == EcDisposition.ChangeToNewRevision || d.Disposition == EcDisposition.Rework)
                                  && c.Kind == EcChangeKind.Bom && c.Action == EcChangeAction.NewRevision && c.PartNumber == partNumber
                              orderby o.Id
                              select new { o.Id, c.TargetId }).ToListAsync(ct);
            return rows.Select(r => (r.Id, r.TargetId)).ToList();
        }

        /// <summary>The revision of the BOM with this code that the project baseline stands on: the top BOM, or the BOM of a sub-assembly the baseline lines were copied from; null when the baseline has no line from it.</summary>
        private static async Task<int?> BaselineRevisionOfBomAsync(IUnitOfWork uow, PbProjectBaseline baseline, string code, CancellationToken ct)
        {
            if (baseline == null || string.IsNullOrWhiteSpace(code)) return null;
            if (baseline.SourceBomCode == code) return baseline.SourceBomRevision;
            int? rev = await (from l in uow.Set<PbProjectBomLine>().AsNoTracking()
                              join s in uow.Set<PbBomLine>().AsNoTracking() on l.SourceLineId equals s.Id
                              join h in uow.Set<PbBomHeader>().AsNoTracking() on s.BomHeaderId equals h.Id
                              where l.BaselineId == baseline.Id && !l.Deleted && !l.IsRemoved && h.Code == code
                              select (int?)h.Revision).MinAsync(ct);
            return rev;
        }

        /// <summary>
        /// Judges a production order against the effectivity of every approved order that changes its part (or a part below it): before the point the order keeps the old revision (the routing it was drafted from stays);
        /// at or after it the order must stand on the new BOM revision (its project baseline, sub-assemblies included) and the new routing, unless the order says «complete on old revision» for the order or for its project baseline.
        /// An order whose position cannot be told (a serial-only point and no planned serials) is refused: the serials must be planned first.
        /// </summary>
        public static async Task<EcOrderVerdict> OrderVerdictAsync(IUnitOfWork uow, MfProductionOrder order, InventoryItem item, DateTime today, CancellationToken ct)
        {
            var verdict = new EcOrderVerdict();
            if (item == null || string.IsNullOrWhiteSpace(item.PartNumber)) return verdict;
            List<EcOrder> ecos = await OrdersChangingPartAsync(uow, item.PartNumber, ct);
            if (ecos.Count == 0) return verdict;
            List<int> ids = ecos.Select(e => e.Id).ToList();
            List<EcOrderChange> changes = await uow.Set<EcOrderChange>().AsNoTracking().Where(c => ids.Contains(c.OrderId) && c.Action == EcChangeAction.NewRevision && c.Kind != EcChangeKind.Drawing && c.Kind != EcChangeKind.CharList).ToListAsync(ct);
            List<EcDispositionItem> dispositions = await uow.Set<EcDispositionItem>().AsNoTracking().Where(d => ids.Contains(d.OrderId) && !d.Stale && d.Disposition != null).ToListAsync(ct);
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent && b.WorkOrderId == order.WorkOrderId).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            List<string> serials = Serials(order.SerialsPlanned);
            var problems = new List<string>();
            foreach (EcOrder eco in ecos)
            {
                List<EcOrderChange> mine = changes.Where(c => c.OrderId == eco.Id).ToList();
                if (mine.Count == 0) continue;
                // which of the changes concern this order at all
                var bomRelevant = new List<(EcOrderChange C, int OldRevision)>();
                foreach (EcOrderChange c in mine.Where(c => c.Kind == EcChangeKind.Bom))
                {
                    int? has = await BaselineRevisionOfBomAsync(uow, baseline, c.Code, ct);
                    if (has.HasValue && c.NewRevision.HasValue && has.Value < c.NewRevision.Value) bomRelevant.Add((c, has.Value));
                }
                var opcRelevant = new List<EcOrderChange>();
                foreach (EcOrderChange c in mine.Where(c => c.Kind == EcChangeKind.Opc))
                    if (await uow.Set<PbOpcStep>().AsNoTracking().AnyAsync(s => !s.Deleted && s.RoutingId == c.TargetId && s.PartNumber == item.PartNumber, ct)) opcRelevant.Add(c);
                var routingRelevant = new List<(EcOrderChange C, MfRoutingRevision Target)>();
                foreach (EcOrderChange c in mine.Where(c => c.Kind == EcChangeKind.Routing))
                {
                    MfRoutingRevision target = await uow.Set<MfRoutingRevision>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == c.TargetId, ct);
                    if (target != null && target.ItemId == item.Id) routingRelevant.Add((c, target));
                }
                if (bomRelevant.Count == 0 && opcRelevant.Count == 0 && routingRelevant.Count == 0) continue;

                EcPosition pos = EcRules.Position(eco, order.Number, serials, today);
                if (pos == EcPosition.Before) { verdict.StaleRoutingAllowed = true; continue; }
                if (pos == EcPosition.NotStated)
                {
                    problems.Add($"نقطه اجرای {eco.Number} ({EcRules.EffectivityText(eco)}) با این دستور سنجیده نمی‌شود (دستور سریال برنامه‌ریزی‌شده یا شماره هم‌ردیف ندارد)؛ سریال‌های دستور را برنامه‌ریزی کنید");
                    verdict.Orders.Add(eco.Number);
                    continue;
                }
                if (pos == EcPosition.Straddle)
                {
                    problems.Add($"سریال‌های برنامه‌ریزی‌شده این دستور در دو سوی نقطه اجرای {eco.Number} ({EcRules.EffectivityText(eco)}) قرار دارند؛ دستور را به دو دستور تقسیم کنید");
                    verdict.Orders.Add(eco.Number);
                    continue;
                }
                // at or after the point
                bool Decided(string key, EcDisposition d) => dispositions.Any(x => x.OrderId == eco.Id && x.ImpactKey == key && x.Disposition == d);
                if (Decided(EcRules.Key("ORDER", order.Id), EcDisposition.CompleteOnOldRevision)) continue;
                foreach (var b in bomRelevant)
                {
                    if (baseline != null && Decided(EcRules.Key("BASELINE", baseline.Id), EcDisposition.CompleteOnOldRevision)) continue;
                    problems.Add($"پایه پروژه این دستور روی BOM {b.C.Code} بازنگری {b.OldRevision:00} است و از نقطه اجرای {eco.Number} ({EcRules.EffectivityText(eco)}) بازنگری {b.C.NewRevision.Value:00} لازم است؛ پایه پروژه را به بازنگری جدید ببرید (یا دستور را «تکمیل با بازنگری قدیم» کنید)");
                    verdict.Orders.Add(eco.Number);
                }
                foreach (EcOrderChange c in opcRelevant)
                {
                    MfRoutingRevision published = await uow.Set<MfRoutingRevision>().AsNoTracking().Where(r => !r.Deleted && r.ItemId == item.Id && r.SourceOpcRoutingId == c.TargetId).OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
                    if (published != null && published.Status == MfRoutingStatus.Approved && order.RoutingRevisionId == published.Id) continue;
                    problems.Add($"از نقطه اجرای {eco.Number} ({EcRules.EffectivityText(eco)}) دستور باید با مسیر ساخت چارت OPC {c.Code} بازنگری {(c.NewRevision ?? 0):00} ساخته شود" + (published == null ? " و این چارت هنوز به مسیر ساخت (MES) منتقل نشده است" : published.Status != MfRoutingStatus.Approved ? " که مسیر ساخت آن هنوز تایید نشده است" : "؛ «به‌روزرسانی مسیر ساخت» دستور را بزنید"));
                    verdict.Orders.Add(eco.Number);
                }
                foreach (var rr in routingRelevant)
                {
                    if (rr.Target.Status == MfRoutingStatus.Approved && order.RoutingRevisionId == rr.Target.Id) continue;
                    problems.Add($"از نقطه اجرای {eco.Number} ({EcRules.EffectivityText(eco)}) دستور باید با مسیر ساخت بازنگری {rr.Target.Revision} ساخته شود" + (rr.Target.Status != MfRoutingStatus.Approved ? " که هنوز تایید نشده است" : "؛ «به‌روزرسانی مسیر ساخت» دستور را بزنید"));
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

        /// <summary>
        /// The planned serials of an order whose part is changed by an approved order with a serial point cannot be rewritten so that the order moves across the point (or straddles it): the order would silently take
        /// the other revision. Setting serials on an order that had none is allowed.
        /// </summary>
        public static async Task<string> SerialsChangeProblemAsync(IUnitOfWork uow, MfProductionOrder order, InventoryItem item, string newPlanned, DateTime today, CancellationToken ct)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.PartNumber)) return null;
            List<string> oldSerials = Serials(order.SerialsPlanned), newSerials = Serials(newPlanned);
            if (oldSerials.OrderBy(x => x).SequenceEqual(newSerials.OrderBy(x => x))) return null;
            foreach (EcOrder eco in (await OrdersChangingPartAsync(uow, item.PartNumber, ct)).Where(e => !string.IsNullOrWhiteSpace(e.EffectiveFromSerial)))
            {
                EcPosition was = EcRules.Position(eco, order.Number, oldSerials, today), now = EcRules.Position(eco, order.Number, newSerials, today);
                if (now == EcPosition.Straddle || (was != EcPosition.NotStated && was != now))
                    return $"سریال‌های برنامه‌ریزی‌شده دستور را نمی‌توان طوری عوض کرد که دستور از نقطه اجرای {eco.Number} ({EcRules.EffectivityText(eco)}) جابه‌جا شود یا دو سوی آن بیفتد؛ برای تقسیم یا انتقال سریال‌ها دستور تغییر مهندسی لازم است.";
            }
            return null;
        }
    }
}
