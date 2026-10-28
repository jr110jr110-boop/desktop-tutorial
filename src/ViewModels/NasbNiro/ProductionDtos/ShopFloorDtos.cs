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

        /// <summary>R1p D43: the work centre has machines and requires one: the start is refused without <c>machineId</c> (a waiting booking excepted).</summary>
        public bool MachineRequired { get; set; }

        /// <summary>The signed-in operator has a booking (running or ended) on this operation, so he may report quantities and finish it.</summary>
        public bool HasMyBooking { get; set; }
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

        /// <summary>
        /// R1p D6/D33: the serial numbers of the scrapped pieces, one per line, for a serial-tracked item when only a part of the order's planned serials is scrapped (the scrap of every planned piece needs none).
        /// They must be among the planned serials of the order and as many as <see cref="ScrapQty"/>.
        /// </summary>
        public string ScrapSerials { get; set; }
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
        /// <summary>0 with <see cref="EmployeeHidden"/> when the reader may not see who worked (only PRODUCTIONMANAGER, SHOPSUPERVISOR, PAYROLL, FINANCEMANAGER, CEO and the employee himself do).</summary>
        public int EmployeeId { get; set; }

        public string EmployeeName { get; set; }

        /// <summary>R1p D1: the person behind the hours is hidden from this reader; the hours per work centre stay visible.</summary>
        public bool EmployeeHidden { get; set; }

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

        /// <summary>R1p D39: an extra booking added to an operation of a completed order (the original bookings are frozen).</summary>
        public bool IsCorrection { get; set; }

        public int? CorrectsBookingId { get; set; }

        /// <summary>R1p D39: a correction that no production manager (other than its creator) has approved yet; it is in no cost run, hour report or OEE until then.</summary>
        public bool CorrectionPending { get; set; }

        public string CorrectionApprovedAtText { get; set; }

        /// <summary>Concurrency token for bookings/edit.</summary>
        public string RowVersion { get; set; }
    }

    /// <summary>R1p D1: booked hours per work centre and day, without a name (the reader without the right to see names uses this).</summary>
    public class BookingHoursQuery
    {
        public int? WorkCenterId { get; set; }
        public int? OrderId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public class BookingHoursDto
    {
        public int? WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public string WorkCenterTitle { get; set; }
        public DateTime Date { get; set; }
        public string DateText { get; set; }
        public int Bookings { get; set; }
        public decimal Hours { get; set; }
    }

    /// <summary>
    /// R1p D39: the bookings of a completed (or closed) order are frozen. A shop supervisor adds an extra booking with a reason; it carries <c>IsCorrection</c>, never changes an original booking and
    /// adds hours only (a wrong booking is neutralised by finance, not by the shop).
    /// </summary>
    public class SaveCorrectionBookingDto
    {
        public int OperationId { get; set; }
        public int EmployeeId { get; set; }
        public MfBookingKind Kind { get; set; } = MfBookingKind.Run;
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public int? MachineId { get; set; }

        /// <summary>The frozen booking this one corrects, when it corrects one.</summary>
        public int? CorrectsBookingId { get; set; }

        public string Note { get; set; }
        public string Reason { get; set; }
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

        /// <summary>R1p D6/D33: the serial numbers of the scrapped pieces, one per line (see <see cref="ReportQuantityDto.ScrapSerials"/>).</summary>
        public string ScrapSerials { get; set; }
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

        /// <summary>R1p D6/D33: the serial numbers of the pieces scrapped by the increase of <see cref="ScrapQty"/>, one per line (see <see cref="ReportQuantityDto.ScrapSerials"/>).</summary>
        public string ScrapSerials { get; set; }
    }

    /// <summary>
    /// R1p D39: the approval of a correction booking by a production manager other than its creator (a correction counts in no cost run, hour report or OEE before that), or its rejection with a reason.
    /// </summary>
    public class ApproveCorrectionDto
    {
        public int Id { get; set; }

        /// <summary>True (default) approves; false rejects the correction (a reason is then required) and removes it.</summary>
        public bool Approve { get; set; } = true;

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
