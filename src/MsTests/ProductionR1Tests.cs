using System;
using System.Collections.Generic;
using System.Linq;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.AfterSales;
using Services.NasbNiro.Production;

namespace MsTests
{
    /// <summary>
    /// The pure rules of release R1p (production, maintenance and after-sales audit findings): the quantity chain of the operations (D7), the OEE after ISO 22400-2 on MACHINE time (D29), MTTR on clock hours (D37),
    /// the corrections that wait for a second person, the frozen quantities and the one rework per NCR (D38, D39, D43, D6, D8), the scrap serials, the SLA clock (D24), the survey rules (D25), the warranty rule (D26),
    /// the field MTBF hours (D36) and the model of the new columns. The pooled labour rate (D1, D2) is tested in <c>ProductionCostTests</c>. Every figure is worked out by hand in the test.
    /// </summary>
    [TestClass]
    public class ProductionR1Tests
    {
        // ------------------------------------------------------------------------------------------ D7: the quantity an operation has to report

        private static MfOperation Op(int id, int sequence, decimal good, MfOperationStatus status = MfOperationStatus.Done, bool rework = false, int? reworkOf = null, decimal reworkQty = 0) =>
            new MfOperation { Id = id, OrderId = 1, Sequence = sequence, GoodQty = good, Status = status, IsRework = rework, ReworkOfOperationId = reworkOf, ReworkQty = reworkQty };

        [TestMethod]
        public void The_First_Operation_Expects_The_Order_Quantity_And_Every_Later_One_The_Good_Quantity_Of_The_One_Before()
        {
            // order of 10: step 10 made 8 good (2 scrapped), step 20 made 7 good, step 30 is still to be done
            var ops = new List<MfOperation> { Op(1, 10, 8), Op(2, 20, 7), Op(3, 30, 0, MfOperationStatus.Ready) };
            Assert.AreEqual(10m, MfQuantityRules.Expected(ops, ops[0], 10m));
            Assert.AreEqual(8m, MfQuantityRules.Expected(ops, ops[1], 10m));
            Assert.AreEqual(7m, MfQuantityRules.Expected(ops, ops[2], 10m));
            // the list may come in any order: the chain order (sequence) is applied
            Assert.AreEqual(7m, MfQuantityRules.Expected(new[] { ops[2], ops[0], ops[1] }, ops[2], 10m));
        }

        [TestMethod]
        public void A_Skipped_Step_Passes_The_Pieces_On_Untouched()
        {
            var ops = new List<MfOperation> { Op(1, 10, 8), Op(2, 20, 0, MfOperationStatus.Skipped), Op(3, 30, 0, MfOperationStatus.Ready) };
            Assert.AreEqual(8m, MfQuantityRules.Expected(ops, ops[2], 10m));
        }

        [TestMethod]
        public void A_Rework_Operation_Does_The_Rejected_Quantity_And_The_Step_After_It_Follows_The_Rework_Result()
        {
            // step 20 was rejected with 3 of its 5 pieces to rework; the rework step made 3 good; step 30 then has 3 pieces in front of it... plus nothing else, because 20 reported 5 good of which 3 came back
            var ops = new List<MfOperation>
            {
                Op(1, 10, 5),
                Op(2, 20, 5, MfOperationStatus.Rejected, reworkQty: 3),
                Op(3, 20, 3, MfOperationStatus.Done, rework: true, reworkOf: 2),
                Op(4, 30, 0, MfOperationStatus.Ready),
            };
            Assert.AreEqual(3m, MfQuantityRules.Expected(ops, ops[2], 5m), "the rework step does the 3 rejected pieces");
            Assert.AreEqual(3m, MfQuantityRules.Expected(ops, ops[3], 5m), "step 30 follows the rework step, whose good quantity is 3");
            // a rework step whose origin recorded no rework quantity does what the origin had in front of it
            var plain = new List<MfOperation> { Op(1, 10, 5), Op(2, 20, 5, MfOperationStatus.Rejected), Op(3, 20, 0, MfOperationStatus.Ready, rework: true, reworkOf: 2) };
            Assert.AreEqual(5m, MfQuantityRules.Expected(plain, plain[2], 5m));
        }

        [TestMethod]
        public void A_Written_Off_Quantity_Reduces_The_Quantity_The_Order_Makes_And_Never_Goes_Below_Zero()
        {
            Assert.AreEqual(85m, MfQuantityRules.EffectiveOrderQuantity(100m, 15m));
            Assert.AreEqual(100m, MfQuantityRules.EffectiveOrderQuantity(100m, 0m));
            Assert.AreEqual(0m, MfQuantityRules.EffectiveOrderQuantity(5m, 9m));
        }

        // ------------------------------------------------------------------------------------------ D29: OEE after ISO 22400-2

        [TestMethod]
        public void Oee_Of_A_Centre_That_Ran_40_Of_176_Planned_Hours_At_The_Standard_Is_22_7_Percent()
        {
            // the example of the reviewer: 22 working days x 8 h = 176 planned hours, 40 hours run at the standard speed with only good pieces
            MfOeeMath.Result r = MfOeeMath.Compute(new MfOeeMath.Input { PlannedHours = 176m, RunHours = 40m, StandardHours = 40m, GoodQty = 40m });
            Assert.AreEqual(176m, r.PlannedBusyHours);
            Assert.AreEqual(22.7m, r.AvailabilityPercent, "40 / 176");
            Assert.AreEqual(100m, r.PerformancePercent);
            Assert.AreEqual(100m, r.QualityPercent);
            Assert.AreEqual(22.7m, r.OeePercent);
            Assert.AreEqual(136m, r.IdleHours, "176 - 40 run: nobody booked the rest");
            // the figure of the earlier releases called the same centre 100 % available (no downtime) and so 100 % OEE
            Assert.AreEqual(100m, MfOeeMath.Legacy(176m, 0m, 40m, 40m, 40m, 0m));
        }

        [TestMethod]
        public void Oee_Of_The_Smoke_Week_Is_Worked_Out_By_Hand()
        {
            // CNC centre 08-03 to 08-07: planned 40 h, breakdown 4 h, run 10 h, 7 standard hours for 7 produced pieces (6 good, 1 scrap)
            // availability 10 / 40 = 25.0, performance 7 / 10 = 70.0, quality 6 / 7 = 85.7, OEE = 7 x 6 / (7 x 40) = 15.0; idle = 40 - 4 - 10 = 26
            MfOeeMath.Result r = MfOeeMath.Compute(new MfOeeMath.Input { PlannedHours = 40m, BreakdownHours = 4m, RunHours = 10m, StandardHours = 7m, GoodQty = 6m, ScrapQty = 1m });
            Assert.AreEqual(40m, r.PlannedBusyHours);
            Assert.AreEqual(25m, r.AvailabilityPercent);
            Assert.AreEqual(70m, r.PerformancePercent);
            Assert.AreEqual(85.7m, r.QualityPercent);
            Assert.AreEqual(15m, r.OeePercent);
            Assert.AreEqual(26m, r.IdleHours);
            Assert.AreEqual(54m, MfOeeMath.Legacy(40m, 4m, 10m, 7m, 6m, 1m), "the old figure: 90.0 x 70.0 x 85.7");
        }

