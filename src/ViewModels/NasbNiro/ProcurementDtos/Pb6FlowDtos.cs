using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ContractsSystem;
using Entities.Model.NasbNiro.Finance;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProcurementDtos
{
    // PB6: the DTOs of the petty-cash flow, the progress statements and the contracts system. Dates are ISO in JSON; money is rial (D7).

    /// <summary>The one action body of the three modules: the action name, a note (reason) and an optional reference.</summary>
    public class FlowActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public string Reference { get; set; }
        public decimal? Amount { get; set; }
        public DateTime? Date { get; set; }
        public int? BankAccountId { get; set; }
        public int? UserId { get; set; }
        /// <summary>The row the action is about (signing the miscellaneous expense form of a petty-cash row).</summary>
        public int? LineId { get; set; }
    }

    public class FlowQuery : PaginationParams
    {
        public int? Status { get; set; }
        public int? ContractId { get; set; }
        public int? PurchaseOrderId { get; set; }
        public int? ProviderId { get; set; }
        public int? PettyCashId { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string Contains { get; set; }
    }

    // ===================================================================================== petty cash flow

    public class SavePettyRequestDto
    {
        public int? Id { get; set; }
        public int PettyCashId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; }
        public bool Submit { get; set; }
    }

    public class PettyRequestDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int PettyCashId { get; set; }
        public string HolderName { get; set; }
        public FnPettyRequestStatus Status { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; }
        public DateTime RequestDate { get; set; }
        public decimal Balance { get; set; }
        public decimal Ceiling { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? ControlledAt { get; set; }
        public string ControlledByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ToppedUpAt { get; set; }
        public string PaymentReference { get; set; }
        public string RejectReason { get; set; }
        public string CreatorName { get; set; }
    }

    public class SavePettySummaryDto
    {
        public int? Id { get; set; }
        public int PettyCashId { get; set; }
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }
        public string Notes { get; set; }

        /// <summary>true = take the lines from the settlement lines of the period (the lines sent are ignored).</summary>
        public bool AutoFill { get; set; }
        public List<SavePettySummaryLineDto> Lines { get; set; } = new List<SavePettySummaryLineDto>();
    }

    public class SavePettySummaryLineDto
    {
        public int? Id { get; set; }
        public DateTime? Date { get; set; }
        public string InvoiceNumber { get; set; }
        public string PrApprovalNumber { get; set; }
        public string WarehouseReceiptNumber { get; set; }
        public string ProjectCode { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public FnPettyChannel Channel { get; set; } = FnPettyChannel.PettyCash;
        public FnPettyDocType DocType { get; set; } = FnPettyDocType.Invoice;
        public FnPettyExpenseKind ExpenseKind { get; set; } = FnPettyExpenseKind.Goods;
        public FnPettyGoodsClass GoodsClass { get; set; }
        public FnPettyPayKind PayKind { get; set; } = FnPettyPayKind.Cash;
        public string RecipientName { get; set; }
        public string TrackingNumber { get; set; }
        public int? SettlementLineId { get; set; }
        public int? PurchaseRequestId { get; set; }
        public int? ReceiptDocumentId { get; set; }
        public PettyVehicleFormDto Vehicle { get; set; }
        public PettyMiscFormDto Misc { get; set; }
    }

    public class PettyVehicleFormDto
    {
        public DateTime Date { get; set; }
        public string Unit { get; set; }
        public string VehicleType { get; set; }
        public string Origin { get; set; }
        public string Destination { get; set; }
        public string Description { get; set; }
    }

    public class PettyMiscFormDto
    {
        public string RequestingUnit { get; set; }
        public string ProjectCode { get; set; }
        public string WhyNoInvoice { get; set; }
        public string Description { get; set; }
        public bool Signed { get; set; }
    }

    public class PettySummaryDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int PettyCashId { get; set; }
        public string HolderName { get; set; }
        public DateTime Date { get; set; }
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }
        public FnSummaryStatus Status { get; set; }
        public int AttachmentsCount { get; set; }
        public decimal PreviousBalance { get; set; }
        public decimal Charge { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal Balance { get; set; }
        public string TotalInWords { get; set; }
        public string Notes { get; set; }
        public string ReturnReason { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? ControlledAt { get; set; }
        public string ControlledByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? FinalAt { get; set; }
        public string FinalByName { get; set; }
        public int? SettlementId { get; set; }
        public string CreatorName { get; set; }
        public List<PettySummaryLineDto> Lines { get; set; } = new List<PettySummaryLineDto>();

        /// <summary>The evidence problems of the lines right now (empty = ready to submit).</summary>
        public List<string> Problems { get; set; } = new List<string>();
    }

    public class PettySummaryLineDto : SavePettySummaryLineDto
    {
        public int RowNumber { get; set; }
        public int AttachmentCount { get; set; }
        public List<string> Missing { get; set; } = new List<string>();
    }

    // ===================================================================================== progress statements

    public class SaveStatementDto
    {
        public int? Id { get; set; }
        public PsSide Side { get; set; } = PsSide.Supplier;
        public int? PurchaseOrderId { get; set; }
        public int? ContractId { get; set; }
        public int? ProvisionalStatementId { get; set; }
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }
        public string Subject { get; set; }
        public string SupportingDocuments { get; set; }
        public List<SaveStatementLineDto> Lines { get; set; } = new List<SaveStatementLineDto>();
        public List<SaveStatementDeductionDto> Deductions { get; set; } = new List<SaveStatementDeductionDto>();

        /// <summary>Payments made outside the system (an old advance paid by cheque): the system adds the ones it knows by itself.</summary>
        public List<SavePreviousPaymentDto> ManualPrevious { get; set; } = new List<SavePreviousPaymentDto>();
    }

    public class SaveStatementLineDto
    {
        public string Title { get; set; }
        public string SerialOrDrawing { get; set; }
        public string ProjectCode { get; set; }
        public string EquipmentCode { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal AdvanceAmount { get; set; }
        public decimal AfterDeliveryQcAmount { get; set; }
        public decimal GoodPerformanceAmount { get; set; }
        public decimal AfterFinalTestsAmount { get; set; }
        public bool IsAddendum { get; set; }
        public int? OrderLineId { get; set; }
        public int? CbsItemId { get; set; }
    }

    public class SaveStatementDeductionDto
    {
        public PsDeductionKind Kind { get; set; }
        public string Title { get; set; }
        public string ItemTitle { get; set; }
        public string SerialNumber { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public string Basis { get; set; }
        public string DeviationType { get; set; }
        public string LetterNumber { get; set; }
        public int? NcrId { get; set; }

        /// <summary>Legal: the verified rate; the percent comes from the rate table and the base is the gross of the period unless given.</summary>
        public int? LegalRateId { get; set; }
        public decimal? BaseAmount { get; set; }
    }

    public class SavePreviousPaymentDto
    {
        public decimal Amount { get; set; }
        public string PaymentDocNumber { get; set; }
        public string MethodTitle { get; set; }
        public string PaymentTitle { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string StatementSerial { get; set; }
    }

    public class StatementDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public PsSide Side { get; set; }
        public PsStatus Status { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string PurchaseOrderNumber { get; set; }
        public int? ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public int Serial { get; set; }
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }
        public DateTime? SendingDate { get; set; }
        public string Subject { get; set; }
        public string SupportingDocuments { get; set; }
        public PsCounterpartyStatus CounterpartyStatus { get; set; }
        public string CounterpartyComments { get; set; }
        public decimal ContractAmount { get; set; }
        public decimal AddendaAmount { get; set; }
        public decimal PreviousPayments { get; set; }
        public decimal NonconformityDeductions { get; set; }
        public decimal LegalDeductions { get; set; }
        public decimal GrossThisPeriod { get; set; }
        public decimal PayableThisPeriod { get; set; }
        public DateTimeOffset? PreparedAt { get; set; }
        public string PreparedByName { get; set; }
        public decimal? TechnicalApprovedAmount { get; set; }
        public DateTimeOffset? TechnicalApprovedAt { get; set; }
        public string TechnicalApprovedByName { get; set; }
        public decimal? SupervisionApprovedAmount { get; set; }
        public DateTimeOffset? SupervisionApprovedAt { get; set; }
        public string SupervisionApprovedByName { get; set; }
        public DateTimeOffset? PaymentAuthorisedAt { get; set; }
        public string PaymentAuthorisedByName { get; set; }
        public string ProjectManagerName { get; set; }
        public int? PaymentRequestId { get; set; }
        public string PaymentRequestNumber { get; set; }
        public DateTime? PaidDate { get; set; }
        public PsPayMethod? PayMethod { get; set; }
        public string ChequeNumber { get; set; }
        public decimal? ExpectedAmount { get; set; }
        public decimal? ReceivedAmount { get; set; }
        public decimal? PaymentVariance { get; set; }
        public string ReturnReason { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public List<ProcStatementLineDto> Lines { get; set; } = new List<ProcStatementLineDto>();
        public List<StatementPreviousDto> Previous { get; set; } = new List<StatementPreviousDto>();
        public List<StatementDeductionDto> Deductions { get; set; } = new List<StatementDeductionDto>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class ProcStatementLineDto : SaveStatementLineDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public decimal LineTotal { get; set; }
        public decimal ThisPeriodAmount { get; set; }
        public decimal? ProgressPercent { get; set; }
        public string CbsCode { get; set; }
    }

    public class StatementPreviousDto : SavePreviousPaymentDto
    {
        public int Id { get; set; }
        public bool IsAuto { get; set; }
    }

    public class StatementDeductionDto : SaveStatementDeductionDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public string NcrNumber { get; set; }
        public decimal? Percent { get; set; }
        public decimal Amount { get; set; }
        public string RateTitle { get; set; }
    }

    public class StatementPaymentDto
    {
        public int Id { get; set; }
        public DateTime PaidDate { get; set; }
        public PsPayMethod PayMethod { get; set; }
        public string ChequeNumber { get; set; }
        public decimal ReceivedAmount { get; set; }
        public string Note { get; set; }
    }

    public class StatementCounterpartyDto
    {
        public int Id { get; set; }
        public DateTime? SendingDate { get; set; }
        public PsCounterpartyStatus Status { get; set; }
        public string Comments { get; set; }
    }

    public class LegalRateDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public decimal Percent { get; set; }
        public Entities.Model.NasbNiro.Finance.FnDeductionKind DeductionKind { get; set; }
        public string BasisNote { get; set; }
        public string SourceReference { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        public PsRateStatus Status { get; set; }
        public DateTimeOffset? VerifiedAt { get; set; }
        public string VerifiedByName { get; set; }
        public string CreatorName { get; set; }
    }

    public class SaveLegalRateDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public decimal Percent { get; set; }
        public Entities.Model.NasbNiro.Finance.FnDeductionKind DeductionKind { get; set; } = Entities.Model.NasbNiro.Finance.FnDeductionKind.Withholding;
        public string BasisNote { get; set; }
        public string SourceReference { get; set; }
        public DateTime? EffectiveFrom { get; set; }
    }

    public class CashFlowDto
    {
        public int? PurchaseOrderId { get; set; }
        public int? ContractId { get; set; }
        public string Title { get; set; }
        public List<CashFlowMonthDto> Months { get; set; } = new List<CashFlowMonthDto>();
        public decimal PlannedTotal { get; set; }
        public decimal ActualTotal { get; set; }
    }

    public class CashFlowMonthDto
    {
        /// <summary>yyyy-MM</summary>
        public string Month { get; set; }
        public decimal PlannedPayment { get; set; }
        public decimal ActualPayment { get; set; }
        public decimal PlannedReceipt { get; set; }
        public decimal ActualReceipt { get; set; }
        public decimal CumulativePlanned { get; set; }
        public decimal CumulativeActual { get; set; }
    }

    public class SaveCashPlanDto
    {
        public int? PurchaseOrderId { get; set; }
        public int? ContractId { get; set; }
        public List<SaveCashPlanItemDto> Items { get; set; } = new List<SaveCashPlanItemDto>();
    }

    public class SaveCashPlanItemDto
    {
        public PsCashDirection Direction { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
    }

    public class StatementAnalyticsDto
    {
        public int Count { get; set; }
        public decimal TotalPayable { get; set; }
        public decimal TotalGross { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal AverageApprovalDays { get; set; }
        public List<AnalyticsRowDto> ByContract { get; set; } = new List<AnalyticsRowDto>();
        public List<AnalyticsRowDto> ByPeriod { get; set; } = new List<AnalyticsRowDto>();
        public List<AnalyticsRowDto> ByStatus { get; set; } = new List<AnalyticsRowDto>();
        public List<AnalyticsRowDto> ByCbs { get; set; } = new List<AnalyticsRowDto>();
    }

    public class AnalyticsRowDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public int Count { get; set; }
        public decimal Amount { get; set; }
    }

    public class StatementPrintDto
    {
        public StatementDto Statement { get; set; }
        public string BuyerName { get; set; }
        public string PayableInWords { get; set; }
        public string TemplateCode { get; set; }
    }

    public class SaveCbsDto
    {
        public int? PurchaseOrderId { get; set; }
        public int? ContractId { get; set; }
        public List<SaveCbsItemDto> Items { get; set; } = new List<SaveCbsItemDto>();
    }

    public class SaveCbsItemDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public int? WbsNodeId { get; set; }
        public string ParentCode { get; set; }
    }

    public class CbsItemDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public int? WbsNodeId { get; set; }
        public string WbsCode { get; set; }
        public string WbsTitle { get; set; }
        public decimal? ProgressPercent { get; set; }
        public decimal EarnedAmount { get; set; }
        public decimal ClaimedAmount { get; set; }
    }

    // ===================================================================================== contracts system

    public class SaveDealRequestDto
    {
        public int? Id { get; set; }
        public CtDealFormKind FormKind { get; set; } = CtDealFormKind.Employer;
        public string Title { get; set; }
        public string Subject { get; set; }
        public string FundingSource { get; set; }
        public string OrganisationalUnit { get; set; }
        public CtDealLevel DealLevel { get; set; } = CtDealLevel.Medium;
        public string ContractTypeTitle { get; set; }
        public string SelectionMethodTitle { get; set; }
        public decimal EstimatedAmount { get; set; }
        public int? ContractId { get; set; }
        public string PartyName { get; set; }
        public string EmployerRank { get; set; }
        public string EmployerField { get; set; }

        /// <summary>The person who decides a step of the chain (key = the role of the step, value = the user); he must hold the role.</summary>
        public Dictionary<string, int> Designated { get; set; } = new Dictionary<string, int>();
        public bool Submit { get; set; }
    }

    public class DealRequestDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public CtDealFormKind FormKind { get; set; }
        public CtDealStatus Status { get; set; }
        public string Title { get; set; }
        public string Subject { get; set; }
        public string FundingSource { get; set; }
        public string OrganisationalUnit { get; set; }
        public CtDealLevel DealLevel { get; set; }
        public string ContractTypeTitle { get; set; }
        public string SelectionMethodTitle { get; set; }
        public decimal EstimatedAmount { get; set; }
        public string Currency { get; set; } = "ریال";
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public string PartyName { get; set; }
        public string EmployerRank { get; set; }
        public string EmployerField { get; set; }
        public int Round { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string ReturnReason { get; set; }
        public string CreatorName { get; set; }
        public int CreatorUserId { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public List<DealStepDto> Steps { get; set; } = new List<DealStepDto>();
        public List<HistoryDto> History { get; set; } = new List<HistoryDto>();
        public int? TenderRunId { get; set; }
    }

    public class DealStepDto
    {
        public int Id { get; set; }
        public int Round { get; set; }
        public int Order { get; set; }
        public string Role { get; set; }
        public int? DesignatedUserId { get; set; }
        public string DesignatedName { get; set; }
        public CtStepState State { get; set; }
        public string DecidedByName { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string Note { get; set; }
    }

    public class HistoryDto
    {
        public string Action { get; set; }
        public string Note { get; set; }
        public string UserName { get; set; }
        public DateTimeOffset At { get; set; }
        public int Round { get; set; }
    }

    public class SaveTenderRunDto
    {
        public int? Id { get; set; }
        public int DealRequestId { get; set; }
        public CtTenderMethod Method { get; set; }
        public CtGuaranteeType GuaranteeType { get; set; }
        public decimal? GuaranteeAmount { get; set; }
        public DateTime? CallDate { get; set; }
        public DateTime? OpeningDate { get; set; }
        public string Notes { get; set; }
        public List<SaveParticipantDto> Participants { get; set; } = new List<SaveParticipantDto>();
    }

    public class SaveParticipantDto
    {
        public int? Id { get; set; }
        public int? ProviderId { get; set; }
        public string CompanyName { get; set; }
        public decimal OfferAmount { get; set; }
        public decimal? TechnicalScore { get; set; }
        public string Notes { get; set; }
        public bool Disqualified { get; set; }
        public string DisqualifyReason { get; set; }
    }

    public class TenderRunDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int DealRequestId { get; set; }
        public string DealRequestNumber { get; set; }
        public string DealTitle { get; set; }
        public CtTenderStatus Status { get; set; }
        public CtTenderMethod Method { get; set; }
        public CtGuaranteeType GuaranteeType { get; set; }
        public decimal? GuaranteeAmount { get; set; }
        public DateTime? CallDate { get; set; }
        public DateTime? OpeningDate { get; set; }
        public string Notes { get; set; }
        public int? WinnerParticipantId { get; set; }
        public string WinnerReason { get; set; }
        public DateTimeOffset? WinnerSelectedAt { get; set; }
        public string WinnerSelectedByName { get; set; }
        public DateTimeOffset? WinnerConfirmedAt { get; set; }
        public string WinnerConfirmedByName { get; set; }
        public string CancelReason { get; set; }
        public List<ParticipantDto> Participants { get; set; } = new List<ParticipantDto>();
        public List<HistoryDto> History { get; set; } = new List<HistoryDto>();
    }

    public class ParticipantDto : SaveParticipantDto
    {
        public string ProviderName { get; set; }
        public bool Lowest { get; set; }
        public int AttachmentCount { get; set; }
    }

    public class SelectWinnerDto
    {
        public int TenderRunId { get; set; }
        public int ParticipantId { get; set; }
        public string Reason { get; set; }
    }

    public class SaveContractDraftDto
    {
        public int? Id { get; set; }
        public int? DealRequestId { get; set; }
        public int? TenderRunId { get; set; }
        public int? ProviderId { get; set; }
        public string CounterpartyName { get; set; }
        public string ContractKindTitle { get; set; }
        public string TemplateCode { get; set; }
        public string Title { get; set; }
        public string Subject { get; set; }
        public decimal ContractAmount { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? DurationDays { get; set; }
        public decimal? AdvancePercent { get; set; }
        public decimal? RetentionPercent { get; set; }
        public decimal? PerformanceGuaranteePercent { get; set; }
        public string PaymentTerms { get; set; }
        public string SpecialConditions { get; set; }
    }

    public class ContractDraftDto : SaveContractDraftDto
    {
        public string Number { get; set; }
        public CtDraftStatus Status { get; set; }
        public string ProviderName { get; set; }
        public string ContractNumber { get; set; }
        public DateTime? SignedDate { get; set; }
        public int ExchangeRound { get; set; }
        public string CounterpartyComments { get; set; }
        public int? PurchaseOrderId { get; set; }
        public int? ContractId { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public List<HistoryDto> History { get; set; } = new List<HistoryDto>();
    }

    public class ContractDraftPrintDto
    {
        public ContractDraftDto Draft { get; set; }
        public string BuyerName { get; set; }
        public string AmountInWords { get; set; }
        public string TemplateTitle { get; set; }
        public List<ContractPrintClauseDto> Clauses { get; set; } = new List<ContractPrintClauseDto>();
    }

    /// <summary>The contract-changes cartable: the addenda of the supplier contracts and the statuses of the project contracts in one list.</summary>
    public class ContractChangeDto
    {
        public string Kind { get; set; }
        public int Id { get; set; }
        public string Number { get; set; }
        public string ContractTitle { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public decimal AmountBefore { get; set; }
        public decimal AmountAfter { get; set; }
        public DateTime? DeliveryDateBefore { get; set; }
        public DateTime? DeliveryDateAfter { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public string Route { get; set; }
    }

    public class ContractMappingRowDto
    {
        public string Need { get; set; }
        public string ExistingModule { get; set; }
        public string Extension { get; set; }
    }
}
