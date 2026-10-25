using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Sales
{
    // Sales (release S1 part A). All tables start with "Sl". The customer master is FnCustomer of finance: it is extended by a side table, never duplicated.
    // Money is in rials (one company, rial only); every percentage is an input.

    /// <summary>A number the company can change (for example the proposal approval threshold). No value is seeded.</summary>
    public class SlSetting : BaseEntity
    {
        public string Key { get; set; }
        public decimal? Value { get; set; }
        public string Title { get; set; }
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------ customers (CRM)

    /// <summary>1:1 side table of FnCustomer: the sales view of the customer.</summary>
    public class SlCustomerProfile : BaseEntity
    {
        public int CustomerId { get; set; }
        public SlCustomerType? Type { get; set; }
        public SlIndustry? Industry { get; set; }
        public string Country { get; set; }
        public string City { get; set; }
        public SlCustomerStatus Status { get; set; } = SlCustomerStatus.Prospect;

        /// <summary>The sales user who owns the account.</summary>
        public int? OwnerUserId { get; set; }

        /// <summary>Rial. Finance sets it; there is no default.</summary>
        public decimal? CreditLimitRial { get; set; }

        public int? CreditSetByUserId { get; set; }
        public DateTimeOffset? CreditSetAt { get; set; }
        public int? PaymentTermDays { get; set; }
        public string Note { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SlContact : BaseEntity
    {
        public int CustomerId { get; set; }
        public string FullName { get; set; }
        public string JobTitle { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class SlSite : BaseEntity
    {
        public int CustomerId { get; set; }
        public string Title { get; set; }
        public string Address { get; set; }
        public string Country { get; set; }
        public string CoordinatesText { get; set; }
    }

    public class SlActivity : BaseEntity
    {
        public int CustomerId { get; set; }
        public int? ContactId { get; set; }
        public SlActivityKind Kind { get; set; }
        public DateTime ActivityDate { get; set; }
        public string Subject { get; set; }
        public string Notes { get; set; }
        public DateTime? NextActionDate { get; set; }
        public int? InquiryId { get; set; }
        public int? ProposalId { get; set; }

        /// <summary>Release S1 part B (after-sales service): the request an activity belongs to. No foreign key yet.</summary>
        public int? ServiceRequestId { get; set; }

        public int OwnerUserId { get; set; }
    }

    /// <summary>Links a legacy contract or tender (free text employer) to the customer master without changing the legacy tables.</summary>
    public class SlCustomerLink : BaseEntity
    {
        public SlLinkSource SourceType { get; set; }
        public int SourceId { get; set; }
        public int CustomerId { get; set; }
        public SlLinkMethod Method { get; set; }
        public int LinkedByUserId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ inquiry, bid decision

    public class SlInquiry : BaseEntity
    {
        public string Number { get; set; }
        public DateTime ReceivedDate { get; set; }
        public int CustomerId { get; set; }
        public int? ContactId { get; set; }
        public string Channel { get; set; }
        public string CustomerReference { get; set; }
        public string Title { get; set; }
        public SlProductType ProductType { get; set; }
        public decimal Quantity { get; set; } = 1;
        public DateTime? RequiredDeliveryDate { get; set; }

        /// <summary>The deadline of the proposal.</summary>
        public DateTime? ProposalDueDate { get; set; }

        /// <summary>Document ids (uploaded through the documents API) of the RFQ package, separated by commas.</summary>
        public string AttachmentIds { get; set; }

        public SlInquiryStatus Status { get; set; } = SlInquiryStatus.Received;

        /// <summary>The tender record the system drafted when the company decided to bid.</summary>
        public int? TenderId { get; set; }

        public string Note { get; set; }
        public string CancelReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>The go / no-go gate. Sales prepares the checklist; only the CEO decides.</summary>
    public class SlBidDecision : BaseEntity
    {
        public int InquiryId { get; set; }
        public SlBidStatus Status { get; set; } = SlBidStatus.Draft;

        public bool? TechnicalCapabilityOk { get; set; }
        public string TechnicalNote { get; set; }

        /// <summary>Computed from the production load report (weeks above 100 % of the capacity between today and the required delivery).</summary>
        public int? CapacityOverloadWeeks { get; set; }

        public DateTime? CapacityCheckedUntil { get; set; }
        public DateTimeOffset? CapacityCheckedAt { get; set; }
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

        /// <summary>Everyone who prepared the checklist (",5,7,"): none of them decides.</summary>
        public string EditorUserIds { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecisionReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SlReasonCode : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public SlReasonKind Kind { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // ------------------------------------------------------------------------------------------ estimate

    /// <summary>A revision of the cost estimate of an inquiry.</summary>
    public class SlEstimate : BaseEntity
    {
        public string Number { get; set; }
        public int InquiryId { get; set; }
        public int Revision { get; set; }
        public SlEstimateStatus Status { get; set; } = SlEstimateStatus.Draft;
        public string Note { get; set; }

        // inputs (nothing is seeded: a missing value is a clear error when the price is asked for)
        public decimal? OverheadPercent { get; set; }
        public decimal? ContingencyPercent { get; set; }
        public decimal? TargetMarginPercent { get; set; }
        public decimal? GuaranteeAmount { get; set; }
        public decimal? GuaranteeMonths { get; set; }

        /// <summary>Percent of the guaranteed amount per month: cost = amount x rate x months.</summary>
        public decimal? GuaranteeRatePercentPerMonth { get; set; }

        public decimal? FinancingAmount { get; set; }
        public decimal? FinancingMonths { get; set; }
        public decimal? FinancingRatePercentPerMonth { get; set; }

        // results (stored for lists and the dashboard; recomputed on every save)
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

        /// <summary>Cost plus overhead: a proposal below it needs the CEO.</summary>
        public decimal TotalCost { get; set; }

        public decimal? Price { get; set; }
        public int MissingCostLines { get; set; }
        public string MissingInputs { get; set; }

        public string EditorUserIds { get; set; }
        public int? FinalizedByUserId { get; set; }
        public DateTimeOffset? FinalizedAt { get; set; }

        public ICollection<SlEstimateEquipment> Equipments { get; set; } = new List<SlEstimateEquipment>();
        public ICollection<SlEstimateLine> Lines { get; set; } = new List<SlEstimateLine>();
        public ICollection<SlEstimateRisk> Risks { get; set; } = new List<SlEstimateRisk>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SlEstimateEquipment
    {
        public int Id { get; set; }
        public int EstimateId { get; set; }
        public int EquipmentId { get; set; }
        public int Count { get; set; }
    }

    public class SlEstimateLine
    {
        public int Id { get; set; }
        public int EstimateId { get; set; }
        public int RowNumber { get; set; }
        public SlCostKind Kind { get; set; }
        public string Description { get; set; }
        public int? EquipmentId { get; set; }
        public int? EquipmentBomId { get; set; }
        public int? ItemId { get; set; }
        public int? WorkCenterId { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }

        /// <summary>Null: not known yet (the estimate cannot be finalised).</summary>
        public decimal? UnitCost { get; set; }

        public SlCostSource CostSource { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
    }

    public class SlEstimateRisk
    {
        public int Id { get; set; }
        public int EstimateId { get; set; }
        public string Title { get; set; }
        public decimal ProbabilityPercent { get; set; }
        public decimal ImpactAmount { get; set; }
        public decimal ExpectedCost { get; set; }
    }

    // ------------------------------------------------------------------------------------------ proposal

    public class SlProposal : BaseEntity
    {
        public string Number { get; set; }
        public int Revision { get; set; } = 1;
        public int InquiryId { get; set; }
        public int EstimateId { get; set; }
        public Guid? TechnicalDocumentId { get; set; }
        public Guid? CommercialDocumentId { get; set; }
        public int? ValidityDays { get; set; }

        /// <summary>Rial, before VAT.</summary>
        public decimal Price { get; set; }

        public int? DeliveryWeeks { get; set; }
        public string PaymentTermsText { get; set; }
        public string GuaranteesText { get; set; }
        public string DeviationsText { get; set; }

        // the terms the contract will start with (all inputs)
        public decimal? VatPercent { get; set; }
        public decimal? AdvancePercent { get; set; }
        public decimal? RetentionPercent { get; set; }
        public decimal? LatePenaltyPercentPerWeek { get; set; }
        public decimal? PenaltyCapPercent { get; set; }
        public int? ClientReviewDays { get; set; }

        public SlProposalStatus Status { get; set; } = SlProposalStatus.Draft;

        /// <summary>Cost plus overhead of the estimate when the proposal was approved or last saved (the below-cost test).</summary>
        public decimal? EstimateTotalCost { get; set; }

        public bool BelowCost { get; set; }
        public string EditorUserIds { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string ApprovalNote { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? SubmittedByUserId { get; set; }
        public string CustomerReference { get; set; }

        // result
        public DateTimeOffset? ResultAt { get; set; }
        public int? ResultReasonId { get; set; }
        public string Competitor { get; set; }
        public decimal? CompetitorPrice { get; set; }
        public string LessonsLearned { get; set; }
        public int? ContractId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ price lists, quotations, sales orders

    public class SlPriceList : BaseEntity
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public SlPriceListStatus Status { get; set; } = SlPriceListStatus.Draft;
        public string Note { get; set; }
        public string EditorUserIds { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public ICollection<SlPriceListLine> Lines { get; set; } = new List<SlPriceListLine>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SlPriceListLine
    {
        public int Id { get; set; }
        public int PriceListId { get; set; }
        public int ItemId { get; set; }

        /// <summary>Rial per unit; an input.</summary>
        public decimal UnitPrice { get; set; }

        public decimal MinQuantity { get; set; }
    }

    public class SlQuotation : BaseEntity
    {
        public string Number { get; set; }
        public DateTime QuotationDate { get; set; }
        public int CustomerId { get; set; }
        public int? ContactId { get; set; }
        public DateTime ValidUntil { get; set; }
        public SlQuotationStatus Status { get; set; } = SlQuotationStatus.Draft;
        public int? PriceListId { get; set; }
        public string Note { get; set; }
        public decimal NetAmount { get; set; }
        public int? SalesOrderId { get; set; }
        public ICollection<SlQuotationLine> Lines { get; set; } = new List<SlQuotationLine>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SlQuotationLine
    {
        public int Id { get; set; }
        public int QuotationId { get; set; }
        public int RowNumber { get; set; }
        public int ItemId { get; set; }
        public string Description { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal Amount { get; set; }
    }

    public class SlSalesOrder : BaseEntity
    {
        public string Number { get; set; }
        public DateTime OrderDate { get; set; }
        public int CustomerId { get; set; }
        public int? ContactId { get; set; }
        public int? QuotationId { get; set; }
        public string CustomerOrderNumber { get; set; }
        public DateTime? RequestedDeliveryDate { get; set; }
        public SlOrderStatus Status { get; set; } = SlOrderStatus.Draft;

        /// <summary>Before VAT.</summary>
        public decimal NetAmount { get; set; }

        /// <summary>Set when the last confirm attempt found the customer above the credit limit (or without one).</summary>
        public bool CreditApprovalRequired { get; set; }

        public string CreditReason { get; set; }
        public decimal? CreditExcess { get; set; }
        public int? CreditApprovedByUserId { get; set; }
        public DateTimeOffset? CreditApprovedAt { get; set; }

        /// <summary>The order amount (before VAT) the finance manager approved; a different amount needs a new approval.</summary>
        public decimal? CreditApprovedNetAmount { get; set; }

        public string CreditApprovalNote { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public int? ConfirmedByUserId { get; set; }
        public string CancelReason { get; set; }
        public string EditorUserIds { get; set; }
        public ICollection<SlSalesOrderLine> Lines { get; set; } = new List<SlSalesOrderLine>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SlSalesOrderLine
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int RowNumber { get; set; }
        public int ItemId { get; set; }
        public string Description { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal Amount { get; set; }

        /// <summary>Stock held for this line: what is ordered and not delivered yet while the order is confirmed.</summary>
        public decimal ReservedQuantity { get; set; }

        public decimal DeliveredQuantity { get; set; }
        public decimal InvoicedQuantity { get; set; }
    }

    /// <summary>A customer delivery document of a sales order and the invoice the system drafted for it.</summary>
    public class SlOrderDelivery : BaseEntity
    {
        public int SalesOrderId { get; set; }
        public int DocumentId { get; set; }
        public int? InvoiceId { get; set; }
        public string DraftNote { get; set; }
        public bool Reversed { get; set; }
    }
}
