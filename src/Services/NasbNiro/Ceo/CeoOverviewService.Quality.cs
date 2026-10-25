using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
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
            CeoDashboardDto b = await BaseAsync(ctx, ct);
            ResponseDto<QualityDashboardDto> answer = await _quality.GetDashboardAsync(ct);
            if (answer == null || !answer.Success || answer.Data == null) throw new InvalidOperationException("The quality dashboard is not available: " + answer?.Message);
            QualityDashboardDto q = answer.Data;

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(CeoKeys.OpenNcrs, "گزارش‌های عدم انطباق باز", b.OpenNcrs, CeoUnit.Count, CeoRules.ByCount(b.OpenNcrs, 5, 15), CeoLinks.Ncr, CeoDrillRef.QcNcrOpen,
                "همه‌ی گزارش‌های بسته‌نشده؛ عمده: " + CeoText.Number(q.Ncrs.Major) + "؛ طبقه‌بندی‌نشده: " + CeoText.Number(q.Ncrs.NotClassified)), higherIsBetter: false));
            section.Kpis.Add(Kpi("overdueNcrs", "عدم انطباق‌های گذشته از مهلت تصمیم", q.OverdueNcrs, CeoUnit.Count, CeoRules.ByAny(q.OverdueNcrs, 3), CeoLinks.Ncr, CeoDrillRef.QcNcrOpen,
                "مهلت تصمیم: هفت روز برای جزئی و چهارده روز برای عمده (تنظیم شرکت)"));
            section.Kpis.Add(Kpi("overdueCapas", "اقدام‌های اصلاحی و پیشگیرانه‌ی عقب‌افتاده", b.OverdueCapas, CeoUnit.Count, CeoRules.DangerIfAny(b.OverdueCapas), CeoLinks.Capa, CeoDrillRef.QcCapaOverdue,
                "همه‌ی اقدام‌های باز: " + CeoText.Number(q.CapaOpen)));
            section.Kpis.Add(Kpi("calibrationOverdue", "تجهیزات اندازه‌گیری با کالیبراسیون سررسیدگذشته", b.CalibrationOverdue, CeoUnit.Count, CeoRules.DangerIfAny(b.CalibrationOverdue), CeoLinks.Calibration, CeoDrillRef.QcCalibrationDue,
                "تا سی روز آینده سررسید می‌شوند: " + CeoText.Number(b.CalibrationDue30)));
            section.Kpis.Add(Kpi("complaintsOpen", "شکایت‌های باز مشتریان", q.ComplaintsOpen, CeoUnit.Count, CeoRules.DangerIfAny(q.ComplaintsOverdue), CeoLinks.Complaints, CeoDrillRef.QcComplaintsOpen,
                "از مهلت پاسخ گذشته: " + CeoText.Number(q.ComplaintsOverdue)));
            section.Kpis.Add(Kpi("pendingHPoints", "نقاط توقف (H) منتظر رهاسازی", b.PendingHPoints, CeoUnit.Count, CeoRules.WarnIfAny(b.PendingHPoints), CeoLinks.QualityDashboard, CeoDrillRef.QcHPointsPending,
                "در برنامه‌های بازرسی و آزمون تأییدشده"));

            // chart 1: NCRs opened (report date) and closed (closing time) by month
            DateTimeOffset from = new DateTimeOffset(ctx.From);
            var ncrs = await _uow.Set<NonConformance>().AsNoTracking()
                .Where(n => !n.Deleted && n.Status != NcrStatus.Cancelled && (n.ReportDate >= ctx.From || (n.ClosedAt != null && n.ClosedAt >= from)))
                .Select(n => new { n.ReportDate, n.ClosedAt }).ToListAsync(ct);
            section.Charts.Add(Chart("ncrByMonth", "عدم انطباق‌های ثبت‌شده و بسته‌شده به تفکیک ماه", CeoKind.Bar, CeoUnit.Count, ctx.Labels, CeoLinks.Ncr, CeoDrillRef.QcNcrOpen,
                "ثبت‌شده بر پایه‌ی تاریخ گزارش و بسته‌شده بر پایه‌ی زمان بستن.",
                Series("ثبت‌شده", CountByMonth(ctx, ncrs.Select(n => n.ReportDate)), CeoRole.Negative),
                Series("بسته‌شده", CountByMonth(ctx, ncrs.Where(n => n.ClosedAt.HasValue).Select(n => n.ClosedAt.Value.LocalDateTime.Date)), CeoRole.Positive)));

            // chart 2: the age of the open NCRs as the quality dashboard counts them (cancelled ones are not open there)
            section.Charts.Add(Chart("ncrAge", "سن عدم انطباق‌های باز", CeoKind.Donut, CeoUnit.Count, new[] { "تا ۷ روز", "۸ تا ۳۰ روز", "بیش از ۳۰ روز" }, CeoLinks.Ncr, CeoDrillRef.QcNcrOpen,
                "از تاریخ گزارش تا امروز؛ لغوشده‌ها شمرده نمی‌شوند.",
                Series("گزارش‌ها", new decimal[] { q.Ncrs.Age0To7, q.Ncrs.Age8To30, q.Ncrs.Age31Plus })));
            return section;
        }
    }
}
