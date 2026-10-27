using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Ceo
{
    // Section 6: inventory. Stock value, items below their minimum, quarantine, material requests waiting for the storekeeper;
    // the stock value by warehouse, the purchased and the consumed material by month.
    public sealed partial class CeoOverviewService
    {
        /// <summary>How many warehouses the value chart names; the rest is added up as "other warehouses".</summary>
        private const int WarehouseChartCount = 8;

        private async Task<CeoSectionDto> InventoryAsync(CeoContext ctx, CancellationToken ct)
        {
            CeoDashboardDto b = (await PartsAsync(ctx, ct)).Dashboard;

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.StockValue, b.StockValue, null,
                "به بهای میانگین موزون کالاها؛ اموال مشتری شمرده نمی‌شود."), higherIsBetter: null));
            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.ItemsBelowMinimum, b.ItemsBelowMinimum, CeoRules.WarnIfAny(b.ItemsBelowMinimum),
                "کالاهای فعال دارای حداقل موجودی که مانده‌ی آن‌ها (بدون انبار ضایعات) از حداقل کمتر است."), higherIsBetter: false));
            section.Kpis.Add(Kpi(ctx, "quarantineLines", b.QuarantineLines, CeoRules.ByCount(b.QuarantineLines, 10, 30),
                "موجودی منتظر تصمیم کنترل کیفیت در انبارهای قرنطینه"));
            section.Kpis.Add(Kpi(ctx, "openMaterialRequests", b.OpenMaterialRequests, CeoRules.ByCount(b.OpenMaterialRequests, 15, 40),
                "ارسال‌شده یا تأییدشده و هنوز حواله‌ی آن‌ها کامل نشده است."));

            // chart 1: the stock value by warehouse (the biggest eight, the rest together)
            List<CeoWarehouseValue> warehouses = await CeoFigures.StockValueByWarehouseAsync(_uow, ct);
            List<CeoWarehouseValue> shown = warehouses.Take(WarehouseChartCount).ToList();
            var labels = shown.Select(w => w.Title).ToList();
            var values = shown.Select(w => w.Value).ToList();
            decimal others = warehouses.Skip(WarehouseChartCount).Sum(w => w.Value);
            if (others > 0m)
            {
                labels.Add("سایر انبارها");
                values.Add(others);
            }
            section.Charts.Add(Chart("stockValueByWarehouse", "ارزش موجودی به تفکیک انبار", CeoKind.HBar, CeoUnit.Rial, labels, CeoLinks.Warehouses, CeoDrillRef.InvStockValueByWarehouse,
                WarehouseValueNote(warehouses.Sum(w => w.Value), b.StockValue),
                Series("ارزش موجودی", values)));

            // chart 2: purchase receipts (net of the returns to the supplier) against the material consumed by production (net of the returns from production), by month,
            // valued at the cost of the movements. The drill behind it lists the items moved most in a period; it takes the month so far, and its kind is receipt
            // (the first series: its netValue is the figure of the chart); the consumption is the kind issue of the same drill.
            var flow = await _uow.Set<StockTransaction>().AsNoTracking()
                .Where(t => t.TransactionDate >= ctx.From && t.TransactionDate < ctx.End &&
                    (t.Document.Type == InventoryDocumentType.PurchaseReceipt || t.Document.Type == InventoryDocumentType.SupplierReturn ||
                     t.Document.Type == InventoryDocumentType.Issue || t.Document.Type == InventoryDocumentType.ProductionReturn))
                .GroupBy(t => new { Day = t.TransactionDate.Date, Type = t.Document.Type })
                .Select(g => new { g.Key.Day, g.Key.Type, Amount = g.Sum(t => t.CostAmount ?? 0m) }).ToListAsync(ct);
            // a receipt is stored with a positive amount and a return to the supplier with a negative one: the receipts are the plain sum of the two;
            // an issue is stored with a negative amount and a return from production with a positive one: the consumption is the negated sum
            bool IsReceipt(InventoryDocumentType type) => type == InventoryDocumentType.PurchaseReceipt || type == InventoryDocumentType.SupplierReturn;
            decimal[] received = CeoTrends.SumByMonth(ctx.Window, flow.Where(f => IsReceipt(f.Type)).Select(f => (f.Day, f.Amount)));
            decimal[] consumed = CeoTrends.SumByMonth(ctx.Window, flow.Where(f => !IsReceipt(f.Type)).Select(f => (f.Day, -f.Amount)));
            Dictionary<string, string> flowParams = ctx.LatestMonth();
            flowParams["kind"] = "receipt";
            section.Charts.Add(WithDrillParams(Chart("stockFlowByMonth", "رسید خرید و مصرف مواد در تولید به تفکیک ماه", CeoKind.Bar, CeoUnit.Rial, ctx.Labels, CeoLinks.Kardex, CeoDrillRef.InvTopMovements,
                "ارزش حرکت‌های انبار به بهای ثبت‌شده؛ رسید خرید خالص از برگشت به تأمین‌کننده است و مصرف، خالص از برگشت از تولید؛ حرکت‌های ابطال‌شده کم می‌شوند. فهرست جزئیات رسیدهای خرید را نشان می‌دهد و مصرف تولید را هم می‌شود از همان فهرست خواست.",
                Series("رسید خرید", received, CeoRole.Primary), Series("مصرف تولید", consumed, CeoRole.Secondary)), flowParams));
            return section;
        }

        /// <summary>
        /// The note of the warehouse chart. The chart multiplies the quantity on hand of every warehouse (positive balances only, customer property left out) by the weighted average cost of the item;
        /// the stock value figure is the sum of the item costs. They agree except for stock the chart cannot value (a negative balance, an item without a cost row), and the note says by how much.
        /// </summary>
        private static string WarehouseValueNote(decimal chartTotal, decimal stockValue)
        {
            string note = "مقدار موجودی هر انبار ضرب در بهای میانگین موزون کالا؛ اموال مشتری شمرده نمی‌شود.";
            decimal difference = Math.Abs(stockValue - chartTotal);
            if (difference >= 1m)
                note += " جمع انبارها (" + CeoText.ShortRial(chartTotal) + ") با ارزش موجودی کل (" + CeoText.ShortRial(stockValue) + ") " + CeoText.ShortRial(difference) +
                        " اختلاف دارد؛ موجودی منفی و کالای بدون بهای میانگین در نمودار نمی‌آید.";
            return note;
        }
    }
}
