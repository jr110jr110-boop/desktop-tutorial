using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.OfficeDtos;

namespace MsTests
{
    /// <summary>
    /// Release R1f (payroll, attendance, leave): the shared leave accrual, the early-leave deduction, holiday work inside the overtime cap, the approved-overtime rule,
    /// the protected-group split of the payroll voucher and of the production labour, and the refusal of a missing rate-table key. Pure functions only: no database.
    /// </summary>
    [TestClass]
    public class PayrollControlTests
    {
        private static readonly PersianCalendar Persian = new PersianCalendar();

        private static DateTime P(int year, int month, int day) => Persian.ToDateTime(year, month, day, 0, 0, 0, 0);

        // ------------------------------------------------------------------------------------------ shared leave accrual

        [TestMethod]
        public void Leave_Accrual_Starts_In_The_Hire_Month_Of_A_Mid_Year_Hire()
        {
            // hired on 1405/07/10: the first accruing month of 1405 is month 7, a year before the hire accrues nothing
            Assert.AreEqual(7, LeaveAccrual.FirstMonth(1405, P(1405, 7, 10), null, null));
            Assert.AreEqual(1, LeaveAccrual.FirstMonth(1405, P(1399, 3, 1), null, null));
            Assert.AreEqual(13, LeaveAccrual.FirstMonth(1404, P(1405, 7, 10), null, null));
            // 3 months (7, 8, 9) at 2.5 days
            Assert.AreEqual(7.5m, LeaveAccrual.Entitlement(2.5m, 7, 0, 9));
            // months already booked are not added twice: booked up to 7, now up to 9
            Assert.AreEqual(5.0m, LeaveAccrual.Entitlement(2.5m, 7, 7, 9));
            Assert.AreEqual(0m, LeaveAccrual.Entitlement(2.5m, 7, 9, 9));
            // a rate of zero (or less) accrues nothing
            Assert.AreEqual(0m, LeaveAccrual.Entitlement(0m, 7, 0, 9));
        }

        [TestMethod]
        public void Leave_Accrual_Starts_At_The_Go_Live_Month_Not_Before()
        {
            int goLive = PersianCal.Index(1405, 4);
            // an old employee: the system accrues only from the go-live month
            Assert.AreEqual(4, LeaveAccrual.FirstMonth(1405, P(1399, 3, 1), goLive, null));
            // hired after the go-live month: the hire month wins
            Assert.AreEqual(6, LeaveAccrual.FirstMonth(1405, P(1405, 6, 2), goLive, null));
            // a go-live in an earlier year changes nothing in this year; one in a later year means no accrual yet
            Assert.AreEqual(1, LeaveAccrual.FirstMonth(1405, P(1399, 3, 1), PersianCal.Index(1404, 7), null));
            Assert.AreEqual(13, LeaveAccrual.FirstMonth(1405, P(1399, 3, 1), PersianCal.Index(1406, 1), null));
            // 1405/04 .. 1405/08 = 5 months
            Assert.AreEqual(12.5m, LeaveAccrual.Entitlement(2.5m, 4, 0, 8));
        }

        [TestMethod]
        public void An_Opening_Balance_Replaces_The_Go_Live_Month()
        {
            int goLive = PersianCal.Index(1405, 4);
            // the opening balance is as of month 6: the accrual continues from month 6, whatever the go-live month says
            Assert.AreEqual(6, LeaveAccrual.FirstMonth(1405, P(1399, 3, 1), goLive, 6));
            Assert.AreEqual(6, LeaveAccrual.FirstMonth(1405, P(1399, 3, 1), null, 6));
            // an employee hired after the opening month still starts at the hire month
            Assert.AreEqual(8, LeaveAccrual.FirstMonth(1405, P(1405, 8, 1), goLive, 6));
            // a plan reads the opening per employee
            var plan = new Dictionary<int, int?> { [1] = 6, [2] = null };
            Assert.AreEqual(6, LeaveAccrual.FirstMonth(1405, P(1399, 3, 1), goLive, plan[1]));
            Assert.AreEqual(4, LeaveAccrual.FirstMonth(1405, P(1399, 3, 1), goLive, plan[2]));
        }

