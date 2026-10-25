using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
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
            CeoDashboardDto b = await BaseAsync(ctx, ct);
            ResponseDto<PagedList<PcPortfolioRowDto>> answer = await _projects.GetPortfolioAsync(new PaginationParams { PageNumber = 1, PageSize = 0 }, ct);
            if (answer == null || !answer.Success || answer.Data == null) throw new InvalidOperationException("The portfolio is not available: " + answer?.Message);
            List<PcPortfolioRowDto> rows = answer.Data.ToList();

            var section = new CeoSectionDto();
            section.Kpis.Add(Trended(ctx, Kpi(CeoKeys.ActiveProjects, "پروژه‌های فعال", b.PcActiveProjects, CeoUnit.Count, null, CeoLinks.Portfolio, CeoDrillRef.PcPortfolio,
                "پروژه‌هایی که شرایط قراردادشان در کنترل پروژه ثبت شده است."), higherIsBetter: null));
            section.Kpis.Add(Kpi("projectsRed", "پروژه‌های قرمز", b.PcProjectsRed, CeoUnit.Count, CeoRules.DangerIfAny(b.PcProjectsRed), CeoLinks.Portfolio, CeoDrillRef.PcPortfolio,
                "شاخص برنامه (SPI) یا هزینه (CPI) در وضعیت قرمز"));
            section.Kpis.Add(Kpi("projectsAmber", "پروژه‌های زرد", b.PcProjectsAmber, CeoUnit.Count, CeoRules.WarnIfAny(b.PcProjectsAmber), CeoLinks.Portfolio, CeoDrillRef.PcPortfolio,
                "شاخص برنامه (SPI) یا هزینه (CPI) در وضعیت زرد"));
            section.Kpis.Add(Kpi("pcLateActivities", "فعالیت‌های دارای تأخیر", b.PcLateActivities, CeoUnit.Count, CeoRules.ByCount(b.PcLateActivities, 1, 10), CeoLinks.PcActivities, CeoDrillRef.PcLateActivities,
                "مراحل پرداخت عقب‌افتاده: " + CeoText.Number(b.PcOverdueMilestones) + "؛ درخواست‌های تغییر باز: " + CeoText.Number(b.PcOpenChangeRequests)));
            section.Kpis.Add(Kpi("pcPenaltyExposure", "مواجهه‌ی جریمه‌ی تأخیر", b.PcPenaltyExposure, CeoUnit.Rial, CeoRules.WarnIfAny(b.PcPenaltyExposure), CeoLinks.PcPenalties, CeoDrillRef.PcPenaltyExposure,
                "جریمه‌ی محاسبه‌شده‌ی قراردادها تا امروز"));
            section.Kpis.Add(Kpi("pcGuaranteeAlerts", "ضمانت‌نامه‌های نزدیک به سررسید", b.PcGuaranteeAlerts, CeoUnit.Count, CeoRules.WarnIfAny(b.PcGuaranteeAlerts), CeoLinks.PcGuarantees, CeoDrillRef.PcGuaranteesExpiring,
                "سی روز یا کمتر تا سررسید"));

            // chart 1: each project once: red when one of its indexes is red, amber when none is red and one is amber
            bool Red(PcPortfolioRowDto r) => r.SpiStatus == "red" || r.CpiStatus == "red";
            bool Amber(PcPortfolioRowDto r) => !Red(r) && (r.SpiStatus == "amber" || r.CpiStatus == "amber");
            int red = rows.Count(Red), amber = rows.Count(Amber), green = rows.Count - red - amber;
            section.Charts.Add(Chart("projectStatus", "وضعیت پروژه‌ها", CeoKind.Donut, CeoUnit.Count, new[] { "سبز", "زرد", "قرمز" }, CeoLinks.Portfolio, CeoDrillRef.PcPortfolio,
                "هر پروژه یک بار شمرده می‌شود؛ قرمز اگر یکی از دو شاخص برنامه و هزینه قرمز باشد.",
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
