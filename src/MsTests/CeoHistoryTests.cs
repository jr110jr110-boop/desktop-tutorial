using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Ceo;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ceo;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;

namespace MsTests
{
    /// <summary>
    /// Review item 17: the trends of the CEO overview show something from the first day. The live value is the last point, and the months before the first nightly snapshot are derived from the records
    /// for four keys: cash (accounting events), receivablesOpen (the as-of ageing), openNcrs and serviceSlaBreaches (their dates). A real snapshot always wins over a derived month.
    /// The records are placed relative to the Persian months before today, so the tests hold whatever day they run on.
    /// </summary>
    [TestClass]
    public class CeoHistoryTests
    {
        internal static DateTime Month(int monthsBack) => CeoTrends.AddMonths(CeoTrends.MonthStart(DateTime.Today), -monthsBack);

        private static DateTime EndOfMonth(int monthsBack) => CeoTrends.Span(Month(monthsBack)).To;

        private static List<DateTime> PastMonths() => CeoHistory.PastMonths(DateTime.Today);

        // ==================================================================================== the month lists

        [TestMethod]
        public void The_Past_Months_Are_The_Eleven_Persian_Months_Before_The_Current_One_And_Covered_Months_Are_Not_Missing()
        {
            DateTime today = new DateTime(2026, 10, 25);   // 1405/08/03, Aban

            List<DateTime> past = CeoHistory.PastMonths(today);

            Assert.AreEqual(11, past.Count);
            Assert.AreEqual(new DateTime(2025, 11, 22), past[0], "Azar 1404");
            Assert.AreEqual(new DateTime(2026, 9, 23), past[10], "Mehr 1405, the month before the current one");
            Assert.IsTrue(past.SequenceEqual(past.OrderBy(m => m)));
            Assert.IsFalse(past.Contains(CeoTrends.MonthStart(today)), "the current month is the live value");

            var covered = new HashSet<DateTime> { past[10], past[9] };
            CollectionAssert.AreEqual(past.Take(9).ToArray(), CeoHistory.MissingMonths(today, covered));
            CollectionAssert.AreEqual(past, CeoHistory.MissingMonths(today, null));
            CollectionAssert.AreEqual(new[] { "cash", "receivablesOpen", "openNcrs", "serviceSlaBreaches" }, CeoHistory.Keys, "the four ★ keys that can be backfilled without a new table");
            foreach (string key in CeoHistory.Keys) CollectionAssert.Contains(CeoKeys.Snapshot, key);
        }

        // ==================================================================================== cash

        internal static AccountingEvent Event(int id, DateTime day, AccountingEventStatus status, params AccountingEventLine[] lines)
        {
            var e = new AccountingEvent { Id = id, Type = AccountingEventType.SalesInvoice, EventDate = day, Status = status, SourceType = "Test", SourceId = id, SourceNumber = "E-" + id, Key = Guid.NewGuid(), Lines = new List<AccountingEventLine>() };
            int row = 1;
            foreach (AccountingEventLine line in lines)
            {
                line.RowNumber = row++;
                line.AccountRole = "Test";
                e.Lines.Add(line);
            }
            return e;
        }

        internal static AccountingEventLine Line(string party, decimal debit = 0m, decimal credit = 0m) => new AccountingEventLine { PartyType = party, Debit = debit, Credit = credit };

        [TestMethod]
        public async Task Cash_At_A_Month_End_Is_The_Net_Of_The_Cash_And_Bank_Events_Up_To_That_Day()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            using (IServiceScope seed = provider.CreateScope())
            {
                var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<AccountingEvent>().AddRange(
                    Event(1, Month(3).AddDays(2), AccountingEventStatus.Posted, Line("BankAccount", debit: 1000), Line("Revenue", credit: 1000)),
                    Event(2, Month(2).AddDays(4), AccountingEventStatus.Posted, Line("CashBox", credit: 200), Line("Expense", debit: 200)),
                    Event(3, Month(2).AddDays(6), AccountingEventStatus.Posted, Line("BankAccount", credit: 100), Line("CashBox", debit: 100)),       // between two cash accounts: net zero
                    Event(4, Month(2).AddDays(1), AccountingEventStatus.NotRequired, Line("BankAccount", debit: 9999)),                                // not an event of the books
                    Event(5, Month(1).AddDays(1), AccountingEventStatus.Pending, Line("BankAccount", debit: 500), Line("Revenue", credit: 500)),     // pending counts, as it does on the dashboard
                    Event(6, DateTime.Today, AccountingEventStatus.Posted, Line("BankAccount", debit: 77)));                                          // the current month is the live value
                await db.SaveChangesAsync();
            }
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            List<CeoTrendPoint> points = await CeoHistory.DeriveKeyAsync(uow, CeoKeys.Cash, null, PastMonths(), null, CancellationToken.None);

