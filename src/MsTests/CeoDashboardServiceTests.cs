using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Sales;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.ProjectControlDtos;
using ViewModels.NasbNiro.QualityDtos;

namespace MsTests
{
    /// <summary>
    /// The CEO dashboard behind the overview (review items 2 and 5). The open-NCR figure leaves out the cancelled reports like the quality dashboard does,
    /// and the dashboard hands over the module results it was made from (the quality dashboard, the sales numbers, the production load) so that the overview does not read the modules again.
    /// </summary>
    [TestClass]
    public class CeoDashboardServiceTests
    {
        private sealed class Calls
        {
            public int Counts { get; set; }
            public int Dashboard { get; set; }
        }

        private static CeoDashboardService Service(IUnitOfWork uow, Calls calls)
        {
            IQualityDashboardService quality = CeoOverviewServiceTests.Proxy<IQualityDashboardService>(
                ("GetCountsAsync", (Func<CancellationToken, Task<QualityCountsDto>>)(ct => { calls.Counts++; return Task.FromResult(new QualityCountsDto { OverdueCapas = 1, OverdueNcrs = 2 }); })),
                ("GetDashboardAsync", (Func<CancellationToken, Task<ResponseDto<QualityDashboardDto>>>)(ct =>
                {
                    calls.Dashboard++;
                    return Task.FromResult(ResponseDtoHelper.Ok(new QualityDashboardDto { OverdueCapas = 1, OverdueNcrs = 2, CapaOpen = 5, Ncrs = new NcrAgeDto { Major = 1, Age0To7 = 2, Age8To30 = 1 } }));
                })));
            IPcReportService projects = CeoOverviewServiceTests.Proxy<IPcReportService>(
                ("GetDashboardAsync", (Func<CancellationToken, Task<PcDashboardDto>>)(ct => Task.FromResult(new PcDashboardDto { ActiveProjects = 4, ProjectsRed = 1, ProjectsAmber = 2 }))));
            IFinanceDashboardService finance = CeoOverviewServiceTests.Proxy<IFinanceDashboardService>(
                ("GetCeoFinanceAsync", (Func<CancellationToken, Task<CeoFinanceDto>>)(ct => Task.FromResult(new CeoFinanceDto { CashAndBank = 5m }))));
            return new CeoDashboardService(uow, quality, projects, finance, CeoOverviewServiceTests.User("CEO"));
        }

        private static async Task SeedAsync(ServiceProvider provider)
        {
            using IServiceScope scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            DateTime day = DateTime.Today;
            db.Set<NonConformance>().AddRange(
                CeoHistoryTests.Ncr(1, day, NcrStatus.Open),
                CeoHistoryTests.Ncr(2, day, NcrStatus.Approved),
                CeoHistoryTests.Ncr(3, day, NcrStatus.Executed),
                CeoHistoryTests.Ncr(4, day, NcrStatus.Closed),                    // closed: not open
                CeoHistoryTests.Ncr(5, day, NcrStatus.Cancelled),                 // cancelled: not open (review item 2)
                CeoHistoryTests.Ncr(6, day, NcrStatus.Open, deleted: true));      // deleted: not counted
            SlProposal Proposal(int id, SlProposalStatus status, bool deleted = false)
            {
                var p = new SlProposal { Id = id, Number = "P-" + id, InquiryId = id, EstimateId = id, Status = status };
                if (deleted) p.Delete();
                return p;
            }
            db.Set<SlProposal>().AddRange(Proposal(1, SlProposalStatus.Won), Proposal(2, SlProposalStatus.Won), Proposal(3, SlProposalStatus.Lost), Proposal(4, SlProposalStatus.Draft), Proposal(5, SlProposalStatus.Won, deleted: true));
            await db.SaveChangesAsync();
        }

