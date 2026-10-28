using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production;

namespace MsTests
{
    /// <summary>
    /// The pure rules of the production cost posting: the payroll hourly rate, the POOLED AVERAGE LABOUR RATE (R1p review items 1, 2 and 4: a pool per cost centre, the company pool, the carry and the flagged quarter-end line),
    /// the rate the days absorb at, the month-end true-up lines, the voucher mask and the refusal of the Sepidar export, and the payroll/production labour hand-over. Every figure is worked out by hand in the test.
    /// </summary>
    [TestClass]
    public class ProductionCostTests
    {
        [TestMethod]
        public void Paid_Hours_Are_Paid_Days_Times_Daily_Hours_Plus_Overtime_And_Holiday_Hours()
        {
            Assert.AreEqual(208m, MfCostEngine.PaidHours(26, 8, 0, 0));
            Assert.AreEqual(224m, MfCostEngine.PaidHours(26, 8, 10, 6));
            Assert.AreEqual(8m, MfCostEngine.PaidHours(0, 8, 8, 0));
        }

        [TestMethod]
        public void Hourly_Rate_Is_The_Employer_Cost_Divided_By_The_Paid_Hours()
        {
            // employer cost of the month (earnings + employer insurance + provisions) 44,800,000 / (26 x 8 + 10 + 6 = 224 h) = 200,000 a hour
            Assert.AreEqual(200000m, MfCostEngine.HourlyRate(44800000m, MfCostEngine.PaidHours(26, 8, 10, 6)));
            // without the overtime and holiday hours the same cost would give a higher (wrong) rate: 44,800,000 / 208
            Assert.AreNotEqual(200000m, MfCostEngine.HourlyRate(44800000m, MfCostEngine.PaidHours(26, 8, 0, 0)));
            Assert.AreEqual(215384.62m, System.Math.Round(MfCostEngine.HourlyRate(44800000m, 208m), 2));
        }

        // ------------------------------------------------------------------------------------------ the pools of a month (review items 1, 2, 4)

        /// <summary>One booking: one payroll-rated employee, his hours, what payroll says they cost (hours x his own hourly cost, unrounded) and what the days absorbed for them.</summary>
        private static MfPoolItem Item(int id, string centre, int employee, decimal hours, decimal cost, decimal absorbed) =>
            new MfPoolItem { Id = id, CostCenter = centre, Employees = new HashSet<int> { employee }, Hours = hours, Cost = cost, Absorbed = absorbed };

        // The month used by the pool tests. Employee: hours / cost (his own hourly cost x hours) / absorbed by the days.
        //   CNC:    e1 8 h / 800,000 / 700,000    e2 4 h / 480,000 / 350,000    e3 6 h / 540,000 / 525,000
        //   WELD:   e4 5 h / 650,000 / 437,500    e5 5 h / 500,000 / 437,500    e6 10 h / 1,100,000 / 1,125,000
        //   PAINT:  e7 2 h / 160,000 / 150,000
        //   ASM:    e8 3 h / 270,000 / 250,000    e9 5 h / 400,000 / 400,000
        private static MfPoolItem[] Cnc() => new[] { Item(1, "CNC", 1, 8m, 800000m, 700000m), Item(2, "CNC", 2, 4m, 480000m, 350000m), Item(3, "CNC", 3, 6m, 540000m, 525000m) };
        private static MfPoolItem[] Weld() => new[] { Item(4, "WELD", 4, 5m, 650000m, 437500m), Item(5, "WELD", 5, 5m, 500000m, 437500m), Item(6, "WELD", 6, 10m, 1100000m, 1125000m) };
        private static MfPoolItem Paint() => Item(7, "PAINT", 7, 2m, 160000m, 150000m);
        private static MfPoolItem[] Assembly() => new[] { Item(8, "ASM", 8, 3m, 270000m, 250000m), Item(9, "ASM", 9, 5m, 400000m, 400000m) };