        [TestMethod]
        public void Leave_Booking_Is_Idempotent_And_Carries_The_Previous_Year_Within_The_Limit()
        {
            var previous = new LeaveAccrual.State { CarriedForward = 4m, Entitled = 30m, Used = 20m, LastMonth = 12 }; // 14 left, the limit is 9
            Assert.AreEqual(9m, LeaveAccrual.Carry(previous.CarriedForward, previous.Entitled, previous.Used, 9m));
            Assert.AreEqual(0m, LeaveAccrual.Carry(0m, 10m, 25m, 9m), "never below zero");

            // a new year starts from the carry, then month 3 is booked: months 1..3 at 2.5 days, 1 day used
            LeaveAccrual.State b = LeaveAccrual.Book(null, previous, 2.5m, 9m, 1, 3, 1m);
            Assert.AreEqual(9m, b.CarriedForward);
            Assert.AreEqual(7.5m, b.Entitled);
            Assert.AreEqual(1m, b.Used);
            Assert.AreEqual(3, b.LastMonth);
            // the same month booked again changes nothing; the next month adds one month
            LeaveAccrual.Book(b, previous, 2.5m, 9m, 1, 3, 1m);
            Assert.AreEqual(7.5m, b.Entitled);
            Assert.AreEqual(1m, b.Used);
            LeaveAccrual.Book(b, previous, 2.5m, 9m, 1, 4, 2m);
            Assert.AreEqual(10m, b.Entitled);
            Assert.AreEqual(3m, b.Used);
        }

        [TestMethod]
        public void Mid_Year_Hire_Gets_The_Exact_Pro_Rata_Entitlement()
        {
            // the numbers of the smoke: rate 2.5 days a month (read from the rate table), hired in month 7, booked up to month 8 -> 2 months = 5 days
            LeaveAccrual.State b = LeaveAccrual.Book(null, null, 2.5m, 9m, LeaveAccrual.FirstMonth(1405, P(1405, 7, 15), null, null), 8, 0m);
            Assert.AreEqual(0m, b.CarriedForward);
            Assert.AreEqual(5m, b.Entitled);
        }

        [TestMethod]
        public void Go_Live_Month_Setting_Is_Parsed_In_Every_Common_Form()
        {
            int y, m;
            Assert.IsTrue(PayrollGoLive.TryParse("1405/08", out y, out m)); Assert.AreEqual(1405, y); Assert.AreEqual(8, m);
            Assert.IsTrue(PayrollGoLive.TryParse("1405-8", out y, out m)); Assert.AreEqual(8, m);
            Assert.IsTrue(PayrollGoLive.TryParse("140508", out y, out m)); Assert.AreEqual(1405, y); Assert.AreEqual(8, m);
            Assert.IsTrue(PayrollGoLive.TryParse("۱۴۰۵/۰۸", out y, out m)); Assert.AreEqual(1405, y); Assert.AreEqual(8, m);
            Assert.IsFalse(PayrollGoLive.TryParse("1405/13", out _, out _));
            Assert.IsFalse(PayrollGoLive.TryParse("2026/08", out _, out _));
            Assert.IsFalse(PayrollGoLive.TryParse("", out _, out _));
            Assert.IsFalse(PayrollGoLive.TryParse("abc", out _, out _));
            Assert.AreEqual("1405/08", PayrollGoLive.Format(1405, 8));
        }

        // ------------------------------------------------------------------------------------------ early leave, approved overtime, holiday work

        private static DayInput Shift(int inMinute, int outMinute, bool nonWorking = false)
        {
            DateTime date = new DateTime(2026, 8, 12);
            return new DayInput
            {
                Date = date, HasShift = true, StartMinute = 8 * 60, EndMinute = 16 * 60, BreakMinutes = 30, GraceMinutes = 15, DelayPenaltyFactor = 3m, NonWorking = nonWorking,
                OvertimeRequiresApproval = false, Punches = new List<DateTime> { date.AddMinutes(inMinute), date.AddMinutes(outMinute) },
            };
        }

        [TestMethod]
        public void Early_Leave_Beyond_The_Grace_Is_Deducted_Like_Lateness()
        {
            // out at 15:40: 20 minutes early, beyond the 15-minute grace: the whole 20 minutes x 3 = 60 minutes
            DayResult r = AttendanceMath.Compute(Shift(8 * 60, 15 * 60 + 40));
            Assert.AreEqual(20, r.EarlyLeaveMinutes);
            Assert.AreEqual(60, r.EarlyLeaveDeductionMinutes);
            Assert.AreEqual(0, r.DelayDeductionMinutes);
            // exactly the grace (15) and inside it: nothing
            Assert.AreEqual(0, AttendanceMath.Compute(Shift(8 * 60, 15 * 60 + 45)).EarlyLeaveDeductionMinutes);
            Assert.AreEqual(0, AttendanceMath.Compute(Shift(8 * 60, 15 * 60 + 50)).EarlyLeaveMinutes);
            // 16 minutes early is beyond the grace
            Assert.AreEqual(48, AttendanceMath.Compute(Shift(8 * 60, 15 * 60 + 44)).EarlyLeaveDeductionMinutes);
        }

