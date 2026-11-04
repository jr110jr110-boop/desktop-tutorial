using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Pc = Entities.Model.NasbNiro.ProjectControl;

namespace Services.NasbNiro.Attachments
{
    /// <summary>What the attachment layer needs to know about the record a file is attached to.</summary>
    public sealed class OwnerFacts
    {
        public bool Exists { get; set; }

        /// <summary>The number the people know the record by.</summary>
        public string Number { get; set; }

        /// <summary>Approved (or otherwise past the point where the uploader may take a file away): the files can only be voided or superseded.</summary>
        public bool Locked { get; set; }

        /// <summary>Cancelled or rejected: no new files.</summary>
        public bool Closed { get; set; }

        /// <summary>Roles that read the record's attachments; null = every signed-in user (the legacy modules are open).</summary>
        public string ReadRoles { get; set; }

        /// <summary>Roles of the owning module that add, void and supersede.</summary>
        public string WriteRoles { get; set; }

        /// <summary>When the record is locked, these roles (instead of <see cref="WriteRoles"/>) may still void and supersede, with a reason. Null = the same write roles.</summary>
        public string LockedWriteRoles { get; set; }

        /// <summary>The maker of the record. With <see cref="CreatorEditsWhileOpen"/>, the module lets only the maker (and the PMO) change the record while it is open, and so its files.</summary>
        public int? CreatorUserId { get; set; }

        public bool CreatorEditsWhileOpen { get; set; }

        /// <summary>The module's own edit rule applied to the files of the record: open + creator-only record = the maker or the PMO; locked = <see cref="LockedWriteRoles"/>; otherwise the write roles.</summary>
        public bool CanWrite(IInventoryUserContext user)
        {
            if (Closed) return false;
            if (Locked) return InRoles(user, LockedWriteRoles ?? WriteRoles);
            if (CreatorEditsWhileOpen) return user.IsInRole("PMO") || (CreatorUserId.HasValue && CreatorUserId.Value == user.UserId && user.UserId > 0 && InRoles(user, WriteRoles));
            return InRoles(user, WriteRoles);
        }

        private static bool InRoles(IInventoryUserContext user, string roles) => roles == null || user.IsInRole(roles);

        /// <summary>The purchase requests upstream of this record (empty when it is not downstream of one).</summary>
        public HashSet<int> PurchaseRequestIds { get; } = new HashSet<int>();

        /// <summary>The request lines upstream of this record; empty = every line of the requests above.</summary>
        public HashSet<int> PurchaseRequestLineIds { get; } = new HashSet<int>();
    }

    /// <summary>
    /// PB3: the one place that knows, for each kind of record, its number, whether it is approved (locked), who reads and who writes its attachments, and which purchase request lies upstream of it.
    /// The rules mirror the modules' own: an owner that is not here cannot get attachments.
    /// </summary>
    /// <summary>
    /// P-B7a: the owners whose rule is not a plain role list (a task is read by whoever sees the task; a progress report by its chain; a photo by the project readers).
    /// The implementation lives with the owning module and is asked for the facts of one record; a missing implementation fails closed (the record does not exist for the layer).
    /// </summary>
    public interface IAttachmentRecordRules
    {
        Task<OwnerFacts> FactsAsync(AttachmentOwnerType type, int id, CancellationToken cancellationToken);
    }

    public static class AttachmentOwners
    {
        /// <summary>The roles that read the records of one owner type (null = every signed-in user). The same values as in <see cref="FactsAsync"/>, without a database trip: the dossier decides step by step with it.</summary>
        public static string ReadRolesOf(AttachmentOwnerType type)
        {
            switch (type)
            {
                case AttachmentOwnerType.PurchaseRequest:
                case AttachmentOwnerType.PurchaseRequestLine:
                case AttachmentOwnerType.Inquiry:
                case AttachmentOwnerType.Quote:
                case AttachmentOwnerType.PurchaseOrder:
                case AttachmentOwnerType.SupplierInvoice:
                case AttachmentOwnerType.PaymentRequest:
                    return AttachmentRoles.Procurement;
                case AttachmentOwnerType.WarehouseDocument:
                case AttachmentOwnerType.QcInspection:
                case AttachmentOwnerType.Ncr:
                case AttachmentOwnerType.ProductionOperation:
                case AttachmentOwnerType.PartOperation:
                case AttachmentOwnerType.InventoryItem:
                case AttachmentOwnerType.InventoryLot:
                case AttachmentOwnerType.SerialUnit:
                    return AttachmentRoles.Operational;
                case AttachmentOwnerType.TreasuryPayment:
                    return AttachmentRoles.Finance;
                case AttachmentOwnerType.PettyCashSettlementLine:
                    return FinanceRoles.Readers;
                case AttachmentOwnerType.ProjectCostEntry:
                    return AttachmentRoles.Finance + ",PROJECTMANAGER,PLANNING";
                default:
                    return null;
            }
        }