            Assert.IsNotNull(points);
            CollectionAssert.AreEqual(new[] { Month(3), Month(2), Month(1) }, points.Select(p => p.Month).ToArray(), "no point before the month of the first event: that is no history, not a zero");
            CollectionAssert.AreEqual(new[] { 1000m, 800m, 1300m }, points.Select(p => p.Value).ToArray());
            CollectionAssert.AreEqual(new[] { EndOfMonth(3), EndOfMonth(2), EndOfMonth(1) }, points.Select(p => p.Date).ToArray(), "each point is the end of its month");
        }

        [TestMethod]
        public async Task A_Key_With_No_Records_Has_No_History_Not_A_Row_Of_Zeros()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            int ageingCalls = 0;
            Func<DateTime, CancellationToken, Task<decimal?>> ageing = (day, ct) => { ageingCalls++; return Task.FromResult<decimal?>(1m); };

            foreach (string key in CeoHistory.Keys)
            {
                List<CeoTrendPoint> points = await CeoHistory.DeriveKeyAsync(uow, key, ageing, PastMonths(), null, CancellationToken.None);
                Assert.IsNotNull(points, key);
                Assert.AreEqual(0, points.Count, key);
            }
            Assert.AreEqual(0, ageingCalls, "the receivables module is not asked for a month before the first invoice");
            Assert.AreEqual(0, (await CeoHistory.DeriveKeyAsync(uow, CeoKeys.Cash, null, new List<DateTime>(), null, CancellationToken.None)).Count, "no month, no point");
        }

        // ==================================================================================== open NCRs

        internal static NonConformance Ncr(int id, DateTime reported, NcrStatus status, DateTimeOffset? closed = null, bool deleted = false)
        {
            var ncr = new NonConformance { Id = id, Number = "NCR-" + id, ReportDate = reported, Status = status, ClosedAt = closed };
            if (deleted) ncr.Delete();
            return ncr;
        }

        [TestMethod]
        public async Task Open_Ncrs_At_A_Month_End_Are_The_Reports_Made_By_Then_And_Not_Closed_Or_Cancelled_By_Then()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            using (IServiceScope seed = provider.CreateScope())
            {
                var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<NonConformance>().AddRange(
                    Ncr(1, Month(3).AddDays(2), NcrStatus.Open),                                                                              // open at all three month ends
                    Ncr(2, Month(2).AddDays(5), NcrStatus.Closed, new DateTimeOffset(Month(1).AddDays(3))),                                  // open at the end of month 2, closed in month 1
                    Ncr(3, Month(2).AddDays(7), NcrStatus.Cancelled),                                                                          // cancelled: never counted
                    Ncr(4, Month(1).AddDays(10), NcrStatus.Approved),                                                                         // reported in month 1
                    Ncr(5, Month(1).AddDays(11), NcrStatus.Open, deleted: true),                                                              // deleted: never counted
                    Ncr(6, DateTime.Today, NcrStatus.Open));                                                                                  // the current month is the live value
                await db.SaveChangesAsync();
            }
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            List<CeoTrendPoint> points = await CeoHistory.DeriveKeyAsync(uow, CeoKeys.OpenNcrs, null, PastMonths(), null, CancellationToken.None);

            Assert.IsNotNull(points);
            CollectionAssert.AreEqual(new[] { Month(3), Month(2), Month(1) }, points.Select(p => p.Month).ToArray());
            CollectionAssert.AreEqual(new[] { 1m, 2m, 2m }, points.Select(p => p.Value).ToArray(), "month 3: #1; month 2: #1 and #2; month 1: #1 and #4 (#2 is closed by then)");
        }

        // ==================================================================================== SLA breaches

        private static SvServiceRequest Request(int id, DateTime received, SvRequestStatus status, DateTime? responseDue = null, DateTime? resolutionDue = null, DateTime? firstResponse = null, DateTime? resolved = null, bool deleted = false)
        {
            var request = new SvServiceRequest
            {
                Id = id, Number = "SR-" + id, CustomerId = 1, ReceivedAt = received, Description = "x", Type = SvRequestType.Breakdown, Status = status,
                ResponseDue = responseDue, ResolutionDue = resolutionDue, FirstResponseAt = firstResponse, ResolvedAt = resolved,
            };
            if (deleted) request.Delete();
            return request;
        }

        [TestMethod]
        public async Task Sla_Breaches_At_A_Month_End_Are_The_Requests_Open_Then_And_Past_Their_Due_Time_At_That_Moment()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            using (IServiceScope seed = provider.CreateScope())
            {
                var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<SvServiceRequest>().AddRange(
                    // answered in time, resolved late in month 1: breached (resolution) at the ends of months 3 and 2, not at the end of month 1 (it was resolved by then)
                    Request(1, Month(3).AddDays(1), SvRequestStatus.Resolved, Month(3).AddDays(2), Month(3).AddDays(3), Month(3).AddDays(1), Month(1).AddDays(5)),
                    // received in month 2, answered in time, never resolved: breached at the ends of months 2 and 1
                    Request(2, Month(2).AddDays(2), SvRequestStatus.InProgress, Month(2).AddDays(3), Month(2).AddDays(4), Month(2).AddDays(2)),
                    // received in month 1 with a due time in the current month: not past due at any past month end
                    Request(3, Month(1).AddDays(2), SvRequestStatus.Open, Month(0).AddDays(20)),
                    // deleted: never counted
                    Request(4, Month(3).AddDays(1), SvRequestStatus.Open, Month(3).AddDays(2), deleted: true));
                await db.SaveChangesAsync();
            }
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            List<CeoTrendPoint> points = await CeoHistory.DeriveKeyAsync(uow, CeoKeys.ServiceSlaBreaches, null, PastMonths(), null, CancellationToken.None);

            Assert.IsNotNull(points);
            CollectionAssert.AreEqual(new[] { Month(3), Month(2), Month(1) }, points.Select(p => p.Month).ToArray());
            CollectionAssert.AreEqual(new[] { 1m, 2m, 1m }, points.Select(p => p.Value).ToArray());
        }

        // ==================================================================================== receivables

        [TestMethod]
        public async Task Receivables_At_A_Month_End_Come_From_The_As_Of_Ageing_From_The_Month_Of_The_First_Posted_Invoice()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            using (IServiceScope seed = provider.CreateScope())
            {
                var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                FnSalesInvoice Sale(int id, DateTime date, FnInvoiceType type, FnDocStatus status) =>
                    new FnSalesInvoice { Id = id, Number = "S-" + id, Date = date, CustomerId = 1, Type = type, Status = status, NetAmount = 100 };
                db.Set<FnSalesInvoice>().AddRange(
                    Sale(1, Month(5).AddDays(3), FnInvoiceType.CreditNote, FnDocStatus.Posted),     // a credit note is not the start of the receivables
                    Sale(2, Month(4).AddDays(3), FnInvoiceType.Progress, FnDocStatus.Draft),        // a draft is not posted
                    Sale(3, Month(3).AddDays(3), FnInvoiceType.Progress, FnDocStatus.Posted));      // the first real invoice
                await db.SaveChangesAsync();
            }
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var asked = new List<DateTime>();
            Func<DateTime, CancellationToken, Task<decimal?>> ageing = (day, ct) =>
            {
                asked.Add(day);
                if (day == EndOfMonth(3)) return Task.FromResult<decimal?>(40m);
                if (day == EndOfMonth(2)) return Task.FromResult<decimal?>(null);      // the receivables module could not answer for this day
                if (day == EndOfMonth(1)) return Task.FromResult<decimal?>(60m);
                throw new InvalidOperationException("asked for " + day);
            };

            List<CeoTrendPoint> points = await CeoHistory.DeriveKeyAsync(uow, CeoKeys.ReceivablesOpen, ageing, PastMonths(), null, CancellationToken.None);

            Assert.IsNotNull(points);
            CollectionAssert.AreEqual(new[] { EndOfMonth(3), EndOfMonth(2), EndOfMonth(1) }, asked, "one ageing per month end, from the month of the first invoice, in order");
            CollectionAssert.AreEqual(new[] { Month(3), Month(1) }, points.Select(p => p.Month).ToArray(), "a month the module could not answer for has no point");
            CollectionAssert.AreEqual(new[] { 40m, 60m }, points.Select(p => p.Value).ToArray());
        }

        [TestMethod]
        public async Task A_Derivation_That_Fails_Is_Reported_As_Null_So_That_The_Failure_Is_Not_Remembered()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            using (IServiceScope seed = provider.CreateScope())
            {
                var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<FnSalesInvoice>().Add(new FnSalesInvoice { Id = 1, Number = "S-1", Date = Month(3).AddDays(3), CustomerId = 1, Type = FnInvoiceType.Progress, Status = FnDocStatus.Posted, NetAmount = 100 });
                await db.SaveChangesAsync();
            }
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var log = new CeoOverviewServiceTests.RecordingLogger<CeoOverviewService>();
            Func<DateTime, CancellationToken, Task<decimal?>> broken = (day, ct) => throw new InvalidOperationException("the receivables module is down");

            List<CeoTrendPoint> points = await CeoHistory.DeriveKeyAsync(uow, CeoKeys.ReceivablesOpen, broken, PastMonths(), log, CancellationToken.None);

            Assert.IsNull(points, "null means could not be read; an empty list means there is no history");
            Assert.AreEqual(1, log.Problems.Count(p => p.StartsWith("Warning", StringComparison.Ordinal)), string.Join(Environment.NewLine, log.Problems));

            bool refused = false;
            try { await CeoHistory.DeriveKeyAsync(uow, CeoKeys.StockValue, null, PastMonths(), null, CancellationToken.None); }
            catch (ArgumentOutOfRangeException) { refused = true; }
            Assert.IsTrue(refused, "a key that cannot be derived is a programming error, not a failure to read");
        }

        [TestMethod]
        public async Task The_Derive_Call_Skips_The_Months_A_Snapshot_Covers_And_Returns_Only_The_Keys_That_Have_History()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            using (IServiceScope seed = provider.CreateScope())
            {
                var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<NonConformance>().Add(Ncr(1, Month(3).AddDays(2), NcrStatus.Open));
                await db.SaveChangesAsync();
            }
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var covered = new Dictionary<string, HashSet<DateTime>> { [CeoKeys.OpenNcrs] = new HashSet<DateTime> { Month(2) } };

            Dictionary<string, List<CeoTrendPoint>> derived = await CeoHistory.DeriveAsync(uow, null, DateTime.Today, covered, null, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { CeoKeys.OpenNcrs }, derived.Keys.ToArray(), "cash, receivables and SLA have no records: no entry");
            CollectionAssert.AreEqual(new[] { Month(3), Month(1) }, derived[CeoKeys.OpenNcrs].Select(p => p.Month).ToArray(), "month 2 is covered by a real snapshot and is not derived");
        }

        // ==================================================================================== through the overview

        private static async Task SeedNcrsAsync(ServiceProvider provider)
        {
            using IServiceScope seed = provider.CreateScope();
            var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Set<NonConformance>().AddRange(
                Ncr(1, Month(3).AddDays(2), NcrStatus.Open),
                Ncr(2, Month(2).AddDays(5), NcrStatus.Closed, new DateTimeOffset(Month(1).AddDays(3))),
                Ncr(3, Month(2).AddDays(7), NcrStatus.Cancelled),
                Ncr(4, Month(1).AddDays(10), NcrStatus.Approved));
            await db.SaveChangesAsync();
        }

        [TestMethod]
        public async Task The_Overview_Shows_The_Derived_Months_Then_The_Live_Value_And_The_Change_Against_The_Previous_Month()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            await SeedNcrsAsync(provider);
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            CeoDashboardDto dashboard = CeoOverviewServiceTests.Dashboard();
            dashboard.OpenNcrs = 5;
            var log = new CeoOverviewServiceTests.RecordingLogger<CeoOverviewService>();

            CeoOverviewDto overview = (await CeoOverviewServiceTests.Overview(uow, "CEO", dashboard, new MemoryCache(new MemoryCacheOptions()), log).GetOverviewAsync(true, CancellationToken.None)).Data;

            CeoKpiDto ncrs = CeoOverviewServiceTests.Kpi(overview, CeoKeys.OpenNcrs);
            CollectionAssert.AreEqual(new[] { 1m, 2m, 2m, 5m }, ncrs.Trend, "three derived month ends, then the live value");
            CollectionAssert.AreEqual(new[] { CeoText.MonthLabel(Month(3)), CeoText.MonthLabel(Month(2)), CeoText.MonthLabel(Month(1)), CeoText.MonthLabel(DateTime.Today) }, ncrs.TrendLabels);
            Assert.AreEqual(3m, ncrs.Delta, "5 now against 2 at the end of last month");
            Assert.AreEqual(false, ncrs.DeltaGood, "more open NCRs is not good");
            Assert.AreEqual(ncrs.Trend.Last(), ncrs.Value.Value);

            // the other keys have no records: the live point alone
            CeoKpiDto cash = CeoOverviewServiceTests.Kpi(overview, CeoKeys.Cash);
            CollectionAssert.AreEqual(new[] { cash.Value.Value }, cash.Trend);
            // a key that cannot be derived never shows more than the live point either
            CeoKpiDto stock = CeoOverviewServiceTests.Kpi(overview, CeoKeys.StockValue);
            Assert.AreEqual(1, stock.Trend.Count);
            Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
        }

        [TestMethod]
        public async Task A_Real_Snapshot_Wins_Over_The_Derived_Month_And_The_Months_Around_It_Stay_Derived()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            await SeedNcrsAsync(provider);
            using (IServiceScope seed = provider.CreateScope())
            {
                var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<CeoKpiSnapshot>().Add(new CeoKpiSnapshot { Date = Month(1).AddDays(10), Key = CeoKeys.OpenNcrs, Value = 40m, CreatedAt = DateTimeOffset.Now });
                await db.SaveChangesAsync();
            }
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            CeoDashboardDto dashboard = CeoOverviewServiceTests.Dashboard();
            dashboard.OpenNcrs = 5;
            var log = new CeoOverviewServiceTests.RecordingLogger<CeoOverviewService>();

            CeoOverviewDto overview = (await CeoOverviewServiceTests.Overview(uow, "CEO", dashboard, new MemoryCache(new MemoryCacheOptions()), log).GetOverviewAsync(true, CancellationToken.None)).Data;

            CeoKpiDto ncrs = CeoOverviewServiceTests.Kpi(overview, CeoKeys.OpenNcrs);
            CollectionAssert.AreEqual(new[] { 1m, 2m, 40m, 5m }, ncrs.Trend, "the snapshot of last month (40) replaces the derived 2; the earlier months stay derived");
            Assert.AreEqual(-35m, ncrs.Delta);
            Assert.AreEqual(true, ncrs.DeltaGood, "fewer open NCRs is good");
            Assert.AreEqual(0, log.Problems.Count, string.Join(Environment.NewLine, log.Problems));
        }

        [TestMethod]
        public async Task The_Derived_History_Is_Kept_For_A_While_And_A_Failed_Read_Is_Tried_Again_On_The_Next_Request()
        {
            using ServiceProvider provider = CeoOverviewServiceTests.InMemory();
            await SeedNcrsAsync(provider);
            using IServiceScope scope = provider.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            CeoDashboardDto dashboard = CeoOverviewServiceTests.Dashboard();
            dashboard.OpenNcrs = 5;
            var cache = new MemoryCache(new MemoryCacheOptions());
            var log = new CeoOverviewServiceTests.RecordingLogger<CeoOverviewService>();
            CeoOverviewService service = CeoOverviewServiceTests.Overview(uow, "CEO", dashboard, cache, log);

            await service.GetOverviewAsync(true, CancellationToken.None);

            // a second computation finds the derived points in the cache: a record added now does not change them until the history lifetime has passed
            using (IServiceScope more = provider.CreateScope())
            {
                var db = more.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Set<NonConformance>().Add(Ncr(9, Month(3).AddDays(4), NcrStatus.Open));
                await db.SaveChangesAsync();
            }
            CeoOverviewDto second = (await service.GetOverviewAsync(true, CancellationToken.None)).Data;
            CollectionAssert.AreEqual(new[] { 1m, 2m, 2m, 5m }, CeoOverviewServiceTests.Kpi(second, CeoKeys.OpenNcrs).Trend, "served from the history cache");
            Assert.AreEqual(TimeSpan.FromMinutes(30), CeoCache.HistoryLifetime);
        }
    }
}