        [TestMethod]
        public void Early_Leave_Uses_The_Same_Coefficient_As_Lateness_And_The_Company_Switch()
        {
            DayInput day = Shift(8 * 60 + 30, 15 * 60 + 30); // 30 minutes late and 30 minutes early
            DayResult both = AttendanceMath.Compute(day);
            Assert.AreEqual(90, both.DelayDeductionMinutes);
            Assert.AreEqual(90, both.EarlyLeaveDeductionMinutes);

            day.DelayPenaltyFactor = 2m;
            Assert.AreEqual(60, AttendanceMath.Compute(day).EarlyLeaveDeductionMinutes);

            // the company switch off: the early leave is still reported, nothing is deducted for it; lateness is unaffected
            day.EarlyLeaveDeductionEnabled = false;
            DayResult off = AttendanceMath.Compute(day);
            Assert.AreEqual(30, off.EarlyLeaveMinutes);
            Assert.AreEqual(0, off.EarlyLeaveDeductionMinutes);
            Assert.AreEqual(60, off.DelayDeductionMinutes);
            Assert.IsTrue(new DayInput().EarlyLeaveDeductionEnabled, "starred default: on");
        }

        [TestMethod]
        public void Overtime_Counts_Only_Up_To_The_Approved_Request_And_The_Rest_Is_Reported()
        {
            // in 08:00, out 18:30: 150 minutes after the shift; the request of the day approved 60
            DayInput day = Shift(8 * 60, 18 * 60 + 30);
            day.OvertimeRequiresApproval = true;
            day.ApprovedOvertimeMinutes = 60;
            DayResult r = AttendanceMath.Compute(day);
            Assert.AreEqual(60, r.OvertimeMinutes);
            Assert.AreEqual(90, r.UnapprovedOvertimeMinutes);

            // no request at all: nothing counts and all of it is reported
            day.ApprovedOvertimeMinutes = 0;
            r = AttendanceMath.Compute(day);
            Assert.AreEqual(0, r.OvertimeMinutes);
            Assert.AreEqual(150, r.UnapprovedOvertimeMinutes);

            // an approval above what was worked counts what was worked
            day.ApprovedOvertimeMinutes = 240;
            r = AttendanceMath.Compute(day);
            Assert.AreEqual(150, r.OvertimeMinutes);
            Assert.AreEqual(0, r.UnapprovedOvertimeMinutes);

            // the company switch off: every minute counts (the monthly cap still applies at the close)
            day.OvertimeRequiresApproval = false;
            day.ApprovedOvertimeMinutes = 0;
            r = AttendanceMath.Compute(day);
            Assert.AreEqual(150, r.OvertimeMinutes);
            Assert.AreEqual(0, r.UnapprovedOvertimeMinutes);
            Assert.IsTrue(new DayInput().OvertimeRequiresApproval, "starred default: on");
        }

        [TestMethod]
        public void Holiday_Work_Follows_The_Overtime_Approval_Rule()
        {
            // a day off, 09:00-15:00 with the 30-minute break: 330 minutes worked
            DayInput day = Shift(9 * 60, 15 * 60, nonWorking: true);
            Assert.AreEqual(330, AttendanceMath.Compute(day).HolidayMinutes);

            day.OvertimeRequiresApproval = true;
            day.ApprovedOvertimeMinutes = 120;
            DayResult r = AttendanceMath.Compute(day);
            Assert.AreEqual(120, r.HolidayMinutes);
            Assert.AreEqual(210, r.UnapprovedOvertimeMinutes);

            // the daily maximum applies to holiday work like to overtime
            day.OvertimeRequiresApproval = false;
            day.MaxOvertimeMinutes = 240;
            Assert.AreEqual(240, AttendanceMath.Compute(day).HolidayMinutes);
        }

        [TestMethod]
        public void Holiday_Work_And_Overtime_Share_One_Monthly_Cap()
        {
            var days = new List<(DateTime Date, int OvertimeMinutes, int HolidayMinutes)>
            {
                (new DateTime(2026, 8, 1), 600, 0),   // 10 h overtime
                (new DateTime(2026, 8, 5), 0, 480),   // 8 h holiday work
                (new DateTime(2026, 8, 9), 300, 0),   // 5 h overtime
            };
            // cap 22 h = 1320: 600 + 480 + 240 of the last day; the last 60 minutes do not count
            var counted = AttendanceMath.MonthlyExtraWork(days, 1320);
            Assert.AreEqual(840, counted.OvertimeMinutes);
            Assert.AreEqual(480, counted.HolidayMinutes);
            Assert.AreEqual(1320, counted.OvertimeMinutes + counted.HolidayMinutes);

            // the order of the dates decides, not the order of the list
            var shuffled = new List<(DateTime Date, int OvertimeMinutes, int HolidayMinutes)> { days[2], days[1], days[0] };
            Assert.AreEqual(counted, AttendanceMath.MonthlyExtraWork(shuffled, 1320));

            // a cap of zero counts nothing; a big cap counts everything
            Assert.AreEqual((0, 0), AttendanceMath.MonthlyExtraWork(days, 0));
            Assert.AreEqual((900, 480), AttendanceMath.MonthlyExtraWork(days, 100000));
        }