        [TestMethod]
        public void Oee_Of_The_Smoke_Month_Counts_Setup_As_A_Loss_And_Rounds_Half_Away_From_Zero()
        {
            // month so far: planned 96 h, breakdown 4 h, run 10 h, setup 2 h (ATT03), 7 standard hours, 6 good, 1 scrap
            // availability 10 / 96 = 10.4, OEE = 6 / 96 = 6.25 -> 6.3, idle = 96 - 4 - 10 - 2 = 80; old figure 95.8 x 70.0 x 85.7 = 57.5
            MfOeeMath.Result r = MfOeeMath.Compute(new MfOeeMath.Input { PlannedHours = 96m, BreakdownHours = 4m, RunHours = 10m, SetupHours = 2m, StandardHours = 7m, GoodQty = 6m, ScrapQty = 1m });
            Assert.AreEqual(10.4m, r.AvailabilityPercent);
            Assert.AreEqual(6.3m, r.OeePercent);
            Assert.AreEqual(80m, r.IdleHours);
            Assert.AreEqual(57.5m, MfOeeMath.Legacy(96m, 4m, 10m, 7m, 6m, 1m));
        }

        [TestMethod]
        public void Planned_Maintenance_Is_Taken_Out_Of_The_Planned_Busy_Time()
        {
            // 176 planned, 16 of them planned maintenance: busy time 160; 40 h run at the standard = 25.0 % (not 22.7 %)
            MfOeeMath.Result r = MfOeeMath.Compute(new MfOeeMath.Input { PlannedHours = 176m, PlannedMaintenanceHours = 16m, RunHours = 40m, StandardHours = 40m, GoodQty = 40m });
            Assert.AreEqual(160m, r.PlannedBusyHours);
            Assert.AreEqual(25m, r.AvailabilityPercent);
            Assert.AreEqual(25m, r.OeePercent);
            Assert.AreEqual(120m, r.IdleHours);
            // all of the planned time is maintenance: nothing left to be busy in, no OEE
            Assert.IsNull(MfOeeMath.Compute(new MfOeeMath.Input { PlannedHours = 16m, PlannedMaintenanceHours = 16m, RunHours = 1m, StandardHours = 1m, GoodQty = 1m }).OeePercent);
        }

        [TestMethod]
        public void Oee_Is_Empty_Without_Run_Time_Or_Pieces_And_Idle_Time_Is_Never_Negative()
        {
            MfOeeMath.Result none = MfOeeMath.Compute(new MfOeeMath.Input { PlannedHours = 40m });
            Assert.AreEqual(0m, none.AvailabilityPercent);
            Assert.IsNull(none.PerformancePercent);
            Assert.IsNull(none.QualityPercent);
            Assert.IsNull(none.OeePercent);
            Assert.AreEqual(40m, none.IdleHours);
            // people book in parallel on one machine, so booked hours can exceed the planned busy time: idle stops at zero
            MfOeeMath.Result over = MfOeeMath.Compute(new MfOeeMath.Input { PlannedHours = 8m, RunHours = 12m, StandardHours = 12m, GoodQty = 12m });
            Assert.AreEqual(0m, over.IdleHours);
            Assert.AreEqual(150m, over.AvailabilityPercent);
            Assert.AreEqual(150m, over.OeePercent, "the product stays right: 12 standard hours earned in 8 planned hours");
        }

        // ------------------------------------------------------------------------------------------ D37: MTTR on clock hours

        [TestMethod]
        public void Mttr_Is_The_Mean_Clock_Time_Of_The_Breakdowns_That_Started_In_The_Period_And_Were_Repaired_By_Its_End()
        {
            var start = new DateTime(2026, 8, 3);
            var end = new DateTime(2026, 8, 10); // exclusive end of the period 08-03 to 08-09
            var repairs = new List<(DateTime Start, DateTime? End)>
            {
                (new DateTime(2026, 8, 4, 15, 0, 0), new DateTime(2026, 8, 5, 9, 0, 0)),   // over the night: 18 clock hours (the shift hours of it would be 2)
                (new DateTime(2026, 8, 6, 10, 0, 0), new DateTime(2026, 8, 6, 14, 0, 0)),  // 4 hours
                (new DateTime(2026, 8, 7, 9, 0, 0), null),                                  // not repaired yet: not in the mean
                (new DateTime(2026, 8, 2, 20, 0, 0), new DateTime(2026, 8, 3, 6, 0, 0)),   // started before the period
                (new DateTime(2026, 8, 9, 20, 0, 0), new DateTime(2026, 8, 10, 6, 0, 0)),  // repaired after the end of the period
            };
            Assert.AreEqual(11m, MfOeeMath.Mttr(repairs, start, end), "(18 + 4) / 2");
            Assert.IsNull(MfOeeMath.Mttr(new List<(DateTime, DateTime?)>(), start, end));
            Assert.AreEqual(4m, MfOeeMath.Mttr(repairs.Skip(1).Take(1), start, end));
        }

        // ------------------------------------------------------------------------------------------ D29: OEE on machine time (review item 11)

        private static DateTime T(int hour, int minute = 0) => new DateTime(2026, 8, 3, hour, minute, 0);

        private static MfMachineTime.Row Row(int? machine, MfBookingKind kind, DateTime start, DateTime end) =>
            new MfMachineTime.Row { MachineId = machine, Kind = kind, Start = start, End = end, Hours = (decimal)(end - start).TotalHours };

        [TestMethod]
        public void Intervals_Of_One_Machine_Are_Joined_And_Cut_So_That_Two_People_Use_The_Machine_Once()
        {
            var joined = MfIntervals.Union(new[] { (T(8), T(10)), (T(9), T(11)), (T(11), T(12)), (T(13), T(14)), (T(15), T(15)) });
            CollectionAssert.AreEqual(new[] { (T(8), T(12)), (T(13), T(14)) }, joined, "the overlapping and the touching ones are joined, the empty one is dropped");
            Assert.AreEqual(5m, MfIntervals.Hours(joined));
            var left = MfIntervals.Subtract(joined, MfIntervals.Union(new[] { (T(9), T(10)), (T(11, 30), T(13, 30)) }));
            CollectionAssert.AreEqual(new[] { (T(8), T(9)), (T(10), T(11, 30)), (T(13, 30), T(14)) }, left, "08-12 less 09-10 and 11:30-13:30, and 13-14 less 13-13:30");
            Assert.AreEqual(3m, MfIntervals.Hours(left), "1 + 1.5 + 0.5");
        }

