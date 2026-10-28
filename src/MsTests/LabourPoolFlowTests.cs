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
    /// R1p review items 1, 2 and 4 end to end on an in-memory database: the daily cost run absorbs payroll-rated labour at the standard rate and then at the pooled average rate of the last closed month; the month-end
    /// run closes the pool, posts ONE company-level true-up line per pool, carries a month that is too small and flags the quarter-end line. The in-memory provider runs the same code as the server but does not
    /// translate the queries to SQL (the translation of the grouped queries is checked in <c>ProductionR1Tests</c>). Every figure is worked out by hand in the comments.
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

            public Harness(decimal? standardLabourRate = 80000m)
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
                Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
                Costing = new CostingService(Db, new FakeUser(1, "FINANCEMANAGER"));
            }

            public void Dispose()
            {
                _scope.Dispose();
                _provider.Dispose();
            }

            public MfLabourService Labour(string role = "FINANCEMANAGER") => new MfLabourService(Db, new FakeUser(2, role), Costing, Configuration);
            public MfCostEngine Engine() => new MfCostEngine(Db, Costing, 3, Configuration);
        }

        // ------------------------------------------------------------------------------------------ production

        /// <summary>The CNC work centre (cost centre CNC, standard labour rate 90,000 for people without a payslip), one order, one operation, and the employees:
        /// e1 100,000 / h, e2 120,000, e3 90,000, e4 110,000 (employer cost 20,000,000 / 24,000,000 / 18,000,000 / 22,000,000 over 25 days x 8 h = 200 paid hours) and e5 without a payslip.</summary>
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
            foreach (var (code, cost) in new[] { ("e1", 20000000m), ("e2", 24000000m), ("e3", 18000000m), ("e4", 22000000m), ("e5", 0m) })
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

        private static async Task<CostRunResultDto> RunDayAsync(Harness h, DateTime day, CancellationToken ct = default)
        {
            CostRunResultDto result = await h.Engine().RunAsync(day.Date, ct);
            return result;
        }

        private static async Task<List<AccountingEvent>> EventsAsync(ApplicationDbContext db, string sourceType, int sourceId) =>
            await db.Set<AccountingEvent>().Include(e => e.Lines).Where(e => e.SourceType == sourceType && e.SourceId == sourceId).ToListAsync();

        [TestMethod]
        public async Task The_Days_Absorb_At_The_Standard_Rate_Then_At_The_Pool_Rate_And_Every_Month_End_Posts_One_Company_Level_Variance_Line()
        {
            using var h = new Harness();
            var (op, people) = await SeedProductionAsync(h.Db);
            DateTime d1 = new DateTime(2026, 8, 3), d2 = new DateTime(2026, 8, 4);   // 1405/05/12 and 1405/05/13 (Mordad)
            h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e1"], d1, 8, 8), Book(op, people["e2"], d1, 8, 4), Book(op, people["e5"], d1, 9, 3), Book(op, people["e3"], d2, 8, 6), Book(op, people["e4"], d2, 8, 2));
            await h.Db.SaveChangesAsync();

            // ---- step 1: no month is closed yet; the payroll-rated people are absorbed at the standard rate 80,000 (the setting), e5 (no payslip) at the work-centre rate 90,000
            CostRunResultDto day1 = await RunDayAsync(h, d1);
            Assert.AreEqual(3, day1.BookingsPosted);
            Assert.AreEqual(2, day1.LabourAtStandardRate);
            Assert.AreEqual(0, day1.LabourAtPoolRate);
            Assert.AreEqual(2, day1.LabourRateFromPayroll);
            Assert.AreEqual(1, day1.LabourRateFromWorkCenter);
            Assert.AreEqual(1230000m, day1.LabourAmount, "(8 + 4) h x 80,000 + 3 h x 90,000 = 960,000 + 270,000");
            CostRunResultDto day2 = await RunDayAsync(h, d2);
            Assert.AreEqual(640000m, day2.LabourAmount, "(6 + 2) h x 80,000");
            Assert.AreEqual(0, day2.Errors.Count, string.Join("; ", day2.Errors));

            // the labour entry of a day is the hours times the rate of the pool, per work order: nothing in it is one person's pay
            List<CostEntry> entries = await h.Db.Set<CostEntry>().Where(c => c.SourceType == MfCostService.RunSource && c.Kind == CostKind.Labour).OrderBy(c => c.EntryDate).ToListAsync();
            CollectionAssert.AreEqual(new[] { 1230000m, 640000m }, entries.Select(c => c.Amount).ToArray());
            Assert.IsTrue(entries.All(c => c.WorkOrderId == 7 && c.ContractId == 3 && !c.LabourProtected));

            // what the booking of a payroll-rated employee remembers for the true-up
            MfTimeBooking b1 = await h.Db.Set<MfTimeBooking>().SingleAsync(b => b.EmployeeId == people["e1"].Id);
            Assert.AreEqual(800000m, b1.PayrollCost, "8 h x 100,000");
            Assert.AreEqual(640000m, b1.AbsorbedLabour, "8 h x 80,000");
            Assert.IsTrue(b1.RateFromPayroll);
            Assert.AreEqual(140505, b1.LabourPendingPeriod);
            MfTimeBooking b5 = await h.Db.Set<MfTimeBooking>().SingleAsync(b => b.EmployeeId == people["e5"].Id);
            Assert.IsNull(b5.LabourPendingPeriod, "a booking costed at the work-centre rate is final and is in no pool");
            Assert.IsFalse(b5.RateFromPayroll);

            // ---- step 2: the month ends. Pool CNC: e1..e4 = 4 people, 20 h, cost 800,000 + 480,000 + 540,000 + 220,000 = 2,040,000, absorbed 640,000 + 320,000 + 480,000 + 160,000 = 1,600,000
            //      -> rate 2,040,000 / 20 = 102,000 and ONE line of 440,000
            MfLabourMonthResultDto first = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 9, 1) }, default)).Data;
            Assert.AreEqual(1, first.Periods.Count);
            Assert.AreEqual(140505, first.Periods[0].Period);
            Assert.AreEqual(4, first.Periods[0].Bookings);
            MfLabourPoolDto cnc = first.Periods[0].Pools.Single();
            Assert.AreEqual("CNC", cnc.PoolKey);
            Assert.AreEqual(4, cnc.People);
            Assert.AreEqual(20m, cnc.Hours);
            Assert.AreEqual(102000m, cnc.Rate);
            Assert.AreEqual(1600000m, cnc.Absorbed);
            Assert.AreEqual(440000m, cnc.Variance);
            Assert.IsFalse(cnc.Protected);

            MfLabourPool row = await h.Db.Set<MfLabourPool>().SingleAsync();
            Assert.AreEqual((MfLabourModule.Production, 140505, "CNC"), (row.Module, row.Period, row.PoolKey));
            Assert.AreEqual((2040000m, 1600000m, 440000m, 102000m, false, 4), (row.Cost, row.Absorbed, row.Variance, row.Rate.Value, row.Protected, row.Items));
            AccountingEvent trueUp = (await EventsAsync(h.Db, MfLabourMonth.SourceType, 140505)).Single();
            Assert.AreEqual(2, trueUp.Lines.Count);
            AccountingEventLine variance = trueUp.Lines.Single(l => l.AccountRole == AccountRoles.LabourRateVariance), absorbedLine = trueUp.Lines.Single(l => l.AccountRole == AccountRoles.LabourAbsorbed);
            Assert.AreEqual((440000m, 0m), (variance.Debit, variance.Credit), "under-absorbed: the variance account is debited");
            Assert.AreEqual((0m, 440000m), (absorbedLine.Debit, absorbedLine.Credit), "and the absorbed-labour account credited");
            Assert.IsTrue(trueUp.Lines.All(l => l.CostCenter == "CNC" && l.WorkOrderId == null && l.ContractId == null && !l.LabourProtected), "no work order, no contract, no day on the line");
            CostEntry monthEntry = await h.Db.Set<CostEntry>().SingleAsync(c => c.SourceType == MfLabourMonth.SourceType);
            Assert.AreEqual((440000m, "CNC", false), (monthEntry.Amount, monthEntry.CostCenter, monthEntry.LabourProtected));
            Assert.IsNull(monthEntry.WorkOrderId);
            Assert.AreEqual(4, await h.Db.Set<MfTimeBooking>().CountAsync(b => b.LabourAggregatedAt != null));
            // a second run closes nothing (idempotent)
            MfLabourMonthResultDto again = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 9, 1) }, default)).Data;
            Assert.AreEqual(0, again.Periods.Count);
            Assert.AreEqual(1, await h.Db.Set<MfLabourPool>().CountAsync());

            // ---- step 3: Shahrivar. e1 works 5 h on 08-25 (1405/06/03), and a late booking of 2 h for 08-10 (month 1405/05, already closed) is entered afterwards
            h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e1"], new DateTime(2026, 8, 25), 8, 5), Book(op, people["e1"], new DateTime(2026, 8, 10), 8, 2));
            await h.Db.SaveChangesAsync();
            CostRunResultDto shahrivar = await RunDayAsync(h, new DateTime(2026, 8, 25));
            CostRunResultDto late = await RunDayAsync(h, new DateTime(2026, 8, 10));
            Assert.AreEqual(1, shahrivar.LabourAtPoolRate);
            Assert.AreEqual(1, late.LabourAtPoolRate);
            Assert.AreEqual(510000m, shahrivar.LabourAmount, "5 h x the pool rate 102,000");
            Assert.AreEqual(204000m, late.LabourAmount, "2 h x 102,000");
            List<MfTimeBooking> shahrivarBookings = await h.Db.Set<MfTimeBooking>().Where(b => b.StartAt >= new DateTime(2026, 8, 10) && b.StartAt < new DateTime(2026, 8, 26)).ToListAsync();
            Assert.IsTrue(shahrivarBookings.All(b => b.LabourPendingPeriod == 140506), "the late booking goes to the first month that is not closed: 1405/06");

            //      the month ends with ONE payroll-rated person in the whole company: nothing can be published, but Shahrivar is a quarter end, so ONE company-wide line is posted, flagged;
            //      cost 500,000 + 200,000 = 700,000, absorbed 714,000: over-absorbed by 14,000
            MfLabourMonthResultDto quarter = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 10, 1) }, default)).Data;
            Assert.AreEqual(1, quarter.Periods.Count);
            Assert.AreEqual(140506, quarter.Periods[0].Period);
            Assert.IsTrue(quarter.Periods[0].Protected);
            MfLabourPoolDto company = quarter.Periods[0].Pools.Single();
            Assert.AreEqual((MfLabourPooling.CompanyKey, true, 1, 7m), (company.PoolKey, company.IsCompany, company.People, company.Hours));
            Assert.IsNull(company.Rate, "a protected pool has no rate");
            Assert.AreEqual(714000m, company.Absorbed, "the finance manager reads the amounts");
            Assert.AreEqual(-14000m, company.Variance);
            AccountingEvent flagged = (await EventsAsync(h.Db, MfLabourMonth.SourceType, 140506)).Single();
            Assert.IsTrue(flagged.Lines.All(l => l.LabourProtected && l.CostCenter == null && l.WorkOrderId == null), "both lines of the event carry the flag, no cost centre, no work order");
            AccountingEventLine flaggedVariance = flagged.Lines.Single(l => l.AccountRole == AccountRoles.LabourRateVariance);
            Assert.AreEqual((0m, 14000m), (flaggedVariance.Debit, flaggedVariance.Credit), "over-absorbed: the variance account is credited");
            Assert.AreEqual((14000m, 0m), (flagged.Lines.Single(l => l.AccountRole == AccountRoles.LabourAbsorbed).Debit, flagged.Lines.Single(l => l.AccountRole == AccountRoles.LabourAbsorbed).Credit));
            CostEntry flaggedEntry = await h.Db.Set<CostEntry>().SingleAsync(c => c.SourceType == MfLabourMonth.SourceType && c.SourceId == 140506);
            Assert.AreEqual((-14000m, null, true), (flaggedEntry.Amount, flaggedEntry.CostCenter, flaggedEntry.LabourProtected));
            MfLabourPool flaggedPool = await h.Db.Set<MfLabourPool>().SingleAsync(p => p.Period == 140506);
            Assert.IsTrue(flaggedPool.Protected);
            Assert.IsNull(flaggedPool.Rate);

            // the history hides the amounts of a protected pool from a reader who may not see them (the production manager), and shows them to the finance manager
            List<MfLabourPeriodDto> seenByManager = (await h.Labour("PRODUCTIONMANAGER").HistoryAsync(default)).Data;
            MfLabourPeriodDto protectedMonth = seenByManager.Single(p => p.Period == 140506);
            Assert.IsTrue(protectedMonth.Protected);
            Assert.IsNull(protectedMonth.Variance);
            Assert.IsNull(protectedMonth.Pools.Single().Absorbed);
            Assert.IsNull(protectedMonth.Pools.Single().Variance);
            Assert.AreEqual(440000m, seenByManager.Single(p => p.Period == 140505).Variance, "an ordinary month is plain for everybody who reads the screen");
            Assert.AreEqual(-14000m, (await h.Labour().HistoryAsync(default)).Data.Single(p => p.Period == 140506).Variance);
            Assert.IsFalse((await h.Labour("SALES").HistoryAsync(default)).Success, "and a role outside the readers reads nothing");

            // ---- step 4: Mehr (1405/07), two people only, not a quarter end: the month is carried to the next one and no pool row exists for it
            h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e1"], new DateTime(2026, 10, 5), 8, 4), Book(op, people["e2"], new DateTime(2026, 10, 5), 8, 2));
            await h.Db.SaveChangesAsync();
            CostRunResultDto mehr = await RunDayAsync(h, new DateTime(2026, 10, 5));
            Assert.AreEqual(2, mehr.LabourAtPoolRate, "the protected pool of Shahrivar has no rate: the newest rate is still the one of 1405/05");
            Assert.AreEqual(612000m, mehr.LabourAmount, "(4 + 2) h x 102,000");
            MfLabourMonthResultDto carry = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 11, 2) }, default)).Data;
            Assert.AreEqual(0, carry.Periods.Count);
            Assert.AreEqual(2, carry.BookingsCarried);
            Assert.IsFalse(await h.Db.Set<MfLabourPool>().AnyAsync(p => p.Period == 140507), "nothing is published for a month that is too small");
            Assert.IsTrue((await h.Db.Set<MfTimeBooking>().Where(b => b.StartAt >= new DateTime(2026, 10, 5) && b.StartAt < new DateTime(2026, 10, 6)).ToListAsync()).All(b => b.LabourPendingPeriod == 140508 && b.LabourAggregatedAt == null));

            // ---- step 5: Aban (1405/08): e3 6 h and e4 4 h join the two carried bookings -> four people, 16 h; cost 400,000 + 240,000 + 540,000 + 440,000 = 1,620,000; absorbed 408,000 + 204,000 + 612,000 + 408,000 = 1,632,000
            //      -> rate 1,620,000 / 16 = 101,250 and a line of -12,000
            h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e3"], new DateTime(2026, 11, 4), 8, 6), Book(op, people["e4"], new DateTime(2026, 11, 4), 8, 4));
            await h.Db.SaveChangesAsync();
            CostRunResultDto aban = await RunDayAsync(h, new DateTime(2026, 11, 4));
            Assert.AreEqual(1020000m, aban.LabourAmount, "(6 + 4) h x 102,000");
            MfLabourMonthResultDto pooled = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 12, 1) }, default)).Data;
            Assert.AreEqual(1, pooled.Periods.Count);
            Assert.AreEqual(140508, pooled.Periods[0].Period);
            Assert.AreEqual(4, pooled.Periods[0].Bookings, "the two carried bookings and the two of Aban");
            MfLabourPoolDto abanPool = pooled.Periods[0].Pools.Single();
            Assert.AreEqual(("CNC", 4, 16m, 101250m, 1632000m, -12000m), (abanPool.PoolKey, abanPool.People, abanPool.Hours, abanPool.Rate, abanPool.Absorbed, abanPool.Variance));
            Assert.IsFalse(abanPool.Protected);

            // ---- step 6: the next day absorbs at the newest closed rate, 101,250; a correction waits for its second person
            h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e1"], new DateTime(2026, 12, 3), 8, 3), Book(op, people["e2"], new DateTime(2026, 12, 3), 12, 1, correction: true));
            await h.Db.SaveChangesAsync();
            CostRunResultDto december = await RunDayAsync(h, new DateTime(2026, 12, 3));
            Assert.AreEqual(1, december.BookingsPosted, "the correction is pending: no cost run takes it");
            Assert.AreEqual(303750m, december.LabourAmount, "3 h x the pool rate 101,250");
            MfTimeBooking pending = await h.Db.Set<MfTimeBooking>().SingleAsync(b => b.IsCorrection);
            Assert.IsNull(pending.CostPostedAt);
            pending.CorrectionApprovedByUserId = 9;
            pending.CorrectionApprovedAt = DateTimeOffset.Now;
            await h.Db.SaveChangesAsync();
            CostRunResultDto approved = await RunDayAsync(h, new DateTime(2026, 12, 3));
            Assert.AreEqual(1, approved.BookingsPosted, "once approved the correction is costed");
            Assert.AreEqual(101250m, approved.LabourAmount, "1 h x 101,250");
        }

        [TestMethod]
        public async Task The_Cost_Centres_With_Three_People_Have_Their_Own_Pool_And_The_Rest_Form_The_Company_Pool_Each_With_Its_Own_Rate_For_The_Days()
        {
            using var h = new Harness();
            var (op, people) = await SeedProductionAsync(h.Db);
            // a second centre WELD with its own cost centre: e1, e2, e3 work on CNC; the three of them and e4 on WELD would make both pools big enough, so use e4/e5-less sets:
            // CNC: e1, e2, e3 (3 people); WELD: e4 only (1 person) -> WELD cannot be a pool of its own, its person is alone in the rest -> everything dissolves into one company pool
            var weld = new MfWorkCenter { Code = "MF-WELD", Title = "Weld", Kind = MfWorkCenterKind.Welding, CostCenter = "WELD", LabourRateRial = 95000m, IsActive = true };
            h.Db.Set<MfWorkCenter>().Add(weld);
            await h.Db.SaveChangesAsync();
            var weldOp = new MfOperation { OrderId = op.OrderId, Sequence = 20, Title = "Weld", WorkCenterId = weld.Id, Status = MfOperationStatus.InProgress };
            h.Db.Set<MfOperation>().Add(weldOp);
            await h.Db.SaveChangesAsync();
            DateTime day = new DateTime(2026, 8, 3);
            h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e1"], day, 8, 8), Book(op, people["e2"], day, 8, 4), Book(op, people["e3"], day, 8, 6), Book(weldOp, people["e4"], day, 8, 2));
            await h.Db.SaveChangesAsync();
            Assert.AreEqual(4, (await RunDayAsync(h, day)).BookingsPosted);

            // cost: CNC 800,000 + 480,000 + 540,000 = 1,820,000 (18 h), WELD 220,000 (2 h): e4 is alone in WELD, so one company pool of 4 people, 20 h, 2,040,000 -> rate 102,000
            MfLabourMonthResultDto result = (await h.Labour().RunAsync(new MfLabourMonthRunDto { AsOf = new DateTime(2026, 9, 1) }, default)).Data;
            MfLabourPoolDto only = result.Periods.Single().Pools.Single();
            Assert.AreEqual((MfLabourPooling.CompanyKey, 4, 20m, 102000m), (only.PoolKey, only.People, only.Hours, only.Rate), "the CNC rate 101,111.1111 is never published beside a one-person cost centre");

            // the days then absorb at that company rate in both cost centres
            h.Db.Set<MfTimeBooking>().AddRange(Book(op, people["e1"], new DateTime(2026, 9, 6), 8, 1), Book(weldOp, people["e4"], new DateTime(2026, 9, 6), 8, 1));
            await h.Db.SaveChangesAsync();
            CostRunResultDto next = await RunDayAsync(h, new DateTime(2026, 9, 6));
            Assert.AreEqual(2, next.LabourAtPoolRate);
            Assert.AreEqual(204000m, next.LabourAmount, "(1 + 1) h x 102,000");
        }

        // ------------------------------------------------------------------------------------------ after-sales

        private static SvServiceOrder ClosedOrder(string number, int period, decimal payrollCost, decimal pooled, params (int Employee, decimal Hours, bool Payroll)[] entries)
        {
            var order = new SvServiceOrder
            {
                Number = number, Title = "آزمون " + number, UnitId = 1, Status = SvOrderStatus.Closed, Decision = SvWarrantyDecision.Warranty, LabourPendingPeriod = period, LabourPayrollCost = payrollCost, LabourPooledAmount = pooled,
                LabourHours = entries.Sum(e => e.Hours), CostCenterCode = "AFTERSALES",
            };
            foreach (var e in entries)
                order.TimeEntries.Add(new SvTimeEntry { EmployeeId = e.Employee, Hours = e.Hours, Kind = SvTimeKind.Work, WorkDate = new DateTime(2026, 11, 10), RateFromPayroll = e.Payroll });
            return order;
        }

        [TestMethod]
        public async Task The_Service_Month_Pool_Posts_One_Company_Level_Line_At_The_Pool_Rate_And_Ignores_Engineers_Without_A_Payslip()
        {
            using var h = new Harness();
            // 1405/08: O1 engineers 1 and 2, 5 h each, payroll cost 1,000,000, absorbed 900,000; O2 engineer 3, 8 h, cost 1,200,000, absorbed 1,125,000; engineer 9 has no payslip (2 h at the standard rate: not in the pool)
            h.Db.Set<SvServiceOrder>().AddRange(
                ClosedOrder("SVO-T1", 140508, 1000000m, 900000m, (1, 5m, true), (2, 5m, true)),
                ClosedOrder("SVO-T2", 140508, 1200000m, 1125000m, (3, 8m, true), (9, 2m, false)));
            await h.Db.SaveChangesAsync();
            var service = new SvLabourService(h.Db, new FakeUser(2, "FINANCEMANAGER"), h.Costing, h.Configuration);

            // the month 1405/08 has not ended on 11-20
            Assert.AreEqual(0, (await service.RunAsync(new SvJobsRunDto { AsOf = new DateTime(2026, 11, 20) }, default)).Data.Periods.Count);
            SvLabourAggregateResultDto result = (await service.RunAsync(new SvJobsRunDto { AsOf = new DateTime(2026, 12, 1) }, default)).Data;
            SvLabourPeriodDto month = result.Periods.Single();
            // three engineers, 18 h; cost 2,200,000; absorbed 2,025,000; variance 175,000; rate 2,200,000 / 18 = 122,222.2222
            Assert.AreEqual((140508, 2, 3, 18m), (month.Period, month.Orders, month.People, month.Hours));
            Assert.AreEqual(122222.2222m, month.Rate);
            Assert.AreEqual(2025000m, month.Absorbed);
            Assert.AreEqual(175000m, month.Variance);
            Assert.IsFalse(month.Protected);

            AccountingEvent ev = (await EventsAsync(h.Db, SvLabourMonth.SourceType, 140508)).Single();
            Assert.AreEqual(2, ev.Lines.Count);
            Assert.AreEqual((175000m, 0m), (ev.Lines.Single(l => l.AccountRole == AccountRoles.LabourRateVariance).Debit, ev.Lines.Single(l => l.AccountRole == AccountRoles.LabourRateVariance).Credit));
            Assert.AreEqual((0m, 175000m), (ev.Lines.Single(l => l.AccountRole == AccountRoles.ServiceLabourAbsorbed).Debit, ev.Lines.Single(l => l.AccountRole == AccountRoles.ServiceLabourAbsorbed).Credit));
            Assert.IsTrue(ev.Lines.All(l => l.CostCenter == "AFTERSALES" && l.WorkOrderId == null && l.ContractId == null && !l.LabourProtected), "no order number, no contract on the line");
            CostEntry entry = await h.Db.Set<CostEntry>().SingleAsync(c => c.SourceType == SvLabourMonth.SourceType);
            Assert.AreEqual((175000m, "AFTERSALES", false), (entry.Amount, entry.CostCenter, entry.LabourProtected));
            Assert.IsTrue(await h.Db.Set<SvServiceOrder>().AllAsync(o => o.LabourAggregatedAt != null));
            Assert.AreEqual(0, (await service.RunAsync(new SvJobsRunDto { AsOf = new DateTime(2026, 12, 1) }, default)).Data.Periods.Count, "a second run closes nothing");

            // the history shows the closed month with its rate to the readers
            SvLabourPeriodDto history = (await service.HistoryAsync(default)).Data.Single();
            Assert.AreEqual((122222.2222m, 175000m), (history.Rate.Value, history.Variance.Value));
        }

        [TestMethod]
        public async Task A_Service_Month_With_Fewer_Than_Three_Engineers_Waits_And_Is_Posted_Flagged_At_The_Quarter_End()
        {
            using var h = new Harness();
            // O5 (1405/07, engineer 1, 2 h, cost 300,000, absorbed 280,000) is alone in Mehr: carried; O6 (1405/09 = Azar, a quarter end, engineer 2, 4 h, cost 600,000, absorbed 540,000)
            h.Db.Set<SvServiceOrder>().AddRange(ClosedOrder("SVO-T5", 140507, 300000m, 280000m, (1, 2m, true)), ClosedOrder("SVO-T6", 140509, 600000m, 540000m, (2, 4m, true)));
            await h.Db.SaveChangesAsync();
            var service = new SvLabourService(h.Db, new FakeUser(2, "CEO"), h.Costing, h.Configuration);

            SvLabourAggregateResultDto first = (await service.RunAsync(new SvJobsRunDto { AsOf = new DateTime(2026, 11, 2 + 0) }, default)).Data;   // 1405/08/11: Mehr has ended, Aban has not
            Assert.AreEqual(0, first.Periods.Count);
            Assert.AreEqual(1, first.OrdersCarried);
            Assert.AreEqual(140508, (await h.Db.Set<SvServiceOrder>().SingleAsync(o => o.Number == "SVO-T5")).LabourPendingPeriod, "carried to the next open month");

            // on 2027-01-01 (1405/10/11) Aban (one engineer, not a quarter end) is carried again and Azar takes both orders: 2 engineers, 6 h, cost 900,000, absorbed 820,000, variance 80,000, flagged, no rate
            SvLabourAggregateResultDto second = (await service.RunAsync(new SvJobsRunDto { AsOf = new DateTime(2027, 1, 1) }, default)).Data;
            SvLabourPeriodDto azar = second.Periods.Single();
            Assert.AreEqual((140509, 2, 2, 6m), (azar.Period, azar.Orders, azar.People, azar.Hours));
            Assert.IsTrue(azar.Protected);
            Assert.IsNull(azar.Rate);
            Assert.AreEqual((820000m, 80000m), (azar.Absorbed.Value, azar.Variance.Value), "the CEO reads a protected amount");
            AccountingEvent ev = (await EventsAsync(h.Db, SvLabourMonth.SourceType, 140509)).Single();
            Assert.IsTrue(ev.Lines.All(l => l.LabourProtected), "both lines of the event are flagged");
            Assert.IsTrue((await h.Db.Set<CostEntry>().SingleAsync(c => c.SourceType == SvLabourMonth.SourceType)).LabourProtected);

            // a reader outside payroll, finance manager and CEO sees no amount of the flagged month (the production manager has no access to the service screen at all)
            var finance = new SvLabourService(h.Db, new FakeUser(5, "FINANCIAL"), h.Costing, h.Configuration);
            SvLabourPeriodDto seen = (await finance.HistoryAsync(default)).Data.Single();
            Assert.IsTrue(seen.Protected);
            Assert.IsNull(seen.Absorbed);
            Assert.IsNull(seen.Variance);
            Assert.IsNull(seen.Rate);
        }
    }
}
