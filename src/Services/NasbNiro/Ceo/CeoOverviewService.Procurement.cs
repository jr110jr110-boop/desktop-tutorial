using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Ceo
{
    // Section 7: procurement. Open purchase orders, late order lines, the gates waiting (requests, invoices, payments), blocked suppliers;
    // the biggest suppliers of the year and the value ordered against the value paid by month.
    public sealed partial class CeoOverviewService
    {
        /// <summary>How many suppliers the spend chart shows.</summary>
        private const int SupplierChartCount = 6;

        private async Task<CeoSectionDto> ProcurementAsync(CeoContext ctx, CancellationToken ct)
        {
            CeoDashboardDto b = (await PartsAsync(ctx, ct)).Dashboard;

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.OpenPurchaseOrdersAmount, b.OpenPurchaseOrdersAmount, null,
                CeoText.Number(b.OpenPurchaseOrders) + " سفارش تأییدشده، صادرشده یا نیمه‌تحویل"), higherIsBetter: null));
            section.Kpis.Add(Kpi(ctx, "lateOrderLines", b.LateOrderLines, CeoRules.ByAny(b.LateOrderLines, 10),
                "سفارش‌های صادرشده که تاریخ تحویل ردیف آن‌ها گذشته و کامل تحویل نشده است."));
            section.Kpis.Add(Kpi(ctx, "purchaseRequestsWaiting", b.PurchaseRequestsWaitingApproval, CeoRules.ByCount(b.PurchaseRequestsWaitingApproval, 5, 15),
                "همه‌ی درخواست‌های باز (ارسال‌شده یا تأییدشده): " + CeoText.Number(b.OpenPurchaseRequests)));
            section.Kpis.Add(Kpi(ctx, "invoicesWaiting", b.InvoicesWaitingApproval, CeoRules.ByCount(b.InvoicesWaitingApproval, 5, 15),
                "دریافت‌شده و هنوز فاکتورنشده: " + CeoText.ShortRial(b.ReceivedNotInvoiced)));
            section.Kpis.Add(Kpi(ctx, "paymentsWaiting", b.PaymentsWaitingApproval, CeoRules.ByCount(b.PaymentsWaitingApproval, 5, 15),
                "مبلغ خالص: " + CeoText.ShortRial(b.PaymentsWaitingAmount)));
            section.Kpis.Add(Kpi(ctx, "blockedSuppliers", b.BlockedSuppliers, CeoRules.WarnIfAny(b.BlockedSuppliers),
                "ارزیابی دوره‌ای سررسیدشده: " + CeoText.Number(b.SupplierEvaluationsDue)));

            // chart 1: the biggest suppliers by the value of the purchase orders of the last twelve months (not drafts, not cancelled)
            var spend = await _uow.Set<PurchaseOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.OrderDate >= ctx.From && o.OrderDate < ctx.End && o.Status != PurchaseOrderStatus.Draft && o.Status != PurchaseOrderStatus.Cancelled)
                .GroupBy(o => new { o.ProviderId, o.Provider.Name })
                .Select(g => new { g.Key.ProviderId, g.Key.Name, Amount = g.Sum(o => o.TotalAmount) })
                .OrderByDescending(x => x.Amount).ThenBy(x => x.ProviderId).Take(SupplierChartCount).ToListAsync(ct);
            section.Charts.Add(Chart("spendBySupplier", "بیشترین خرید از تأمین‌کنندگان در دوازده ماه", CeoKind.HBar, CeoUnit.Rial, spend.Select(s => s.Name), CeoLinks.ProcurementOrders, CeoDrillRef.PrSpendBySupplier,
                "ارزش سفارش‌های خرید تأییدشده تا بسته‌شده؛ حداکثر " + CeoText.Number(SupplierChartCount) + " تأمین‌کننده.",
                Series("ارزش سفارش‌ها", spend.Select(s => s.Amount))));

            // chart 2: the value ordered by order month against the net value paid by payment month. The drill behind it lists the payments still waiting and has no month filter, so it carries no period.
            var orders = await _uow.Set<PurchaseOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.OrderDate >= ctx.From && o.OrderDate < ctx.End && o.Status != PurchaseOrderStatus.Draft && o.Status != PurchaseOrderStatus.Cancelled)
                .Select(o => new { o.OrderDate, o.TotalAmount }).ToListAsync(ct);
            DateTimeOffset from = new DateTimeOffset(ctx.From);
            var payments = await _uow.Set<PaymentRequest>().AsNoTracking()
                .Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Paid && p.PaidAt != null && p.PaidAt >= from)
                .Select(p => new { p.PaidAt, p.Amount, p.Deductions }).ToListAsync(ct);
            section.Charts.Add(Chart("ordersAndPayments", "سفارش‌های خرید و پرداخت‌ها به تفکیک ماه", CeoKind.Bar, CeoUnit.Rial, ctx.Labels, CeoLinks.ProcurementPayments, CeoDrillRef.PrPaymentsWaiting,
                "سفارش بر پایه‌ی تاریخ سفارش و پرداخت بر پایه‌ی تاریخ پرداخت (مبلغ خالص پس از کسورات).",
                Series("سفارش‌های خرید", CeoTrends.SumByMonth(ctx.Window, orders.Select(o => (o.OrderDate, o.TotalAmount))), CeoRole.Primary),
                Series("پرداخت‌شده", CeoTrends.SumByMonth(ctx.Window, payments.Select(p => (p.PaidAt.Value.LocalDateTime.Date, p.Amount - p.Deductions))), CeoRole.Secondary)));
            return section;
        }
    }
}
