using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;

namespace ViewModels.NasbNiro.ProductionDtos
{
    public class OrderQuery : MfPageQuery
    {
        public MfOrderStatus? Status { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public int? ItemId { get; set; }

        /// <summary>Only the orders that are open (draft, released or in progress).</summary>
        public bool? OpenOnly { get; set; }

        /// <summary>Only the orders the last scheduler run found late against their need date.</summary>
        public bool? LateOnly { get; set; }
    }

    public class OrderComponentDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
    }

    public class OrderListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int ContractId { get; set; }
        public int? ProcessId { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public MfOrderStatus Status { get; set; }
        public int? RoutingRevisionId { get; set; }

        /// <summary>1 = most urgent: CPM total float ascending, then the need date.</summary>
        public int? Priority { get; set; }

        public int? TotalFloatDays { get; set; }

        /// <summary>Late finish of the linked project activity, otherwise the date of the work order.</summary>
        public DateTime? NeedDate { get; set; }

        public string NeedDateText { get; set; }
        public DateTime? PlannedStart { get; set; }
        public string PlannedStartText { get; set; }
        public DateTime? PlannedFinish { get; set; }
        public string PlannedFinishText { get; set; }
        public string ActualStartText { get; set; }
        public string ActualFinishText { get; set; }
        public int OperationCount { get; set; }
        public int OperationsDone { get; set; }
        public bool AutoDrafted { get; set; }

        /// <summary>Result of the last scheduler run: the forecast finish is after the need date.</summary>
        public bool IsLate { get; set; }

        public int DaysLate { get; set; }
        public string KitReadyDateText { get; set; }
        public bool KitShortage { get; set; }
        public string ScheduleNote { get; set; }
        public string ScheduledAtText { get; set; }
    }

    public class OrderDto : OrderListDto
    {
        public int? BomId { get; set; }
        public int? ActivityId { get; set; }
        public string SerialsPlanned { get; set; }
        public int? KitRequestId { get; set; }
        public string KitNumber { get; set; }

        /// <summary>The material requests of the kit are fully issued.</summary>
        public bool KitIssued { get; set; }

        public string KitOverrideReason { get; set; }
        public bool ReleaseOverride { get; set; }
        public string ReleaseOverrideReason { get; set; }
        public string ShortageText { get; set; }
        public string ReleasedByName { get; set; }
        public string ReleasedAtText { get; set; }
        public int? ProductionReceiptId { get; set; }
        public string ProductionReceiptNumber { get; set; }
        public string CancelReason { get; set; }
        public string Note { get; set; }
        public List<OrderComponentDto> Components { get; set; } = new List<OrderComponentDto>();
        public List<OperationDto> Operations { get; set; } = new List<OperationDto>();

        /// <summary>The computed release checks (only while the order is a draft).</summary>
        public ReleaseCheckResultDto Release { get; set; }

        public string RowVersion { get; set; }
    }

    public class OperationQuery : MfPageQuery
    {
        /// <summary>One operation (the inbox links of QC requests point to an operation).</summary>
        public int? Id { get; set; }
        public int? OrderId { get; set; }
        public int? WorkCenterId { get; set; }
        public MfOperationStatus? Status { get; set; }
    }

    public class OperationDto
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string OrderNumber { get; set; }

        /// <summary>Job card code: order number / sequence.</summary>
        public string JobCard { get; set; }

