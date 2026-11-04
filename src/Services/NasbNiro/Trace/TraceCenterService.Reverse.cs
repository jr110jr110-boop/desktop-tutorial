using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.TraceDtos;

namespace Services.NasbNiro.Trace
{
    public sealed partial class TraceCenterService
    {
        private sealed class RNode
        {
            public string Key;
            public string Kind;
            public int Id;
            public int Level;
            public string FromKey;
            public string Via;
            public int? OrderId;
            public int? DocumentId;
            public decimal? Quantity;
            public int? WorkOrderId;
            public int? ItemId;
        }

        private static string LotKey(int id) => "L" + id;
        private static string SerialKey(int id) => "S" + id;

        public async Task<ResponseDto<ReverseTraceDto>> ReverseAsync(ReverseTraceQuery query, CancellationToken ct)
        {
            string text = Clean(query?.Q);
            if (text == null && query?.LotId == null && query?.ProviderId == null) return Bad<ReverseTraceDto>("شماره بچ، شماره ذوب، تامین‌کننده، سفارش خرید یا درخواست خرید را وارد کنید.");
            int depth = Math.Max(1, Math.Min(query.Depth ?? DefaultReverseDepth, MaxReverseDepth));
            var dto = new ReverseTraceDto { Query = text, MaxDepth = depth };

            (string mode, HashSet<int> rootLots, HashSet<int> rootSerials) = await ResolveRootsAsync(query, text, dto.Notes, ct);
            if (rootLots.Count == 0 && rootSerials.Count == 0)
                return ResponseDtoHelper.NotFound<ReverseTraceDto>("بچ، سریال یا سندی با این مشخصه پیدا نشد.");
            dto.Mode = mode;
            if (rootLots.Count + rootSerials.Count > MaxRoots)
            {
                rootLots = rootLots.OrderBy(i => i).Take(MaxRoots).ToHashSet();
                rootSerials = rootSerials.OrderBy(i => i).Take(Math.Max(0, MaxRoots - rootLots.Count)).ToHashSet();
                dto.Notes.Add($"بیش از {MaxRoots} بچ یا سریال پیدا شد؛ فقط {MaxRoots} مورد اول بررسی شد. جستجو را دقیق‌تر کنید.");
                dto.Truncated = true;
            }

            // ------------------------------------------------------------------ closure, level by level
            var nodes = new Dictionary<string, RNode>();
            foreach (int id in rootLots) nodes[LotKey(id)] = new RNode { Key = LotKey(id), Kind = "lot", Id = id, Level = 0 };
            foreach (int id in rootSerials) nodes[SerialKey(id)] = new RNode { Key = SerialKey(id), Kind = "serial", Id = id, Level = 0 };
            List<int> frontLots = rootLots.ToList(), frontSerials = rootSerials.ToList();

            for (int level = 1; level <= depth && (frontLots.Count > 0 || frontSerials.Count > 0); level++)
            {
                var found = new Dictionary<string, RNode>();
                void Add(string kind, int id, string from, string via, int? orderId = null, int? documentId = null, decimal? quantity = null, int? workOrderId = null, int? itemId = null)
                {
                    string key = kind == "lot" ? LotKey(id) : kind == "serial" ? SerialKey(id) : "I" + id;
                    if (nodes.ContainsKey(key) || found.ContainsKey(key)) return;
                    found[key] = new RNode { Key = key, Kind = kind, Id = id, Level = level, FromKey = from, Via = via, OrderId = orderId, DocumentId = documentId, Quantity = quantity, WorkOrderId = workOrderId, ItemId = itemId };
                }

                // A. a production order consumed it (kit issues) -> what the order made
                var links = await _uow.Set<GenealogyLink>().AsNoTracking()
                    .Where(l => !l.Deleted && ((l.InputLotId != null && frontLots.Contains(l.InputLotId.Value)) || (l.InputSerialUnitId != null && frontSerials.Contains(l.InputSerialUnitId.Value))))
                    .Select(l => new { l.InputLotId, l.InputSerialUnitId, l.OutputLotId, l.OutputSerialUnitId, l.OutputItemId, l.ProductionOrderId, l.ReceiptDocumentId, l.Quantity, WorkOrderId = (int?)l.ProductionOrder.WorkOrderId })
                    .ToListAsync(ct);
                foreach (var l in links)
                {
                    string from = l.InputLotId.HasValue ? LotKey(l.InputLotId.Value) : SerialKey(l.InputSerialUnitId.Value);
                    if (l.OutputSerialUnitId.HasValue) Add("serial", l.OutputSerialUnitId.Value, from, "production", l.ProductionOrderId, l.ReceiptDocumentId, l.Quantity, l.WorkOrderId);
                    else if (l.OutputLotId.HasValue) Add("lot", l.OutputLotId.Value, from, "production", l.ProductionOrderId, l.ReceiptDocumentId, l.Quantity, l.WorkOrderId);
                    else Add("item", l.OutputItemId, from, "production", l.ProductionOrderId, l.ReceiptDocumentId, l.Quantity, l.WorkOrderId, l.OutputItemId);
                }

                // B. a stage conversion made a new lot or serial piece from it
                if (frontLots.Count > 0)
                    foreach (var c in await _uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && l.ParentLotId != null && frontLots.Contains(l.ParentLotId.Value)).Select(l => new { l.Id, Parent = l.ParentLotId.Value }).ToListAsync(ct))
                        Add("lot", c.Id, LotKey(c.Parent), "conversion");
                if (frontLots.Count > 0 || frontSerials.Count > 0)
                    foreach (var c in await _uow.Set<SerialUnit>().AsNoTracking()
                        .Where(u => !u.Deleted && ((u.SourceLotId != null && frontLots.Contains(u.SourceLotId.Value)) || (u.SourceSerialUnitId != null && frontSerials.Contains(u.SourceSerialUnitId.Value))))
                        .Select(u => new { u.Id, u.SourceLotId, u.SourceSerialUnitId }).ToListAsync(ct))
                        Add("serial", c.Id, c.SourceSerialUnitId.HasValue && frontSerials.Contains(c.SourceSerialUnitId.Value) ? SerialKey(c.SourceSerialUnitId.Value) : LotKey(c.SourceLotId.Value), "conversion");

                // C. it was installed in an assembly (assembly link, also when it was taken out again later: it was inside the product once)
                foreach (var a in await _uow.Set<AssemblyLink>().AsNoTracking()
                    .Where(a => !a.Deleted && ((a.ChildLotId != null && frontLots.Contains(a.ChildLotId.Value)) || (a.ChildSerialUnitId != null && frontSerials.Contains(a.ChildSerialUnitId.Value))))
                    .Select(a => new { a.ParentSerialUnitId, a.ChildLotId, a.ChildSerialUnitId, a.Quantity, a.WorkOrderId }).ToListAsync(ct))
                    Add("serial", a.ParentSerialUnitId, a.ChildSerialUnitId.HasValue ? SerialKey(a.ChildSerialUnitId.Value) : LotKey(a.ChildLotId.Value), "assembly", null, null, a.Quantity, a.WorkOrderId);

                // D. the assembly a serial piece is installed in now (also when no assembly link row exists)
                if (frontSerials.Count > 0)
                    foreach (var p in await _uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && frontSerials.Contains(u.Id) && u.ParentSerialUnitId != null).Select(u => new { u.Id, Parent = u.ParentSerialUnitId.Value }).ToListAsync(ct))
                        Add("serial", p.Parent, SerialKey(p.Id), "assembly");