        [TestMethod]
        public void The_Pooled_Average_Rate_Is_The_Pool_Cost_Divided_By_The_Pool_Hours_To_Four_Decimals()
        {
            Assert.AreEqual(101111.1111m, MfLabourPooling.Rate(1820000m, 18m), "1,820,000 / 18 h");
            Assert.AreEqual(112500m, MfLabourPooling.Rate(2250000m, 20m));
            Assert.AreEqual(133.6m, MfLabourPooling.Rate(601.2m, 4.5m), "the cost is not rounded before the division");
            Assert.AreEqual(0m, MfLabourPooling.Rate(0m, 5m));
            Assert.IsNull(MfLabourPooling.Rate(1000m, 0m), "a pool without hours has no rate");
            Assert.AreEqual(0.6667m, MfLabourPooling.Rate(2m, 3m));
        }

        [TestMethod]
        public void A_Day_Absorbs_The_Booked_Hours_Times_The_Pool_Rate_To_The_Whole_Rial_Half_Away_From_Zero()
        {
            Assert.AreEqual(808889m, MfLabourRates.Absorbed(8m, 101111.1111m), "8 x 101,111.1111 = 808,888.8888");
            Assert.AreEqual(606667m, MfLabourRates.Absorbed(6m, 101111.1111m), "6 x 101,111.1111 = 606,666.6666");
            Assert.AreEqual(8m, MfLabourRates.Absorbed(2.5m, 3m), "7.5 rounds up");
            Assert.AreEqual(1m, MfLabourRates.Absorbed(0.5m, 1m), "0.5 rounds away from zero");
            Assert.AreEqual(0m, MfLabourRates.Absorbed(0m, 112500m));
        }

        [TestMethod]
        public void Each_Cost_Centre_With_Three_People_Is_Its_Own_Pool_And_The_Month_End_Variance_Is_Cost_Less_Absorbed()
        {
            MfPoolPlan plan = MfLabourPooling.Plan(Cnc().Concat(Weld()), 140505, MfLabourMonth.DefaultCostCenter);
            Assert.AreEqual(0, plan.CarriedItemIds.Count);
            Assert.AreEqual(2, plan.Pools.Count);

            MfPoolResult cnc = plan.Pools[0];
            Assert.AreEqual("CNC", cnc.Key);
            Assert.AreEqual(3, cnc.People);
            Assert.AreEqual(18m, cnc.Hours, "8 + 4 + 6");
            Assert.AreEqual(1820000m, cnc.Cost, "800,000 + 480,000 + 540,000");
            Assert.AreEqual(1575000m, cnc.Absorbed, "700,000 + 350,000 + 525,000");
            Assert.AreEqual(245000m, cnc.Variance, "under-absorbed by 245,000");
            Assert.AreEqual(101111.1111m, cnc.Rate);
            Assert.IsFalse(cnc.Protected);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, cnc.ItemIds);

            MfPoolResult weld = plan.Pools[1];
            Assert.AreEqual("WELD", weld.Key);
            Assert.AreEqual(3, weld.People);
            Assert.AreEqual(20m, weld.Hours);
            Assert.AreEqual(2250000m, weld.Cost);
            Assert.AreEqual(2000000m, weld.Absorbed);
            Assert.AreEqual(250000m, weld.Variance);
            Assert.AreEqual(112500m, weld.Rate);
            CollectionAssert.AreEqual(new[] { 4, 5, 6 }, weld.ItemIds);
        }

        [TestMethod]
        public void The_Cost_Centres_With_Fewer_Than_Three_People_Form_The_Company_Pool_When_They_Are_Three_Together()
        {
            // PAINT has one person and ASM two: alone each would show a person's pay; together they are three people
            MfPoolPlan plan = MfLabourPooling.Plan(Cnc().Concat(new[] { Paint() }).Concat(Assembly()), 140505, MfLabourMonth.DefaultCostCenter);
            Assert.AreEqual(0, plan.CarriedItemIds.Count);
            Assert.AreEqual(2, plan.Pools.Count);
            Assert.AreEqual("CNC", plan.Pools[0].Key, "the cost centre with three people keeps its own pool");
            Assert.AreEqual(101111.1111m, plan.Pools[0].Rate);

            MfPoolResult company = plan.Pools[1];
            Assert.AreEqual(MfLabourPooling.CompanyKey, company.Key);
            Assert.AreEqual(3, company.People);
            Assert.AreEqual(10m, company.Hours, "2 + 3 + 5");
            Assert.AreEqual(830000m, company.Cost, "160,000 + 270,000 + 400,000");
            Assert.AreEqual(800000m, company.Absorbed);
            Assert.AreEqual(30000m, company.Variance);
            Assert.AreEqual(83000m, company.Rate);
            CollectionAssert.AreEqual(new[] { 7, 8, 9 }, company.ItemIds);
        }

