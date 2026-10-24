using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Providers;

namespace Entities.Model.NasbNiro.Procurement
{
    // Procurement (تدارکات) inside PMIS: PR-IP-004, PR-IP-005, WI-PL-004, WI-PO-003, BL-006, BL-007 and WI-WH-001,
    // corrected where they conflict with ISO 9001 8.4 / API Q1 5.6 and internal control:
    // the requester, the approver, the buyer, the receiver and the payer are different people; every purchase goes
    // purchase request -> inquiry (quotes) -> selection -> purchase order (reviewed before release) -> receipt (quarantine,
    // inspection) -> supplier invoice matched to the order and the accepted receipt -> payment request to finance.
    // All amounts are rial.

    public enum PurchaseRequestSource
    {
        /// <summary>From the BOM / MTO of a work order (planning).</summary>
        Bom = 1,
        /// <summary>Stock below the reorder point (warehouse).</summary>
        ReorderPoint = 2,
        /// <summary>A unit's own need (ad hoc).</summary>
        AdHoc = 3,
        /// <summary>Shortage of an approved material request.</summary>
        MaterialShortage = 4,
    }

    public enum PurchaseKind
    {
        Goods = 1,
        /// <summary>Invoice-based services (cutting, welding, calibration...).</summary>
        Service = 2,
        /// <summary>Contract-based part making by a contractor (PR-IP-005 6-3).</summary>
        Subcontract = 3,
    }

    public enum PurchaseRequestStatus
    {
        Draft = 0,
        Submitted = 1,
        Approved = 2,
        /// <summary>Everything is on purchase orders.</summary>
        Ordered = 3,
        Closed = 4,
        Rejected = 9,
    }

