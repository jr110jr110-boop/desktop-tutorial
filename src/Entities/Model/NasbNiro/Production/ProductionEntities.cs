using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;

namespace Entities.Model.NasbNiro.Production
{
    // ------------------------------------------------------------------------------------------ master data

    /// <summary>A place where operations are done (work centre). The capacity fields feed the finite-capacity scheduler of part B.</summary>
    public class MfWorkCenter : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public MfWorkCenterKind Kind { get; set; }

        /// <summary>Calendar of project control (PcCalendar) the work centre works by.</summary>
        public int? CalendarId { get; set; }

        public int ShiftsPerDay { get; set; } = 1;
        public decimal HoursPerShift { get; set; } = 8;
        public decimal EfficiencyPercent { get; set; } = 100;

        /// <summary>★ provisional decision: 85 %.</summary>
        public decimal UtilisationPercent { get; set; } = 85;

        public int ParallelResources { get; set; } = 1;

        /// <summary>Rial per hour; set by finance only (never invented).</summary>
        public decimal? MachineRateRial { get; set; }

        /// <summary>Rial per hour; set by finance only. The labour rate used until a payroll month exists (part B).</summary>
        public decimal? LabourRateRial { get; set; }

        public string CostCenter { get; set; }
        public decimal QueueDaysDefault { get; set; }
        public bool IsActive { get; set; } = true;

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>1:1 side table of the legacy Machine (which only has Id, Title and IsActive).</summary>
    public class MfMachineInfo : BaseEntity
    {
        public int MachineId { get; set; }
        public int? WorkCenterId { get; set; }

        /// <summary>Fixed asset of finance (FnAsset).</summary>
        public int? AssetId { get; set; }