        public int Sequence { get; set; }
        public string Title { get; set; }
        public int? WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public string WorkCenterTitle { get; set; }
        public decimal SetupHoursPlanned { get; set; }
        public decimal RunHoursPlanned { get; set; }
        public DateTime? PlannedStart { get; set; }
        public string PlannedStartText { get; set; }
        public DateTime? PlannedFinish { get; set; }
        public string PlannedFinishText { get; set; }
        public MfOperationStatus Status { get; set; }
        public decimal GoodQty { get; set; }
        public decimal ScrapQty { get; set; }
        public decimal ReworkQty { get; set; }
        public MfInspectionRequirement InspectionRequirement { get; set; }
        public int? ItpTestType { get; set; }
        public int? ItpActivityId { get; set; }
        public bool QcAccepted { get; set; }
        public string QcRequestedAtText { get; set; }
        public int? QcRecordId { get; set; }
        public string QcRecordNumber { get; set; }
        public int? NcrId { get; set; }
        public string NcrNumber { get; set; }
        public bool IsOutside { get; set; }
        public int? OutsideSupplierId { get; set; }
        public int? PurchaseRequestId { get; set; }
        public string PurchaseRequestNumber { get; set; }
        public int? PurchaseRequestLineId { get; set; }
        public int? PurchaseOrderLineId { get; set; }
        public int? SubcontractIssueId { get; set; }
        public string SubcontractIssueNumber { get; set; }
        public bool IsRework { get; set; }
        public int? ReworkOfOperationId { get; set; }
        public string SkipReason { get; set; }
        public string StartedAtText { get; set; }
        public string FinishedAtText { get; set; }
        public string Note { get; set; }

        /// <summary>Bookings of the operation that are still running.</summary>
        public int RunningBookings { get; set; }

        public decimal HoursBooked { get; set; }
    }

    // ------------------------------------------------------------------------------------------ release gate

    public class ReleaseCheckDto
    {
        /// <summary>routing, workcenters, drawings, itp, materials.</summary>
        public string Code { get; set; }

        public string Title { get; set; }
        public bool Passed { get; set; }
        public string Message { get; set; }

        /// <summary>Only the material check can be overridden, and only by the production manager with a reason.</summary>
        public bool Overridable { get; set; }
    }

    public class ShortageDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public decimal Required { get; set; }
        public decimal OnHand { get; set; }
        public decimal Reserved { get; set; }
        public decimal Incoming { get; set; }
        public decimal Available { get; set; }
        public decimal Short { get; set; }
    }

    public class ReleaseCheckResultDto
    {
        public int OrderId { get; set; }
        public bool CanRelease { get; set; }
        public bool CanReleaseWithOverride { get; set; }
        public List<ReleaseCheckDto> Checks { get; set; } = new List<ReleaseCheckDto>();
        public List<ShortageDto> Shortages { get; set; } = new List<ShortageDto>();
    }

    public class ReleaseOrderDto
    {
        public int Id { get; set; }

        /// <summary>A shortage list is released only by the production manager, with this reason.</summary>
        public string OverrideReason { get; set; }

        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ drafting and editing

    public class DraftOrdersDto
    {
        public int? WorkOrderId { get; set; }
        public int? ProcessId { get; set; }

        /// <summary>When the process output code matches no item: the item to make.</summary>
        public int? ItemId { get; set; }
    }

    public class DraftOrdersResultDto
    {
        public int Drafted { get; set; }
        public int Skipped { get; set; }
        public List<int> OrderIds { get; set; } = new List<int>();
        public List<string> Notes { get; set; } = new List<string>();
    }

    public class SaveComponentLineDto
    {
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
    }

    public class SaveComponentsDto
    {
        public int OrderId { get; set; }
        public List<SaveComponentLineDto> Components { get; set; } = new List<SaveComponentLineDto>();
        public string SerialsPlanned { get; set; }
        public string RowVersion { get; set; }
    }

    public class OrderActionDto
    {
        public int Id { get; set; }

        /// <summary>cancel, close, kit-override, refresh-routing.</summary>
        public string Action { get; set; }

        public string Reason { get; set; }
    }

    public class OperationActionDto
    {
        public int Id { get; set; }

        /// <summary>skip, complete-outside.</summary>
        public string Action { get; set; }

        public string Reason { get; set; }

        /// <summary>complete-outside: the confirmed return-from-contractor document.</summary>
        public int? DocumentId { get; set; }
    }

    public class QcDecisionDto
    {
        public int OperationId { get; set; }

        /// <summary>Accept = 1, Reject = 2, Rework = 3, Hold = 4.</summary>
        public QcResult Result { get; set; }

        public string Note { get; set; }

        /// <summary>For an ITP point operation: the ITP activity of the work order the acceptance rests on (found by the test type when empty).</summary>
        public int? ItpActivityId { get; set; }
    }

    public class ReworkOperationDto
    {
        public int OperationId { get; set; }
        public int NcrId { get; set; }
        public string Note { get; set; }
    }
}
