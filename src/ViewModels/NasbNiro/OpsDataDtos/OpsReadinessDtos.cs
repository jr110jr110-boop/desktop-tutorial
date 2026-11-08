using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.OpsDataDtos
{
    public class ReadinessOffenderDto
    {
        public string Text { get; set; }
        public string Link { get; set; }
    }

    public class ReadinessItemDto
    {
        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>OK, Missing, Partial, NotApplicable.</summary>
        public string Status { get; set; }
        public bool Hard { get; set; }
        public string Severity { get; set; }
        public string Owner { get; set; }
        public string OwnerTitle { get; set; }
        public int Total { get; set; }
        public int MissingCount { get; set; }
        public List<ReadinessOffenderDto> Offenders { get; set; } = new List<ReadinessOffenderDto>();
        public DateTime? DueDate { get; set; }
        public string DueDateText { get; set; }
        public bool Overdue { get; set; }
    }

    public class ReadinessDto
    {
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public int Percent { get; set; }
        public int HardMissing { get; set; }
        public int SoftMissing { get; set; }

        /// <summary>No hard item is missing: the baseline may be created.</summary>
        public bool Ready { get; set; }

        /// <summary>An exception approved by the PMO and the CEO lets the baseline pass.</summary>
        public bool HasApprovedException { get; set; }
        public List<ReadinessItemDto> Items { get; set; } = new List<ReadinessItemDto>();
        public string Message { get; set; }
        public int? PendingExceptionId { get; set; }
    }

    public class ReadinessProjectDto
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public string ProductCode { get; set; }
        public int Percent { get; set; }
        public int HardMissing { get; set; }
        public int SoftMissing { get; set; }
        public int OpenObligations { get; set; }
        public int OverdueObligations { get; set; }
        public string Link { get; set; }
    }

    public class ReadinessSummaryDto
    {
        public DateTime AsOf { get; set; }
        public int Projects { get; set; }
        public int AveragePercent { get; set; }
        public int OpenObligations { get; set; }
        public int OverdueObligations { get; set; }
        public List<ReadinessProjectDto> Items { get; set; } = new List<ReadinessProjectDto>();
    }

    public class ReadinessExceptionDto
    {
        public int Id { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int ProductId { get; set; }
        public string ItemCodes { get; set; }
        public string Justification { get; set; }
        public string RequestedByName { get; set; }
        public int RequestedByUserId { get; set; }
        public string RequestedAtText { get; set; }

        /// <summary>Pending, Approved, Rejected, Used.</summary>
        public string Status { get; set; }
        public string StatusTitle { get; set; }
        public string PmoByName { get; set; }
        public string PmoAtText { get; set; }
        public string CeoByName { get; set; }
        public string CeoAtText { get; set; }
        public string RejectReason { get; set; }
        public bool CanDecide { get; set; }
    }

    public class RequestReadinessExceptionDto
    {
        public int WorkOrderId { get; set; }
        public int ProductId { get; set; }
        public string Justification { get; set; }
    }

    public class DecideReadinessExceptionDto
    {
        public int Id { get; set; }
        public bool Approve { get; set; }
        public string Reason { get; set; }
    }

    public class ReadinessObligationDto
    {
        public int Id { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public string ItemCode { get; set; }
        public string Title { get; set; }
        public string OwnerTitle { get; set; }
        public bool Hard { get; set; }
        public int MissingCount { get; set; }
        public string Detail { get; set; }
        public DateTime DueDate { get; set; }
        public string DueDateText { get; set; }
        public bool Overdue { get; set; }
        public bool Closed { get; set; }
    }
}
