using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Attachments;
using ViewModels.NasbNiro.AttachmentDtos;
using ViewModels.NasbNiro.TraceDtos;

namespace Services.NasbNiro.Trace
{
    public sealed partial class TraceCenterService
    {
        // ------------------------------------------------------------------------------------------ units, level by level

        private async Task EnsureItemsAsync(TreeCtx c, IEnumerable<int> itemIds, CancellationToken ct)
        {
            List<int> need = itemIds.Where(i => !c.Items.ContainsKey(i)).Distinct().ToList();
            if (need.Count == 0) return;
            foreach (var i in await _uow.Set<InventoryItem>().AsNoTracking().Where(i => need.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title, i.PartNumber, i.StageCode2 }).ToListAsync(ct))
                c.Items[i.Id] = (i.Code, i.Title, i.PartNumber, i.StageCode2);
        }

        /// <summary>Loads the lots and serial pieces that are not in the context yet (two queries, then one for their items).</summary>
        private async Task EnsureUnitsAsync(TreeCtx c, IEnumerable<int> lotIds, IEnumerable<int> serialIds, CancellationToken ct)
        {
            List<int> needLots = lotIds.Where(i => !c.Units.ContainsKey(LotKey(i))).Distinct().ToList();
            List<int> needSerials = serialIds.Where(i => !c.Units.ContainsKey(SerialKey(i))).Distinct().ToList();
            if (needLots.Count > 0)
                foreach (var l in await _uow.Set<InventoryLot>().AsNoTracking().Where(l => needLots.Contains(l.Id))
                    .Select(l => new { l.Id, l.ItemId, l.LotNumber, l.HeatNumber, l.CertificateNumber, l.CertificateDocumentId, l.ProviderId, l.ParentLotId, l.CreateDate }).ToListAsync(ct))
                    c.Units[LotKey(l.Id)] = new UnitInfo
                    {
                        Key = LotKey(l.Id), IsLot = true, Id = l.Id, ItemId = l.ItemId, Number = l.LotNumber, Heat = l.HeatNumber, Cert = l.CertificateNumber, CertDocument = l.CertificateDocumentId,
                        ProviderId = l.ProviderId, ParentLotId = l.ParentLotId, Date = l.CreateDate.DateTime,
                    };
            if (needSerials.Count > 0)
                foreach (var u in await _uow.Set<SerialUnit>().AsNoTracking().Where(u => needSerials.Contains(u.Id))
                    .Select(u => new { u.Id, u.ItemId, u.SerialNumber, u.Status, u.WorkOrderId, u.LotId, u.SourceSerialUnitId, u.SourceLotId, u.CreateDate }).ToListAsync(ct))
                    c.Units[SerialKey(u.Id)] = new UnitInfo
                    {
                        Key = SerialKey(u.Id), IsLot = false, Id = u.Id, ItemId = u.ItemId, Number = u.SerialNumber, Status = TraceLabels.Serial(u.Status), WorkOrderId = u.WorkOrderId, LotId = u.LotId,
                        SourceSerialId = u.SourceSerialUnitId, SourceLotId = u.SourceLotId, Date = u.CreateDate.DateTime,
                    };
            await EnsureItemsAsync(c, c.Units.Values.Select(u => u.ItemId), ct);
        }

        private void AddEdge(TreeCtx c, string from, InputEdge edge)
        {
            if (!c.Inputs.TryGetValue(from, out List<InputEdge> list)) c.Inputs[from] = list = new List<InputEdge>();
            if (list.Any(e => e.ToKey == edge.ToKey && e.Via == edge.Via && e.OrderId == edge.OrderId)) return;
            list.Add(edge);
        }

