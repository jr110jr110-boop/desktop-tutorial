using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Sales;

namespace ViewModels.NasbNiro.SalesDtos
{
    // ------------------------------------------------------------------------------------------ inquiries

    public class SlInquiryQuery : SlPageQuery
    {
        public SlInquiryStatus? Status { get; set; }
        public int? CustomerId { get; set; }
        public SlProductType? ProductType { get; set; }
    }

    public class SlInquiryDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime ReceivedDate { get; set; }
        public string ReceivedDateText { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int? ContactId { get; set; }
        public string Channel { get; set; }
        public string CustomerReference { get; set; }
        public string Title { get; set; }
        public SlProductType ProductType { get; set; }
        public string ProductTypeText { get; set; }
        public decimal Quantity { get; set; }
        public DateTime? RequiredDeliveryDate { get; set; }
        public string RequiredDeliveryDateText { get; set; }
        public DateTime? ProposalDueDate { get; set; }
        public string ProposalDueDateText { get; set; }
        public string AttachmentIds { get; set; }
        public SlInquiryStatus Status { get; set; }
        public string StatusText { get; set; }
        public int? TenderId { get; set; }
        public string Note { get; set; }
        public string CancelReason { get; set; }
        public string RowVersion { get; set; }
    }

    public class SlSaveInquiryDto
    {
        public int? Id { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public int CustomerId { get; set; }
        public int? ContactId { get; set; }
        public string Channel { get; set; }
        public string CustomerReference { get; set; }
        public string Title { get; set; }
        public SlProductType ProductType { get; set; } = SlProductType.Other;
        public decimal Quantity { get; set; } = 1;
        public DateTime? RequiredDeliveryDate { get; set; }
        public DateTime? ProposalDueDate { get; set; }
        public string AttachmentIds { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action "review" (start the review) or "cancel" (with a reason).</summary>
    public class SlInquiryActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ bid decision

    public class SlBidDecisionQuery : SlPageQuery
    {
        public SlBidStatus? Status { get; set; }
        public int? InquiryId { get; set; }
    }

    public class SlBidDecisionDto
    {
        public int Id { get; set; }
        public int InquiryId { get; set; }
        public string InquiryNumber { get; set; }
        public string InquiryTitle { get; set; }
        public string CustomerName { get; set; }
        public SlBidStatus Status { get; set; }
        public bool? TechnicalCapabilityOk { get; set; }
        public string TechnicalNote { get; set; }
        public int? CapacityOverloadWeeks { get; set; }
        public DateTime? CapacityCheckedUntil { get; set; }
        public string CapacityCheckedUntilText { get; set; }
        public string CapacityNote { get; set; }
        public bool CommercialRiskReviewed { get; set; }
        public string CommercialRiskNote { get; set; }
        public bool PaymentTermsReviewed { get; set; }
        public string PaymentTermsNote { get; set; }
        public bool GuaranteesReviewed { get; set; }
        public string GuaranteesNote { get; set; }
        public SlSanctionCheck SanctionCheck { get; set; }
        public string SanctionNote { get; set; }
        public decimal? ExpectedMarginPercent { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string SubmittedAtText { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecidedAtText { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecidedByName { get; set; }
        public string DecisionReason { get; set; }
        public int? TenderId { get; set; }
        public string RowVersion { get; set; }
    }

    public class SlSaveBidDecisionDto
    {
        public int InquiryId { get; set; }
        public bool? TechnicalCapabilityOk { get; set; }
        public string TechnicalNote { get; set; }
        public string CapacityNote { get; set; }
        public bool CommercialRiskReviewed { get; set; }
        public string CommercialRiskNote { get; set; }
        public bool PaymentTermsReviewed { get; set; }
        public string PaymentTermsNote { get; set; }
        public bool GuaranteesReviewed { get; set; }
        public string GuaranteesNote { get; set; }
        public SlSanctionCheck SanctionCheck { get; set; }
        public string SanctionNote { get; set; }
        public decimal? ExpectedMarginPercent { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Submit the checklist (sales), or decide (the CEO): Decision is "bid" or "nobid".</summary>
    public class SlBidActionDto
    {
        public int InquiryId { get; set; }
        public string Decision { get; set; }
        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ estimates

    public class SlEstimateQuery : SlPageQuery
    {
        public int? InquiryId { get; set; }
        public SlEstimateStatus? Status { get; set; }
    }

    public class SlEstimateEquipmentDto
    {
        public int EquipmentId { get; set; }
        public string EquipmentName { get; set; }
        public int Count { get; set; }
    }

    public class SlEstimateLineDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public SlCostKind Kind { get; set; }
        public string Description { get; set; }
        public int? EquipmentId { get; set; }
        public int? EquipmentBomId { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public int? WorkCenterId { get; set; }
        public string WorkCenterName { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal? UnitCost { get; set; }
        public SlCostSource CostSource { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
    }

    public class SlEstimateRiskDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public decimal ProbabilityPercent { get; set; }
        public decimal ImpactAmount { get; set; }
        public decimal ExpectedCost { get; set; }
    }

    public class SlEstimateDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int InquiryId { get; set; }
        public string InquiryNumber { get; set; }
        public int Revision { get; set; }
        public SlEstimateStatus Status { get; set; }
        public string Note { get; set; }
        public decimal? OverheadPercent { get; set; }
        public decimal? ContingencyPercent { get; set; }
        public decimal? TargetMarginPercent { get; set; }
        public decimal? GuaranteeAmount { get; set; }
        public decimal? GuaranteeMonths { get; set; }
        public decimal? GuaranteeRatePercentPerMonth { get; set; }
        public decimal? FinancingAmount { get; set; }
        public decimal? FinancingMonths { get; set; }
        public decimal? FinancingRatePercentPerMonth { get; set; }
        public decimal MaterialCost { get; set; }
        public decimal LabourCost { get; set; }
        public decimal EngineeringCost { get; set; }
        public decimal SubcontractCost { get; set; }
        public decimal PackingTransportCost { get; set; }
        public decimal DirectCost { get; set; }
        public decimal GuaranteeCost { get; set; }
        public decimal FinancingCost { get; set; }
        public decimal RiskCost { get; set; }
        public decimal ContingencyCost { get; set; }
        public decimal CostBeforeOverhead { get; set; }
        public decimal OverheadCost { get; set; }
        public decimal TotalCost { get; set; }
        public decimal? Price { get; set; }
        public int MissingCostLines { get; set; }
        public string MissingInputs { get; set; }
        public DateTimeOffset? FinalizedAt { get; set; }
        public string FinalizedAtText { get; set; }
        public List<SlEstimateEquipmentDto> Equipments { get; set; } = new List<SlEstimateEquipmentDto>();
        public List<SlEstimateLineDto> Lines { get; set; } = new List<SlEstimateLineDto>();
        public List<SlEstimateRiskDto> Risks { get; set; } = new List<SlEstimateRiskDto>();
        public string RowVersion { get; set; }
    }

    public class SlSaveEstimateLineDto
    {
        public SlCostKind Kind { get; set; }
        public string Description { get; set; }
        public int? EquipmentId { get; set; }
        public int? EquipmentBomId { get; set; }
        public int? ItemId { get; set; }
        public int? WorkCenterId { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }

        /// <summary>Empty: the system fills it from the cost sources (average cost, last purchase, work-centre rate); still empty means the cost is missing.</summary>
        public decimal? UnitCost { get; set; }

        public string Note { get; set; }
    }

    public class SlSaveEstimateRiskDto
    {
        public string Title { get; set; }
        public decimal ProbabilityPercent { get; set; }
        public decimal ImpactAmount { get; set; }
    }

    /// <summary>Saves a draft estimate. Lines and risks replace the previous ones.</summary>
    public class SlSaveEstimateDto
    {
        public int? Id { get; set; }
        public int InquiryId { get; set; }
        public string Note { get; set; }
        public decimal? OverheadPercent { get; set; }
        public decimal? ContingencyPercent { get; set; }
        public decimal? TargetMarginPercent { get; set; }
        public decimal? GuaranteeAmount { get; set; }
        public decimal? GuaranteeMonths { get; set; }
        public decimal? GuaranteeRatePercentPerMonth { get; set; }
        public decimal? FinancingAmount { get; set; }
        public decimal? FinancingMonths { get; set; }
        public decimal? FinancingRatePercentPerMonth { get; set; }
        public List<SlSaveEstimateLineDto> Lines { get; set; } = new List<SlSaveEstimateLineDto>();
        public List<SlSaveEstimateRiskDto> Risks { get; set; } = new List<SlSaveEstimateRiskDto>();
        public string RowVersion { get; set; }
    }

    public class SlEstimateEquipmentInputDto
    {
        public int EquipmentId { get; set; }
        public int Count { get; set; } = 1;
    }

    /// <summary>The system drafts the estimate lines from equipment templates (BOM x cost sources, routing standards x work-centre rates).</summary>
    public class SlEstimateFromEquipmentDto
    {
        public int InquiryId { get; set; }
        public List<SlEstimateEquipmentInputDto> Equipments { get; set; } = new List<SlEstimateEquipmentInputDto>();
        public decimal? OverheadPercent { get; set; }
        public decimal? ContingencyPercent { get; set; }
        public decimal? TargetMarginPercent { get; set; }
    }

    /// <summary>Action "finalize" (draft to final) or "revise" (a new draft revision, the old one is superseded).</summary>
    public class SlEstimateActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ proposals

    public class SlProposalQuery : SlPageQuery
    {
        public SlProposalStatus? Status { get; set; }
        public int? InquiryId { get; set; }
        public int? CustomerId { get; set; }
    }

    public class SlProposalDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int Revision { get; set; }
        public int InquiryId { get; set; }
        public string InquiryNumber { get; set; }
        public string InquiryTitle { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public SlProductType ProductType { get; set; }
        public int EstimateId { get; set; }
        public Guid? TechnicalDocumentId { get; set; }
        public Guid? CommercialDocumentId { get; set; }
        public int? ValidityDays { get; set; }
        public decimal Price { get; set; }
        public int? DeliveryWeeks { get; set; }
        public string PaymentTermsText { get; set; }
        public string GuaranteesText { get; set; }
        public string DeviationsText { get; set; }
        public decimal? VatPercent { get; set; }
        public decimal? AdvancePercent { get; set; }
        public decimal? RetentionPercent { get; set; }
        public decimal? LatePenaltyPercentPerWeek { get; set; }
        public decimal? PenaltyCapPercent { get; set; }
        public int? ClientReviewDays { get; set; }
        public SlProposalStatus Status { get; set; }
        public string StatusText { get; set; }
        public decimal? EstimateTotalCost { get; set; }
        public bool BelowCost { get; set; }
        public bool NeedsCeo { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApprovedAtText { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string ApprovedByName { get; set; }
        public string ApprovalNote { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string SubmittedAtText { get; set; }
        public string CustomerReference { get; set; }
        public DateTimeOffset? ResultAt { get; set; }
        public string ResultAtText { get; set; }
        public int? ResultReasonId { get; set; }
        public string ResultReason { get; set; }
        public string Competitor { get; set; }
        public decimal? CompetitorPrice { get; set; }
        public string LessonsLearned { get; set; }
        public int? ContractId { get; set; }

        /// <summary>The contract review drafted when the proposal was won (project control).</summary>
        public int? ContractReviewId { get; set; }
        public string RowVersion { get; set; }
    }

    public class SlSaveProposalDto
    {
        public int? Id { get; set; }
        public int InquiryId { get; set; }

        /// <summary>The final estimate the price comes from.</summary>
        public int EstimateId { get; set; }

        public Guid? TechnicalDocumentId { get; set; }
        public Guid? CommercialDocumentId { get; set; }
        public int? ValidityDays { get; set; }

        /// <summary>Rial, before VAT. Empty: the price of the estimate.</summary>
        public decimal? Price { get; set; }

        public int? DeliveryWeeks { get; set; }
        public string PaymentTermsText { get; set; }
        public string GuaranteesText { get; set; }
        public string DeviationsText { get; set; }
        public decimal? VatPercent { get; set; }
        public decimal? AdvancePercent { get; set; }
        public decimal? RetentionPercent { get; set; }
        public decimal? LatePenaltyPercentPerWeek { get; set; }
        public decimal? PenaltyCapPercent { get; set; }
        public int? ClientReviewDays { get; set; }
        public string CustomerReference { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Action "approve", "submit", "revise" or "cancel".</summary>
    public class SlProposalActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Result of a submitted proposal: Result is "won" or "lost". Won makes the contract.</summary>
    public class SlProposalResultDto
    {
        public int Id { get; set; }
        public string Result { get; set; }
        public int? ReasonId { get; set; }
        public string Competitor { get; set; }
        public decimal? CompetitorPrice { get; set; }
        public string LessonsLearned { get; set; }
        public string RowVersion { get; set; }
    }

    public class SlWinLossRowDto
    {
        public int Key { get; set; }
        public string Name { get; set; }
        public int Won { get; set; }
        public int Lost { get; set; }
        public decimal? HitRatePercent { get; set; }
        public decimal WonValue { get; set; }
        public decimal LostValue { get; set; }
    }

    public class SlWinLossDto
    {
        public List<SlWinLossRowDto> ByProduct { get; set; } = new List<SlWinLossRowDto>();
        public List<SlWinLossRowDto> ByCustomer { get; set; } = new List<SlWinLossRowDto>();
        public List<SlWinLossRowDto> ByReason { get; set; } = new List<SlWinLossRowDto>();
        public int Won { get; set; }
        public int Lost { get; set; }
        public decimal? HitRatePercent { get; set; }
    }

    public class SlWinLossQuery
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }
}