        [TestMethod]
        public void One_Or_Two_Left_Over_People_Dissolve_Every_Pool_Of_The_Month_Into_One_Company_Pool()
        {
            // CNC (three people) and PAINT (one): the CNC rate 101,111.1111 published beside a company rate would let anybody subtract it and isolate the person on PAINT
            MfPoolPlan one = MfLabourPooling.Plan(Cnc().Concat(new[] { Paint() }), 140505, MfLabourMonth.DefaultCostCenter);
            Assert.AreEqual(1, one.Pools.Count);
            MfPoolResult all = one.Pools[0];
            Assert.AreEqual(MfLabourPooling.CompanyKey, all.Key);
            Assert.AreEqual(4, all.People);
            Assert.AreEqual(20m, all.Hours, "18 + 2");
            Assert.AreEqual(1980000m, all.Cost, "1,820,000 + 160,000");
            Assert.AreEqual(1725000m, all.Absorbed);
            Assert.AreEqual(255000m, all.Variance);
            Assert.AreEqual(99000m, all.Rate);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 7 }, all.ItemIds);

            // two left over people are the same
            MfPoolPlan two = MfLabourPooling.Plan(Cnc().Concat(new[] { Paint(), Item(8, "ASM", 8, 3m, 270000m, 250000m) }), 140505, MfLabourMonth.DefaultCostCenter);
            Assert.AreEqual(1, two.Pools.Count);
            Assert.AreEqual(MfLabourPooling.CompanyKey, two.Pools[0].Key);
            Assert.AreEqual(5, two.Pools[0].People);
        }

        [TestMethod]
        public void One_Person_Booked_In_Two_Cost_Centres_Counts_Once()
        {
            // employee 1 works in CNC (with e2, e3) and in PAINT: PAINT has one person, so the month is one company pool of the three distinct people
            var items = new[] { Item(1, "CNC", 1, 8m, 800000m, 700000m), Item(2, "CNC", 2, 4m, 480000m, 350000m), Item(3, "CNC", 3, 6m, 540000m, 525000m), Item(10, "PAINT", 1, 1m, 100000m, 90000m) };
            Assert.AreEqual(3, MfLabourPooling.People(items));
            MfPoolPlan plan = MfLabourPooling.Plan(items, 140505, MfLabourMonth.DefaultCostCenter);
            Assert.AreEqual(1, plan.Pools.Count);
            Assert.AreEqual(MfLabourPooling.CompanyKey, plan.Pools[0].Key);
            Assert.AreEqual(3, plan.Pools[0].People);
            Assert.AreEqual(19m, plan.Pools[0].Hours);
            Assert.AreEqual(1920000m, plan.Pools[0].Cost);
        }

        [TestMethod]
        public void A_Month_With_Fewer_Than_Three_Payroll_Rated_People_Is_Carried_And_At_The_Quarter_End_Posted_Flagged_Without_A_Rate()
        {
            var two = new[] { Item(1, "CNC", 1, 8m, 800000m, 700000m), Paint() };

            MfPoolPlan carried = MfLabourPooling.Plan(two, 140505, MfLabourMonth.DefaultCostCenter);
            Assert.AreEqual(0, carried.Pools.Count, "Mordad is not a quarter end: nothing is published");
            CollectionAssert.AreEqual(new[] { 1, 7 }, carried.CarriedItemIds);

            MfPoolPlan capped = MfLabourPooling.Plan(two, 140506, MfLabourMonth.DefaultCostCenter);
            Assert.AreEqual(0, capped.CarriedItemIds.Count);
            Assert.AreEqual(1, capped.Pools.Count, "Shahrivar ends the quarter: ONE company-wide line");
            MfPoolResult line = capped.Pools[0];
            Assert.AreEqual(MfLabourPooling.CompanyKey, line.Key);
            Assert.IsTrue(line.Protected);
            Assert.IsNull(line.Rate, "a protected pool has no rate: it would be the average of one or two people");
            Assert.AreEqual(2, line.People);
            Assert.AreEqual(10m, line.Hours);
            Assert.AreEqual(960000m, line.Cost, "800,000 + 160,000");
            Assert.AreEqual(850000m, line.Absorbed);
            Assert.AreEqual(110000m, line.Variance);

            foreach (int month in new[] { 3, 6, 9, 12 }) Assert.IsTrue(MfLabourPooling.IsQuarterEnd(140500 + month), "month " + month);
            foreach (int month in new[] { 1, 2, 4, 5, 7, 8, 10, 11 }) Assert.IsFalse(MfLabourPooling.IsQuarterEnd(140500 + month), "month " + month);
            Assert.AreEqual(0, MfLabourPooling.Plan(new MfPoolItem[0], 140506, MfLabourMonth.DefaultCostCenter).Pools.Count, "nothing booked, nothing posted");
        }

        [TestMethod]
        public void The_Pool_Cost_Is_Rounded_Once_And_A_Booking_Without_A_Cost_Centre_Belongs_To_The_Production_Cost_Centre()
        {
            // 100.4 + 200.4 + 300.4 = 601.2 -> 601 once (not 100 + 200 + 300); 4.5 hours: rate 601.2 / 4.5 = 133.6; absorbed 600 -> variance 1
            var items = new[]
            {
                Item(1, null, 1, 1.5m, 100.4m, 200m), Item(2, null, 2, 1.5m, 200.4m, 200m), Item(3, null, 3, 1.5m, 300.4m, 200m),
            };
            MfPoolPlan plan = MfLabourPooling.Plan(items, 140505, MfLabourMonth.DefaultCostCenter);
            Assert.AreEqual(1, plan.Pools.Count);
            Assert.AreEqual("PRODUCTION", plan.Pools[0].Key);
            Assert.AreEqual(601m, plan.Pools[0].Cost);
            Assert.AreEqual(133.6m, plan.Pools[0].Rate);
            Assert.AreEqual(1m, plan.Pools[0].Variance);
        }

        [TestMethod]
        public void The_Service_Department_Is_One_Pool_Or_The_Carry_Or_The_Flagged_Quarter_End_Line()
        {
            // three engineers on the orders closed in the month: one pool, rate 90,000 (cost 1,800,000 / 20 h), absorbed 1,700,000 -> variance 100,000
            var three = new[]
            {
                Item(1, null, 1, 8m, 800000m, 760000m), Item(2, null, 2, 7m, 630000m, 665000m), Item(3, null, 3, 5m, 370000m, 275000m),
            };
            MfPoolPlan pooled = MfLabourPooling.PlanSingle(three, 140508, "AFTERSALES");
            Assert.AreEqual(1, pooled.Pools.Count);
            Assert.AreEqual("AFTERSALES", pooled.Pools[0].Key);
            Assert.AreEqual(3, pooled.Pools[0].People);
            Assert.AreEqual(20m, pooled.Pools[0].Hours);
            Assert.AreEqual(1800000m, pooled.Pools[0].Cost);
            Assert.AreEqual(1700000m, pooled.Pools[0].Absorbed);
            Assert.AreEqual(100000m, pooled.Pools[0].Variance);
            Assert.AreEqual(90000m, pooled.Pools[0].Rate);

            // two engineers (the same one on two orders is still one person): carried, then flagged at the end of the quarter (Azar)
            var two = new[] { Item(1, null, 7, 4m, 400000m, 350000m), Item(2, null, 7, 2m, 200000m, 180000m), Item(3, null, 8, 3m, 300000m, 250000m) };
            Assert.AreEqual(2, MfLabourPooling.People(two));
            MfPoolPlan carried = MfLabourPooling.PlanSingle(two, 140508, "AFTERSALES");
            Assert.AreEqual(0, carried.Pools.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, carried.CarriedItemIds);
            MfPoolPlan capped = MfLabourPooling.PlanSingle(two, 140509, "AFTERSALES");
            Assert.AreEqual(0, capped.CarriedItemIds.Count);
            Assert.AreEqual(1, capped.Pools.Count);
            Assert.IsTrue(capped.Pools[0].Protected);
            Assert.IsNull(capped.Pools[0].Rate);
            Assert.AreEqual(900000m, capped.Pools[0].Cost);
            Assert.AreEqual(780000m, capped.Pools[0].Absorbed);
            Assert.AreEqual(120000m, capped.Pools[0].Variance);
        }

        // ------------------------------------------------------------------------------------------ the rate of the days

        [TestMethod]
        public void The_Days_Absorb_At_The_Newest_Closed_Months_Own_Pool_Else_The_Company_Pool_Of_That_Month()
        {
            var pools = new List<MfPoolRate>
            {
                new MfPoolRate { Period = 140505, Key = "CNC", Rate = 100000m },
                new MfPoolRate { Period = 140505, Key = MfLabourPooling.CompanyKey, Rate = 90000m },
                new MfPoolRate { Period = 140506, Key = MfLabourPooling.CompanyKey, Rate = 95000m },
                new MfPoolRate { Period = 140507, Key = "CNC", Rate = 101111.1111m },
                new MfPoolRate { Period = 140507, Key = MfLabourPooling.CompanyKey, Rate = 99000m },
            };
            Assert.AreEqual(101111.1111m, MfLabourRates.Pooled(pools, "CNC").Rate, "the newest month has an own pool of the cost centre");
            Assert.AreEqual(99000m, MfLabourRates.Pooled(pools, "WELD").Rate, "no pool of its own in the newest month: that month's company pool");
            Assert.AreEqual(99000m, MfLabourRates.Pooled(pools, null).Rate, "no cost centre: the company pool");
            Assert.AreEqual(140507, MfLabourRates.Pooled(pools, "WELD").Period);

            // the newest month has the company pool only: it wins over an older own pool (the rate of the cost centre two months ago is not the rate now)
            var older = new List<MfPoolRate>
            {
                new MfPoolRate { Period = 140505, Key = "CNC", Rate = 100000m },
                new MfPoolRate { Period = 140506, Key = MfLabourPooling.CompanyKey, Rate = 95000m },
            };
            Assert.AreEqual(95000m, MfLabourRates.Pooled(older, "CNC").Rate);
            Assert.IsNull(MfLabourRates.Pooled(new List<MfPoolRate>(), "CNC"), "no month closed yet: the standard rate is used");
            // a cost centre code is a key and is compared exactly: «cnc» is not the pool of «CNC»
            var sameMonth = new List<MfPoolRate>
            {
                new MfPoolRate { Period = 140506, Key = "CNC", Rate = 100000m },
                new MfPoolRate { Period = 140506, Key = MfLabourPooling.CompanyKey, Rate = 95000m },
            };
            Assert.AreEqual(100000m, MfLabourRates.Pooled(sameMonth, "CNC").Rate, "within a month the pool of the cost centre wins over the company pool");
            Assert.AreEqual(95000m, MfLabourRates.Pooled(sameMonth, "cnc").Rate);
        }

        [TestMethod]
        public void Until_A_Month_Is_Closed_The_Standard_Rate_Is_The_Setting_Else_The_Work_Centre_Rate_And_Never_Zero()
        {
            Assert.AreEqual(150000m, MfLabourRates.Standard(150000m, 100000m), "the configured rate wins");
            Assert.AreEqual(100000m, MfLabourRates.Standard(null, 100000m));
            Assert.AreEqual(100000m, MfLabourRates.Standard(0m, 100000m), "zero is not a rate");
            Assert.AreEqual(100000m, MfLabourRates.Standard(-5m, 100000m));
            Assert.IsNull(MfLabourRates.Standard(null, null));
            Assert.IsNull(MfLabourRates.Standard(0m, 0m));

            string Read(string value) => MfCostEngine.ConfiguredStandardRate(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["Production:StandardLabourRate"] = value }).Build()) is decimal d ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none";
            Assert.AreEqual("250000", Read("250000"));
            Assert.AreEqual("none", Read("0"));
            Assert.AreEqual("none", Read("-5"));
            Assert.AreEqual("none", Read("abc"));
            Assert.IsNull(MfCostEngine.ConfiguredStandardRate(null));
            Assert.IsNull(MfCostEngine.ConfiguredStandardRate(new ConfigurationBuilder().Build()), "★ the key has no default");
            Assert.AreEqual("Production:StandardLabourRate", MfCostEngine.StandardRateKey);
        }

        // ------------------------------------------------------------------------------------------ the one company-level true-up line

        [TestMethod]
        public void The_Month_End_Variance_Is_Two_Balanced_Lines_Without_A_Work_Order_A_Contract_Or_A_Day()
        {
            // under-absorbed by 245,000 (CNC of the example above): the variance account is debited and the absorbed-labour account credited
            List<AccountingEventLine> under = MfLabourVariance.Lines(245000m, AccountRoles.LabourAbsorbed, "CNC", false, "تفاوت");
            Assert.AreEqual(2, under.Count);
            Assert.AreEqual(AccountRoles.LabourRateVariance, under[0].AccountRole);
            Assert.AreEqual(245000m, under[0].Debit);
            Assert.AreEqual(0m, under[0].Credit);
            Assert.AreEqual(AccountRoles.LabourAbsorbed, under[1].AccountRole);
            Assert.AreEqual(0m, under[1].Debit);
            Assert.AreEqual(245000m, under[1].Credit);
            Assert.IsTrue(under.All(l => l.CostCenter == "CNC" && !l.LabourProtected && l.WorkOrderId == null && l.ContractId == null));
            Assert.AreEqual(under.Sum(l => l.Debit), under.Sum(l => l.Credit));

            // over-absorbed by 90,000 and flagged: the other way round, the company pool has no cost centre
            List<AccountingEventLine> over = MfLabourVariance.Lines(-90000m, AccountRoles.ServiceLabourAbsorbed, null, true, "تفاوت");
            Assert.AreEqual(AccountRoles.LabourRateVariance, over[0].AccountRole);
            Assert.AreEqual(0m, over[0].Debit);
            Assert.AreEqual(90000m, over[0].Credit);
            Assert.AreEqual(AccountRoles.ServiceLabourAbsorbed, over[1].AccountRole);
            Assert.AreEqual(90000m, over[1].Debit);
            Assert.IsTrue(over.All(l => l.CostCenter == null && l.LabourProtected), "both lines of a protected pool carry the flag");
            Assert.AreEqual(over.Sum(l => l.Debit), over.Sum(l => l.Credit));

            Assert.AreEqual(0, MfLabourVariance.Lines(0m, AccountRoles.LabourAbsorbed, "CNC", false, "x").Count, "an exactly absorbed pool posts nothing");
        }

        // ------------------------------------------------------------------------------------------ who sees a protected line

        [TestMethod]
        public void A_Protected_Voucher_Line_Is_Hidden_From_Everybody_Outside_Payroll_Finance_Manager_And_Ceo()
        {
            // the flagged lines always come as one debit and one credit of the same amount, so the books of the other readers stay balanced
            IQueryable<FnVoucherLine> lines = new List<FnVoucherLine>
            {
                new FnVoucherLine { Id = 1, Debit = 245000m },
                new FnVoucherLine { Id = 2, Credit = 245000m },
                new FnVoucherLine { Id = 3, Debit = 90000m, LabourProtected = true },
                new FnVoucherLine { Id = 4, Credit = 90000m, LabourProtected = true },
            }.AsQueryable();
            CollectionAssert.AreEqual(new[] { 1, 2 }, FnLabourMask.Visible(lines, false).Select(l => l.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, FnLabourMask.Visible(lines, true).Select(l => l.Id).ToArray());
            Assert.AreEqual(FnLabourMask.Visible(lines, false).Sum(l => l.Debit), FnLabourMask.Visible(lines, false).Sum(l => l.Credit), "still balanced for the other readers");
        }

        [TestMethod]
        public void The_Sepidar_Export_Of_A_Protected_Line_Is_Refused_And_Names_The_Persian_Months_Once()
        {
            Assert.IsNull(FinanceLinkService.ProtectedLabourRefusal(new DateTime[0]));
            string refusal = FinanceLinkService.ProtectedLabourRefusal(new[] { MfLabourMonth.End(140509), MfLabourMonth.End(140506), MfLabourMonth.End(140506).AddHours(10) });
            StringAssert.Contains(refusal, "1405/06، 1405/09", "the months in order, each once");
            StringAssert.Contains(refusal, "فقط مدیر مالی، مدیرعامل یا حقوق و دستمزد");
            Assert.IsFalse(refusal.Contains("api/"), "no API path in a user text");
        }

        private sealed class FakeUser : IInventoryUserContext
        {
            private readonly string[] _roles;
            public FakeUser(params string[] roles) { _roles = roles; }
            public int UserId => 7;
            public bool IsInRole(string role) => role.Split(',').Any(r => _roles.Contains(r.Trim(), StringComparer.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Payroll_Figures_Are_Read_By_Payroll_Finance_Manager_And_Ceo_And_Names_By_The_Shop_Management_Too()
        {
            CollectionAssert.AreEquivalent(new[] { "PAYROLL", "FINANCEMANAGER", "CEO" }, MfPrivacy.PayrollRoles.Split(','));
            CollectionAssert.AreEquivalent(new[] { "PRODUCTIONMANAGER", "SHOPSUPERVISOR", "PAYROLL", "FINANCEMANAGER", "CEO" }, MfPrivacy.NamedRoles.Split(','));
            foreach (string role in new[] { "PAYROLL", "FINANCEMANAGER", "CEO" }) Assert.IsTrue(MfPrivacy.SeesPayroll(new FakeUser(role)), role);
            foreach (string role in new[] { "PRODUCTIONMANAGER", "SHOPSUPERVISOR", "PMO", "FINANCIAL", "SALES", "AUDITOR" }) Assert.IsFalse(MfPrivacy.SeesPayroll(new FakeUser(role)), role);
            foreach (string role in new[] { "PRODUCTIONMANAGER", "SHOPSUPERVISOR", "PAYROLL", "FINANCEMANAGER", "CEO" }) Assert.IsTrue(MfPrivacy.SeesNames(new FakeUser(role)), role);
            foreach (string role in new[] { "PMO", "OPERATOR", "SALES", "QC" }) Assert.IsFalse(MfPrivacy.SeesNames(new FakeUser(role)), role);

            // a seat of the AI (or the evaluation user, which acts as CEO) never sees either: other people read what it writes
            var scopes = new ServiceCollection().AddHttpContextAccessor().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            var ceo = new AiActingUser { UserId = 5, UserName = "seat.eval", Roles = new[] { "CEO" } };
            (bool payroll, bool names) = AiActing.RunAsync(scopes, ceo, "EVAL", sp => Task.FromResult((MfPrivacy.SeesPayroll(new FakeUser("CEO")), MfPrivacy.SeesNames(new FakeUser("CEO"))))).GetAwaiter().GetResult();
            Assert.IsFalse(payroll);
            Assert.IsFalse(names);
            Assert.IsTrue(MfPrivacy.SeesPayroll(new FakeUser("CEO")), "outside the run nothing changed");
        }

        [TestMethod]
        public void The_Role_Lists_Of_The_Cost_Screens_Match_The_Named_Roles_And_The_Month_End_Run_Roles()
        {
            // review item 4: the attendance differences name the people, so they are read by exactly the named roles
            Assert.AreEqual(MfPrivacy.NamedRoles, ProductionRoles.AttendanceCheck);
            // review item 18: the month-end run is posted by finance and the production manager; payroll only reads
            CollectionAssert.AreEquivalent(new[] { "FINANCEMANAGER", "FINANCIAL", "PRODUCTIONMANAGER" }, MfCostService.PostRoles.Split(','));
            CollectionAssert.IsSubsetOf(new[] { "FINANCEMANAGER", "FINANCIAL", "PRODUCTIONMANAGER", "PAYROLL" }, MfCostService.ReadRoles.Split(','));
            Assert.IsFalse(MfCostService.PostRoles.Split(',').Contains("PAYROLL"));
            Assert.IsNull(typeof(MfCostService).GetField("PayrollRoles"), "the unused list of payroll roles is gone (MfPrivacy.PayrollRoles is the one)");
        }

        // ------------------------------------------------------------------------------------------ the months

        [TestMethod]
        public void The_Month_Arithmetic_Of_The_Monthly_Posting_Is_The_Persian_Calendar()
        {
            Assert.AreEqual(140505, MfLabourMonth.Of(new DateTime(2026, 8, 6)));
            Assert.AreEqual("MLM-140506", MfLabourMonth.Number(140506));
            Assert.AreEqual("1405/05", MfLabourMonth.Text(140505));
            Assert.AreEqual(new DateTime(2026, 8, 22), MfLabourMonth.End(140505), "Mordad has 31 days: 1405/05/31");
            Assert.AreEqual(new DateTime(2026, 9, 22), MfLabourMonth.End(140506), "Shahrivar has 31 days: 1405/06/31");
            Assert.AreEqual(140506, MfLabourMonth.NextOpen(140505, new int[0]));
            Assert.AreEqual(140507, MfLabourMonth.NextOpen(140505, new[] { 140506 }), "a closed month is never reopened");
            Assert.AreEqual(140601, MfLabourMonth.NextOpen(140512, new int[0]));
            Assert.AreEqual("CNC", MfLabourMonth.CostCenterOf(" CNC "));
            Assert.IsNull(MfLabourMonth.CostCenterOf("  "));
            Assert.IsNull(MfLabourMonth.CostCenterOf(null));
        }

        [TestMethod]
        public void A_Late_Booking_Goes_To_Its_Own_Month_Or_The_First_Later_Month_That_Is_Not_Closed()
        {
            var day = new DateTime(2026, 8, 6); // 1405/05
            Assert.AreEqual(140505, MfLabourMonth.Assign(day, new int[0]));
            Assert.AreEqual(140505, MfLabourMonth.Assign(day, new[] { 140504 }), "an earlier closed month does not matter");
            Assert.AreEqual(140506, MfLabourMonth.Assign(day, new[] { 140505 }), "its month is closed: the next one takes it");
            Assert.AreEqual(140507, MfLabourMonth.Assign(day, new[] { 140505, 140506 }));
            Assert.AreEqual(140601, MfLabourMonth.Assign(new DateTime(2027, 3, 1), new[] { 140512 }), "across the end of the Persian year");
        }

        // ------------------------------------------------------------------------------------------ payroll hand-over

        [TestMethod]
        public void Payroll_Skips_The_Labour_Entry_Of_A_Work_Order_That_Production_Already_Absorbed()
        {
            var labour = new Dictionary<int, decimal> { { 1, 1000m }, { 2, 0m }, { 3, 500m }, { 4, 250m } };
            var posted = PayrollRunService.LabourEntriesToPost(labour, new HashSet<int> { 3 });
            CollectionAssert.AreEqual(new[] { 1, 4 }, posted.ConvertAll(x => x.Key));
            CollectionAssert.AreEqual(new[] { 1000m, 250m }, posted.ConvertAll(x => x.Value));
            Assert.AreEqual(3, PayrollRunService.LabourEntriesToPost(labour, new HashSet<int>()).Count); // nothing absorbed: all three non-zero entries, as before
        }
    }
}
