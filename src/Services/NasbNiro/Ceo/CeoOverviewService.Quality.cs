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
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Ceo
{
    // Section 5: quality. Open NCRs, overdue corrective actions, overdue calibration, customer complaints, hold points waiting;
    // NCRs opened and closed by month, the age of the open NCRs.
    public sealed partial class CeoOverviewService
    {
        private async Task<CeoSectionDto> QualityAsync(CeoContext ctx, CancellationToken ct)
        {
            CeoDashboardParts parts = await PartsAsync(ctx, ct);
            CeoDashboardDto b = parts.Dashboard;
            // the quality dashboard was read for the CEO dashboard (its counts are the base part of it): the same result, not a second read of the quality module
            QualityDashboardDto q = parts.Quality ?? throw new InvalidOperationException("The quality dashboard is not available.");

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.OpenNcrs, b.OpenNcrs, CeoRules.ByCount(b.OpenNcrs, 5, 15),
                "گزارش‌های بسته‌نشده و لغونشده (لغوشده‌ها شمرده نمی‌شوند)؛ عمده: " + CeoText.Number(q.Ncrs.Major) + "؛ طبقه‌بندی‌نشده: " + CeoText.Number(q.Ncrs.NotClassified)), higherIsBetter: false));
            section.Kpis.Add(Kpi(ctx, "overdueNcrs", q.OverdueNcrs, CeoRules.ByAny(q.OverdueNcrs, 3),
                "مهلت تصمیم: هفت روز برای جزئی و چهارده روز برای عمده (تنظیم شرکت)"));
            section.Kpis.Add(Kpi(ctx, "overdueCapas", b.OverdueCapas, CeoRules.DangerIfAny(b.OverdueCapas),
                "همه‌ی اقدام‌های باز: " + CeoText.Number(q.CapaOpen)));
            section.Kpis.Add(Kpi(ctx, "calibrationOverdue", b.CalibrationOverdue, CeoRules.DangerIfAny(b.CalibrationOverdue),
                "تا سی روز آینده سررسید می‌شوند: " + CeoText.Number(b.CalibrationDue30)));
            section.Kpis.Add(Kpi(ctx, "complaintsOpen", q.ComplaintsOpen, CeoRules.DangerIfAny(q.ComplaintsOverdue),
                "از مهلت پاسخ گذشته: " + CeoText.Number(q.ComplaintsOverdue)));
            section.Kpis.Add(Kpi(ctx, "pendingHPoints", b.PendingHPoints, CeoRules.WarnIfAny(b.PendingHPoints),
                "در برنامه‌های بازرسی و آزمون تأییدشده"));

            // chart 1: NCRs opened (report date) and closed (closing time) by month. The drill behind it lists the open NCRs and has no month filter, so it carries no period.
            DateTimeOffset from = new DateTimeOffset(ctx.From);
            var ncrs = await _uow.Set<NonConformance>().AsNoTracking()
                .Where(n => !n.Deleted && n.Status != NcrStatus.Cancelled && (n.ReportDate >= ctx.From || (n.ClosedAt != null && n.ClosedAt >= from)))
                .Select(n => new { n.ReportDate, n.ClosedAt }).ToListAsync(ct);
            section.Charts.Add(Chart("ncrByMonth", "عدم انطباق‌های ثبت‌شده و بسته‌شده به تفکیک ماه", CeoKind.Bar, CeoUnit.Count, ctx.Labels, CeoLinks.Ncr, CeoDrillRef.QcNcrOpen,
                "ثبت‌شده بر پایه‌ی تاریخ گزارش و بسته‌شده بر پایه‌ی زمان بستن.",
                Series("ثبت‌شده", CountByMonth(ctx, ncrs.Select(n => n.ReportDate)), CeoRole.Negative),
                Series("بسته‌شده", CountByMonth(ctx, ncrs.Where(n => n.ClosedAt.HasValue).Select(n => n.ClosedAt.Value.LocalDateTime.Date)), CeoRole.Positive)));

            // chart 2: the age of the open NCRs as the quality dashboard counts them: the same open NCRs as the KPI above (neither closed nor cancelled)
            section.Charts.Add(Chart("ncrAge", "سن عدم انطباق‌های باز", CeoKind.Donut, CeoUnit.Count, new[] { "تا ۷ روز", "۸ تا ۳۰ روز", "بیش از ۳۰ روز" }, CeoLinks.Ncr, CeoDrillRef.QcNcrOpen,
                "از تاریخ گزارش تا امروز؛ لغوشده‌ها شمرده نمی‌شوند.",
                Series("گزارش‌ها", new decimal[] { q.Ncrs.Age0To7, q.Ncrs.Age8To30, q.Ncrs.Age31Plus })));
            return section;
        }
    }
}
