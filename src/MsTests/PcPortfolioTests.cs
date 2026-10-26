using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace MsTests
{
    /// <summary>
    /// The project portfolio behind the CEO dashboard and the CEO overview (review items 5, 11 and 21): one query for the overdue initiation steps whatever the number of projects,
    /// a memo of the rows that hands out copies and is dropped when the service saves, and one definition of red and amber for the dashboard counts.
    /// </summary>
    [TestClass]
    public class PcPortfolioTests
    {
        private sealed class Counters
        {
            public int Evm { get; set; }
            public int OverdueInitiation { get; set; }

            /// <summary>The (schedule, cost) status of each project; a project that is not listed is green.</summary>
            public Dictionary<int, (string Spi, string Cpi)> Statuses { get; } = new Dictionary<int, (string, string)>();
        }

        private static PcReportService Service(IUnitOfWork uow, string roles, Counters counters)
        {
            IPcProgressService progress = CeoOverviewServiceTests.Proxy<IPcProgressService>(
                ("ComputeEvmAsync", (Func<int, DateTime, CancellationToken, Task<PcEvmDto>>)((contractId, asOf, ct) =>
                {
                    counters.Evm++;
                    (string spi, string cpi) = counters.Statuses.TryGetValue(contractId, out var status) ? status : ("green", "green");
                    return Task.FromResult(new PcEvmDto { ContractId = contractId, AsOf = asOf, SpiStatus = spi, CpiStatus = cpi, Spi = 1m, Cpi = 1m, PlannedPercent = 50m, PhysicalPercent = 40m });
                })));
            IPcCommercialService commercial = CeoOverviewServiceTests.Proxy<IPcCommercialService>(
                ("GuaranteeAlertsAsync", (Func<int?, CancellationToken, Task<List<PcGuaranteeAlertDto>>>)((contractId, ct) => Task.FromResult(new List<PcGuaranteeAlertDto>()))));
            IPcProjectService project = CeoOverviewServiceTests.Proxy<IPcProjectService>(
                ("OverdueContractIdsAsync", (Func<CancellationToken, Task<List<int>>>)(ct => { counters.OverdueInitiation++; return Task.FromResult(new List<int> { 2 }); })));
            IPcMdrService mdr = CeoOverviewServiceTests.Proxy<IPcMdrService>(
                ("OverdueResponsesAsync", (Func<int?, CancellationToken, Task<int>>)((contractId, ct) => Task.FromResult(0))));
            IPcBaselineService baseline = CeoOverviewServiceTests.Proxy<IPcBaselineService>(
                ("OverdueChangeContractIdsAsync", (Func<CancellationToken, Task<List<int>>>)(ct => Task.FromResult(new List<int>()))));
            return new PcReportService(uow, CeoOverviewServiceTests.User(roles), progress, commercial, project, mdr, baseline);
        }

        private static async Task SeedAsync(ServiceProvider provider, int projects)
        {
            using IServiceScope scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (int id = 1; id <= projects; id++)
            {
                db.Set<ContractEntity>().Add(new ContractEntity { Id = id, Code = "C" + id, Title = "contract " + id, Status = ContractStatus.Current });
                db.Set<PcContractTerms>().Add(new PcContractTerms { Id = id, ContractId = id, ProjectCode = "P" + id });
            }
            db.Set<PcReportIssue>().Add(new PcReportIssue { Id = 1, Number = "RPC-1", Kind = PcReportKind.ClientMonthly, ContractId = 1, DataDate = DateTime.Today, GeneratedAt = DateTimeOffset.Now, Hash = "h", Content = "{}" });
            await db.SaveChangesAsync();
        }

        private static readonly PaginationParams All = new PaginationParams { PageNumber = 1, PageSize = 0 };

        [TestMethod]
        public async Task The_Overdue_Initiation_Steps_Are_Asked_Once_For_The_Whole_Portfolio_And_Not_At_All_When_There_Is_No_Project()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            await SeedAsync(provider, 3);
            using IServiceScope scope = provider.CreateScope();
            var counters = new Counters();
            PcReportService service = Service(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), "PMO", counters);

            List<PcPortfolioRowDto> rows = (await service.GetPortfolioAsync(All, CancellationToken.None)).Data.ToList();

            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual(3, counters.Evm, "each project is computed");
            Assert.AreEqual(1, counters.OverdueInitiation, "... but the query over all the projects runs once, not once per project");
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, rows.Select(r => r.OverdueInitiationSteps).ToArray(), "the answer is shared: project 2 is the one with an overdue step");

            using ServiceProvider empty = CeoOverviewServiceTests.InMemory();
            using IServiceScope emptyScope = empty.CreateScope();
            var none = new Counters();
            List<PcPortfolioRowDto> noRows = (await Service(emptyScope.ServiceProvider.GetRequiredService<IUnitOfWork>(), "PMO", none).GetPortfolioAsync(All, CancellationToken.None)).Data.ToList();
            Assert.AreEqual(0, noRows.Count);
            Assert.AreEqual(0, none.OverdueInitiation, "no project, no query");
        }

        [TestMethod]
        public async Task The_Memo_Of_The_Rows_Hands_Out_Copies_So_A_Reader_Who_Changes_What_It_Got_Changes_Nothing_For_The_Next_One()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            await SeedAsync(provider, 3);
            using IServiceScope scope = provider.CreateScope();
            var counters = new Counters();
            PcReportService service = Service(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), "PMO", counters);

            List<PcPortfolioRowDto> first = (await service.GetPortfolioAsync(All, CancellationToken.None)).Data.ToList();
            Assert.AreEqual(3, counters.Evm);
            foreach (PcPortfolioRowDto row in first) { row.SpiStatus = "red"; row.LateActivities = 99; row.ContractTitle = "changed"; }
            first.Clear();

            List<PcPortfolioRowDto> second = (await service.GetPortfolioAsync(All, CancellationToken.None)).Data.ToList();
            Assert.AreEqual(3, counters.Evm, "the second read is served from the memo: nothing is computed again");
            Assert.AreEqual(3, second.Count);
            Assert.IsTrue(second.All(r => r.SpiStatus == "green" && r.LateActivities == 0 && r.ContractTitle.StartsWith("contract ", StringComparison.Ordinal)), "the first reader's changes did not reach the memo");
            foreach (PcPortfolioRowDto row in second) { row.CpiStatus = "red"; row.PenaltyExposure = 5m; }

            List<PcPortfolioRowDto> third = (await service.GetPortfolioAsync(All, CancellationToken.None)).Data.ToList();
            Assert.AreEqual(3, counters.Evm);
            Assert.IsTrue(third.All(r => r.CpiStatus == "green" && r.PenaltyExposure == 0m), "... nor did the changes of the second one (a copy of a copy)");
            Assert.IsFalse(object.ReferenceEquals(second[0], third[0]));

            // the dashboard reads the same memo
            PcDashboardDto dashboard = await service.GetDashboardAsync(CancellationToken.None);
            Assert.AreEqual(3, counters.Evm);
            Assert.AreEqual(3, dashboard.ActiveProjects);
            Assert.AreEqual(0, dashboard.ProjectsRed);
        }

        [TestMethod]
        public async Task A_Service_That_Saves_Drops_Its_Memo_So_A_Read_After_A_Write_In_The_Same_Request_Is_Not_Stale()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            await SeedAsync(provider, 3);
            using IServiceScope scope = provider.CreateScope();
            var counters = new Counters();
            PcReportService service = Service(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), "PMO", counters);

            Assert.AreEqual(0, (await service.GetDashboardAsync(CancellationToken.None)).ProjectsRed);
            Assert.AreEqual(3, counters.Evm);

            // a project turns red; within the memo's few seconds the old rows would still be served ...
            counters.Statuses[1] = ("red", "green");
            Assert.AreEqual(0, (await service.GetDashboardAsync(CancellationToken.None)).ProjectsRed, "the memo is still valid and no write happened");
            Assert.AreEqual(3, counters.Evm);

            // ... but this service saves a report, and the memo goes
            ResponseDto<PcReportDto> released = await service.ReleaseReportAsync(1, CancellationToken.None);
            Assert.IsTrue(released.Success, released.Message);

            PcDashboardDto after = await service.GetDashboardAsync(CancellationToken.None);
            Assert.AreEqual(6, counters.Evm, "the rows were computed again after the save");
            Assert.AreEqual(1, after.ProjectsRed, "... and show the project that turned red");
        }

        [TestMethod]
        public async Task The_Dashboard_Counts_Every_Project_Once_As_Red_Amber_Or_Neither_With_The_Definitions_The_Overview_Uses()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            await SeedAsync(provider, 6);
            using IServiceScope scope = provider.CreateScope();
            var counters = new Counters();
            counters.Statuses[1] = ("red", "amber");      // red
            counters.Statuses[2] = ("amber", "red");      // red, not amber as well
            counters.Statuses[3] = ("amber", "green");    // amber
            counters.Statuses[4] = ("green", "amber");    // amber
            counters.Statuses[5] = ("amber", "amber");    // amber
            counters.Statuses[6] = ("none", "none");      // neither
            PcReportService service = Service(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), "PMO", counters);

            PcDashboardDto dashboard = await service.GetDashboardAsync(CancellationToken.None);
            List<PcPortfolioRowDto> rows = (await service.GetPortfolioAsync(All, CancellationToken.None)).Data.ToList();

            Assert.AreEqual(6, dashboard.ActiveProjects);
            Assert.AreEqual(2, dashboard.ProjectsRed);
            Assert.AreEqual(3, dashboard.ProjectsAmber);
            Assert.AreEqual(rows.Count(PcReportService.IsRed), dashboard.ProjectsRed, "the dashboard figure and the donut of the overview use the same definition");
            Assert.AreEqual(rows.Count(PcReportService.IsAmber), dashboard.ProjectsAmber);
            Assert.AreEqual(0, rows.Count(r => PcReportService.IsRed(r) && PcReportService.IsAmber(r)), "no project is both");
            Assert.AreEqual(1, rows.Count - dashboard.ProjectsRed - dashboard.ProjectsAmber, "red + amber + green is the number of projects");
        }
    }
}