        [TestMethod]
        public void Machine_Time_Counts_A_Moment_Of_The_Machine_For_One_Kind_Only_And_A_Manual_Booking_Its_Own_Hours()
        {
            var rows = new List<MfMachineTime.Row>
            {
                Row(1, MfBookingKind.Run, T(8), T(12)),          // two welders run the same machine: 08:00-12:00 and 10:00-14:00
                Row(1, MfBookingKind.Run, T(10), T(14)),
                Row(1, MfBookingKind.Setup, T(7), T(9)),         // setup 07:00-09:00, of which 08:00-09:00 is run time
                Row(1, MfBookingKind.Cleaning, T(14), T(14, 30)),
                Row(1, MfBookingKind.Waiting, T(12), T(15)),     // waiting 12:00-15:00, of which up to 14:30 is something else
                Row(2, MfBookingKind.Run, T(8), T(9)),           // another machine: its own hour
                Row(null, MfBookingKind.Run, T(8), T(10)),       // a manual work centre: every person is a resource of his own
                Row(null, MfBookingKind.Waiting, T(9), T(10, 30)),
            };
            MfMachineTime.Result r = MfMachineTime.Of(rows);
            Assert.AreEqual(9m, r.Run, "machine 1: 08:00-14:00 = 6 h (not 4 + 4 person-hours), machine 2: 1 h, the manual centre: 2 h");
            Assert.AreEqual(1m, r.Setup, "07:00-08:00 only");
            Assert.AreEqual(0.5m, r.Other, "the cleaning 14:00-14:30");
            Assert.AreEqual(2m, r.Waiting, "machine 1: 14:30-15:00 = 0.5 h, the manual centre: 1.5 h");
        }

        [TestMethod]
        public void Oee_Availability_Is_The_Machine_Run_Time_Over_The_Planned_Busy_Time_So_Two_People_On_One_Machine_Are_Not_Counted_Twice()
        {
            // 8 planned hours, one machine: two welders each booked 4 h of run, overlapping 10:00-12:00: person-hours 8, machine run time 6 (08:00-14:00)
            MfMachineTime.Result machine = MfMachineTime.Of(new[] { Row(1, MfBookingKind.Run, T(8), T(12)), Row(1, MfBookingKind.Run, T(10), T(14)) });
            Assert.AreEqual(6m, machine.Run);
            MfOeeMath.Result r = MfOeeMath.Compute(new MfOeeMath.Input { PlannedHours = 8m, RunHours = machine.Run, StandardHours = 5.4m, GoodQty = 9m, ScrapQty = 1m });
            Assert.AreEqual(75m, r.AvailabilityPercent, "6 / 8");
            Assert.AreEqual(90m, r.PerformancePercent, "5.4 / 6");
            Assert.AreEqual(90m, r.QualityPercent, "9 / 10");
            Assert.AreEqual(60.8m, r.OeePercent, "5.4 x 9 / (10 x 8) = 60.75, half away from zero");
            // the person-hours of the earlier definition would have called the same machine 100 % available
            Assert.AreEqual(100m, MfOeeMath.Compute(new MfOeeMath.Input { PlannedHours = 8m, RunHours = 8m, StandardHours = 5.4m, GoodQty = 9m, ScrapQty = 1m }).AvailabilityPercent);
        }

        // ------------------------------------------------------------------------------------------ D43, D38, D39: terminal and booking rules

        [TestMethod]
        public void The_Machine_Is_Mandatory_At_The_Terminal_Only_Where_There_Is_One_To_Choose_And_The_Work_Runs()
        {
            Assert.IsTrue(MfTerminalRules.MachineRequired(true, 2, MfBookingKind.Run));
            Assert.IsTrue(MfTerminalRules.MachineRequired(true, 1, MfBookingKind.Setup));
            Assert.IsTrue(MfTerminalRules.MachineRequired(true, 1, MfBookingKind.Rework));
            Assert.IsFalse(MfTerminalRules.MachineRequired(false, 2, MfBookingKind.Run), "the centre was told not to require it");
            Assert.IsFalse(MfTerminalRules.MachineRequired(true, 0, MfBookingKind.Run), "a centre without machines has nothing to choose");
            Assert.IsFalse(MfTerminalRules.MachineRequired(true, 2, MfBookingKind.Waiting), "nothing runs on a machine while somebody waits");
            Assert.IsTrue(new MfWorkCenter().RequiresMachine, "a new work centre requires the machine by default");
        }

        [TestMethod]
        public void An_Early_QC_Acceptance_Holds_Only_While_The_Reported_Quantities_Are_The_Ones_That_Were_Accepted()
        {
            var open = new MfOperation { QcAccepted = false, GoodQty = 5 };
            Assert.IsFalse(MfTerminalRules.AcceptanceStillHolds(open));
            var legacy = new MfOperation { QcAccepted = true, GoodQty = 5 };
            Assert.IsTrue(MfTerminalRules.AcceptanceStillHolds(legacy), "an acceptance given before the snapshot existed is not questioned");
            var accepted = new MfOperation { QcAccepted = true, GoodQty = 5, ScrapQty = 1, ReworkQty = 0, QcAcceptedGoodQty = 5, QcAcceptedScrapQty = 1, QcAcceptedReworkQty = 0 };
            Assert.IsTrue(MfTerminalRules.AcceptanceStillHolds(accepted));
            accepted.GoodQty = 6;
            Assert.IsFalse(MfTerminalRules.AcceptanceStillHolds(accepted), "one more good piece was reported after the inspection");
            accepted.GoodQty = 5;
            accepted.ReworkQty = 1;
            Assert.IsFalse(MfTerminalRules.AcceptanceStillHolds(accepted), "a rework quantity appeared");
            accepted.ReworkQty = 0;
            accepted.ScrapQty = 2;
            Assert.IsFalse(MfTerminalRules.AcceptanceStillHolds(accepted), "more scrap");
        }

        [TestMethod]
        public void Bookings_Of_A_Completed_Order_Are_Frozen_And_Names_Are_Shown_To_The_Named_Roles_And_The_Employee_Himself()
        {
            Assert.IsTrue(MfBookingRules.IsFrozen(MfOrderStatus.Completed));
            Assert.IsTrue(MfBookingRules.IsFrozen(MfOrderStatus.Closed));
            Assert.IsTrue(MfBookingRules.IsFrozen(MfOrderStatus.Cancelled));
            Assert.IsFalse(MfBookingRules.IsFrozen(MfOrderStatus.Released));
            Assert.IsFalse(MfBookingRules.IsFrozen(MfOrderStatus.InProgress));
            Assert.IsFalse(MfBookingRules.IsFrozen(MfOrderStatus.Draft));
            Assert.IsTrue(MfBookingRules.SeesName(true, null, 5));
            Assert.IsTrue(MfBookingRules.SeesName(false, 5, 5), "his own booking");
            Assert.IsFalse(MfBookingRules.SeesName(false, 4, 5), "a colleague's booking");
            Assert.IsFalse(MfBookingRules.SeesName(false, null, 5), "a reader without a personnel record");
        }

