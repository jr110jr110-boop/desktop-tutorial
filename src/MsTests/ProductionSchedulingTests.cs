using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Production;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.ProductionDtos;

namespace MsTests
{
    /// <summary>The hand-checked cases of the finite-capacity scheduler and the KPI formulas (the smoke test repeats them through the API).</summary>
    [TestClass]
    public class ProductionSchedulingTests
    {
        // Monday to Friday, one shift of 8 hours, efficiency and utilisation 100 %: 8 hours a day
        private static MfCenter Center() => new MfCenter
        {
            Wc = new MfWorkCenter { Id = 1, Code = "T" }, Calendar = PcWorkCalendar.MondayToFriday(), ShiftHours = 8, Gross = 8, Factor = 1,
        };

        private static readonly DateTime Monday = new DateTime(2026, 8, 3);

        [TestMethod]
        public void Three_Orders_Fill_The_Days_In_Priority_Order()
        {
            MfCenter c = Center();
            var used = new Dictionary<(int, DateTime), decimal>();
            Assert.IsTrue(MfScheduler.Place(c, Monday, 6, used, true, out DateTime s1, out DateTime f1));
            Assert.AreEqual(Monday, s1);
            Assert.AreEqual(Monday, f1);
            Assert.IsTrue(MfScheduler.Place(c, Monday, 5, used, true, out DateTime s2, out DateTime f2));
            Assert.AreEqual(Monday, s2);
            Assert.AreEqual(new DateTime(2026, 8, 4), f2);
            Assert.IsTrue(MfScheduler.Place(c, Monday, 51, used, true, out DateTime s3, out DateTime f3));
            Assert.AreEqual(new DateTime(2026, 8, 4), s3);
            Assert.AreEqual(new DateTime(2026, 8, 12), f3);
            Assert.AreEqual(8m, used[(1, Monday)]);
            Assert.AreEqual(6m, used[(1, new DateTime(2026, 8, 12))]);
        }

        [TestMethod]
        public void Without_The_Capacity_Limit_Orders_Stack_On_The_Same_Day()
        {
            MfCenter c = Center();
            var used = new Dictionary<(int, DateTime), decimal>();
            MfScheduler.Place(c, Monday, 6, used, false, out _, out _);
            MfScheduler.Place(c, Monday, 5, used, false, out _, out _);
            MfScheduler.Place(c, Monday, 51, used, false, out _, out DateTime finish);
            Assert.AreEqual(19m, used[(1, Monday)]);
            Assert.AreEqual(new DateTime(2026, 8, 11), finish);
            Assert.AreEqual(3m, used[(1, new DateTime(2026, 8, 11))]);
        }

        [TestMethod]
        public void Maintenance_Downtime_Takes_Hours_From_The_Day()
        {
            MfCenter c = Center();
            c.Down[new DateTime(2026, 8, 5)] = 4;
            Assert.AreEqual(4m, c.Capacity(new DateTime(2026, 8, 5)));
            Assert.AreEqual(0m, c.Capacity(new DateTime(2026, 8, 8))); // Saturday
            var used = new Dictionary<(int, DateTime), decimal>();
            MfScheduler.Place(c, Monday, 6, used, true, out _, out _);
            MfScheduler.Place(c, Monday, 5, used, true, out _, out _);
            Assert.IsTrue(MfScheduler.Place(c, Monday, 51, used, true, out _, out DateTime f3));
            Assert.AreEqual(new DateTime(2026, 8, 13), f3);
        }

        [TestMethod]
        public void A_Day_Without_Any_Capacity_Is_Skipped_And_No_Capacity_At_All_Fails()
        {
            MfCenter c = Center();
            c.Down[new DateTime(2026, 8, 5)] = 8;
            var used = new Dictionary<(int, DateTime), decimal>();
            Assert.IsTrue(MfScheduler.Place(c, new DateTime(2026, 8, 5), 3, used, true, out DateTime start, out _));
            Assert.AreEqual(new DateTime(2026, 8, 6), start);
            MfCenter none = Center();
            none.Gross = 0;
            Assert.IsFalse(MfScheduler.Place(none, Monday, 1, used, true, out _, out _));
        }

        [TestMethod]
        public void Zero_Hours_Take_No_Capacity()
        {
            MfCenter c = Center();
            var used = new Dictionary<(int, DateTime), decimal>();
            Assert.IsTrue(MfScheduler.Place(c, new DateTime(2026, 8, 8), 0, used, true, out DateTime s, out DateTime f));
            Assert.AreEqual(new DateTime(2026, 8, 8), s);
            Assert.AreEqual(s, f);
            Assert.AreEqual(0, used.Count);
        }

