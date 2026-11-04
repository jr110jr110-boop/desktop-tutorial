using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ContractsSystem;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.DashboardDtos;

namespace MsTests
{
    /// <summary>P-B7b: the management dashboards: role gating, price masking, the commitments forecast buckets, the delay-cause report and the portfolio EVM shape.</summary>
    [TestClass]
    public class EpmDashboardTests
    {
        private static readonly ClaimsPrincipal Seeder = AppHost.Principal(1, "seed", "PMO");
        private static readonly ClaimsPrincipal Ceo = AppHost.Principal(41, "ceo.one", "CEO");
        private static readonly ClaimsPrincipal Finance = AppHost.Principal(42, "finance.one", "FINANCIAL");
        private static readonly ClaimsPrincipal ProjectManager = AppHost.Principal(43, "pm.one", "PROJECTMANAGER");
        private static readonly ClaimsPrincipal Operator = AppHost.Principal(44, "operator.one", "OPERATOR");
        private static readonly ClaimsPrincipal Store = AppHost.Principal(45, "store.one", "WAREHOUSE");

        private static Task<ResponseDto<T>> Dash<T>(AppHost host, ClaimsPrincipal who, Func<IEpmDashboardService, Task<ResponseDto<T>>> call) => host.As(who, p => call(p.GetRequiredService<IEpmDashboardService>()));

        private static int NewContract(AppHost host, string code)
        {
            int id = 0;
            host.Write(u =>
            {
                var c = AppHost.Add(u, new ContractEntity { Code = code, Title = "contract " + code, Status = ContractStatus.Current });
                u.SaveChanges();
                id = c.Id;
            }, Seeder);
            return id;
        }

        private static DateTime ThisWeekStart()
        {
            DateTime d = DateTime.Today;
            return d.AddDays(-(((int)d.DayOfWeek + 1) % 7));
        }

        [TestMethod]
        public async Task EachRoleSeesOnlyItsDashboards()
        {
            var host = new AppHost("P7bDash_roles_" + Guid.NewGuid().ToString("N"));
            Assert.AreEqual(9, (await Dash(host, Ceo, s => s.IndexAsync(default))).Data.Count);
            Assert.AreEqual(0, (await Dash(host, Operator, s => s.IndexAsync(default))).Data.Count);
            var store = (await Dash(host, Store, s => s.IndexAsync(default))).Data;
            CollectionAssert.AreEqual(new[] { "project", "warehouse" }, store.Select(x => x.Key).OrderBy(k => k).ToArray(), "the warehouse role reads the project register and its own dashboard");
            Assert.IsFalse((await Dash(host, Store, s => s.GetAsync("procurement", null, default))).Success);
            Assert.IsFalse((await Dash(host, Operator, s => s.GetAsync("warehouse", null, default))).Success);
            Assert.IsTrue((await Dash(host, Store, s => s.GetAsync("warehouse", null, default))).Success);
            Assert.IsFalse((await Dash(host, Ceo, s => s.GetAsync("no-such", null, default))).Success);
            Assert.IsFalse((await Dash(host, Ceo, s => s.GetAsync("project", null, default))).Success, "the project dashboard needs a project");
        }

        [TestMethod]
        public async Task EveryDashboardRendersOnAnEmptyDatabase()
        {
            var host = new AppHost("P7bDash_empty_" + Guid.NewGuid().ToString("N"));
            int contract = NewContract(host, "C-EMPTY");
            foreach (string key in new[] { "portfolio", "project", "procurement", "warehouse", "quality", "production", "tasks", "finance", "ceo" })
            {
                var r = await Dash(host, Ceo, s => s.GetAsync(key, key == "project" ? contract : (int?)null, default));
                Assert.IsTrue(r.Success, key + ": " + r.Message);
                Assert.AreEqual(key, r.Data.Key);
                Assert.IsTrue(r.Data.Kpis.Count > 0 || r.Data.Charts.Count > 0, key);
                foreach (DashChartDto c in r.Data.Charts)
                    foreach (DashSeriesDto series in c.Series) Assert.AreEqual(c.Labels.Count, series.Values.Count, key + "/" + c.Key);
            }
        }

