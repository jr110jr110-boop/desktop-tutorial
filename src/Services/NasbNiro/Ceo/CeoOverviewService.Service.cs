using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.AfterSales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.AfterSales;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Ceo
{
    // Section 8: after-sales service. SLA breaches, open requests, units in warranty, the warranty cost of the year (masked for a reader who does not see pay),
    // goodwill waiting for the CEO, customer satisfaction; requests received and resolved by month, the warranty cost by month (masked), the satisfaction by month.
    public sealed partial class CeoOverviewService
    {
        private async Task<CeoSectionDto> ServiceAsync(CeoContext ctx, CancellationToken ct)
        {
            CeoDashboardDto b = (await PartsAsync(ctx, ct)).Dashboard;

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.ServiceSlaBreaches, b.ServiceSlaBreaches, CeoRules.DangerIfAny(b.ServiceSlaBreaches),
                "درخواست‌های بازی که مهلت پاسخ یا مهلت حل آن‌ها گذشته است."), higherIsBetter: false));
            section.Kpis.Add(Kpi(ctx, "serviceOpenRequests", b.ServiceOpenRequests, null,
                "باز، واگذارشده، در حال انجام یا منتظر مشتری"));
            section.Kpis.Add(Kpi(ctx, "serviceUnitsInWarranty", b.ServiceUnitsInWarranty, null,
                "تجهیزات فعال مشتریان که گارانتی آن‌ها هنوز تمام نشده است."));
            if (b.ServiceWarrantyCostHidden)
                section.Kpis.Add(HiddenKpi(ctx, "serviceWarrantyCostYtd",
                    "هزینه‌ی نیروی کار برخی سفارش‌ها حقوق یک یا دو نفر است و فقط برای مدیرعامل، مدیر مالی و حقوق و دستمزد نمایش داده می‌شود."));
            else
                section.Kpis.Add(Kpi(ctx, "serviceWarrantyCostYtd", b.ServiceWarrantyCostYtd, null,
                    "سفارش‌های گارانتی بسته‌شده از ابتدای سال شمسی؛ نیروی کار و قطعات"));
            section.Kpis.Add(Kpi(ctx, "serviceGoodwillWaiting", b.ServiceGoodwillWaiting, CeoRules.WarnIfAny(b.ServiceGoodwillWaiting),
                "سفارش‌های خارج از گارانتی که مشتری حسن نیت خواسته و تصمیم مدیرعامل لازم است."));
            decimal? score = b.ServiceSurveyAverageScore;
            section.Kpis.Add(Kpi(ctx, CeoKeys.ServiceSurveyScore, score, score.HasValue ? CeoRules.Satisfaction(score.Value) : CeoStatus.Neutral,
                "میانگین امتیاز نظرسنجی‌های پاسخ‌داده‌شده از پنج؛ بدون نظرسنجی مقداری ندارد.", score.HasValue ? CeoText.Number(score.Value, 2) + " از " + CeoText.Number(5) : null));

            // chart 1: requests received and requests resolved, by month. The drill behind it lists the open requests and has no month filter, so it carries no period.
            var received = await _uow.Set<SvServiceRequest>().AsNoTracking()
                .Where(r => !r.Deleted && r.ReceivedAt >= ctx.From && r.ReceivedAt < ctx.End)
                .GroupBy(r => r.ReceivedAt.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
            var resolved = await _uow.Set<SvServiceRequest>().AsNoTracking()
                .Where(r => !r.Deleted && r.ResolvedAt != null && r.ResolvedAt >= ctx.From && r.ResolvedAt < ctx.End)
                .GroupBy(r => r.ResolvedAt.Value.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
            section.Charts.Add(Chart("requestsByMonth", "درخواست‌های خدمات دریافت‌شده و حل‌شده به تفکیک ماه", CeoKind.Bar, CeoUnit.Count, ctx.Labels, CeoLinks.ServiceRequests, CeoDrillRef.SvRequestsOpen,
                "دریافت‌شده بر پایه‌ی زمان دریافت و حل‌شده بر پایه‌ی زمان حل.",
                Series("دریافت‌شده", CeoTrends.SumByMonth(ctx.Window, received.Select(r => (r.Day, (decimal)r.Count))), CeoRole.Primary),
                Series("حل‌شده", CeoTrends.SumByMonth(ctx.Window, resolved.Select(r => (r.Day, (decimal)r.Count))), CeoRole.Positive)));

            // chart 2: the warranty cost of the closed warranty orders by closing month; hidden under the same rule as the dashboard of the service module
            DateTimeOffset from = new DateTimeOffset(ctx.From);
            var warranty = await _uow.Set<SvServiceOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.Status == SvOrderStatus.Closed && o.Decision == SvWarrantyDecision.Warranty && o.ClosedAt != null && o.ClosedAt >= from)
                .Select(o => new { o.ClosedAt, o.LabourAmount, o.PartsAmount, o.LabourPooledAmount, o.LabourEmployeeCount, o.LabourPayrollEmployeeCount }).ToListAsync(ct);
            const string costTitle = "هزینه‌ی گارانتی به تفکیک ماه";
            // R1p: the labour of a new order is absorbed at a standard rate (never one person's pay); only orders closed under the old personal-rate rule (SvLegacyLabour) can still hide the chart
            if (!ctx.SeesPayroll && warranty.Any(o => SvLegacyLabour.IsProtected(o.LabourPooledAmount, o.LabourEmployeeCount, o.LabourPayrollEmployeeCount)))
            {
                section.Charts.Add(HiddenChart("warrantyCostByMonth", costTitle, CeoKind.StackedBar, CeoUnit.Rial, CeoLinks.ServiceOrders, CeoDrillRef.SvWarrantyCostByMonth,
                    "هزینه‌ی نیروی کار برخی سفارش‌های قدیمی (پیش از نرخ استاندارد) حقوق یک یا دو نفر است و فقط برای مدیرعامل، مدیر مالی و حقوق و دستمزد نمایش داده می‌شود."));
            }
            else
            {
                section.Charts.Add(WithDrillParams(Chart("warrantyCostByMonth", costTitle, CeoKind.StackedBar, CeoUnit.Rial, ctx.Labels, CeoLinks.ServiceOrders, CeoDrillRef.SvWarrantyCostByMonth,
                    "سفارش‌های گارانتی بسته‌شده بر پایه‌ی زمان بستن.",
                    Series("قطعات", CeoTrends.SumByMonth(ctx.Window, warranty.Select(o => (o.ClosedAt.Value.LocalDateTime.Date, o.PartsAmount))), CeoRole.Primary),
                    Series("نیروی کار", CeoTrends.SumByMonth(ctx.Window, warranty.Select(o => (o.ClosedAt.Value.LocalDateTime.Date, o.LabourAmount))), CeoRole.Secondary)), ctx.LatestMonth()));
            }

            // chart 3: the average score of the VERIFIED surveys answered in each month (R1p D25: the same answers as the KPI; months without a verified answer are left out)
            var surveys = await SvSurveyMath.VerifiedAnswers(_uow.Set<SlSurvey>().AsNoTracking())
                .Where(s => s.AnsweredAt >= from)
                .Select(s => new { s.AnsweredAt, Score = s.AverageScore.Value }).ToListAsync(ct);
            int unverified = await SvSurveyMath.UnverifiedAnswers(_uow.Set<SlSurvey>().AsNoTracking()).Where(s => s.AnsweredAt >= from).CountAsync(ct);
            var months = new List<string>();
            var averages = new List<decimal>();
            for (int i = 0; i < ctx.Window.Count; i++)
            {
                List<decimal> scores = surveys.Where(s => CeoTrends.IndexOf(ctx.Window, s.AnsweredAt.Value.LocalDateTime.Date) == i).Select(s => s.Score).ToList();
                if (scores.Count == 0) continue;
                months.Add(ctx.Labels[i]);
                averages.Add(Math.Round(scores.Average(), 2, MidpointRounding.AwayFromZero));
            }
            section.Charts.Add(WithDrillParams(Chart("surveyScoreByMonth", "میانگین رضایت مشتریان به تفکیک ماه", CeoKind.Line, CeoUnit.Count, months, CeoLinks.ServiceSurveys, CeoDrillRef.SvSurveyByMonth,
                "میانگین امتیاز نظرسنجی‌های تأییدشده از پنج بر پایه‌ی ماه پاسخ؛ ماه‌های بدون پاسخ تأییدشده نمایش داده نمی‌شوند" + (unverified > 0 ? "؛ " + CeoText.Number(unverified) + " پاسخ هنوز تأیید نشده است." : "."),
                Series("میانگین امتیاز", averages)), ctx.LatestMonth()));
            return section;
        }
    }
}
