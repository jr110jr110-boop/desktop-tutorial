using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.InventoryDtos;

namespace Services.NasbNiro.Sales
{
    /// <summary>
    /// What happens to a sales order when the warehouse delivers against it (the customer-delivery document names the order instead of a contract):
    /// the delivered quantities and the reservation move, and the system drafts the sales invoice. The warehouse document service calls these methods
    /// inside its own confirm and cancel, so the stock and the order change in one save. The QC loading approval of the document is unchanged.
    /// </summary>
    internal static class SlFulfilment
    {
        private const string DocumentTypeError = "سفارش فروش فقط در سند تحویل به مشتری می‌آید.";

        /// <summary>Checks a draft delivery document against its sales order. Returns a Persian message, or null when it is fine.</summary>
        public static async Task<string> CheckDraftAsync(IUnitOfWork uow, SaveDocumentDto dto, List<SaveDocumentLineDto> lines, CancellationToken ct)
        {
            if (!dto.SalesOrderId.HasValue)
                return lines.Any(l => l.SalesOrderLineId.HasValue) ? "ردیف سفارش فروش بدون انتخاب سفارش فروش." : null;
            if (dto.Type != InventoryDocumentType.CustomerDelivery) return DocumentTypeError;
            if (dto.ContractId.HasValue) return "تحویل یا برای قرارداد است یا برای سفارش فروش، نه هر دو.";
            SlSalesOrder order = await uow.Set<SlSalesOrder>().AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == dto.SalesOrderId.Value && !o.Deleted, ct);
            if (order == null) return "سفارش فروش پیدا نشد.";
            if (order.Status != SlOrderStatus.Confirmed && order.Status != SlOrderStatus.PartlyDelivered)
                return $"سفارش فروش {order.Number} در وضعیت «{SlText.OrderStatus(order.Status)}» است و تحویل ندارد.";
            var asked = new Dictionary<int, decimal>();
            int n = 0;
            foreach (SaveDocumentLineDto l in lines)
            {
                n++;
                string row = $"ردیف {n}: ";
                if (!l.SalesOrderLineId.HasValue) return row + "ردیف سفارش فروش را انتخاب کنید.";
                SlSalesOrderLine line = order.Lines.FirstOrDefault(x => x.Id == l.SalesOrderLineId.Value);
                if (line == null) return row + "این ردیف مال سفارش فروش انتخاب‌شده نیست.";
                if (line.ItemId != l.ItemId) return row + "کالای ردیف با کالای سفارش فروش یکی نیست.";
                asked[line.Id] = asked.GetValueOrDefault(line.Id) + l.Quantity;
                if (asked[line.Id] > line.Quantity - line.DeliveredQuantity)
                    return row + $"تعداد تحویل ({SlText.Number(asked[line.Id])}) از مانده سفارش ({SlText.Number(line.Quantity - line.DeliveredQuantity)}) بیشتر است.";
            }
            return null;
        }