        /// <summary>
        /// What the seeds are made of, level by level: the inputs of the production order that made them (genealogy links), the previous stage (stage conversion), the lot a serial piece was cut from, and the
        /// pieces installed in an assembly. Four queries per level whatever the number of nodes.
        /// </summary>
        private async Task ExpandDownAsync(TreeCtx c, List<int> seedLots, List<int> seedSerials, int depth, CancellationToken ct)
        {
            await EnsureUnitsAsync(c, seedLots, seedSerials, ct);
            var visited = new HashSet<string>(seedLots.Select(LotKey).Concat(seedSerials.Select(SerialKey)));
            List<int> frontLots = seedLots.Distinct().ToList(), frontSerials = seedSerials.Distinct().ToList();

            for (int level = 1; level <= depth && (frontLots.Count > 0 || frontSerials.Count > 0); level++)
            {
                var newLots = new HashSet<int>();
                var newSerials = new HashSet<int>();
                var untracked = new List<(string From, int ItemId, InputEdge Edge)>();

                void Link(string from, int? lotId, int? serialId, InputEdge edge)
                {
                    if (lotId.HasValue) { edge.ToKey = LotKey(lotId.Value); newLots.Add(lotId.Value); }
                    else if (serialId.HasValue) { edge.ToKey = SerialKey(serialId.Value); newSerials.Add(serialId.Value); }
                    else return;
                    AddEdge(c, from, edge);
                }

                var links = await _uow.Set<GenealogyLink>().AsNoTracking()
                    .Where(l => !l.Deleted && ((l.OutputLotId != null && frontLots.Contains(l.OutputLotId.Value)) || (l.OutputSerialUnitId != null && frontSerials.Contains(l.OutputSerialUnitId.Value))))
                    .Select(l => new { l.OutputLotId, l.OutputSerialUnitId, l.InputLotId, l.InputSerialUnitId, l.InputItemId, l.Quantity, l.IsShared, l.ProductionOrderId, l.IssueDocumentId }).ToListAsync(ct);
                foreach (var l in links)
                {
                    string from = l.OutputSerialUnitId.HasValue ? SerialKey(l.OutputSerialUnitId.Value) : LotKey(l.OutputLotId.Value);
                    var edge = new InputEdge { Quantity = l.Quantity, Via = "production", OrderId = l.ProductionOrderId, IssueDocumentId = l.IssueDocumentId, Shared = l.IsShared };
                    if (l.InputLotId.HasValue || l.InputSerialUnitId.HasValue) Link(from, l.InputLotId, l.InputSerialUnitId, edge);
                    else untracked.Add((from, l.InputItemId, edge));
                }
                foreach (var u in untracked)
                {
                    string key = "I" + u.ItemId;
                    if (!c.Units.ContainsKey(key)) c.Units[key] = new UnitInfo { Key = key, Untracked = true, Id = u.ItemId, ItemId = u.ItemId };
                    u.Edge.ToKey = key;
                    AddEdge(c, u.From, u.Edge);
                }

                // the previous stage and the lot a piece belongs to: read from the units already loaded
                foreach (int id in frontLots)
                {
                    UnitInfo lot = c.Units[LotKey(id)];
                    if (lot.ParentLotId.HasValue) Link(lot.Key, lot.ParentLotId, null, new InputEdge { Via = "conversion", Quantity = 0 });
                }
                foreach (int id in frontSerials)
                {
                    UnitInfo s = c.Units[SerialKey(id)];
                    if (s.SourceSerialId.HasValue) Link(s.Key, null, s.SourceSerialId, new InputEdge { Via = "conversion" });
                    else if (s.SourceLotId.HasValue) Link(s.Key, s.SourceLotId, null, new InputEdge { Via = "conversion" });
                    if (s.LotId.HasValue) Link(s.Key, s.LotId, null, new InputEdge { Via = "lot" });
                }

                if (frontSerials.Count > 0)
                {
                    foreach (var a in await _uow.Set<AssemblyLink>().AsNoTracking().Where(a => !a.Deleted && a.RemovedAt == null && frontSerials.Contains(a.ParentSerialUnitId))
                        .Select(a => new { a.ParentSerialUnitId, a.ChildSerialUnitId, a.ChildLotId, a.Quantity }).ToListAsync(ct))
                        Link(SerialKey(a.ParentSerialUnitId), a.ChildLotId, a.ChildSerialUnitId, new InputEdge { Via = "assembly", Quantity = a.Quantity });
                    foreach (var k in await _uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && u.ParentSerialUnitId != null && frontSerials.Contains(u.ParentSerialUnitId.Value))
                        .Select(u => new { u.Id, Parent = u.ParentSerialUnitId.Value }).ToListAsync(ct))
                        Link(SerialKey(k.Parent), null, k.Id, new InputEdge { Via = "assembly", Quantity = 1 });
                }

                await EnsureUnitsAsync(c, newLots, newSerials, ct);
                frontLots = newLots.Where(i => visited.Add(LotKey(i))).ToList();
                frontSerials = newSerials.Where(i => visited.Add(SerialKey(i))).ToList();
                if (level == depth && (frontLots.Count > 0 || frontSerials.Count > 0)) c.Truncated = true;
            }
        }

        // ------------------------------------------------------------------------------------------ everything around the units

        /// <summary>
        /// The documents around the units in the context: purchase receipts (and the orders, inquiries and requests behind them), inspections, NCRs, traceability operations, files and customer deliveries.
        /// A fixed set of queries: it does not grow with the number of units.
        /// </summary>
        private async Task LoadAroundAsync(TreeCtx c, List<int> partItemIds, int? workOrderId, CancellationToken ct)
        {
            List<int> lotIds = c.Units.Values.Where(u => u.IsLot && !u.Untracked).Select(u => u.Id).ToList();
            List<int> serialIds = c.Units.Values.Where(u => !u.IsLot && !u.Untracked).Select(u => u.Id).ToList();

            c.Receipts = await ReceiptsAsync(lotIds, serialIds, ct);
            List<int> receiptDocs = c.Receipts.Select(r => r.DocumentId).Distinct().ToList();

            c.Inspections = (await _uow.Set<QcInspection>().AsNoTracking()
                .Where(i => !i.Deleted && ((i.LotId != null && lotIds.Contains(i.LotId.Value)) || receiptDocs.Contains(i.ReceiptDocumentId)))
                .Select(i => new InspInfo
                {
                    Id = i.Id, Number = i.Number, Status = i.Status, Accepted = i.AcceptedQuantity, Rejected = i.RejectedQuantity, ReportNumber = i.ReportNumber,
                    ReceiptDocumentId = i.ReceiptDocumentId, LotId = i.LotId, NcrId = i.NcrId, InspectedAt = i.InspectedAt,
                }).ToListAsync(ct));

            c.Ncrs = (await _uow.Set<NonConformance>().AsNoTracking()
                .Where(n => !n.Deleted && ((n.LotId != null && lotIds.Contains(n.LotId.Value)) || (workOrderId != null && n.WorkOrderId == workOrderId && partItemIds.Contains(n.ItemId))))
                .Select(n => new NcrInfo { Id = n.Id, Number = n.Number, Status = n.Status, Severity = n.Severity, ItemId = n.ItemId, LotId = n.LotId, Date = n.ReportDate, Description = n.Description, SerialNumbers = n.SerialNumbers })
                .ToListAsync(ct));

            c.PartOps = (await _uow.Set<PartOperation>().AsNoTracking()
                .Where(p => !p.Deleted && ((p.LotId != null && lotIds.Contains(p.LotId.Value)) || (p.SerialUnitId != null && serialIds.Contains(p.SerialUnitId.Value))))
                .Select(p => new PartOpInfo { Id = p.Id, Title = p.Title, Status = p.Status, Qc = p.QcStatus, QcAt = p.QcAt, SerialId = p.SerialUnitId, LotId = p.LotId, Sequence = p.Sequence, Note = p.QcNote }).ToListAsync(ct));

            // providers of the lots
            List<int> providerIds = c.Units.Values.Where(u => u.ProviderId.HasValue).Select(u => u.ProviderId.Value).Union(c.Receipts.Where(r => r.ProviderId.HasValue).Select(r => r.ProviderId.Value)).Distinct().ToList();
            if (providerIds.Count > 0)
                foreach (var p in await _uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => providerIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct))
                    c.Providers[p.Id] = p.Name;

            await EnsureItemsAsync(c, c.Ncrs.Select(n => n.ItemId), ct);
            await LoadFilesAsync(c, ct);

            // customer deliveries of the seeds (what was made, not what it was made of)
            var dl = new List<int>();
            foreach (ReverseDeliveryDto d in await DeliveriesAsync(c.SeedLots, c.SeedSerials, dl, ct))
            {
                if (!c.Deliveries.TryGetValue(d.NodeKey, out List<ReverseDeliveryDto> list)) c.Deliveries[d.NodeKey] = list = new List<ReverseDeliveryDto>();
                list.Add(d);
            }
        }

        private static TraceFileDto ToFile(AttachmentDto a, bool propagated) => new TraceFileDto
        {
            Id = a.Id, DocumentId = a.DocumentId, Title = a.Title, FileName = a.FileName, KindTitle = a.KindTitle, Revision = a.Revision, OwnerTitle = a.OwnerTitle, OwnerNumber = a.OwnerNumber,
            Propagated = propagated, Legacy = a.Legacy,
        };

        /// <summary>
        /// Files of every record in the context in ONE call of the attachment service (it applies the read rights of each owner type and hides the price kinds), plus the technical files of the purchase requests,
        /// which follow the chain to the receipt, the inspection and the NCR (PB3 propagation): they are read by everyone who reads a record downstream of the request.
        /// </summary>
        private async Task LoadFilesAsync(TreeCtx c, CancellationToken ct)
        {
            var owners = new List<KeyValuePair<AttachmentOwnerType, int>>();
            void Own(AttachmentOwnerType t, IEnumerable<int> ids) { foreach (int id in ids.Distinct().Where(i => i > 0)) owners.Add(new KeyValuePair<AttachmentOwnerType, int>(t, id)); }
            Own(AttachmentOwnerType.InventoryLot, c.Units.Values.Where(u => u.IsLot && !u.Untracked).Select(u => u.Id));
            Own(AttachmentOwnerType.SerialUnit, c.Units.Values.Where(u => !u.IsLot && !u.Untracked).Select(u => u.Id));
            Own(AttachmentOwnerType.WarehouseDocument, c.Receipts.Select(r => r.DocumentId));
            Own(AttachmentOwnerType.PurchaseOrder, c.Receipts.Where(r => r.PoId.HasValue).Select(r => r.PoId.Value));
            Own(AttachmentOwnerType.Inquiry, c.Receipts.Where(r => r.InquiryId.HasValue).Select(r => r.InquiryId.Value));
            Own(AttachmentOwnerType.QcInspection, c.Inspections.Select(i => i.Id));
            Own(AttachmentOwnerType.Ncr, c.Ncrs.Select(n => n.Id));
            Own(AttachmentOwnerType.PartOperation, c.PartOps.Select(p => p.Id));
            Own(AttachmentOwnerType.ProductionOperation, c.OpsByOrder.Values.SelectMany(o => o).Select(o => o.Id));
            if (owners.Count > 0)
                foreach (var kv in await _attachments.ForOwnersAsync(owners, ct))
                    c.Files[kv.Key] = kv.Value.Where(a => a.Current).Select(a => ToFile(a, false)).ToList();

            if (!_user.IsInRole(AttachmentRoles.TechnicalChain)) return;
            List<int> prIds = c.Receipts.Where(r => r.PrId.HasValue).Select(r => r.PrId.Value).Distinct().ToList();
            List<int> prLineIds = c.Receipts.Where(r => r.RequestLineId.HasValue).Select(r => r.RequestLineId.Value).Distinct().ToList();
            if (prIds.Count == 0 && prLineIds.Count == 0) return;
            var rows = await _uow.Set<RecordAttachment>().AsNoTracking().Where(a => !a.Deleted && a.RemovedAt == null && !a.Voided && a.SupersededById == null
                    && a.Kind != AttachmentKind.Invoice && a.Kind != AttachmentKind.PaymentSlip
                    && ((a.OwnerType == AttachmentOwnerType.PurchaseRequest && prIds.Contains(a.OwnerId)) || (a.OwnerType == AttachmentOwnerType.PurchaseRequestLine && prLineIds.Contains(a.OwnerId))))
                .OrderBy(a => a.Id).ToListAsync(ct);
            foreach (RecordAttachment a in rows)
            {
                var f = new TraceFileDto
                {
                    Id = a.Id, DocumentId = a.DocumentId, Title = a.Title, FileName = a.FileName, Revision = a.Revision, KindTitle = AttachmentText.KindTitle(a.Kind),
                    OwnerTitle = AttachmentText.OwnerTitle(a.OwnerType),
                };
                Dictionary<int, List<TraceFileDto>> target = a.OwnerType == AttachmentOwnerType.PurchaseRequest ? c.RequestFiles : c.RequestLineFiles;
                if (!target.TryGetValue(a.OwnerId, out List<TraceFileDto> list)) target[a.OwnerId] = list = new List<TraceFileDto>();
                list.Add(f);
            }
        }

        // ------------------------------------------------------------------------------------------ orders and operations

        private async Task LoadOrdersAsync(TreeCtx c, IQueryable<MfProductionOrder> orders, CancellationToken ct)
        {
            foreach (var o in await orders.Where(o => !o.Deleted).Select(o => new OrderInfo { Id = o.Id, Number = o.Number, WorkOrderId = o.WorkOrderId, ItemId = o.ItemId, Status = o.Status, Quantity = o.Quantity, Unit = o.Unit, ReceiptId = o.ProductionReceiptId }).ToListAsync(ct))
                c.Orders[o.Id] = o;
            List<int> orderIds = c.Orders.Keys.ToList();
            if (orderIds.Count == 0) return;
            var ops = await _uow.Set<MfOperation>().AsNoTracking().Where(o => !o.Deleted && orderIds.Contains(o.OrderId)).OrderBy(o => o.Sequence).ThenBy(o => o.Id)
                .Select(o => new OpInfo
                {
                    Id = o.Id, OrderId = o.OrderId, Sequence = o.Sequence, Title = o.Title, Status = o.Status, Good = o.GoodQty, Scrap = o.ScrapQty, Rework = o.ReworkQty, QcAccepted = o.QcAccepted,
                    QcNumber = o.QcRecord.Number, QcResult = (QcResult?)o.QcRecord.Result, NcrId = o.NcrId, NcrNumber = o.Ncr.Number, IsOutside = o.IsOutside, Concession = o.AcceptedByConcession,
                }).ToListAsync(ct);
            foreach (OpInfo op in ops)
            {
                if (!c.OpsByOrder.TryGetValue(op.OrderId, out List<OpInfo> list)) c.OpsByOrder[op.OrderId] = list = new List<OpInfo>();
                list.Add(op);
            }
        }
    }
}