    public class PurchaseRequest : BaseEntity
    {
        public string Number { get; set; }
        public PurchaseRequestSource Source { get; set; }
        public PurchaseKind Kind { get; set; }
        public PurchaseRequestStatus Status { get; set; }
        public DateTime RequestDate { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public int? ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        public string CostCenter { get; set; }

        /// <summary>The material request whose shortage this request buys.</summary>
        public int? MaterialRequestId { get; set; }

        public string Description { get; set; }

        /// <summary>Sum of the estimated amounts (rial): decides the approvals and the number of quotes.</summary>
        public decimal EstimatedAmount { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public int? PlanningApprovedByUserId { get; set; }

        /// <summary>Medium and major purchases (BL-006): the managing director.</summary>
        public DateTimeOffset? ManagementApprovedAt { get; set; }
        public int? ManagementApprovedByUserId { get; set; }

        /// <summary>Major purchases (BL-006 art. 5): the board resolution reference.</summary>
        public string BoardResolution { get; set; }

        public DateTimeOffset? RejectedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public string RejectReason { get; set; }

        public ICollection<PurchaseRequestLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PurchaseRequestLine
    {
        public int Id { get; set; }

        public int PurchaseRequestId { get; set; }
        public PurchaseRequest PurchaseRequest { get; set; }

        public int RowNumber { get; set; }

        /// <summary>Coded item; empty for services described in text.</summary>
        public int? ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public DateTime? NeededBy { get; set; }
        public decimal? EstimatedUnitPrice { get; set; }

        /// <summary>Technical requirements to pass to the supplier: drawing and revision, standard, certificates (EN 10204 3.1)...</summary>
        public string TechnicalRequirements { get; set; }

        /// <summary>Legacy project buy order (BOM process) this line buys for.</summary>
        public int? BuyOrderId { get; set; }

        public int? MaterialRequestLineId { get; set; }
    }

    public enum InquiryStatus
    {
        Open = 0,
        /// <summary>A quote has been chosen; waiting for the committee approvals.</summary>
        Selected = 1,
        Approved = 2,
        Cancelled = 9,
    }

    /// <summary>استعلام قیمت: quotes of several suppliers for (part of) one purchase request.</summary>
    public class PurchaseInquiry : BaseEntity
    {
        public string Number { get; set; }
        public InquiryStatus Status { get; set; }
        public DateTime InquiryDate { get; set; }

        public int PurchaseRequestId { get; set; }
        public PurchaseRequest PurchaseRequest { get; set; }

        public string Description { get; set; }

        /// <summary>Quotes required by the amount (BL-007 s.5-4 / BL-006 art. 4).</summary>
        public int RequiredQuotes { get; set; }

        /// <summary>Why fewer quotes are enough (only few suppliers); needs the management approval.</summary>
        public string FewerQuotesReason { get; set; }

        public int? SelectedQuoteId { get; set; }

        /// <summary>Why the chosen quote is not the cheapest technically accepted one.</summary>
        public string SelectionReason { get; set; }
        public DateTimeOffset? SelectedAt { get; set; }
        public int? SelectedByUserId { get; set; }

        // supplier selection committee (WI-PL-004 step 6): financial and technical approvals, different people
        public DateTimeOffset? FinancialApprovedAt { get; set; }
        public int? FinancialApprovedByUserId { get; set; }
        public DateTimeOffset? CommercialApprovedAt { get; set; }
        public int? CommercialApprovedByUserId { get; set; }
        public DateTimeOffset? TechnicalApprovedAt { get; set; }
        public int? TechnicalApprovedByUserId { get; set; }
        public DateTimeOffset? ManagementApprovedAt { get; set; }
        public int? ManagementApprovedByUserId { get; set; }
        public string CommitteeMinutes { get; set; }

        public ICollection<SupplierQuote> Quotes { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SupplierQuote
    {
        public int Id { get; set; }

        public int InquiryId { get; set; }
        public PurchaseInquiry Inquiry { get; set; }

        public int ProviderId { get; set; }
        public Provider Provider { get; set; }

        public string QuoteNumber { get; set; }
        public DateTime QuoteDate { get; set; }
        public DateTime? ValidUntil { get; set; }
        public int? DeliveryDays { get; set; }
        public string PaymentTerms { get; set; }
        public decimal? VatPercent { get; set; }

        /// <summary>Engineering / QC: the offer meets the technical requirements.</summary>
        public bool TechnicallyAccepted { get; set; }
        public decimal? TechnicalScore { get; set; }
        public string TechnicalNote { get; set; }

        public Guid? DocumentId { get; set; }
        public string Notes { get; set; }

        public decimal TotalAmount { get; set; }

        public ICollection<SupplierQuoteLine> Lines { get; set; }
    }

    public class SupplierQuoteLine
    {
        public int Id { get; set; }

        public int QuoteId { get; set; }
        public SupplierQuote Quote { get; set; }

        public int RequestLineId { get; set; }
        public PurchaseRequestLine RequestLine { get; set; }

        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public enum PurchaseOrderStatus
    {
        Draft = 0,
        /// <summary>Reviewed and approved; not yet sent to the supplier.</summary>
        Approved = 1,
        /// <summary>Sent to the supplier: this is the warehouse entry permit (مجوز ورود).</summary>
        Released = 2,
        PartlyReceived = 3,
        Received = 4,
        Closed = 5,
        Cancelled = 9,
    }

    /// <summary>سفارش خرید / قرارداد خرید.</summary>
    public class PurchaseOrder : BaseEntity
    {
        public string Number { get; set; }
        public PurchaseOrderStatus Status { get; set; }
        public PurchaseKind Kind { get; set; }
        public DateTime OrderDate { get; set; }

        public int ProviderId { get; set; }
        public Provider Provider { get; set; }

        public int? InquiryId { get; set; }
        public PurchaseInquiry Inquiry { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public int? ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        /// <summary>Foreign purchase: currency code and the rate used (rial per unit).</summary>
        public string Currency { get; set; }
        public decimal? ExchangeRate { get; set; }
        public string Incoterms { get; set; }

        public string PaymentTerms { get; set; }
        public decimal? VatPercent { get; set; }

        /// <summary>Advance payment percent (BL-006: at most 25%, against a guarantee).</summary>
        public decimal? AdvancePercent { get; set; }
        public string AdvanceGuarantee { get; set; }
        public DateTime? AdvanceGuaranteeExpiry { get; set; }

        /// <summary>Performance guarantee (BL-006 art. 8: 10%, 5% for construction/haulage).</summary>
        public string PerformanceGuarantee { get; set; }
        public decimal? RetentionPercent { get; set; }

        /// <summary>Purchases above the contract threshold (WI-PL-004 step 7) need a signed contract.</summary>
        public string ContractNumber { get; set; }
        public DateTime? ContractSignedAt { get; set; }

        /// <summary>Requirements communicated to the supplier (ISO 9001 8.4.3, API Q1): specs, certificates, inspection, right of access.</summary>
        public string Requirements { get; set; }

        public DateTime? DeliveryDate { get; set; }
        public string DeliveryPlace { get; set; }
        public decimal? LatePenaltyPercentPerWeek { get; set; }
        public string Description { get; set; }

        public decimal TotalAmount { get; set; }

        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public int? ReleasedByUserId { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }
        public string CancelReason { get; set; }

        public ICollection<PurchaseOrderLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PurchaseOrderLine
    {
        public int Id { get; set; }

        public int PurchaseOrderId { get; set; }
        public PurchaseOrder PurchaseOrder { get; set; }

        public int RowNumber { get; set; }

        public int? RequestLineId { get; set; }
        public PurchaseRequestLine RequestLine { get; set; }

        public int? ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }

        /// <summary>Rial per unit (converted for foreign orders).</summary>
        public decimal UnitPrice { get; set; }

        public DateTime? DeliveryDate { get; set; }

        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }

        /// <summary>Capital purchase: the approved invoice line drafts a fixed asset for finance to complete.</summary>
        public bool IsCapital { get; set; }

        /// <summary>Accepted quantity (goods: QC-released receipts; services: service acceptance).</summary>
        public decimal ReceivedQuantity { get; set; }
        public decimal InvoicedQuantity { get; set; }
        public DateTime? LastReceiptDate { get; set; }
    }

    /// <summary>تایید انجام خدمت: acceptance of a service or subcontract line by QC or the requesting unit (not the buyer).</summary>
    public class ServiceAcceptance : BaseEntity
    {
        public string Number { get; set; }

        public int PurchaseOrderLineId { get; set; }
        public PurchaseOrderLine PurchaseOrderLine { get; set; }

        public DateTime AcceptanceDate { get; set; }
        public decimal Quantity { get; set; }
        public string Notes { get; set; }
        public Guid? DocumentId { get; set; }
    }

    public enum SupplierInvoiceStatus
    {
        Draft = 0,
        /// <summary>Matched to the order and the accepted receipts within tolerance.</summary>
        Matched = 1,
        /// <summary>Price or quantity differs: needs the management approval.</summary>
        NeedsApproval = 2,
        Approved = 3,
        Cancelled = 9,
    }

    public class SupplierInvoice : BaseEntity
    {
        public string Number { get; set; }
        public SupplierInvoiceStatus Status { get; set; }

        public int ProviderId { get; set; }
        public Provider Provider { get; set; }

        public int PurchaseOrderId { get; set; }
        public PurchaseOrder PurchaseOrder { get; set; }

        /// <summary>The supplier's invoice number and date (must be in the company's name).</summary>
        public string InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public bool IsOfficial { get; set; }

        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }
        /// <summary>Part of the VAT that is not deductible (lines flagged non-recoverable): it is cost, not input VAT (F3b).</summary>
        public decimal NonRecoverableVatAmount { get; set; }

        /// <summary>Difference against the order prices (rial).</summary>
        public decimal PriceVariance { get; set; }
        public string MatchNote { get; set; }

        public Guid? DocumentId { get; set; }

        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }
        public string CancelReason { get; set; }

        public ICollection<SupplierInvoiceLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class SupplierInvoiceLine
    {
        public int Id { get; set; }

        public int InvoiceId { get; set; }
        public SupplierInvoice Invoice { get; set; }

        public int OrderLineId { get; set; }
        public PurchaseOrderLine OrderLine { get; set; }

        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        /// <summary>The VAT of this line cannot be deducted (F3b): it becomes part of the cost and stays out of the VAT return.</summary>
        public bool NonRecoverableVat { get; set; }
    }

    public enum PaymentRequestType
    {
        Advance = 1,
        /// <summary>Progress payment statement (صورت وضعیت).</summary>
        Progress = 2,
        /// <summary>Payment of an approved invoice.</summary>
        Invoice = 3,
        Final = 4,
    }

    public enum PaymentRequestStatus
    {
        Draft = 0,
        Approved = 1,
        /// <summary>Finance paid (in Sepidar).</summary>
        Paid = 2,
        Rejected = 9,
    }

    /// <summary>درخواست پرداخت to finance. The payment itself is made and booked in Sepidar.</summary>
    public class PaymentRequest : BaseEntity
    {
        public string Number { get; set; }
        public PaymentRequestType Type { get; set; }
        public PaymentRequestStatus Status { get; set; }
        public DateTime RequestDate { get; set; }

        public int ProviderId { get; set; }
        public Provider Provider { get; set; }

        public int? PurchaseOrderId { get; set; }
        public PurchaseOrder PurchaseOrder { get; set; }

        public int? InvoiceId { get; set; }
        public SupplierInvoice Invoice { get; set; }

        public decimal Amount { get; set; }

        /// <summary>Deducted: late penalty, retention, tax withholding (BL-007 s.5-11).</summary>
        public decimal Deductions { get; set; }
        public string DeductionNote { get; set; }

        public bool QcApproved { get; set; }
        public string Description { get; set; }

        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? PaidAt { get; set; }
        public int? PaidByUserId { get; set; }
        public string PaymentReference { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum SupplierGrade
    {
        NotEvaluated = 0,
        A = 1,
        B = 2,
        C = 3,
        D = 4,
    }

    /// <summary>Approved vendor list entry (FO-PO-008 / FO-PO-009) of one supplier.</summary>
    public class SupplierProfile
    {
        public int Id { get; set; }

        public int ProviderId { get; set; }
        public Provider Provider { get; set; }

        public SupplierGrade Grade { get; set; }
        public decimal? Score { get; set; }
        public DateTime? EvaluatedAt { get; set; }
        public DateTime? NextEvaluation { get; set; }

        /// <summary>Supply categories (item group codes or titles) the supplier is approved for, comma separated.</summary>
        public string Categories { get; set; }

        public bool IsStrategic { get; set; }
        public bool Blocked { get; set; }
        public string BlockReason { get; set; }

        /// <summary>Written warnings after returns (WI-PO-003 note 3: third time = cooperation cut).</summary>
        public int Warnings { get; set; }

        public string SepidarCode { get; set; }
        public string NationalId { get; set; }
        public string EconomicCode { get; set; }

        /// <summary>Registered IBAN (شبا) for bank transfers; a transfer to another IBAN needs the finance manager.</summary>
        public string Iban { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum EvaluationKind
    {
        Initial = 1,
        Secondary = 2,
        /// <summary>Every 6 months for contracts longer than 6 months.</summary>
        Periodic = 3,
        EndOfContract = 4,
    }

    /// <summary>One evaluation (FO-PO-003..007): weighted questions, final score = sum(S*W)/sum(W).</summary>
    public class SupplierEvaluation : BaseEntity
    {
        public int ProviderId { get; set; }
        public Provider Provider { get; set; }

        public EvaluationKind Kind { get; set; }
        public DateTime EvaluationDate { get; set; }
        public int? PurchaseOrderId { get; set; }
        public decimal Score { get; set; }
        public SupplierGrade Grade { get; set; }
        public string Notes { get; set; }

        public ICollection<SupplierEvaluationLine> Lines { get; set; }
    }

    public class SupplierEvaluationLine
    {
        public int Id { get; set; }

        public int EvaluationId { get; set; }
        public SupplierEvaluation Evaluation { get; set; }

        public string Question { get; set; }
        public decimal Weight { get; set; }

        /// <summary>0..100; empty = not applicable.</summary>
        public decimal? Score { get; set; }
    }

    /// <summary>Company thresholds and rules (editable by management); amounts in rial.</summary>
    public class ProcurementSetting
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public decimal Value { get; set; }
        public string Title { get; set; }
        public string Source { get; set; }
    }
}