        /// <summary>State check at confirm: the order must still be open for delivery.</summary>
        public static async Task<string> CheckConfirmAsync(IUnitOfWork uow, InventoryDocument document, CancellationToken ct)
        {
            SlSalesOrder order = await uow.Set<SlSalesOrder>().AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == document.SalesOrderId.Value && !o.Deleted, ct);
            if (order == null) return "سفارش فروش پیدا نشد.";
            if (order.Status != SlOrderStatus.Confirmed && order.Status != SlOrderStatus.PartlyDelivered)
                return $"سفارش فروش {order.Number} در وضعیت «{SlText.OrderStatus(order.Status)}» است و تحویل ندارد.";
            foreach (var g in document.Lines.GroupBy(l => l.SalesOrderLineId))
            {
                SlSalesOrderLine line = order.Lines.FirstOrDefault(x => x.Id == g.Key);
                if (line == null) return "ردیف سند به ردیف سفارش فروش وصل نیست.";
                decimal q = g.Sum(l => l.Quantity);
                if (q > line.Quantity - line.DeliveredQuantity) return $"تعداد تحویل ردیف {line.RowNumber} سفارش ({SlText.Number(q)}) از مانده سفارش ({SlText.Number(line.Quantity - line.DeliveredQuantity)}) بیشتر است.";
            }
            return null;
        }

        private static void Recompute(SlSalesOrder order, int userId, DateTimeOffset now)
        {
            if (order.Status == SlOrderStatus.Draft || order.Status == SlOrderStatus.Cancelled || order.Status == SlOrderStatus.Closed) return;
            bool all = order.Lines.All(l => l.DeliveredQuantity >= l.Quantity);
            bool any = order.Lines.Any(l => l.DeliveredQuantity > 0);
            bool invoiced = any && order.Lines.All(l => l.InvoicedQuantity >= l.DeliveredQuantity);
            order.Status = all ? (invoiced ? SlOrderStatus.Invoiced : SlOrderStatus.Delivered) : any ? SlOrderStatus.PartlyDelivered : SlOrderStatus.Confirmed;
            order.ModifyDate = now;
            order.ModifierUserId = userId;
        }

        /// <summary>Called by the confirm of a delivery document (its number is already set). Changes are made in memory; the caller saves.</summary>
        public static async Task ApplyConfirmAsync(IUnitOfWork uow, int userId, InventoryDocument document, CancellationToken ct)
        {
            SlSalesOrder order = await uow.Set<SlSalesOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == document.SalesOrderId.Value, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            var parts = new List<(SlSalesOrderLine Line, decimal Quantity)>();
            foreach (InventoryDocumentLine dl in document.Lines.OrderBy(l => l.RowNumber))
            {
                SlSalesOrderLine line = order.Lines.First(l => l.Id == dl.SalesOrderLineId);
                line.DeliveredQuantity += dl.Quantity;
                line.ReservedQuantity = Math.Max(0, line.ReservedQuantity - dl.Quantity);
                parts.Add((line, dl.Quantity));
            }
            var delivery = new SlOrderDelivery { SalesOrderId = order.Id, DocumentId = document.Id, CreateDate = now, CreatorUserId = userId, ModifyDate = now, ModifierUserId = userId };
            uow.Set<SlOrderDelivery>().Add(delivery);
            await DraftInvoiceAsync(uow, userId, order, delivery, document.Number, document.DocumentDate, parts, ct);
            Recompute(order, userId, now);
        }

        /// <summary>Drafts the sales invoice of one delivery. Without a VAT rate in the table no invoice is made and the delivery says why.</summary>
        private static async Task DraftInvoiceAsync(IUnitOfWork uow, int userId, SlSalesOrder order, SlOrderDelivery delivery, string documentNumber, DateTime date,
            List<(SlSalesOrderLine Line, decimal Quantity)> parts, CancellationToken ct)
        {
            decimal? rate = await TaxRates.RateAtAsync(uow, FnTaxKind.Vat, date.Date, ct);
            if (!rate.HasValue)
            {
                delivery.DraftNote = "نرخ مالیات بر ارزش افزوده برای این تاریخ در جدول نرخ‌ها ثبت نشده است؛ پیش‌نویس فاکتور ساخته نشد. پس از ثبت نرخ، «پیش‌نویس فاکتور» سفارش را بزنید.";
                return;
            }
            FnCustomer customer = await uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CustomerId, ct);
            if (customer == null || !customer.IsActive)
            {
                delivery.DraftNote = "مشتری پیدا نشد یا غیرفعال است؛ پیش‌نویس فاکتور ساخته نشد.";
                return;
            }
            int? termDays = await uow.Set<SlCustomerProfile>().AsNoTracking().Where(p => p.CustomerId == order.CustomerId && !p.Deleted).Select(p => p.PaymentTermDays).FirstOrDefaultAsync(ct);
            List<int> itemIds = parts.Select(p => p.Line.ItemId).Distinct().ToList();
            Dictionary<int, InventoryItem> items = await uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            var invoice = new FnSalesInvoice
            {
                Number = await InventoryNumbers.NextNumberAsync(uow, FinanceNumbers.SalesInvoice, "SI", date.Date, ct), Date = date.Date, DueDate = termDays.HasValue ? date.Date.AddDays(termDays.Value) : (DateTime?)null,
                CustomerId = order.CustomerId, Type = FnInvoiceType.Goods, Status = FnDocStatus.Draft, CreatedByUserId = userId, CreatedAt = DateTimeOffset.Now, EditorUserIds = LedgerMath.WithEditor(null, userId),
                Description = $"پیش‌نویس خودکار از تحویل {documentNumber} سفارش فروش {order.Number}", Lines = new List<FnSalesInvoiceLine>(),
            };
            int row = 1;
            foreach (var (line, quantity) in parts)
            {
                InventoryItem item = items.GetValueOrDefault(line.ItemId);
                var (_, discount, amount) = SlText.LineAmount(quantity, line.UnitPrice, line.DiscountPercent);
                decimal vat = SlText.R0(amount * rate.Value / 100m);
                invoice.Lines.Add(new FnSalesInvoiceLine
                {
                    RowNumber = row++, Description = SlText.Clean(line.Description) ?? item?.Title ?? "کالا", ItemCode = item?.Code, Quantity = quantity, Unit = item?.Unit, UnitPrice = line.UnitPrice,
                    Discount = discount, Amount = amount, VatRatePercent = rate.Value, VatAmount = vat, Total = amount + vat,
                });
                line.InvoicedQuantity += quantity;
            }
            invoice.NetAmount = invoice.Lines.Sum(l => l.Amount);
            invoice.DiscountAmount = invoice.Lines.Sum(l => l.Discount);
            invoice.VatAmount = invoice.Lines.Sum(l => l.VatAmount);
            invoice.GrossAmount = invoice.NetAmount + invoice.VatAmount;
            invoice.ReceivableAmount = invoice.GrossAmount;
            uow.Set<FnSalesInvoice>().Add(invoice);
            delivery.InvoiceId = invoice.Id;
            delivery.DraftNote = null;
        }

        /// <summary>Called by the cancel of a confirmed delivery document. Returns a Persian refusal when the invoice is already approved or posted.</summary>
        public static async Task<string> ApplyCancelAsync(IUnitOfWork uow, int userId, InventoryDocument document, CancellationToken ct)
        {
            SlOrderDelivery delivery = await uow.Set<SlOrderDelivery>().FirstOrDefaultAsync(d => d.DocumentId == document.Id && !d.Deleted && !d.Reversed, ct);
            if (delivery == null) return null;
            FnSalesInvoice invoice = delivery.InvoiceId.HasValue ? await uow.Set<FnSalesInvoice>().FirstOrDefaultAsync(i => i.Id == delivery.InvoiceId.Value, ct) : null;
            if (invoice != null && invoice.Status != FnDocStatus.Draft && invoice.Status != FnDocStatus.Cancelled)
                return $"فاکتور فروش {invoice.Number} این تحویل تأیید یا ثبت شده است؛ ابتدا مالی باید آن را برگرداند.";
            SlSalesOrder order = await uow.Set<SlSalesOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == delivery.SalesOrderId, ct);
            List<InventoryDocumentLine> lines = await uow.Set<InventoryDocumentLine>().AsNoTracking().Where(l => l.DocumentId == document.Id).ToListAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            bool open = order.Status == SlOrderStatus.Confirmed || order.Status == SlOrderStatus.PartlyDelivered || order.Status == SlOrderStatus.Delivered || order.Status == SlOrderStatus.Invoiced;
            foreach (InventoryDocumentLine dl in lines.Where(l => l.SalesOrderLineId.HasValue))
            {
                SlSalesOrderLine line = order.Lines.FirstOrDefault(l => l.Id == dl.SalesOrderLineId);
                if (line == null) continue;
                line.DeliveredQuantity = Math.Max(0, line.DeliveredQuantity - dl.Quantity);
                if (invoice != null && invoice.Status == FnDocStatus.Draft) line.InvoicedQuantity = Math.Max(0, line.InvoicedQuantity - dl.Quantity);
                if (open) line.ReservedQuantity = line.Quantity - line.DeliveredQuantity;
            }
            if (invoice != null && invoice.Status == FnDocStatus.Draft)
            {
                invoice.Status = FnDocStatus.Cancelled;
                invoice.Description = (invoice.Description ?? "") + " (باطل شد: سند تحویل باطل شد)";
            }
            delivery.Reversed = true;
            delivery.ModifyDate = now;
            delivery.ModifierUserId = userId;
            Recompute(order, userId, now);
            return null;
        }

        /// <summary>For the order's deliveries without an invoice (and not reversed) drafts the invoice now. Returns a Persian message when nothing could be drafted.</summary>
        public static async Task<string> DraftMissingInvoicesAsync(IUnitOfWork uow, int userId, int orderId, CancellationToken ct)
        {
            SlSalesOrder order = await uow.Set<SlSalesOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && !o.Deleted, ct);
            if (order == null) return "سفارش فروش پیدا نشد.";
            List<SlOrderDelivery> pending = await uow.Set<SlOrderDelivery>().Where(d => d.SalesOrderId == orderId && !d.Deleted && !d.Reversed && d.InvoiceId == null).OrderBy(d => d.Id).ToListAsync(ct);
            if (pending.Count == 0) return "تحویلی بدون پیش‌نویس فاکتور برای این سفارش نیست.";
            DateTimeOffset now = DateTimeOffset.Now;
            int made = 0;
            foreach (SlOrderDelivery d in pending)
            {
                InventoryDocument doc = await uow.Set<InventoryDocument>().AsNoTracking().Include(x => x.Lines).FirstAsync(x => x.Id == d.DocumentId, ct);
                var parts = new List<(SlSalesOrderLine, decimal)>();
                foreach (InventoryDocumentLine dl in doc.Lines.OrderBy(l => l.RowNumber))
                {
                    SlSalesOrderLine line = order.Lines.FirstOrDefault(l => l.Id == dl.SalesOrderLineId);
                    if (line != null) parts.Add((line, dl.Quantity));
                }
                await DraftInvoiceAsync(uow, userId, order, d, doc.Number, doc.DocumentDate, parts, ct);
                if (d.InvoiceId.HasValue) made++;
                d.ModifyDate = now;
                d.ModifierUserId = userId;
            }
            if (made == 0) return pending.Select(p => p.DraftNote).FirstOrDefault(n => n != null) ?? "پیش‌نویس فاکتور ساخته نشد.";
            Recompute(order, userId, now);
            return null;
        }
    }
}