        [TestMethod]
        public void Planned_Serial_Numbers_Are_Split_Once_And_Joined_Whole_To_Fit_The_Column()
        {
            CollectionAssert.AreEqual(new[] { "A1", "A2", "A3" }, MfSerialRules.Planned("A1, A2;A3\nA1 \r\n"));
            Assert.AreEqual(0, MfSerialRules.Planned(null).Count);
            Assert.AreEqual("S1, S2, S3", MfSerialRules.Joined(new[] { "S1", "S2", "S3" }, 200));
            // three serials of 10 characters in a column of 30: two fit whole, the third is cut with the ellipsis (never a serial cut in the middle)
            string cut = MfSerialRules.Joined(new[] { "AAAAAAAAAA", "BBBBBBBBBB", "CCCCCCCCCC" }, 30);
            Assert.AreEqual("AAAAAAAAAA, BBBBBBBBBB …", cut);
            Assert.IsTrue(cut.Length <= 30);
            Assert.IsNull(MfSerialRules.Joined(new[] { new string('Z', 40) }, 30), "not even the first fits");
            Assert.IsNull(MfSerialRules.Joined(new string[0], 200));
        }

        // ------------------------------------------------------------------------------------------ D39: corrections wait for a second person (review item 9)

        [TestMethod]
        public void A_Correction_Counts_Only_After_A_Production_Manager_Other_Than_Its_Creator_Approved_It()
        {
            var list = new List<MfTimeBooking>
            {
                new MfTimeBooking { Id = 1, CreatorUserId = 4 },
                new MfTimeBooking { Id = 2, IsCorrection = true, CreatorUserId = 4 },
                new MfTimeBooking { Id = 3, IsCorrection = true, CreatorUserId = 4, CorrectionApprovedByUserId = 9 },
            };
            CollectionAssert.AreEqual(new[] { 1, 3 }, MfBookingRules.Effective(list.AsQueryable()).Select(b => b.Id).ToArray(), "the pending correction is in no hours, cost or OEE figure");
            StringAssert.Contains(MfBookingRules.ApprovalRefusal(list[0], 9), "اصلاحی نیست", "an ordinary booking needs no approval");
            Assert.IsNull(MfBookingRules.ApprovalRefusal(list[1], 9), "another production manager may approve");
            StringAssert.Contains(MfBookingRules.ApprovalRefusal(list[1], 4), "تفکیک وظایف", "the creator may not approve his own correction");
            StringAssert.Contains(MfBookingRules.ApprovalRefusal(list[2], 8), "قبلاً تأیید", "approved once");
        }

        [TestMethod]
        public void A_Correction_May_Reach_Back_Only_The_Configured_Days_And_Only_Into_The_Attended_Time_Of_The_Day()
        {
            var today = new DateTime(2026, 10, 27, 15, 0, 0);
            Assert.IsNull(MfBookingRules.BackdateRefusal(new DateTime(2026, 10, 20, 8, 0, 0), today, 7), "exactly 7 days back is allowed (★ Production:MaxBackdateDays = 7)");
            StringAssert.Contains(MfBookingRules.BackdateRefusal(new DateTime(2026, 10, 19, 23, 59, 0), today, 7), "حداکثر 7 روز");
            Assert.IsNull(MfBookingRules.BackdateRefusal(new DateTime(2026, 10, 27, 8, 0, 0), today, 7));
            Assert.IsNull(MfBookingRules.BackdateRefusal(new DateTime(2026, 10, 27, 8, 0, 0), today, 0), "0 days: only today");
            StringAssert.Contains(MfBookingRules.BackdateRefusal(new DateTime(2026, 10, 26, 8, 0, 0), today, 0), "حداکثر 0 روز");
            Assert.IsNotNull(MfBookingRules.BackdateRefusal(new DateTime(2026, 10, 26, 8, 0, 0), today, -3), "a negative setting is read as 0");

            StringAssert.Contains(MfBookingRules.AttendanceRefusal(0, 0, 60), "تردد ثبت نشده", "a day without attendance records takes no correction");
            Assert.IsNull(MfBookingRules.AttendanceRefusal(480, 420, 60), "7 h booked + 1 h = the 8 h he was present");
            string over = MfBookingRules.AttendanceRefusal(480, 420, 61);
            StringAssert.Contains(over, "(7 ساعت)");
            StringAssert.Contains(over, "(1.02 ساعت)");
            StringAssert.Contains(over, "(8 ساعت)");
            Assert.IsNotNull(MfBookingRules.AttendanceRefusal(480, 480, 1));
        }

        // ------------------------------------------------------------------------------------------ D7, D38: quantities are final once something rests on them (review item 7)

        private static MfOperation Step(int id, int sequence, MfOperationStatus status, bool qcAccepted = false, bool concession = false, int? ncr = null, bool started = false) =>
            new MfOperation
            {
                Id = id, OrderId = 1, Sequence = sequence, Status = status, QcAccepted = qcAccepted, AcceptedByConcession = concession, NcrId = ncr,
                StartedAt = started ? new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero) : (DateTimeOffset?)null,
            };

        [TestMethod]
        public void The_Quantities_Of_A_Done_Step_Are_Frozen_After_QC_Acceptance_A_Concession_An_Ncr_Or_When_The_Next_Step_Started()
        {
            var accepted = Step(1, 10, MfOperationStatus.Done, qcAccepted: true);
            StringAssert.Contains(MfBookingRules.QuantityFrozen(accepted, new[] { accepted }), "پذیرش بازرسی QC");

            var concession = Step(1, 10, MfOperationStatus.Done, concession: true);
            StringAssert.Contains(MfBookingRules.QuantityFrozen(concession, new[] { concession }), "ارفاق");

            var withNcr = Step(1, 10, MfOperationStatus.Done, ncr: 5);
            StringAssert.Contains(MfBookingRules.QuantityFrozen(withNcr, new[] { withNcr }), "گزارش عدم انطباق");
            var rejected = Step(1, 10, MfOperationStatus.Rejected, ncr: 5);
            StringAssert.Contains(MfBookingRules.QuantityFrozen(rejected, new[] { rejected }), "گزارش عدم انطباق");
            var open = Step(1, 10, MfOperationStatus.InProgress, ncr: 5);
            Assert.IsNull(MfBookingRules.QuantityFrozen(open, new[] { open }), "a step still running can be corrected");

            // the next step started working on the pieces
            var first = Step(1, 10, MfOperationStatus.Done);
            var nextRunning = Step(2, 20, MfOperationStatus.InProgress);
            StringAssert.Contains(MfBookingRules.QuantityFrozen(first, new[] { first, nextRunning }), "مرحله بعد");
            var nextReady = Step(2, 20, MfOperationStatus.Ready);
            Assert.IsNull(MfBookingRules.QuantityFrozen(first, new[] { first, nextReady }), "the next step has not started: the quantity can still be corrected");
            var skipped = Step(2, 20, MfOperationStatus.Skipped);
            var afterSkipped = Step(3, 30, MfOperationStatus.Done);
            StringAssert.Contains(MfBookingRules.QuantityFrozen(first, new[] { first, skipped, afterSkipped }), "مرحله بعد", "a skipped step passes the pieces on: the step after it counts");
            Assert.IsNull(MfBookingRules.QuantityFrozen(first, new[] { first, skipped, Step(3, 30, MfOperationStatus.Ready) }));
            Assert.IsNull(MfBookingRules.QuantityFrozen(first, new[] { first }), "the last step has nobody after it");
            var ready = Step(1, 10, MfOperationStatus.Ready);
            Assert.IsNull(MfBookingRules.QuantityFrozen(ready, new[] { ready, nextRunning }));
        }