        [TestMethod]
        public void Weeks_Start_On_Saturday_And_Dates_Are_Persian()
        {
            Assert.AreEqual(new DateTime(2026, 8, 1), MfScheduler.WeekStart(Monday));
            Assert.AreEqual(new DateTime(2026, 8, 1), MfScheduler.WeekStart(new DateTime(2026, 8, 1)));
            Assert.AreEqual(new DateTime(2026, 8, 1), MfScheduler.WeekStart(new DateTime(2026, 8, 7)));
            Assert.AreEqual(new DateTime(2026, 8, 8), MfScheduler.WeekStart(new DateTime(2026, 8, 8)));
            Assert.AreEqual("1405/05/21", MfText.Date(new DateTime(2026, 8, 12)));
            Assert.AreEqual("1405/05/12", MfText.Date(new DateTime(2026, 8, 3)));
        }

        [TestMethod]
        public void A_Machine_Stands_For_Its_Share_Of_The_Parallel_Resources()
        {
            Assert.AreEqual(1m, MfScheduler.MachineShare(1, 1));
            Assert.AreEqual(1m, MfScheduler.MachineShare(2, 2));
            Assert.AreEqual(0.5m, MfScheduler.MachineShare(1, 2));
            Assert.AreEqual(1m, MfScheduler.MachineShare(3, 2));
            Assert.AreEqual(1m, MfScheduler.MachineShare(1, 0));
        }

        [TestMethod]
        public void Downtime_Hours_Count_Only_Working_Days_And_At_Most_One_Shift_A_Day()
        {
            MfCenter c = Center();
            DateTime from = Monday, to = new DateTime(2026, 8, 7);
            Assert.AreEqual(4m, MfKpi.DowntimeHours(c, new DateTime(2026, 8, 5, 10, 0, 0), new DateTime(2026, 8, 5, 14, 0, 0), from, to));
            Assert.AreEqual(8m, MfKpi.DowntimeHours(c, new DateTime(2026, 8, 5, 8, 0, 0), new DateTime(2026, 8, 5, 20, 0, 0), from, to));
            Assert.AreEqual(16m, MfKpi.DowntimeHours(c, new DateTime(2026, 8, 5, 8, 0, 0), new DateTime(2026, 8, 6, 20, 0, 0), from, to));
            Assert.AreEqual(0m, MfKpi.DowntimeHours(c, new DateTime(2026, 8, 8, 8, 0, 0), new DateTime(2026, 8, 9, 20, 0, 0), from, new DateTime(2026, 8, 9))); // the weekend
            Assert.AreEqual(7m, MfKpi.DowntimeHours(c, new DateTime(2026, 8, 7, 9, 0, 0), null, from, to)); // still down: 09:00 to the end of the shift window (08:00-16:00) of that day
            Assert.AreEqual(40m, MfKpi.PlannedHours(c, from, to));
        }

        [TestMethod]
        public void A_Stop_Counts_Only_Inside_The_Shift_Window_Of_The_Day()
        {
            MfCenter c = Center();
            DateTime from = Monday, to = new DateTime(2026, 8, 7);
            Assert.AreEqual(2m, MfKpi.DowntimeHours(c, new DateTime(2026, 8, 5, 6, 0, 0), new DateTime(2026, 8, 5, 10, 0, 0), from, to)); // 06:00-08:00 is outside the shift
            Assert.AreEqual(0m, MfKpi.DowntimeHours(c, new DateTime(2026, 8, 5, 16, 0, 0), new DateTime(2026, 8, 5, 23, 0, 0), from, to)); // after the shift
            Assert.AreEqual(2m, MfKpi.DowntimeHours(c, new DateTime(2026, 8, 5, 15, 0, 0), new DateTime(2026, 8, 6, 9, 0, 0), from, to)); // 1 h on Wednesday, 1 h on Thursday
            Assert.AreEqual(8m, MfKpi.DowntimeHours(c, new DateTime(2026, 8, 5, 0, 0, 0), new DateTime(2026, 8, 6, 0, 0, 0), from, to)); // a whole calendar day is at most the 8 shift hours
            (DateTime a, DateTime b) = c.ShiftWindow(new DateTime(2026, 8, 5, 13, 0, 0));
            Assert.AreEqual(new DateTime(2026, 8, 5, 8, 0, 0), a);
            Assert.AreEqual(new DateTime(2026, 8, 5, 16, 0, 0), b);
            c.ShiftHours = 24; // three shifts: the whole day
            Assert.AreEqual((new DateTime(2026, 8, 5), new DateTime(2026, 8, 6)), c.ShiftWindow(new DateTime(2026, 8, 5)));
            c.ShiftHours = 16; // two shifts: 08:00 to midnight
            Assert.AreEqual((new DateTime(2026, 8, 5, 8, 0, 0), new DateTime(2026, 8, 6)), c.ShiftWindow(new DateTime(2026, 8, 5)));
            Assert.AreEqual(4m, c.ShiftOverlapHours(new DateTime(2026, 8, 5), new DateTime(2026, 8, 5, 20, 0, 0), new DateTime(2026, 8, 6, 3, 0, 0)));
        }

