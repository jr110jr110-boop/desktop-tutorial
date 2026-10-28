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

        /// <summary>
        /// R1p D43: an operation of this centre cannot be started on the terminal without choosing a machine (the machine hours, the machine overhead and the OEE come from it).
        /// ★ provisional decision: true by default; the rule applies only while the centre has at least one machine that is not retired (a centre without machines has nothing to choose).
        /// </summary>
        public bool RequiresMachine { get; set; } = true;

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

        /// <summary>R1p D12: a welding step. It starts only with an approved WPS (<see cref="WpsId"/>) and a booking employee who holds a valid welder qualification for the process of that WPS.</summary>
        public bool IsWelding { get; set; }

        /// <summary>R1p D12: the welding procedure specification (Wps of the quality module) the step is welded by.</summary>
        public int? WpsId { get; set; }

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

        /// <summary>R1p D6: pieces of the order scrapped by an approved NCR decision (the order makes <c>Quantity - WrittenOffQty</c>; a replacement order is drafted for them).</summary>
        public decimal WrittenOffQty { get; set; }

        /// <summary>R1p D6: the order this one was drafted to replace after a scrap decision.</summary>
        public int? ReplacesOrderId { get; set; }

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

        /// <summary>R1p D12: a welding step (copied from the standard when the order is drafted).</summary>
        public bool IsWelding { get; set; }

        /// <summary>R1p D12: the approved WPS the step is welded by (copied from the standard).</summary>
        public int? WpsId { get; set; }

        /// <summary>R1p D12: the weld log drafted from the booking when the welding step finished.</summary>
        public int? WeldLogId { get; set; }

        /// <summary>R1p D6: the production manager let the order continue with this rejected step on the strength of an approved NCR concession (<see cref="NcrId"/>).</summary>
        public bool AcceptedByConcession { get; set; }

        /// <summary>R1p D38: the reported quantities at the moment QC accepted the step; a finish with other quantities needs a new acceptance.</summary>
        public decimal? QcAcceptedGoodQty { get; set; }

        public decimal? QcAcceptedScrapQty { get; set; }
        public decimal? QcAcceptedReworkQty { get; set; }

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

        /// <summary>
        /// R1p D1 (pooled average rate): the Persian month (yyyymm) whose labour pool takes this booking at the month-end true-up. The daily cost posting sets it for the booking of a payroll-rated employee
        /// (a booking paid by a work-centre standard rate is final at posting and has none; a late booking of a month that is already closed goes to the next open month). The monthly run moves it to a later month
        /// while the pool of its month is too small (fewer than three payroll-rated employees).
        /// </summary>
        public int? LabourPendingPeriod { get; set; }

        /// <summary>
        /// R1p D1: hours x the employee's own hourly payroll cost, unrounded. It only ever goes into the cost total of a pool (the month-end true-up); no API shows it, because on its own it is one person's pay.
        /// </summary>
        public decimal? PayrollCost { get; set; }

        /// <summary>R1p D1: the labour the daily cost posting absorbed for this booking: hours x the pooled average rate (the last closed month of its pool, else the standard rate), rounded to a whole rial. The true-up of the month is pool cost less the sum of these.</summary>
        public decimal? AbsorbedLabour { get; set; }

        /// <summary>R1p D1: the hourly rate of the employee came from an approved payslip (the employee counts as payroll-rated in the pool test).</summary>
        public bool RateFromPayroll { get; set; }

        /// <summary>R1p D1: the month-end true-up took this booking into its pool.</summary>
        public DateTimeOffset? LabourAggregatedAt { get; set; }

        /// <summary>R1p D39: an extra booking the shop supervisor added to an operation of a completed order, with a reason (bookings of a completed order are never edited).</summary>
        public bool IsCorrection { get; set; }

        public int? CorrectsBookingId { get; set; }

        /// <summary>
        /// R1p review 9: a correction booking is pending until a production manager other than the one who made it approves it (maker-checker); the cost posting skips a correction without this approval.
        /// Null for every other booking.
        /// </summary>
        public int? CorrectionApprovedByUserId { get; set; }

        public DateTimeOffset? CorrectionApprovedAt { get; set; }

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

        /// <summary>Kept for the database (it exists since release P1). Since R1p labour is absorbed at a pooled average rate, so no run total shows one person's pay: always false.</summary>
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

    public enum MfLabourModule
    {
        Production = 0,
        Service = 1,
    }

    /// <summary>
    /// R1p D1/D2: one labour pool of one Persian month and its month-end true-up. Labour is absorbed into work orders at a pooled average rate (pool cost / pool hours), so no published amount is one person's pay:
    /// the pool is the payroll-rated employees with hours in the month, per cost centre when that cost centre has at least three of them, else the company pool of the remaining people, else nothing is published and the
    /// month is carried (at a Persian quarter end the carry is posted as one company-wide line flagged protected). The month-end run stores the pool here (people, hours, payroll cost, what the days absorbed, the rate for the
    /// next month) and posts the true-up (cost less absorbed) as ONE line without work order, WBS node or day. The daily cost posting reads the newest unprotected rate of the pool as its absorption rate.
    /// A row of a protected pool carries no rate and is read only by PAYROLL, FINANCEMANAGER and CEO.
    /// </summary>
    public class MfLabourPool
    {
        public int Id { get; set; }
        public MfLabourModule Module { get; set; }

        /// <summary>The Persian month as yyyymm.</summary>
        public int Period { get; set; }

        /// <summary>The cost centre code of the pool, or "*" for the company pool.</summary>
        public string PoolKey { get; set; }

        /// <summary>Distinct payroll-rated employees with hours in the pool (3 or more unless the pool is protected).</summary>
        public int People { get; set; }

        public decimal Hours { get; set; }

        /// <summary>The labour cost of the pool from payroll (the sum of the own hourly cost x hours of its people): the only payroll figure that leaves the payroll boundary.</summary>
        public decimal Cost { get; set; }

        /// <summary>What the days (production) or the closed orders (service) absorbed for the pool at the rate in force.</summary>
        public decimal Absorbed { get; set; }

        /// <summary>Cost less absorbed: the one company-level line of the month.</summary>
        public decimal Variance { get; set; }

        /// <summary>Cost / hours, four decimals: the absorption rate of the next days. Null for a protected pool.</summary>
        public decimal? Rate { get; set; }

        /// <summary>Posted by the quarter-end cap although fewer than three payroll-rated employees are behind it (flagged on the cost entry, the event line and the voucher line).</summary>
        public bool Protected { get; set; }

        /// <summary>Bookings (production) or orders (service) the true-up took.</summary>
        public int Items { get; set; }

        public int? AccountingEventId { get; set; }
        public AccountingEvent AccountingEvent { get; set; }

        public DateTimeOffset PostedAt { get; set; }

        /// <summary>0 = the nightly job.</summary>
        public int PostedByUserId { get; set; }
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