        [TestMethod]
        public void Night_Window_Comes_From_The_Input_And_No_Window_Means_No_Night_Minutes()
        {
            DateTime date = new DateTime(2026, 8, 12);
            // equal values = no window configured (the rate-table keys are missing and the close refuses before this point)
            Assert.AreEqual(0, AttendanceMath.NightOverlap(date, date.AddHours(20), date.AddHours(23), 0, 0));
            // the window of the company keys: 22:00-06:00; 20:00-23:30 overlaps 90 minutes
            Assert.AreEqual(90, AttendanceMath.NightOverlap(date, date.AddHours(20), date.AddHours(23).AddMinutes(30), 22 * 60, 6 * 60));
        }

        // ------------------------------------------------------------------------------------------ sheet numbers, wage history, HR close

        [TestMethod]
        public void A_Sheet_Cannot_Exceed_The_Policy_Cap_With_Overtime_And_Holiday_Work_Together()
        {
            var n = new SheetNumbersDto { DaysWorked = 26, OvertimeHours = 15m, HolidayHours = 8m };
            Assert.IsNotNull(SheetNumbers.Validate(n, 1405, 8, 22m), "15 + 8 = 23 h is above the 22 h cap");
            n.HolidayHours = 7m;
            Assert.IsNull(SheetNumbers.Validate(n, 1405, 8, 22m));
            // the old 400-hour bound is gone: the cap is the policy's
            n.OvertimeHours = 100m;
            Assert.IsNotNull(SheetNumbers.Validate(n, 1405, 8, 22m));
            Assert.IsNull(SheetNumbers.Validate(n, 1405, 8, 120m));
            Assert.IsNotNull(SheetNumbers.Validate(null, 1405, 8, 22m));
        }

        [TestMethod]
        public void Sheet_Numbers_Compare_At_Two_Decimals_And_Snapshot_Is_Canonical()
        {
            var a = new SheetNumbersDto { DaysWorked = 26m, OvertimeHours = 4.001m };
            var b = new SheetNumbersDto { DaysWorked = 26.00m, OvertimeHours = 4.004m };
            Assert.IsTrue(SheetNumbers.Same(a, b));
            b.OvertimeHours = 4.1m;
            Assert.IsFalse(SheetNumbers.Same(a, b));
            string one = SheetNumbers.Snapshot(new[] { new KeyValuePair<int, SheetNumbersDto>(2, b), new KeyValuePair<int, SheetNumbersDto>(1, a) });
            string two = SheetNumbers.Snapshot(new[] { new KeyValuePair<int, SheetNumbersDto>(1, a), new KeyValuePair<int, SheetNumbersDto>(2, b) });
            Assert.AreEqual(one, two);
            Assert.AreEqual(PayrollHash.Hex(one), PayrollHash.Hex(two));
            Assert.AreNotEqual(PayrollHash.Hex(one), PayrollHash.Hex(one.Replace("26.00", "25.00")));
        }

        [TestMethod]
        public void The_Wage_In_Force_Comes_From_The_Approved_History()
        {
            var rows = new List<FnEmployeeWage>
            {
                new FnEmployeeWage { Id = 1, EmployeeId = 7, EffectiveFrom = P(1405, 1, 1), DailyWage = 1000m },
                new FnEmployeeWage { Id = 2, EmployeeId = 7, EffectiveFrom = P(1405, 6, 1), DailyWage = 1500m },
            };
            bool fromHistory;
            Assert.AreEqual(1000m, WageHistory.WageAt(rows, 9999m, P(1405, 5, 1), out fromHistory)); Assert.IsTrue(fromHistory);
            Assert.AreEqual(1500m, WageHistory.WageAt(rows, 9999m, P(1405, 6, 1), out fromHistory)); Assert.IsTrue(fromHistory);
            Assert.AreEqual(1500m, WageHistory.WageAt(rows, 9999m, P(1405, 9, 1), out _));
            // a month before the first row uses the first row
            Assert.AreEqual(1000m, WageHistory.WageAt(rows, 9999m, P(1404, 12, 1), out _));
            // no history yet: the cached current wage, and the caller can see that (BaseSource = CurrentWage)
            Assert.AreEqual(9999m, WageHistory.WageAt(new List<FnEmployeeWage>(), 9999m, P(1405, 5, 1), out fromHistory)); Assert.IsFalse(fromHistory);
            Assert.AreEqual(9999m, WageHistory.WageAt(null, 9999m, P(1405, 5, 1), out fromHistory)); Assert.IsFalse(fromHistory);
            Assert.AreEqual(P(1405, 6, 1), WageHistory.MonthStart(P(1405, 6, 17)));
        }

