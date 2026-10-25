using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Production;

namespace ViewModels.NasbNiro.ProductionDtos
{
    // ------------------------------------------------------------------------------------------ terminal

    public class TerminalQuery : MfPageQuery
    {
        public int? WorkCenterId { get; set; }

        /// <summary>Job card (order number or order/sequence), serial number or label of a part.</summary>
        public string Code { get; set; }
    }

    public class TerminalOperationDto : OperationDto
    {
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public decimal OrderQuantity { get; set; }
        public string Unit { get; set; }
        public int? OrderPriority { get; set; }
        public string NeedDateText { get; set; }

        /// <summary>The signed-in operator has a running booking on this operation.</summary>
        public bool MyRunning { get; set; }

        public string MyRunningSinceText { get; set; }

        /// <summary>Start of the operator's running booking (ISO; the terminal timer counts from it).</summary>
        public DateTime? MyRunningSince { get; set; }
    }

    public class StartOperationDto
    {
        public int OperationId { get; set; }
        public MfBookingKind Kind { get; set; } = MfBookingKind.Run;
        public int? MachineId { get; set; }
        public string Note { get; set; }
    }

    public class PauseOperationDto
    {
        public int OperationId { get; set; }
        public int? ReasonCodeId { get; set; }
        public string Note { get; set; }
    }

    public class ReportQuantityDto
    {
        public int OperationId { get; set; }
        public decimal GoodQty { get; set; }
        public decimal ScrapQty { get; set; }
        public int? ScrapReasonCodeId { get; set; }
        public decimal ReworkQty { get; set; }
        public string Note { get; set; }
    }

    public class FinishOperationDto : ReportQuantityDto
    {
    }

    public class RequestQcDto
    {
        public int OperationId { get; set; }
        public string Note { get; set; }
    }

    public class MachineDownDto
    {
        public int MachineId { get; set; }
        public int? OperationId { get; set; }
        public int? ReasonCodeId { get; set; }
        public string Description { get; set; }
    }

    public class MaintenanceRequestQuery : MfPageQuery
    {
        public int? MachineId { get; set; }
        public MfMaintenanceRequestStatus? Status { get; set; }
    }

    public class MaintenanceRequestDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public int? WorkCenterId { get; set; }
        public int? OperationId { get; set; }
        public int? ReasonCodeId { get; set; }
        public int ReportedByUserId { get; set; }
        public string ReportedByName { get; set; }
        public DateTime ReportedAt { get; set; }
        public string ReportedAtText { get; set; }
        public string Description { get; set; }
        public MfMaintenanceRequestStatus Status { get; set; }
    }

    // ------------------------------------------------------------------------------------------ bookings

    public class BookingQuery : MfPageQuery
    {
        public int? OperationId { get; set; }
        public int? OrderId { get; set; }
        public int? EmployeeId { get; set; }
        public int? WorkCenterId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public bool? RunningOnly { get; set; }
    }

    public class BookingDto
    {
        public int Id { get; set; }
        public int OperationId { get; set; }
        public string OrderNumber { get; set; }
        public string JobCard { get; set; }
        public string OperationTitle { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public int? MachineId { get; set; }
        public string MachineTitle { get; set; }
        public MfBookingKind Kind { get; set; }
        public DateTime StartAt { get; set; }
        public string StartAtText { get; set; }
        public DateTime? EndAt { get; set; }
        public string EndAtText { get; set; }
        public int Minutes { get; set; }
        public decimal Hours { get; set; }
        public decimal GoodQty { get; set; }
        public decimal ScrapQty { get; set; }
        public int? ScrapReasonCodeId { get; set; }
        public decimal ReworkQty { get; set; }
        public int? ReasonCodeId { get; set; }
        public string Note { get; set; }
        public MfBookingSource Source { get; set; }
        public string SupervisorReason { get; set; }
        public string EditReason { get; set; }
        public bool Running { get; set; }

        /// <summary>The day of the booking is over: only the shop supervisor can still change it, with a reason.</summary>
        public bool DayClosed { get; set; }

        /// <summary>The booking is costed in the accounting (daily cost posting): it cannot be changed any more.</summary>
        public bool CostPosted { get; set; }
    }

    /// <summary>A completed booking made by a supervisor for somebody else (reason required).</summary>
    public class SaveBookingDto
    {
        public int OperationId { get; set; }
        public int EmployeeId { get; set; }
        public MfBookingKind Kind { get; set; } = MfBookingKind.Run;
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public int? MachineId { get; set; }
        public decimal GoodQty { get; set; }
        public decimal ScrapQty { get; set; }
        public int? ScrapReasonCodeId { get; set; }
        public decimal ReworkQty { get; set; }
        public int? ReasonCodeId { get; set; }
        public string Note { get; set; }
        public string Reason { get; set; }
    }

    public class EditBookingDto
    {
        public int Id { get; set; }
        public MfBookingKind? Kind { get; set; }
        public DateTime? StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public int? MachineId { get; set; }
        public decimal? GoodQty { get; set; }
        public decimal? ScrapQty { get; set; }
        public int? ScrapReasonCodeId { get; set; }
        public decimal? ReworkQty { get; set; }
        public string Note { get; set; }
        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ attendance versus booking

    public class AttendanceDifferenceQuery
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int? EmployeeId { get; set; }

        /// <summary>More employees to check (people who may be present without any booking).</summary>
        public List<int> EmployeeIds { get; set; } = new List<int>();

        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;

        /// <summary>Differences up to this many minutes are not listed.</summary>
        public int ToleranceMinutes { get; set; } = 15;
    }

    public class AttendanceDifferenceDto
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public DateTime Date { get; set; }
        public string DateText { get; set; }
        public decimal AttendanceHours { get; set; }
        public decimal BookedHours { get; set; }

        /// <summary>Attendance minus booked hours.</summary>
        public decimal DifferenceHours { get; set; }

        /// <summary>Unbooked (present but not booked), OverBooked (booked more than present) or NoAttendance (booked on a day without attendance).</summary>
        public string Kind { get; set; }

        public string Message { get; set; }
    }
}