        [TestMethod]
        public async Task ProcurementCountsComeFromTheOrders()
        {
            var host = new AppHost("P7bDash_proc_" + Guid.NewGuid().ToString("N"));
            host.Write(u =>
            {
                AppHost.Add(u, new PurchaseOrder { Number = "PO-1", Status = PurchaseOrderStatus.Released, ProviderId = 1, OrderDate = DateTime.Today });
                AppHost.Add(u, new PurchaseOrder { Number = "PO-2", Status = PurchaseOrderStatus.Draft, ProviderId = 1, OrderDate = DateTime.Today });
                u.SaveChanges();
                var po = u.Set<PurchaseOrder>().First(p => p.Number == "PO-1");
                u.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine { PurchaseOrderId = po.Id, RowNumber = 1, Description = "bolts", Unit = "EA", Quantity = 10, ReceivedQuantity = 4, UnitPrice = 100, DeliveryDate = DateTime.Today.AddDays(-3) });
                u.SaveChanges();
            }, Seeder);
            var d = (await Dash(host, Ceo, s => s.GetAsync("procurement", null, default))).Data;
            Assert.AreEqual(2m, d.Kpis.Single(k => k.Key == "orders").Value);
            Assert.AreEqual(1m, d.Kpis.Single(k => k.Key == "overdue").Value);
            Assert.AreEqual(600m, d.Kpis.Single(k => k.Key == "open-value").Value);
            Assert.AreEqual("PO-1", d.Tables.Single(t => t.Key == "overdue").Rows.Single()[0]);
        }