        [TestMethod]
        public void Payroll_Refuses_A_Month_Whose_Attendance_Close_Is_Not_Confirmed()
        {
            Assert.IsNotNull(HrClose.Refusal(1405, 8, null));
            StringAssert.Contains(HrClose.Refusal(1405, 8, null), "1405/08");
            Assert.IsNotNull(HrClose.Refusal(1405, 8, new OfAttendanceMonth { Year = 1405, Month = 8, Status = OfMonthStatus.Prepared }));
            Assert.IsNotNull(HrClose.Refusal(1405, 8, new OfAttendanceMonth { Year = 1405, Month = 8, Status = OfMonthStatus.Rejected }));
            Assert.IsNull(HrClose.Refusal(1405, 8, new OfAttendanceMonth { Year = 1405, Month = 8, Status = OfMonthStatus.Confirmed }));
            // the legacy value 0 is Confirmed: closes written before the two-step close stay valid
            Assert.AreEqual(OfMonthStatus.Confirmed, default(OfMonthStatus));
        }

        // ------------------------------------------------------------------------------------------ protected groups

        [TestMethod]
        public void A_Cost_Centre_And_Work_Order_Group_Under_Three_Payroll_Employees_Is_Protected()
        {
            var parts = new List<PayrollProtection.Part>
            {
                new PayrollProtection.Part { EmployeeId = 1, CostCenter = "CC1", WorkOrderId = 10 },                       // one employee: protected
                new PayrollProtection.Part { EmployeeId = 1, CostCenter = "CC1", WorkOrderId = 10 },                       // the same person twice is still one
                new PayrollProtection.Part { EmployeeId = 1, CostCenter = "CC1", WorkOrderId = 11 },
                new PayrollProtection.Part { EmployeeId = 2, CostCenter = "CC1", WorkOrderId = 11 },
                new PayrollProtection.Part { EmployeeId = 3, CostCenter = "CC1", WorkOrderId = 11 },                       // three: visible
                new PayrollProtection.Part { EmployeeId = 4, CostCenter = "CC2", WorkOrderId = 11 },
                new PayrollProtection.Part { EmployeeId = 5, CostCenter = "CC2", WorkOrderId = 11 },                       // two: protected
                new PayrollProtection.Part { EmployeeId = 6, CostCenter = null, WorkOrderId = 12 },                        // no cost centre: grouped as ""
            };
            HashSet<(string CostCenter, int WorkOrderId)> groups = PayrollProtection.ProtectedGroups(parts);
            Assert.IsTrue(groups.Contains(("CC1", 10)));
            Assert.IsFalse(groups.Contains(("CC1", 11)));
            Assert.IsTrue(groups.Contains(("CC2", 11)));
            Assert.IsTrue(groups.Contains(("", 12)));
            Assert.AreEqual(3, groups.Count);
            Assert.AreEqual(0, PayrollProtection.ProtectedGroups(new List<PayrollProtection.Part>()).Count);
        }

        // ------------------------------------------------------------------------------------------ a missing rate-table key refuses

        private static PayrollRates Rates(params (string Key, decimal Value)[] values) =>
            new PayrollRates(values.Select(v => new FnPayrollRateTable { Year = 0, Key = v.Key, Value = v.Value }), 1405);

        [TestMethod]
        public void A_Missing_Key_Refuses_With_A_Message_That_Names_It()
        {
            PayrollRates rates = Rates((PayrollKeys.MonthDays, 30m));
            Assert.IsNull(rates.Require(1405, PayrollKeys.MonthDays));
            string refusal = rates.Require(1405, PayrollKeys.MonthDays, PayrollKeys.YearDays, PayrollKeys.NightStart);
            Assert.IsNotNull(refusal);
            StringAssert.Contains(refusal, PayrollKeys.YearDays);
            StringAssert.Contains(refusal, PayrollKeys.NightStart);
            Assert.IsFalse(refusal.Contains(PayrollKeys.MonthDays), "a key that exists is not named");
            StringAssert.Contains(refusal, "1405");
            CollectionAssert.AreEquivalent(new[] { PayrollKeys.NightStart, PayrollKeys.YearDays }, rates.Missing.ToArray());
            // a year row wins over the all-years row
            var withYear = new PayrollRates(new[] { new FnPayrollRateTable { Year = 0, Key = "K", Value = 1m }, new FnPayrollRateTable { Year = 1405, Key = "K", Value = 2m } }, 1405);
            Assert.AreEqual(2m, withYear.Get("K"));
        }