                // E. the serial pieces that belong to the lot (the heat number is on them)
                if (frontLots.Count > 0)
                    foreach (var m in await _uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && u.LotId != null && frontLots.Contains(u.LotId.Value)).Select(u => new { u.Id, Lot = u.LotId.Value }).ToListAsync(ct))
                        Add("serial", m.Id, LotKey(m.Lot), "lotMember");

                foreach (var kv in found)
                {
                    if (nodes.Count >= MaxReverseNodes) { dto.Truncated = true; break; }
                    nodes[kv.Key] = kv.Value;
                }
                frontLots = found.Values.Where(n => n.Kind == "lot" && nodes.ContainsKey(n.Key)).Select(n => n.Id).ToList();
                frontSerials = found.Values.Where(n => n.Kind == "serial" && nodes.ContainsKey(n.Key)).Select(n => n.Id).ToList();
                if (dto.Truncated) break;
                if (level == depth && (frontLots.Count > 0 || frontSerials.Count > 0))
                {
                    // is there anything beyond the last level? one more cheap check: the same A..E queries would find it; we only say the cap was reached
                    dto.Truncated = true;
                    dto.Notes.Add($"عمق جستجو به سقف {depth} سطح رسید؛ ممکن است مصرف‌کننده‌های بعدی نمایش داده نشده باشد (عمق را بیشتر کنید).");
                }
            }

            await FillReverseAsync(dto, nodes, ct);
            return ResponseDtoHelper.Ok(dto);
        }

        // ---------------------------------------------------------------------------------------------- roots

        private async Task<(string Mode, HashSet<int> Lots, HashSet<int> Serials)> ResolveRootsAsync(ReverseTraceQuery query, string text, List<string> notes, CancellationToken ct)
        {
            var lots = new HashSet<int>();
            var serials = new HashSet<int>();
            string kind = Clean(query.Kind)?.ToLowerInvariant();

            if (query.LotId.HasValue)
            {
                if (await _uow.Set<InventoryLot>().AnyAsync(l => l.Id == query.LotId.Value && !l.Deleted, ct)) lots.Add(query.LotId.Value);
                return ("lot", lots, serials);
            }
            if (query.ProviderId.HasValue || kind == "supplier")
            {
                List<int> providers = query.ProviderId.HasValue ? new List<int> { query.ProviderId.Value }
                    : await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => !p.Deleted && p.Name.Contains(text)).OrderBy(p => p.Id).Select(p => p.Id).Take(5).ToListAsync(ct);
                if (providers.Count > 1) notes.Add($"{providers.Count} تامین‌کننده با این نام پیدا شد؛ همه بررسی شدند.");
                lots.UnionWith(await _uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && l.ProviderId != null && providers.Contains(l.ProviderId.Value)).Select(l => l.Id).ToListAsync(ct));
                await AddReceiptsAsync(_uow.Set<InventoryDocumentLine>().AsNoTracking().Where(l => l.Document.ProviderId != null && providers.Contains(l.Document.ProviderId.Value)), lots, serials, ct);
                return ("supplier", lots, serials);
            }
            if (text == null) return (null, lots, serials);

            // the labels printed by the warehouse
            (string labelKind, string labelItem, string labelValue) = ParseLabel(text);
            if (labelKind == "L")
            {
                lots.UnionWith(await _uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && l.Item.Code == labelItem && l.LotNumber == labelValue).Select(l => l.Id).ToListAsync(ct));
                if (lots.Count > 0) return ("lot", lots, serials);
            }
            if (labelKind == "S")
            {
                serials.UnionWith(await _uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && u.Item.Code == labelItem && u.SerialNumber == labelValue).Select(u => u.Id).ToListAsync(ct));
                if (serials.Count > 0) return ("serial", lots, serials);
            }

            if (kind == null || kind == "heat" || kind == "lot")
            {
                var found = await _uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && (l.LotNumber == text || l.HeatNumber == text || l.CertificateNumber == text))
                    .Select(l => new { l.Id, l.HeatNumber }).ToListAsync(ct);
                if (found.Count > 0)
                {
                    lots.UnionWith(found.Select(f => f.Id));
                    return (found.Any(f => f.HeatNumber == text) ? "heat" : "lot", lots, serials);
                }
            }
            if (kind == null || kind == "po")
            {
                int? poId = await _uow.Set<PurchaseOrder>().AsNoTracking().Where(o => !o.Deleted && o.Number == text).OrderBy(o => o.Id).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
                if (poId.HasValue)
                {
                    await AddOrderReceiptsAsync(new List<int> { poId.Value }, lots, serials, ct);
                    if (lots.Count == 0 && serials.Count == 0) notes.Add("برای این سفارش خرید هنوز رسید تاییدشده‌ای ثبت نشده است.");
                    return ("po", lots, serials);
                }
            }
            if (kind == null || kind == "pr")
            {
                int? prId = await _uow.Set<PurchaseRequest>().AsNoTracking().Where(o => !o.Deleted && o.Number == text).OrderBy(o => o.Id).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
                if (prId.HasValue)
                {
                    List<int> lineIds = await _uow.Set<PurchaseRequestLine>().AsNoTracking().Where(l => l.PurchaseRequestId == prId.Value).Select(l => l.Id).ToListAsync(ct);
                    List<int> pos = await _uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.RequestLineId != null && lineIds.Contains(l.RequestLineId.Value)).Select(l => l.PurchaseOrderId).Distinct().ToListAsync(ct);
                    pos.AddRange(await _uow.Set<PurchaseOrder>().AsNoTracking().Where(o => !o.Deleted && o.InquiryId != null && o.Inquiry.PurchaseRequestId == prId.Value).Select(o => o.Id).ToListAsync(ct));
                    await AddOrderReceiptsAsync(pos.Distinct().ToList(), lots, serials, ct);
                    if (lots.Count == 0 && serials.Count == 0) notes.Add("برای این درخواست خرید هنوز رسید تاییدشده‌ای ثبت نشده است.");
                    return ("pr", lots, serials);
                }
            }
            if (kind == null || kind == "mr")
            {
                int? mrId = await _uow.Set<MaterialRequest>().AsNoTracking().Where(o => !o.Deleted && o.Number == text).OrderBy(o => o.Id).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
                if (mrId.HasValue)
                {
                    var moves = await _uow.Set<StockTransaction>().AsNoTracking().Where(t => !t.IsReversal && t.Quantity < 0 && t.Document.MaterialRequestId == mrId.Value && t.Document.Type == InventoryDocumentType.Issue
                        && t.Document.Status == InventoryDocumentStatus.Confirmed).Select(t => new { t.LotId, t.SerialUnitId }).ToListAsync(ct);
                    foreach (var m in moves)
                    {
                        if (m.SerialUnitId.HasValue) serials.Add(m.SerialUnitId.Value);
                        else if (m.LotId.HasValue) lots.Add(m.LotId.Value);
                    }
                    if (lots.Count == 0 && serials.Count == 0) notes.Add("برای این درخواست مواد هنوز حواله تاییدشده‌ای با بچ یا سریال ثبت نشده است.");
                    return ("mr", lots, serials);
                }
            }
            if (kind == null || kind == "serial")
            {
                serials.UnionWith(await _uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && u.SerialNumber == text).Select(u => u.Id).ToListAsync(ct));
                if (serials.Count > 0) return ("serial", lots, serials);
            }
            if (kind == null)
            {
                // a supplier by name, as the last try
                List<int> providers = await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => !p.Deleted && p.Name.Contains(text)).OrderBy(p => p.Id).Select(p => p.Id).Take(5).ToListAsync(ct);
                if (providers.Count > 0)
                {
                    lots.UnionWith(await _uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && l.ProviderId != null && providers.Contains(l.ProviderId.Value)).Select(l => l.Id).ToListAsync(ct));
                    await AddReceiptsAsync(_uow.Set<InventoryDocumentLine>().AsNoTracking().Where(l => l.Document.ProviderId != null && providers.Contains(l.Document.ProviderId.Value)), lots, serials, ct);
                    return ("supplier", lots, serials);
                }
            }
            return (kind, lots, serials);
        }

        private async Task AddOrderReceiptsAsync(List<int> poIds, HashSet<int> lots, HashSet<int> serials, CancellationToken ct)
        {
            if (poIds.Count == 0) return;
            await AddReceiptsAsync(_uow.Set<InventoryDocumentLine>().AsNoTracking()
                .Where(l => (l.Document.PurchaseOrderId != null && poIds.Contains(l.Document.PurchaseOrderId.Value)) || (l.PurchaseOrderLine != null && poIds.Contains(l.PurchaseOrderLine.PurchaseOrderId))), lots, serials, ct);
        }

        /// <summary>The lots and serial pieces that confirmed purchase receipt lines (already filtered by the caller) brought in.</summary>
        private async Task AddReceiptsAsync(IQueryable<InventoryDocumentLine> lines, HashSet<int> lots, HashSet<int> serials, CancellationToken ct)
        {
            IQueryable<InventoryDocumentLine> receipts = lines.Where(l => !l.Document.Deleted && l.Document.Status == InventoryDocumentStatus.Confirmed && l.Document.Type == InventoryDocumentType.PurchaseReceipt);
            lots.UnionWith(await receipts.Where(l => l.LotId != null).Select(l => l.LotId.Value).Distinct().ToListAsync(ct));
            serials.UnionWith(await _uow.Set<InventoryDocumentLineSerial>().AsNoTracking().Where(s => s.SerialUnitId != null && receipts.Any(l => l.Id == s.LineId)).Select(s => s.SerialUnitId.Value).Distinct().ToListAsync(ct));
        }

        // ---------------------------------------------------------------------------------------------- fill

        private async Task FillReverseAsync(ReverseTraceDto dto, Dictionary<string, RNode> nodes, CancellationToken ct)
        {
            List<int> lotIds = nodes.Values.Where(n => n.Kind == "lot").Select(n => n.Id).ToList();
            List<int> serialIds = nodes.Values.Where(n => n.Kind == "serial").Select(n => n.Id).ToList();

            var lots = lotIds.Count == 0 ? new Dictionary<int, (int ItemId, string Number, string Heat, string Cert, int? ProviderId)>()
                : (await _uow.Set<InventoryLot>().AsNoTracking().Where(l => lotIds.Contains(l.Id)).Select(l => new { l.Id, l.ItemId, l.LotNumber, l.HeatNumber, l.CertificateNumber, l.ProviderId }).ToListAsync(ct))
                    .ToDictionary(l => l.Id, l => (ItemId: l.ItemId, Number: l.LotNumber, Heat: l.HeatNumber, Cert: l.CertificateNumber, ProviderId: l.ProviderId));
            var serials = serialIds.Count == 0 ? new Dictionary<int, (int ItemId, string Number, int? WorkOrderId, int? LotId)>()
                : (await _uow.Set<SerialUnit>().AsNoTracking().Where(u => serialIds.Contains(u.Id)).Select(u => new { u.Id, u.ItemId, u.SerialNumber, u.WorkOrderId, u.LotId }).ToListAsync(ct))
                    .ToDictionary(u => u.Id, u => (ItemId: u.ItemId, Number: u.SerialNumber, WorkOrderId: u.WorkOrderId, LotId: u.LotId));

            var itemIds = new HashSet<int>(lots.Values.Select(l => l.ItemId));
            itemIds.UnionWith(serials.Values.Select(s => s.ItemId));
            itemIds.UnionWith(nodes.Values.Where(n => n.ItemId.HasValue).Select(n => n.ItemId.Value));
            List<int> itemList = itemIds.ToList();
            var items = itemList.Count == 0 ? new Dictionary<int, (string Code, string Title, string PartNumber, string Stage)>()
                : (await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemList.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title, i.PartNumber, i.StageCode2 }).ToListAsync(ct))
                    .ToDictionary(i => i.Id, i => (Code: i.Code, Title: i.Title, PartNumber: i.PartNumber, Stage: i.StageCode2));

            List<int> orderIds = nodes.Values.Where(n => n.OrderId.HasValue).Select(n => n.OrderId.Value).Distinct().ToList();
            var orders = orderIds.Count == 0 ? new Dictionary<int, string>()
                : (await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Number }).ToListAsync(ct)).ToDictionary(o => o.Id, o => o.Number);
            List<int> docIds = nodes.Values.Where(n => n.DocumentId.HasValue).Select(n => n.DocumentId.Value).Distinct().ToList();
            var docs = docIds.Count == 0 ? new Dictionary<int, string>()
                : (await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => docIds.Contains(d.Id)).Select(d => new { d.Id, d.Number }).ToListAsync(ct)).ToDictionary(d => d.Id, d => d.Number);

            // the work order of a node: the order that made it, the assembly link, else the work order stored on the serial piece
            foreach (RNode n in nodes.Values)
                if (!n.WorkOrderId.HasValue && n.Kind == "serial" && serials.TryGetValue(n.Id, out var s)) n.WorkOrderId = s.WorkOrderId;

            // ---- the nodes
            var fromKeys = new HashSet<string>(nodes.Values.Where(n => n.FromKey != null).Select(n => n.FromKey));
            Dictionary<int, WorkOrderInfo> workOrders = await LoadWorkOrdersAsync(nodes.Values.Where(n => n.WorkOrderId.HasValue).Select(n => n.WorkOrderId.Value).Distinct().ToList(), ct);

            foreach (RNode n in nodes.Values.OrderBy(n => n.Level).ThenBy(n => n.Key, StringComparer.Ordinal))
            {
                var v = new ReverseNodeDto { Key = n.Key, Level = n.Level, Kind = n.Kind, Id = n.Id, FromKey = n.FromKey, Via = n.Via, Quantity = n.Quantity, Terminal = n.Level > 0 && !fromKeys.Contains(n.Key) };
                int itemId = n.ItemId ?? 0;
                if (n.Kind == "lot" && lots.TryGetValue(n.Id, out var lot)) { itemId = lot.ItemId; v.LotNumber = lot.Number; v.HeatNumber = lot.Heat; }
                else if (n.Kind == "serial" && serials.TryGetValue(n.Id, out var ser))
                {
                    itemId = ser.ItemId; v.SerialNumber = ser.Number;
                    if (ser.LotId.HasValue && lots.TryGetValue(ser.LotId.Value, out var parentLot)) { v.LotNumber = parentLot.Number; v.HeatNumber = parentLot.Heat; }
                }
                v.ItemId = itemId;
                if (items.TryGetValue(itemId, out var it)) { v.ItemCode = it.Code; v.ItemTitle = it.Title; v.PartNumber = it.PartNumber; v.StageCode = it.Stage; }
                if (n.OrderId.HasValue && orders.TryGetValue(n.OrderId.Value, out string orderNumber)) v.OrderNumber = orderNumber;
                if (n.DocumentId.HasValue && docs.TryGetValue(n.DocumentId.Value, out string docNumber)) v.DocumentNumber = docNumber;
                if (n.WorkOrderId.HasValue && workOrders.TryGetValue(n.WorkOrderId.Value, out WorkOrderInfo wo)) v.WorkOrderCode = wo.Code;
                if (n.Level == 0) dto.Roots.Add(new ReverseRootDto
                {
                    Key = n.Key, Kind = n.Kind, Id = n.Id, ItemId = itemId, ItemCode = v.ItemCode, ItemTitle = v.ItemTitle, LotNumber = v.LotNumber, HeatNumber = v.HeatNumber, SerialNumber = v.SerialNumber,
                    CertificateNumber = n.Kind == "lot" && lots.TryGetValue(n.Id, out var rootLot) ? rootLot.Cert : null,
                });
                else dto.Nodes.Add(v);
            }

            // ---- the procurement facts of the roots (supplier, receipt, order, request)
            List<ReceiptRow> receipts = await ReceiptsAsync(dto.Roots.Where(r => r.Kind == "lot").Select(r => r.Id).ToList(), dto.Roots.Where(r => r.Kind == "serial").Select(r => r.Id).ToList(), ct);
            Dictionary<int, string> providerNames = new Dictionary<int, string>();
            List<int> providerIds = lots.Values.Where(l => l.ProviderId.HasValue).Select(l => l.ProviderId.Value).Distinct().ToList();
            if (providerIds.Count > 0)
                providerNames = (await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => providerIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct)).ToDictionary(p => p.Id, p => p.Name);
            foreach (ReverseRootDto root in dto.Roots)
            {
                ReceiptRow r = receipts.Where(x => root.Kind == "lot" ? x.LotId == root.Id : x.SerialUnitId == root.Id).OrderBy(x => x.DocumentDate).FirstOrDefault();
                if (r != null)
                {
                    root.ReceiptNumber = r.DocumentNumber; root.PurchaseOrderNumber = r.PoNumber; root.PurchaseRequestNumber = r.PrNumber; root.ProviderName = r.ProviderName;
                    root.ReceivedQuantity = receipts.Where(x => root.Kind == "lot" ? x.LotId == root.Id : x.SerialUnitId == root.Id).Sum(x => x.Quantity); root.ReceivedDate = r.DocumentDate;
                }
                if (root.ProviderName == null && root.Kind == "lot" && lots.TryGetValue(root.Id, out var l) && l.ProviderId.HasValue && providerNames.TryGetValue(l.ProviderId.Value, out string pname)) root.ProviderName = pname;
            }

            // ---- distinct items (the answer to "which parts did it end up in")
            dto.Items = dto.Nodes.GroupBy(n => n.ItemId).Select(g => new ReverseItemDto
            {
                ItemId = g.Key, Code = g.First().ItemCode, Title = g.First().ItemTitle, PartNumber = g.First().PartNumber, StageCode = g.First().StageCode,
                NodeCount = g.Count(), MinLevel = g.Min(n => n.Level), MaxLevel = g.Max(n => n.Level),
            }).OrderBy(i => i.MinLevel).ThenBy(i => i.Code, StringComparer.Ordinal).ToList();
            dto.Terminals = dto.Nodes.Where(n => n.Terminal).ToList();

            // ---- customer deliveries of any node (root included): confirmed customer delivery documents
            var deliveryWorkOrders = new List<int>();
            List<ReverseDeliveryDto> deliveries = await DeliveriesAsync(nodes.Values.Where(n => n.Kind == "lot").Select(n => n.Id).ToList(), nodes.Values.Where(n => n.Kind == "serial").Select(n => n.Id).ToList(), deliveryWorkOrders, ct);
            dto.Deliveries = deliveries;

            // ---- projects
            var projectCounts = nodes.Values.Where(n => n.WorkOrderId.HasValue).GroupBy(n => n.WorkOrderId.Value).ToDictionary(g => g.Key, g => g.Count());
            List<int> extraWorkOrders = deliveryWorkOrders.Distinct().Where(w => !workOrders.ContainsKey(w)).ToList();
            foreach (var kv in await LoadWorkOrdersAsync(extraWorkOrders, ct)) workOrders[kv.Key] = kv.Value;
            foreach (int w in deliveryWorkOrders.Distinct()) if (!projectCounts.ContainsKey(w)) projectCounts[w] = 0;
            foreach (var kv in projectCounts.OrderBy(k => k.Key))
            {
                if (!workOrders.TryGetValue(kv.Key, out WorkOrderInfo wo)) continue;
                dto.Projects.Add(new ReverseProjectDto { WorkOrderId = kv.Key, WorkOrderCode = wo.Code, WorkOrderTitle = wo.Title, ContractCode = wo.ContractCode, ContractTitle = wo.ContractTitle, Customer = wo.Customer, NodeCount = kv.Value });
            }

            dto.Counts = new ReverseCountsDto
            {
                Roots = dto.Roots.Count, Nodes = dto.Nodes.Count, Items = dto.Items.Count, Lots = dto.Nodes.Count(n => n.Kind == "lot"), Serials = dto.Nodes.Count(n => n.Kind == "serial"),
                Terminals = dto.Terminals.Count, Projects = dto.Projects.Count, Deliveries = dto.Deliveries.Count,
            };
        }

        internal sealed class WorkOrderInfo
        {
            public string Code;
            public string Title;
            public string ContractCode;
            public string ContractTitle;
            public string Customer;
        }

        private async Task<Dictionary<int, WorkOrderInfo>> LoadWorkOrdersAsync(List<int> ids, CancellationToken ct)
        {
            if (ids.Count == 0) return new Dictionary<int, WorkOrderInfo>();
            return (await _uow.Set<WorkOrder>().AsNoTracking().Where(w => ids.Contains(w.Id))
                .Select(w => new { w.Id, w.Code, w.Title, ContractCode = w.Contract.Code, ContractTitle = w.Contract.Title, Customer = w.Contract.Employer }).ToListAsync(ct))
                .ToDictionary(w => w.Id, w => new WorkOrderInfo { Code = w.Code, Title = w.Title, ContractCode = w.ContractCode, ContractTitle = w.ContractTitle, Customer = w.Customer });
        }

        private async Task<List<ReverseDeliveryDto>> DeliveriesAsync(List<int> lotIds, List<int> serialIds, List<int> workOrderIds, CancellationToken ct)
        {
            var list = new List<ReverseDeliveryDto>();
            if (lotIds.Count == 0 && serialIds.Count == 0) return list;
            var rows = await _uow.Set<StockTransaction>().AsNoTracking()
                .Where(t => !t.IsReversal && t.Quantity < 0 && t.Document.Type == InventoryDocumentType.CustomerDelivery && t.Document.Status == InventoryDocumentStatus.Confirmed && !t.Document.Deleted
                    && ((t.SerialUnitId == null && t.LotId != null && lotIds.Contains(t.LotId.Value)) || (t.SerialUnitId != null && serialIds.Contains(t.SerialUnitId.Value))))
                .Select(t => new
                {
                    t.LotId, t.SerialUnitId, t.Quantity, ItemCode = t.Item.Code, DocumentId = t.DocumentId, t.Document.Number, t.Document.DocumentDate, t.Document.Destination, t.Document.WorkOrderId,
                    WorkOrderCode = t.Document.WorkOrder.Code, ContractCode = t.Document.Contract.Code, Customer = t.Document.Contract.Employer,
                    LotNumber = t.Lot.LotNumber, SerialNumber = t.SerialUnit.SerialNumber,
                }).ToListAsync(ct);
            workOrderIds.AddRange(rows.Where(r => r.WorkOrderId.HasValue).Select(r => r.WorkOrderId.Value));
            foreach (var r in rows.OrderBy(r => r.DocumentDate).ThenBy(r => r.Number, StringComparer.Ordinal))
                list.Add(new ReverseDeliveryDto
                {
                    DocumentId = r.DocumentId, DocumentNumber = r.Number, DocumentDate = r.DocumentDate, NodeKey = r.SerialUnitId.HasValue ? SerialKey(r.SerialUnitId.Value) : LotKey(r.LotId.Value),
                    ItemCode = r.ItemCode, SerialNumber = r.SerialNumber, LotNumber = r.SerialUnitId.HasValue ? null : r.LotNumber, Quantity = -r.Quantity, WorkOrderCode = r.WorkOrderCode,
                    ContractCode = r.ContractCode, Customer = r.Customer, Destination = r.Destination,
                });
            return list;
        }
    }
}