        [TestMethod]
        public async Task TheForecastPutsEachCommitmentInItsWeekAndMasksPricesForOtherRoles()
        {
            var host = new AppHost("P7bDash_fc_" + Guid.NewGuid().ToString("N"));
            int contract = NewContract(host, "C-FC");
            DateTime week2 = ThisWeekStart().AddDays(9);
            host.Write(u =>
            {
                var po = AppHost.Add(u, new PurchaseOrder { Number = "PO-F1", Status = PurchaseOrderStatus.Released, ProviderId = 1, ContractId = contract, OrderDate = DateTime.Today });
                var late = AppHost.Add(u, new PurchaseOrder { Number = "PO-F2", Status = PurchaseOrderStatus.Released, ProviderId = 1, ContractId = contract, OrderDate = DateTime.Today });
                u.SaveChanges();
                u.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine { PurchaseOrderId = po.Id, RowNumber = 1, Description = "pipe", Unit = "EA", Quantity = 10, ReceivedQuantity = 4, UnitPrice = 100, DeliveryDate = week2, ContractId = contract });
                u.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine { PurchaseOrderId = late.Id, RowNumber = 1, Description = "valve", Unit = "EA", Quantity = 2, ReceivedQuantity = 0, UnitPrice = 1000, DeliveryDate = ThisWeekStart().AddDays(-20), ContractId = contract });
                AppHost.Add(u, new PsStatement { Number = "PST-1", Side = PsSide.Supplier, Status = PsStatus.PaymentRequested, ContractId = contract, PeriodFrom = DateTime.Today.AddDays(-30), PeriodTo = DateTime.Today, SendingDate = week2.AddDays(7), PayableThisPeriod = 500m });
                // paid and draft statements never count
                AppHost.Add(u, new PsStatement { Number = "PST-2", Side = PsSide.Supplier, Status = PsStatus.Paid, ContractId = contract, PeriodFrom = DateTime.Today, PeriodTo = DateTime.Today, PayableThisPeriod = 9999m });
                AppHost.Add(u, new PsStatement { Number = "PST-3", Side = PsSide.Supplier, Status = PsStatus.Draft, ContractId = contract, PeriodFrom = DateTime.Today, PeriodTo = DateTime.Today, PayableThisPeriod = 8888m });
                u.SaveChanges();
            }, Seeder);

            var f = (await Dash(host, Finance, s => s.CommitmentsAsync(new CommitmentsQuery { ContractId = contract, Period = "week", Count = 6 }, default))).Data;
            Assert.IsFalse(f.PricesMasked);
            Assert.AreEqual(6, f.Buckets.Count);
            Assert.AreEqual(600m, f.Buckets[1].OpenOrders, "(10 - 4) x 100 in the second week");
            Assert.AreEqual(500m, f.Buckets[2].Statements);
            Assert.AreEqual(2000m, f.Overdue, "the late order is overdue");
            Assert.AreEqual(0m, f.Buckets[0].OpenOrders, "overdue amounts are kept apart: they are not added to the first bucket");
            Assert.AreEqual(600m, f.OpenOrdersTotal);
            Assert.AreEqual(500m, f.StatementsTotal);
            Assert.AreEqual(1100m, f.Total, "the buckets only");
            Assert.AreEqual(3100m, f.TotalWithOverdue);
            Assert.AreEqual(3, f.Items.Count);
            Assert.AreEqual(1, f.Items.Count(i => i.IsOverdue));

            // a project manager sees the forecast page but no money
            var masked = (await Dash(host, ProjectManager, s => s.CommitmentsAsync(new CommitmentsQuery { ContractId = contract }, default))).Data;
            Assert.IsTrue(masked.PricesMasked);
            Assert.AreEqual(0m, masked.Total);
            Assert.AreEqual(0, masked.Items.Count);

            // another contract has none of it
            int other = NewContract(host, "C-FC2");
            Assert.AreEqual(0m, (await Dash(host, Finance, s => s.CommitmentsAsync(new CommitmentsQuery { ContractId = other }, default))).Data.Total);
            // the monthly view and a bad period
            var month = (await Dash(host, Finance, s => s.CommitmentsAsync(new CommitmentsQuery { ContractId = contract, Period = "month", Count = 3 }, default))).Data;
            Assert.AreEqual(3, month.Buckets.Count);
            Assert.AreEqual(3100m, month.Total + month.Overdue, "all three commitments are in the three months or overdue");
            // the monthly buckets are Persian months: each starts on the first day of a Persian month and is labelled in Persian
            var pc = new System.Globalization.PersianCalendar();
            Assert.IsTrue(month.Buckets.All(b => pc.GetDayOfMonth(b.From) == 1 && b.Label.Any(ch => ch >= '\u0600' && ch <= '\u06FF')));
            Assert.IsFalse((await Dash(host, Finance, s => s.CommitmentsAsync(new CommitmentsQuery { Period = "year" }, default))).Success);
            Assert.IsFalse((await Dash(host, Operator, s => s.CommitmentsAsync(new CommitmentsQuery(), default))).Success);
        }

        [TestMethod]
        public async Task AForeignCurrencyOrderIsConvertedWithItsRateOrLeftOutAndCounted()
        {
            var host = new AppHost("P7bDash_fx_" + Guid.NewGuid().ToString("N"));
            int contract = NewContract(host, "C-FX");
            DateTime week2 = ThisWeekStart().AddDays(9);
            host.Write(u =>
            {
                var rial = AppHost.Add(u, new PurchaseOrder { Number = "PO-X1", Status = PurchaseOrderStatus.Released, ProviderId = 1, ContractId = contract, OrderDate = DateTime.Today });
                var usd = AppHost.Add(u, new PurchaseOrder { Number = "PO-X2", Status = PurchaseOrderStatus.Released, ProviderId = 1, ContractId = contract, OrderDate = DateTime.Today, Currency = "USD", ExchangeRate = 50m });
                var norate = AppHost.Add(u, new PurchaseOrder { Number = "PO-X3", Status = PurchaseOrderStatus.Released, ProviderId = 1, ContractId = contract, OrderDate = DateTime.Today, Currency = "EUR" });
                u.SaveChanges();
                foreach (var po in new[] { rial, usd, norate })
                    u.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine { PurchaseOrderId = po.Id, RowNumber = 1, Description = "x", Unit = "EA", Quantity = 1, ReceivedQuantity = 0, UnitPrice = 100, DeliveryDate = week2, ContractId = contract });
                u.SaveChanges();
            }, Seeder);
            var f = (await Dash(host, Finance, s => s.CommitmentsAsync(new CommitmentsQuery { ContractId = contract, Period = "week", Count = 4 }, default))).Data;
            Assert.AreEqual(100m + 100m * 50m, f.OpenOrdersTotal, "the dollar order counts with its rate; the euro order without a rate is not added at face value");
            Assert.AreEqual(1, f.ExcludedForeignOrders);
        }

        [TestMethod]
        public async Task OnlyManagementAndFinanceReadTheDelayCauseReport()
        {
            var host = new AppHost("P7bDash_dgate_" + Guid.NewGuid().ToString("N"));
            Assert.IsTrue((await Dash(host, Ceo, s => s.DelayCausesAsync(new DelayCauseQuery(), default))).Success);
            Assert.IsFalse((await Dash(host, Store, s => s.DelayCausesAsync(new DelayCauseQuery(), default))).Success, "a project-control reader such as the warehouse role is not management");
            Assert.IsFalse((await Dash(host, Operator, s => s.DelayCausesAsync(new DelayCauseQuery(), default))).Success);
        }

        [TestMethod]
        public async Task TheDelayCauseReportGroupsLateLinesByCauseAndIssuesByResponsible()
        {
            var host = new AppHost("P7bDash_delay_" + Guid.NewGuid().ToString("N"));
            int contract = NewContract(host, "C-DL");
            host.Write(u =>
            {
                var r1 = AppHost.Add(u, new PcDailyReport { ContractId = contract, ReportDate = DateTime.Today.AddDays(-2), ConfirmedAt = DateTimeOffset.Now });
                var r2 = AppHost.Add(u, new PcDailyReport { ContractId = contract, ReportDate = DateTime.Today.AddDays(-1), ConfirmedAt = DateTimeOffset.Now });
                var draft = AppHost.Add(u, new PcDailyReport { ContractId = contract, ReportDate = DateTime.Today.AddDays(-1) });
                u.SaveChanges();
                u.Set<PcDailyReportLine>().Add(new PcDailyReportLine { ReportId = r1.Id, ActivityId = 1, IsLate = true, DelayReasonCode = "MAT", DelayReason = "material not arrived" });
                u.Set<PcDailyReportLine>().Add(new PcDailyReportLine { ReportId = r1.Id, ActivityId = 2, IsLate = true, DelayReasonCode = "MAT", DelayReason = "material not arrived" });
                u.Set<PcDailyReportLine>().Add(new PcDailyReportLine { ReportId = r2.Id, ActivityId = 1, IsLate = true, DelayReasonCode = "MAT", DelayReason = "material not arrived" });
                u.Set<PcDailyReportLine>().Add(new PcDailyReportLine { ReportId = r2.Id, ActivityId = 3, IsLate = true, DelayReasonCode = "WTH", DelayReason = "bad weather" });
                u.Set<PcDailyReportLine>().Add(new PcDailyReportLine { ReportId = r2.Id, ActivityId = 4, IsLate = false });
                // an unconfirmed report is not evidence
                u.Set<PcDailyReportLine>().Add(new PcDailyReportLine { ReportId = draft.Id, ActivityId = 5, IsLate = true, DelayReasonCode = "WTH", DelayReason = "bad weather" });
                AppHost.Add(u, new PcIssue { ContractId = contract, Number = "I-1", Title = "supplier late", DelayReasonCode = "MAT", Status = PcIssueStatus.Open, RaisedDate = DateTime.Today.AddDays(-5), DueDate = DateTime.Today.AddDays(-1) });
                AppHost.Add(u, new PcIssue { ContractId = contract, Number = "I-2", Title = "supplier late 2", DelayReasonCode = "MAT", Status = PcIssueStatus.Closed, RaisedDate = DateTime.Today.AddDays(-9), ClosedAt = DateTimeOffset.Now });
                u.SaveChanges();
            }, Seeder);
            var report = (await Dash(host, ProjectManager, s => s.DelayCausesAsync(new DelayCauseQuery { ContractId = contract }, default))).Data;
            Assert.AreEqual(4, report.TotalLateLines);
            Assert.AreEqual(2, report.ByCause.Count);
            DelayCauseRowDto mat = report.ByCause[0];
            Assert.AreEqual("MAT", mat.CauseCode);
            Assert.AreEqual(3, mat.LateLines);
            Assert.AreEqual(2, mat.Activities);
            Assert.AreEqual(2, mat.Reports);
            Assert.AreEqual(1, mat.OpenIssues);
            Assert.AreEqual(1, mat.ClosedIssues);
            Assert.AreEqual("material not arrived", mat.Cause);
            Assert.AreEqual(1, report.ByResponsible.Sum(r => r.OverdueIssues));
            // one cause only
            Assert.AreEqual(1, (await Dash(host, ProjectManager, s => s.DelayCausesAsync(new DelayCauseQuery { ContractId = contract, CauseCode = "WTH" }, default))).Data.TotalLateLines);
            // a range with nothing
            Assert.AreEqual(0, (await Dash(host, ProjectManager, s => s.DelayCausesAsync(new DelayCauseQuery { ContractId = contract, From = DateTime.Today.AddDays(-400), To = DateTime.Today.AddDays(-300) }, default))).Data.TotalLateLines);
            Assert.IsFalse((await Dash(host, ProjectManager, s => s.DelayCausesAsync(new DelayCauseQuery { From = DateTime.Today, To = DateTime.Today.AddDays(-1) }, default))).Success);
            Assert.IsFalse((await Dash(host, Operator, s => s.DelayCausesAsync(new DelayCauseQuery(), default))).Success);
        }

        [TestMethod]
        public async Task ThePortfolioEvmListsEveryCurrentProjectAndMasksTheMoneyOnly()
        {
            var host = new AppHost("P7bDash_evm_" + Guid.NewGuid().ToString("N"));
            NewContract(host, "C-E1");
            NewContract(host, "C-E2");
            host.Write(u => { AppHost.Add(u, new ContractEntity { Code = "C-DONE", Title = "done", Status = ContractStatus.Done }); u.SaveChanges(); }, Seeder);
            var full = (await Dash(host, Finance, s => s.PortfolioEvmAsync(null, default))).Data;
            Assert.AreEqual(2, full.Projects);
            Assert.AreEqual(2, full.Rows.Count);
            Assert.IsFalse(full.PricesMasked);
            Assert.IsNotNull(full.Bac);
            var masked = (await Dash(host, ProjectManager, s => s.PortfolioEvmAsync(null, default))).Data;
            Assert.IsTrue(masked.PricesMasked);
            Assert.IsNull(masked.Bac);
            Assert.IsNull(masked.Ac);
            Assert.IsTrue(masked.Rows.All(r => r.Ac == null && r.Bac == null));
            Assert.IsFalse((await Dash(host, Operator, s => s.PortfolioEvmAsync(null, default))).Success);
        }

        [TestMethod]
        public async Task PricesAreMaskedOnTheProcurementAndFinanceDashboards()
        {
            var host = new AppHost("P7bDash_mask_" + Guid.NewGuid().ToString("N"));
            var qa = AppHost.Principal(46, "quality.one", "QC");
            // the quality role has no procurement dashboard at all
            Assert.IsFalse((await Dash(host, qa, s => s.GetAsync("procurement", null, default))).Success);
            var pmo = AppHost.Principal(47, "pmo.two", "PMO");
            // PMO sees prices; a CEO too
            Assert.IsFalse((await Dash(host, pmo, s => s.GetAsync("finance", null, default))).Data.PricesMasked);
            // a planning user sees the procurement dashboard with prices (PLANNING is in the price roles) but a project manager has no procurement dashboard
            Assert.IsFalse((await Dash(host, ProjectManager, s => s.GetAsync("procurement", null, default))).Success);
            var auditor = AppHost.Principal(48, "auditor.one", "AUDITOR");
            Assert.IsFalse((await Dash(host, auditor, s => s.GetAsync("procurement", null, default))).Data.PricesMasked);
        }
    }
}
