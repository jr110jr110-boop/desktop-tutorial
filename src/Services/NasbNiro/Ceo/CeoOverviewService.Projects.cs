using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.Ceo
{
    // Section 3: projects (project control). Active, red and amber projects, late activities, penalty exposure, guarantees near their end;
    // the status of every project and the planned against the physical progress of the weakest ones.
    public sealed partial class CeoOverviewService
    {
        /// <summary>How many projects the progress chart shows (the weakest by schedule index).</summary>
        private const int ProgressChartProjects = 8;

        private async Task<CeoSectionDto> ProjectsAsync(CeoContext ctx, CancellationToken ct)
        {
            CeoDashboardDto b = (await PartsAsync(ctx, ct)).Dashboard;
            ResponseDto<PagedList<PcPortfolioRowDto>> answer = await _projects.GetPortfolioAsync(new PaginationParams { PageNumber = 1, PageSize = 0 }, ct);
            if (answer == null || !answer.Success || answer.Data == null) throw new InvalidOperationException("The portfolio is not available: " + answer?.Message);
            List<PcPortfolioRowDto> rows = answer.Data.ToList();

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(ctx, CeoKeys.ActiveProjects, b.PcActiveProjects, null,
                "پروژه‌هایی که شرایط قراردادشان در کنترل پروژه ثبت شده است."), higherIsBetter: null));
            section.Kpis.Add(Kpi(ctx, "projectsRed", b.PcProjectsRed, CeoRules.DangerIfAny(b.PcProjectsRed),
                "پروژه‌ای که شاخص برنامه (SPI) یا هزینه (CPI) آن قرمز است"));
            section.Kpis.Add(Kpi(ctx, "projectsAmber", b.PcProjectsAmber, CeoRules.WarnIfAny(b.PcProjectsAmber),
                "پروژه‌ای که یکی از دو شاخص برنامه (SPI) یا هزینه (CPI) آن زرد است و هیچ‌کدام قرمز نیست؛ هر پروژه فقط یک رنگ دارد"));
            section.Kpis.Add(Kpi(ctx, "pcLateActivities", b.PcLateActivities, CeoRules.ByCount(b.PcLateActivities, 1, 10),
                "مراحل پرداخت عقب‌افتاده: " + CeoText.Number(b.PcOverdueMilestones) + "؛ درخواست‌های تغییر باز: " + CeoText.Number(b.PcOpenChangeRequests)));
            section.Kpis.Add(Kpi(ctx, "pcPenaltyExposure", b.PcPenaltyExposure, CeoRules.WarnIfAny(b.PcPenaltyExposure),
                "جریمه‌ی محاسبه‌شده‌ی قراردادها تا امروز"));
            section.Kpis.Add(Kpi(ctx, "pcGuaranteeAlerts", b.PcGuaranteeAlerts, CeoRules.WarnIfAny(b.PcGuaranteeAlerts),
                "سی روز یا کمتر تا سررسید"));

            // chart 1: each project once, with the same definition as the red and amber figures above (PcReportService.IsRed / IsAmber): red when one of its indexes is red,
            // amber when none is red and one is amber, green otherwise; so red + amber + green is the number of projects and the donut agrees with the two KPIs
            int red = rows.Count(PcReportService.IsRed), amber = rows.Count(PcReportService.IsAmber), green = rows.Count - red - amber;
            section.Charts.Add(Chart("projectStatus", "وضعیت پروژه‌ها", CeoKind.Donut, CeoUnit.Count, new[] { "سبز", "زرد", "قرمز" }, CeoLinks.Portfolio, CeoDrillRef.PcPortfolio,
                "هر پروژه یک بار شمرده می‌شود؛ قرمز اگر یکی از دو شاخص برنامه و هزینه قرمز باشد و زرد اگر قرمز نباشد و یکی زرد باشد.",
                Series("پروژه‌ها", new decimal[] { green, amber, red })));

            // chart 2: planned against physical progress of the projects with the weakest schedule index
            List<PcPortfolioRowDto> weakest = rows.OrderBy(r => r.Spi ?? 9m).ThenBy(r => r.ContractId).Take(ProgressChartProjects).ToList();
            section.Charts.Add(Chart("progressByProject", "پیشرفت برنامه‌ای و واقعی پروژه‌ها", CeoKind.Bar, CeoUnit.Percent,
                weakest.Select(r => string.IsNullOrWhiteSpace(r.ProjectCode) ? r.ContractTitle : r.ProjectCode),
                CeoLinks.Portfolio, CeoDrillRef.PcPortfolio, "پروژه‌های ضعیف‌تر از نظر شاخص برنامه (SPI)؛ حداکثر " + CeoText.Number(ProgressChartProjects) + " پروژه.",
                Series("برنامه‌ای", weakest.Select(r => Math.Round(r.PlannedPercent, 1)), CeoRole.Neutral), Series("واقعی", weakest.Select(r => Math.Round(r.PhysicalPercent, 1)))));
            return section;
        }
    }
}
