using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Procurement;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProcurementDtos
{
    // PB6: archive, addenda, guarantees, payment terms, the contract print, the reports and the bridge from the legacy buy orders.

    // ---------------------------------------------------------------- archive

    public class ArchiveDto
    {
        public ProcArchiveOwner OwnerType { get; set; }
        public int OwnerId { get; set; }
        public string Reason { get; set; }
    }

    public class ArchiveRecordDto
    {
        public ProcArchiveOwner OwnerType { get; set; }
        public int OwnerId { get; set; }
        public string Number { get; set; }
        public string Description { get; set; }
        public string StatusTitle { get; set; }
        public bool IsArchived { get; set; }
        public DateTimeOffset ArchivedAt { get; set; }
        public string ArchivedByName { get; set; }
        public string ArchiveReason { get; set; }
        public DateTimeOffset? RestoredAt { get; set; }
        public string RestoredByName { get; set; }
        public string RestoreReason { get; set; }
        public int Times { get; set; }
        public string Route { get; set; }
    }

    public class ArchiveQuery : PaginationParams
    {
        public ProcArchiveOwner? OwnerType { get; set; }
        public string Contains { get; set; }

        /// <summary>false = also the documents that were restored (their history stays).</summary>
        public bool? OnlyArchived { get; set; }
    }

    // ---------------------------------------------------------------- request revisions

    public class PurchaseRequestRevisionDto
    {
        public int Id { get; set; }
        public int PurchaseRequestId { get; set; }
        public int Revision { get; set; }
        public string Reason { get; set; }
        public PurchaseRequestStatus StatusWhenFrozen { get; set; }
        public decimal EstimatedAmount { get; set; }
        public string SnapshotJson { get; set; }
        public string Hash { get; set; }
        public DateTimeOffset FrozenAt { get; set; }
        public string FrozenByName { get; set; }
        public string PlanningApprovedByName { get; set; }
        public string ManagementApprovedByName { get; set; }
    }

    /// <summary>The chain of one request: the revisions that were returned or rejected (each its own request) and the frozen snapshots of the approved ones.</summary>
    public class RequestChainDto
    {
        public List<RequestChainItemDto> Items { get; set; } = new List<RequestChainItemDto>();
        public List<PurchaseRequestRevisionDto> Snapshots { get; set; } = new List<PurchaseRequestRevisionDto>();
    }

    public class RequestChainItemDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int Revision { get; set; }
        public PurchaseRequestStatus Status { get; set; }
        public string ReturnReason { get; set; }
        public string RejectReason { get; set; }
        public DateTimeOffset CreateDate { get; set; }
    }

    // ---------------------------------------------------------------- addenda

    public class SaveAddendumDto
    {
        public int? Id { get; set; }
        public int PurchaseOrderId { get; set; }
        public string Reason { get; set; }
        public DateTime? DeliveryDateAfter { get; set; }
        public List<SaveAddendumLineDto> Lines { get; set; } = new List<SaveAddendumLineDto>();
    }

    public class SaveAddendumLineDto
    {
        public int OrderLineId { get; set; }
        public decimal QuantityAfter { get; set; }
        public decimal UnitPriceAfter { get; set; }
    }

    public class AddendumDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int PurchaseOrderId { get; set; }
        public string PurchaseOrderNumber { get; set; }
        public int Sequence { get; set; }
        public AddendumKind Kind { get; set; }
        public AddendumStatus Status { get; set; }
        public string Reason { get; set; }
        public decimal AmountBefore { get; set; }
        public decimal AmountAfter { get; set; }
        public DateTime? DeliveryDateBefore { get; set; }
        public DateTime? DeliveryDateAfter { get; set; }
        public decimal MaxVolumeChangePercent { get; set; }
        public decimal VolumeLimitPercent { get; set; }
        public bool NeedsCeo { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public DateTimeOffset? OrderApprovedAt { get; set; }
        public string OrderApprovedByName { get; set; }
        public DateTimeOffset? CeoApprovedAt { get; set; }
        public string CeoApprovedByName { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
        public string RejectReason { get; set; }
        public List<AddendumLineDto> Lines { get; set; } = new List<AddendumLineDto>();
    }

    public class AddendumLineDto
    {
        public int OrderLineId { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal OriginalQuantity { get; set; }
        public decimal QuantityBefore { get; set; }
        public decimal QuantityAfter { get; set; }
        public decimal UnitPriceBefore { get; set; }
        public decimal UnitPriceAfter { get; set; }
        public decimal VolumeChangePercent { get; set; }
    }

    // ---------------------------------------------------------------- guarantees

    public class SaveGuaranteeDto
    {
        public int? Id { get; set; }
        public int ProviderId { get; set; }
        public int? PurchaseOrderId { get; set; }
        public GuaranteeKind Kind { get; set; }
        public GuaranteePurpose Purpose { get; set; }
        public decimal Amount { get; set; }
        public string GuaranteeNumber { get; set; }
        public string Issuer { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string ReleaseConditions { get; set; }
    }

    public class GuaranteeDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string PurchaseOrderNumber { get; set; }
        public GuaranteeKind Kind { get; set; }
        public GuaranteePurpose Purpose { get; set; }
        public decimal Amount { get; set; }
        public string GuaranteeNumber { get; set; }
        public string Issuer { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public SupplierGuaranteeStatus Status { get; set; }
        public string ReleaseConditions { get; set; }
        public string ReleaseReason { get; set; }
        public DateTimeOffset? ReleaseRequestedAt { get; set; }
        public string ReleaseRequestedByName { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public string ReleasedByName { get; set; }
        public int DaysToExpiry { get; set; }

        /// <summary>none / soon (inside the alert window) / expired.</summary>
        public string ExpiryState { get; set; }
        public int AttachmentCount { get; set; }
    }

    public class GuaranteeAlertResultDto
    {
        public int Alerted { get; set; }
        public int MarkedExpired { get; set; }
    }

    // ---------------------------------------------------------------- payment terms

    public class SavePaymentTermsDto
    {
        public int PurchaseOrderId { get; set; }

        /// <summary>true = replace the terms with the template of the company (30 / 30 / 30 / 10).</summary>
        public bool UseTemplate { get; set; }
        public List<SavePaymentTermDto> Terms { get; set; } = new List<SavePaymentTermDto>();
    }

    public class SavePaymentTermDto
    {
        public PaymentTermKind Kind { get; set; }
        public decimal Percent { get; set; }
        public int? DueDays { get; set; }
        public string Condition { get; set; }
    }

    public class PaymentTermDto
    {
        public int Id { get; set; }
        public int PurchaseOrderId { get; set; }
        public int RowNumber { get; set; }
        public PaymentTermKind Kind { get; set; }
        public decimal Percent { get; set; }
        public decimal Amount { get; set; }
        public int? DueDays { get; set; }
        public string Condition { get; set; }
        public PaymentTermStatus Status { get; set; }
        public int? PaymentRequestId { get; set; }
        public string PaymentRequestNumber { get; set; }

        /// <summary>Whether the condition of this milestone is met now (a payment request may be made), and why not.</summary>
        public bool Eligible { get; set; }
        public string EligibleNote { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTimeOffset? ConditionConfirmedAt { get; set; }
        public string ConditionConfirmedByName { get; set; }
    }

    public class RequestTermPaymentDto
    {
        public int TermId { get; set; }

        /// <summary>The approved invoice for the cheque after the invoice.</summary>
        public int? InvoiceId { get; set; }
        public string Note { get; set; }
    }

    // ---------------------------------------------------------------- the printed contract (FO-IP-040-04)

    public class ContractPrintDto
    {
        public string TemplateCode { get; set; }
        public string TemplateTitle { get; set; }
        public string OrderNumber { get; set; }
        public string ContractNumber { get; set; }
        public DateTime? ContractDate { get; set; }
        public string BuyerName { get; set; }
        public string SupplierName { get; set; }
        public string SupplierNationalId { get; set; }
        public string SupplierEconomicCode { get; set; }
        public string Subject { get; set; }
        public decimal TotalAmount { get; set; }
        public string TotalInWords { get; set; }
        public decimal VatPercent { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string DeliveryPlace { get; set; }
        public string ProjectCode { get; set; }
        public decimal VolumeLimitPercent { get; set; }
        public decimal? LatePenaltyPercentPerWeek { get; set; }
        public decimal? RetentionPercent { get; set; }
        public string Requirements { get; set; }
        public List<ContractPrintLineDto> Lines { get; set; } = new List<ContractPrintLineDto>();
        public List<ContractPrintTermDto> PaymentTerms { get; set; } = new List<ContractPrintTermDto>();
        public List<ContractPrintClauseDto> Clauses { get; set; } = new List<ContractPrintClauseDto>();
        public List<string> Guarantees { get; set; } = new List<string>();
        public List<string> Addenda { get; set; } = new List<string>();
    }

    public class ContractPrintLineDto
    {
        public int Row { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Amount { get; set; }
    }

    public class ContractPrintTermDto
    {
        public int Row { get; set; }
        public string Title { get; set; }
        public decimal Percent { get; set; }
        public decimal Amount { get; set; }
        public string Condition { get; set; }
    }

    public class ContractPrintClauseDto
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
    }

    // ---------------------------------------------------------------- reports

    public class ReportColumnDto
    {
        public string Key { get; set; }
        public string Title { get; set; }

        /// <summary>text / number / money / date</summary>
        public string Type { get; set; }
    }

    public class ReportTableDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public List<ReportColumnDto> Columns { get; set; } = new List<ReportColumnDto>();
        public List<Dictionary<string, object>> Rows { get; set; } = new List<Dictionary<string, object>>();
        public Dictionary<string, decimal> Totals { get; set; } = new Dictionary<string, decimal>();
    }

    public class ProcurementReportQuery
    {
        public string Report { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int? ProviderId { get; set; }
        public int? ContractId { get; set; }

        /// <summary>Overdue cycle: days from the approval of a request to its first purchase order (default from the settings).</summary>
        public int? OverdueDays { get; set; }
    }

    // ---------------------------------------------------------------- legacy buy orders

    public class LegacyBuyOrderDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int Status { get; set; }
        public string StatusTitle { get; set; }
        public bool IsOpen { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int Count { get; set; }
        public string ExitCommodityCode { get; set; }
        public string EnterCommodityCode { get; set; }
        public decimal FinancePrice { get; set; }
        public DateTimeOffset RegisterDate { get; set; }
        public int? PurchaseRequestId { get; set; }
        public string PurchaseRequestNumber { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string PurchaseOrderNumber { get; set; }
    }

    public class ConvertBuyOrderDto
    {
        public int BuyOrderId { get; set; }

        /// <summary>The item the converted line buys (the legacy buy order has only text codes).</summary>
        public int? ItemId { get; set; }
        public decimal? UnitPrice { get; set; }
        public string Note { get; set; }
    }

    public class ConvertBuyOrderResultDto
    {
        public int BuyOrderId { get; set; }
        public int PurchaseRequestId { get; set; }
        public string PurchaseRequestNumber { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string PurchaseOrderNumber { get; set; }
        public string Message { get; set; }
    }
}
