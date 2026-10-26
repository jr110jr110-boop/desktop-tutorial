using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Ceo
{
    // Section 4: production. Late orders (last scheduler run), machines down, overdue maintenance, overloaded weeks, OEE of the month;
    // open orders by state, the peak load of every work centre in the next four weeks, OEE by work centre.
    public sealed partial class CeoOverviewService
    {
        /// <summary>The load chart looks as far ahead as the CEO dashboard does (four weeks, 110 percent of the capacity).</summary>
        private const int LoadWeeks = 4;
        private const decimal LoadThreshold = 110m;

        private async Task<CeoSectionDto> ProductionAsync(CeoContext ctx, CancellationToken ct)
        {
            CeoDashboardParts parts = await PartsAsync(ctx, ct);
            CeoDashboardDto b = parts.Dashboard;

            // OEE of the Persian month so far: the work centres, and the shop floor as the sums put through the same formula
            DateTime monthStart = CeoTrends.MonthStart(ctx.Today);
            List<OeeDto> oee = await MfKpi.OeeAsync(_uow, monthStart, ctx.Today, null, ct);
            OeeDto total = MfKpi.Compute(new OeeDto
            {
                PlannedHours = oee.Sum(o => o.PlannedHours), DowntimeHours = oee.Sum(o => o.DowntimeHours), BookedRunHours = oee.Sum(o => o.BookedRunHours), StandardHours = oee.Sum(o => o.StandardHours),
                GoodQty = oee.Sum(o => o.GoodQty), ScrapQty = oee.Sum(o => o.ScrapQty),
            });

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.LateProductionOrders, b.ProductionLateOrders, CeoRules.ByAny(b.ProductionLateOrders, 5),
                "دستورهای باز که پایان برنامه‌ریزی‌شده‌شان (آخرین اجرای زمان‌بندی) از تاریخ نیاز دیرتر است."), higherIsBetter: false));
            section.Kpis.Add(Kpi(ctx, "machinesDown", b.ProductionMachinesDown, CeoRules.DangerIfAny(b.ProductionMachinesDown),
                "ماشین‌هایی که وضعیتشان «خراب» ثبت شده است."));
            section.Kpis.Add(Kpi(ctx, "maintenanceOverdue", b.ProductionMaintenanceOverdue, CeoRules.ByAny(b.ProductionMaintenanceOverdue, 5),
                "دستورهای نگهداری باز که موعدشان گذشته است."));
            section.Kpis.Add(Kpi(ctx, "overloadWeeks", b.ProductionOverloadWeeks, CeoRules.ByAny(b.ProductionOverloadWeeks, 4),
                "هفته‌های مراکز کار در چهار هفته‌ی آینده که بار از ۱۱۰٪ ظرفیت بیشتر است؛ هفته‌ی بدون ظرفیت که بار دارد هم شمرده می‌شود."));
            section.Kpis.Add(Kpi(ctx, "oee", total.OeePercent, total.OeePercent.HasValue ? CeoRules.Oee(total.OeePercent.Value) : CeoStatus.Neutral,
                "ماه شمسی جاری تا امروز؛ دسترسی × عملکرد × کیفیت. بدون ثبت زمان تولید مقداری ندارد."));

            // chart 1: the open production orders by state; the late ones are the same orders as the KPI
            var states = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && (o.Status == MfOrderStatus.Draft || o.Status == MfOrderStatus.Released ||
                    o.Status == MfOrderStatus.InProgress || o.Status == MfOrderStatus.Completed))
                .GroupBy(o => new { o.Status, o.IsLate }).Select(g => new { g.Key.Status, g.Key.IsLate, Count = g.Count() }).ToListAsync(ct);
            bool Running(MfOrderStatus s) => s == MfOrderStatus.Released || s == MfOrderStatus.InProgress;
            section.Charts.Add(Chart("ordersByState", "دستورهای تولید به تفکیک وضعیت", CeoKind.Donut, CeoUnit.Count,
                new[] { "پیش‌نویس", "در جریان و به‌موقع", "در جریان و دارای تأخیر", "تکمیل‌شده (بسته‌نشده)" }, CeoLinks.ProductionOrders, CeoDrillRef.MfOrdersByStatus,
                "دستورهای صادرشده و در حال اجرا تا بسته‌شدن شمرده می‌شوند.",
                Series("دستورها", new decimal[]
                {
                    states.Where(s => s.Status == MfOrderStatus.Draft).Sum(s => s.Count),
                    states.Where(s => Running(s.Status) && !s.IsLate).Sum(s => s.Count),
                    states.Where(s => Running(s.Status) && s.IsLate).Sum(s => s.Count),
                    states.Where(s => s.Status == MfOrderStatus.Completed).Sum(s => s.Count),
                })));

            // chart 2: the highest weekly load of every work centre in the next four weeks, against its capacity. The load report is the one the dashboard counted the overloaded weeks from
            // (computed once per request); it is only computed here when the dashboard did not hand it over.
            LoadReportDto load = parts.ProductionLoad ?? await MfScheduleService.LoadAsync(_uow, ctx.Today, LoadWeeks, LoadThreshold, null, ct);
            // a week without capacity but with load has no percentage; the KPI counts it as an overloaded week, the chart can only show it as zero, and the note says how many there are
            int noCapacityWeeks = load.Rows.Sum(r => r.Cells.Count(c => !c.LoadPercent.HasValue && c.Overloaded));
            string loadNote = "بیشترین بار هفتگی نسبت به ظرفیت؛ بالای ۱۱۰٪ اضافه‌بار حساب می‌شود.";
            if (noCapacityWeeks > 0)
                loadNote += " " + CeoText.Number(noCapacityWeeks) + " هفته‌ی مرکز کار ظرفیت ندارد ولی بار دارد؛ درصد ندارد و صفر نمایش داده می‌شود، اما در شمارش هفته‌های اضافه‌بار آمده است.";
            section.Charts.Add(Chart("workCenterLoad", "بار مراکز کار در چهار هفته‌ی آینده", CeoKind.HBar, CeoUnit.Percent, load.Rows.Select(r => r.Code), CeoLinks.ProductionSchedule, CeoDrillRef.MfWorkCenterLoad,
                loadNote,
                Series("بار به درصد ظرفیت", load.Rows.Select(r => r.Cells.Count == 0 ? 0m : r.Cells.Max(c => c.LoadPercent ?? 0m)))));

            // chart 3: OEE of the work centres that booked run time and pieces this month (the drill takes the same month so far)
            List<OeeDto> measured = oee.Where(o => o.OeePercent.HasValue).ToList();
            section.Charts.Add(WithDrillParams(Chart("oeeByWorkCenter", "بهره‌وری کلی تجهیزات به تفکیک مرکز کار", CeoKind.HBar, CeoUnit.Percent, measured.Select(o => o.WorkCenterCode), CeoLinks.ProductionOee, CeoDrillRef.MfOeeByWorkCenter,
                "ماه شمسی جاری تا امروز؛ فقط مراکزی که زمان تولید و قطعه ثبت کرده‌اند.",
                Series("OEE", measured.Select(o => o.OeePercent.Value))), ctx.LatestMonth()));
            return section;
        }
    }
}
