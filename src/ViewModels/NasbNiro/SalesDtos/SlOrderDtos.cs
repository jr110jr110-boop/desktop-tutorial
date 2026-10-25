using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Sales;

namespace ViewModels.NasbNiro.SalesDtos
{
    // ------------------------------------------------------------------------------------------ price lists

    public class SlPriceListQuery : SlPageQuery
    {
        public SlPriceListStatus? Status { get; set; }
    }

    public class SlPriceListLineDto
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal MinQuantity { get; set; }
    }

    public class SlPriceListDto
    {
        /// <summary>True when the current user made or edited this record, so maker-checker refuses their approval; the page hides the approve button.</summary>
        public bool TouchedByMe { get; set; }

        public int Id { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public DateTime ValidFrom { get; set; }
        public string ValidFromText { get; set; }
        public DateTime? ValidTo { get; set; }
        public string ValidToText { get; set; }
        public SlPriceListStatus Status { get; set; }
        public string Note { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApprovedAtText { get; set; }
        public List<SlPriceListLineDto> Lines { get; set; } = new List<SlPriceListLineDto>();
        public string RowVersion { get; set; }
    }

    public class SlSavePriceListLineDto
    {
        public int ItemId { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal MinQuantity { get; set; }
    }

    public class SlSavePriceListDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string Note { get; set; }
        public List<SlSavePriceListLineDto> Lines { get; set; } = new List<SlSavePriceListLineDto>();
        public string RowVersion { get; set; }
    }

    /// <summary>Action "approve" (somebody who did not edit it), or "retire".</summary>
    public class SlPriceListActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>Cost-plus suggestion: the cost of each item from the stock cost, plus the markup the user enters (no markup is assumed).</summary>
    public class SlPriceSuggestDto
    {
        public List<int> ItemIds { get; set; } = new List<int>();
        public decimal? MarkupPercent { get; set; }
    }

    public class SlPriceSuggestionDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public decimal? UnitCost { get; set; }
        public SlCostSource CostSource { get; set; }
        public decimal? SuggestedPrice { get; set; }
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------ quotations

    public class SlQuotationQuery : SlPageQuery
    {
        public SlQuotationStatus? Status { get; set; }
        public int? CustomerId { get; set; }
    }

    public class SlDocLineDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public string Unit { get; set; }
        public string Description { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal Amount { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal DeliveredQuantity { get; set; }
        public decimal InvoicedQuantity { get; set; }
    }

    public class SlSaveDocLineDto
    {
        public int ItemId { get; set; }
        public string Description { get; set; }
        public decimal Quantity { get; set; }

        /// <summary>Empty: the price of the approved price list, if any.</summary>
        public decimal? UnitPrice { get; set; }

        public decimal DiscountPercent { get; set; }
    }

    public class SlQuotationDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime QuotationDate { get; set; }
        public string QuotationDateText { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int? ContactId { get; set; }
        public DateTime ValidUntil { get; set; }
        public string ValidUntilText { get; set; }
        public SlQuotationStatus Status { get; set; }
        public int? PriceListId { get; set; }
        public string Note { get; set; }
        public decimal NetAmount { get; set; }
        public int? SalesOrderId { get; set; }
        public List<SlDocLineDto> Lines { get; set; } = new List<SlDocLineDto>();
        public string RowVersion { get; set; }
    }

    public class SlSaveQuotationDto
    {
        public int? Id { get; set; }
        public DateTime? QuotationDate { get; set; }
        public int CustomerId { get; set; }
        public int? ContactId { get; set; }
        public DateTime? ValidUntil { get; set; }
        public int? PriceListId { get; set; }
        public string Note { get; set; }
        public List<SlSaveDocLineDto> Lines { get; set; } = new List<SlSaveDocLineDto>();
        public string RowVersion { get; set; }
    }

    /// <summary>Action "send", "accept", "reject" or "cancel".</summary>
    public class SlQuotationActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string RowVersion { get; set; }
    }

    public class SlQuotationToOrderDto
    {
        public int QuotationId { get; set; }
        public string CustomerOrderNumber { get; set; }
        public DateTime? RequestedDeliveryDate { get; set; }
    }

    // ------------------------------------------------------------------------------------------ sales orders

    public class SlOrderQuery : SlPageQuery
    {
        public SlOrderStatus? Status { get; set; }
        public int? CustomerId { get; set; }
        public bool? CreditPending { get; set; }
    }

    public class SlOrderDto
    {
        /// <summary>True when the current user made or edited this record, so maker-checker refuses their approval; the page hides the approve button.</summary>
        public bool TouchedByMe { get; set; }

        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime OrderDate { get; set; }
        public string OrderDateText { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int? ContactId { get; set; }
        public int? QuotationId { get; set; }
        public string CustomerOrderNumber { get; set; }
        public DateTime? RequestedDeliveryDate { get; set; }
        public string RequestedDeliveryDateText { get; set; }
        public SlOrderStatus Status { get; set; }
        public string StatusText { get; set; }
        public decimal NetAmount { get; set; }
        public bool CreditApprovalRequired { get; set; }
        public string CreditReason { get; set; }
        public decimal? CreditExcess { get; set; }
        public int? CreditApprovedByUserId { get; set; }
        public DateTimeOffset? CreditApprovedAt { get; set; }
        public string CreditApprovedAtText { get; set; }
        public decimal? CreditApprovedNetAmount { get; set; }
        public string CreditApprovalNote { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public string ConfirmedAtText { get; set; }
        public string CancelReason { get; set; }
        public List<SlDocLineDto> Lines { get; set; } = new List<SlDocLineDto>();
        public List<SlOrderDeliveryDto> Deliveries { get; set; } = new List<SlOrderDeliveryDto>();
        public string RowVersion { get; set; }
    }

    public class SlOrderDeliveryDto
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; }
        public int? InvoiceId { get; set; }
        public string InvoiceNumber { get; set; }
        public string DraftNote { get; set; }
        public bool Reversed { get; set; }
    }

    public class SlSaveOrderDto
    {
        public int? Id { get; set; }
        public DateTime? OrderDate { get; set; }
        public int CustomerId { get; set; }
        public int? ContactId { get; set; }
        public string CustomerOrderNumber { get; set; }
        public DateTime? RequestedDeliveryDate { get; set; }
        public List<SlSaveDocLineDto> Lines { get; set; } = new List<SlSaveDocLineDto>();
        public string RowVersion { get; set; }
    }

    /// <summary>Action "confirm", "cancel" (with a reason) or "close".</summary>
    public class SlOrderActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }

    public class SlCreditApproveDto
    {
        public int Id { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class SlDraftInvoiceDto
    {
        public int OrderId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ dashboards

    public class SlStageValueDto
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public decimal Value { get; set; }
    }

    public class SlDashboardDto
    {
        public DateTime AsOf { get; set; }
        public string AsOfText { get; set; }
        public int OpenInquiries { get; set; }
        public decimal PipelineValue { get; set; }
        public List<SlStageValueDto> PipelineByStage { get; set; } = new List<SlStageValueDto>();
        public List<SlStageValueDto> PipelineByProduct { get; set; } = new List<SlStageValueDto>();
        public int Won { get; set; }
        public int Lost { get; set; }
        public decimal? HitRatePercent { get; set; }
        public List<SlProposalDueDto> ProposalsDueThisWeek { get; set; } = new List<SlProposalDueDto>();
        public int ProposalsDueThisWeekCount { get; set; }
        public int QuotationsExpiringCount { get; set; }
        public List<SlQuotationExpiringDto> QuotationsExpiring { get; set; } = new List<SlQuotationExpiringDto>();
        public int BidDecisionsWaiting { get; set; }
        public int ProposalsWaitingApproval { get; set; }
        public int OrdersWaitingCredit { get; set; }
        public int OpenOrders { get; set; }
    }

    public class SlProposalDueDto
    {
        public int InquiryId { get; set; }
        public string InquiryNumber { get; set; }
        public string Title { get; set; }
        public string CustomerName { get; set; }
        public DateTime DueDate { get; set; }
        public string DueDateText { get; set; }
        public SlInquiryStatus Status { get; set; }
    }

    public class SlQuotationExpiringDto
    {
        public int QuotationId { get; set; }
        public string Number { get; set; }
        public string CustomerName { get; set; }
        public DateTime ValidUntil { get; set; }
        public string ValidUntilText { get; set; }
        public decimal NetAmount { get; set; }
    }

    /// <summary>The sales numbers added to the CEO dashboard.</summary>
    public class SlCeoCountsDto
    {
        public int OpenInquiries { get; set; }
        public decimal PipelineValue { get; set; }
        public int BidDecisionsWaiting { get; set; }
        public int ProposalsWaitingApproval { get; set; }
        public int ProposalsDueSoon { get; set; }
        public int BelowCostProposals { get; set; }
        public int OrdersWaitingCredit { get; set; }
        public decimal? HitRatePercent { get; set; }
    }
}
