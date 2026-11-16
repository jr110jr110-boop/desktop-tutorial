using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Engineering
{
    // ENG-E1 (engineering module, external workflow): the reference document list (the definition of "complete"), the platform dossier of every product of the bank, the engineering case
    // (classification gate, tender package, charter review), clarifications through commercial, the handover to the PMO with its lock, the emergency waiver and the document debts, and the distribution.
    // Records are never deleted physically: they are voided with a reason. Every decision column is named so that the data layer (SeatApprovalGuard) refuses an AI seat. No migration is written here.

    public enum EnRefStatus { Draft = 0, UnderReview = 1, Approved = 2, Superseded = 3, Void = 4 }

    public enum EnDossierStatus { NotAssessed = 0, Incomplete = 1, Completing = 2, Complete = 3 }

    public enum EnLineState { Missing = 0, Available = 1, NotApplicable = 2 }

    public enum EnClassification { None = 0, Platform = 1, New = 2 }

    public enum EnCaseStatus
    {
        Classifying = 1, AwaitingEngConfirm = 2, AwaitingCeoApproval = 3, TenderPreparation = 4, TenderIssued = 5, Awarded = 6, InEngineering = 7, HandedOver = 8, Accepted = 9, Released = 10, ClosedLost = 11, Cancelled = 12,
    }

    public enum EnClarStatus { Draft = 0, WithCommercial = 1, SentToClient = 2, Answered = 3, Closed = 4, Void = 5 }

    public enum EnCharterOutcome { None = 0, Confirmed = 1, Returned = 2 }

    public enum EnHandoverStatus { Draft = 0, Submitted = 1, Returned = 2, Accepted = 3, Released = 4, Void = 5 }

    public enum EnWaiverStatus { Requested = 0, Approved = 1, Rejected = 2, Void = 4 }

    public enum EnDebtStatus { Open = 0, Settled = 1, Void = 2 }

    public enum EnUnit { Qa = 1, Qc = 2, Production = 3, Procurement = 4, Warehouse = 5, Planning = 6 }

    // ------------------------------------------------------------------------------------------ the reference document list

    /// <summary>A revision of the reference list of engineering documents (VDDR) a platform needs to be complete. Based on API 611/612 and PMBOK; the system ships no content of its own.</summary>
    public class EnReferenceList : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>For example "API 612".</summary>
        public string StandardSource { get; set; }

        public int Revision { get; set; } = 1;
        public EnRefStatus Status { get; set; }
        public int PreparedByUserId { get; set; }

        /// <summary>Everyone who drafted or edited it (",5,7,"); none of them approves it.</summary>
        public string EditorUserIds { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }

        /// <summary>SHA-256 of the lines at the submission; the approval binds to it.</summary>
        public string ContentHash { get; set; }

        public string Note { get; set; }
        public string VoidReason { get; set; }

        public ICollection<EnReferenceLine> Lines { get; set; } = new List<EnReferenceLine>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class EnReferenceLine
    {
        public int Id { get; set; }
        public int ListId { get; set; }
        public EnReferenceList List { get; set; }
        public int Seq { get; set; }
        public string DocumentType { get; set; }
        public string Discipline { get; set; }
        public string Title { get; set; }
        public bool Required { get; set; } = true;

        /// <summary>The standard says the document is needed at the tender stage.</summary>
        public bool TenderStage { get; set; }

        public bool ClientSubmittal { get; set; }
        public string StandardClause { get; set; }
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------ the platform dossier

    /// <summary>The engineering documents of one platform (an active product of the bank) against the approved reference list. Complete only when a manager confirmed it.</summary>
    public class EnPlatformDossier : BaseEntity
    {
        public int ProductId { get; set; }
        public int? ReferenceListId { get; set; }

        /// <summary>The Code of the reference list (the standard basis, e.g. API 611 or API 612) this platform is assessed against; chosen by the engineering manager when more than one Code is approved.</summary>
        public string ReferenceCode { get; set; }

        /// <summary>Where the standard of this dossier is known from (the data sheet of the project the platform was built for, or the client data sheet of the case that created it).</summary>
        public string StandardBasis { get; set; }
        public int ReferenceRevision { get; set; }
        public EnDossierStatus Status { get; set; }
        public DateTimeOffset? AssessedAt { get; set; }
        public int? LastEditedByUserId { get; set; }
        public string EditorUserIds { get; set; }
        public int? ConfirmedByUserId { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }

        /// <summary>SHA-256 of the lines at the confirmation; any later change of a line voids the confirmation.</summary>
        public string ConfirmedHash { get; set; }

        public string Note { get; set; }

        public ICollection<EnPlatformDocument> Documents { get; set; } = new List<EnPlatformDocument>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class EnPlatformDocument
    {
        public int Id { get; set; }
        public int DossierId { get; set; }
        public EnPlatformDossier Dossier { get; set; }
        public int? ReferenceLineId { get; set; }
        public int Seq { get; set; }
        public string DocumentType { get; set; }
        public string Discipline { get; set; }
        public string Title { get; set; }
        public bool Required { get; set; } = true;
        public bool TenderStage { get; set; }
        public EnLineState State { get; set; }

        /// <summary>The file uploaded through the documents API.</summary>
        public Guid? DocumentId { get; set; }

        /// <summary>The controlled document of the EDMS, when there is one.</summary>
        public int? ControlledDocumentId { get; set; }

        public string Revision { get; set; }
        public string NotApplicableReason { get; set; }

        /// <summary>The system proved the line from its own state (a released BOM, an approved routing); no file is needed.</summary>
        public bool SystemProven { get; set; }

        /// <summary>The approved deviation (head of engineering and CEO) that made a Required line not applicable.</summary>
        public int? DeviationId { get; set; }

        public int? SuppliedByUserId { get; set; }
        public DateTimeOffset? SuppliedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ the case

    /// <summary>One engineering case: an inquiry (tender) or a direct contract. It carries the classification (platform or new design), the tender package, the charter review and the handover.</summary>
    public class EnCase : BaseEntity
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public int? InquiryId { get; set; }
        public int? ContractId { get; set; }
        public string CustomerRequirementNote { get; set; }

        /// <summary>The content hash of the standard-gap report the CEO approved the case with (empty when there was no gap).</summary>
        public string ApprovedGapHash { get; set; }
        public EnCaseStatus Status { get; set; } = EnCaseStatus.Classifying;
        public EnClassification Classification { get; set; }
        public int? ProductId { get; set; }
        public bool PlatformWithChanges { get; set; }

        /// <summary>The Code of the reference list a New-design handover is built from; set by the manager at the classification confirm.</summary>
        public string ReferenceCode { get; set; }

        /// <summary>Where the standard of the project is known from: the data sheet number and clause of the client (the client sets the standard; nobody in the company chooses it).</summary>
        public string StandardEvidence { get; set; }

        /// <summary>Optional attachment (uploaded document) of the client data sheet.</summary>
        public Guid? StandardAttachmentId { get; set; }
        public string ChangeNote { get; set; }

        public EnClassification ProposedClassification { get; set; }
        public int? ProposedProductId { get; set; }
        public bool ProposedWithChanges { get; set; }
        public string ProposedChangeNote { get; set; }
        public string ProposalBasis { get; set; }
        public int? ProposedByUserId { get; set; }
        public DateTimeOffset? ProposedAt { get; set; }

        public string EditorUserIds { get; set; }
        public int? EngConfirmedByUserId { get; set; }
        public DateTimeOffset? EngConfirmedAt { get; set; }
        public int? CeoApprovedByUserId { get; set; }
        public DateTimeOffset? CeoApprovedAt { get; set; }
        public string ClassificationHash { get; set; }
        public string ReturnReason { get; set; }

        public int? TenderPackageId { get; set; }
        public int? TenderReleasedByUserId { get; set; }
        public DateTimeOffset? TenderReleasedAt { get; set; }
        public string TenderHash { get; set; }

        /// <summary>New design: what the conceptual design defines and what it assumes.</summary>
        public string ConceptNote { get; set; }

        public EnCharterOutcome CharterReviewOutcome { get; set; }
        public int? CharterReviewedByUserId { get; set; }
        public DateTimeOffset? CharterReviewedAt { get; set; }
        public string CharterHash { get; set; }
        public string CharterReviewNote { get; set; }

        public string ClosedReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Append-only history of a case: one row for every transition.</summary>
    public class EnCaseEvent
    {
        public int Id { get; set; }
        public int CaseId { get; set; }
        public string Kind { get; set; }
        public DateTimeOffset At { get; set; }
        public int? UserId { get; set; }
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------ clarification through commercial

    public class EnClarification : BaseEntity
    {
        public string Number { get; set; }
        public int CaseId { get; set; }
        public int? FollowsId { get; set; }
        public string Question { get; set; }
        public string QuestionAttachmentIds { get; set; }
        public int AskedByUserId { get; set; }
        public DateTimeOffset AskedAt { get; set; }
        public EnClarStatus Status { get; set; }
        public int? SentToClientByUserId { get; set; }
        public DateTimeOffset? SentToClientAt { get; set; }
        public string ClientLetterRef { get; set; }
        public string Answer { get; set; }
        public string AnswerAttachmentIds { get; set; }
        public int? AnswerRecordedByUserId { get; set; }
        public DateTimeOffset? AnswerRecordedAt { get; set; }
        public int? ClosedByUserId { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string VoidReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    // ------------------------------------------------------------------------------------------ handover to the PMO

    public class EnHandover : BaseEntity
    {
        public string Number { get; set; }
        public int CaseId { get; set; }
        public int ContractId { get; set; }
        public int Round { get; set; } = 1;
        public EnHandoverStatus Status { get; set; }
        public int PreparedByUserId { get; set; }
        public string EditorUserIds { get; set; }

        /// <summary>Computed at the submission: false when it passed the lock with an emergency waiver.</summary>
        public bool Complete { get; set; }

        public int MissingRequiredCount { get; set; }
        public int? WaiverId { get; set; }
        public int? SubmittedByUserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? AcceptedByUserId { get; set; }
        public DateTimeOffset? AcceptedAt { get; set; }
        public int? ReturnedByUserId { get; set; }
        public DateTimeOffset? ReturnedAt { get; set; }
        public string ReturnReason { get; set; }
        public int? ReleasedByUserId { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public string ContentHash { get; set; }
        public string Note { get; set; }
        public string VoidReason { get; set; }

        public ICollection<EnHandoverLine> Lines { get; set; } = new List<EnHandoverLine>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class EnHandoverLine
    {
        public int Id { get; set; }
        public int HandoverId { get; set; }
        public EnHandover Handover { get; set; }
        public int Seq { get; set; }
        public string DocumentType { get; set; }
        public string Discipline { get; set; }
        public string Title { get; set; }
        public bool Required { get; set; } = true;
        public EnLineState State { get; set; }
        public Guid? DocumentId { get; set; }
        public string Revision { get; set; }
        public int? PlatformDocumentId { get; set; }

        /// <summary>The project MDR row (PcEngineeringDeliverable) that matches the line.</summary>
        public int? DeliverableId { get; set; }

        /// <summary>The bank page that proves a system-derived line (BOM, manufacturing route); no file.</summary>
        public string LinkPath { get; set; }

        public string Note { get; set; }
    }

    /// <summary>The emergency waiver of the handover lock: approved by the CEO personally, for one handover and only for the missing set of its snapshot.</summary>
    public class EnWaiver : BaseEntity
    {
        public string Number { get; set; }
        public int HandoverId { get; set; }
        public int CaseId { get; set; }
        public string Reason { get; set; }

        /// <summary>JSON: the missing lines at the request.</summary>
        public string MissingSnapshot { get; set; }

        public DateTimeOffset DebtDueAt { get; set; }
        public EnWaiverStatus Status { get; set; }
        public int RequestedByUserId { get; set; }
        public DateTimeOffset RequestedAt { get; set; }

        /// <summary>A request that nobody decided by then expires; an approval that was not used by then expires too.</summary>

        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }

        /// <summary>The handover was submitted with it: a waiver is used once.</summary>
        public DateTimeOffset? UsedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A required document that a waived handover did not have: it stays open until the document exists (zero tolerance).</summary>
    public class EnDocumentDebt : BaseEntity
    {
        public int WaiverId { get; set; }
        public int HandoverLineId { get; set; }
        public int CaseId { get; set; }
        public int ContractId { get; set; }
        public int? ProductId { get; set; }
        public string DocumentType { get; set; }
        public string Title { get; set; }
        public DateTimeOffset DueAt { get; set; }
        public EnDebtStatus Status { get; set; }
        public DateTimeOffset? SettledAt { get; set; }
        public int? SettledByUserId { get; set; }
        public Guid? SettledDocumentId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ distribution

    public class EnDistribution : BaseEntity
    {
        public int HandoverId { get; set; }
        public int CaseId { get; set; }
        public int ContractId { get; set; }
        public EnUnit Unit { get; set; }
        public string Roles { get; set; }
        public DateTimeOffset NotifiedAt { get; set; }
        public int? AcknowledgedByUserId { get; set; }
        public DateTimeOffset? AcknowledgedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
