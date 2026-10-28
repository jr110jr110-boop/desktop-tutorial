using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.AfterSales;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.ProductionDtos;
using ViewModels.NasbNiro.ServiceDtos;

namespace MsTests
{
    /// <summary>
    /// R1p round 3 end to end on an in-memory database: the daily cost run absorbs EVERY booking at the standard rate of the work centre (else the setting, else it refuses and names the centre) and remembers the payroll cost
    /// of the payroll-rated people; the month-end run closes the pool of a module, posts ONE company-level variance line (actual payroll cost less what was absorbed) when the month passes the publication gate (at least
    /// five payroll-rated people, nobody over half of the hours), carries a month that does not pass, flags the quarter-end line, and shows the pool only to the payroll readers. The in-memory provider runs the same code as
    /// the server but does not translate the queries to SQL (the translation of the grouped queries is checked in <c>ProductionR1Tests</c>). Every figure is worked out by hand in the comments.
    /// </summary>
    [TestClass]
    public class LabourPoolFlowTests
    {
        private sealed class FakeUser : IInventoryUserContext
        {
            private readonly string[] _roles;
            public FakeUser(int id, params string[] roles) { UserId = id; _roles = roles; }
            public int UserId { get; }
            public bool IsInRole(string role) => role.Split(',').Any(r => _roles.Contains(r.Trim(), StringComparer.OrdinalIgnoreCase));
        }

        private sealed class Harness : IDisposable
        {
            private readonly ServiceProvider _provider;
            private readonly IServiceScope _scope;
            public ApplicationDbContext Db { get; }
            public IConfiguration Configuration { get; }
            public CostingService Costing { get; }

            /// <param name="standardLabourRate">The setting Production:StandardLabourRate (a work centre without a rate of its own takes it); null leaves it unset.</param>
            /// <param name="extra">More settings (the gate: Production:PoolMinPeople and so on).</param>
            public Harness(decimal? standardLabourRate = 80000m, IDictionary<string, string> extra = null)
            {
                var services = new ServiceCollection();
                services.AddOptions();
                services.AddLogging();
                services.Configure<ViewModels.Identity.Settings.SiteSettings>(o => o.CookieOptions = new ViewModels.Identity.Settings.CookieOptions { DistributedSqlServerCacheOptions = new ViewModels.Identity.Settings.DistributedSqlServerCacheOptions { TableName = "AppSqlCache", SchemaName = "dbo" } });
                services.AddHttpContextAccessor();
                string name = "r1p-" + Guid.NewGuid().ToString("N");
                services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name));
                _provider = services.BuildServiceProvider();
                _scope = _provider.CreateScope();
                Db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var settings = new Dictionary<string, string> { ["Service:AllowFutureAsOf"] = "true" };
                if (standardLabourRate.HasValue) settings["Production:StandardLabourRate"] = standardLabourRate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (extra != null) foreach (var kv in extra) settings[kv.Key] = kv.Value;
                Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
                Costing = new CostingService(Db, new FakeUser(1, "FINANCEMANAGER"));
            }

            public void Dispose()
            {
                _scope.Dispose();
                _provider.Dispose();
            }