        // ------------------------------------------------------------------------------------------ D6, D33: scrap serials (review item 12)

        [TestMethod]
        public void A_Partial_Scrap_Of_Serial_Pieces_Needs_The_Serials_And_The_Rest_Stay_On_The_Order()
        {
            var planned = new List<string> { "S1", "S2", "S3" };
            // every planned piece scrapped: no names needed
            CollectionAssert.AreEqual(new[] { "S1", "S2", "S3" }, MfSerialRules.Scrapped(planned, null, 3m).Scrapped);
            // part of them: exactly that many serials, written as planned (the typed case does not matter)
            var (one, error) = MfSerialRules.Scrapped(planned, "s2", 1m);
            Assert.IsNull(error);
            CollectionAssert.AreEqual(new[] { "S2" }, one);
            CollectionAssert.AreEqual(new[] { "S1", "S3" }, MfSerialRules.Remaining(planned, one), "the remaining serials in the planned order");
            // no serials for a part of the pieces
            StringAssert.Contains(MfSerialRules.Scrapped(planned, null, 2m).Error, "شماره سریال 2 قطعه");
            // a serial that is not planned (or was scrapped already)
            StringAssert.Contains(MfSerialRules.Scrapped(planned, "S9", 1m).Error, "جزو سریال‌های برنامه‌ریزی‌شده");
            // the count must match
            StringAssert.Contains(MfSerialRules.Scrapped(planned, "S1, S2", 1m).Error, "برابر نیست");
            // whole pieces only
            Assert.IsNotNull(MfSerialRules.Scrapped(planned, "S1", 1.5m).Error);
            Assert.IsNotNull(MfSerialRules.Scrapped(planned, "S1", 0m).Error);
            // nothing planned: nothing to track
            var none = MfSerialRules.Scrapped(new List<string>(), null, 4m);
            Assert.IsNull(none.Error);
            Assert.AreEqual(0, none.Scrapped.Count);
            Assert.AreEqual("S1\nS3", MfSerialRules.PerLine(new[] { "S1", "S3" }), "the NCR keeps one serial per line");
        }

        [TestMethod]
        public void The_Replacement_Pieces_Get_New_Traceable_Serials_That_Are_Never_Taken_Twice()
        {
            CollectionAssert.AreEqual(new[] { "S1-R", "S2-R" }, MfSerialRules.Replacements(new[] { "S1", "S2" }, s => false));
            CollectionAssert.AreEqual(new[] { "S1-R2", "S2-R" }, MfSerialRules.Replacements(new[] { "S1", "S2" }, s => s == "S1-R"), "S1-R is taken: S1-R2");
            CollectionAssert.AreEqual(new[] { "S1-R", "S1-R2" }, MfSerialRules.Replacements(new[] { "S1", "S1" }, s => false), "even inside one list");
        }

        // ------------------------------------------------------------------------------------------ D6, D8: what an NCR has to say

        private static NonConformance Ncr(NcrDisposition disposition, NcrStatus status) => new NonConformance { Disposition = disposition, Status = status };

        [TestMethod]
        public void A_Concession_Or_Scrap_Needs_The_Decision_To_Be_Approved_And_A_Rework_Needs_An_Approved_Or_Executed_Rework_Decision()
        {
            Assert.IsNull(MfNcrRules.Check(Ncr(NcrDisposition.Concession, NcrStatus.Approved), NcrDisposition.Concession));
            Assert.IsNull(MfNcrRules.Check(Ncr(NcrDisposition.Concession, NcrStatus.Closed), NcrDisposition.Concession));
            StringAssert.Contains(MfNcrRules.Check(Ncr(NcrDisposition.Concession, NcrStatus.Open), NcrDisposition.Concession), "تأیید نشده");
            StringAssert.Contains(MfNcrRules.Check(Ncr(NcrDisposition.Rework, NcrStatus.Approved), NcrDisposition.Concession), "ارفاقی", "another decision is not a concession");
            Assert.IsNull(MfNcrRules.Check(Ncr(NcrDisposition.Scrap, NcrStatus.Approved), NcrDisposition.Scrap));
            Assert.IsNotNull(MfNcrRules.Check(Ncr(NcrDisposition.Scrap, NcrStatus.Cancelled), NcrDisposition.Scrap), "a cancelled NCR decides nothing");
            Assert.IsNull(MfNcrRules.Check(Ncr(NcrDisposition.Rework, NcrStatus.Approved), NcrDisposition.Rework));
            Assert.IsNull(MfNcrRules.Check(Ncr(NcrDisposition.Rework, NcrStatus.Executed), NcrDisposition.Rework));
            Assert.IsNotNull(MfNcrRules.Check(Ncr(NcrDisposition.Rework, NcrStatus.Open), NcrDisposition.Rework), "the decision has to be signed first");
            Assert.IsNotNull(MfNcrRules.Check(Ncr(NcrDisposition.None, NcrStatus.Open), NcrDisposition.Rework));
            Assert.IsNotNull(MfNcrRules.Check(Ncr(NcrDisposition.Rework, NcrStatus.Closed), NcrDisposition.Rework), "a closed rework is over: no new rework operation");
        }

        [TestMethod]
        public void One_Ncr_Allows_One_Rework_Operation_And_A_Failed_Rework_Needs_A_New_Report()
        {
            // review item 6: the NCR already gave its rework operation
            var used = new NonConformance { Disposition = NcrDisposition.Rework, Status = NcrStatus.Approved, ReworkOperationId = 42 };
            StringAssert.Contains(MfNcrRules.Check(used, NcrDisposition.Rework), "فقط یک بار");
            used.Status = NcrStatus.Executed;
            Assert.IsNotNull(MfNcrRules.Check(used, NcrDisposition.Rework));
            // the same NCR before its rework operation exists, and the other dispositions, are not limited by it
            Assert.IsNull(MfNcrRules.Check(new NonConformance { Disposition = NcrDisposition.Rework, Status = NcrStatus.Approved }, NcrDisposition.Rework));
            Assert.IsNull(MfNcrRules.Check(new NonConformance { Disposition = NcrDisposition.Scrap, Status = NcrStatus.Approved, ReworkOperationId = 42 }, NcrDisposition.Scrap));
            Assert.IsNull(MfNcrRules.Check(new NonConformance { Disposition = NcrDisposition.Concession, Status = NcrStatus.Closed, ReworkOperationId = 42 }, NcrDisposition.Concession));
        }

        // ------------------------------------------------------------------------------------------ D19: a work order without an ITP is refused too

