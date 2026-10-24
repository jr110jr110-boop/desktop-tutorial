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
            Assert.AreEqual(8m, MfKpi.DowntimeHours(c, new DateTime(2026, 8, 7, 9, 0, 0), null, from, to)); // still down: to the end of the period
            Assert.AreEqual(40m, MfKpi.PlannedHours(c, from, to));
        }

        [TestMethod]
        public void Oee_Of_A_Small_Case_Is_Availability_Times_Performance_Times_Quality()
        {
            OeeDto o = MfKpi.Compute(new OeeDto { PlannedHours = 40, DowntimeHours = 4, BookedRunHours = 10, StandardHours = 7, GoodQty = 6, ScrapQty = 1 });
            Assert.AreEqual(90.0m, o.AvailabilityPercent);
            Assert.AreEqual(70.0m, o.PerformancePercent);
            Assert.AreEqual(85.7m, o.QualityPercent);
            Assert.AreEqual(54.0m, o.OeePercent);
        }

        [TestMethod]
        public void Oee_Is_Empty_Without_Run_Time_Or_Pieces()
        {
            OeeDto o = MfKpi.Compute(new OeeDto { PlannedHours = 40, DowntimeHours = 0 });
            Assert.AreEqual(100.0m, o.AvailabilityPercent);
            Assert.IsNull(o.PerformancePercent);
            Assert.IsNull(o.QualityPercent);
            Assert.IsNull(o.OeePercent);
        }
    }
}
