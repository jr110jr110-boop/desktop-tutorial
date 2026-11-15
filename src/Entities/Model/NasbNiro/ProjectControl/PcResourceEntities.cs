using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace Entities.Model.NasbNiro.ProjectControl
{
    // Project control, priority 4 (phases 4a and 4b): the project resource pool, the standard rates and the resource plan of a contract (assignments of resources to activities).
    // The plan is drafted by the planning agent (RES.PLAN.DRAFT); people review, submit and approve it. An approved plan is part of the baseline (PcBaselineResource).

    public enum PcResourceKind { Labour = 1, Machine = 2, Equipment = 3, Material = 4, Subcontract = 5 }

    /// <summary>The unit a resource is counted in; the rate is rial per this unit and the quantity of an assignment is in this unit.</summary>
    public enum PcResourceUnit { Hour = 1, Day = 2, Each = 3 }

    public enum PcRateStatus { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3 }

    public enum PcResourcePlanStatus { Draft = 0, Submitted = 1, Approved = 2, Returned = 3 }

    public enum PcResourceDistribution { Uniform = 1, FrontLoaded = 2, BackLoaded = 3 }

    public enum PcAssignmentSource { Agent = 1, Human = 2, Import = 3 }

    /// <summary>
    /// A resource of the pool. A Labour resource is a ROLE or specialty group (for example a welder, capacity 3), not a person. A Labour resource may be linked to one employee (a named person); it can then never
    /// carry a rate and points to a role resource (<see cref="RateResourceId"/>) whose rate is used, so nobody's pay can be derived from project costs.
    /// </summary>
    public class PcResource : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public PcResourceKind Kind { get; set; }
        public PcResourceUnit Unit { get; set; }

        /// <summary>The largest number of units that can work at the same time (greater than zero).</summary>
        public decimal Capacity { get; set; } = 1m;

        public int? CalendarId { get; set; }
        public PcCalendar Calendar { get; set; }

        /// <summary>The unit manager role (upper case) whose activities this resource serves; the planning agent matches <see cref="PcActivity.ResponsibleUnit"/> with it.</summary>
        public string ResponsibleUnit { get; set; }

        // at most one link, matching the kind: employee -> Labour, work centre -> Machine, supplier -> Subcontract
        public int? EmployeeId { get; set; }
        public int? WorkCenterId { get; set; }
        public int? ProviderId { get; set; }

        /// <summary>Only for a Labour resource linked to an employee: the role resource whose rate is used for it.</summary>
        public int? RateResourceId { get; set; }

        public bool IsActive { get; set; } = true;
        public string Notes { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A standard rate (rial per unit of the resource) from a date. Proposed by the finance manager and approved by a second person; only an approved rate is used for costing.</summary>
    public class PcResourceRate : BaseEntity
    {
        public int ResourceId { get; set; }
        public PcResource Resource { get; set; }
        public decimal Rate { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public PcRateStatus Status { get; set; }

        /// <summary>The person who proposed the rate (the maker).</summary>
        public int MakerUserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>The resource plan of a contract. One open plan (draft, submitted or returned) per contract; a new version after a baseline is opened only through an approved change request.</summary>
    public class PcResourcePlan : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public int Version { get; set; }
        public PcResourcePlanStatus Status { get; set; }

        /// <summary>The person who made or last edited the plan; the planning agent while nobody has touched its draft.</summary>
        public int MakerUserId { get; set; }

        /// <summary>The plan was first drafted by the planning agent (kept for history even when a person edits it).</summary>
        public bool PreparedByAgent { get; set; }

        /// <summary>The approved change request that opened this version after the baseline; null for a version before the baseline.</summary>
        public int? ChangeRequestId { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ReturnedAt { get; set; }
        public int? ReturnedByUserId { get; set; }
        public string ReturnReason { get; set; }

        /// <summary>Everyone who made or edited the plan (",5,7,"): none of them approves it.</summary>
        public string EditorUserIds { get; set; }

        public ICollection<PcActivityResource> Assignments { get; set; }
        public ICollection<PcResourcePlanSkip> Skips { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>One resource on one activity of a plan: the quantity (in the unit of the resource) and how it is spread over the activity.</summary>
    public class PcActivityResource
    {
        public int Id { get; set; }
        public int PlanId { get; set; }
        public PcResourcePlan Plan { get; set; }
        public int ActivityId { get; set; }
        public PcActivity Activity { get; set; }
        public int ResourceId { get; set; }
        public PcResource Resource { get; set; }
        public decimal Quantity { get; set; }
        public PcResourceDistribution Distribution { get; set; } = PcResourceDistribution.Uniform;
        public string ResponsibleRole { get; set; }
        public PcAssignmentSource Source { get; set; }
        public int EditedByUserId { get; set; }
        public DateTimeOffset EditedAt { get; set; }
    }

    /// <summary>An activity that the planner deliberately leaves without a resource («بدون منبع») in a plan.</summary>
    public class PcResourcePlanSkip
    {
        public int Id { get; set; }
        public int PlanId { get; set; }
        public PcResourcePlan Plan { get; set; }
        public int ActivityId { get; set; }
        public PcActivity Activity { get; set; }
        public int MarkedByUserId { get; set; }
        public DateTimeOffset MarkedAt { get; set; }
    }

    /// <summary>The approved assignments frozen with a baseline, with the standard rate of that day (null when the resource had no approved rate).</summary>
    public class PcBaselineResource
    {
        public int Id { get; set; }
        public int BaselineId { get; set; }
        public PcBaseline Baseline { get; set; }
        public int ActivityId { get; set; }
        public int ResourceId { get; set; }
        public decimal Quantity { get; set; }
        public PcResourceDistribution Distribution { get; set; }
        public decimal? RateAtBaseline { get; set; }
    }

    // ------------------------------------------------------------------------------------------ priority 4, phases 4d and 4e

    /// <summary>A resource of the pool that the project charter names as pre-assigned (the structured list next to the free text of the charter).</summary>
    public class PcCharterResource
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public int ResourceId { get; set; }
        public PcResource Resource { get; set; }
        public decimal Quantity { get; set; }
        public string Note { get; set; }
    }

    public enum PcCostProposalStatus { Proposed = 1, Accepted = 2, Rejected = 3, Superseded = 4 }

    /// <summary>
    /// A proposed line of the cost baseline (CBS) by resource class, made by the planning agent (RES.COST.PROPOSE) from an approved resource plan: quantity x approved standard rate. It is never a cost-baseline line by itself:
    /// the owner of the cost baseline accepts it (the line is then written through the existing cost-baseline path) or rejects it (RES.COST.REVIEW).
    /// </summary>
    public class PcResourceCostProposal : BaseEntity
    {
        public int ContractId { get; set; }

        /// <summary>The approved resource plan the amounts come from.</summary>
        public int PlanId { get; set; }
        public int PlanVersion { get; set; }
        public PcResourceClass ResourceClass { get; set; }

        /// <summary>The amount proposed as a NEW cost-baseline line: the sum of quantity x approved rate of the lines of the class (rate present, not payroll-sensitive) less what the cost baseline already holds for that class.</summary>
        public decimal Amount { get; set; }

        /// <summary>What the cost baseline already held for this resource class when the proposal was made (the proposal is for the difference).</summary>
        public decimal ExistingAmount { get; set; }

        /// <summary>Lines left out because the resource had no approved rate, and lines left out because they are payroll-sensitive (a small labour group): counted, never priced.</summary>
        public int ExcludedNoRate { get; set; }
        public int ExcludedSensitive { get; set; }
        public int LineCount { get; set; }

        public PcCostProposalStatus Status { get; set; }
        public bool PreparedByAgent { get; set; }

        /// <summary>The cost-baseline line written when the proposal was accepted.</summary>
        public int? CostLineId { get; set; }
        public PcCostBaselineLine CostLine { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string RejectReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
