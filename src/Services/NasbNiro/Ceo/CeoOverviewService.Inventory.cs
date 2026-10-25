using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
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
            CeoDashboardDto b = await BaseAsync(ctx, ct);

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(CeoKeys.StockValue, "ارزش موجودی انبار", b.StockValue, CeoUnit.Rial, null, CeoLinks.Stock, CeoDrillRef.InvStockValueByWarehouse,
                "به بهای میانگین موزون کالاها؛ اموال مشتری شمرده نمی‌شود."), higherIsBetter: null));
            section.Kpis.Add(Trended(ctx, Kpi(CeoKeys.ItemsBelowMinimum, "کالاهای زیر حداقل موجودی", b.ItemsBelowMinimum, CeoUnit.Count, CeoRules.WarnIfAny(b.ItemsBelowMinimum), CeoLinks.Stock, CeoDrillRef.InvItemsBelowMinimum,
                "کالاهای فعال دارای حداقل موجودی که مانده‌ی آن‌ها (بدون انبار ضایعات) از حداقل کمتر است."), higherIsBetter: false));
            section.Kpis.Add(Kpi("quarantineLines", "ردیف‌های موجود در قرنطینه", b.QuarantineLines, CeoUnit.Count, CeoRules.ByCount(b.QuarantineLines, 10, 30), CeoLinks.Warehouses, CeoDrillRef.InvQuarantine,
                "موجودی منتظر تصمیم کنترل کیفیت در انبارهای قرنطینه"));
            section.Kpis.Add(Kpi("openMaterialRequests", "درخواست‌های مواد منتظر حواله", b.OpenMaterialRequests, CeoUnit.Count, CeoRules.ByCount(b.OpenMaterialRequests, 15, 40), CeoLinks.MaterialRequests, CeoDrillRef.InvMaterialRequestsOpen,
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
                "مقدار موجودی هر انبار ضرب در بهای میانگین موزون کالا؛ اموال مشتری شمرده نمی‌شود.",
                Series("ارزش موجودی", values)));

            // chart 2: purchase receipts against the material consumed by production (net of the returns), by month, valued at the cost of the movements
            var flow = await _uow.Set<StockTransaction>().AsNoTracking()
                .Where(t => t.TransactionDate >= ctx.From && t.TransactionDate < ctx.End &&
                    (t.Document.Type == InventoryDocumentType.PurchaseReceipt || t.Document.Type == InventoryDocumentType.Issue || t.Document.Type == InventoryDocumentType.ProductionReturn))
                .GroupBy(t => new { Day = t.TransactionDate.Date, Type = t.Document.Type })
                .Select(g => new { g.Key.Day, g.Key.Type, Amount = g.Sum(t => t.CostAmount ?? 0m) }).ToListAsync(ct);
            // an issue is stored with a negative amount and a return from production with a positive one: the consumption is the negated sum
            decimal[] received = CeoTrends.SumByMonth(ctx.Window, flow.Where(f => f.Type == InventoryDocumentType.PurchaseReceipt).Select(f => (f.Day, f.Amount)));
            decimal[] consumed = CeoTrends.SumByMonth(ctx.Window, flow.Where(f => f.Type != InventoryDocumentType.PurchaseReceipt).Select(f => (f.Day, -f.Amount)));
            section.Charts.Add(Chart("stockFlowByMonth", "رسید خرید و مصرف مواد در تولید به تفکیک ماه", CeoKind.Bar, CeoUnit.Rial, ctx.Labels, CeoLinks.Kardex, CeoDrillRef.InvTopMovements,
                "ارزش حرکت‌های انبار به بهای ثبت‌شده؛ مصرف، خالص از برگشت به انبار است و حرکت‌های ابطال‌شده کم می‌شوند.",
                Series("رسید خرید", received, CeoRole.Primary), Series("مصرف تولید", consumed, CeoRole.Secondary)));
            return section;
        }
    }
}