        [TestMethod]
        public async Task The_Open_Ncr_Figure_Is_Neither_Closed_Nor_Cancelled_And_Is_The_Same_In_The_Plain_Dashboard_And_In_Its_Parts()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            await SeedAsync(provider);
            using IServiceScope scope = provider.CreateScope();
            var calls = new Calls();
            CeoDashboardService service = Service(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), calls);

            CeoDashboardDto plain = (await service.GetAsync(CancellationToken.None)).Data;
            CeoDashboardParts parts = await service.GetPartsAsync(CancellationToken.None);

            Assert.AreEqual(3, plain.OpenNcrs, "open, approved and executed; not the closed, the cancelled or the deleted report");
            Assert.AreEqual(3, parts.Dashboard.OpenNcrs);
            Assert.IsTrue(plain.Alerts.Any(a => a.Text.StartsWith("3 ", StringComparison.Ordinal) && a.Link == "/inventory/ncr"), "the alert counts the same reports: " + string.Join(" | ", plain.Alerts.Select(a => a.Text)));
        }

        [TestMethod]
        public async Task The_Parts_Carry_The_Module_Results_The_Dashboard_Was_Made_From_And_Read_The_Quality_Dashboard_Once()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            await SeedAsync(provider);
            using IServiceScope scope = provider.CreateScope();
            var calls = new Calls();
            CeoDashboardService service = Service(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), calls);

            CeoDashboardDto plain = (await service.GetAsync(CancellationToken.None)).Data;
            Assert.AreEqual(1, calls.Counts, "the plain dashboard reads the small counts of the quality module");
            Assert.AreEqual(0, calls.Dashboard);

            CeoDashboardParts parts = await service.GetPartsAsync(CancellationToken.None);

            Assert.AreEqual(1, calls.Counts, "the parts do not ask for the counts again");
            Assert.AreEqual(1, calls.Dashboard, "... they read the whole quality dashboard once, and the counts of the CEO dashboard are the base part of it");
            Assert.AreEqual(2, parts.Quality.OverdueNcrs);
            Assert.AreEqual(5, parts.Quality.CapaOpen);
            Assert.AreEqual(parts.Quality.OverdueCapas, parts.Dashboard.OverdueCapas, "the dashboard figure and the part it was made from are one number");
            Assert.AreEqual(plain.OverdueCapas, parts.Dashboard.OverdueCapas);

            Assert.IsNotNull(parts.Sales, "the sales numbers of the dashboard");
            Assert.AreEqual(2, parts.Sales.Won, "two proposals won; the deleted one is not counted");
            Assert.AreEqual(1, parts.Sales.Lost);
            Assert.AreEqual(parts.Sales.HitRatePercent, parts.Dashboard.SalesHitRatePercent, "the hit rate is made of them");
            Assert.AreEqual(parts.Sales.PipelineValue, parts.Dashboard.SalesPipelineValue);
            Assert.IsTrue(parts.Sales.PipelineByStage.Count > 0, "the pipeline by stage lists the stages even when there is no open inquiry");
            Assert.AreEqual(parts.Sales.PipelineValue, parts.Sales.PipelineByStage.Sum(s => s.Value));

            Assert.IsNotNull(parts.ProductionLoad, "the load report of the next four weeks");
            Assert.AreEqual(4, parts.ProductionLoad.Weeks);
            Assert.AreEqual(parts.ProductionLoad.Overloads.Count, parts.Dashboard.ProductionOverloadWeeks, "the overloaded weeks of the dashboard are counted from this report");

            Assert.AreEqual(plain.StockValue, parts.Dashboard.StockValue);
            Assert.AreEqual(plain.PcProjectsRed, parts.Dashboard.PcProjectsRed);
            Assert.AreEqual(1, parts.Dashboard.PcProjectsRed);
            Assert.AreEqual(2, parts.Dashboard.PcProjectsAmber);
            Assert.AreEqual(5m, parts.Dashboard.Finance.CashAndBank);
        }
    }
}
