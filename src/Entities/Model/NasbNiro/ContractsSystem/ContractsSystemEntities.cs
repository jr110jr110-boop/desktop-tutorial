using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Providers;

namespace Entities.Model.NasbNiro.ContractsSystem
{
    // PB6: the contracts system (پیوست ۶) and the progress statements (پیوست ۷). Tables start with "Ct" (contracts system) and "Ps" (progress statements).
    // It extends what exists (legacy Contract and Tender, the sales proposal, project-control terms, the purchase order as the supplier contract): the mapping is in docs/pb6-contracts-mapping.md.
    // Rial only (decision D7).

    // ------------------------------------------------------------------------------------------------ cartable 1: the deal request

    public enum CtDealFormKind
    {
        /// <summary>Choosing a consultant.</summary>
        Consultant = 1,
        /// <summary>The employer (project) side.</summary>
        Employer = 2,
    }

    public enum CtDealLevel
    {
        Small = 1,
        Medium = 2,
        Large = 3,
        Strategic = 4,
    }

    public enum CtDealStatus
    {
        Draft = 0,
        Submitted = 1,
        Approved = 2,
        /// <summary>Sent back to the requester with a reason; the requester edits and sends it again (a new round).</summary>
        Returned = 3,
        /// <summary>A tender run was opened for it.</summary>
        InTender = 4,
        Rejected = 9,
    }

    public class CtDealRequest : BaseEntity
    {
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

        /// <summary>Rial only (D7).</summary>
        public decimal EstimatedAmount { get; set; }

        public int? ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        /// <summary>The employer (for the employer form) or the consultant being selected, as free text.</summary>
        public string PartyName { get; set; }

        /// <summary>The employer's rank and field (رتبه و رشته).</summary>
        public string EmployerRank { get; set; }
        public string EmployerField { get; set; }

