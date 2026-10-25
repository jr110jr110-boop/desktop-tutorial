using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Office;

namespace ViewModels.NasbNiro.OfficeDtos
{
    // ------------------------------------------------------------------------------------------ shifts

    public class ShiftDto
    {
        public int Id { get; set; }
        public string Title { get; set; }

        /// <summary>"HH:mm".</summary>
        public string Start { get; set; }
        public string End { get; set; }
        public int BreakMinutes { get; set; }
        public string NightStart { get; set; }
        public string NightEnd { get; set; }

        /// <summary>Weekday numbers of the working days: 0 = Sunday ... 6 = Saturday (Iran: 6, 0, 1, 2, 3 for Saturday to Wednesday).</summary>
        public List<int> WorkDays { get; set; } = new List<int>();

        public bool IsActive { get; set; }
        public bool CrossesMidnight { get; set; }

        /// <summary>Planned working minutes of a day: end - start - break.</summary>
        public int ScheduledMinutes { get; set; }
    }

    public class SaveShiftDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public int BreakMinutes { get; set; }
        public string NightStart { get; set; } = "22:00";
        public string NightEnd { get; set; } = "06:00";
        public List<int> WorkDays { get; set; } = new List<int>();
        public bool IsActive { get; set; } = true;
    }

    public class ShiftAssignmentDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string PersonnelCode { get; set; }
        public int ShiftId { get; set; }
        public string ShiftTitle { get; set; }
        public DateTime FromDate { get; set; }
        public string FromDateText { get; set; }
        public DateTime? ToDate { get; set; }
        public string ToDateText { get; set; }
    }

    public class SaveShiftAssignmentDto
    {
        public int EmployeeId { get; set; }
        public int ShiftId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class ShiftAssignmentQuery : OfficePageQuery
    {
        public int? EmployeeId { get; set; }
        public int? ShiftId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ devices and clock events

    public class DeviceDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Location { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset? LastPushAt { get; set; }
        public string LastPushAtText { get; set; }
    }

    public class SaveDeviceDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Location { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>Issue a new key for an existing device (the old key stops working).</summary>
        public bool RotateKey { get; set; }
    }

    /// <summary>The key is shown once; only its hash is stored.</summary>
    public class DeviceKeyDto
    {
        public DeviceDto Device { get; set; }
        public string ApiKey { get; set; }
    }

    public class ClockQuery : OfficePageQuery
    {
        public int? EmployeeId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public OfClockSource? Source { get; set; }
    }

    public class ClockEventDto
    {
        public long Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string PersonnelCode { get; set; }
        public DateTime Timestamp { get; set; }
        public string TimestampText { get; set; }
        public string DeviceCode { get; set; }
        public OfClockDirection Direction { get; set; }
        public OfClockSource Source { get; set; }
        public string Reason { get; set; }
        public string CreatedByName { get; set; }
    }

    public class ManualClockDto
    {
        public int EmployeeId { get; set; }
        public DateTime Timestamp { get; set; }
        public OfClockDirection Direction { get; set; }

        /// <summary>Required: manual events are audited.</summary>
        public string Reason { get; set; }
    }

    public class ClockRowDto
    {
        /// <summary>Card or personnel code.</summary>
        public string Code { get; set; }

        /// <summary>Persian (1405/05/26) or ISO date; with Time.</summary>
        public string Date { get; set; }
        public string Time { get; set; }
        public string Device { get; set; }
        public string Direction { get; set; }
    }

    public class ImportClockDto
    {
        public string DeviceCode { get; set; }

        /// <summary>Rows read from the Excel/CSV file by the front.</summary>
        public List<ClockRowDto> Rows { get; set; } = new List<ClockRowDto>();

        /// <summary>Or the file as CSV text: code, date, time, device, direction.</summary>
        public string Csv { get; set; }
    }

    public class ClockImportResultDto
    {
        public int Imported { get; set; }
        public int Duplicates { get; set; }

        /// <summary>Rows whose personnel number matches nobody: reported, never created.</summary>
        public int Unknown { get; set; }

        /// <summary>The distinct unknown personnel numbers (at most 200).</summary>
        public List<string> UnknownCodes { get; set; } = new List<string>();

        /// <summary>Rows read from the file (Excel import).</summary>
        public int RowsRead { get; set; }

        /// <summary>Events refused: in the future, or in a month whose payroll is approved.</summary>
        public int Rejected { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class DevicePushEventDto
    {
        public string EmployeeCode { get; set; }
        public DateTime Timestamp { get; set; }
        public OfClockDirection Direction { get; set; }
    }

    public class DevicePushDto
    {
        public List<DevicePushEventDto> Events { get; set; } = new List<DevicePushEventDto>();
    }

    public class DevicePushResultDto
    {
        public int Accepted { get; set; }
        public int Duplicates { get; set; }
        public int Rejected { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------------------------------ policy, days, month

    public class AttendancePolicyDto
    {
        /// <summary>Delay up to this many minutes is allowed (company: 15). Beyond it the whole delay is deducted.</summary>
        public int GraceMinutes { get; set; } = 15;
        public int? MaxOvertimeMinutesPerDay { get; set; }
        public bool OvertimeNeedsPreApproval { get; set; }
        public int RoundingMinutes { get; set; }

        /// <summary>Overtime above this many minutes a month is not counted (company: 1320 = 22 hours).</summary>
        /// <summary>Null on a save = keep the stored value (an older client that does not know the field must not reset it).</summary>
        public int? MonthlyOvertimeCapMinutes { get; set; }

        /// <summary>Delay beyond the grace is deducted as delay minutes x this coefficient (company: 3). Null on a save = keep the stored value.</summary>
        public decimal? DelayPenaltyFactor { get; set; }
    }

    public class ComputeDaysDto
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int? EmployeeId { get; set; }
    }

    public class ComputeResultDto
    {
        public int Days { get; set; }
        public int Employees { get; set; }
        public int SkippedLocked { get; set; }
        public int EmployeesWithoutShift { get; set; }
    }

    public class DayQuery : OfficePageQuery
    {
        public int? EmployeeId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public OfDayStatus? Status { get; set; }
    }

    public class AttendanceDayDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string PersonnelCode { get; set; }
        public DateTime Date { get; set; }
        public string DateText { get; set; }
        public int? ShiftId { get; set; }
        public string ShiftTitle { get; set; }
        public OfDayStatus Status { get; set; }
        public bool NonWorkingDay { get; set; }
        public DateTime? FirstIn { get; set; }
        public DateTime? LastOut { get; set; }
        public int WorkedMinutes { get; set; }
        public int DelayMinutes { get; set; }

        /// <summary>Delay minutes x the coefficient (0 inside the grace).</summary>
        public int DelayDeductionMinutes { get; set; }
        public int EarlyLeaveMinutes { get; set; }
        public int OvertimeMinutes { get; set; }
        public int NightMinutes { get; set; }
        public int HolidayMinutes { get; set; }
        public int AbsenceDays { get; set; }
        public int UnpaidLeaveDays { get; set; }
        public decimal PaidLeaveDays { get; set; }
        public decimal SickLeaveDays { get; set; }
        public int MissionDays { get; set; }
        public string Note { get; set; }
    }

    public class CloseMonthDto
    {
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>Days with a single punch count as absence (otherwise the close is refused until HR fixes them).</summary>
        public bool AcceptIncompleteAsAbsent { get; set; }
    }

    public class SheetRowDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public string PersonnelCode { get; set; }
        public decimal DaysWorked { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal NightHours { get; set; }
        public decimal HolidayHours { get; set; }
        public decimal PaidLeaveDays { get; set; }
        public decimal SickLeaveDays { get; set; }
        public decimal UnpaidLeaveDays { get; set; }

        /// <summary>Absence days for payroll: real absence plus the days deducted for lateness (LateDeductionDays), which reduce the paid days exactly once.</summary>
        public decimal AbsenceDays { get; set; }
        public decimal MissionDays { get; set; }

        /// <summary>Overtime hours as worked before the monthly cap (OvertimeHours is the counted value, at most the policy cap).</summary>
        public decimal OvertimeWorkedHours { get; set; }

        /// <summary>Total delay in hours (beyond the grace, as counted per day).</summary>
        public decimal DelayHours { get; set; }

        /// <summary>Deducted lateness: delay x coefficient, in hours.</summary>
        public decimal DelayDeductionHours { get; set; }

        /// <summary>The part of AbsenceDays that comes from the lateness deduction (deducted minutes per day / scheduled minutes of the shift, at most 1 a day).</summary>
        public decimal LateDeductionDays { get; set; }
    }

    public class CloseMonthResultDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Sheets { get; set; }
        public int SkippedLocked { get; set; }
        public int IncompleteDays { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<SheetRowDto> Rows { get; set; } = new List<SheetRowDto>();
    }

    public class MonthStatusDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public bool Closed { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string ClosedAtText { get; set; }
        public int Sheets { get; set; }

        /// <summary>A payroll run of the month is approved: the month is locked here too.</summary>
        public bool LockedByPayroll { get; set; }

        /// <summary>The saved attendance sheet of the month (one row per employee), so a closed month can be read and printed again.</summary>
        public List<SheetRowDto> Rows { get; set; } = new List<SheetRowDto>();
    }

    /// <summary>Net hours present per person and day, for the production module's time-booking check.</summary>
    public class WorkedHoursQuery
    {
        public List<int> EmployeeIds { get; set; } = new List<int>();
        public DateTime From { get; set; }
        public DateTime To { get; set; }
    }

    public class WorkedHoursDto
    {
        public int EmployeeId { get; set; }
        public DateTime Date { get; set; }
        public int WorkedMinutes { get; set; }
        public decimal WorkedHours { get; set; }
    }

    // ------------------------------------------------------------------------------------------ leave, mission, overtime

    public class LeaveQuery : OfficePageQuery
    {
        public int? EmployeeId { get; set; }
        public OfLeaveStatus? Status { get; set; }
        public OfLeaveKind? Kind { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public bool? MineOnly { get; set; }
        public bool? WaitingForMe { get; set; }
    }

    public class LeaveDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public OfLeaveKind Kind { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string FromDateText { get; set; }
        public string ToDateText { get; set; }
        public string FromTime { get; set; }
        public string ToTime { get; set; }
        public decimal? Hours { get; set; }
        public OfClockDirection? Direction { get; set; }
        public decimal Days { get; set; }
        public string Reason { get; set; }
        public Guid? AttachmentDocumentId { get; set; }
        public OfLeaveStatus Status { get; set; }
        public int? ApproverPositionId { get; set; }
        public string ApproverPositionTitle { get; set; }
        public string ApprovedByName { get; set; }
        public bool ApprovedOnBehalf { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApproverComment { get; set; }
        public string RecordedByName { get; set; }
        public DateTimeOffset? RecordedAt { get; set; }
        public string RejectReason { get; set; }
        public List<string> AllowedActions { get; set; } = new List<string>();
        public string RowVersion { get; set; }
    }

    public class SaveLeaveDto
    {
        public int? Id { get; set; }

        /// <summary>HR only: a request written for somebody else. Others always write for themselves.</summary>
        public int? EmployeeId { get; set; }
        public OfLeaveKind Kind { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string FromTime { get; set; }
        public string ToTime { get; set; }
        public decimal? Hours { get; set; }
        public OfClockDirection? Direction { get; set; }
        public string Reason { get; set; }
        public Guid? AttachmentDocumentId { get; set; }

        /// <summary>Send for approval at once.</summary>
        public bool Submit { get; set; }
        public string RowVersion { get; set; }
    }

    public class LeaveActionDto
    {
        public int Id { get; set; }

        /// <summary>submit, approve, record, reject, cancel.</summary>
        public string Action { get; set; }

        public string Comment { get; set; }
        public int? ActingPositionId { get; set; }
        public string RowVersion { get; set; }
    }

    public class LeaveBalanceDto
    {
        public int EmployeeId { get; set; }
        public int Year { get; set; }

        /// <summary>From the payroll leave balance (FnLeaveBalance), which payroll updates when it approves a month.</summary>
        public decimal CarriedForward { get; set; }
        public decimal Entitled { get; set; }
        public decimal Used { get; set; }
        public int LastMonth { get; set; }
        public decimal EntitlementRate { get; set; }

        /// <summary>Entitlement of the months payroll has not booked yet, up to the month of the leave.</summary>
        public decimal ProjectedEntitlement { get; set; }

        /// <summary>Approved or recorded leave of months payroll has not booked yet.</summary>
        public decimal PendingDays { get; set; }

        /// <summary>CarriedForward + Entitled - Used + ProjectedEntitlement - PendingDays.</summary>
        public decimal Available { get; set; }
    }
}

namespace ViewModels.NasbNiro.OfficeDtos
{
    // ------------------------------------------------------------------------------------------ Excel import of the time-attendance software (PW Kara)

    /// <summary>
    /// Which column of the Excel export holds what. A column is a letter (A, B ...) or the text of its header cell (when HeaderRow is set).
    /// Date and time are either two columns or one date-time column. ★ Awaiting a real PW Kara export: the mapping is deliberately free.
    /// </summary>
    public class ClockFileMappingDto
    {
        /// <summary>Worksheet name; empty = the first sheet.</summary>
        public string Sheet { get; set; }

        /// <summary>1-based row of the headings; 0 = the file has no heading row.</summary>
        public int HeaderRow { get; set; } = 1;

        /// <summary>First data row (1-based); 0 = the row after the heading row.</summary>
        public int FirstDataRow { get; set; }

        /// <summary>Personnel number or card number (matched with the personnel code of the employee; leading zeros are ignored).</summary>
        public string CodeColumn { get; set; }
        public string DateColumn { get; set; }
        public string TimeColumn { get; set; }

        /// <summary>
        /// Order of a written date: "YMD" (1405/05/20 or 2026-08-11), "DMY" (20/05/1405) or "MDY" (05/20/2026). A date that starts with a four digit year is read the same way whatever is chosen;
        /// a year-last date such as 05/06/2026 is refused as ambiguous while no order is chosen. The preview suggests YMD for year-first dates and DMY otherwise ★.
        /// </summary>
        public string DateOrder { get; set; }

        /// <summary>One column with date and time together (used when DateColumn and TimeColumn are empty).</summary>
        public string DateTimeColumn { get; set; }

        /// <summary>Optional: entry/exit column.</summary>
        public string DirectionColumn { get; set; }

        /// <summary>Texts of the direction column that mean entry (comma separated); empty: ورود, in, i, 1.</summary>
        public string InValues { get; set; }

        /// <summary>Texts that mean exit (comma separated); empty: خروج, out, o, 2.</summary>
        public string OutValues { get; set; }

        /// <summary>Optional: device (a code or title of a registered device is mapped to the device code).</summary>
        public string DeviceColumn { get; set; }

        /// <summary>Optional: site / place column (used as the device text when there is no device column).</summary>
        public string SiteColumn { get; set; }

        /// <summary>Device code stored when the file has no device or site column (for example US10C-HQ).</summary>
        public string DefaultDeviceCode { get; set; }
    }

    public class ClockImportProfileDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public ClockFileMappingDto Mapping { get; set; } = new ClockFileMappingDto();
    }

    public class SaveClockImportProfileDto
    {
        public int? Id { get; set; }
        public string Name { get; set; }
        public ClockFileMappingDto Mapping { get; set; } = new ClockFileMappingDto();

        /// <summary>Removes the profile (id required).</summary>
        public bool Delete { get; set; }
    }

    public class ClockFileColumnDto
    {
        /// <summary>Column letter: A, B, ...</summary>
        public string Letter { get; set; }

        /// <summary>Text of the heading row (null without heading).</summary>
        public string Header { get; set; }
        public List<string> Samples { get; set; } = new List<string>();

        /// <summary>For each sample: the Persian date it is read as ("1405/07/11"), "مبهم" when its order is ambiguous, "" when it is no date. Read with the suggested order.</summary>
        public List<string> SampleDates { get; set; } = new List<string>();
    }

    public class ClockFilePreviewDto
    {
        public List<string> Sheets { get; set; } = new List<string>();
        public string Sheet { get; set; }
        public int RowCount { get; set; }
        public List<ClockFileColumnDto> Columns { get; set; } = new List<ClockFileColumnDto>();

        /// <summary>The first rows as text (index = column).</summary>
        public List<List<string>> Rows { get; set; } = new List<List<string>>();

        /// <summary>A guessed mapping from the heading texts (Persian and English); the admin confirms or changes it.</summary>
        public ClockFileMappingDto Suggested { get; set; }
    }

    /// <summary>The Excel import form (the file travels beside it as multipart).</summary>
    public class ClockFileImportDto
    {
        /// <summary>A saved profile; or Mapping below.</summary>
        public int? ProfileId { get; set; }
        public ClockFileMappingDto Mapping { get; set; }

        /// <summary>Save the mapping under this name as a profile (new or the same name replaced).</summary>
        public string SaveProfileAs { get; set; }
    }
}
