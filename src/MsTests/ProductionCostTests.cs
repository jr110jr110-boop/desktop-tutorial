using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
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
    /// The pure rules of the production cost posting: the payroll hourly rate, the STANDARD RATE the days absorb at, the month's pool and its publication gate (R1p round 3: at least five payroll-rated people, nobody over half
    /// of the hours, the two largest over four fifths; otherwise carried, and flagged at the quarter end), the month-end variance lines, the voucher mask and the refusal of the Sepidar export, the masks of the rows of the
    /// earlier release, and the payroll/production labour hand-over. Every figure is worked out by hand in the test.
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

        // ------------------------------------------------------------------------------------------ the month of a module: the pool, the gate, the carry (round 3)

        /// <summary>One booking: one payroll-rated employee, his hours, what payroll says they cost (hours x his own hourly cost, unrounded) and what the day absorbed for them at the STANDARD rate.</summary>
        private static MfPoolItem Item(int id, int employee, decimal hours, decimal cost, decimal absorbed) => MfPoolItem.Of(id, employee, hours, cost, absorbed);

        // The month used by the pool tests: the standard rate is 100,000 a hour, six payroll-rated people.
        //   person: hours / cost (hours x his own hourly cost) / absorbed (hours x 100,000)
        //   e1  8 h /   800,000 /   800,000      e2  6 h /   660,000 /   600,000      e3  6 h /   540,000 /   600,000
        //   e4  5 h /   600,000 /   500,000      e5  5 h /   450,000 /   500,000      e6  4 h /   520,000 /   400,000
        //   together 34 h / 3,570,000 / 3,400,000: variance 170,000 (under-absorbed), actual average rate 105,000
        private static MfPoolItem[] SixPeople() => new[]
        {
            Item(1, 1, 8m, 800000m, 800000m), Item(2, 2, 6m, 660000m, 600000m), Item(3, 3, 6m, 540000m, 600000m),
            Item(4, 4, 5m, 600000m, 500000m), Item(5, 5, 5m, 450000m, 500000m), Item(6, 6, 4m, 520000m, 400000m),
        };

        /// <summary>Items of people with the given hours and nothing else (cost and absorbed are not looked at by the gate).</summary>
        private static MfPoolItem[] People(params decimal[] hours) => hours.Select((h, i) => Item(i + 1, i + 1, h, 0m, 0m)).ToArray();

        [TestMethod]
        public void The_Actual_Average_Rate_Is_The_Pool_Cost_Divided_By_The_Pool_Hours_To_Four_Decimals()
        {
            Assert.AreEqual(105000m, MfLabourPooling.Rate(3570000m, 34m), "3,570,000 / 34 h");
            Assert.AreEqual(101111.1111m, MfLabourPooling.Rate(1820000m, 18m), "1,820,000 / 18 h");
            Assert.AreEqual(133.6m, MfLabourPooling.Rate(601.2m, 4.5m), "the cost is not rounded before the division");
            Assert.AreEqual(0m, MfLabourPooling.Rate(0m, 5m));
            Assert.IsNull(MfLabourPooling.Rate(1000m, 0m), "a pool without hours has no rate");
            Assert.AreEqual(0.6667m, MfLabourPooling.Rate(2m, 3m));
        }

        [TestMethod]
        public void A_Day_Absorbs_The_Booked_Hours_Times_The_Standard_Rate_To_The_Whole_Rial_Half_Away_From_Zero()
        {
            Assert.AreEqual(800000m, MfLabourRates.Absorbed(8m, 100000m), "8 h x 100,000");
            Assert.AreEqual(808889m, MfLabourRates.Absorbed(8m, 101111.1111m), "8 x 101,111.1111 = 808,888.8888");
            Assert.AreEqual(606667m, MfLabourRates.Absorbed(6m, 101111.1111m), "6 x 101,111.1111 = 606,666.6666");
            Assert.AreEqual(8m, MfLabourRates.Absorbed(2.5m, 3m), "7.5 rounds up");
            Assert.AreEqual(1m, MfLabourRates.Absorbed(0.5m, 1m), "0.5 rounds away from zero");
            Assert.AreEqual(0m, MfLabourRates.Absorbed(0m, 112500m));
        }

        [TestMethod]
        public void The_Gate_Needs_Five_People_With_Hours()
        {
            var defaults = new MfLabourGateSettings();
            Assert.AreEqual((5, 0.50m, 0.80m), (defaults.MinPeople, defaults.MaxShare, defaults.MaxTopTwoShare), "★ the defaults");

            MfGateResult six = MfLabourPooling.Gate(SixPeople(), defaults);
            Assert.AreEqual((6, 34m, true, false, true), (six.People, six.Hours, six.EnoughPeople, six.Dominated, six.Passed));
            Assert.AreEqual(8m / 34m, six.LargestShare);
            Assert.AreEqual(14m / 34m, six.TopTwoShare, "8 + 6 of 34");

            MfGateResult five = MfLabourPooling.Gate(SixPeople().Take(5), defaults);
            Assert.AreEqual((5, true, true), (five.People, five.EnoughPeople, five.Passed), "exactly the minimum is enough");
            MfGateResult four = MfLabourPooling.Gate(SixPeople().Take(4), defaults);
            Assert.AreEqual((4, false, false), (four.People, four.EnoughPeople, four.Passed), "four people are too few: the old minimum of three is gone");

            // a person with no hours is not a person of the pool; one person on several bookings counts once and his hours add up
            var items = SixPeople().Take(4).Concat(new[] { Item(7, 7, 0m, 0m, 0m), Item(8, 1, 2m, 0m, 0m) }).ToArray();
            Assert.AreEqual(4, MfLabourPooling.People(items));
            Assert.AreEqual(4, MfLabourPooling.Gate(items, defaults).People);
            Assert.AreEqual(27m, MfLabourPooling.Gate(items, defaults).Hours, "8 + 2 + 6 + 6 + 5 h");

            // the settings can ask for more people, never for fewer than the three of the earlier releases
            Assert.IsTrue(MfLabourPooling.Gate(SixPeople(), new MfLabourGateSettings { MinPeople = 6 }).Passed);
            Assert.IsFalse(MfLabourPooling.Gate(SixPeople(), new MfLabourGateSettings { MinPeople = 7 }).Passed);
        }

        [TestMethod]
        public void The_Gate_Refuses_A_Pool_Where_One_Person_Has_Over_Half_Or_Two_People_Over_Four_Fifths_Of_The_Hours()
        {
            var settings = new MfLabourGateSettings();

            // 17 of 32 h: over half
            MfGateResult one = MfLabourPooling.Gate(People(17m, 3m, 3m, 3m, 3m, 3m), settings);
            Assert.IsTrue(one.Dominated);
            Assert.IsTrue(one.EnoughPeople);
            Assert.IsFalse(one.Passed);

            // exactly half (16 of 32 h) is allowed; the two largest are 20 of 32 = 0.625
            MfGateResult half = MfLabourPooling.Gate(People(16m, 4m, 3m, 3m, 3m, 3m), settings);
            Assert.AreEqual(0.5m, half.LargestShare);
            Assert.AreEqual(0.625m, half.TopTwoShare);
            Assert.IsFalse(half.Dominated, "the boundary is exact: at most half");
            Assert.IsTrue(half.Passed);

            // 13 + 13 of 32 h = 0.8125: two people over four fifths (nobody over half)
            MfGateResult two = MfLabourPooling.Gate(People(13m, 13m, 2m, 2m, 1m, 1m), settings);
            Assert.IsTrue(two.LargestShare < 0.5m);
            Assert.IsTrue(two.Dominated);
            Assert.IsFalse(two.Passed);

            // exactly four fifths (8 + 8 of 20 h) is allowed
            MfGateResult fifths = MfLabourPooling.Gate(People(8m, 8m, 2m, 1m, 1m), settings);
            Assert.AreEqual(0.8m, fifths.TopTwoShare);
            Assert.IsFalse(fifths.Dominated);
            Assert.IsTrue(fifths.Passed);

            // 8.5 + 8.5 of 20 h = 0.85
            Assert.IsTrue(MfLabourPooling.Gate(People(8.5m, 8.5m, 1m, 1m, 1m), settings).Dominated);

            // one person on two bookings is one person: 10 + 7 h of 32 is 17 h
            var split = new[] { Item(1, 1, 10m, 0m, 0m), Item(2, 1, 7m, 0m, 0m), Item(3, 2, 3m, 0m, 0m), Item(4, 3, 3m, 0m, 0m), Item(5, 4, 3m, 0m, 0m), Item(6, 5, 3m, 0m, 0m), Item(7, 6, 3m, 0m, 0m) };
            Assert.AreEqual(6, MfLabourPooling.People(split));
            Assert.IsTrue(MfLabourPooling.Gate(split, settings).Dominated, "the hours of one person add up before the share is worked out");

            // the shares are settings too
            Assert.IsTrue(MfLabourPooling.Gate(People(17m, 3m, 3m, 3m, 3m, 3m), new MfLabourGateSettings { MaxShare = 0.60m, MaxTopTwoShare = 0.90m }).Passed);
            Assert.IsFalse(MfLabourPooling.Gate(People(8m, 6m, 6m, 5m, 5m, 4m), new MfLabourGateSettings { MaxShare = 0.20m }).Passed);
        }

        [TestMethod]
        public void The_Gate_Settings_Are_Read_From_The_Configuration_With_A_Floor_And_Defaults_For_Unusable_Values()
        {
            MfLabourGateSettings Read(params (string Key, string Value)[] values) =>
                MfLabourGateSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build());

            Assert.AreEqual("Production:PoolMinPeople", MfLabourGateSettings.MinPeopleKey);
            Assert.AreEqual("Production:PoolMaxShare", MfLabourGateSettings.MaxShareKey);
            Assert.AreEqual("Production:PoolMaxTopTwoShare", MfLabourGateSettings.MaxTopTwoShareKey);

            var none = MfLabourGateSettings.Read(null);
            Assert.AreEqual((5, 0.50m, 0.80m), (none.MinPeople, none.MaxShare, none.MaxTopTwoShare));
            var empty = Read();
            Assert.AreEqual((5, 0.50m, 0.80m), (empty.MinPeople, empty.MaxShare, empty.MaxTopTwoShare));

            var set = Read(("Production:PoolMinPeople", "7"), ("Production:PoolMaxShare", "0.4"), ("Production:PoolMaxTopTwoShare", "0.7"));
            Assert.AreEqual((7, 0.4m, 0.7m), (set.MinPeople, set.MaxShare, set.MaxTopTwoShare));
            Assert.AreEqual(3, Read(("Production:PoolMinPeople", "2")).MinPeople, "never below the three people of the earlier releases");
            Assert.AreEqual(3, Read(("Production:PoolMinPeople", "3")).MinPeople);

            var bad = Read(("Production:PoolMinPeople", "abc"), ("Production:PoolMaxShare", "1.5"), ("Production:PoolMaxTopTwoShare", "0"));
            Assert.AreEqual((5, 0.50m, 0.80m), (bad.MinPeople, bad.MaxShare, bad.MaxTopTwoShare), "unusable values are the defaults");
            Assert.AreEqual(5, Read(("Production:PoolMinPeople", "0")).MinPeople);
            Assert.AreEqual(5, Read(("Production:PoolMinPeople", "-4")).MinPeople);
            Assert.AreEqual(1m, Read(("Production:PoolMaxShare", "1")).MaxShare, "a share of one (no limit) is allowed");
        }

        [TestMethod]
        public void A_Month_That_Passes_The_Gate_Is_One_Pool_With_The_Variance_Of_Cost_Less_Absorbed()
        {
            MfPoolPlan plan = MfLabourPooling.Plan(SixPeople(), 140505, new MfLabourGateSettings());
            Assert.AreEqual(0, plan.CarriedItemIds.Count);
            Assert.IsTrue(plan.Gate.Passed);
            MfPoolResult pool = plan.Pool;
            Assert.AreEqual(MfLabourPooling.CompanyKey, pool.Key);
            Assert.AreEqual("*", pool.Key, "one pool a module and month");
            Assert.AreEqual((6, 34m, 3570000m, 3400000m, 170000m), (pool.People, pool.Hours, pool.Cost, pool.Absorbed, pool.Variance));
            Assert.AreEqual(105000m, pool.Rate);
            Assert.IsFalse(pool.Protected);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, pool.ItemIds);
        }

        [TestMethod]
        public void A_Month_That_Fails_The_Gate_Is_Carried_And_At_The_Quarter_End_Posted_Flagged_Without_A_Rate()
        {
            var settings = new MfLabourGateSettings();
            MfPoolItem[] four = SixPeople().Take(4).ToArray();

            MfPoolPlan carried = MfLabourPooling.Plan(four, 140505, settings);
            Assert.IsNull(carried.Pool, "Mordad is not a quarter end: nothing is published");
            Assert.IsFalse(carried.Gate.Passed);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, carried.CarriedItemIds);

            MfPoolPlan capped = MfLabourPooling.Plan(four, 140506, settings);
            Assert.AreEqual(0, capped.CarriedItemIds.Count);
            MfPoolResult line = capped.Pool;
            Assert.IsNotNull(line, "Shahrivar ends the quarter: ONE company-wide line");
            Assert.AreEqual(MfLabourPooling.CompanyKey, line.Key);
            Assert.IsTrue(line.Protected);
            Assert.IsNull(line.Rate, "a protected pool has no rate");
            Assert.AreEqual((4, 25m, 2600000m, 2500000m, 100000m), (line.People, line.Hours, line.Cost, line.Absorbed, line.Variance), "800,000 + 660,000 + 540,000 + 600,000 less 2,500,000");

            // a dominated pool fails the same way
            MfPoolItem[] dominated = { Item(1, 1, 17m, 1700000m, 1700000m), Item(2, 2, 3m, 300000m, 300000m), Item(3, 3, 3m, 300000m, 300000m), Item(4, 4, 3m, 300000m, 300000m), Item(5, 5, 3m, 330000m, 300000m), Item(6, 6, 3m, 300000m, 300000m) };
            Assert.IsNull(MfLabourPooling.Plan(dominated, 140507, settings).Pool, "Mehr");
            Assert.AreEqual(6, MfLabourPooling.Plan(dominated, 140507, settings).CarriedItemIds.Count);
            MfPoolPlan flagged = MfLabourPooling.Plan(dominated, 140509, settings);
            Assert.IsTrue(flagged.Pool.Protected, "Azar");
            Assert.AreEqual(30000m, flagged.Pool.Variance);

            foreach (int month in new[] { 3, 6, 9, 12 }) Assert.IsTrue(MfLabourPooling.IsQuarterEnd(140500 + month), "month " + month);
            foreach (int month in new[] { 1, 2, 4, 5, 7, 8, 10, 11 }) Assert.IsFalse(MfLabourPooling.IsQuarterEnd(140500 + month), "month " + month);
            MfPoolPlan empty = MfLabourPooling.Plan(new MfPoolItem[0], 140506, settings);
            Assert.IsNull(empty.Pool, "nothing booked, nothing posted");
            Assert.IsNull(empty.Gate);
            Assert.AreEqual(0, empty.CarriedItemIds.Count);
        }

        [TestMethod]
        public void The_Pool_Cost_Is_Rounded_Once()
        {
            // 100.4 + 200.4 + 300.4 = 601.2 -> 601 once (not 100 + 200 + 300); 4.5 hours: rate 601.2 / 4.5 = 133.6; absorbed 600 -> variance 1
            var items = new[] { Item(1, 1, 1.5m, 100.4m, 200m), Item(2, 2, 1.5m, 200.4m, 200m), Item(3, 3, 1.5m, 300.4m, 200m) };
            MfPoolPlan plan = MfLabourPooling.Plan(items, 140505, new MfLabourGateSettings { MinPeople = 3 });
            Assert.IsTrue(plan.Gate.Passed);
            Assert.AreEqual((601m, 133.6m, 1m), (plan.Pool.Cost, plan.Pool.Rate, plan.Pool.Variance));
        }

        [TestMethod]
        public void The_Service_Department_Pools_The_Engineers_Of_Several_Orders_And_An_Engineer_Counts_Once()
        {
            // order 1: engineers 7 (4 h) and 8 (3 h); order 2: engineer 7 (2 h) again; orders 3 and 4: engineers 9, 10, 11 (2 h each)
            MfPoolItem Order(int id, decimal cost, decimal absorbed, params (int Engineer, decimal Hours)[] who)
            {
                var item = new MfPoolItem { Id = id, Cost = cost, Absorbed = absorbed };
                foreach (var w in who) item.PersonHours[w.Engineer] = item.PersonHours.GetValueOrDefault(w.Engineer) + w.Hours;
                return item;
            }
            var orders = new[]
            {
                Order(1, 700000m, 630000m, (7, 4m), (8, 3m)), Order(2, 200000m, 180000m, (7, 2m)),
                Order(3, 220000m, 180000m, (9, 2m), (10, 2m)), Order(4, 120000m, 90000m, (11, 1m)),
            };
            Assert.AreEqual(5, MfLabourPooling.People(orders));
            Assert.AreEqual(14m, orders.Sum(o => o.Hours), "7 + 2 + 4 + 1 h");
            MfPoolPlan plan = MfLabourPooling.Plan(orders, 140508, new MfLabourGateSettings());
            Assert.IsTrue(plan.Gate.Passed, "engineer 7 has 6 of 14 hours (0.43), the two largest 9 of 14 (0.64)");
            Assert.AreEqual((5, 14m, 1240000m, 1080000m, 160000m), (plan.Pool.People, plan.Pool.Hours, plan.Pool.Cost, plan.Pool.Absorbed, plan.Pool.Variance));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, plan.Pool.ItemIds);

            // the same engineers on fewer orders are still four people: carried, then flagged at the end of the quarter (Azar)
            MfPoolItem[] few = orders.Take(3).ToArray();
            Assert.AreEqual(4, MfLabourPooling.People(few));
            Assert.IsNull(MfLabourPooling.Plan(few, 140508, new MfLabourGateSettings()).Pool);
            Assert.IsTrue(MfLabourPooling.Plan(few, 140509, new MfLabourGateSettings()).Pool.Protected);
        }

        // ------------------------------------------------------------------------------------------ the standard rate of the days

        [TestMethod]
        public void The_Standard_Rate_Is_The_Work_Centre_Rate_Else_The_Setting_And_Never_Zero()
        {
            Assert.AreEqual(150000m, MfLabourRates.Standard(150000m, 100000m), "the rate finance set for the work centre wins");
            Assert.AreEqual(100000m, MfLabourRates.Standard(null, 100000m), "a centre without a rate takes the setting");
            Assert.AreEqual(100000m, MfLabourRates.Standard(0m, 100000m), "zero is not a rate");
            Assert.AreEqual(100000m, MfLabourRates.Standard(-5m, 100000m));
            Assert.AreEqual(150000m, MfLabourRates.Standard(150000m, null));
            Assert.IsNull(MfLabourRates.Standard(null, null), "neither: the run refuses and names the centre");
            Assert.IsNull(MfLabourRates.Standard(0m, 0m));
            Assert.IsNull(MfLabourRates.Standard(null, 0m));

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
            // production, under-absorbed by 170,000 (the month above): the variance account is debited and the absorbed-labour account credited; company level, so no cost centre either
            List<AccountingEventLine> under = MfLabourVariance.Lines(170000m, AccountRoles.LabourAbsorbed, null, false, "تفاوت");
            Assert.AreEqual(2, under.Count);
            Assert.AreEqual(AccountRoles.LabourRateVariance, under[0].AccountRole);
            Assert.AreEqual(170000m, under[0].Debit);
            Assert.AreEqual(0m, under[0].Credit);
            Assert.AreEqual(AccountRoles.LabourAbsorbed, under[1].AccountRole);
            Assert.AreEqual(0m, under[1].Debit);
            Assert.AreEqual(170000m, under[1].Credit);
            Assert.IsTrue(under.All(l => l.CostCenter == null && !l.LabourProtected && l.WorkOrderId == null && l.ContractId == null));
            Assert.AreEqual(under.Sum(l => l.Debit), under.Sum(l => l.Credit));

            // after-sales keeps its own cost centre on the two lines
            List<AccountingEventLine> service = MfLabourVariance.Lines(160000m, AccountRoles.ServiceLabourAbsorbed, "AFTERSALES", false, "تفاوت");
            Assert.AreEqual(AccountRoles.ServiceLabourAbsorbed, service[1].AccountRole);
            Assert.IsTrue(service.All(l => l.CostCenter == "AFTERSALES" && l.WorkOrderId == null && l.ContractId == null));

            // over-absorbed by 90,000 and flagged (the quarter-end line): the other way round
            List<AccountingEventLine> over = MfLabourVariance.Lines(-90000m, AccountRoles.ServiceLabourAbsorbed, null, true, "تفاوت");
            Assert.AreEqual(AccountRoles.LabourRateVariance, over[0].AccountRole);
            Assert.AreEqual(0m, over[0].Debit);
            Assert.AreEqual(90000m, over[0].Credit);
            Assert.AreEqual(AccountRoles.ServiceLabourAbsorbed, over[1].AccountRole);
            Assert.AreEqual(90000m, over[1].Debit);
            Assert.IsTrue(over.All(l => l.CostCenter == null && l.LabourProtected), "both lines of a protected pool carry the flag");
            Assert.AreEqual(over.Sum(l => l.Debit), over.Sum(l => l.Credit));

            Assert.AreEqual(0, MfLabourVariance.Lines(0m, AccountRoles.LabourAbsorbed, null, false, "x").Count, "an exactly absorbed pool posts nothing");
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
        public void The_Visible_Total_Of_A_Voucher_Leaves_Out_The_Flagged_Debits_For_Everybody_Outside_Payroll_Finance_Manager_And_Ceo()
        {
            // the helper the CEO drill of the waiting vouchers calls, like the voucher list does: a voucher's total is the sum of its debits
            var lines = new List<FnVoucherLine>
            {
                new FnVoucherLine { Debit = 245000m }, new FnVoucherLine { Credit = 245000m },
                new FnVoucherLine { Debit = 90000m, LabourProtected = true }, new FnVoucherLine { Credit = 90000m, LabourProtected = true },
            };
            Assert.AreEqual(335000m, FnLabourMask.VisibleTotal(lines, true), "245,000 + 90,000");
            Assert.AreEqual(245000m, FnLabourMask.VisibleTotal(lines, false), "the flagged 90,000 is not in it");
            Assert.AreEqual(90000m, FnLabourMask.HiddenDebit(lines));
            Assert.AreEqual(0m, FnLabourMask.VisibleTotal(null, false));
            Assert.AreEqual(0m, FnLabourMask.VisibleTotal(new List<FnVoucherLine>(), true));
            Assert.AreEqual(0m, FnLabourMask.HiddenDebit(null));
            // a voucher total (the sum of all debits) less the hidden debits is what the voucher list shows
            Assert.AreEqual(FnLabourMask.VisibleTotal(lines, false), lines.Sum(l => l.Debit) - FnLabourMask.HiddenDebit(lines));
            StringAssert.Contains(FnLabourMask.Refusal("دوره‌ی 1405/06"), "دوره‌ی 1405/06");
            StringAssert.Contains(FnLabourMask.Refusal("x"), "فقط برای حقوق و دستمزد، مدیر مالی و مدیرعامل");
        }

        [TestMethod]
        public void A_Row_Of_The_Earlier_Release_Is_Masked_By_Its_Own_Group_Rule_And_A_Row_Of_This_Release_Never_Is()
        {
            // the earlier release's group rule: payroll-rated people behind the amount, fewer than three of them (employees and payroll-rated people both count)
            Assert.IsTrue(MfLegacyLabour.Group(1, 1));
            Assert.IsTrue(MfLegacyLabour.Group(2, 2));
            Assert.IsTrue(MfLegacyLabour.Group(5, 2), "five people, two of them with a payslip");
            Assert.IsTrue(MfLegacyLabour.Group(2, 5));
            Assert.IsFalse(MfLegacyLabour.Group(3, 3));
            Assert.IsFalse(MfLegacyLabour.Group(4, 0), "nobody with a payslip: there is no pay to show");
            Assert.IsFalse(MfLegacyLabour.Group(0, 0));

            // a run: only a run flagged by the earlier release can be protected, and the postings of it by their own group
            var earlier = new MfCostRun { LabourProtected = true };
            var current = new MfCostRun { LabourProtected = false };
            Assert.IsTrue(MfLegacyLabour.IsProtected(earlier));
            Assert.IsFalse(MfLegacyLabour.IsProtected(current));
            Assert.IsFalse(MfLegacyLabour.IsProtected((MfCostRun)null));
            var small = new MfCostPosting { EmployeeCount = 2, PayrollEmployeeCount = 1 };
            var big = new MfCostPosting { EmployeeCount = 4, PayrollEmployeeCount = 4 };
            Assert.IsTrue(MfLegacyLabour.IsProtected(earlier, small));
            Assert.IsFalse(MfLegacyLabour.IsProtected(earlier, big));
            Assert.IsFalse(MfLegacyLabour.IsProtected(current, small), "a posting of this release with one payroll-rated person was absorbed at the standard rate: it shows nothing of any pay");
            Assert.IsFalse(MfLegacyLabour.IsProtected(earlier, null));

            // what the reader gets
            Assert.IsNull(MfLegacyLabour.Labour(500000m, true, false));
            Assert.AreEqual(500000m, MfLegacyLabour.Labour(500000m, true, true));
            Assert.AreEqual(500000m, MfLegacyLabour.Labour(500000m, false, false));
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