        public static async Task<OwnerFacts> FactsAsync(IUnitOfWork uow, AttachmentOwnerType type, int id, CancellationToken ct, IAttachmentRecordRules rules = null)
        {
            var f = new OwnerFacts();
            switch (type)
            {
                case AttachmentOwnerType.TmTask:
                case AttachmentOwnerType.PcProgressSubmission:
                case AttachmentOwnerType.PcProjectPhoto:
                    return rules == null ? f : await rules.FactsAsync(type, id, ct);
                case AttachmentOwnerType.PurchaseRequest:
                {
                    var r = await uow.Set<PurchaseRequest>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.CreatorUserId }).FirstOrDefaultAsync(ct);
                    if (r == null) return f;
                    f.Exists = true; f.Number = r.Number;
                    // a request that was sent no longer loses a file: the creator or the PMO edits a draft; after it is sent only planning and the PMO void or replace, with a reason
                    f.Locked = r.Status != PurchaseRequestStatus.Draft; f.Closed = r.Status == PurchaseRequestStatus.Rejected;
                    f.ReadRoles = AttachmentRoles.Procurement; f.WriteRoles = AttachmentRoles.PurchaseRequestWrite; f.LockedWriteRoles = AttachmentRoles.PurchaseRequestLockedWrite;
                    f.CreatorUserId = r.CreatorUserId; f.CreatorEditsWhileOpen = true;
                    f.PurchaseRequestIds.Add(id);
                    return f;
                }
                case AttachmentOwnerType.PurchaseRequestLine:
                {
                    var l = await uow.Set<PurchaseRequestLine>().AsNoTracking().Where(x => x.Id == id)
                        .Select(x => new { x.PurchaseRequestId, x.RowNumber, x.PurchaseRequest.Number, x.PurchaseRequest.Status, x.PurchaseRequest.Deleted, x.PurchaseRequest.CreatorUserId }).FirstOrDefaultAsync(ct);
                    if (l == null || l.Deleted) return f;
                    f.Exists = true; f.Number = l.Number + " / " + l.RowNumber;
                    f.Locked = l.Status != PurchaseRequestStatus.Draft; f.Closed = l.Status == PurchaseRequestStatus.Rejected;
                    f.ReadRoles = AttachmentRoles.Procurement; f.WriteRoles = AttachmentRoles.PurchaseRequestWrite; f.LockedWriteRoles = AttachmentRoles.PurchaseRequestLockedWrite;
                    f.CreatorUserId = l.CreatorUserId; f.CreatorEditsWhileOpen = true;
                    f.PurchaseRequestIds.Add(l.PurchaseRequestId);
                    return f;
                }
                case AttachmentOwnerType.Inquiry:
                {
                    var q = await uow.Set<PurchaseInquiry>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.PurchaseRequestId }).FirstOrDefaultAsync(ct);
                    if (q == null) return f;
                    f.Exists = true; f.Number = q.Number;
                    f.Locked = q.Status == InquiryStatus.Approved || q.Status == InquiryStatus.Cancelled; f.Closed = q.Status == InquiryStatus.Cancelled;
                    f.ReadRoles = AttachmentRoles.Procurement; f.WriteRoles = AttachmentRoles.InquiryWrite;
                    f.PurchaseRequestIds.Add(q.PurchaseRequestId);
                    foreach (int line in await uow.Set<SupplierQuoteLine>().AsNoTracking().Where(l => l.Quote.InquiryId == id).Select(l => l.RequestLineId).Distinct().ToListAsync(ct)) f.PurchaseRequestLineIds.Add(line);
                    return f;
                }
                case AttachmentOwnerType.Quote:
                {
                    var q = await uow.Set<SupplierQuote>().AsNoTracking().Where(x => x.Id == id)
                        .Select(x => new { x.QuoteNumber, x.InquiryId, x.Inquiry.Number, x.Inquiry.Status, x.Inquiry.PurchaseRequestId, x.Inquiry.Deleted }).FirstOrDefaultAsync(ct);
                    if (q == null || q.Deleted) return f;
                    f.Exists = true; f.Number = q.Number + " / " + (q.QuoteNumber ?? ("#" + id));
                    f.Locked = q.Status == InquiryStatus.Approved || q.Status == InquiryStatus.Cancelled; f.Closed = q.Status == InquiryStatus.Cancelled;
                    f.ReadRoles = AttachmentRoles.Procurement; f.WriteRoles = AttachmentRoles.InquiryWrite;
                    f.PurchaseRequestIds.Add(q.PurchaseRequestId);
                    foreach (int line in await uow.Set<SupplierQuoteLine>().AsNoTracking().Where(l => l.QuoteId == id).Select(l => l.RequestLineId).Distinct().ToListAsync(ct)) f.PurchaseRequestLineIds.Add(line);
                    return f;
                }
                case AttachmentOwnerType.PurchaseOrder:
                {
                    var o = await uow.Set<PurchaseOrder>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status }).FirstOrDefaultAsync(ct);
                    if (o == null) return f;
                    f.Exists = true; f.Number = o.Number;
                    f.Locked = o.Status != PurchaseOrderStatus.Draft; f.Closed = o.Status == PurchaseOrderStatus.Cancelled;
                    f.ReadRoles = AttachmentRoles.Procurement; f.WriteRoles = AttachmentRoles.OrderWrite;
                    await AddOrderChainAsync(uow, f, new[] { id }, ct);
                    return f;
                }
                case AttachmentOwnerType.WarehouseDocument:
                {
                    var d = await uow.Set<InventoryDocument>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.Type, x.PurchaseOrderId }).FirstOrDefaultAsync(ct);
                    if (d == null) return f;
                    f.Exists = true; f.Number = string.IsNullOrWhiteSpace(d.Number) ? "پیش‌نویس " + id : d.Number;
                    f.Locked = d.Status != InventoryDocumentStatus.Draft; f.Closed = d.Status == InventoryDocumentStatus.Cancelled;
                    f.ReadRoles = AttachmentRoles.Operational; f.WriteRoles = AttachmentRoles.ReceiptWrite;
                    await AddOrderChainAsync(uow, f, await OrderIdsOfDocumentsAsync(uow, new[] { id }, ct), ct);
                    return f;
                }
                case AttachmentOwnerType.QcInspection:
                {
                    var i = await uow.Set<QcInspection>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.ReceiptDocumentId }).FirstOrDefaultAsync(ct);
                    if (i == null) return f;
                    f.Exists = true; f.Number = i.Number;
                    f.Locked = i.Status == InspectionStatus.Done;
                    f.ReadRoles = AttachmentRoles.Operational; f.WriteRoles = AttachmentRoles.InspectionWrite;
                    await AddOrderChainAsync(uow, f, await OrderIdsOfDocumentsAsync(uow, new[] { i.ReceiptDocumentId }, ct), ct);
                    return f;
                }
                case AttachmentOwnerType.Ncr:
                {
                    var n = await uow.Set<NonConformance>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.InspectionId, x.ReceiptDocumentId }).FirstOrDefaultAsync(ct);
                    if (n == null) return f;
                    f.Exists = true; f.Number = n.Number;
                    f.Locked = n.Status != NcrStatus.Open; f.Closed = n.Status == NcrStatus.Cancelled;
                    f.ReadRoles = AttachmentRoles.Operational; f.WriteRoles = AttachmentRoles.NcrWrite; f.LockedWriteRoles = AttachmentRoles.NcrLockedWrite;
                    var docs = new List<int>();
                    if (n.ReceiptDocumentId.HasValue) docs.Add(n.ReceiptDocumentId.Value);
                    if (n.InspectionId.HasValue)
                    {
                        int? receipt = await uow.Set<QcInspection>().AsNoTracking().Where(x => x.Id == n.InspectionId.Value).Select(x => (int?)x.ReceiptDocumentId).FirstOrDefaultAsync(ct);
                        if (receipt.HasValue) docs.Add(receipt.Value);
                    }
                    await AddOrderChainAsync(uow, f, await OrderIdsOfDocumentsAsync(uow, docs, ct), ct);
                    return f;
                }
                case AttachmentOwnerType.ProductionOperation:
                {
                    var o = await uow.Set<MfOperation>().AsNoTracking().Where(x => x.Id == id && !x.Deleted)
                        .Select(x => new { x.Sequence, x.Title, x.Status, x.PurchaseRequestId, x.PurchaseRequestLineId, x.OrderId }).FirstOrDefaultAsync(ct);
                    if (o == null) return f;
                    string orderNumber = await uow.Set<MfProductionOrder>().AsNoTracking().Where(m => m.Id == o.OrderId).Select(m => m.Number).FirstOrDefaultAsync(ct);
                    f.Exists = true; f.Number = (orderNumber ?? "") + " / " + o.Sequence + " " + o.Title;
                    f.Locked = o.Status == MfOperationStatus.Done || o.Status == MfOperationStatus.Rejected || o.Status == MfOperationStatus.Skipped;
                    f.ReadRoles = AttachmentRoles.Operational; f.WriteRoles = AttachmentRoles.ProductionWrite;
                    if (o.PurchaseRequestId.HasValue) f.PurchaseRequestIds.Add(o.PurchaseRequestId.Value);
                    if (o.PurchaseRequestLineId.HasValue) f.PurchaseRequestLineIds.Add(o.PurchaseRequestLineId.Value);
                    return f;
                }
                case AttachmentOwnerType.PartOperation:
                {
                    var o = await uow.Set<PartOperation>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Sequence, x.Title, x.Status }).FirstOrDefaultAsync(ct);
                    if (o == null) return f;
                    f.Exists = true; f.Number = o.Sequence + " " + o.Title;
                    f.Locked = o.Status == PartOperationStatus.Done || o.Status == PartOperationStatus.Rejected;
                    f.ReadRoles = AttachmentRoles.Operational; f.WriteRoles = AttachmentRoles.PartOperationWrite;
                    return f;
                }
                case AttachmentOwnerType.SupplierInvoice:
                {
                    var v = await uow.Set<SupplierInvoice>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.PurchaseOrderId }).FirstOrDefaultAsync(ct);
                    if (v == null) return f;
                    f.Exists = true; f.Number = v.Number;
                    f.Locked = v.Status == SupplierInvoiceStatus.Approved || v.Status == SupplierInvoiceStatus.Cancelled; f.Closed = v.Status == SupplierInvoiceStatus.Cancelled;
                    f.ReadRoles = AttachmentRoles.Procurement; f.WriteRoles = AttachmentRoles.InvoiceWrite;
                    await AddOrderChainAsync(uow, f, new[] { v.PurchaseOrderId }, ct);
                    return f;
                }
                case AttachmentOwnerType.PaymentRequest:
                {
                    var p = await uow.Set<PaymentRequest>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => new { x.Number, x.Status, x.PurchaseOrderId, InvoiceOrderId = x.Invoice == null ? (int?)null : x.Invoice.PurchaseOrderId }).FirstOrDefaultAsync(ct);
                    if (p == null) return f;
                    f.Exists = true; f.Number = p.Number;
                    f.Locked = p.Status != PaymentRequestStatus.Draft; f.Closed = p.Status == PaymentRequestStatus.Rejected;
                    f.ReadRoles = AttachmentRoles.Procurement; f.WriteRoles = AttachmentRoles.PaymentWrite;
                    var orders = new List<int>();
                    if (p.PurchaseOrderId.HasValue) orders.Add(p.PurchaseOrderId.Value);
                    if (p.InvoiceOrderId.HasValue) orders.Add(p.InvoiceOrderId.Value);
                    await AddOrderChainAsync(uow, f, orders, ct);
                    return f;
                }
                case AttachmentOwnerType.TreasuryPayment:
                {
                    var p = await uow.Set<FnPayment>().AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Number, x.Status }).FirstOrDefaultAsync(ct);
                    if (p == null) return f;
                    f.Exists = true; f.Number = string.IsNullOrWhiteSpace(p.Number) ? "پیش‌نویس " + id : p.Number;
                    f.Locked = p.Status != FnDocStatus.Draft; f.Closed = p.Status == FnDocStatus.Cancelled;
                    f.ReadRoles = AttachmentRoles.Finance; f.WriteRoles = AttachmentRoles.PaymentWrite;
                    return f;
                }
                case AttachmentOwnerType.ProjectCostEntry:
                {
                    var c = await uow.Set<CostEntry>().AsNoTracking().Where(x => x.Id == id).Select(x => new { x.SourceType, x.SourceNumber, x.Description }).FirstOrDefaultAsync(ct);
                    // only a cost written by hand needs its receipt or invoice; system costs come with the document of their own module
                    if (c == null || c.SourceType != "ProjectControl") return f;
                    f.Exists = true; f.Number = "هزینه #" + id;
                    f.Locked = await uow.Set<Pc.PcCostCheck>().AnyAsync(x => x.CostEntryId == id, ct);
                    f.ReadRoles = AttachmentRoles.Finance + ",PROJECTMANAGER,PLANNING"; f.WriteRoles = AttachmentRoles.CostWrite;
                    return f;
                }
                case AttachmentOwnerType.Contract:
                {
                    var c = await uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => x.Code).FirstOrDefaultAsync(ct);
                    return Legacy(f, c, AttachmentRoles.ContractWrite);
                }
                case AttachmentOwnerType.Tender:
                {
                    var c = await uow.Set<Entities.Model.NasbNiro.Tenders.Tender>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => x.Code).FirstOrDefaultAsync(ct);
                    return Legacy(f, c, AttachmentRoles.TenderWrite);
                }
                case AttachmentOwnerType.BuyOrder:
                {
                    var c = await uow.Set<Entities.Model.NasbNiro.Contracts.BuyOrder>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => x.Title).FirstOrDefaultAsync(ct);
                    return Legacy(f, c, AttachmentRoles.BuyOrderWrite);
                }
                case AttachmentOwnerType.Process:
                {
                    var c = await uow.Set<Entities.Model.NasbNiro.Contracts.ProcessEntity>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => x.Id).FirstOrDefaultAsync(ct);
                    return Legacy(f, c == 0 ? null : "فرایند " + c, AttachmentRoles.ProcessWrite);
                }
                case AttachmentOwnerType.EquipmentProcess:
                {
                    var c = await uow.Set<Entities.Model.NasbNiro.Equipments.EquipmentProcess>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => x.Id).FirstOrDefaultAsync(ct);
                    return Legacy(f, c == 0 ? null : "فرایند تجهیز " + c, AttachmentRoles.ProcessWrite);
                }
                case AttachmentOwnerType.PettyCashSettlementLine:
                {
                    var l = await uow.Set<FnPettyCashSettlementLine>().AsNoTracking().Where(x => x.Id == id).Select(x => new { x.RowNumber, x.Settlement.Number, x.Settlement.Status }).FirstOrDefaultAsync(ct);
                    if (l == null) return f;
                    f.Exists = true; f.Number = (l.Number ?? "تسویه") + " / " + l.RowNumber;
                    f.Locked = l.Status != FnDocStatus.Draft; f.Closed = l.Status == FnDocStatus.Cancelled;
                    f.ReadRoles = FinanceRoles.Readers; f.WriteRoles = AttachmentRoles.PettyCashWrite;
                    return f;
                }
                case AttachmentOwnerType.InventoryItem:
                {
                    var c = await uow.Set<InventoryItem>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => x.Code).FirstOrDefaultAsync(ct);
                    if (c == null) return f;
                    f.Exists = true; f.Number = c; f.ReadRoles = AttachmentRoles.Operational; f.WriteRoles = AttachmentRoles.StockMasterWrite;
                    return f;
                }
                case AttachmentOwnerType.InventoryLot:
                {
                    var c = await uow.Set<InventoryLot>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => x.LotNumber).FirstOrDefaultAsync(ct);
                    if (c == null) return f;
                    f.Exists = true; f.Number = c; f.Locked = true; f.ReadRoles = AttachmentRoles.Operational; f.WriteRoles = AttachmentRoles.StockMasterWrite;
                    return f;
                }
                case AttachmentOwnerType.SerialUnit:
                {
                    var c = await uow.Set<SerialUnit>().AsNoTracking().Where(x => x.Id == id && !x.Deleted).Select(x => x.SerialNumber).FirstOrDefaultAsync(ct);
                    if (c == null) return f;
                    f.Exists = true; f.Number = c; f.Locked = true; f.ReadRoles = AttachmentRoles.Operational; f.WriteRoles = AttachmentRoles.StockMasterWrite;
                    return f;
                }
                default:
                    return f;
            }
        }

        /// <summary>Contracts, tenders, buy orders and processes have no approval state in this layer: their files are always locked (void with a reason), never removed.</summary>
        private static OwnerFacts Legacy(OwnerFacts f, string number, string writeRoles)
        {
            if (number == null) return f;
            f.Exists = true; f.Number = number; f.Locked = true; f.ReadRoles = null; f.WriteRoles = writeRoles;
            return f;
        }

        /// <summary>The purchase orders a set of warehouse documents belongs to: the order on the document, the orders of its lines, and (for a QC release) the order of the receipt it releases.</summary>
        public static async Task<List<int>> OrderIdsOfDocumentsAsync(IUnitOfWork uow, IEnumerable<int> documentIds, CancellationToken ct)
        {
            var docs = documentIds.Distinct().ToList();
            var orders = new HashSet<int>();
            if (docs.Count == 0) return orders.ToList();
            foreach (int o in await uow.Set<InventoryDocument>().AsNoTracking().Where(d => docs.Contains(d.Id) && d.PurchaseOrderId != null).Select(d => d.PurchaseOrderId.Value).ToListAsync(ct)) orders.Add(o);
            foreach (int o in await uow.Set<InventoryDocumentLine>().AsNoTracking().Where(l => docs.Contains(l.DocumentId) && l.PurchaseOrderLineId != null)
                .Select(l => l.PurchaseOrderLine.PurchaseOrderId).ToListAsync(ct)) orders.Add(o);
            // a final (QC release) document: the receipt it releases
            List<int> receipts = await uow.Set<QcInspection>().AsNoTracking().Where(i => i.ReleaseDocumentId != null && docs.Contains(i.ReleaseDocumentId.Value)).Select(i => i.ReceiptDocumentId).ToListAsync(ct);
            if (receipts.Count > 0)
            {
                foreach (int o in await uow.Set<InventoryDocument>().AsNoTracking().Where(d => receipts.Contains(d.Id) && d.PurchaseOrderId != null).Select(d => d.PurchaseOrderId.Value).ToListAsync(ct)) orders.Add(o);
                foreach (int o in await uow.Set<InventoryDocumentLine>().AsNoTracking().Where(l => receipts.Contains(l.DocumentId) && l.PurchaseOrderLineId != null)
                    .Select(l => l.PurchaseOrderLine.PurchaseOrderId).ToListAsync(ct)) orders.Add(o);
            }
            return orders.ToList();
        }

        /// <summary>The purchase requests (and request lines) a set of purchase orders buys: through the inquiry of the order and through the request line of each order line.</summary>
        public static async Task AddOrderChainAsync(IUnitOfWork uow, OwnerFacts f, IEnumerable<int> orderIds, CancellationToken ct)
        {
            var orders = orderIds.Distinct().ToList();
            if (orders.Count == 0) return;
            foreach (int pr in await uow.Set<PurchaseOrder>().AsNoTracking().Where(o => orders.Contains(o.Id) && o.InquiryId != null).Select(o => o.Inquiry.PurchaseRequestId).ToListAsync(ct)) f.PurchaseRequestIds.Add(pr);
            var lines = await uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => orders.Contains(l.PurchaseOrderId) && l.RequestLineId != null)
                .Select(l => new { Line = l.RequestLineId.Value, l.RequestLine.PurchaseRequestId }).ToListAsync(ct);
            foreach (var l in lines) { f.PurchaseRequestIds.Add(l.PurchaseRequestId); f.PurchaseRequestLineIds.Add(l.Line); }
        }
    }
}
