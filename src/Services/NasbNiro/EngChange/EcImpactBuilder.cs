using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.EngChange;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.EngChangeDtos;

namespace Services.NasbNiro.EngChange
{
    /// <summary>What the impact analysis starts from: the part numbers that change and the revisions that are being changed.</summary>
    public sealed class EcImpactInput
    {
        public List<string> PartNumbers { get; set; } = new List<string>();

        /// <summary>BOM headers (any revision) that the order changes: the project baselines on any revision of the same BOM are impacted, and the BOM itself is not listed as a user of its own parts.</summary>
        public List<int> BomHeaderIds { get; set; } = new List<int>();
        public List<int> OpcRoutingIds { get; set; } = new List<int>();
        public List<int> DeliverableIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// The computed impact analysis of an engineering change (Package A2): where-used over the product BOMs at all levels, project baselines on the old revision, open and released MES orders with their routings,
    /// stock and work in progress by warehouse, open purchase requests and orders, the QC points and characteristic lists, the special-process steps and the delivered serials. Every list is built from the registers;
    /// nothing is typed. The rows are sorted, so the same data gives the same JSON and the same SHA-256.
    /// </summary>
    public static class EcImpactBuilder
    {
        /// <summary>★ A list longer than this is cut and flagged: an order with a cut list cannot be approved (narrow the request).</summary>
        public const int MaxPerCategory = 1000;

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public sealed class Snapshot
        {
            /// <summary>When it was computed. NOT part of the JSON and the hash (the same data must give the same fingerprint); the order keeps it in ImpactAt.</summary>
            [System.Text.Json.Serialization.JsonIgnore]
            public DateTimeOffset ComputedAt { get; set; }
            public List<string> Parts { get; set; } = new List<string>();
            public List<EcImpactItemDto> Items { get; set; } = new List<EcImpactItemDto>();
            public List<int> Truncated { get; set; } = new List<int>();
        }

        /// <summary>Canonical form: lists sorted, quantities rounded to 4 decimals without trailing zeros, no volatile field. The same data always gives the same text and the same SHA-256.</summary>
        public static string Serialize(Snapshot s)
        {
            var canon = new Snapshot
            {
                Parts = s.Parts.OrderBy(p => p, StringComparer.Ordinal).ToList(),
                Truncated = s.Truncated.OrderBy(x => x).ToList(),
                Items = s.Items.OrderBy(i => (int)i.Category).ThenBy(i => i.Key, StringComparer.Ordinal).Select(i => new EcImpactItemDto
                {
                    Key = i.Key, Category = i.Category, Text = i.Text, PartNumber = i.PartNumber, RefId = i.RefId,
                    Quantity = i.Quantity.HasValue ? decimal.Round(i.Quantity.Value, 4) / 1.0000000000000000000000000000m : (decimal?)null,
                }).ToList(),
            };
            return JsonSerializer.Serialize(canon, Json);
        }

        public static Snapshot Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<Snapshot>(json, Json); }
            catch (JsonException) { return null; }
        }

