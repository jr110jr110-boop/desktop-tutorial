using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Trace
{
    public sealed partial class TraceCenterService
    {
        /// <summary>One purchase receipt line that brought a lot or a serial piece in, with the purchase order and request behind it.</summary>
        internal sealed class ReceiptRow
        {
            public int? LotId;
            public int? SerialUnitId;
            public int DocumentId;
            public string DocumentNumber;
            public DateTime DocumentDate;
            public int? ProviderId;
            public string ProviderName;
            public decimal Quantity;
            public decimal? LineUnitPrice;
            public int? PoId;
            public string PoNumber;
            public string PoStatus;
            public int? PoLineId;
            public decimal? PoUnitPrice;
            public int? RequestLineId;
            public int? PrId;
            public string PrNumber;
            public string PrStatus;
            public int? InquiryId;
            public string InquiryNumber;
            public string InquiryStatus;
        }

        /// <summary>
        /// The purchase receipts of many lots and serial pieces, with their purchase orders, inquiries and requests: six queries however many lots (receipt lines of lots, receipt lines of serials, orders, requests,
        /// order-line requests, inquiries).
        /// </summary>
        internal async Task<List<ReceiptRow>> ReceiptsAsync(List<int> lotIds, List<int> serialIds, CancellationToken ct)
        {
            var rows = new List<ReceiptRow>();
            IQueryable<InventoryDocumentLine> lines = _uow.Set<InventoryDocumentLine>().AsNoTracking()
                .Where(l => !l.Document.Deleted && l.Document.Status == InventoryDocumentStatus.Confirmed && l.Document.Type == InventoryDocumentType.PurchaseReceipt);
            if (lotIds.Count > 0)
            {
                var found = await lines.Where(l => l.LotId != null && lotIds.Contains(l.LotId.Value)).Select(l => new
                {
                    l.LotId, l.Quantity, LineUnitPrice = l.UnitPrice, DocumentId = l.DocumentId, l.Document.Number, l.Document.DocumentDate, l.Document.ProviderId, ProviderName = l.Document.Provider.Name,
                    HeaderPo = l.Document.PurchaseOrderId, l.PurchaseOrderLineId, LinePo = (int?)l.PurchaseOrderLine.PurchaseOrderId, PoUnitPrice = (decimal?)l.PurchaseOrderLine.UnitPrice,
                    RequestLineId = l.PurchaseOrderLine.RequestLineId,
                }).ToListAsync(ct);
                rows.AddRange(found.Select(f => new ReceiptRow
                {
                    LotId = f.LotId, DocumentId = f.DocumentId, DocumentNumber = f.Number, DocumentDate = f.DocumentDate, ProviderId = f.ProviderId, ProviderName = f.ProviderName, Quantity = f.Quantity,
                    LineUnitPrice = f.LineUnitPrice, PoId = f.LinePo ?? f.HeaderPo, PoLineId = f.PurchaseOrderLineId, PoUnitPrice = f.PoUnitPrice, RequestLineId = f.RequestLineId,
                }));
            }
            if (serialIds.Count > 0)
            {
                var found = await _uow.Set<InventoryDocumentLineSerial>().AsNoTracking()
                    .Where(s => s.SerialUnitId != null && serialIds.Contains(s.SerialUnitId.Value) && !s.Line.Document.Deleted && s.Line.Document.Status == InventoryDocumentStatus.Confirmed
                                && s.Line.Document.Type == InventoryDocumentType.PurchaseReceipt)
                    .Select(s => new
                    {
                        s.SerialUnitId, LineUnitPrice = s.Line.UnitPrice, DocumentId = s.Line.DocumentId, s.Line.Document.Number, s.Line.Document.DocumentDate, s.Line.Document.ProviderId,
                        ProviderName = s.Line.Document.Provider.Name, HeaderPo = s.Line.Document.PurchaseOrderId, s.Line.PurchaseOrderLineId,
                        LinePo = (int?)s.Line.PurchaseOrderLine.PurchaseOrderId, PoUnitPrice = (decimal?)s.Line.PurchaseOrderLine.UnitPrice, RequestLineId = s.Line.PurchaseOrderLine.RequestLineId,
                    }).ToListAsync(ct);
                rows.AddRange(found.Select(f => new ReceiptRow
                {
                    SerialUnitId = f.SerialUnitId, DocumentId = f.DocumentId, DocumentNumber = f.Number, DocumentDate = f.DocumentDate, ProviderId = f.ProviderId, ProviderName = f.ProviderName,
                    Quantity = 1, LineUnitPrice = f.LineUnitPrice, PoId = f.LinePo ?? f.HeaderPo, PoLineId = f.PurchaseOrderLineId, PoUnitPrice = f.PoUnitPrice, RequestLineId = f.RequestLineId,
                }));
            }
            if (rows.Count == 0) return rows;

            List<int> poIds = rows.Where(r => r.PoId.HasValue).Select(r => r.PoId.Value).Distinct().ToList();
            var orders = poIds.Count == 0 ? null : (await _uow.Set<PurchaseOrder>().AsNoTracking().Where(o => poIds.Contains(o.Id))
                .Select(o => new { o.Id, o.Number, o.Status, o.InquiryId, InquiryNumber = o.Inquiry.Number, InquiryStatus = (InquiryStatus?)o.Inquiry.Status, InquiryPr = (int?)o.Inquiry.PurchaseRequestId }).ToListAsync(ct)).ToDictionary(o => o.Id);

            List<int> requestLineIds = rows.Where(r => r.RequestLineId.HasValue).Select(r => r.RequestLineId.Value).Distinct().ToList();
            var requestLines = requestLineIds.Count == 0 ? new Dictionary<int, int>()
                : (await _uow.Set<PurchaseRequestLine>().AsNoTracking().Where(l => requestLineIds.Contains(l.Id)).Select(l => new { l.Id, l.PurchaseRequestId }).ToListAsync(ct)).ToDictionary(l => l.Id, l => l.PurchaseRequestId);

            foreach (ReceiptRow r in rows)
            {
                if (r.PoId.HasValue && orders != null && orders.TryGetValue(r.PoId.Value, out var po))
                {
                    r.PoNumber = po.Number; r.PoStatus = TraceLabels.PurchaseOrder(po.Status); r.InquiryId = po.InquiryId; r.InquiryNumber = po.InquiryNumber; r.InquiryStatus = po.InquiryStatus.HasValue ? TraceLabels.Inquiry(po.InquiryStatus.Value) : null;
                    if (po.InquiryPr.HasValue) r.PrId = po.InquiryPr;
                }
                if (r.RequestLineId.HasValue && requestLines.TryGetValue(r.RequestLineId.Value, out int prId)) r.PrId = prId;
            }
            List<int> prIds = rows.Where(r => r.PrId.HasValue).Select(r => r.PrId.Value).Distinct().ToList();
            if (prIds.Count > 0)
            {
                var prs = (await _uow.Set<PurchaseRequest>().AsNoTracking().Where(p => prIds.Contains(p.Id)).Select(p => new { p.Id, p.Number, p.Status }).ToListAsync(ct)).ToDictionary(p => p.Id);
                foreach (ReceiptRow r in rows.Where(r => r.PrId.HasValue))
                    if (prs.TryGetValue(r.PrId.Value, out var pr)) { r.PrNumber = pr.Number; r.PrStatus = TraceLabels.PurchaseRequest(pr.Status); }
            }
            return rows;
        }
    }
}
