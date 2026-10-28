using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProjectControl;

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    // ---------------------------------------------------------------- baselines

    public class PcBaselineSignatureDto
    {
        public string UnitRole { get; set; }
        public int? UserId { get; set; }
        public string UserName { get; set; }
        public DateTimeOffset? SignedAt { get; set; }
    }

    public class PcBaselineDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public int Number { get; set; }
        public PcBaselineKind Kind { get; set; }
        public PcBaselineStatus Status { get; set; }
        public string Reason { get; set; }
        public int? ChangeRequestId { get; set; }
        public DateTime? ProjectStart { get; set; }
        public DateTime? ProjectFinish { get; set; }
        public decimal Bac { get; set; }
        public decimal ManagementReserve { get; set; }
        public int ActivityCount { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public bool IsCurrent { get; set; }
        public List<PcBaselineSignatureDto> Signatures { get; set; } = new List<PcBaselineSignatureDto>();
        public string RowVersion { get; set; }
    }

    public class PcBaselineCreateDto
    {
        public int ContractId { get; set; }
        public string Reason { get; set; }
    }

    public class PcBaselineActionDto
    {
        public int Id { get; set; }

        /// <summary>sign, approve</summary>
        public string Action { get; set; }
        public string Note { get; set; }
    }

    public class PcCostLineDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public PcCostClass CostClass { get; set; }
        public PcResourceClass? ResourceClass { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; }
    }

    // ---------------------------------------------------------------- change control

    public class PcChangeScenarioItemDto
    {
        public int ActivityId { get; set; }
        public int DurationDelta { get; set; }
        public decimal BudgetDelta { get; set; }
    }

    public class PcChangeBoardDto
    {
        public string Role { get; set; }
        public int? UserId { get; set; }
        public string UserName { get; set; }
        public PcVote Vote { get; set; }
        public DateTimeOffset? VotedAt { get; set; }
        public string Comment { get; set; }
    }

    public class PcChangeDto
    {
        public int? Id { get; set; }
        public string Number { get; set; }
        public int ContractId { get; set; }
        public PcChangeOrigin Origin { get; set; }
        public PcChangeType Type { get; set; }
        public int RequesterUserId { get; set; }
        public string ManagerAgreement { get; set; }
        public string Description { get; set; }
        public string Reason { get; set; }
        public PcChangeStatus Status { get; set; }
        public DateTimeOffset? RegisteredAt { get; set; }
        public DateTime? DueDate { get; set; }
        public bool Escalated { get; set; }
        public List<PcChangeScenarioItemDto> Scenario { get; set; } = new List<PcChangeScenarioItemDto>();
        public decimal RequestedPriceDelta { get; set; }
        public int RequestedDaysDelta { get; set; }

        public int? ImpactDeltaDays { get; set; }
        public decimal? ImpactDeltaCost { get; set; }
        public bool? ImpactOnCriticalPath { get; set; }
        public int AffectedPoLines { get; set; }
        public int AffectedItpPoints { get; set; }
        public DateTimeOffset? AnalysedAt { get; set; }
        public string ImpactScope { get; set; }
        public string ImpactResources { get; set; }
        public string ImpactQuality { get; set; }
        public string ImpactRisk { get; set; }
        public string ImpactProcurement { get; set; }
        public string ImpactClientContract { get; set; }
        public string Recommendation { get; set; }
        public List<PcImpactActivityDto> ImpactActivities { get; set; } = new List<PcImpactActivityDto>();

        public PcChangeDecision Decision { get; set; }
        public string DecisionNote { get; set; }
        public string DecidedByRole { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public int? BaselineId { get; set; }
        public List<PcChangeBoardDto> Board { get; set; } = new List<PcChangeBoardDto>();
        public string RowVersion { get; set; }
    }

    public class PcImpactActivityDto
    {
        public int ActivityId { get; set; }
        public string Code { get; set; }
        public DateTime? OldFinish { get; set; }
        public DateTime? NewFinish { get; set; }
        public bool Critical { get; set; }
    }

    public class PcChangeActionDto
    {
        public int Id { get; set; }

        /// <summary>register, analyze, recommend, vote, decide, close</summary>
        public string Action { get; set; }
        public string Note { get; set; }
        public PcVote Vote { get; set; }
        public string BoardRole { get; set; }
        public PcChangeDecision Decision { get; set; }

        /// <summary>Impact text fields supplied with "analyze".</summary>
        public string ImpactScope { get; set; }
        public string ImpactResources { get; set; }
        public string ImpactQuality { get; set; }
        public string ImpactRisk { get; set; }
        public string ImpactProcurement { get; set; }
        public string ImpactClientContract { get; set; }
    }

    public class PcChangeQuery : PcContractQuery
    {
        public PcChangeStatus? Status { get; set; }
    }

    public class PcAuthorityDto
    {
        public int? Id { get; set; }
        public string Role { get; set; }
        public bool CanDecide { get; set; }
        public decimal? MaxAmount { get; set; }
        public int? MaxDelayDays { get; set; }
        public string Note { get; set; }
    }

    // ---------------------------------------------------------------- claims

    public class PcClaimDto
    {
        public int? Id { get; set; }
        public string Number { get; set; }
        public int ContractId { get; set; }
        public PcClaimDirection Direction { get; set; }
        public string Issue { get; set; }
        public string Basis { get; set; }
        public int? DaysQuantified { get; set; }
        public decimal? AmountQuantified { get; set; }
        public PcClaimStatus Status { get; set; }
        public bool IsCrossClaim { get; set; }
        public bool PmoApproved { get; set; }
        public bool DesignManufacturingApproved { get; set; }
        public string ReviewNote { get; set; }
        public string RowVersion { get; set; }

        // R1a2 (H4): read-only (the save ignores them): who quantified and who approved, and when. R1a2 (L-7): and who wrote the text last (none of them can approve)
        public int? QuantifiedByUserId { get; set; }
        public int? LastEditedByUserId { get; set; }
        public int? PmoApprovedByUserId { get; set; }
        public DateTimeOffset? PmoApprovedAt { get; set; }
        public int? DesignApprovedByUserId { get; set; }
        public DateTimeOffset? DesignApprovedAt { get; set; }
    }

    public class PcClaimActionDto
    {
        public int Id { get; set; }

        /// <summary>quantify, notify, negotiate, settle, withdraw, litigate, approve-pmo, approve-design</summary>
        public string Action { get; set; }
        public int? Days { get; set; }
        public decimal? Amount { get; set; }
        public string Note { get; set; }
    }

    // ---------------------------------------------------------------- shipment

    public class PcShipmentDto
    {
        public int? Id { get; set; }
        public string Number { get; set; }
        public int ContractId { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int? ReleaseNoteId { get; set; }
        public PcShipmentStatus Status { get; set; }
        public string PackingList { get; set; }
        public string PackingVerification { get; set; }
        public Guid? PackingPhotoDocumentId { get; set; }
        public string ShippingDocuments { get; set; }
        public string Waybill { get; set; }
        public DateTime? ShippedAt { get; set; }
        public string DeliveryReceiptNumber { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public Guid? SignedReceiptDocumentId { get; set; }
        public string HandoverMinutesNumber { get; set; }
        public DateTime? HandoverAt { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcShipmentActionDto
    {
        public int Id { get; set; }

        /// <summary>pack, release, ship, deliver, handover</summary>
        public string Action { get; set; }
        public string PackingList { get; set; }
        public string PackingVerification { get; set; }
        public Guid? PackingPhotoDocumentId { get; set; }
        public string ShippingDocuments { get; set; }
        public string Waybill { get; set; }
        public DateTime? Date { get; set; }
        public string DeliveryReceiptNumber { get; set; }
        public Guid? SignedReceiptDocumentId { get; set; }
        public string HandoverMinutesNumber { get; set; }
    }

    public class PcShipmentQuery : PcContractQuery
    {
        public int? WorkOrderId { get; set; }
        public PcShipmentStatus? Status { get; set; }
    }

    // ---------------------------------------------------------------- reports

    public class PcReportRequestDto
    {
        public int ContractId { get; set; }
        public PcReportKind Kind { get; set; }
        public DateTime? DataDate { get; set; }
    }

    public class PcReportDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public PcReportKind Kind { get; set; }
        public int ContractId { get; set; }
        public DateTime DataDate { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }
        public string Hash { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public string Content { get; set; }
    }

    public class PcReportQuery : PcContractQuery
    {
        public PcReportKind? Kind { get; set; }
    }

    public class PcClientReportContent
    {
        public string ProjectCode { get; set; }
        public string ContractTitle { get; set; }
        public DateTime DataDate { get; set; }
        public decimal PhysicalPercent { get; set; }
        public decimal PlannedPercent { get; set; }
        public DateTime? BaselineFinish { get; set; }
        public DateTime? ForecastFinish { get; set; }
        public List<PcPhaseProgressDto> Phases { get; set; } = new List<PcPhaseProgressDto>();
        public int DocumentsSubmitted { get; set; }
        public int DocumentsApproved { get; set; }
        public int DocumentsAwaitingResponse { get; set; }
        public List<string> Milestones { get; set; } = new List<string>();

        /// <summary>Filled only when the contract allows it (ClientReportShowsIndices).</summary>
        public decimal? Spi { get; set; }
        public decimal? Cpi { get; set; }
    }

    public class PcPortfolioRowDto
    {
        public int ContractId { get; set; }
        public string ContractTitle { get; set; }
        public string ProjectCode { get; set; }
        public decimal PhysicalPercent { get; set; }
        public decimal PlannedPercent { get; set; }
        public decimal? Spi { get; set; }
        public decimal? Cpi { get; set; }
        public string SpiStatus { get; set; }
        public string CpiStatus { get; set; }
        public DateTime? ForecastFinish { get; set; }
        public int LateActivities { get; set; }
        public int OpenChanges { get; set; }
        public int GuaranteeAlerts { get; set; }
        public int OverdueMilestones { get; set; }
        public int OverdueInitiationSteps { get; set; }
        public int OverdueClientResponses { get; set; }
        public decimal PenaltyExposure { get; set; }
    }
}

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    /// <summary>New fields of the CEO dashboard (all computed, read only).</summary>
    public class PcDashboardDto
    {
        public int ActiveProjects { get; set; }
        public int ProjectsRed { get; set; }
        public int ProjectsAmber { get; set; }
        public int LateActivities { get; set; }
        public int OpenChangeRequests { get; set; }
        public int OverdueChangeRequests { get; set; }
        public int GuaranteeAlerts { get; set; }
        public int OverdueMilestones { get; set; }
        public int OverdueInitiationSteps { get; set; }
        public int OverdueClientResponses { get; set; }
        public decimal PenaltyExposure { get; set; }
        public List<PcPortfolioRowDto> Worst { get; set; } = new List<PcPortfolioRowDto>();
    }
}