        private static string Num(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        private static void Add(Snapshot s, EcImpactCategory category, string key, string text, string part, decimal? qty, int? refId)
        {
            if (s.Items.Any(x => x.Key == key)) return;
            if (s.Items.Count(x => x.Category == category) >= MaxPerCategory)
            {
                if (!s.Truncated.Contains((int)category)) s.Truncated.Add((int)category);
                return;
            }
            s.Items.Add(new EcImpactItemDto { Key = key, Category = category, Text = text, PartNumber = part, Quantity = qty, RefId = refId });
        }

        public static async Task<Snapshot> ComputeAsync(IUnitOfWork uow, EcImpactInput input, CancellationToken ct)
        {
            List<string> parts = (input?.PartNumbers ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().ToUpperInvariant()).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
            var snap = new Snapshot { ComputedAt = DateTimeOffset.Now, Parts = parts };
            if (parts.Count == 0) return snap;

            // the items of the parts: the part itself and every stage item (PartNumber is shared)
            var items = await uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.PartNumber != null && parts.Contains(i.PartNumber)).Select(i => new { i.Id, i.Code, i.Title, i.PartNumber, i.StageCode2 }).ToListAsync(ct);
            List<int> itemIds = items.Select(i => i.Id).ToList();
            string ItemText(int id) { var i = items.FirstOrDefault(x => x.Id == id); return i == null ? "#" + id : i.Code; }
            string PartOf(int id) => items.FirstOrDefault(x => x.Id == id)?.PartNumber;

            List<int> bomIds = (input.BomHeaderIds ?? new List<int>()).Distinct().ToList();
            var targetBoms = bomIds.Count == 0 ? new List<PbBomHeader>() : await uow.Set<PbBomHeader>().AsNoTracking().Where(h => bomIds.Contains(h.Id)).ToListAsync(ct);
            List<string> targetCodes = targetBoms.Select(b => b.Code).Distinct().ToList();

            await WhereUsedAsync(uow, snap, parts, targetCodes, ct);
            List<int> baselineWorkOrders = await BaselinesAsync(uow, snap, parts, targetCodes, ct);
            List<MfProductionOrder> orders = await OrdersAsync(uow, snap, itemIds, ItemText, PartOf, ct);
            await StockAsync(uow, snap, itemIds, ItemText, PartOf, ct);
            await ProcurementAsync(uow, snap, itemIds, ItemText, PartOf, ct);
            await QcAsync(uow, snap, parts, itemIds, input.DeliverableIds ?? new List<int>(), baselineWorkOrders.Concat(orders.Select(o => o.WorkOrderId)).Distinct().ToList(), ct);
            await SpecialAsync(uow, snap, itemIds, orders, ItemText, ct);
            await DeliveredAsync(uow, snap, itemIds, ItemText, PartOf, ct);

            snap.Items = snap.Items.OrderBy(i => (int)i.Category).ThenBy(i => i.Key, StringComparer.Ordinal).ToList();
            snap.Truncated.Sort();
            return snap;
        }

        // ------------------------------------------------------------------------------------------ 1. where-used (all levels)

        private static async Task WhereUsedAsync(IUnitOfWork uow, Snapshot snap, List<string> parts, List<string> targetCodes, CancellationToken ct)
        {
            var seenAssemblies = new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
            var seenHeaders = new HashSet<int>();
            List<string> frontier = parts.ToList();
            int level = 1;
            for (; level <= 8 && frontier.Count > 0; level++)
            {
                List<string> current = frontier;
                var rows = await (from l in uow.Set<PbBomLine>().AsNoTracking()
                                  join h in uow.Set<PbBomHeader>().AsNoTracking() on l.BomHeaderId equals h.Id
                                  where !l.Deleted && !h.Deleted && !l.IsHeading && h.Status != PbBomStatus.Obsolete && l.PartNumber != null && current.Contains(l.PartNumber)
                                  select new { h.Id, h.Code, h.Revision, h.Status, h.AssemblyPartNumber, l.Qty, l.PartNumber }).ToListAsync(ct);
                var next = new List<string>();
                foreach (var g in rows.GroupBy(r => r.Id).OrderBy(g => g.Key))
                {
                    var r = g.First();
                    if (!seenHeaders.Add(r.Id)) continue;
                    if (targetCodes.Contains(r.Code)) continue;   // the BOM that is being changed is not a user of its own parts
                    decimal? qty = level == 1 ? g.Sum(x => x.Qty) : (decimal?)null;
                    Add(snap, EcImpactCategory.WhereUsed, EcRules.Key("BOM", r.Id), $"BOM {r.Code} بازنگری {r.Revision:00} ({StatusText(r.Status)}) — سطح {level}، قطعه {string.Join("، ", g.Select(x => x.PartNumber).Distinct())}", r.AssemblyPartNumber, qty, r.Id);
                    if (!string.IsNullOrWhiteSpace(r.AssemblyPartNumber) && seenAssemblies.Add(r.AssemblyPartNumber)) next.Add(r.AssemblyPartNumber);
                }
                frontier = next;
            }
            // the walk stops at 8 levels: a deeper chain is flagged (the list is then incomplete and the order cannot be approved until the scope is narrowed)
            if (frontier.Count > 0 && !snap.Truncated.Contains((int)EcImpactCategory.WhereUsed)) snap.Truncated.Add((int)EcImpactCategory.WhereUsed);
        }

