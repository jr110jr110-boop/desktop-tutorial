using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    // Project control priority 4 (phases 4a and 4b): resource pool, standard rates, resource plan, assignments, budget view.
    // A rate or an amount that could show one person's cost is null with RateHidden = true for a reader who is not FINANCEMANAGER, PAYROLL, CEO or PMO (project rule 6).

    public class PcResourceQuery : PaginationParams
    {
        public PcResourceKind? Kind { get; set; }
        public bool? IsActive { get; set; }
        public string Contains { get; set; }
    }

    public class PcResourceDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public PcResourceKind Kind { get; set; }
        public PcResourceUnit Unit { get; set; }
        public decimal Capacity { get; set; }
        public int? CalendarId { get; set; }
        public string CalendarName { get; set; }
        public string ResponsibleUnit { get; set; }
        public int? EmployeeId { get; set; }
        public int? WorkCenterId { get; set; }
        public int? ProviderId { get; set; }
        public int? RateResourceId { get; set; }
        public string RateResourceCode { get; set; }
        public bool IsActive { get; set; }
        public string Notes { get; set; }

        /// <summary>The resource is on an assignment of some plan (it can then be deactivated but not deleted).</summary>
        public bool HasAssignments { get; set; }

        /// <summary>The resource may carry a rate of its own (not an employee-linked one).</summary>
        public bool CanCarryRate { get; set; }

        /// <summary>The approved rate that applies today (the rate of the role resource for an employee-linked one); null when none or hidden.</summary>
        public decimal? EffectiveRate { get; set; }
        public bool RateHidden { get; set; }

        /// <summary>Priority 4d: the capacity and the calendar of a machine resource linked to a work centre are read from the work centre (read only here); the three fields tell the form.</summary>
        public bool CapacityFromWorkCenter { get; set; }
        public string WorkCenterCode { get; set; }
        public decimal? WorkCenterDayHours { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcResourceRateQuery : PaginationParams
    {
        public int? ResourceId { get; set; }
        public PcRateStatus? Status { get; set; }
    }

    public class PcResourceRateDto
    {
        public int Id { get; set; }
        public int ResourceId { get; set; }
        public string ResourceCode { get; set; }
        public string ResourceTitle { get; set; }
        public decimal? Rate { get; set; }
        public bool RateHidden { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public PcRateStatus Status { get; set; }
        public int MakerUserId { get; set; }
        public string MakerName { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Propose a rate (a draft), or change a draft one (Id set).</summary>
    public class PcResourceRateSaveDto
    {
        public int? Id { get; set; }
        public int ResourceId { get; set; }
        public decimal Rate { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action: submit (the maker), approve or reject (a second person; reject needs the reason in Note).</summary>
    public class PcResourceRateActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
    }

    public class PcResourcePlanDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public string ContractCode { get; set; }
        public int Version { get; set; }
        public PcResourcePlanStatus Status { get; set; }
        public int MakerUserId { get; set; }
        public string MakerName { get; set; }
        public bool PreparedByAgent { get; set; }
        public int? ChangeRequestId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ReturnedAt { get; set; }
        public string ReturnReason { get; set; }

        /// <summary>The project manager of the contract: the one person who approves the plan (null: the PMO approves).</summary>
        public int? ApproverUserId { get; set; }

        /// <summary>Who will really decide on the plan: the project manager, or null (the PMO) when the contract has none or the manager made or edited the plan.</summary>
        public int? DeciderUserId { get; set; }
        public string DeciderName { get; set; }

        /// <summary>The plan can be edited now (draft or returned).</summary>
        public bool Editable { get; set; }

        /// <summary>Activities of the contract that need a resource (their duration is above zero).</summary>
        public int ActivitiesNeedingResource { get; set; }

        /// <summary>Of them: without an assignment and not marked «بدون منبع».</summary>
        public int UnassignedCount { get; set; }
        public List<int> MarkedUnassignedActivityIds { get; set; } = new List<int>();
        public List<PcActivityResourceDto> Assignments { get; set; } = new List<PcActivityResourceDto>();
        public string RowVersion { get; set; }
    }

    public class PcActivityResourceDto
    {
        public int Id { get; set; }
        public int PlanId { get; set; }
        public int ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public string ActivityTitle { get; set; }
        public int ResourceId { get; set; }
        public string ResourceCode { get; set; }
        public string ResourceTitle { get; set; }
        public PcResourceKind ResourceKind { get; set; }
        public PcResourceUnit ResourceUnit { get; set; }
        public decimal Quantity { get; set; }
        public PcResourceDistribution Distribution { get; set; }
        public string ResponsibleRole { get; set; }
        public PcAssignmentSource Source { get; set; }
    }

    public class PcAssignmentLineDto
    {
        public int ResourceId { get; set; }
        public decimal Quantity { get; set; }
        public PcResourceDistribution? Distribution { get; set; }
        public string ResponsibleRole { get; set; }
    }

    /// <summary>Bulk upsert of the resources of one activity of a plan; RemoveOthers deletes the rows of the activity that are not listed.</summary>
    public class PcActivityAssignmentsDto
    {
        public int PlanId { get; set; }
        public int ActivityId { get; set; }
        public List<PcAssignmentLineDto> Lines { get; set; } = new List<PcAssignmentLineDto>();
        public bool RemoveOthers { get; set; }
    }

    /// <summary>Marks activities as deliberately without a resource («بدون منبع»). No ids and AllRemaining = true marks every activity that is still unassigned; Unmark takes the mark off.</summary>
    public class PcMarkUnassignedDto
    {
        public int PlanId { get; set; }
        public List<int> ActivityIds { get; set; }
        public bool AllRemaining { get; set; }
        public bool Unmark { get; set; }
    }

    /// <summary>Opens a plan of a contract by hand (an empty draft). After a baseline the approved change request that reopens the plan is named in ChangeRequestId.</summary>
    public class PcResourcePlanOpenDto
    {
        public int ContractId { get; set; }
        public int? ChangeRequestId { get; set; }
    }

    /// <summary>Action: submit (the planner), approve (the project manager of the contract), return (the project manager; the reason is in Note).</summary>
    public class PcResourcePlanActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Throws away an open plan that was opened by mistake (before any baseline, no change request): the reason is optional.</summary>
    public class PcResourcePlanDiscardDto
    {
        public int Id { get; set; }
        public string Reason { get; set; }
    }

    public class PcResourceBudgetLineDto
    {
        public int AssignmentId { get; set; }
        public int ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public int ResourceId { get; set; }
        public string ResourceCode { get; set; }
        public string ResourceTitle { get; set; }
        public PcResourceClass ResourceClass { get; set; }
        public decimal Quantity { get; set; }
        public decimal? Rate { get; set; }
        public decimal? Amount { get; set; }
        public bool RateHidden { get; set; }

        /// <summary>The resource has no approved rate on the date; the line is not in the totals.</summary>
        public bool NoApprovedRate { get; set; }

        /// <summary>«بدون نرخ مصوب» for a line without an approved rate; else null.</summary>
        public string Note { get; set; }
    }

    public class PcResourceBudgetGroupDto
    {
        public int? ResourceId { get; set; }
        public string ResourceCode { get; set; }
        public string ResourceTitle { get; set; }
        public PcResourceClass ResourceClass { get; set; }
        public decimal Quantity { get; set; }
        public decimal Amount { get; set; }
        public int LineCount { get; set; }
    }

    /// <summary>The cost of a plan: quantity times the approved rate of the date. Lines without an approved rate are listed and left out of the totals; for a reader who may not see individual cost, the hidden lines are left out too.</summary>
    public class PcResourceBudgetDto
    {
        public int PlanId { get; set; }
        public int ContractId { get; set; }
        public DateTime AsOf { get; set; }
        public List<PcResourceBudgetLineDto> Lines { get; set; } = new List<PcResourceBudgetLineDto>();
        public List<PcResourceBudgetGroupDto> ByResource { get; set; } = new List<PcResourceBudgetGroupDto>();
        public List<PcResourceBudgetGroupDto> ByClass { get; set; } = new List<PcResourceBudgetGroupDto>();
        public decimal Total { get; set; }

        /// <summary>Lines without an approved rate (not in the totals).</summary>
        public int NoRateCount { get; set; }

        /// <summary>Lines left out of the totals for this reader because their cost could show one person's pay.</summary>
        public int HiddenLineCount { get; set; }
        public bool RateHidden { get; set; }
    }

    public class PcBaselineResourceDto
    {
        public int BaselineId { get; set; }
        public int ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public int ResourceId { get; set; }
        public string ResourceCode { get; set; }
        public decimal Quantity { get; set; }
        public PcResourceDistribution Distribution { get; set; }
        public decimal? RateAtBaseline { get; set; }
        public bool RateHidden { get; set; }
    }
}