            public MfLabourService Labour(string role = "FINANCEMANAGER") => new MfLabourService(Db, new FakeUser(2, role), Costing, Configuration);
            public MfCostService Cost(string role = "FINANCEMANAGER") => new MfCostService(Db, new FakeUser(2, role), Costing, Configuration);
            public MfCostEngine Engine() => new MfCostEngine(Db, Costing, 3, Configuration);
            public SvLabourService Service(string role = "FINANCEMANAGER") => new SvLabourService(Db, new FakeUser(2, role), Costing, Configuration);
        }

        // ------------------------------------------------------------------------------------------ production

        /// <summary>The CNC work centre (cost centre CNC, STANDARD labour rate 90,000 an hour that finance set), one order, one operation, and the employees. Their own hourly payroll cost (employer cost over 25 days x 8 h = 200 paid hours):
        /// e1 100,000 (20,000,000), e2 120,000 (24,000,000), e3 90,000 (18,000,000), e4 110,000 (22,000,000), e6 130,000 (26,000,000), e7 80,000 (16,000,000); e5 has no payslip.</summary>
        private static async Task<(MfOperation Op, Dictionary<string, FnEmployee> People)> SeedProductionAsync(ApplicationDbContext db)
        {
            var centre = new MfWorkCenter { Code = "MF-CNC", Title = "CNC", Kind = MfWorkCenterKind.Machine, CostCenter = "CNC", LabourRateRial = 90000m, IsActive = true };
            db.Set<MfWorkCenter>().Add(centre);
            var order = new MfProductionOrder { Number = "PO-T-1", WorkOrderId = 7, ContractId = 3, Status = MfOrderStatus.InProgress, Quantity = 10 };
            db.Set<MfProductionOrder>().Add(order);
            await db.SaveChangesAsync();
            var op = new MfOperation { OrderId = order.Id, Sequence = 10, Title = "CNC", WorkCenterId = centre.Id, Status = MfOperationStatus.InProgress };
            db.Set<MfOperation>().Add(op);

            var run = new FnPayrollRun { Number = "PR-T-1", Year = 1405, Month = 4, Status = FnPayrollStatus.Approved };
            db.Set<FnPayrollRun>().Add(run);
            db.Set<FnPayrollRateTable>().Add(new FnPayrollRateTable { Year = 0, Key = "DailyWorkHours", Value = 8m, Source = "test", CreatedAt = DateTimeOffset.Now });
            var people = new Dictionary<string, FnEmployee>();
            foreach (var (code, cost) in new[] { ("e1", 20000000m), ("e2", 24000000m), ("e3", 18000000m), ("e4", 22000000m), ("e5", 0m), ("e6", 26000000m), ("e7", 16000000m) })
            {
                var employee = new FnEmployee { PersonnelCode = code, FirstName = code, LastName = "آزمون", NationalCode = "00" + code, HireDate = new DateTime(2020, 1, 1), IsActive = true, CreatedAt = DateTimeOffset.Now };
                db.Set<FnEmployee>().Add(employee);
                people[code] = employee;
                if (cost > 0) db.Set<FnPayslip>().Add(new FnPayslip { Run = run, Employee = employee, DaysPaid = 25m, GrossEarnings = cost });
            }
            await db.SaveChangesAsync();
            return (op, people);
        }

        private static MfTimeBooking Book(MfOperation op, FnEmployee who, DateTime day, int fromHour, int hours, bool correction = false)
        {
            DateTime start = day.Date.AddHours(fromHour);
            return new MfTimeBooking
            {
                OperationId = op.Id, EmployeeId = who.Id, Kind = MfBookingKind.Run, StartAt = start, EndAt = start.AddHours(hours), Minutes = hours * 60, Hours = hours, Source = MfBookingSource.Terminal,
                IsCorrection = correction,
            };
        }

        private static Task<CostRunResultDto> RunDayAsync(Harness h, DateTime day, CancellationToken ct = default) => h.Engine().RunAsync(day.Date, ct);

        private static async Task<List<AccountingEvent>> EventsAsync(ApplicationDbContext db, string sourceType, int sourceId) =>
            await db.Set<AccountingEvent>().Include(e => e.Lines).Where(e => e.SourceType == sourceType && e.SourceId == sourceId).ToListAsync();

        [TestMethod]
        public async Task The_Days_Absorb_At_The_Standard_Rate_And_Every_Month_End_Posts_One_Company_Level_Variance_Line()
        {
            using var h = new Harness();
            var (op, people) = await SeedProductionAsync(h.Db);
            DateTime d1 = new DateTime(2026, 8, 3), d2 = new DateTime(2026, 8, 4);   // 1405/05/12 and 1405/05/13 (Mordad)
            h.Db.Set<MfTimeBooking>().AddRange(
                Book(op, people["e1"], d1, 8, 8), Book(op, people["e2"], d1, 8, 4), Book(op, people["e5"], d1, 9, 3),
                Book(op, people["e3"], d2, 8, 6), Book(op, people["e4"], d2, 8, 2), Book(op, people["e6"], d2, 8, 5), Book(op, people["e7"], d2, 8, 3));
            await h.Db.SaveChangesAsync();

            // ---- step 1: every booking is absorbed at the standard rate of the CNC work centre, 90,000 an hour, whoever worked it and whatever he is paid (e5 has no payslip, and the setting 80,000 is not used)
            CostRunResultDto day1 = await RunDayAsync(h, d1);
            Assert.AreEqual(3, day1.BookingsPosted);
            Assert.AreEqual(2, day1.LabourRateFromPayroll);
            Assert.AreEqual(1, day1.LabourRateFromWorkCenter);
            Assert.AreEqual(1350000m, day1.LabourAmount, "(8 + 4 + 3) h x 90,000");
            CostRunResultDto day2 = await RunDayAsync(h, d2);
            Assert.AreEqual(4, day2.BookingsPosted);
            Assert.AreEqual(1440000m, day2.LabourAmount, "(6 + 2 + 5 + 3) h x 90,000");
            Assert.AreEqual(0, day2.Errors.Count, string.Join("; ", day2.Errors));
            Assert.IsFalse(day1.Runs.Single().LabourHidden, "a run of this release is never hidden: it shows hours x a standard");

            // the labour entry of a day is the hours times the standard rate, per work order: nothing in it is one person's pay
            List<CostEntry> entries = await h.Db.Set<CostEntry>().Where(c => c.SourceType == MfCostService.RunSource && c.Kind == CostKind.Labour).OrderBy(c => c.EntryDate).ToListAsync();
            CollectionAssert.AreEqual(new[] { 1350000m, 1440000m }, entries.Select(c => c.Amount).ToArray());
            Assert.IsTrue(entries.All(c => c.WorkOrderId == 7 && c.ContractId == 3 && !c.LabourProtected));
            Assert.IsTrue(await h.Db.Set<MfCostRun>().AllAsync(r => !r.LabourProtected), "no run of this release carries the flag of the earlier one");

            // what the booking of a payroll-rated employee remembers for the month-end variance
            MfTimeBooking b1 = await h.Db.Set<MfTimeBooking>().SingleAsync(b => b.EmployeeId == people["e1"].Id);
            Assert.AreEqual(800000m, b1.PayrollCost, "8 h x his own 100,000");
            Assert.AreEqual(720000m, b1.AbsorbedLabour, "8 h x the standard 90,000");
            Assert.IsTrue(b1.RateFromPayroll);
            Assert.AreEqual(140505, b1.LabourPendingPeriod);
            MfTimeBooking b5 = await h.Db.Set<MfTimeBooking>().SingleAsync(b => b.EmployeeId == people["e5"].Id);
            Assert.IsNull(b5.LabourPendingPeriod, "a person without a payslip is absorbed at the same standard rate and has no variance: he is in no pool");
            Assert.IsNull(b5.PayrollCost);
            Assert.IsFalse(b5.RateFromPayroll);

            // ---- step 2: the month ends. Six payroll-rated people, 28 h (nobody over half; the two largest 14 of 28), passes the gate.
            //      cost 800,000 + 480,000 + 540,000 + 220,000 + 650,000 + 240,000 = 2,930,000, absorbed 28 h x 90,000 = 2,520,000 -> variance 410,000, actual average rate 2,930,000 / 28 = 104,642.8571
            MfLabourMonthResultDto first = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 9, 1) }, default)).Data;
            Assert.AreEqual(1, first.Periods.Count);
            MfLabourPeriodDto mordad = first.Periods[0];
            Assert.AreEqual((140505, "Posted", 6, 2520000m, false), (mordad.Period, mordad.State, mordad.Bookings, mordad.Absorbed, mordad.Protected));
            Assert.AreEqual(410000m, mordad.Variance);
            MfLabourPoolDto pool = mordad.Pools.Single();
            Assert.AreEqual(("*", true, 6, 28m), (pool.PoolKey, pool.IsCompany, pool.People, pool.Hours));
            Assert.AreEqual((104642.8571m, 2930000m, 2520000m, 410000m, false), (pool.Rate.Value, pool.Cost, pool.Absorbed, pool.Variance, pool.Protected));

            MfLabourPool row = await h.Db.Set<MfLabourPool>().SingleAsync();
            Assert.AreEqual((MfLabourModule.Production, 140505, "*"), (row.Module, row.Period, row.PoolKey), "one pool a module and month");
            Assert.AreEqual((2930000m, 2520000m, 410000m, 104642.8571m, false, 6), (row.Cost, row.Absorbed, row.Variance, row.Rate.Value, row.Protected, row.Items));
            AccountingEvent trueUp = (await EventsAsync(h.Db, MfLabourMonth.SourceType, 140505)).Single();
            Assert.AreEqual(2, trueUp.Lines.Count);
            AccountingEventLine variance = trueUp.Lines.Single(l => l.AccountRole == AccountRoles.LabourRateVariance), absorbedLine = trueUp.Lines.Single(l => l.AccountRole == AccountRoles.LabourAbsorbed);
            Assert.AreEqual((410000m, 0m), (variance.Debit, variance.Credit), "under-absorbed: the variance account is debited");
            Assert.AreEqual((0m, 410000m), (absorbedLine.Debit, absorbedLine.Credit), "and the absorbed-labour account credited");
            Assert.IsTrue(trueUp.Lines.All(l => l.CostCenter == null && l.WorkOrderId == null && l.ContractId == null && !l.LabourProtected), "company level: no cost centre, no work order, no contract, no day on the line");
            CostEntry monthEntry = await h.Db.Set<CostEntry>().SingleAsync(c => c.SourceType == MfLabourMonth.SourceType);
            Assert.AreEqual((410000m, null, false), (monthEntry.Amount, monthEntry.CostCenter, monthEntry.LabourProtected));
            Assert.IsNull(monthEntry.WorkOrderId);
            Assert.AreEqual(6, await h.Db.Set<MfTimeBooking>().CountAsync(b => b.LabourAggregatedAt != null));
            // a second run closes nothing (idempotent)
            MfLabourMonthResultDto again = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 9, 1) }, default)).Data;
            Assert.AreEqual(0, again.Periods.Count);
            Assert.AreEqual(1, await h.Db.Set<MfLabourPool>().CountAsync());

            // the pool (people, hours, actual rate, cost, variance) goes to the payroll readers only; the production manager reads the month, its state and what the days absorbed (hours x a standard)
            List<MfLabourPeriodDto> seenByManager = (await h.Labour("PRODUCTIONMANAGER").HistoryAsync(default)).Data;
            MfLabourPeriodDto ordinary = seenByManager.Single(p => p.Period == 140505);
            Assert.AreEqual(("Posted", 6, 2520000m), (ordinary.State, ordinary.Bookings, ordinary.Absorbed));
            Assert.AreEqual(0, ordinary.Pools.Count, "no pool for a reader who is not PAYROLL, FINANCEMANAGER or CEO");
            Assert.IsNull(ordinary.Variance);
            foreach (string role in new[] { "PAYROLL", "CEO", "FINANCEMANAGER" })
            {
                MfLabourPeriodDto plain = (await h.Labour(role).HistoryAsync(default)).Data.Single(p => p.Period == 140505);
                Assert.AreEqual((410000m, 1), (plain.Variance.Value, plain.Pools.Count), role);
            }

            // ---- step 3: Shahrivar. e1 works 5 h on 08-25 (1405/06/03), and a late booking of 2 h for 08-10 (month 1405/05, already closed) is entered afterwards
            h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e1"], new DateTime(2026, 8, 25), 8, 5), Book(op, people["e1"], new DateTime(2026, 8, 10), 8, 2));
            await h.Db.SaveChangesAsync();
            CostRunResultDto shahrivar = await RunDayAsync(h, new DateTime(2026, 8, 25));
            CostRunResultDto late = await RunDayAsync(h, new DateTime(2026, 8, 10));
            Assert.AreEqual(450000m, shahrivar.LabourAmount, "5 h x the standard 90,000: a closed month changes no rate");
            Assert.AreEqual(180000m, late.LabourAmount, "2 h x 90,000");
            List<MfTimeBooking> shahrivarBookings = await h.Db.Set<MfTimeBooking>().Where(b => b.StartAt >= new DateTime(2026, 8, 10) && b.StartAt < new DateTime(2026, 8, 26)).ToListAsync();
            Assert.IsTrue(shahrivarBookings.All(b => b.LabourPendingPeriod == 140506), "the late booking goes to the first month that is not closed: 1405/06");

            //      the month ends with ONE payroll-rated person in the whole company: the gate is not passed, but Shahrivar is a quarter end, so ONE company-wide line is posted, flagged;
            //      cost 7 h x 100,000 = 700,000, absorbed 7 h x 90,000 = 630,000: under-absorbed by 70,000
            MfLabourMonthResultDto quarter = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 10, 1) }, default)).Data;
            Assert.AreEqual(1, quarter.Periods.Count);
            MfLabourPeriodDto shahrivarMonth = quarter.Periods[0];
            Assert.AreEqual((140506, "Protected", true), (shahrivarMonth.Period, shahrivarMonth.State, shahrivarMonth.Protected));
            MfLabourPoolDto company = shahrivarMonth.Pools.Single();
            Assert.AreEqual((MfLabourPooling.CompanyKey, true, 1, 7m), (company.PoolKey, company.IsCompany, company.People, company.Hours));
            Assert.IsNull(company.Rate, "a protected pool has no rate");
            Assert.AreEqual((700000m, 630000m, 70000m), (company.Cost, company.Absorbed, company.Variance), "the finance manager reads the amounts");
            AccountingEvent flagged = (await EventsAsync(h.Db, MfLabourMonth.SourceType, 140506)).Single();
            Assert.IsTrue(flagged.Lines.All(l => l.LabourProtected && l.CostCenter == null && l.WorkOrderId == null), "both lines of the event carry the flag, no cost centre, no work order");
            AccountingEventLine flaggedVariance = flagged.Lines.Single(l => l.AccountRole == AccountRoles.LabourRateVariance);
            Assert.AreEqual((70000m, 0m), (flaggedVariance.Debit, flaggedVariance.Credit));
            AccountingEventLine flaggedAbsorbed = flagged.Lines.Single(l => l.AccountRole == AccountRoles.LabourAbsorbed);
            Assert.AreEqual((0m, 70000m), (flaggedAbsorbed.Debit, flaggedAbsorbed.Credit));
            CostEntry flaggedEntry = await h.Db.Set<CostEntry>().SingleAsync(c => c.SourceType == MfLabourMonth.SourceType && c.SourceId == 140506);
            Assert.AreEqual((70000m, null, true), (flaggedEntry.Amount, flaggedEntry.CostCenter, flaggedEntry.LabourProtected));
            MfLabourPool flaggedPool = await h.Db.Set<MfLabourPool>().SingleAsync(p => p.Period == 140506);
            Assert.IsTrue(flaggedPool.Protected);
            Assert.IsNull(flaggedPool.Rate);

            // the history hides the pool and the variance of the protected month from a reader who may not see them (the production manager), and shows them to the finance manager
            MfLabourPeriodDto protectedMonth = (await h.Labour("PRODUCTIONMANAGER").HistoryAsync(default)).Data.Single(p => p.Period == 140506);
            Assert.AreEqual(("Protected", true, 630000m), (protectedMonth.State, protectedMonth.Protected, protectedMonth.Absorbed));
            Assert.IsNull(protectedMonth.Variance);
            Assert.AreEqual(0, protectedMonth.Pools.Count);
            Assert.AreEqual(70000m, (await h.Labour().HistoryAsync(default)).Data.Single(p => p.Period == 140506).Variance);
            Assert.IsFalse((await h.Labour("SALES").HistoryAsync(default)).Success, "and a role outside the readers reads nothing");

            // ---- step 4: Mehr (1405/07), two people only, not a quarter end: the month is carried to the next one and no pool row exists for it
            h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e1"], new DateTime(2026, 10, 5), 8, 4), Book(op, people["e2"], new DateTime(2026, 10, 5), 8, 2));
            await h.Db.SaveChangesAsync();
            CostRunResultDto mehr = await RunDayAsync(h, new DateTime(2026, 10, 5));
            Assert.AreEqual(540000m, mehr.LabourAmount, "(4 + 2) h x 90,000");
            MfLabourMonthResultDto carry = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 11, 2) }, default)).Data;
            Assert.AreEqual(0, carry.Periods.Count);
            Assert.AreEqual(2, carry.BookingsCarried);
            Assert.IsFalse(await h.Db.Set<MfLabourPool>().AnyAsync(p => p.Period == 140507), "nothing is published for a month that does not pass the gate");
            Assert.IsTrue((await h.Db.Set<MfTimeBooking>().Where(b => b.StartAt >= new DateTime(2026, 10, 5) && b.StartAt < new DateTime(2026, 10, 6)).ToListAsync()).All(b => b.LabourPendingPeriod == 140508 && b.LabourAggregatedAt == null));
            // the month that waits is listed as pending, without any payroll figure
            MfLabourPeriodDto waiting = (await h.Labour("PRODUCTIONMANAGER").HistoryAsync(default)).Data.Single(p => p.Period == 140508);
            Assert.AreEqual(("Pending", 2, 540000m, 0), (waiting.State, waiting.Bookings, waiting.Absorbed, waiting.Pools.Count));
            Assert.IsNull(waiting.Variance);

            // ---- step 5: Aban (1405/08): e3 6 h, e4 4 h, e6 5 h and e7 3 h join the two carried bookings -> six people, 24 h (the largest 6, the two largest 11 of 24: passes);
            //      cost 400,000 + 240,000 + 540,000 + 440,000 + 650,000 + 240,000 = 2,510,000; absorbed 24 h x 90,000 = 2,160,000 -> variance 350,000, actual average rate 2,510,000 / 24 = 104,583.3333
            h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e3"], new DateTime(2026, 11, 4), 8, 6), Book(op, people["e4"], new DateTime(2026, 11, 4), 8, 4),
                Book(op, people["e6"], new DateTime(2026, 11, 4), 8, 5), Book(op, people["e7"], new DateTime(2026, 11, 4), 8, 3));
            await h.Db.SaveChangesAsync();
            CostRunResultDto aban = await RunDayAsync(h, new DateTime(2026, 11, 4));
            Assert.AreEqual(1620000m, aban.LabourAmount, "(6 + 4 + 5 + 3) h x 90,000");
            MfLabourMonthResultDto pooled = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 12, 1) }, default)).Data;
            Assert.AreEqual(1, pooled.Periods.Count);
            Assert.AreEqual((140508, 6), (pooled.Periods[0].Period, pooled.Periods[0].Bookings), "the two carried bookings and the four of Aban");
            MfLabourPoolDto abanPool = pooled.Periods[0].Pools.Single();
            Assert.AreEqual(("*", 6, 24m, 104583.3333m, 2510000m, 2160000m, 350000m), (abanPool.PoolKey, abanPool.People, abanPool.Hours, abanPool.Rate.Value, abanPool.Cost, abanPool.Absorbed, abanPool.Variance));
            Assert.IsFalse(abanPool.Protected);

            // ---- step 6: the next day absorbs at the standard rate again; a correction waits for its second person
            h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e1"], new DateTime(2026, 12, 3), 8, 3), Book(op, people["e2"], new DateTime(2026, 12, 3), 12, 1, correction: true));
            await h.Db.SaveChangesAsync();
            CostRunResultDto december = await RunDayAsync(h, new DateTime(2026, 12, 3));
            Assert.AreEqual(1, december.BookingsPosted, "the correction is pending: no cost run takes it");
            Assert.AreEqual(270000m, december.LabourAmount, "3 h x the standard 90,000");
            MfTimeBooking pending = await h.Db.Set<MfTimeBooking>().SingleAsync(b => b.IsCorrection);
            Assert.IsNull(pending.CostPostedAt);
            pending.CorrectionApprovedByUserId = 9;
            pending.CorrectionApprovedAt = DateTimeOffset.Now;
            await h.Db.SaveChangesAsync();
            CostRunResultDto approved = await RunDayAsync(h, new DateTime(2026, 12, 3));
            Assert.AreEqual(1, approved.BookingsPosted, "once approved the correction is costed");
            Assert.AreEqual(90000m, approved.LabourAmount, "1 h x 90,000");
        }

        [TestMethod]
        public async Task A_Work_Centre_Without_A_Rate_Takes_The_Setting_And_When_There_Is_None_The_Run_Refuses_And_Names_The_Centre()
        {
            // the setting 80,000 serves a work centre that has no standard rate of its own
            using (var h = new Harness())
            {
                var (op, people) = await SeedProductionAsync(h.Db);
                var weld = new MfWorkCenter { Code = "MF-WELD", Title = "Weld", Kind = MfWorkCenterKind.Welding, CostCenter = "WELD", LabourRateRial = null, IsActive = true };
                h.Db.Set<MfWorkCenter>().Add(weld);
                await h.Db.SaveChangesAsync();
                var weldOp = new MfOperation { OrderId = op.OrderId, Sequence = 20, Title = "Weld", WorkCenterId = weld.Id, Status = MfOperationStatus.InProgress };
                h.Db.Set<MfOperation>().Add(weldOp);
                await h.Db.SaveChangesAsync();
                DateTime day = new DateTime(2026, 8, 3);
                h.Db.Set<MfTimeBooking>().AddRange(Book(weldOp, people["e1"], day, 8, 2), Book(op, people["e1"], day, 11, 1));
                await h.Db.SaveChangesAsync();
                CostRunResultDto result = await RunDayAsync(h, day);
                Assert.AreEqual(2, result.BookingsPosted);
                Assert.AreEqual(250000m, result.LabourAmount, "2 h x the setting 80,000 at WELD + 1 h x the work-centre rate 90,000 at CNC");
                Assert.AreEqual(0, result.Errors.Count);
            }

            // no setting and a work centre without a rate: the booking is not costed, the error names the centre, and a person asking for the run gets a refusal
            using (var h = new Harness(standardLabourRate: null))
            {
                var (op, people) = await SeedProductionAsync(h.Db);
                var weld = new MfWorkCenter { Code = "MF-WELD", Title = "Weld", Kind = MfWorkCenterKind.Welding, CostCenter = "WELD", LabourRateRial = null, IsActive = true };
                h.Db.Set<MfWorkCenter>().Add(weld);
                await h.Db.SaveChangesAsync();
                var weldOp = new MfOperation { OrderId = op.OrderId, Sequence = 20, Title = "Weld", WorkCenterId = weld.Id, Status = MfOperationStatus.InProgress };
                h.Db.Set<MfOperation>().Add(weldOp);
                await h.Db.SaveChangesAsync();
                DateTime day = new DateTime(2026, 8, 3);
                h.Db.Set<MfTimeBooking>().AddRange(Book(weldOp, people["e1"], day, 8, 2), Book(op, people["e1"], day, 11, 1));
                await h.Db.SaveChangesAsync();
                CostRunResultDto result = await RunDayAsync(h, day);
                Assert.AreEqual((1, 1), (result.BookingsPosted, result.BookingsSkipped));
                Assert.AreEqual(90000m, result.LabourAmount, "only the CNC hour, at its own work-centre rate");
                string error = result.Errors.Single();
                StringAssert.Contains(error, "MF-WELD");
                StringAssert.Contains(error, "نرخ ساعتی دستمزد استاندارد");
                Assert.IsNull((await h.Db.Set<MfTimeBooking>().SingleAsync(b => b.OperationId == weldOp.Id)).CostPostedAt, "the booking stays unposted: never a zero-cost posting");

                // only the unrated booking left (a day in the past, so that the service accepts it): a refusal that names the work centre
                DateTime past = DateTime.Today.AddDays(-5);
                h.Db.Set<MfTimeBooking>().Add(Book(weldOp, people["e2"], past, 8, 2));
                await h.Db.SaveChangesAsync();
                var refused = await h.Cost().RunAsync(new RunCostDto { Date = past }, default);
                Assert.IsFalse(refused.Success);
                StringAssert.Contains(refused.Message, "MF-WELD");
            }
        }

        [TestMethod]
        public async Task The_Gate_Carries_A_Pool_Of_Three_People_Unless_The_Setting_Lowers_The_Minimum_And_Carries_A_Pool_One_Person_Dominates()
        {
            // three payroll-rated people, 4 h each: not enough for the default minimum of five (carried), enough when the company sets the minimum to three
            foreach (bool lowered in new[] { false, true })
            {
                using var h = new Harness(extra: lowered ? new Dictionary<string, string> { ["Production:PoolMinPeople"] = "3" } : null);
                var (op, people) = await SeedProductionAsync(h.Db);
                DateTime day = new DateTime(2026, 8, 3);
                h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e1"], day, 8, 4), Book(op, people["e2"], day, 8, 4), Book(op, people["e3"], day, 8, 4));
                await h.Db.SaveChangesAsync();
                await RunDayAsync(h, day);
                MfLabourMonthResultDto result = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 9, 1) }, default)).Data;
                if (lowered)
                {
                    // cost 400,000 + 480,000 + 360,000 = 1,240,000, absorbed 12 h x 90,000 = 1,080,000: variance 160,000
                    Assert.AreEqual(1, result.Periods.Count);
                    Assert.AreEqual((3, 12m, 160000m), (result.Periods[0].Pools.Single().People, result.Periods[0].Pools.Single().Hours, result.Periods[0].Variance.Value));
                }
                else
                {
                    Assert.AreEqual(0, result.Periods.Count);
                    Assert.AreEqual(3, result.BookingsCarried);
                    Assert.IsFalse(await h.Db.Set<MfLabourPool>().AnyAsync());
                }
            }

            // six people, but e1 has 12 of 22 hours (more than half): carried
            using var dominated = new Harness();
            var (dop, dpeople) = await SeedProductionAsync(dominated.Db);
            DateTime d = new DateTime(2026, 8, 3);
            dominated.Db.Set<MfTimeBooking>().AddRange(Book(dop, dpeople["e1"], d, 6, 12), Book(dop, dpeople["e2"], d, 8, 2), Book(dop, dpeople["e3"], d, 8, 2), Book(dop, dpeople["e4"], d, 8, 2), Book(dop, dpeople["e6"], d, 8, 2), Book(dop, dpeople["e7"], d, 8, 2));
            await dominated.Db.SaveChangesAsync();
            await RunDayAsync(dominated, d);
            MfLabourMonthResultDto carried = (await dominated.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 9, 1) }, default)).Data;
            Assert.AreEqual((0, 6), (carried.Periods.Count, carried.BookingsCarried), "12 of 22 hours is more than half");
            StringAssert.Contains(carried.Message, "منتقل شد");
        }

        // ------------------------------------------------------------------------------------------ masks of the rows of the earlier release

        [TestMethod]
        public async Task Runs_And_Postings_Of_The_Earlier_Release_Show_No_Labour_To_Readers_Outside_Payroll_Finance_Manager_And_Ceo()
        {
            using var h = new Harness();
            DateTime day = new DateTime(2026, 8, 3);
            var earlier = new MfCostRun { Number = "MCR-OLD", PostingDate = day, RunAt = DateTimeOffset.Now, BookingCount = 3, LabourHours = 10m, LabourAmount = 1500000m, EmployeeCount = 3, PayrollEmployeeCount = 1, LabourProtected = true };
            var current = new MfCostRun { Number = "MCR-NEW", PostingDate = day.AddDays(1), RunAt = DateTimeOffset.Now, BookingCount = 1, LabourHours = 2m, LabourAmount = 180000m, EmployeeCount = 1, PayrollEmployeeCount = 1, LabourProtected = false };
            h.Db.Set<MfCostRun>().AddRange(earlier, current);
            await h.Db.SaveChangesAsync();
            h.Db.Set<MfCostPosting>().AddRange(
                new MfCostPosting { RunId = earlier.Id, PostingDate = day, WorkOrderId = 7, BookingCount = 1, LabourHours = 4m, LabourAmount = 500000m, EmployeeCount = 2, PayrollEmployeeCount = 1 },
                new MfCostPosting { RunId = earlier.Id, PostingDate = day, WorkOrderId = 8, BookingCount = 2, LabourHours = 6m, LabourAmount = 1000000m, EmployeeCount = 4, PayrollEmployeeCount = 4 },
                new MfCostPosting { RunId = current.Id, PostingDate = day.AddDays(1), WorkOrderId = 7, BookingCount = 1, LabourHours = 2m, LabourAmount = 180000m, EmployeeCount = 1, PayrollEmployeeCount = 1 });
            await h.Db.SaveChangesAsync();

            // the production manager: the run of the earlier release and its small posting are hidden; its large posting and the run of this release (one payroll-rated person, absorbed at the standard rate) are plain
            var runs = (await h.Cost("PRODUCTIONMANAGER").GetRunsAsync(new CostRunQuery(), default)).Data;
            CostRunDto oldRun = runs.Single(r => r.Number == "MCR-OLD"), newRun = runs.Single(r => r.Number == "MCR-NEW");
            Assert.AreEqual((true, (decimal?)null), (oldRun.LabourHidden, oldRun.LabourAmount));
            Assert.AreEqual((false, (decimal?)180000m), (newRun.LabourHidden, newRun.LabourAmount));
            var postings = (await h.Cost("PRODUCTIONMANAGER").GetPostingsAsync(new CostPostingQuery(), default)).Data;
            CostPostingDto small = postings.Single(p => p.WorkOrderId == 7 && p.RunNumber == "MCR-OLD"), large = postings.Single(p => p.WorkOrderId == 8), fresh = postings.Single(p => p.RunNumber == "MCR-NEW");
            Assert.AreEqual((true, (decimal?)null), (small.LabourHidden, small.LabourAmount));
            Assert.AreEqual((false, (decimal?)1000000m), (large.LabourHidden, large.LabourAmount));
            Assert.AreEqual((false, (decimal?)180000m), (fresh.LabourHidden, fresh.LabourAmount));

            // the payroll readers see everything
            foreach (string role in new[] { "PAYROLL", "FINANCEMANAGER", "CEO" })
            {
                var seen = (await h.Cost(role).GetRunsAsync(new CostRunQuery(), default)).Data.Single(r => r.Number == "MCR-OLD");
                Assert.AreEqual((false, (decimal?)1500000m), (seen.LabourHidden, seen.LabourAmount), role);
                var seenPosting = (await h.Cost(role).GetPostingsAsync(new CostPostingQuery(), default)).Data.Single(p => p.WorkOrderId == 7 && p.RunNumber == "MCR-OLD");
                Assert.AreEqual((false, (decimal?)500000m), (seenPosting.LabourHidden, seenPosting.LabourAmount), role);
            }
        }

        // ------------------------------------------------------------------------------------------ after-sales

        /// <summary>An order closed by this release: the payroll cost of its payroll-rated hours (<paramref name="payrollCost"/>) and what those hours absorbed at the standard rate (<paramref name="absorbed"/>).</summary>
        private static SvServiceOrder ClosedOrder(string number, int period, decimal payrollCost, decimal absorbed, params (int Employee, decimal Hours, bool Payroll)[] entries)
        {
            var order = new SvServiceOrder
            {
                Number = number, Title = "آزمون " + number, UnitId = 1, Status = SvOrderStatus.Closed, Decision = SvWarrantyDecision.Warranty, LabourPendingPeriod = period, LabourPayrollCost = payrollCost, LabourPooledAmount = absorbed,
                LabourHours = entries.Sum(e => e.Hours), CostCenterCode = "AFTERSALES", LabourEmployeeCount = entries.Select(e => e.Employee).Distinct().Count(), LabourPayrollEmployeeCount = entries.Where(e => e.Payroll).Select(e => e.Employee).Distinct().Count(),
            };
            foreach (var e in entries)
                order.TimeEntries.Add(new SvTimeEntry { EmployeeId = e.Employee, Hours = e.Hours, Kind = SvTimeKind.Work, WorkDate = new DateTime(2026, 11, 10), RateFromPayroll = e.Payroll });
            return order;
        }

        [TestMethod]
        public async Task The_Service_Month_Pool_Posts_One_Company_Level_Line_Against_The_Standard_Rate_And_Ignores_Engineers_Without_A_Payslip()
        {
            using var h = new Harness();
            // 1405/08, the standard rate of the after-sales work centre 90,000. O1: engineers 1 and 2, 5 h each, payroll cost 1,000,000, absorbed 900,000. O2: engineer 3, 8 h, cost 1,200,000, absorbed 720,000, and engineer 9
            // (no payslip, 2 h at the same standard rate: not in the pool). O3: engineers 4 and 5, 6 h each, cost 1,200,000, absorbed 1,080,000.
            h.Db.Set<SvServiceOrder>().AddRange(
                ClosedOrder("SVO-T1", 140508, 1000000m, 900000m, (1, 5m, true), (2, 5m, true)),
                ClosedOrder("SVO-T2", 140508, 1200000m, 720000m, (3, 8m, true), (9, 2m, false)),
                ClosedOrder("SVO-T3", 140508, 1200000m, 1080000m, (4, 6m, true), (5, 6m, true)));
            await h.Db.SaveChangesAsync();
            var service = h.Service();

            // the month 1405/08 has not ended on 11-20
            Assert.AreEqual(0, (await service.RunAsync(new SvJobsRunDto { AsOf = new DateTime(2026, 11, 20) }, default)).Data.Periods.Count);
            SvLabourAggregateResultDto result = (await service.RunAsync(new SvJobsRunDto { AsOf = new DateTime(2026, 12, 1) }, default)).Data;
            SvLabourPeriodDto month = result.Periods.Single();
            // five payroll-rated engineers, 30 h (the largest 8, the two largest 14 of 30: passes the gate); cost 3,400,000; absorbed 2,700,000; variance 700,000; actual average rate 3,400,000 / 30 = 113,333.3333
            Assert.AreEqual((140508, "Posted", 3, 2700000m), (month.Period, month.State, month.Orders, month.Absorbed));
            Assert.AreEqual((5, 30m, 3400000m, 700000m), (month.People.Value, month.Hours.Value, month.Cost.Value, month.Variance.Value));
            Assert.AreEqual(113333.3333m, month.Rate);
            Assert.IsFalse(month.Protected);

            AccountingEvent ev = (await EventsAsync(h.Db, SvLabourMonth.SourceType, 140508)).Single();
            Assert.AreEqual(2, ev.Lines.Count);
            Assert.AreEqual((700000m, 0m), (ev.Lines.Single(l => l.AccountRole == AccountRoles.LabourRateVariance).Debit, ev.Lines.Single(l => l.AccountRole == AccountRoles.LabourRateVariance).Credit));
            Assert.AreEqual((0m, 700000m), (ev.Lines.Single(l => l.AccountRole == AccountRoles.ServiceLabourAbsorbed).Debit, ev.Lines.Single(l => l.AccountRole == AccountRoles.ServiceLabourAbsorbed).Credit));
            Assert.IsTrue(ev.Lines.All(l => l.CostCenter == "AFTERSALES" && l.WorkOrderId == null && l.ContractId == null && !l.LabourProtected), "no order number, no contract on the line");
            CostEntry entry = await h.Db.Set<CostEntry>().SingleAsync(c => c.SourceType == SvLabourMonth.SourceType);
            Assert.AreEqual((700000m, "AFTERSALES", false), (entry.Amount, entry.CostCenter, entry.LabourProtected));
            MfLabourPool pool = await h.Db.Set<MfLabourPool>().SingleAsync();
            Assert.AreEqual((MfLabourModule.Service, 140508, "*", 3), (pool.Module, pool.Period, pool.PoolKey, pool.Items));
            Assert.IsTrue(await h.Db.Set<SvServiceOrder>().AllAsync(o => o.LabourAggregatedAt != null));
            Assert.AreEqual(0, (await service.RunAsync(new SvJobsRunDto { AsOf = new DateTime(2026, 12, 1) }, default)).Data.Periods.Count, "a second run closes nothing");

            // the payroll readers see the pool; finance sees the month, its state and what the orders absorbed, nothing of payroll
            foreach (string role in new[] { "FINANCEMANAGER", "CEO", "PAYROLL" })
            {
                SvLabourPeriodDto history = (await h.Service(role).HistoryAsync(default)).Data.Single();
                Assert.AreEqual((113333.3333m, 700000m, 5), (history.Rate.Value, history.Variance.Value, history.People.Value), role);
            }
            SvLabourPeriodDto finance = (await h.Service("FINANCIAL").HistoryAsync(default)).Data.Single();
            Assert.AreEqual(("Posted", 2700000m), (finance.State, finance.Absorbed));
            Assert.IsTrue(finance.People == null && finance.Hours == null && finance.Rate == null && finance.Cost == null && finance.Variance == null, "no payroll figure for finance");
            Assert.IsFalse((await h.Service("SALES").HistoryAsync(default)).Success);
        }

        [TestMethod]
        public async Task A_Service_Month_Below_The_Gate_Waits_And_Is_Posted_Flagged_At_The_Quarter_End()
        {
            using var h = new Harness();
            // O5 (1405/07, engineer 1, 2 h, cost 300,000, absorbed 180,000) is alone in Mehr: carried; O6 (1405/09 = Azar, a quarter end, engineer 2, 4 h, cost 600,000, absorbed 360,000)
            h.Db.Set<SvServiceOrder>().AddRange(ClosedOrder("SVO-T5", 140507, 300000m, 180000m, (1, 2m, true)), ClosedOrder("SVO-T6", 140509, 600000m, 360000m, (2, 4m, true)));
            await h.Db.SaveChangesAsync();
            var service = h.Service("CEO");

            SvLabourAggregateResultDto first = (await service.RunAsync(new SvJobsRunDto { AsOf = new DateTime(2026, 11, 2) }, default)).Data;   // 1405/08/11: Mehr has ended, Aban has not
            Assert.AreEqual(0, first.Periods.Count);
            Assert.AreEqual(1, first.OrdersCarried);
            Assert.AreEqual(140508, (await h.Db.Set<SvServiceOrder>().SingleAsync(o => o.Number == "SVO-T5")).LabourPendingPeriod, "carried to the next open month");
            SvLabourPeriodDto waiting = (await h.Service("FINANCIAL").HistoryAsync(default)).Data.Single(p => p.Period == 140508);
            Assert.AreEqual(("Pending", 1, 180000m), (waiting.State, waiting.Orders, waiting.Absorbed));
            Assert.IsNull(waiting.Variance);

            // on 2027-01-01 (1405/10/11) Aban (one engineer, not a quarter end) is carried again and Azar takes both orders: 2 engineers, 6 h, cost 900,000, absorbed 540,000, variance 360,000, flagged, no rate
            SvLabourAggregateResultDto second = (await service.RunAsync(new SvJobsRunDto { AsOf = new DateTime(2027, 1, 1) }, default)).Data;
            SvLabourPeriodDto azar = second.Periods.Single();
            Assert.AreEqual((140509, "Protected", 2, true), (azar.Period, azar.State, azar.Orders, azar.Protected));
            Assert.AreEqual((2, 6m), (azar.People.Value, azar.Hours.Value));
            Assert.IsNull(azar.Rate);
            Assert.AreEqual((900000m, 540000m, 360000m), (azar.Cost.Value, azar.Absorbed, azar.Variance.Value), "the CEO reads a protected amount");
            AccountingEvent ev = (await EventsAsync(h.Db, SvLabourMonth.SourceType, 140509)).Single();
            Assert.IsTrue(ev.Lines.All(l => l.LabourProtected), "both lines of the event are flagged");
            Assert.IsTrue((await h.Db.Set<CostEntry>().SingleAsync(c => c.SourceType == SvLabourMonth.SourceType)).LabourProtected);

            // a reader outside payroll, finance manager and CEO sees no amount of the flagged month except what the orders absorbed at the standard rate
            SvLabourPeriodDto seen = (await h.Service("FINANCIAL").HistoryAsync(default)).Data.Single(p => p.Period == 140509);
            Assert.IsTrue(seen.Protected);
            Assert.AreEqual(540000m, seen.Absorbed);
            Assert.IsTrue(seen.People == null && seen.Hours == null && seen.Rate == null && seen.Cost == null && seen.Variance == null);
        }

        [TestMethod]
        public async Task The_Service_Month_Run_Refuses_While_An_Order_Of_The_Earlier_Release_Waits_And_The_Nightly_Run_Skips_It()
        {
            using var h = new Harness();
            // an order closed by the earlier release: it stored only the labour at the engineers' own pay and held it back for the old monthly posting (no payroll cost, no pooled amount, a pending month)
            var legacy = new SvServiceOrder { Number = "SVO-OLD-1", Title = "قدیمی", UnitId = 1, Status = SvOrderStatus.Closed, Decision = SvWarrantyDecision.Warranty, LabourPendingPeriod = 140507, LabourAmount = 400000m, LabourHours = 2m, CostCenterCode = "AFTERSALES", LabourEmployeeCount = 1, LabourPayrollEmployeeCount = 1 };
            // and a new order that would be taken
            h.Db.Set<SvServiceOrder>().AddRange(legacy, ClosedOrder("SVO-T7", 140507, 300000m, 180000m, (1, 2m, true)));
            await h.Db.SaveChangesAsync();

            var refused = await h.Service().RunAsync(new SvJobsRunDto { AsOf = new DateTime(2026, 11, 2) }, default);
            Assert.IsFalse(refused.Success);
            StringAssert.Contains(refused.Message, "SVO-OLD-1");
            StringAssert.Contains(refused.Message, "1405/07");
            Assert.IsFalse(refused.Message.Contains("SVO-T7"), "only the waiting orders of the earlier release are named");
            Assert.AreEqual(0, await h.Db.Set<MfLabourPool>().CountAsync());
            Assert.IsNull((await h.Db.Set<SvServiceOrder>().SingleAsync(o => o.Number == "SVO-T7")).LabourAggregatedAt, "nothing was closed");

            // the nightly run skips (and logs) while the order waits
            Assert.AreEqual(0, await h.Service().RunNightlyAsync(default));
            Assert.AreEqual(0, await h.Db.Set<MfLabourPool>().CountAsync());

            // once the data step converted the order (a payroll cost and a pooled amount of zero or less: the standard-rate part is kept apart), the run goes on
            legacy.LabourPayrollCost = 0m;
            legacy.LabourPooledAmount = -400000m;
            await h.Db.SaveChangesAsync();
            var allowed = await h.Service().RunAsync(new SvJobsRunDto { AsOf = new DateTime(2026, 11, 2) }, default);
            Assert.IsTrue(allowed.Success, allowed.Message);
        }
    }
}
