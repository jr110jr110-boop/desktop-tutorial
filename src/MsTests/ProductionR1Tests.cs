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
    /// The pure rules of release R1p (production, maintenance and after-sales audit findings): the quantity chain of the operations (D7), the OEE after ISO 22400-2 (D29), MTTR on clock hours (D37),
    /// the privacy of a month of labour with its carry to the next month and the quarter-end cap (D1, D2), the booking and terminal rules (D38, D39, D43), the NCR dispositions (D6, D8),
    /// the SLA clock (D24), the survey rules (D25), the warranty rule (D26) and the field MTBF hours (D36). Every figure is worked out by hand in the test.
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

        // ------------------------------------------------------------------------------------------ D1 and D2: the privacy of a month of labour

        [TestMethod]
        public void A_Pool_With_Fewer_Than_Three_Payroll_Rated_People_Waits_And_At_The_Quarter_End_Is_Posted_Flagged()
        {
            Assert.AreEqual(MfLabourMonthAction.Post, MfLabourCarry.Decide(3, 3, 140505));
            Assert.AreEqual(MfLabourMonthAction.Carry, MfLabourCarry.Decide(1, 1, 140505), "Mordad is not a quarter end");
            Assert.AreEqual(MfLabourMonthAction.PostProtected, MfLabourCarry.Decide(1, 1, 140506), "Shahrivar ends the quarter");
            Assert.AreEqual(MfLabourMonthAction.Carry, MfLabourCarry.Decide(2, 2, 140507));
            Assert.AreEqual(MfLabourMonthAction.PostProtected, MfLabourCarry.Decide(2, 2, 140509), "Azar");
            Assert.AreEqual(MfLabourMonthAction.PostProtected, MfLabourCarry.Decide(1, 1, 140512), "Esfand");
            Assert.AreEqual(MfLabourMonthAction.PostProtected, MfLabourCarry.Decide(2, 2, 140503), "Khordad");
            Assert.AreEqual(MfLabourMonthAction.Post, MfLabourCarry.Decide(1, 0, 140505), "the work-centre standard rate is nobody's pay");
            Assert.AreEqual(MfLabourMonthAction.Carry, MfLabourCarry.Decide(6, 1, 140505), "one payroll employee among five on the standard rate: the known part can be subtracted");
            foreach (int month in new[] { 3, 6, 9, 12 }) Assert.IsTrue(MfLabourCarry.IsQuarterEnd(140500 + month), month.ToString());
            foreach (int month in new[] { 1, 2, 4, 5, 7, 8, 10, 11 }) Assert.IsFalse(MfLabourCarry.IsQuarterEnd(140500 + month), month.ToString());
        }

        [TestMethod]
        public void The_Month_Arithmetic_Of_The_Monthly_Posting_Is_The_Persian_Calendar()
        {
            Assert.AreEqual(140505, MfLabourMonth.Of(new DateTime(2026, 8, 6)));
            Assert.AreEqual("MLM-140506", MfLabourMonth.Number(140506));
            Assert.AreEqual("1405/05", MfLabourMonth.Text(140505));
            Assert.AreEqual(new DateTime(2026, 8, 22), MfLabourMonth.End(140505), "Mordad has 31 days: 1405/05/31");
            Assert.AreEqual(new DateTime(2026, 9, 22), MfLabourMonth.End(140506), "Shahrivar has 31 days: 1405/06/31");
            Assert.AreEqual(140506, MfLabourMonth.NextOpen(140505, new int[0]));
            Assert.AreEqual(140507, MfLabourMonth.NextOpen(140505, new[] { 140506 }), "a posted month is never reopened");
            Assert.AreEqual(140601, MfLabourMonth.NextOpen(140512, new int[0]));
        }

        private static MfPendingBooking B(int id, int workOrder, int employee, decimal labour, bool payroll = true, int? wbs = null, string centre = "CNC") =>
            new MfPendingBooking { BookingId = id, WorkOrderId = workOrder, ContractId = 7, WbsNodeId = wbs, CostCenter = centre, EmployeeId = employee, FromPayroll = payroll, Labour = labour };

        [TestMethod]
        public void A_Work_Order_With_Three_Payroll_Rated_People_Is_Posted_On_Its_Own_And_The_Sum_Is_Rounded_Once()
        {
            MfLabourPlan plan = MfLabourPlanner.Plan(new[] { B(1, 10, 1, 100.4m), B(2, 10, 2, 200.4m), B(3, 10, 3, 300.4m) }, 140505);
            Assert.AreEqual(0, plan.CarriedBookingIds.Count);
            Assert.AreEqual(1, plan.Groups.Count);
            MfLabourPlanGroup g = plan.Groups[0];
            Assert.AreEqual(10, g.WorkOrderId);
            Assert.AreEqual(7, g.ContractId);
            Assert.IsFalse(g.Protected);
            Assert.AreEqual(601m, g.Amount, "100.4 + 200.4 + 300.4 = 601.2 rounded once, not 100 + 200 + 300");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, g.BookingIds);
            Assert.AreEqual(2m, MfLabourPlanner.Plan(new[] { B(1, 10, 1, 0.5m), B(2, 10, 2, 0.5m), B(3, 10, 3, 0.5m) }, 140505).Groups[0].Amount, "1.5 rounds away from zero");
        }

        [TestMethod]
        public void Small_Work_Orders_Of_One_Cost_Centre_Are_Posted_Together_Without_A_Work_Order_When_They_Are_Enough_People()
        {
            // three work orders with one payroll-rated person each: alone each would show a person's pay; together in the cost centre they are three people
            MfLabourPlan plan = MfLabourPlanner.Plan(new[] { B(1, 10, 1, 100m), B(2, 11, 2, 200m), B(3, 12, 3, 300m) }, 140505);
            Assert.AreEqual(0, plan.CarriedBookingIds.Count);
            Assert.AreEqual(1, plan.Groups.Count);
            Assert.IsNull(plan.Groups[0].WorkOrderId);
            Assert.AreEqual("CNC", plan.Groups[0].CostCenter);
            Assert.AreEqual(600m, plan.Groups[0].Amount);
            Assert.IsFalse(plan.Groups[0].Protected);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, plan.Groups[0].BookingIds);
        }

        [TestMethod]
        public void A_Pool_That_Is_Still_Too_Small_Is_Carried_To_The_Next_Month_Except_At_The_Quarter_End()
        {
            var pool = new[] { B(1, 10, 1, 100m), B(2, 11, 2, 200m) };
            MfLabourPlan carried = MfLabourPlanner.Plan(pool, 140505);
            Assert.AreEqual(0, carried.Groups.Count);
            CollectionAssert.AreEqual(new[] { 1, 2 }, carried.CarriedBookingIds);

            MfLabourPlan capped = MfLabourPlanner.Plan(pool, 140506);
            Assert.AreEqual(0, capped.CarriedBookingIds.Count);
            Assert.AreEqual(2, capped.Groups.Count);
            Assert.IsTrue(capped.Groups.All(x => x.Protected), "posted per work order, flagged: only PAYROLL, FINANCEMANAGER and CEO read the amounts");
            CollectionAssert.AreEqual(new[] { 10, 11 }, capped.Groups.Select(x => x.WorkOrderId.Value).ToArray());
            CollectionAssert.AreEqual(new[] { 100m, 200m }, capped.Groups.Select(x => x.Amount).ToArray());
        }

        [TestMethod]
        public void A_Big_Work_Order_Is_Posted_While_A_Small_One_In_The_Same_Month_Waits()
        {
            MfLabourPlan plan = MfLabourPlanner.Plan(new[] { B(1, 10, 1, 10m), B(2, 10, 2, 10m), B(3, 10, 3, 10m), B(4, 11, 4, 99m) }, 140505);
            Assert.AreEqual(1, plan.Groups.Count);
            Assert.AreEqual(10, plan.Groups[0].WorkOrderId);
            Assert.AreEqual(30m, plan.Groups[0].Amount);
            CollectionAssert.AreEqual(new[] { 4 }, plan.CarriedBookingIds);
        }

        [TestMethod]
        public void People_On_The_Standard_Rate_Are_Not_Personal_Pay_And_Wbs_Nodes_Are_Posted_Apart()
        {
            // one person without a payroll rate: the work-centre rate is not his pay, so the group is posted as it is
            MfLabourPlan standard = MfLabourPlanner.Plan(new[] { B(1, 10, 1, 40m, payroll: false) }, 140505);
            Assert.AreEqual(1, standard.Groups.Count);
            Assert.AreEqual(40m, standard.Groups[0].Amount);
            Assert.IsFalse(standard.Groups[0].Protected);
            // the same work order on two WBS nodes: two groups
            MfLabourPlan nodes = MfLabourPlanner.Plan(new[] { B(1, 10, 1, 10m, wbs: 5), B(2, 10, 2, 20m, wbs: 5), B(3, 10, 3, 30m, wbs: 5), B(4, 10, 1, 1m, wbs: 6), B(5, 10, 2, 2m, wbs: 6), B(6, 10, 3, 3m, wbs: 6) }, 140505);
            Assert.AreEqual(2, nodes.Groups.Count);
            CollectionAssert.AreEqual(new[] { 60m, 6m }, nodes.Groups.Select(x => x.Amount).ToArray());
            CollectionAssert.AreEqual(new int?[] { 5, 6 }, nodes.Groups.Select(x => x.WbsNodeId).ToArray());
        }

        private static SvPendingLabour Pend(int id, params int[] payrollEmployees) =>
            new SvPendingLabour { OrderId = id, Decision = SvWarrantyDecision.Warranty, CostCenter = "AFTERSALES", Labour = 100m, Employees = payrollEmployees.ToHashSet(), PayrollEmployees = payrollEmployees.ToHashSet() };

        [TestMethod]
        public void The_Month_Pool_Of_Service_Labour_Waits_Instead_Of_Showing_One_Persons_Pay_And_Is_Capped_At_The_Quarter_End()
        {
            // D2: the pool of the month is the sum of its orders; two orders by the same one person are still one person
            Assert.AreEqual(MfLabourMonthAction.Carry, SvLabourMonth.Decide(new[] { Pend(1, 7), Pend(2, 7) }, 140508));
            Assert.AreEqual(MfLabourMonthAction.Post, SvLabourMonth.Decide(new[] { Pend(1, 7), Pend(2, 8), Pend(3, 9) }, 140508));
            Assert.AreEqual(MfLabourMonthAction.PostProtected, SvLabourMonth.Decide(new[] { Pend(1, 7) }, 140509), "Azar is a quarter end");
            Assert.AreEqual(140509, SvLabourMonth.NextOpen(140508, new int[0]));
            Assert.AreEqual(140510, SvLabourMonth.NextOpen(140508, new[] { 140509 }));
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
        public void The_Grouped_Labour_Cost_Queries_Translate_To_Plain_Sql_Aggregates()
        {
            // the cost report of the finance link and the history of the service labour months group the cost entries; the protected flag is summed as an integer (no Any inside the grouping),
            // so the whole grouping is SUM/GROUP BY that the provider translates on every version. ToQueryString throws when a query cannot be translated.
            var (provider, scope, context) = OfflineContext();
            using (provider)
            using (scope)
            {
                string report = Services.NasbNiro.Finance.FinanceLinkService.CostSums(context.Set<Entities.Model.NasbNiro.Finance.CostEntry>().Where(c => c.ContractId == 1)).ToQueryString();
                StringAssert.Contains(report, "GROUP BY");
                StringAssert.Contains(report, "SUM(");
                Assert.IsFalse(report.Contains("EXISTS"), "no subquery inside the grouping: " + report);

                string months = SvLabourService.CostByPeriod(context.Set<Entities.Model.NasbNiro.Finance.CostEntry>().AsNoTracking()).ToQueryString();
                StringAssert.Contains(months, "GROUP BY");
                StringAssert.Contains(months, "SUM(");
                Assert.IsFalse(months.Contains("EXISTS"), "no subquery inside the grouping: " + months);
            }
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
            bool HasForeignKey(Type type, string property) => model.FindEntityType(type).GetForeignKeys().Any(f => f.Properties.Any(p => p.Name == property));

            Has(typeof(MfTimeBooking), "LabourPendingPeriod", "PendingLabour", "RateFromPayroll", "LabourAggregatedAt", "IsCorrection", "CorrectsBookingId");
            Has(typeof(MfCostPosting), "LabourDeferred");
            Has(typeof(MfOperation), "IsWelding", "WpsId", "WeldLogId", "AcceptedByConcession", "QcAcceptedGoodQty", "QcAcceptedScrapQty", "QcAcceptedReworkQty");
            Has(typeof(MfOperationStandard), "IsWelding", "WpsId");
            Has(typeof(MfProductionOrder), "WrittenOffQty", "ReplacesOrderId");
            Has(typeof(MfWorkCenter), "RequiresMachine");
            Has(typeof(Entities.Model.NasbNiro.Finance.CostEntry), "LabourProtected");
            Has(typeof(Entities.Model.NasbNiro.Finance.AccountingEventLine), "LabourProtected");
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
            Assert.IsTrue(model.FindEntityType(typeof(MfTimeBooking)).GetIndexes().Any(i => i.Properties.Count == 1 && i.Properties[0].Name == "LabourPendingPeriod" && i.GetFilter() != null));
        }
    }
}