        [TestMethod]
        public void The_Engine_Records_The_Key_It_Could_Not_Read_So_The_Run_Can_Refuse()
        {
            var rates = Rates(("DailyWorkHours", 8m)); // no PayrollMonthDays, no HolidayHoursPercent
            var items = new List<FnPayrollItem>
            {
                new FnPayrollItem { Code = "HOLIDAY", Title = "h", Kind = FnPayrollItemKind.Earning, Calculation = FnPayrollCalc.PerHour, Variant = "HOLIDAY", RateKey = PayrollKeys.HolidayPremium, Factor = 0.4m },
            };
            var result = PayrollEngine.Calculate(items, rates, new List<FnTaxBracket>(), 1405, 5, Input(wage: 800000m, holidayHours: 5m));
            Assert.IsTrue(rates.Missing.Contains(PayrollKeys.MonthDays));
            Assert.IsTrue(rates.Missing.Contains(PayrollKeys.HolidayPremium));
            Assert.AreEqual(0m, result.Gross, "nothing is paid with a guessed number");
        }

        [TestMethod]
        public void Rate_Table_Keys_Have_Ranges_And_Starred_Defaults_That_Need_Verification()
        {
            Assert.IsNull(PayrollKeys.Validate(PayrollKeys.MonthDays, 30m));
            Assert.IsNotNull(PayrollKeys.Validate(PayrollKeys.MonthDays, 40m));
            Assert.IsNotNull(PayrollKeys.Validate(PayrollKeys.MonthDays, 30.5m), "an integer key");
            Assert.IsNull(PayrollKeys.Validate(PayrollKeys.OvertimeRequestMaxHours, 4m));
            Assert.IsNotNull(PayrollKeys.Validate(PayrollKeys.OvertimeRequestMaxHours, 0m));
            Assert.IsNull(PayrollKeys.Validate("SomeOtherKey", 12345m), "keys outside R1f have no range here");
            foreach (FnPayrollRateTable seed in PayrollKeys.Seeds())
            {
                Assert.IsTrue(seed.Verify, seed.Key);
                Assert.AreEqual(0, seed.Year, seed.Key);
                Assert.IsFalse(string.IsNullOrWhiteSpace(seed.Source), seed.Key);
            }
            // the starred values of the brief
            decimal Default(string key) => PayrollKeys.Spec(key).Default;
            Assert.AreEqual(4m, Default(PayrollKeys.OvertimeRequestMaxHours));
            Assert.AreEqual(40m, Default(PayrollKeys.HolidayPremium));
            Assert.AreEqual(3m, Default(PayrollKeys.SickCompanyDays));
            Assert.AreEqual((decimal)PayrollKeys.SickCompanyThenSso, Default(PayrollKeys.SickPayBasis));
            Assert.AreEqual(30m, Default(PayrollKeys.MonthDays));
            Assert.AreEqual(360m, Default(PayrollKeys.YearDays));
            Assert.AreEqual(480m, Default(PayrollKeys.DefaultDayMinutes));
            Assert.AreEqual(22 * 60m, Default(PayrollKeys.NightStart));
            Assert.AreEqual(6 * 60m, Default(PayrollKeys.NightEnd));
        }

        // ------------------------------------------------------------------------------------------ the engine: holiday premium, sick days, minimum wage, wage in force

        private static PayrollInput Input(decimal wage, decimal holidayHours = 0m, decimal sickDays = 0m, decimal daysWorked = 26m, decimal? overrideWage = null) => new PayrollInput
        {
            Employee = new FnEmployee { HireDate = new DateTime(2020, 1, 1), DailyWage = wage, MaritalStatus = 1 },
            Sheet = new FnAttendanceSheet { DaysWorked = daysWorked, HolidayHours = holidayHours, SickLeaveDays = sickDays },
            DailyWage = overrideWage,
        };

        private static List<FnPayrollItem> BaseAndHoliday() => new List<FnPayrollItem>
        {
            new FnPayrollItem { Code = "BASE", Title = "پایه", Kind = FnPayrollItemKind.Earning, Calculation = FnPayrollCalc.PerDayWorked, Insurable = true, Taxable = true, Order = 10 },
            new FnPayrollItem { Code = "HOLIDAY", Title = "تعطیل‌کاری", Kind = FnPayrollItemKind.Earning, Calculation = FnPayrollCalc.PerHour, Variant = "HOLIDAY", RateKey = PayrollKeys.HolidayPremium, Factor = 0.4m, Order = 20 },
        };

        private static PayrollRates EngineRates(decimal holidayPercent = 40m, decimal sickBasis = 1m, decimal companyDays = 3m, decimal? minimumWage = null)
        {
            var values = new List<(string, decimal)>
            {
                (PayrollKeys.MonthDays, 30m), ("DailyWorkHours", 8m), (PayrollKeys.HolidayPremium, holidayPercent), (PayrollKeys.SickPayBasis, sickBasis), (PayrollKeys.SickCompanyDays, companyDays),
            };
            if (minimumWage.HasValue) values.Add((PayrollKeys.MinimumDailyWage, minimumWage.Value));
            return Rates(values.ToArray());
        }

