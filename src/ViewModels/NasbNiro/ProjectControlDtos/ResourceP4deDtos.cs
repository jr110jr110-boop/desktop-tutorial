using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProjectControl;

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    // Project control priority 4, phases 4d and 4e: MS Project import, actual cost and performance of resources, cost proposal, charter list, report section.

    // ------------------------------------------------------------------------------------------ 4d-1: MS Project import

    public class PcMspNewResourceDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public PcResourceKind Kind { get; set; }
        public PcResourceUnit Unit { get; set; }
        public decimal Capacity { get; set; } = 1m;
        public string ResponsibleUnit { get; set; }
    }

    /// <summary>The reviewed mapping of one MS Project resource: an existing pool resource, or a new one to create (planning and the PMO only); neither = not imported.</summary>
    public class PcMspResourceMapDto
    {
        public int MspUid { get; set; }
        public int? ResourceId { get; set; }
        public PcMspNewResourceDto Create { get; set; }
    }

    public class PcMspImportRequestDto
    {
        public int ContractId { get; set; }

        /// <summary>The text of the MSPDI file («Save As» XML of Microsoft Project).</summary>
        public string Xml { get; set; }
        public List<PcMspResourceMapDto> Resources { get; set; } = new List<PcMspResourceMapDto>();
    }

    public class PcMspResourceRowDto
    {
        public int MspUid { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }

        /// <summary>The MSPDI type: 0 material, 1 work, 2 cost.</summary>
        public int Type { get; set; }

        /// <summary>code, title, normalized or none.</summary>
        public string MatchKind { get; set; }
        public int? SuggestedResourceId { get; set; }
        public string SuggestedCode { get; set; }
        public string SuggestedTitle { get; set; }
        public int AssignmentCount { get; set; }
    }

    public class PcMspAssignmentRowDto
    {
        public int TaskUid { get; set; }
        public string TaskName { get; set; }
        public string Wbs { get; set; }
        public int? ActivityId { get; set; }
        public string ActivityCode { get; set; }

        /// <summary>wbs, code, title or none.</summary>
        public string ActivityMatch { get; set; }
        public int MspResourceUid { get; set; }
        public string MspResourceName { get; set; }
        public int? ResourceId { get; set; }
        public string ResourceCode { get; set; }
        public PcResourceUnit? Unit { get; set; }

        /// <summary>The work of the assignment in hours, as the file states it.</summary>
        public decimal WorkHours { get; set; }

        /// <summary>The quantity in the unit of the pool resource (hours; days by the day length of the project; the units of a material resource).</summary>
        public decimal Quantity { get; set; }

        /// <summary>ok, resourceUnmatched, activityUnmatched, zeroWork, duplicate.</summary>
        public string Status { get; set; }
        public string StatusText { get; set; }
    }

    public class PcMspCountsDto
    {
        public int Assignments { get; set; }
        public int Ok { get; set; }
        public int ResourceUnmatched { get; set; }
        public int ActivityUnmatched { get; set; }
        public int ZeroWork { get; set; }
        public int Duplicate { get; set; }
        public int ResourcesInFile { get; set; }
        public int ResourcesMatched { get; set; }
    }

    public class PcMspPreviewDto
    {
        public int ContractId { get; set; }
        public int PlanId { get; set; }
        public string ProjectName { get; set; }

        /// <summary>The fixed sentence that says the rates of the file are not imported.</summary>
        public string RatesNote { get; set; }

        /// <summary>The fixed sentence that says rows of the plan that are not in the file are not removed.</summary>
        public string KeepNote { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<PcMspResourceRowDto> Resources { get; set; } = new List<PcMspResourceRowDto>();
        public List<PcMspAssignmentRowDto> Rows { get; set; } = new List<PcMspAssignmentRowDto>();
        public PcMspCountsDto Counts { get; set; } = new PcMspCountsDto();
    }

    public class PcMspImportResultDto
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int ResourcesCreated { get; set; }
        public int SkippedResourceUnmatched { get; set; }
        public int SkippedActivityUnmatched { get; set; }
        public int SkippedZeroWork { get; set; }
        public int SkippedDuplicate { get; set; }
        public int Total { get; set; }
        public string RatesNote { get; set; }

        /// <summary>The fixed sentence that says rows of the plan that are not in the file are not removed.</summary>
        public string KeepNote { get; set; }
        public PcResourcePlanDto Plan { get; set; }
    }

    // ------------------------------------------------------------------------------------------ 4d-3: budget and performance of the resources of a project

    public class PcPerfRowDto
    {
        public int? ResourceId { get; set; }
        public string ResourceCode { get; set; }
        public string ResourceTitle { get; set; }
        public PcResourceClass ResourceClass { get; set; }
        public PcResourceUnit? Unit { get; set; }

        /// <summary>The quantity frozen in the current baseline and its amount at the rate of that day (null when hidden or without a rate).</summary>
        public decimal BaselineQuantity { get; set; }
        public decimal? BaselineAmount { get; set; }

        /// <summary>The planned value up to the data date, from the baseline spread over its dates.</summary>
        public decimal PlannedToDateQuantity { get; set; }
        public decimal? PlannedToDateAmount { get; set; }

        /// <summary>The newest approved resource plan at the effective rate of the data date.</summary>
        public decimal PlanQuantity { get; set; }
        public decimal? PlanAmount { get; set; }

        /// <summary>Actuals up to the data date; null when the data model does not tie an actual to this line (see <see cref="ActualNote"/>).</summary>
        public decimal? ActualQuantity { get; set; }
        public decimal? ActualAmount { get; set; }
        public string ActualBasis { get; set; }
        public string ActualNote { get; set; }

        /// <summary>Actual minus the planned value up to the data date (quantity and amount), when both exist.</summary>
        public decimal? VarianceQuantity { get; set; }
        public decimal? VarianceAmount { get; set; }

        /// <summary>Current plan minus baseline at completion (amount).</summary>
        public decimal? PlanVsBaselineAmount { get; set; }

        public bool NoApprovedRate { get; set; }
        public bool RateHidden { get; set; }
        public string Note { get; set; }
    }

    public class PcPerfPeriodDto
    {
        /// <summary>The Persian month, yyyy/mm.</summary>
        public string Key { get; set; }
        public string Text { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public PcResourceClass ResourceClass { get; set; }
        public decimal Quantity { get; set; }
        public decimal? Amount { get; set; }
    }

    public class PcPerfTieDto
    {
        public string Source { get; set; }
        public bool Tied { get; set; }
        public string Note { get; set; }
    }

    public class PcResourcePerformanceDto
    {
        public int ContractId { get; set; }
        public DateTime AsOf { get; set; }
        public bool HasBaseline { get; set; }
        public int? BaselineId { get; set; }
        public int? BaselineNumber { get; set; }
        public int? PlanId { get; set; }
        public int? PlanVersion { get; set; }
        public List<PcPerfRowDto> Rows { get; set; } = new List<PcPerfRowDto>();
        public List<PcPerfRowDto> ByClass { get; set; } = new List<PcPerfRowDto>();
        public List<PcPerfPeriodDto> Periods { get; set; } = new List<PcPerfPeriodDto>();
        public List<PcPerfTieDto> Ties { get; set; } = new List<PcPerfTieDto>();

        /// <summary>Totals of the lines whose amount the viewer may see.</summary>
        public decimal BaselineTotal { get; set; }
        public decimal PlannedToDateTotal { get; set; }
        public decimal PlanTotal { get; set; }
        public decimal ActualTotal { get; set; }
        public int NoRateCount { get; set; }
        public int HiddenLineCount { get; set; }
        public bool RateHidden { get; set; }
    }

    // ------------------------------------------------------------------------------------------ 4d-3: cost-baseline proposal

    public class PcResourceCostProposalDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public string ContractCode { get; set; }
        public int PlanId { get; set; }
        public int PlanVersion { get; set; }
        public PcResourceClass ResourceClass { get; set; }

        /// <summary>Null (RateHidden) for a viewer who may not see what could be one small group's cost.</summary>
        public decimal? Amount { get; set; }

        /// <summary>What the cost baseline already held for this class when the proposal was made; <see cref="Amount"/> is the line to add (the difference).</summary>
        public decimal ExistingAmount { get; set; }
        public decimal ProposedTotal => (Amount ?? 0m) + ExistingAmount;
        public string RowVersion { get; set; }
        public bool RateHidden { get; set; }
        public int ExcludedNoRate { get; set; }
        public int ExcludedSensitive { get; set; }
        public int LineCount { get; set; }
        public PcCostProposalStatus Status { get; set; }
        public bool PreparedByAgent { get; set; }
        public int? CostLineId { get; set; }
        public string RejectReason { get; set; }
        public DateTimeOffset CreateDate { get; set; }
    }

    public class PcCostProposalDecideDto
    {
        public int Id { get; set; }

        /// <summary>accept or reject.</summary>
        public string Action { get; set; }
        public string Reason { get; set; }

        /// <summary>The version of the proposal that the person saw (required: a decision on an older version is refused).</summary>
        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ 4d-4: charter list

    public class PcCharterResourceDto
    {
        public int ResourceId { get; set; }
        public string ResourceCode { get; set; }
        public string ResourceTitle { get; set; }
        public decimal Quantity { get; set; }
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------ 4e: the resource section of the weekly report

    public class PcWeeklyResourceTopDto
    {
        public int ResourceId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public PcResourceUnit Unit { get; set; }

        /// <summary>The load of the project on the resource in the next four weeks, in the unit of the resource (a quantity, never an amount).</summary>
        public decimal Load { get; set; }
    }

    public class PcWeeklyResourcesDto
    {
        public int OpenOverloads { get; set; }
        public int AcceptedOverloads { get; set; }
        public int LapsedOverloads { get; set; }
        public int PlansWaitingApproval { get; set; }
        public int ResourcesWithoutRate { get; set; }
        public List<PcWeeklyResourceTopDto> Top { get; set; } = new List<PcWeeklyResourceTopDto>();
    }
}
