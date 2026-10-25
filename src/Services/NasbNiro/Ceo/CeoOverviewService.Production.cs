using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
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
            CeoDashboardDto b = await BaseAsync(ctx, ct);

            // OEE of the Persian month so far: the work centres, and the shop floor as the sums put through the same formula
            DateTime monthStart = CeoTrends.MonthStart(ctx.Today);
            List<OeeDto> oee = await MfKpi.OeeAsync(_uow, monthStart, ctx.Today, null, ct);
            OeeDto total = MfKpi.Compute(new OeeDto
            {
                PlannedHours = oee.Sum(o => o.PlannedHours), DowntimeHours = oee.Sum(o => o.DowntimeHours), BookedRunHours = oee.Sum(o => o.BookedRunHours), StandardHours = oee.Sum(o => o.StandardHours),
                GoodQty = oee.Sum(o => o.GoodQty), ScrapQty = oee.Sum(o => o.ScrapQty),
            });

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(CeoKeys.LateProductionOrders, "دستورهای تولید دارای تأخیر", b.ProductionLateOrders, CeoUnit.Count, CeoRules.ByAny(b.ProductionLateOrders, 5), CeoLinks.ProductionOrders,
                CeoDrillRef.MfOrdersLate, "دستورهای باز که پایان برنامه‌ریزی‌شده‌شان (آخرین اجرای زمان‌بندی) از تاریخ نیاز دیرتر است."), higherIsBetter: false));
            section.Kpis.Add(Kpi("machinesDown", "ماشین‌های خراب", b.ProductionMachinesDown, CeoUnit.Count, CeoRules.DangerIfAny(b.ProductionMachinesDown), CeoLinks.ProductionMaintenance,
                CeoDrillRef.MfMachinesDown, "ماشین‌هایی که وضعیتشان «خراب» ثبت شده است."));
            section.Kpis.Add(Kpi("maintenanceOverdue", "نگهداری سررسیدگذشته", b.ProductionMaintenanceOverdue, CeoUnit.Count, CeoRules.ByAny(b.ProductionMaintenanceOverdue, 5), CeoLinks.ProductionMaintenance,
                CeoDrillRef.MfMaintenanceOverdue, "دستورهای نگهداری باز که موعدشان گذشته است."));
            section.Kpis.Add(Kpi("overloadWeeks", "هفته‌های بیش از ۱۱۰٪ ظرفیت", b.ProductionOverloadWeeks, CeoUnit.Count, CeoRules.ByAny(b.ProductionOverloadWeeks, 4), CeoLinks.ProductionSchedule,
                CeoDrillRef.MfWorkCenterLoad, "هفته‌های مراکز کار در چهار هفته‌ی آینده که بار از ۱۱۰٪ ظرفیت بیشتر است."));
            section.Kpis.Add(Kpi("oee", "بهره‌وری کلی تجهیزات (OEE)", total.OeePercent, CeoUnit.Percent, total.OeePercent.HasValue ? CeoRules.Oee(total.OeePercent.Value) : CeoStatus.Neutral, CeoLinks.ProductionOee,
                CeoDrillRef.MfOeeByWorkCenter, "ماه شمسی جاری تا امروز؛ دسترسی × عملکرد × کیفیت. بدون ثبت زمان تولید مقداری ندارد."));

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

            // chart 2: the highest weekly load of every work centre in the next four weeks, against its capacity
            LoadReportDto load = await MfScheduleService.LoadAsync(_uow, ctx.Today, LoadWeeks, LoadThreshold, null, ct);
            section.Charts.Add(Chart("workCenterLoad", "بار مراکز کار در چهار هفته‌ی آینده", CeoKind.HBar, CeoUnit.Percent, load.Rows.Select(r => r.Code), CeoLinks.ProductionSchedule, CeoDrillRef.MfWorkCenterLoad,
                "بیشترین بار هفتگی نسبت به ظرفیت؛ بالای ۱۱۰٪ اضافه‌بار حساب می‌شود.",
                Series("بار به درصد ظرفیت", load.Rows.Select(r => r.Cells.Count == 0 ? 0m : r.Cells.Max(c => c.LoadPercent ?? 0m)))));

            // chart 3: OEE of the work centres that booked run time and pieces this month
            List<OeeDto> measured = oee.Where(o => o.OeePercent.HasValue).ToList();
            section.Charts.Add(Chart("oeeByWorkCenter", "بهره‌وری کلی تجهیزات به تفکیک مرکز کار", CeoKind.HBar, CeoUnit.Percent, measured.Select(o => o.WorkCenterCode), CeoLinks.ProductionOee, CeoDrillRef.MfOeeByWorkCenter,
                "ماه شمسی جاری تا امروز؛ فقط مراکزی که زمان تولید و قطعه ثبت کرده‌اند.",
                Series("OEE", measured.Select(o => o.OeePercent.Value))));
            return section;
        }
    }
}
