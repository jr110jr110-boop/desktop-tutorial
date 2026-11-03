using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    public class PcMilestoneDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public int Seq { get; set; }
        public string Title { get; set; }
        public PcMilestoneKind Kind { get; set; }
        public PcMilestoneTrigger Trigger { get; set; }
        public decimal? TriggerPercent { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ActivityId { get; set; }
        public decimal? PercentOfValue { get; set; }
        public decimal Amount { get; set; }
        public DateTime? PlannedDate { get; set; }
        public PcMilestoneStatus Status { get; set; }
        public DateTimeOffset? DueAt { get; set; }
        public DateTimeOffset? PaidAt { get; set; }
        public int? ProvisionalStatementId { get; set; }
        public string FinanceRef { get; set; }
        public string ClientLetterRef { get; set; }

        /// <summary>R1c (M8): marks the final statement of the contract (at most one per contract; a retention milestone is always treated as final).</summary>
        public bool IsFinal { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcMilestoneActionDto
    {
        public int Id { get; set; }

        /// <summary>statement, invoiced, paid, client-letter</summary>
        public string Action { get; set; }
        public int? ProvisionalStatementId { get; set; }
        public string Reference { get; set; }
    }

    public class PcMilestoneEvaluateResultDto
    {
        public int ContractId { get; set; }
        public int BecameDue { get; set; }
        public List<PcMilestoneDto> Milestones { get; set; } = new List<PcMilestoneDto>();
    }

    public class PcGuaranteeDto
    {
        public int? Id { get; set; }
        public PcGuaranteeSource SourceKind { get; set; }
        public int SourceId { get; set; }
        public int? ContractId { get; set; }
        public string Title { get; set; }
        public string Bank { get; set; }
        public string No { get; set; }
        public string LegacyPriceText { get; set; }
        public PcGuaranteeType GuaranteeType { get; set; }
        public decimal? AmountDecimal { get; set; }
        public string CurrencyCode { get; set; }
        public PcGuaranteeIssuer IssuerKind { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public PcGuaranteeRelease ReleaseTrigger { get; set; }
        public PcGuaranteeStatus Status { get; set; }
        public bool ParseFailed { get; set; }
        public string ParseNote { get; set; }
        public int? DaysToExpiry { get; set; }

        /// <summary>
        /// R1a2 (M2, M-3): read-only. The request for the release or the cancellation: who asked, when, for which of the two (1 release, 2 cancellation), with which date and note.
        /// It waits for a different user to confirm with the SAME action (the other action is refused); a request can be withdrawn with guarantees/withdraw.
        /// <see cref="ReleaseRequestOpen"/> is true while the request can still be confirmed (not older than seven days, the guarantee not closed); after a confirmation the fields stay as the record of who asked.
        /// </summary>
        public int? ReleaseRequestedByUserId { get; set; }
        public DateTime? ReleaseRequestedAt { get; set; }
        public PcGuaranteeCloseKind? ReleaseRequestedKind { get; set; }
        public string ReleaseRequestedNote { get; set; }
        public DateTime? ReleaseRequestedDate { get; set; }
        public bool ReleaseRequestOpen { get; set; }

        /// <summary>R1-gaps, for the signed-in caller: may ask for the release now (guarantees/request-release): allowed role, no request waits, the guarantee is not closed.</summary>
        public bool CanRequestRelease { get; set; }

        /// <summary>R1-gaps, for the signed-in caller: may confirm the waiting request for the release (guarantees/confirm-release): allowed role, the open request is a release and somebody else made it.</summary>
        public bool CanConfirmRelease { get; set; }

        public List<PcGuaranteeEventDto> Events { get; set; } = new List<PcGuaranteeEventDto>();
    }

    public class PcGuaranteeEventDto
    {
        public int? Id { get; set; }
        public PcGuaranteeEventKind Kind { get; set; }
        public DateTime EventDate { get; set; }
        public DateTime? NewExpiryDate { get; set; }
        public decimal? Fee { get; set; }
        public Guid? DocumentId { get; set; }
        public string Note { get; set; }
    }

    public class PcGuaranteeEventSaveDto : PcGuaranteeEventDto
    {
        public int GuaranteeId { get; set; }
    }

    /// <summary>
    /// R1a: the release of a guarantee. The date (not in the future) and the note (the letter of the client or the bank) are required by request-release; confirm-release needs neither (the request holds them).
    /// R1-gaps: guarantees/request-release records the request, guarantees/confirm-release (a different user) closes it; guarantees/release is the deprecated alias that decides by state.
    /// </summary>
    public class PcGuaranteeReleaseDto
    {
        public int GuaranteeId { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string Note { get; set; }
        public Guid? DocumentId { get; set; }
    }

    /// <summary>R1a2 (M-3): takes back the open request for the release or the cancellation. Only the one who asked, or a PMO user, can do it.</summary>
    public class PcGuaranteeWithdrawDto
    {
        public int GuaranteeId { get; set; }
    }

    public class PcGuaranteeQuery : PaginationParams
    {
        public int? ContractId { get; set; }
        public PcGuaranteeStatus? Status { get; set; }
        public bool OnlyUnparsed { get; set; }
    }

    public class PcGuaranteeParseResultDto
    {
        public int Created { get; set; }
        public int Parsed { get; set; }
        public int Failed { get; set; }
    }

    public class PcGuaranteeAlertDto
    {
        public int GuaranteeId { get; set; }
        public int? ContractId { get; set; }
        public string Title { get; set; }
        public string No { get; set; }
        public DateTime ExpiryDate { get; set; }
        public int DaysToExpiry { get; set; }

        /// <summary>The alert step reached: 30, 14, 7 (or 0 when already expired).</summary>
        public int AlertLevel { get; set; }
    }

    public class PcPenaltyDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public int? MilestoneId { get; set; }
        public int? WorkOrderId { get; set; }
        public string Subject { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ActualDate { get; set; }
        public int LateDays { get; set; }
        public int GraceDays { get; set; }
        public decimal RatePercentPerWeek { get; set; }
        public decimal? CapPercent { get; set; }
        public decimal BaseAmount { get; set; }
        public decimal Amount { get; set; }
        public PcPenaltyStatus Status { get; set; }
        public string DecisionReason { get; set; }
        public DateTimeOffset ComputedAt { get; set; }
    }

    public class PcPenaltyDecisionDto
    {
        public int Id { get; set; }
        public PcPenaltyStatus Status { get; set; }
        public string Reason { get; set; }

        /// <summary>Negotiated: the amount agreed.</summary>
        public decimal? Amount { get; set; }
    }

    public class PcVariationDto
    {
        public int? Id { get; set; }
        public string Number { get; set; }
        public int ContractId { get; set; }
        public int? ChangeRequestId { get; set; }
        public int? AmendmentId { get; set; }
        public string Description { get; set; }
        public decimal PriceDelta { get; set; }
        public int DaysDelta { get; set; }
        public string ClientApprovalReference { get; set; }
        public DateTime? ClientApprovalDate { get; set; }
        public PcVariationStatus Status { get; set; }

        // R1c (H3): who sent and who approved, the client's letter, and who must approve (read only)
        public int? SubmittedByUserId { get; set; }
        public string SubmittedByName { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }

        /// <summary>PMO or CEO: the role the approval was given as.</summary>
        public string ApprovedByRole { get; set; }
        public int? ClientLetterId { get; set; }
        public Guid? ClientDocumentId { get; set; }
        public DateTimeOffset? TermsAppliedAt { get; set; }
        public string DecisionNote { get; set; }

        /// <summary>True when the price change is above the threshold of the settings (a share of the contract value): only the CEO approves.</summary>
        public bool NeedsCeo { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcVariationActionDto
    {
        public int Id { get; set; }

        /// <summary>submit (the commercial unit), approve (PMO or CEO), reject (PMO or CEO, with a reason in Reference)</summary>
        public string Action { get; set; }
        public string Reference { get; set; }
        public DateTime? Date { get; set; }

        /// <summary>approve: the client's letter (an incoming letter of the office module for this contract) or an uploaded file; one of them is required.</summary>
        public int? ClientLetterId { get; set; }
        public Guid? ClientDocumentId { get; set; }

        /// <summary>reject: the reason.</summary>
        public string Note { get; set; }
    }
}