        public string Maker { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string Capabilities { get; set; }
        public MfMachineStatus Status { get; set; } = MfMachineStatus.Available;

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>1:1 side table of OperationTemplate: the routing master is extended, not duplicated.</summary>
    public class MfOperationStandard : BaseEntity
    {
        public int OperationTemplateId { get; set; }
        public int? WorkCenterId { get; set; }
        public decimal SetupHours { get; set; }
        public decimal RunHoursPerUnit { get; set; }
        public decimal MoveDays { get; set; }
        public decimal QueueDays { get; set; }

        public bool IsOutside { get; set; }
        public int? DefaultSupplierId { get; set; }
        public int OutsideLeadDays { get; set; }

        /// <summary>Quality document control: the controlled document of the work instruction.</summary>
        public int? WorkInstructionDocumentId { get; set; }

        /// <summary>Words that pick the drawings this operation needs out of the MDR of the work order (document type or title contains the text). Empty: no drawing needed.</summary>
        public string DrawingReferenceRule { get; set; }

        /// <summary>For example WPS (welding needs an approved procedure) or NDT; checked by quality, kept here for the job card.</summary>
        public string RequiredQualificationKind { get; set; }

        public string RequiredQualificationProcess { get; set; }
        public MfInspectionRequirement InspectionRequirement { get; set; }

        /// <summary>The ITP test type (QaTestType) of the ITP point this operation waits for.</summary>
        public int? ItpTestType { get; set; }

        public bool FirstArticleRequired { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>
    /// A revision of the routing of an item (or an item group). The steps are the OperationTemplate rows of the key; the revision records that two people
    /// (production manager and engineering, neither of them the one who wrote or edited the steps) approved exactly that content (ContentHash).
    /// </summary>
    public class MfRoutingRevision : BaseEntity
    {
        public int? ItemId { get; set; }
        public int? ItemGroupId { get; set; }
        public int Revision { get; set; }
        public MfRoutingStatus Status { get; set; }
        public string Note { get; set; }

        /// <summary>Hash of the steps and their standards at the last approval step.</summary>
        public string ContentHash { get; set; }

        public int? ProductionApprovedByUserId { get; set; }
        public DateTimeOffset? ProductionApprovedAt { get; set; }
        public int? EngineeringApprovedByUserId { get; set; }
        public DateTimeOffset? EngineeringApprovedAt { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Reasons for scrap, downtime and waiting.</summary>
    public class MfReasonCode : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public MfReasonKind Kind { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // ------------------------------------------------------------------------------------------ production orders

    /// <summary>دستور تولید داخلی: made by the system from a make process, released by people.</summary>
    public class MfProductionOrder : BaseEntity
    {
        public string Number { get; set; }
        public int WorkOrderId { get; set; }
        public int ContractId { get; set; }

        /// <summary>Legacy planning container (ProcessEntity of type Construction / Service / Assembly / FinalAssembly).</summary>
        public int? ProcessId { get; set; }

        public int? BomId { get; set; }

        /// <summary>Project control activity of the process (priority and need date come from it).</summary>
        public int? ActivityId { get; set; }

        public int? ItemId { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }

        /// <summary>Planned serial numbers, one per line of text (empty: labels are made at release).</summary>
        public string SerialsPlanned { get; set; }

        public int? RoutingRevisionId { get; set; }
        public MfOrderStatus Status { get; set; }

        // scheduler (part B)
        public DateTime? PlannedStart { get; set; }
        public DateTime? PlannedFinish { get; set; }

        /// <summary>Result of the last scheduler run: the need date it compared with, the late flag and the days late (calendar days).</summary>
        public DateTime? NeedDate { get; set; }

        public bool IsLate { get; set; }
        public int DaysLate { get; set; }
        public DateTimeOffset? ScheduledAt { get; set; }

        /// <summary>The date the kit is expected (open purchase orders), null when it is complete or cannot be told.</summary>
        public DateTime? KitReadyDate { get; set; }

        /// <summary>The kit is not issued and the stock does not cover what is left to issue.</summary>
        public bool KitShortage { get; set; }

        public string ScheduleNote { get; set; }

        public DateTimeOffset? ActualStart { get; set; }
        public DateTimeOffset? ActualFinish { get; set; }

        public bool AutoDrafted { get; set; }

        /// <summary>The last person who changed the components by hand: he cannot release the order (maker-checker).</summary>
        public int? EditedByUserId { get; set; }

        public DateTimeOffset? ReleasedAt { get; set; }
        public int? ReleasedByUserId { get; set; }

        /// <summary>The production manager released the order in spite of a material shortage.</summary>
        public bool ReleaseOverride { get; set; }

        public string ReleaseOverrideReason { get; set; }

        /// <summary>The shortage list (Persian text) the override covered.</summary>
        public string ShortageText { get; set; }

        public int? KitRequestId { get; set; }

        /// <summary>The kit (material request draft) made at release; the navigation lets the order and the kit be saved together.</summary>
        public MaterialRequest KitRequest { get; set; }

        /// <summary>The first operation may start although the kit is not fully issued (reason, supervisor or manager).</summary>
        public string KitOverrideReason { get; set; }

        public int? KitOverrideByUserId { get; set; }

        public int? ProductionReceiptId { get; set; }
        public string CancelReason { get; set; }
        public string Note { get; set; }

        /// <summary>The operations and components of the order (so the order and its children are saved together).</summary>
        public ICollection<MfOperation> Operations { get; set; } = new List<MfOperation>();

        public ICollection<MfOrderComponent> Components { get; set; } = new List<MfOrderComponent>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A component the order consumes (from the process input commodity or typed by planning). Material availability and the kit are computed from these.</summary>
    public class MfOrderComponent
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
    }

    public class MfOperation : BaseEntity
    {
        public int OrderId { get; set; }
        public int Sequence { get; set; }
        public int? OperationTemplateId { get; set; }
        public string Title { get; set; }
        public int? WorkCenterId { get; set; }
        public decimal SetupHoursPlanned { get; set; }
        public decimal RunHoursPlanned { get; set; }
        public DateTime? PlannedStart { get; set; }
        public DateTime? PlannedFinish { get; set; }
        public MfOperationStatus Status { get; set; }

        public decimal GoodQty { get; set; }
        public decimal ScrapQty { get; set; }
        public decimal ReworkQty { get; set; }

        public MfInspectionRequirement InspectionRequirement { get; set; }
        public int? ItpTestType { get; set; }
        public int? ItpActivityId { get; set; }
        public bool QcAccepted { get; set; }
        public DateTimeOffset? QcRequestedAt { get; set; }
        public int? QcRecordId { get; set; }
        public QcRecord QcRecord { get; set; }
        public int? NcrId { get; set; }
        public NonConformance Ncr { get; set; }

        public bool IsOutside { get; set; }
        public int? OutsideSupplierId { get; set; }
        public int OutsideLeadDays { get; set; }
        public int? PurchaseRequestId { get; set; }
        public int? PurchaseRequestLineId { get; set; }

        /// <summary>Navigations so the purchase request and the operation are stored by one save (the keys are filled by the save).</summary>
        public Entities.Model.NasbNiro.Procurement.PurchaseRequest PurchaseRequest { get; set; }

        public Entities.Model.NasbNiro.Procurement.PurchaseRequestLine PurchaseRequestLine { get; set; }

        /// <summary>The purchase order line that buys the outside work (set when procurement has ordered it).</summary>
        public int? PurchaseOrderLineId { get; set; }

        public int? SubcontractIssueId { get; set; }
        public bool OutsideDrafted { get; set; }

        public bool IsRework { get; set; }
        public int? ReworkOfOperationId { get; set; }

        public string SkipReason { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string Note { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Links an operation of an order to the PartOperation rows (per serial / lot) made for it, so the traceability screens keep working.</summary>
    public class MfOperationPart
    {
        public int Id { get; set; }
        public int OperationId { get; set; }
        public int PartOperationId { get; set; }
        public PartOperation PartOperation { get; set; }
    }

    // ------------------------------------------------------------------------------------------ time booking

    public class MfTimeBooking : BaseEntity
    {
        public int OperationId { get; set; }

        /// <summary>FnEmployee, derived from the employee link of the signed-in user (or named by the supervisor).</summary>
        public int EmployeeId { get; set; }

        public int? MachineId { get; set; }
        public MfBookingKind Kind { get; set; }

        /// <summary>Server local time.</summary>
        public DateTime StartAt { get; set; }

        /// <summary>Null while the booking runs.</summary>
        public DateTime? EndAt { get; set; }

        public int Minutes { get; set; }

        /// <summary>Minutes / 60 rounded to two decimals.</summary>
        public decimal Hours { get; set; }

        public decimal GoodQty { get; set; }
        public decimal ScrapQty { get; set; }
        public int? ScrapReasonCodeId { get; set; }
        public decimal ReworkQty { get; set; }
        public int? ReasonCodeId { get; set; }
        public string Note { get; set; }
        public MfBookingSource Source { get; set; }

        /// <summary>A booking made by a supervisor for somebody else needs a reason.</summary>
        public string SupervisorReason { get; set; }

        /// <summary>Why the booking was changed after it was made (after the day closes only the shop supervisor may, with a reason).</summary>
        public string EditReason { get; set; }

        /// <summary>The daily cost posting (part B) that absorbed this booking; null while it is not posted or when it has no hours to cost.</summary>
        public int? CostRunId { get; set; }

        public MfCostRun CostRun { get; set; }

        /// <summary>Set when the cost posting has handled the booking (a posted booking is final: a correction goes through finance).</summary>
        public DateTimeOffset? CostPostedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A breakdown reported from the terminal or by a user. The planned and breakdown maintenance orders come in part B.</summary>
    public class MfMaintenanceRequest : BaseEntity
    {
        public string Number { get; set; }
        public int MachineId { get; set; }
        public int? WorkCenterId { get; set; }
        public int? OperationId { get; set; }
        public int? ReasonCodeId { get; set; }
        public int ReportedByUserId { get; set; }
        public DateTime ReportedAt { get; set; }

        /// <summary>Persian date and time of the report, for example 1405/08/03 14:05.</summary>
        public string ReportedAtText { get; set; }

        public string Description { get; set; }
        public MfMaintenanceRequestStatus Status { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ daily cost posting (part B)

    /// <summary>One run of the daily cost posting for one booking day. It owns the accounting event "labour absorbed".</summary>
    public class MfCostRun
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime PostingDate { get; set; }
        public DateTimeOffset RunAt { get; set; }

        /// <summary>0 = the nightly job.</summary>
        public int RunByUserId { get; set; }

        public int BookingCount { get; set; }
        public decimal LabourHours { get; set; }
        public decimal MachineHours { get; set; }
        public decimal LabourAmount { get; set; }
        public decimal OverheadAmount { get; set; }

        /// <summary>Distinct employees whose bookings are in the run.</summary>
        public int EmployeeCount { get; set; }

        /// <summary>Distinct employees among them whose labour rate came from payroll.</summary>
        public int PayrollEmployeeCount { get; set; }

        /// <summary>True when one of the postings of the run is too small to show its labour amount without revealing a person's pay (see <c>MfCostMask</c>); the run total is then hidden too.</summary>
        public bool LabourProtected { get; set; }

        public int? AccountingEventId { get; set; }
        public AccountingEvent AccountingEvent { get; set; }
    }

    /// <summary>
    /// Cost absorbed for one work order, WBS node and work centre on one day. Never one row per person, so no individual pay rate can be read from cost data.
    /// </summary>
    public class MfCostPosting
    {
        public int Id { get; set; }
        public int RunId { get; set; }
        public MfCostRun Run { get; set; }
        public DateTime PostingDate { get; set; }
        public int WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public int? WbsNodeId { get; set; }
        public int? WorkCenterId { get; set; }
        public int BookingCount { get; set; }
        public decimal LabourHours { get; set; }
        public decimal MachineHours { get; set; }
        public decimal LabourAmount { get; set; }
        public decimal OverheadAmount { get; set; }

        /// <summary>Distinct employees in the row.</summary>
        public int EmployeeCount { get; set; }

        /// <summary>Distinct employees in the row whose labour rate came from payroll.</summary>
        public int PayrollEmployeeCount { get; set; }
    }

    // ------------------------------------------------------------------------------------------ scheduling (part B)

    public class MfScheduleRun
    {
        public int Id { get; set; }
        public DateTime AsOfDate { get; set; }
        public DateTimeOffset RunAt { get; set; }

        /// <summary>0 = the nightly job.</summary>
        public int RunByUserId { get; set; }

        public int Orders { get; set; }
        public int Operations { get; set; }
        public int LateOrders { get; set; }
        public int Unscheduled { get; set; }
    }

    // ------------------------------------------------------------------------------------------ maintenance (part B)

    public class MfMaintenancePlan : BaseEntity
    {
        public int MachineId { get; set; }
        public string Task { get; set; }
        public MfMaintenanceIntervalKind IntervalKind { get; set; }

        /// <summary>Days or running hours between two services.</summary>
        public decimal IntervalValue { get; set; }

        /// <summary>The day the plan counts from until the first service is done.</summary>
        public DateTime StartDate { get; set; }

        public DateTime? LastDoneAt { get; set; }

        /// <summary>Next due date of a day-based plan (kept by the service).</summary>
        public DateTime? NextDueDate { get; set; }

        public string Checklist { get; set; }
        public decimal EstimatedHours { get; set; }
        public bool IsActive { get; set; } = true;

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A planned or breakdown maintenance job. A breakdown order is drafted from the machine-down report; a planned one by the job that watches the plans.</summary>
    public class MfMaintenanceOrder : BaseEntity
    {
        public string Number { get; set; }
        public int MachineId { get; set; }
        public int? WorkCenterId { get; set; }
        public MfMaintenanceKind Kind { get; set; }
        public MfMaintenanceOrderStatus Status { get; set; }
        public int? PlanId { get; set; }
        public int? RequestId { get; set; }
        public string Task { get; set; }
        public string Checklist { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal EstimatedHours { get; set; }

        /// <summary>The machine is out of production from the start to the end (an open end means it is still down).</summary>
        public DateTime? DowntimeStart { get; set; }

        public DateTime? DowntimeEnd { get; set; }
        public string Cause { get; set; }
        public string ActionTaken { get; set; }

        /// <summary>The maintenance cost centre the spare parts are charged to.</summary>
        public string CostCenter { get; set; }

        public int? MaterialRequestId { get; set; }
        public int? IssueDocumentId { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public int? StartedByUserId { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public int? DoneByUserId { get; set; }
        public string CancelReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class MfMaintenanceLabour : BaseEntity
    {
        public int OrderId { get; set; }
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public decimal Hours { get; set; }
        public string Note { get; set; }
    }

    public class MfMaintenancePart
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public string Note { get; set; }
    }
}