        public static string StatusText(PbBomStatus s)
        {
            switch (s)
            {
                case PbBomStatus.Draft: return "پیش‌نویس";
                case PbBomStatus.UnderReview: return "در بررسی";
                case PbBomStatus.Approved: return "تایید شده";
                case PbBomStatus.Released: return "منتشر شده";
                case PbBomStatus.Superseded: return "جایگزین شده";
                case PbBomStatus.Obsolete: return "منسوخ";
                default: return s.ToString();
            }
        }

        // ------------------------------------------------------------------------------------------ 2. project baselines

        private static async Task<List<int>> BaselinesAsync(IUnitOfWork uow, Snapshot snap, List<string> parts, List<string> targetCodes, CancellationToken ct)
        {
            var baselines = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent).ToListAsync(ct);
            var lineHits = await (from l in uow.Set<PbProjectBomLine>().AsNoTracking()
                                  where !l.Deleted && !l.IsRemoved && !l.IsHeading && l.PartNumber != null && parts.Contains(l.PartNumber)
                                  select new { l.BaselineId, l.PartNumber, l.TotalQty }).ToListAsync(ct);
            var hitByBaseline = lineHits.GroupBy(h => h.BaselineId).ToDictionary(g => g.Key, g => g.ToList());
            var woIds = baselines.Select(b => b.WorkOrderId).Distinct().ToList();
            Dictionary<int, string> wo = woIds.Count == 0 ? new Dictionary<int, string>() : (await uow.Set<WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).Select(w => new { w.Id, w.Code }).ToListAsync(ct)).ToDictionary(w => w.Id, w => w.Code);
            var used = new List<int>();
            foreach (PbProjectBaseline b in baselines.OrderBy(b => b.Id))
            {
                bool onChangedBom = targetCodes.Contains(b.SourceBomCode);
                bool hasPart = hitByBaseline.ContainsKey(b.Id);
                if (!onChangedBom && !hasPart) continue;
                var hits = hasPart ? hitByBaseline[b.Id] : null;
                string reason = onChangedBom && hasPart ? "روی بازنگری BOM و دارای قطعه" : onChangedBom ? "روی بازنگری BOM در حال تغییر" : "دارای قطعه متاثر";
                string text = $"پایه پروژه دستور کار {(wo.TryGetValue(b.WorkOrderId, out string code) ? code : "#" + b.WorkOrderId)} — BOM {b.SourceBomCode} بازنگری {b.SourceBomRevision:00} — {reason}";
                decimal? qty = hasPart ? hits.Sum(h => h.TotalQty) : (decimal?)null;
                Add(snap, EcImpactCategory.Baseline, EcRules.Key("BASELINE", b.Id), text, hasPart ? hits[0].PartNumber : null, qty, b.Id);
                used.Add(b.WorkOrderId);
            }
            return used;
        }

        // ------------------------------------------------------------------------------------------ 3. MES orders and routings

        private static async Task<List<MfProductionOrder>> OrdersAsync(IUnitOfWork uow, Snapshot snap, List<int> itemIds, Func<int, string> itemText, Func<int, string> partOf, CancellationToken ct)
        {
            if (itemIds.Count == 0) return new List<MfProductionOrder>();
            MfOrderStatus[] open = { MfOrderStatus.Draft, MfOrderStatus.Released, MfOrderStatus.InProgress };
            List<int> viaComponent = await uow.Set<MfOrderComponent>().AsNoTracking().Where(c => itemIds.Contains(c.ItemId)).Select(c => c.OrderId).Distinct().ToListAsync(ct);
            List<MfProductionOrder> orders = await uow.Set<MfProductionOrder>().AsNoTracking()
                .Where(o => !o.Deleted && open.Contains(o.Status) && ((o.ItemId != null && itemIds.Contains(o.ItemId.Value)) || viaComponent.Contains(o.Id))).OrderBy(o => o.Id).ToListAsync(ct);
            List<int> woIds = orders.Select(o => o.WorkOrderId).Distinct().ToList();
            Dictionary<int, string> wo = woIds.Count == 0 ? new Dictionary<int, string>() : (await uow.Set<WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).Select(w => new { w.Id, w.Code }).ToListAsync(ct)).ToDictionary(w => w.Id, w => w.Code);
            List<int> revIds = orders.Where(o => o.RoutingRevisionId.HasValue).Select(o => o.RoutingRevisionId.Value).Distinct().ToList();
            Dictionary<int, int> revNo = revIds.Count == 0 ? new Dictionary<int, int>() : (await uow.Set<MfRoutingRevision>().AsNoTracking().Where(r => revIds.Contains(r.Id)).Select(r => new { r.Id, r.Revision }).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.Revision);
            string[] status = { "پیش‌نویس", "صادر شده", "در حال ساخت" };
            foreach (MfProductionOrder o in orders)
            {
                string routing = o.RoutingRevisionId.HasValue && revNo.TryGetValue(o.RoutingRevisionId.Value, out int rn) ? $"مسیر ساخت بازنگری {rn}" : "بدون مسیر ساخت";
                string item = o.ItemId.HasValue ? itemText(o.ItemId.Value) : "-";
                Add(snap, EcImpactCategory.Order, EcRules.Key("ORDER", o.Id), $"دستور تولید {o.Number} ({status[(int)o.Status]}) — دستور کار {(wo.TryGetValue(o.WorkOrderId, out string c) ? c : "#" + o.WorkOrderId)}، کالا {item}، تعداد {Num(o.Quantity)}، {routing}", o.ItemId.HasValue ? partOf(o.ItemId.Value) : null, o.Quantity, o.Id);
            }
            var routings = await uow.Set<MfRoutingRevision>().AsNoTracking().Where(r => !r.Deleted && r.Status == MfRoutingStatus.Approved && r.ItemId != null && itemIds.Contains(r.ItemId.Value)).OrderBy(r => r.Id).ToListAsync(ct);
            foreach (MfRoutingRevision r in routings)
                Add(snap, EcImpactCategory.Order, EcRules.Key("ROUTING", r.Id), $"مسیر ساخت تاییدشده کالا {itemText(r.ItemId.Value)} بازنگری {r.Revision}", partOf(r.ItemId.Value), null, r.Id);
            return orders;
        }

        // ------------------------------------------------------------------------------------------ 4. stock and WIP

        private static async Task StockAsync(IUnitOfWork uow, Snapshot snap, List<int> itemIds, Func<int, string> itemText, Func<int, string> partOf, CancellationToken ct)
        {
            if (itemIds.Count == 0) return;
            var rows = await (from b in uow.Set<StockBalance>().AsNoTracking()
                              join w in uow.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.Id
                              join l in uow.Set<InventoryLot>().AsNoTracking() on b.LotId equals l.Id into lots
                              from l in lots.DefaultIfEmpty()
                              where itemIds.Contains(b.ItemId) && b.Quantity > 0
                              select new { b.ItemId, b.WarehouseId, WarehouseTitle = w.Title, w.Kind, b.LotId, LotNumber = l.LotNumber, b.Quantity }).ToListAsync(ct);
            // one row per warehouse, item and lot: the heat / lot of the stock decides whether the piece can be used, reworked or scrapped
            foreach (var g in rows.GroupBy(r => new { r.WarehouseId, r.ItemId, LotId = r.LotId ?? 0 }).OrderBy(g => g.Key.WarehouseId).ThenBy(g => g.Key.ItemId).ThenBy(g => g.Key.LotId))
            {
                var first = g.First();
                string kind = first.Kind == WarehouseKind.WorkInProgress ? " (کار در جریان)" : "";
                string lot = g.Key.LotId == 0 ? "" : $"، لات {first.LotNumber}";
                Add(snap, EcImpactCategory.Stock, EcRules.Key("STOCK", g.Key.WarehouseId, g.Key.ItemId, g.Key.LotId), $"انبار {first.WarehouseTitle}{kind} — کالا {itemText(g.Key.ItemId)}{lot}، موجودی {Num(g.Sum(x => x.Quantity))}", partOf(g.Key.ItemId), g.Sum(x => x.Quantity), g.Key.WarehouseId);
            }
            // serial pieces in stock or in production (work in progress), one row per serial
            var serials = await (from u in uow.Set<SerialUnit>().AsNoTracking()
                                 where !u.Deleted && itemIds.Contains(u.ItemId) && (u.Status == SerialUnitStatus.InStock || u.Status == SerialUnitStatus.InProduction)
                                 orderby u.Id
                                 select new { u.Id, u.ItemId, u.SerialNumber, u.Status }).ToListAsync(ct);
            foreach (var u in serials)
                Add(snap, EcImpactCategory.Stock, EcRules.Key("STOCKSN", u.Id), $"قطعه سریال {u.SerialNumber} — کالا {itemText(u.ItemId)} ({(u.Status == SerialUnitStatus.InProduction ? "کار در جریان" : "در انبار")})", partOf(u.ItemId), 1, u.Id);
        }

        // ------------------------------------------------------------------------------------------ 5. open PRs and POs

        private static async Task ProcurementAsync(IUnitOfWork uow, Snapshot snap, List<int> itemIds, Func<int, string> itemText, Func<int, string> partOf, CancellationToken ct)
        {
            if (itemIds.Count == 0) return;
            PurchaseRequestStatus[] prOpen = { PurchaseRequestStatus.Draft, PurchaseRequestStatus.Submitted, PurchaseRequestStatus.Approved, PurchaseRequestStatus.Ordered };
            var pr = await (from l in uow.Set<PurchaseRequestLine>().AsNoTracking()
                            join h in uow.Set<PurchaseRequest>().AsNoTracking() on l.PurchaseRequestId equals h.Id
                            where !h.Deleted && prOpen.Contains(h.Status) && l.ItemId != null && itemIds.Contains(l.ItemId.Value)
                            orderby l.Id
                            select new { l.Id, h.Number, l.RowNumber, ItemId = l.ItemId.Value, l.Quantity }).ToListAsync(ct);
            foreach (var r in pr)
                Add(snap, EcImpactCategory.Procurement, EcRules.Key("PR", r.Id), $"درخواست خرید {r.Number} ردیف {r.RowNumber} — کالا {itemText(r.ItemId)}، تعداد {Num(r.Quantity)}", partOf(r.ItemId), r.Quantity, r.Id);
            PurchaseOrderStatus[] poOpen = { PurchaseOrderStatus.Draft, PurchaseOrderStatus.Approved, PurchaseOrderStatus.Released, PurchaseOrderStatus.PartlyReceived };
            var po = await (from l in uow.Set<PurchaseOrderLine>().AsNoTracking()
                            join h in uow.Set<PurchaseOrder>().AsNoTracking() on l.PurchaseOrderId equals h.Id
                            where !h.Deleted && poOpen.Contains(h.Status) && l.ItemId != null && itemIds.Contains(l.ItemId.Value)
                            orderby l.Id
                            select new { l.Id, h.Number, l.RowNumber, ItemId = l.ItemId.Value, l.Quantity, l.ReceivedQuantity }).ToListAsync(ct);
            foreach (var r in po.Where(x => x.Quantity - x.ReceivedQuantity > 0))
                Add(snap, EcImpactCategory.Procurement, EcRules.Key("PO", r.Id), $"سفارش خرید {r.Number} ردیف {r.RowNumber} — کالا {itemText(r.ItemId)}، باقی‌مانده {Num(r.Quantity - r.ReceivedQuantity)}", partOf(r.ItemId), r.Quantity - r.ReceivedQuantity, r.Id);
        }

        // ------------------------------------------------------------------------------------------ 6. QC points, ITP, characteristic lists

        private static async Task QcAsync(IUnitOfWork uow, Snapshot snap, List<string> parts, List<int> itemIds, List<int> targetDeliverables, List<int> workOrderIds, CancellationToken ct)
        {
            var steps = await (from s in uow.Set<PbOpcStep>().AsNoTracking()
                               join r in uow.Set<PbOpcRouting>().AsNoTracking() on s.RoutingId equals r.Id
                               where !s.Deleted && !r.Deleted && s.IsQcPoint && r.Status != PbRoutingStatus.Superseded && s.PartNumber != null && parts.Contains(s.PartNumber)
                               orderby s.Id
                               select new { s.Id, s.PartNumber, s.Seq, s.Title, s.RequiredEvidence, RoutingPart = r.PartNumber, r.Revision }).ToListAsync(ct);
            foreach (var s in steps)
                Add(snap, EcImpactCategory.QcPoint, EcRules.Key("OPCQC", s.Id), $"نقطه کنترل OPC {s.RoutingPart} بازنگری {s.Revision:00} — قطعه {s.PartNumber}، مرحله {s.Seq} ({s.Title})" + (string.IsNullOrWhiteSpace(s.RequiredEvidence) ? "" : "، مدرک " + s.RequiredEvidence), s.PartNumber, null, s.Id);

            if (workOrderIds.Count > 0)
            {
                var itps = await uow.Set<Itp>().AsNoTracking().Where(i => !i.Deleted && workOrderIds.Contains(i.WorkOrderId)).OrderBy(i => i.Id).Select(i => new { i.Id, i.Number }).ToListAsync(ct);
                foreach (var i in itps) Add(snap, EcImpactCategory.QcPoint, EcRules.Key("ITP", i.Id), $"برنامه بازرسی و آزمون {i.Number} (دستور کار متاثر)", null, null, i.Id);
            }

            // characteristic lists (Package B) of the drawings of the parts
            var links = await uow.Set<PcDocumentLink>().AsNoTracking().Where(l => !l.Deleted && l.UnlinkReason == null && l.DeliverableId != null
                && (l.Kind == PcDocLinkKind.Item || l.Kind == PcDocLinkKind.BomLine || l.Kind == PcDocLinkKind.PartNumber)
                && ((l.ItemId != null && itemIds.Contains(l.ItemId.Value)) || (l.PartNumber != null && parts.Contains(l.PartNumber)))).Select(l => l.DeliverableId.Value).ToListAsync(ct);
            List<int> deliverables = links.Concat(targetDeliverables).Distinct().ToList();
            if (deliverables.Count == 0) return;
            var lists = await uow.Set<QcCharacteristicList>().AsNoTracking().Where(l => !l.Deleted && deliverables.Contains(l.DeliverableId) && (l.Status == CharListStatus.Approved || l.Status == CharListStatus.Released))
                .OrderBy(l => l.Id).Select(l => new { l.Id, l.DocumentNumber, l.RevisionCode, l.Version, l.Status }).ToListAsync(ct);
            foreach (var l in lists)
                Add(snap, EcImpactCategory.QcPoint, EcRules.Key("CHARLIST", l.Id), $"فهرست مشخصه‌های نقشه {l.DocumentNumber} بازنگری {l.RevisionCode} نسخه {l.Version}", null, null, l.Id);
        }

        // ------------------------------------------------------------------------------------------ 7. special processes

        private static async Task SpecialAsync(IUnitOfWork uow, Snapshot snap, List<int> itemIds, List<MfProductionOrder> orders, Func<int, string> itemText, CancellationToken ct)
        {
            List<int> orderIds = orders.Select(o => o.Id).ToList();
            if (orderIds.Count > 0)
            {
                var ops = await uow.Set<MfOperation>().AsNoTracking().Where(o => !o.Deleted && o.IsSpecialProcess && orderIds.Contains(o.OrderId)).OrderBy(o => o.Id).Select(o => new { o.Id, o.OrderId, o.Sequence, o.Title }).ToListAsync(ct);
                foreach (var o in ops)
                    Add(snap, EcImpactCategory.SpecialProcess, EcRules.Key("SPOP", o.Id), $"گام فرایند ویژه دستور {orders.First(x => x.Id == o.OrderId).Number} — عملیات {o.Sequence} ({o.Title})", null, null, o.Id);
            }
            if (itemIds.Count == 0) return;
            var templates = await (from t in uow.Set<OperationTemplate>().AsNoTracking()
                                   join s in uow.Set<MfOperationStandard>().AsNoTracking() on t.Id equals s.OperationTemplateId
                                   where !t.Deleted && t.IsActive && !s.Deleted && s.IsSpecialProcess && t.ItemId != null && itemIds.Contains(t.ItemId.Value)
                                   orderby t.Id
                                   select new { t.Id, ItemId = t.ItemId.Value, t.Sequence, t.Title }).ToListAsync(ct);
            foreach (var t in templates)
                Add(snap, EcImpactCategory.SpecialProcess, EcRules.Key("SPTPL", t.Id), $"مرحله فرایند ویژه در مسیر ساخت کالا {itemText(t.ItemId)} — گام {t.Sequence} ({t.Title})", null, null, t.Id);
        }

        // ------------------------------------------------------------------------------------------ 8. delivered units

        private static async Task DeliveredAsync(IUnitOfWork uow, Snapshot snap, List<int> itemIds, Func<int, string> itemText, Func<int, string> partOf, CancellationToken ct)
        {
            if (itemIds.Count == 0) return;
            var serials = await uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && u.Status == SerialUnitStatus.Delivered && itemIds.Contains(u.ItemId)).OrderBy(u => u.Id).Select(u => new { u.Id, u.ItemId, u.SerialNumber, u.WorkOrderId }).ToListAsync(ct);
            List<int> woIds = serials.Where(s => s.WorkOrderId.HasValue).Select(s => s.WorkOrderId.Value).Distinct().ToList();
            Dictionary<int, string> wo = woIds.Count == 0 ? new Dictionary<int, string>() : (await uow.Set<WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).Select(w => new { w.Id, w.Code }).ToListAsync(ct)).ToDictionary(w => w.Id, w => w.Code);
            foreach (var s in serials)
                Add(snap, EcImpactCategory.Delivered, EcRules.Key("SERIAL", s.Id), $"واحد تحویل‌شده سریال {s.SerialNumber} — کالا {itemText(s.ItemId)}" + (s.WorkOrderId.HasValue && wo.TryGetValue(s.WorkOrderId.Value, out string code) ? "، دستور کار " + code : ""), partOf(s.ItemId), 1m, s.Id);
            // delivered units that CONTAIN the part: pieces installed in a delivered assembly, and the outputs of production orders that consumed the part (as-built genealogy)
            var parents = await (from u in uow.Set<SerialUnit>().AsNoTracking()
                                 join p in uow.Set<SerialUnit>().AsNoTracking() on u.ParentSerialUnitId equals p.Id
                                 where !u.Deleted && !p.Deleted && itemIds.Contains(u.ItemId) && p.Status == SerialUnitStatus.Delivered
                                 orderby p.Id
                                 select new { p.Id, p.ItemId, p.SerialNumber, Inner = u.SerialNumber }).ToListAsync(ct);
            foreach (var p in parents)
                Add(snap, EcImpactCategory.Delivered, EcRules.Key("SERIAL", p.Id), $"واحد تحویل‌شده سریال {p.SerialNumber} — کالا {itemText(p.ItemId)} (شامل قطعه سریال {p.Inner})", partOf(p.ItemId), null, p.Id);
            var viaGenealogy = await (from g in uow.Set<GenealogyLink>().AsNoTracking()
                                      join p in uow.Set<SerialUnit>().AsNoTracking() on g.OutputSerialUnitId equals p.Id
                                      where !g.Deleted && !p.Deleted && itemIds.Contains(g.InputItemId) && p.Status == SerialUnitStatus.Delivered
                                      orderby p.Id
                                      select new { p.Id, p.ItemId, p.SerialNumber, g.InputItemId }).ToListAsync(ct);
            foreach (var p in viaGenealogy)
                Add(snap, EcImpactCategory.Delivered, EcRules.Key("SERIAL", p.Id), $"واحد تحویل‌شده سریال {p.SerialNumber} — کالا {itemText(p.ItemId)} (از ردیابی ساخت: قطعه {itemText(p.InputItemId)} مصرف شده)", partOf(p.ItemId), null, p.Id);
            var lots = await uow.Set<AsBuiltRecord>().AsNoTracking().Where(a => !a.Deleted && a.Status == AsBuiltStatus.Current && a.SerialUnitId == null && itemIds.Contains(a.ItemId)).OrderBy(a => a.Id).Select(a => new { a.Id, a.ItemId, a.WorkOrderId }).ToListAsync(ct);
            foreach (var a in lots)
                Add(snap, EcImpactCategory.Delivered, EcRules.Key("ASBUILT", a.Id), $"سابقه ساخت تحویل‌شده (بچ) — کالا {itemText(a.ItemId)}", partOf(a.ItemId), null, a.Id);
        }
    }
}