        [TestMethod]
        public void The_Release_Check_Of_The_Itp_Refuses_A_Missing_One_Names_The_Work_Order_And_Passes_Only_A_Client_Approved_One()
        {
            // no ITP at all: refused (the API cannot delete an ITP, so the smoke cannot reach this branch); the message names the work order
            (bool passed, string message) = MfReleaseRules.Itp(false, ItpStatus.Draft, null, "MF-WO-1");
            Assert.IsFalse(passed);
            StringAssert.Contains(message, "MF-WO-1");
            StringAssert.Contains(message, "برنامه بازرسی و آزمون (ITP) ساخته نشده است");
            // a number or status left over from nowhere does not make a missing ITP pass
            Assert.IsFalse(MfReleaseRules.Itp(false, ItpStatus.ClientApproved, "ITP-1", "MF-WO-1").Passed);

            // an ITP the client has not approved yet (draft, submitted, closed): refused, and the message names its number
            foreach (ItpStatus waiting in new[] { ItpStatus.Draft, ItpStatus.Submitted, ItpStatus.Closed })
            {
                (bool ok, string text) = MfReleaseRules.Itp(true, waiting, "ITP-7", "MF-WO-1");
                Assert.IsFalse(ok, waiting.ToString());
                Assert.AreEqual("ITP شماره ITP-7 هنوز تأیید کارفرما را ندارد.", text);
            }

            // client approved, or already in use: passes
            foreach (ItpStatus approved in new[] { ItpStatus.ClientApproved, ItpStatus.InUse })
            {
                (bool ok, string text) = MfReleaseRules.Itp(true, approved, "ITP-7", "MF-WO-1");
                Assert.IsTrue(ok, approved.ToString());
                Assert.AreEqual("ITP شماره ITP-7 تأیید شده است.", text);
            }
        }

        // ------------------------------------------------------------------------------------------ D24: the SLA clock is the server's

        [TestMethod]
        public void The_Received_Time_Of_A_Request_Is_The_Server_Time_Except_For_A_Call_Or_Letter_Entered_Within_24_Hours()
        {
            var now = new DateTime(2026, 10, 26, 10, 0, 0);
            Assert.IsNull(SvSlaTime.CheckReceived(null, "email", now), "no time supplied: nothing to check");
            Assert.IsNull(SvSlaTime.CheckReceived(now.AddHours(-3), "تماس تلفنی", now));
            Assert.IsNull(SvSlaTime.CheckReceived(now.AddHours(-3), "Phone call", now));
            Assert.IsNull(SvSlaTime.CheckReceived(now.AddHours(-24), "نامه", now), "exactly 24 hours back is allowed");
            Assert.AreEqual(SvSlaTime.ChannelRefusal, SvSlaTime.CheckReceived(now.AddHours(-3), "email", now));
            Assert.AreEqual(SvSlaTime.ChannelRefusal, SvSlaTime.CheckReceived(now.AddHours(-3), null, now));
            Assert.AreEqual(SvSlaTime.TooOldRefusal, SvSlaTime.CheckReceived(now.AddHours(-25), "تلفن", now));
            Assert.AreEqual(SvSlaTime.FutureRefusal, SvSlaTime.CheckReceived(now.AddHours(1), "تلفن", now));
            Assert.IsNull(SvSlaTime.CheckReceived(now.AddMinutes(4), "تلفن", now), "a clock difference of a few minutes is not held against the client");
            Assert.AreEqual(now, SvSlaTime.ReceivedOf(null, now));
            Assert.AreEqual(now, SvSlaTime.ReceivedOf(now.AddMinutes(4), now), "never later than the server time");
            Assert.AreEqual(now.AddHours(-3), SvSlaTime.ReceivedOf(now.AddHours(-3), now));
            Assert.IsTrue(SvSlaTime.IsCallOrLetter("تماس تلفنی"));
            Assert.IsTrue(SvSlaTime.IsCallOrLetter("نامه"));
            Assert.IsFalse(SvSlaTime.IsCallOrLetter("پورتال"));
            Assert.IsFalse(SvSlaTime.IsCallOrLetter(null));
        }

        [TestMethod]
        public void An_Action_Carries_No_Client_Time_And_The_Stored_Received_Time_Is_Not_Changed_By_An_Edit()
        {
            var stored = new DateTime(2026, 10, 26, 9, 0, 0);
            Assert.IsNull(SvSlaTime.CheckAction(null));
            Assert.AreEqual(SvSlaTime.ActionRefusal, SvSlaTime.CheckAction(stored));
            Assert.IsNull(SvSlaTime.CheckUnchanged(null, stored));
            Assert.IsNull(SvSlaTime.CheckUnchanged(stored, stored));
            Assert.IsNull(SvSlaTime.CheckUnchanged(stored.AddMilliseconds(500), stored), "a round trip through JSON may lose the sub-second part");
            Assert.AreEqual(SvSlaTime.ChangeRefusal, SvSlaTime.CheckUnchanged(stored.AddMinutes(1), stored));
        }

        // ------------------------------------------------------------------------------------------ D25: surveys

        [TestMethod]
        public void The_Low_Score_Threshold_Is_The_Setting_Then_The_Configuration_Then_Three_And_Never_Outside_1_To_5()
        {
            Assert.AreEqual(3, SvSurveyMath.Threshold(null, null));
            Assert.AreEqual(4, SvSurveyMath.Threshold("4", "2"));
            Assert.AreEqual(2, SvSurveyMath.Threshold(null, "2"));
            Assert.AreEqual(5, SvSurveyMath.Threshold("9", null), "a score is 1 to 5: more than 5 is cut to 5");
            Assert.AreEqual(2, SvSurveyMath.Threshold("0", "2"), "0 is not usable: the next source is");
            Assert.AreEqual(3, SvSurveyMath.Threshold("abc", "x"));
            Assert.AreEqual("Service:LowScoreThreshold", SvSurveyMath.ConfigKey);
        }

        [TestMethod]
        public void Only_Scores_Below_The_Threshold_Are_Low_And_A_Survey_Is_Answered_Only_When_Every_Question_Has_A_Score()
        {
            var answers = new List<(int RowNumber, string Question, int? Score)> { (1, "q1", 2), (2, "q2", 5), (3, "q3", null), (4, "q4", 3), (5, "q5", 1) };
            var low = SvSurveyMath.LowScores(answers, 3);
            CollectionAssert.AreEqual(new[] { 1, 5 }, low.Select(x => x.RowNumber).ToArray(), "3 is not below 3");
            CollectionAssert.AreEqual(new[] { 2, 1 }, low.Select(x => x.Score).ToArray());
            Assert.AreEqual(0, SvSurveyMath.LowScores(answers, 1).Count);
            Assert.IsTrue(SvSurveyMath.AllAnswered(new int?[] { 4, 5, 3 }));
            Assert.IsFalse(SvSurveyMath.AllAnswered(new int?[] { 4, null, 3 }));
            Assert.IsFalse(SvSurveyMath.AllAnswered(new int?[0]), "no question at all is not an answered survey");
        }

        // ------------------------------------------------------------------------------------------ D26: warranty rests on confirmed dates

