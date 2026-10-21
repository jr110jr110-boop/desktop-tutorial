using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace Entities.Model.NasbNiro.ProjectControl
{
    // Project control module, part 2: WBS, activities, dependencies, baselines, cost baseline.

    public class PcWbsNode : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        public int? ParentId { get; set; }
        public PcWbsNode Parent { get; set; }
        public ICollection<PcWbsNode> Children { get; set; }

        public string Code { get; set; }
        public string Title { get; set; }
        public PcWbsNodeType NodeType { get; set; }
        public PcPhase? Phase { get; set; }

        public int? WorkOrderId { get; set; }
        public int? AssemblyId { get; set; }
        public int? BomId { get; set; }
        public int? ProcessId { get; set; }

        /// <summary>Share among the siblings (nodes and activities under the same parent); the siblings sum to 100.</summary>
        public decimal Weight { get; set; }
        public decimal BudgetAmount { get; set; }
        public string Dictionary { get; set; }
        public string ResponsibleUnit { get; set; }
        public int Sort { get; set; }
        public bool IsGenerated { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PcActivity : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        public int? WbsNodeId { get; set; }
        public PcWbsNode WbsNode { get; set; }

        public string Code { get; set; }
        public string Title { get; set; }
        public PcSourceType SourceType { get; set; }
        public int? SourceId { get; set; }
        public int? WorkOrderId { get; set; }

        /// <summary>Whole working days (hours come with the resource release).</summary>
        public int DurationDays { get; set; }
        public int? CalendarId { get; set; }
        public PcPhase? Phase { get; set; }

        /// <summary>Role of the unit manager who signs the baseline for this activity (PLANNING, ENGINEERING, CONSTRUCTION...).</summary>
        public string ResponsibleUnit { get; set; }

        public PcConstraint ConstraintType { get; set; }
        public DateTime? ConstraintDate { get; set; }

        // current schedule (plan run, ignores progress)
        public DateTime? EarlyStart { get; set; }
        public DateTime? EarlyFinish { get; set; }
        public DateTime? LateStart { get; set; }
        public DateTime? LateFinish { get; set; }
        public int? TotalFloat { get; set; }
        public int? FreeFloat { get; set; }
        public bool IsCritical { get; set; }

        // forecast run (actuals + remaining duration + PO delivery dates)
        public DateTime? ForecastStart { get; set; }
        public DateTime? ForecastFinish { get; set; }

        public DateTime? ActualStart { get; set; }
        public DateTime? ActualFinish { get; set; }

        public PcProgressMethod ProgressMethod { get; set; }
        public decimal Weight { get; set; }
        public decimal BudgetAtCompletion { get; set; }
        public PcActivityStatus Status { get; set; }
        public string OnHoldReason { get; set; }

        // links to the other modules
        public int? PurchaseOrderLineId { get; set; }
        public int? PurchaseRequestLineId { get; set; }
        public int? ItpActivityId { get; set; }
        public PcMilestoneEvent MilestoneEvent { get; set; }
        public decimal? PlannedQuantity { get; set; }
        public int? SerialItemId { get; set; }

        /// <summary>Latest date the material must be on the purchase request / order (late start minus lead time).</summary>
        public DateTime? NeedByDate { get; set; }
        public int LeadTimeDays { get; set; }

        /// <summary>The packaging activity: its actual start needs a released release note (R-SH-1).</summary>
        public bool IsPackaging { get; set; }

        public bool IsLate { get; set; }
        public string LateKind { get; set; }

        /// <summary>Last physical percent computed by the progress engine (for lists).</summary>
        public decimal PercentCache { get; set; }
        public int Sort { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PcDependency
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public int PredecessorId { get; set; }
        public PcActivity Predecessor { get; set; }
        public int SuccessorId { get; set; }
        public PcActivity Successor { get; set; }
        public PcDependencyType Type { get; set; }
        public int LagDays { get; set; }
        public PcDependencyOrigin Origin { get; set; }
    }

    public class PcBaseline : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        /// <summary>0 = original baseline.</summary>
        public int Number { get; set; }
        public PcBaselineKind Kind { get; set; }
        public PcBaselineStatus Status { get; set; }
        public string Reason { get; set; }
        public int? ChangeRequestId { get; set; }

        public DateTime? ProjectStart { get; set; }
        public DateTime? ProjectFinish { get; set; }
        public decimal Bac { get; set; }
        public decimal ManagementReserve { get; set; }

        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }

        public ICollection<PcBaselineSignature> Signatures { get; set; }
        public ICollection<PcBaselineActivity> Activities { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>All unit managers who own activities sign the schedule, then the PMO deputy approves (WI-PL-009 17).</summary>
    public class PcBaselineSignature
    {
        public int Id { get; set; }
        public int BaselineId { get; set; }
        public PcBaseline Baseline { get; set; }
        public string UnitRole { get; set; }
        public int? UserId { get; set; }
        public DateTimeOffset? SignedAt { get; set; }
    }

    public class PcBaselineActivity
    {
        public int Id { get; set; }
        public int BaselineId { get; set; }
        public PcBaseline Baseline { get; set; }
        public int ActivityId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? WbsNodeId { get; set; }
        public PcPhase? Phase { get; set; }
        public DateTime Start { get; set; }
        public DateTime Finish { get; set; }
        public int DurationDays { get; set; }
        public decimal Weight { get; set; }
        public decimal Budget { get; set; }
        public PcProgressMethod ProgressMethod { get; set; }
        public int? CalendarId { get; set; }
        public string ResponsibleUnit { get; set; }
    }

    public class PcBaselineCostPeriod
    {
        public int Id { get; set; }
        public int BaselineId { get; set; }
        public PcBaseline Baseline { get; set; }
        public DateTime PeriodStart { get; set; }
        public decimal PlannedCumulative { get; set; }
    }

    /// <summary>Budget lines of the cost baseline (CBS): contingency and management reserve are kept apart from the activity budgets.</summary>
    public class PcCostBaselineLine
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public int? BaselineId { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public PcCostClass CostClass { get; set; }
        public PcResourceClass? ResourceClass { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; }
    }
}