        /// <summary>How many times the request went to the approvers (1 = the first time); a returned request is edited and sent again.</summary>
        public int Round { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string ReturnReason { get; set; }
        public string EditorUserIds { get; set; }

        public ICollection<CtDealStep> Steps { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum CtStepState
    {
        Waiting = 0,
        Approved = 1,
        Returned = 2,
        Rejected = 3,
    }

    /// <summary>One step of the approval chain of a deal request. The requester may name the person who decides a step (he must hold the role).</summary>
    public class CtDealStep
    {
        public int Id { get; set; }
        public int DealRequestId { get; set; }
        public CtDealRequest DealRequest { get; set; }
        public int Round { get; set; }
        public int Order { get; set; }
        public string Role { get; set; }
        public int? DesignatedUserId { get; set; }
        public CtStepState State { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string Note { get; set; }
    }

    /// <summary>The history of a record of the contracts system (what went back and forth, who did what).</summary>
    public class CtHistory
    {
        public int Id { get; set; }

        /// <summary>"deal", "tender", "draft", "addendum"...</summary>
        public string OwnerType { get; set; }
        public int OwnerId { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public int UserId { get; set; }
        public DateTimeOffset At { get; set; }
        public int Round { get; set; }
    }

    // ------------------------------------------------------------------------------------------------ cartable 2: the tender run and the winner

    public enum CtTenderMethod
    {
        OpenTender = 1,
        LimitedTender = 2,
        Inquiry = 3,
        DirectAward = 4,
        ConsultantSelection = 5,
    }

    public enum CtGuaranteeType
    {
        None = 0,
        Cheque = 1,
        BankGuarantee = 2,
        PromissoryNote = 3,
        Cash = 4,
    }

    public enum CtTenderStatus
    {
        Open = 0,
        /// <summary>A winner was selected (with the reasons); waits for the second person.</summary>
        WinnerSelected = 1,
        WinnerConfirmed = 2,
        Cancelled = 9,
    }

    public class CtTenderRun : BaseEntity
    {
        public string Number { get; set; }
        public int DealRequestId { get; set; }
        public CtDealRequest DealRequest { get; set; }
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
        public int? WinnerSelectedByUserId { get; set; }
        public DateTimeOffset? WinnerConfirmedAt { get; set; }
        public int? WinnerConfirmedByUserId { get; set; }
        public string CancelReason { get; set; }

        public ICollection<CtTenderParticipant> Participants { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class CtTenderParticipant
    {
        public int Id { get; set; }
        public int TenderRunId { get; set; }
        public CtTenderRun TenderRun { get; set; }
        public int? ProviderId { get; set; }
        public Provider Provider { get; set; }
        public string CompanyName { get; set; }
        public decimal OfferAmount { get; set; }
        public decimal? TechnicalScore { get; set; }
        public string Notes { get; set; }
        public bool Disqualified { get; set; }
        public string DisqualifyReason { get; set; }
    }

    // ------------------------------------------------------------------------------------------------ cartable 3: the contract draft and its exchange

    public enum CtDraftStatus
    {
        Draft = 0,
        /// <summary>Sent to the counterparty.</summary>
        Exchanged = 1,
        /// <summary>The counterparty's comments are recorded.</summary>
        CommentsReceived = 2,
        Agreed = 3,
        Signed = 4,
        /// <summary>The notification (ابلاغ) is uploaded.</summary>
        Notified = 5,
        Cancelled = 9,
    }

    public class CtContractDraft : BaseEntity
    {
        public string Number { get; set; }
        public CtDraftStatus Status { get; set; }
        public int? DealRequestId { get; set; }
        public int? TenderRunId { get; set; }
        public int? ProviderId { get; set; }
        public Provider Provider { get; set; }
        public string CounterpartyName { get; set; }

        /// <summary>project / consulting / services / general / supplier.</summary>
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

        /// <summary>Assigned when the contract is signed.</summary>
        public string ContractNumber { get; set; }
        public DateTime? SignedDate { get; set; }
        public int? SignedByUserId { get; set; }
        /// <summary>M10: who agreed the draft; the same person does not sign it.</summary>
        public int? AgreedByUserId { get; set; }
        public int ExchangeRound { get; set; }
        public string CounterpartyComments { get; set; }

        public int? PurchaseOrderId { get; set; }
        public int? ContractId { get; set; }

        /// <summary>Everybody who edited the draft (",5,7,"): none of them signs it.</summary>
        public string EditorUserIds { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A line of the cost breakdown structure (CBS) of a contract, tied to a node of the work breakdown structure so the statements follow the progress of the work.</summary>
    public class CtCbsItem : BaseEntity
    {
        /// <summary>Supplier side: the order.</summary>
        public int? PurchaseOrderId { get; set; }

        /// <summary>Client side / project: the contract.</summary>
        public int? ContractId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ParentId { get; set; }
    }

    // ------------------------------------------------------------------------------------------------ progress statements (پیوست ۷ and the company form)

    public enum PsSide
    {
        /// <summary>A supplier or subcontractor statement for us to pay.</summary>
        Supplier = 1,
        /// <summary>A statement we send to the client.</summary>
        Client = 2,
    }

    public enum PsStatus
    {
        Draft = 0,
        /// <summary>Prepared by commercial, waits for the technical office.</summary>
        Prepared = 1,
        TechnicalApproved = 2,
        SupervisionApproved = 3,
        /// <summary>The project manager of the contract authorised the payment.</summary>
        PaymentAuthorised = 4,
        PaymentRequested = 5,
        Paid = 6,
        Returned = 8,
        Rejected = 9,
    }

    public enum PsCounterpartyStatus
    {
        NotSent = 0,
        Sent = 1,
        Approved = 2,
        Disputed = 3,
    }

    public enum PsPayMethod
    {
        Cheque = 1,
        BankTransfer = 2,
        Cash = 3,
        Other = 9,
    }

    public class PsStatement : BaseEntity
    {
        public string Number { get; set; }
        public PsSide Side { get; set; }
        public PsStatus Status { get; set; }

        public int? PurchaseOrderId { get; set; }
        public PurchaseOrder PurchaseOrder { get; set; }
        public int? ProviderId { get; set; }
        public Provider Provider { get; set; }

        /// <summary>The project contract (client side: the client contract).</summary>
        public int? ContractId { get; set; }

        /// <summary>Client side: the old provisional statement this one continues, when there is one.</summary>
        public int? ProvisionalStatementId { get; set; }

        public int Serial { get; set; }
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }
        public DateTime? SendingDate { get; set; }
        public string Subject { get; set; }
        public string SupportingDocuments { get; set; }

        public PsCounterpartyStatus CounterpartyStatus { get; set; }
        public string CounterpartyComments { get; set; }

        // totals (rial)
        public decimal ContractAmount { get; set; }
        public decimal AddendaAmount { get; set; }
        public decimal PreviousPayments { get; set; }
        public decimal NonconformityDeductions { get; set; }
        public decimal LegalDeductions { get; set; }
        public decimal GrossThisPeriod { get; set; }
        public decimal PayableThisPeriod { get; set; }

        public DateTimeOffset? PreparedAt { get; set; }
        public int? PreparedByUserId { get; set; }
        public decimal? TechnicalApprovedAmount { get; set; }
        public DateTimeOffset? TechnicalApprovedAt { get; set; }
        public int? TechnicalApprovedByUserId { get; set; }
        public decimal? SupervisionApprovedAmount { get; set; }
        public DateTimeOffset? SupervisionApprovedAt { get; set; }
        public int? SupervisionApprovedByUserId { get; set; }
        public DateTimeOffset? PaymentAuthorisedAt { get; set; }
        public int? PaymentAuthorisedByUserId { get; set; }

        public int? PaymentRequestId { get; set; }

        // the payment: date, cheque number (the cheque image is an attachment), the amount received against the amount expected after the deductions
        public DateTime? PaidDate { get; set; }
        public PsPayMethod? PayMethod { get; set; }
        public string ChequeNumber { get; set; }
        public decimal? ExpectedAmount { get; set; }
        public decimal? ReceivedAmount { get; set; }
        public decimal? PaymentVariance { get; set; }
        public DateTimeOffset? PaymentRecordedAt { get; set; }
        public int? PaymentRecordedByUserId { get; set; }

        public DateTimeOffset? ReturnedAt { get; set; }
        public string ReturnReason { get; set; }
        public string EditorUserIds { get; set; }

        public ICollection<PsStatementLine> Lines { get; set; }
        public ICollection<PsPreviousPayment> PreviousPaymentRows { get; set; }
        public ICollection<PsDeduction> Deductions { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PsStatementLine
    {
        public int Id { get; set; }
        public int StatementId { get; set; }
        public PsStatement Statement { get; set; }
        public int RowNumber { get; set; }
        public string Title { get; set; }
        public string SerialOrDrawing { get; set; }
        public string ProjectCode { get; set; }
        public string EquipmentCode { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }

        /// <summary>Quantity x unit price.</summary>
        public decimal LineTotal { get; set; }

        // what is claimed in this period, by stage of the payment terms
        public decimal AdvanceAmount { get; set; }
        public decimal AfterDeliveryQcAmount { get; set; }
        public decimal GoodPerformanceAmount { get; set; }
        public decimal AfterFinalTestsAmount { get; set; }
        public decimal ThisPeriodAmount { get; set; }

        /// <summary>"According to the contract addendum": the line comes from an addendum.</summary>
        public bool IsAddendum { get; set; }
        public int? OrderLineId { get; set; }
        public int? CbsItemId { get; set; }

        /// <summary>The progress of the WBS node of the CBS item when the line was written (the statement follows the progress).</summary>
        public decimal? ProgressPercent { get; set; }
    }

    public class PsPreviousPayment
    {
        public int Id { get; set; }
        public int StatementId { get; set; }
        public PsStatement Statement { get; set; }
        public decimal Amount { get; set; }
        public string PaymentDocNumber { get; set; }
        public string MethodTitle { get; set; }
        public string PaymentTitle { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string StatementSerial { get; set; }

        /// <summary>Filled by the system from the earlier statements and the paid advances (not typed).</summary>
        public bool IsAuto { get; set; }
    }

    public enum PsDeductionKind
    {
        Nonconformity = 1,
        Legal = 2,
    }

    public class PsDeduction
    {
        public int Id { get; set; }
        public int StatementId { get; set; }
        public PsStatement Statement { get; set; }
        public PsDeductionKind Kind { get; set; }
        public int RowNumber { get; set; }
        public string Title { get; set; }

        // nonconformity (the company form): unit price, quantity, basis of the delay or repair, kind of deviation, letter number, serial, item
        public string ItemTitle { get; set; }
        public string SerialNumber { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public string Basis { get; set; }
        public string DeviationType { get; set; }
        public string LetterNumber { get; set; }
        public int? NcrId { get; set; }

        // legal deduction: percent of the base, taken from a verified rate of the rate table
        public int? LegalRateId { get; set; }
        public decimal? Percent { get; set; }
        public decimal? BaseAmount { get; set; }
        public decimal Amount { get; set; }
    }

    public enum PsRateStatus
    {
        /// <summary>Typed by one person; cannot be used until a second person verifies it.</summary>
        Draft = 0,
        Verified = 1,
        Retired = 2,
    }

    /// <summary>Rate table of the legal deductions (insurance, tax, etc.). The system ships with no rates: finance types them and a second person verifies them (no invented rates).</summary>
    public class PsLegalRate : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public decimal Percent { get; set; }

        /// <summary>M7: what kind of deduction the rate is, so the payment request carries it typed into the ledger and the Sepidar data (tax, insurance, good performance).</summary>
        public Entities.Model.NasbNiro.Finance.FnDeductionKind DeductionKind { get; set; } = Entities.Model.NasbNiro.Finance.FnDeductionKind.Withholding;

        /// <summary>What the percent applies to (for example the gross amount of the statement).</summary>
        public string BasisNote { get; set; }

        /// <summary>The law, regulation or letter the rate comes from.</summary>
        public string SourceReference { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        public PsRateStatus Status { get; set; }
        public DateTimeOffset? VerifiedAt { get; set; }
        public int? VerifiedByUserId { get; set; }
        public string EditorUserIds { get; set; }
    }

    public enum PsCashDirection
    {
        Receipt = 1,
        Payment = 2,
    }

    /// <summary>Planned (accrual) receipts and payments of a contract; the actual ones come from the paid statements and payments.</summary>
    public class PsCashPlanItem : BaseEntity
    {
        public int? PurchaseOrderId { get; set; }
        public int? ContractId { get; set; }
        public PsCashDirection Direction { get; set; }
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public string Note { get; set; }
        public int? PaymentTermId { get; set; }
    }
}