        [TestMethod]
        public void The_Holiday_Premium_Is_Read_From_Its_Own_Rate_Key()
        {
            // 800,000 a day / 8 h = 100,000 an hour; 5 holiday hours at 40 % = 200,000
            var result = PayrollEngine.Calculate(BaseAndHoliday(), EngineRates(), new List<FnTaxBracket>(), 1405, 5, Input(800000m, holidayHours: 5m));
            Assert.AreEqual(200000m, result.Lines.Single(l => l.Code == "HOLIDAY").Amount);
            // finance changes the key: the same hours pay differently, no code change
            var changed = PayrollEngine.Calculate(BaseAndHoliday(), EngineRates(holidayPercent: 50m), new List<FnTaxBracket>(), 1405, 5, Input(800000m, holidayHours: 5m));
            Assert.AreEqual(250000m, changed.Lines.Single(l => l.Code == "HOLIDAY").Amount);
        }

        [TestMethod]
        public void Sick_Days_Are_Paid_By_The_Company_First_Then_By_Social_Security()
        {
            var items = BaseAndHoliday();
            // key 1: the company pays the first 3 days of a month; 5 sick days -> 2 are the social security's and are not paid or insured by the company
            var result = PayrollEngine.Calculate(items, EngineRates(), new List<FnTaxBracket>(), 1405, 5, Input(800000m, sickDays: 5m, daysWorked: 21m));
            Assert.AreEqual(2m, result.SickSsoDays);
            Assert.AreEqual(28m, result.DaysPaid);
            Assert.AreEqual(800000m * 28m, result.Lines.Single(l => l.Code == "BASE").Amount);
            // key 0: the company pays them all
            var company = PayrollEngine.Calculate(items, EngineRates(sickBasis: 0m), new List<FnTaxBracket>(), 1405, 5, Input(800000m, sickDays: 5m, daysWorked: 21m));
            Assert.AreEqual(0m, company.SickSsoDays);
            Assert.AreEqual(30m, company.DaysPaid);
            // key 2: social security from the first day
            var sso = PayrollEngine.Calculate(items, EngineRates(sickBasis: 2m), new List<FnTaxBracket>(), 1405, 5, Input(800000m, sickDays: 5m, daysWorked: 21m));
            Assert.AreEqual(5m, sso.SickSsoDays);
            Assert.AreEqual(25m, sso.DaysPaid);
            // within the company days nothing changes
            var few = PayrollEngine.Calculate(items, EngineRates(), new List<FnTaxBracket>(), 1405, 5, Input(800000m, sickDays: 3m, daysWorked: 23m));
            Assert.AreEqual(0m, few.SickSsoDays);
            Assert.AreEqual(30m, few.DaysPaid);
        }

        [TestMethod]
        public void Below_The_Minimum_Wage_Is_A_Warning_And_Never_A_Block()
        {
            var items = BaseAndHoliday();
            var below = PayrollEngine.Calculate(items, EngineRates(minimumWage: 1000000m), new List<FnTaxBracket>(), 1405, 5, Input(900000m));
            Assert.IsTrue(below.BelowMinimumWage);
            Assert.AreEqual(27000000m, below.MonthlyBase);
            Assert.AreEqual(30000000m, below.MinimumMonthly);
            Assert.AreEqual(900000m * 30m, below.Lines.Single(l => l.Code == "BASE").Amount, "the payslip is still calculated");
            Assert.IsFalse(PayrollEngine.Calculate(items, EngineRates(minimumWage: 1000000m), new List<FnTaxBracket>(), 1405, 5, Input(1000000m)).BelowMinimumWage);
            var unknown = PayrollEngine.Calculate(items, EngineRates(), new List<FnTaxBracket>(), 1405, 5, Input(900000m));
            Assert.IsTrue(unknown.MinimumWageUnknown);
            Assert.IsFalse(unknown.BelowMinimumWage);
        }

        [TestMethod]
        public void The_Run_Uses_The_Wage_In_Force_Not_The_Cached_One()
        {
            var items = BaseAndHoliday();
            // the cached wage on the employee is the new one (900,000); the month's approved history says 800,000
            var old = PayrollEngine.Calculate(items, EngineRates(), new List<FnTaxBracket>(), 1405, 5, Input(wage: 900000m, overrideWage: 800000m));
            Assert.AreEqual(800000m * 30m, old.Lines.Single(l => l.Code == "BASE").Amount);
            var current = PayrollEngine.Calculate(items, EngineRates(), new List<FnTaxBracket>(), 1405, 6, Input(wage: 900000m, overrideWage: 900000m));
            Assert.AreEqual(900000m * 30m, current.Lines.Single(l => l.Code == "BASE").Amount);
            // no override: the cached wage
            Assert.AreEqual(900000m * 30m, PayrollEngine.Calculate(items, EngineRates(), new List<FnTaxBracket>(), 1405, 6, Input(wage: 900000m)).Lines.Single(l => l.Code == "BASE").Amount);
        }