        // one 8 hour shift, two parallel resources, efficiency 100 %, utilisation 85 %: 16 x 0.85 = 13.6 hours a day
        private static MfCenter Parallel() => new MfCenter
        {
            Wc = new MfWorkCenter { Id = 2, Code = "P" }, Calendar = PcWorkCalendar.MondayToFriday(), ShiftHours = 8, Gross = 16, Factor = 0.85m,
        };

        [TestMethod]
        public void Capacity_Is_Shifts_Hours_Resources_Efficiency_Utilisation_Minus_Downtime_Hours()
        {
            MfCenter c = Parallel();
            Assert.AreEqual(13.6m, c.Capacity(Monday));
            Assert.AreEqual(13.6m, c.FullCapacity(Monday));
            c.Down[Monday] = 4; // clock hours of the stop, not scaled by the utilisation
            Assert.AreEqual(9.6m, c.Capacity(Monday));
            Assert.AreEqual(13.6m, c.FullCapacity(Monday));
            c.Down[Monday] = 16;
            Assert.AreEqual(0m, c.Capacity(Monday));
            Assert.AreEqual(0m, c.FullCapacity(new DateTime(2026, 8, 8))); // Saturday
        }

        [TestMethod]
        public void An_Order_Of_34_Hours_On_The_85_Percent_Centre_Takes_Three_Days()
        {
            MfCenter c = Parallel();
            var used = new Dictionary<(int, DateTime), decimal>();
            Assert.IsTrue(MfScheduler.Place(c, Monday, 34, used, true, out DateTime s, out DateTime f));
            Assert.AreEqual(Monday, s);
            Assert.AreEqual(new DateTime(2026, 8, 5), f);
            Assert.AreEqual(13.6m, used[(2, Monday)]);
            Assert.AreEqual(13.6m, used[(2, new DateTime(2026, 8, 4))]);
            Assert.AreEqual(6.8m, used[(2, new DateTime(2026, 8, 5))]);
        }

        [TestMethod]
        public void The_Demand_Of_A_Centre_That_Is_Down_Is_Still_Placed_At_The_Full_Capacity()
        {
            MfCenter c = Center();
            c.Down[new DateTime(2026, 8, 5)] = 8; // the machine is down all Wednesday: no capacity, but the work is still waiting for it
            var demand = new Dictionary<(int, DateTime), decimal>();
            Assert.IsTrue(MfScheduler.Place(c, Monday, 30, demand, false, out _, out DateTime finish));
            Assert.AreEqual(new DateTime(2026, 8, 6), finish); // 8 + 8 + 8 + 6: Monday, Tuesday, Wednesday, Thursday
            Assert.AreEqual(8m, demand[(1, new DateTime(2026, 8, 5))]);
            Assert.AreEqual(6m, demand[(1, new DateTime(2026, 8, 6))]);
            var finite = new Dictionary<(int, DateTime), decimal>();
            Assert.IsTrue(MfScheduler.Place(c, Monday, 30, finite, true, out _, out DateTime finiteFinish));
            Assert.AreEqual(new DateTime(2026, 8, 7), finiteFinish); // 8 + 8 + (Wednesday is down) 8 + 6 on Friday
            Assert.IsFalse(finite.ContainsKey((1, new DateTime(2026, 8, 5))));
        }

        [TestMethod]
        public void Oee_Of_A_Small_Case_Is_Availability_Times_Performance_Times_Quality()
        {
            // R1p D29 (ISO 22400-2): the availability is the run time over the planned busy time (10 / 40 = 25.0 %), no longer (planned - downtime) / planned (36 / 40 = 90 %).
            // The OEE is availability x performance x quality = 25.0 % x 70.0 % x 85.7 % = 15.0 %; the figure of the earlier releases (90 % x 70 % x 85.7 % = 54.0 %) stays one release as OeeLegacy.
            OeeDto o = MfKpi.Compute(new OeeDto { PlannedHours = 40, DowntimeHours = 4, BookedRunHours = 10, StandardHours = 7, GoodQty = 6, ScrapQty = 1 });
            Assert.AreEqual(25.0m, o.AvailabilityPercent);
            Assert.AreEqual(70.0m, o.PerformancePercent);
            Assert.AreEqual(85.7m, o.QualityPercent);
            Assert.AreEqual(15.0m, o.OeePercent);
            Assert.AreEqual(54.0m, o.OeeLegacy);
        }

        [TestMethod]
        public void Oee_Is_Empty_Without_Run_Time_Or_Pieces()
        {
            // R1p D29: a centre that was planned for 40 hours and ran none has availability 0 % (before ISO 22400-2 it showed 100 % because no downtime was recorded)
            OeeDto o = MfKpi.Compute(new OeeDto { PlannedHours = 40, DowntimeHours = 0 });
            Assert.AreEqual(0.0m, o.AvailabilityPercent);
            Assert.IsNull(o.PerformancePercent);
            Assert.IsNull(o.QualityPercent);
            Assert.IsNull(o.OeePercent);
            Assert.IsNull(o.OeeLegacy);
        }
    }
}
