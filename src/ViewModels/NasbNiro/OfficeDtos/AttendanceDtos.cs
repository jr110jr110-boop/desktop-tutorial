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
        public int GraceMinutes { get; set; }
        public int? MaxOvertimeMinutesPerDay { get; set; }
        public bool OvertimeNeedsPreApproval { get; set; } = true;
        public int RoundingMinutes { get; set; }
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
        public decimal AbsenceDays { get; set; }
        public decimal MissionDays { get; set; }
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