        // ------------------------------------------------------------------------------------------ culture independence of machine-readable texts

        private static List<AttendanceService.RawClock> Pushed() => new List<AttendanceService.RawClock>
        {
            new AttendanceService.RawClock { Code = "1001", Timestamp = new DateTime(2026, 8, 11, 8, 3, 0), Direction = OfClockDirection.In, Device = "A", Line = "1" },
            new AttendanceService.RawClock { Code = "1001", Timestamp = new DateTime(2026, 8, 11, 16, 5, 0), Direction = OfClockDirection.Out, Device = "A", Line = "2" },
            new AttendanceService.RawClock { Code = "0999", Timestamp = new DateTime(2026, 8, 11, 8, 30, 0), Direction = OfClockDirection.Unknown, Device = "A", Line = "3" },
        };

        [TestMethod]
        public void The_Push_Payload_Hash_Can_Be_Recomputed_From_The_File_In_Any_Culture_And_Any_Order()
        {
            // what the device owner computes with any tool: one canonical line per event, sorted ordinally, joined by a line feed, SHA-256 in lower-case hex
            string canonical = "0999|2026-08-11T08:30|0\n1001|2026-08-11T08:03|1\n1001|2026-08-11T16:05|2";
            string expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            var rows = Pushed();
            CultureInfo before = CultureInfo.CurrentCulture;
            int checkedCultures = 0;
            try
            {
                // the API runs with a Persian request culture (Persian calendar) - the hash must not depend on it, nor on the Hijri or Buddhist calendars of other cultures
                foreach (string name in new[] { "", "en-US", "fa-IR", "ar-SA", "th-TH" })
                {
                    CultureInfo culture;
                    try { culture = new CultureInfo(name); } catch (CultureNotFoundException) { continue; }
                    CultureInfo.CurrentCulture = culture;
                    Assert.AreEqual(expected, AttendanceService.PayloadHash(rows), "culture '" + name + "'");
                    checkedCultures++;
                }
            }
            finally { CultureInfo.CurrentCulture = before; }
            Assert.IsTrue(checkedCultures >= 1, "at least the invariant culture was checked");
            // the order of arrival does not change the hash; one changed direction does
            var reversed = Pushed(); reversed.Reverse();
            Assert.AreEqual(expected, AttendanceService.PayloadHash(reversed));
            var changed = Pushed(); changed[1].Direction = OfClockDirection.In;
            Assert.AreNotEqual(expected, AttendanceService.PayloadHash(changed));
        }

        [TestMethod]
        public void The_Leaver_Lockout_Text_Is_A_Persian_Date_Or_A_Fixed_Label_In_Any_Culture()
        {
            DateTimeOffset someDay = new DateTimeOffset(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Local));
            CultureInfo before = CultureInfo.CurrentCulture;
            try
            {
                foreach (string name in new[] { "", "en-US", "fa-IR", "ar-SA" })
                {
                    CultureInfo culture;
                    try { culture = new CultureInfo(name); } catch (CultureNotFoundException) { continue; }
                    CultureInfo.CurrentCulture = culture;
                    Assert.AreEqual("1405/08/06", GlService.LockoutText(someDay), "culture '" + name + "'");
                }
            }
            finally { CultureInfo.CurrentCulture = before; }
            Assert.AreEqual("بدون قفل", GlService.LockoutText(null));
            // the permanent lock is written with the maximum date, which no calendar could print: a fixed label instead
            Assert.AreEqual("بدون پایان (قفل دائم)", GlService.LockoutText(DateTimeOffset.MaxValue));
        }

        [TestMethod]
        public void A_Free_Text_Is_Cut_To_The_Length_Of_Its_Column_And_Null_Stays_Null()
        {
            // the in-memory test database accepts any length, SQL Server refuses a longer text: the services cut the note or reason before it is stored
            string longText = new string('ب', 1200), midText = new string('ب', 700);
            Assert.AreEqual(longText.Substring(0, 500), LedgerMath.Cap(longText, 500));
            Assert.AreEqual(longText.Substring(0, 1000), OfficeText.Cap(longText, 1000));
            Assert.AreEqual(midText, OfficeText.Cap(midText, 1000), "a text inside the limit is not changed");
            Assert.AreEqual(500, LedgerMath.Cap(midText, 500).Length);
            Assert.AreEqual("abc", LedgerMath.Cap("abc", 500));
            Assert.AreEqual("abc", OfficeText.Cap("abc", 3));
            Assert.AreEqual("ab", OfficeText.Cap("abc", 2));
            Assert.IsNull(LedgerMath.Cap(null, 500));
            Assert.IsNull(OfficeText.Cap(null, 1000));
        }
    }
}