        [TestMethod]
        public void A_Warranty_Decision_Is_Refused_While_The_Dates_Of_The_Unit_Are_One_Persons_Unchecked_Entry()
        {
            string refusal = SvWarranty.CheckDecision(SvWarrantyDecision.Warranty, true, false, false, datesConfirmed: false);
            StringAssert.Contains(refusal, "تأیید نشده");
            Assert.IsNull(SvWarranty.CheckDecision(SvWarrantyDecision.Warranty, true, false, false, datesConfirmed: true));
            Assert.IsNull(SvWarranty.CheckDecision(SvWarrantyDecision.Warranty, true, false, false), "confirmed by default (existing callers)");
            Assert.IsNull(SvWarranty.CheckDecision(SvWarrantyDecision.Billable, false, true, false, datesConfirmed: false), "billable work does not rest on the dates");
            Assert.IsNull(SvWarranty.CheckDecision(SvWarrantyDecision.Goodwill, false, false, true, datesConfirmed: false));
        }

        // ------------------------------------------------------------------------------------------ D36: field MTBF on meter hours inside the period

        [TestMethod]
        public void The_Meter_Hours_Of_A_Period_Are_The_Reading_At_Its_End_Less_The_Reading_Before_Its_Start()
        {
            var readings = new List<(DateTime Date, decimal Hours)> { (new DateTime(2026, 6, 1), 100m), (new DateTime(2026, 7, 15), 180m), (new DateTime(2026, 8, 20), 260m) };
            var from = new DateTime(2026, 7, 1);
            var to = new DateTime(2026, 8, 31);
            Assert.AreEqual(160m, SvKpiMath.PeriodHours(readings, from, to, new DateTime(2025, 1, 1)), "260 at the end less 100 before the start");
            Assert.AreEqual(80m, SvKpiMath.PeriodHours(readings, from, new DateTime(2026, 7, 31), new DateTime(2025, 1, 1)), "180 on 07-15 less 100");
            Assert.AreEqual(260m, SvKpiMath.PeriodHours(readings, null, null, null), "an open period counts from zero");
            Assert.IsNull(SvKpiMath.PeriodHours(readings, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30), new DateTime(2025, 1, 1)), "no reading inside the period: the unit is left out");
        }

        [TestMethod]
        public void A_Unit_Without_A_Reading_Before_The_Period_Is_Measured_Only_When_It_Was_Commissioned_Inside_It()
        {
            var readings = new List<(DateTime Date, decimal Hours)> { (new DateTime(2026, 8, 20), 90m) };
            var from = new DateTime(2026, 8, 1);
            var to = new DateTime(2026, 8, 31);
            Assert.AreEqual(90m, SvKpiMath.PeriodHours(readings, from, to, new DateTime(2026, 8, 5)), "commissioned inside the period: it started at zero");
            Assert.IsNull(SvKpiMath.PeriodHours(readings, from, to, new DateTime(2026, 1, 1)), "an old unit with no earlier reading cannot be measured for the period");
            Assert.IsNull(SvKpiMath.PeriodHours(readings, from, to, null));
        }

        // ------------------------------------------------------------------------------------------ the model of the new columns

        /// <summary>The application context on the SQL Server provider without a server: enough to build the model and to turn a query into its SQL text.</summary>
        private static (ServiceProvider Provider, IServiceScope Scope, ApplicationDbContext Context) OfflineContext()
        {
            var services = new ServiceCollection();
            services.AddOptions();
            services.Configure<ViewModels.Identity.Settings.SiteSettings>(o => o.CookieOptions = new ViewModels.Identity.Settings.CookieOptions { DistributedSqlServerCacheOptions = new ViewModels.Identity.Settings.DistributedSqlServerCacheOptions { TableName = "AppSqlCache", SchemaName = "dbo" } });
            services.AddHttpContextAccessor();
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer("Server=localhost;Database=none;User Id=x;Password=x;TrustServerCertificate=true"));
            ServiceProvider provider = services.BuildServiceProvider();
            IServiceScope scope = provider.CreateScope();
            return (provider, scope, scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        [TestMethod]
        public void The_Grouped_And_Filtered_Queries_Of_Release_R1p_Translate_To_Plain_Sql()
        {
            // ToQueryString throws when a query cannot be translated, so a query that EF could not run on the server fails here and not on the first request
            var (provider, scope, context) = OfflineContext();
            using (provider)
            using (scope)
            {
                // the cost report of the finance link groups the cost entries; the protected flag is summed as an integer (no Any inside the grouping)
                string report = Services.NasbNiro.Finance.FinanceLinkService.CostSums(context.Set<Entities.Model.NasbNiro.Finance.CostEntry>().Where(c => c.ContractId == 1)).ToQueryString();
                StringAssert.Contains(report, "GROUP BY");
                StringAssert.Contains(report, "SUM(");
                Assert.IsFalse(report.Contains("EXISTS"), "no subquery inside the grouping: " + report);

                // review item 19: the booked hours of a period are summed per work centre and day in the database, from the bookings that count (a correction waiting for approval is not one)
                string hours = MfShopFloorService.HoursByCentreAndDay(context.Set<MfTimeBooking>().AsNoTracking(), context.Set<MfOperation>().AsNoTracking(), new DateTime(2026, 8, 1), new DateTime(2026, 9, 1), 5, null).ToQueryString();
                StringAssert.Contains(hours, "GROUP BY");
                StringAssert.Contains(hours, "SUM(");
                StringAssert.Contains(hours, "COUNT(");
                StringAssert.Contains(hours, "CorrectionApprovedByUserId", "only approved corrections count");

                // review item 10: the satisfaction average reads the verified surveys only
                string verified = SvSurveyMath.VerifiedAnswers(context.Set<SlSurvey>()).ToQueryString();
                StringAssert.Contains(verified, "Verified");
                StringAssert.Contains(SvSurveyMath.UnverifiedAnswers(context.Set<SlSurvey>()).ToQueryString(), "Verified");

                // the voucher readers leave the protected labour lines out for the roles that may not read them
                string masked = Services.NasbNiro.Finance.FnLabourMask.Visible(context.Set<Entities.Model.NasbNiro.Finance.FnVoucherLine>(), false).ToQueryString();
                StringAssert.Contains(masked, "WHERE");
                StringAssert.Contains(masked, "[LabourProtected]");
                Assert.IsFalse(Services.NasbNiro.Finance.FnLabourMask.Visible(context.Set<Entities.Model.NasbNiro.Finance.FnVoucherLine>(), true).ToQueryString().Contains("WHERE"), "the payroll readers get every line");
            }
        }

        [TestMethod]
        public void A_Boolean_Column_That_Defaults_To_True_Has_The_Sentinel_True_So_That_False_Is_Stored_As_False()
        {
            // With the default sentinel (false) EF treats a CLR false as "not set" and leaves the column out of the INSERT, so the database default (true) is applied and a false is silently saved as true.
            // HasSentinel(true) makes true the "not set" value instead. Every boolean with a default of true must have it (R1p review 3: RequiresMachine and RequiresIncomingInspection).
            var (provider, scope, context) = OfflineContext();
            using var providerHolder = provider;
            using var scopeHolder = scope;
            var model = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
            var defaultingToTrue = model.GetEntityTypes().SelectMany(e => e.GetProperties()).Where(p => p.ClrType == typeof(bool) && Equals(p.GetDefaultValue(), true)).ToList();
            Assert.IsTrue(defaultingToTrue.Any(p => p.DeclaringType.ClrType == typeof(MfWorkCenter) && p.Name == "RequiresMachine"), "the scan finds the work-centre column");
            Assert.IsTrue(defaultingToTrue.Any(p => p.DeclaringType.ClrType == typeof(InventoryItem) && p.Name == "RequiresIncomingInspection"), "and the item column");
            foreach (var property in defaultingToTrue)
                Assert.AreEqual(true, property.Sentinel, property.DeclaringType.DisplayName() + "." + property.Name + " defaults to true and needs HasSentinel(true)");
            // the CLR default of both new objects is true as well (a new object is "unset")
            Assert.IsTrue(new MfWorkCenter().RequiresMachine);
            Assert.IsTrue(new InventoryItem().RequiresIncomingInspection);
        }

        [TestMethod]
        public void Entity_Model_Carries_The_Columns_Indexes_And_Foreign_Keys_Of_Release_R1p()
        {
            var (provider, scope, context) = OfflineContext();
            using var providerHolder = provider;
            using var scopeHolder = scope;
            var model = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;

            void Has(Type type, params string[] names)
            {
                var entity = model.FindEntityType(type);
                Assert.IsNotNull(entity, type.Name);
                foreach (string name in names) Assert.IsNotNull(entity.FindProperty(name), type.Name + "." + name);
            }
            void Lacks(Type type, params string[] names)
            {
                foreach (string name in names) Assert.IsNull(model.FindEntityType(type).FindProperty(name), type.Name + "." + name + " was removed again");
            }
            bool HasForeignKey(Type type, string property) => model.FindEntityType(type).GetForeignKeys().Any(f => f.Properties.Any(p => p.Name == property));
            void Precision(Type type, string name, int precision, int scale)
            {
                var property = model.FindEntityType(type).FindProperty(name);
                Assert.AreEqual(precision, property.GetPrecision(), type.Name + "." + name + " precision");
                Assert.AreEqual(scale, property.GetScale(), type.Name + "." + name + " scale");
            }

            // the labour pools: the booking carries the cost payroll gave, what the days absorbed and the month of its pool; nothing per person leaves the booking
            Has(typeof(MfTimeBooking), "LabourPendingPeriod", "PayrollCost", "AbsorbedLabour", "RateFromPayroll", "LabourAggregatedAt", "IsCorrection", "CorrectsBookingId", "CorrectionApprovedByUserId", "CorrectionApprovedAt");
            Lacks(typeof(MfTimeBooking), "PendingLabour");
            Lacks(typeof(MfCostPosting), "LabourDeferred");
            Has(typeof(MfLabourPool), "Module", "Period", "PoolKey", "People", "Hours", "Cost", "Absorbed", "Variance", "Rate", "Protected", "Items", "AccountingEventId", "PostedAt", "PostedByUserId");
            Has(typeof(SvServiceOrder), "LabourPendingPeriod", "LabourAggregatedAt", "LabourPayrollCost", "LabourPooledAmount");
            Precision(typeof(MfTimeBooking), "PayrollCost", 28, 10);
            Precision(typeof(MfTimeBooking), "AbsorbedLabour", 18, 2);
            Precision(typeof(MfLabourPool), "Hours", 18, 4);
            Precision(typeof(MfLabourPool), "Cost", 18, 2);
            Precision(typeof(MfLabourPool), "Absorbed", 18, 2);
            Precision(typeof(MfLabourPool), "Variance", 18, 2);
            Precision(typeof(MfLabourPool), "Rate", 18, 4);
            Precision(typeof(SvServiceOrder), "LabourPayrollCost", 28, 10);
            Precision(typeof(SvServiceOrder), "LabourPooledAmount", 22, 2);
            Assert.IsTrue(model.FindEntityType(typeof(MfLabourPool)).GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "Module", "Period", "PoolKey" })), "one pool row per module, month and pool: two runs at once cannot close a month twice");
            Assert.IsTrue(HasForeignKey(typeof(MfLabourPool), "AccountingEventId"));

            // the protected flag travels from the cost entry to the event line and the voucher line
            Has(typeof(Entities.Model.NasbNiro.Finance.CostEntry), "LabourProtected");
            Has(typeof(Entities.Model.NasbNiro.Finance.AccountingEventLine), "LabourProtected");
            Has(typeof(Entities.Model.NasbNiro.Finance.FnVoucherLine), "LabourProtected");

            Has(typeof(MfOperation), "IsWelding", "WpsId", "WeldLogId", "AcceptedByConcession", "QcAcceptedGoodQty", "QcAcceptedScrapQty", "QcAcceptedReworkQty");
            Has(typeof(MfOperationStandard), "IsWelding", "WpsId");
            Has(typeof(MfProductionOrder), "WrittenOffQty", "ReplacesOrderId");
            Has(typeof(MfWorkCenter), "RequiresMachine");
            Has(typeof(NonConformance), "ReworkOperationId");
            Has(typeof(SlSurvey), "SkipReason", "RecordedByRole", "AnsweredByUserId", "Verified", "VerifiedByUserId", "VerifiedAt", "VerifyNote", "ComplaintId");

            Assert.IsTrue(HasForeignKey(typeof(MfProductionOrder), "ReplacesOrderId"));
            Assert.IsTrue(HasForeignKey(typeof(MfOperation), "WpsId"));
            Assert.IsTrue(HasForeignKey(typeof(MfOperation), "WeldLogId"));
            Assert.IsTrue(HasForeignKey(typeof(MfOperationStandard), "WpsId"));
            Assert.IsTrue(HasForeignKey(typeof(MfTimeBooking), "CorrectsBookingId"));
            Assert.IsTrue(HasForeignKey(typeof(SlSurvey), "ComplaintId"));
            Assert.AreEqual(true, model.FindEntityType(typeof(MfWorkCenter)).FindProperty("RequiresMachine").GetDefaultValue(), "existing work centres get true too");
            Assert.IsTrue(model.FindEntityType(typeof(SlSurvey)).GetIndexes().Any(i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == "ComplaintId" && i.GetFilter() != null), "one complaint draft per survey");
            // the month-end run reads the bookings and the orders that wait: the filtered index holds only the rows with a pending month AND not yet pooled (R1p round 3, L11)
            foreach (Type waiting in new[] { typeof(MfTimeBooking), typeof(SvServiceOrder) })
            {
                var pendingIndex = model.FindEntityType(waiting).GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == "LabourPendingPeriod");
                StringAssert.Contains(pendingIndex.GetFilter(), "[LabourPendingPeriod] IS NOT NULL", waiting.Name);
                StringAssert.Contains(pendingIndex.GetFilter(), "[LabourAggregatedAt] IS NULL", waiting.Name);
            }
        }
    }
}
