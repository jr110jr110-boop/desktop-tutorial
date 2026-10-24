using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProcurementDtos
{
    // ---------------------------------------------------------------- purchase request

    public class SavePurchaseRequestDto
    {
        public int? Id { get; set; }
        public PurchaseRequestSource Source { get; set; }
        public PurchaseKind Kind { get; set; }
        public DateTime RequestDate { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public string CostCenter { get; set; }
        public int? MaterialRequestId { get; set; }
        public string Description { get; set; }
        public bool Submit { get; set; }
        public List<SavePurchaseRequestLineDto> Lines { get; set; } = new List<SavePurchaseRequestLineDto>();
    }

    public class SavePurchaseRequestLineDto
    {
        public int? ItemId { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public DateTime? NeededBy { get; set; }
        public decimal? EstimatedUnitPrice { get; set; }
        public string TechnicalRequirements { get; set; }
        public int? BuyOrderId { get; set; }
        public int? MaterialRequestLineId { get; set; }
    }

    public class ProcurementActionDto
    {
        public int Id { get; set; }
        /// <summary>Payment "paid" action: the bank account the money leaves from (required).</summary>
        public int? BankAccountId { get; set; }

        /// <summary>
        /// Request: submit, approve-planning, approve-management, reject, close.
        /// Inquiry: approve-financial, approve-commercial, approve-technical, approve-management, cancel.
        /// Order: approve, release, close, cancel. Invoice: approve, cancel. Payment: approve, paid, reject.
        /// </summary>
        public string Action { get; set; }
        public string Note { get; set; }

        /// <summary>Board resolution, committee minutes or payment reference, depending on the action.</summary>
        public string Reference { get; set; }
    }

    public class PurchaseRequestQuery : PaginationParams
    {
        public PurchaseRequestStatus? Status { get; set; }
        public int? WorkOrderId { get; set; }
        public string Contains { get; set; }
    }

    public class PurchaseRequestDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public PurchaseRequestSource Source { get; set; }
        public PurchaseKind Kind { get; set; }
        public PurchaseRequestStatus Status { get; set; }
        public DateTime RequestDate { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public string CostCenter { get; set; }
        public int? MaterialRequestId { get; set; }
        public string Description { get; set; }
        public decimal EstimatedAmount { get; set; }
        public int RequiredQuotes { get; set; }
        public bool NeedsManagement { get; set; }
        public bool NeedsBoard { get; set; }
        public string BoardResolution { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public int CreatorUserId { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public int? PlanningApprovedByUserId { get; set; }
        public string PlanningApprovedByName { get; set; }
        public DateTimeOffset? ManagementApprovedAt { get; set; }
        public int? ManagementApprovedByUserId { get; set; }
        public string ManagementApprovedByName { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }
        public List<PurchaseRequestLineDto> Lines { get; set; } = new List<PurchaseRequestLineDto>();
    }

    public class PurchaseRequestLineDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal OrderedQuantity { get; set; }
        public DateTime? NeededBy { get; set; }
        public decimal? EstimatedUnitPrice { get; set; }
        public string TechnicalRequirements { get; set; }
        public int? BuyOrderId { get; set; }
    }

    // ---------------------------------------------------------------- inquiry and quotes

    public class SaveInquiryDto
    {
        public int? Id { get; set; }
        public int PurchaseRequestId { get; set; }
        public DateTime InquiryDate { get; set; }
        public string Description { get; set; }
        public string FewerQuotesReason { get; set; }
        public List<SaveQuoteDto> Quotes { get; set; } = new List<SaveQuoteDto>();
    }

    public class SaveQuoteDto
    {
        public int ProviderId { get; set; }
        public string QuoteNumber { get; set; }
        public DateTime QuoteDate { get; set; }
        public DateTime? ValidUntil { get; set; }
        public int? DeliveryDays { get; set; }
        public string PaymentTerms { get; set; }
        public decimal? VatPercent { get; set; }
        public bool TechnicallyAccepted { get; set; }
        public decimal? TechnicalScore { get; set; }
        public string TechnicalNote { get; set; }
        public Guid? DocumentId { get; set; }
        public string Notes { get; set; }
        public List<SaveQuoteLineDto> Lines { get; set; } = new List<SaveQuoteLineDto>();
    }

    public class SaveQuoteLineDto
    {
        public int RequestLineId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class SelectQuoteDto
    {
        public int InquiryId { get; set; }
        public int QuoteId { get; set; }
        public string Reason { get; set; }
    }

    public class InquiryDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public InquiryStatus Status { get; set; }
        public DateTime InquiryDate { get; set; }
        public int PurchaseRequestId { get; set; }
        public string PurchaseRequestNumber { get; set; }
        public string Description { get; set; }
        public int RequiredQuotes { get; set; }
        public string FewerQuotesReason { get; set; }
        public int? SelectedQuoteId { get; set; }
        public string SelectionReason { get; set; }
        public bool NeedsCommittee { get; set; }
        public bool NeedsManagement { get; set; }
        public DateTimeOffset? FinancialApprovedAt { get; set; }
        public string FinancialApprovedByName { get; set; }
        public DateTimeOffset? CommercialApprovedAt { get; set; }
        public string CommercialApprovedByName { get; set; }
        public DateTimeOffset? TechnicalApprovedAt { get; set; }
        public string TechnicalApprovedByName { get; set; }
        public DateTimeOffset? ManagementApprovedAt { get; set; }
        public string ManagementApprovedByName { get; set; }
        public string CommitteeMinutes { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string PurchaseOrderNumber { get; set; }
        public List<QuoteDto> Quotes { get; set; } = new List<QuoteDto>();
    }

    public class QuoteDto
    {
        public int Id { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public SupplierGrade Grade { get; set; }
        public bool Blocked { get; set; }
        public string QuoteNumber { get; set; }
        public DateTime QuoteDate { get; set; }
        public DateTime? ValidUntil { get; set; }
        public int? DeliveryDays { get; set; }
        public string PaymentTerms { get; set; }
        public decimal? VatPercent { get; set; }
        public bool TechnicallyAccepted { get; set; }
        public decimal? TechnicalScore { get; set; }
        public string TechnicalNote { get; set; }
        public Guid? DocumentId { get; set; }
        public string Notes { get; set; }
        public decimal TotalAmount { get; set; }
        public bool Lowest { get; set; }
        public List<SaveQuoteLineDto> Lines { get; set; } = new List<SaveQuoteLineDto>();
    }

    // ---------------------------------------------------------------- purchase order

    public class SavePurchaseOrderDto
    {
        public int? Id { get; set; }
        public PurchaseKind Kind { get; set; }
        public DateTime OrderDate { get; set; }
        public int ProviderId { get; set; }
        public int? InquiryId { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public string Currency { get; set; }
        public decimal? ExchangeRate { get; set; }
        public string Incoterms { get; set; }
        public string PaymentTerms { get; set; }
        public decimal? VatPercent { get; set; }
        public decimal? AdvancePercent { get; set; }
        public string AdvanceGuarantee { get; set; }
        public DateTime? AdvanceGuaranteeExpiry { get; set; }
        public string PerformanceGuarantee { get; set; }
        public decimal? RetentionPercent { get; set; }
        public string ContractNumber { get; set; }
        public DateTime? ContractSignedAt { get; set; }
        public string Requirements { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string DeliveryPlace { get; set; }
        public decimal? LatePenaltyPercentPerWeek { get; set; }
        public string Description { get; set; }
        public List<SavePurchaseOrderLineDto> Lines { get; set; } = new List<SavePurchaseOrderLineDto>();
    }

    public class SavePurchaseOrderLineDto
    {
        public int? RequestLineId { get; set; }
        public int? ItemId { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }

        /// <summary>In the order currency; converted to rial with the exchange rate.</summary>
        public decimal UnitPrice { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        /// <summary>Capital purchase: the approved invoice line drafts a fixed asset for finance to complete.</summary>
        public bool IsCapital { get; set; }
    }

    public class PurchaseOrderQuery : PaginationParams
    {
        public PurchaseOrderStatus? Status { get; set; }
        public int? ProviderId { get; set; }

        /// <summary>Released orders that still have something to receive (warehouse entry permits).</summary>
        public bool? OpenForReceipt { get; set; }
        public string Contains { get; set; }
    }

    public class PurchaseOrderDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public PurchaseOrderStatus Status { get; set; }
        public PurchaseKind Kind { get; set; }
        public DateTime OrderDate { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public SupplierGrade Grade { get; set; }
        public int? InquiryId { get; set; }
        public string InquiryNumber { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public string Currency { get; set; }
        public decimal? ExchangeRate { get; set; }
        public string Incoterms { get; set; }
        public string PaymentTerms { get; set; }
        public decimal? VatPercent { get; set; }
        public decimal? AdvancePercent { get; set; }
        public string AdvanceGuarantee { get; set; }
        public DateTime? AdvanceGuaranteeExpiry { get; set; }
        public string PerformanceGuarantee { get; set; }
        public decimal? RetentionPercent { get; set; }
        public string ContractNumber { get; set; }
        public DateTime? ContractSignedAt { get; set; }
        public string Requirements { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string DeliveryPlace { get; set; }
        public decimal? LatePenaltyPercentPerWeek { get; set; }
        public string Description { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal ReceivedAmount { get; set; }
        public decimal InvoicedAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public int CreatorUserId { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }
        public string CancelReason { get; set; }
        public List<PurchaseOrderLineDto> Lines { get; set; } = new List<PurchaseOrderLineDto>();
    }

    public class PurchaseOrderLineDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public int? RequestLineId { get; set; }
        public string RequestNumber { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Amount { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public bool IsCapital { get; set; }

        /// <summary>Physically received (receipts minus returns) or accepted services.</summary>
        public decimal ReceivedQuantity { get; set; }

        /// <summary>Accepted by QC (or received without inspection): what can be invoiced.</summary>
        public decimal AcceptedQuantity { get; set; }
        public decimal InvoicedQuantity { get; set; }
        public decimal OpenQuantity { get; set; }
        public DateTime? LastReceiptDate { get; set; }
        public bool Late { get; set; }
    }

    public class SaveServiceAcceptanceDto
    {
        public int PurchaseOrderLineId { get; set; }
        public DateTime AcceptanceDate { get; set; }
        public decimal Quantity { get; set; }
        public string Notes { get; set; }
        public Guid? DocumentId { get; set; }
    }

    // ---------------------------------------------------------------- supplier invoice and payment

    public class SaveSupplierInvoiceDto
    {
        public int? Id { get; set; }
        public int PurchaseOrderId { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public bool IsOfficial { get; set; }
        public decimal? VatAmount { get; set; }
        public Guid? DocumentId { get; set; }
        public List<SaveSupplierInvoiceLineDto> Lines { get; set; } = new List<SaveSupplierInvoiceLineDto>();
    }

    public class SaveSupplierInvoiceLineDto
    {
        public int OrderLineId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class SupplierInvoiceDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public SupplierInvoiceStatus Status { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int PurchaseOrderId { get; set; }
        public string PurchaseOrderNumber { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public bool IsOfficial { get; set; }
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PriceVariance { get; set; }
        public string MatchNote { get; set; }
        public Guid? DocumentId { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApprovedByName { get; set; }
        public string CancelReason { get; set; }
        public List<SupplierInvoiceLineDto> Lines { get; set; } = new List<SupplierInvoiceLineDto>();
    }

    public class SupplierInvoiceLineDto
    {
        public int Id { get; set; }
        public int OrderLineId { get; set; }
        public string ItemCode { get; set; }
        public string Description { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal OrderUnitPrice { get; set; }
        public decimal Amount { get; set; }
    }

    public class SavePaymentRequestDto
    {
        public int? Id { get; set; }
        public PaymentRequestType Type { get; set; }
        public DateTime RequestDate { get; set; }
        public int? PurchaseOrderId { get; set; }
        public int? InvoiceId { get; set; }
        public decimal Amount { get; set; }
        public decimal Deductions { get; set; }
        public string DeductionNote { get; set; }
        public string Description { get; set; }
        /// <summary>Deductions by kind (withholding, retention...). Their sum may not exceed Deductions; when Deductions is 0 it becomes their sum.</summary>
        public List<PaymentDeductionDto> DeductionLines { get; set; } = new List<PaymentDeductionDto>();
    }

    public class PaymentDeductionDto
    {
        public FnDeductionKind Kind { get; set; }
        public decimal Base { get; set; }
        public decimal RatePercent { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
    }

    public class PaymentRequestDto
    {
        /// <summary>Set when the old one-click "paid" action created a draft treasury payment (review 2, finding 23).</summary>
        public int? TreasuryPaymentId { get; set; }
        public string TreasuryPaymentNumber { get; set; }
        public int Id { get; set; }
        public string Number { get; set; }
        public PaymentRequestType Type { get; set; }
        public PaymentRequestStatus Status { get; set; }
        public DateTime RequestDate { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string PurchaseOrderNumber { get; set; }
        public int? InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public decimal Amount { get; set; }
        public decimal Deductions { get; set; }
        public decimal NetAmount { get; set; }
        public string DeductionNote { get; set; }
        public bool QcApproved { get; set; }
        public string Description { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? PaidAt { get; set; }
        public string PaidByName { get; set; }
        public string PaymentReference { get; set; }
        public string RejectReason { get; set; }
        public List<PaymentDeductionDto> DeductionLines { get; set; } = new List<PaymentDeductionDto>();
    }

    public class ProcurementListQuery : PaginationParams
    {
        public int? Status { get; set; }
        public int? ProviderId { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string Contains { get; set; }
    }

    // ---------------------------------------------------------------- suppliers

    public class SupplierProfileDto
    {
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public SupplierGrade Grade { get; set; }
        public decimal? Score { get; set; }
        public DateTime? EvaluatedAt { get; set; }
        public DateTime? NextEvaluation { get; set; }
        public bool EvaluationDue { get; set; }
        public string Categories { get; set; }
        public bool IsStrategic { get; set; }
        public bool Blocked { get; set; }
        public string BlockReason { get; set; }
        public int Warnings { get; set; }
        public string SepidarCode { get; set; }
        public string NationalId { get; set; }
        public string EconomicCode { get; set; }
        public string Iban { get; set; }

        // performance (for the periodic evaluation)
        public int Orders { get; set; }
        public decimal OrderedAmount { get; set; }
        public int ReceiptLines { get; set; }
        public int LateReceiptLines { get; set; }
        public decimal? OnTimePercent { get; set; }
        public int Ncrs { get; set; }
        public int Returns { get; set; }
    }

    public class SaveSupplierProfileDto
    {
        public int ProviderId { get; set; }
        public string Categories { get; set; }
        public bool IsStrategic { get; set; }
        public bool Blocked { get; set; }
        public string BlockReason { get; set; }
        public string SepidarCode { get; set; }
        public string NationalId { get; set; }
        public string EconomicCode { get; set; }
        public string Iban { get; set; }
    }

    public class SaveSupplierEvaluationDto
    {
        public int ProviderId { get; set; }
        public EvaluationKind Kind { get; set; }
        public DateTime EvaluationDate { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string Notes { get; set; }
        public List<EvaluationLineDto> Lines { get; set; } = new List<EvaluationLineDto>();
    }

    public class EvaluationLineDto
    {
        public string Question { get; set; }
        public decimal Weight { get; set; }
        public decimal? Score { get; set; }
    }

    public class FnOpenAdvanceDto
    {
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int PurchaseOrderId { get; set; }
        public string OrderNumber { get; set; }
        public decimal Advanced { get; set; }
        public decimal Recovered { get; set; }
        public decimal Open { get; set; }
    }

    public class SupplierEvaluationDto
    {
        public int Id { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public EvaluationKind Kind { get; set; }
        public DateTime EvaluationDate { get; set; }
        public int? PurchaseOrderId { get; set; }
        public decimal Score { get; set; }
        public SupplierGrade Grade { get; set; }
        public string Notes { get; set; }
        public string EvaluatorName { get; set; }
        public List<EvaluationLineDto> Lines { get; set; } = new List<EvaluationLineDto>();
    }

    public class ProcurementSettingDto
    {
        public string Key { get; set; }
        public decimal Value { get; set; }
        public string Title { get; set; }
        public string Source { get; set; }
    }
}
