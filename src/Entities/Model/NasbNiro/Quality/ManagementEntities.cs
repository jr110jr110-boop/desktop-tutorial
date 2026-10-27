using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;

namespace Entities.Model.NasbNiro.Quality
{
    // Material certificates, CAPA, complaints, release, document control, audits and management review.

    public class MaterialCertificate : BaseEntity
    {
        public int LotId { get; set; }
        public InventoryLot Lot { get; set; }

        public CertificateType Type { get; set; }
        public string Number { get; set; }
        public string Issuer { get; set; }
        public Guid? DocumentId { get; set; }

        /// <summary>Chemistry and mechanical values in short.</summary>
        public string Summary { get; set; }

        public CertificateResult Result { get; set; }
        public int? VerifiedByUserId { get; set; }
        public DateTimeOffset? VerifiedAt { get; set; }
        public string VerificationNote { get; set; }
    }

    public class Capa : BaseEntity
    {
        public string Number { get; set; }
        public CapaSource Source { get; set; }
        public int? SourceId { get; set; }
        public string Problem { get; set; }
        public string Containment { get; set; }
        public RootCauseMethod RootCauseMethod { get; set; }
        public string RootCauseAnalysis { get; set; }

        /// <summary>Empty while the system-drafted CAPA waits for QA to assign it.</summary>
        public int? OwnerUserId { get; set; }

        public DateTime? DueDate { get; set; }
        public CapaStatus Status { get; set; }
        public string EffectivenessCriteria { get; set; }
        public DateTime? EffectivenessDueDate { get; set; }
        public string EffectivenessResult { get; set; }
        public DateTime? ImplementedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }
        public string CloseNote { get; set; }
        public bool AutoDrafted { get; set; }

        // ----- R1q
        /// <summary>1 for the first round of actions; a failed effectiveness check re-opens the CAPA with the next number (R1q D20).</summary>
        public int Iteration { get; set; } = 1;

        /// <summary>0 = none, 1 = QA told it is late, 2 = the CEO told (the nightly job of R1q D13).</summary>
        public int EscalationLevel { get; set; }

        public DateTimeOffset? EscalatedAt { get; set; }

        /// <summary>
        /// Required by a rule when it was created (drafted by the system, from a non-conformance or from an audit finding): fixed at creation, so editing the source cannot
        /// turn it into an optional CAPA; only the QA manager cancels it (R1q D21, review M11).
        /// </summary>
        public bool Mandatory { get; set; }

        /// <summary>Everybody who edited the CAPA or finished an action for another person, ids between commas: none of them judges the effectiveness, closes or cancels it (review M11).</summary>
        public string EditorUserIds { get; set; }

        /// <summary>The reason a CAPA was cancelled (R1q D21).</summary>
        public string CancelReason { get; set; }

        public DateTimeOffset? CancelledAt { get; set; }
        public int? CancelledByUserId { get; set; }

        public ICollection<CapaAction> Actions { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class CapaAction
    {
        public int Id { get; set; }

        public int CapaId { get; set; }
        public Capa Capa { get; set; }

        public string Description { get; set; }
        public int? OwnerUserId { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? DoneAt { get; set; }
        public Guid? EvidenceDocumentId { get; set; }
        public string EvidenceNote { get; set; }

        /// <summary>The round of the CAPA this action belongs to (<see cref="Capa.Iteration"/>).</summary>
        public int Iteration { get; set; } = 1;
    }

    public class CustomerComplaint : BaseEntity
    {
        public string Number { get; set; }

        public int? ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public DateTime ReceivedDate { get; set; }
        public string Channel { get; set; }
        public string Description { get; set; }
        public NcrSeverity Severity { get; set; }
        public DateTime ResponseDue { get; set; }
        public ComplaintStatus Status { get; set; }

        public int? NcrId { get; set; }
        public int? CapaId { get; set; }
        public Capa Capa { get; set; }

        public string Response { get; set; }
        public DateTime? RespondedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }

        /// <summary>The after-sales service request this complaint was opened from (release S1 part B); the request points back with its ComplaintId. The foreign key is configured by the service module.</summary>
        public int? ServiceRequestId { get; set; }
        public AfterSales.SvServiceRequest ServiceRequest { get; set; }
    }

    public class ReleaseNote : BaseEntity
    {
        public string Number { get; set; }

        public int WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public int ContractId { get; set; }

        /// <summary>Products and serial numbers released.</summary>
        public string ItemsText { get; set; }

        public ReleaseNoteStatus Status { get; set; }

        /// <summary>JSON of the checks at the moment of release.</summary>
        public string ChecksSnapshot { get; set; }

        public string CertificateOfConformityNumber { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public int? ReleasedByUserId { get; set; }
        public string CancelReason { get; set; }
        public string Note { get; set; }

        /// <summary>Everybody who saved the note, ids between commas: none of them may release it (R1q D22).</summary>
        public string EditorUserIds { get; set; }

        /// <summary>The products and serial numbers released: a customer delivery of the work order may only carry these (R1q D10).</summary>
        public ICollection<ReleaseNoteLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>One released product of a release note: the item with its quantity and, for a serial item, the serial numbers.</summary>
    public class ReleaseNoteLine
    {
        public int Id { get; set; }

        public int ReleaseNoteId { get; set; }
        public ReleaseNote ReleaseNote { get; set; }

        public int RowNumber { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public decimal Quantity { get; set; }

        /// <summary>Serial numbers, one per line of text (serial items only).</summary>
        public string SerialNumbers { get; set; }
    }

    public class ControlledDocument : BaseEntity
    {
        /// <summary>TYPE-DEPT-NNN, for example WI-QC-004.</summary>
        public string Code { get; set; }

        public ControlledDocumentType Type { get; set; }
        public string Department { get; set; }
        public string Title { get; set; }
        public string CurrentRevision { get; set; }
        public ControlledDocumentStatus Status { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public string RetentionClass { get; set; }
        public string ResponsibleUnit { get; set; }

        /// <summary>Project records (final data books) are linked to the contract and work order.</summary>
        public int? ContractId { get; set; }
        public int? WorkOrderId { get; set; }

        public ICollection<DocumentRevisionRecord> Revisions { get; set; }
    }

    public class DocumentRevisionRecord : BaseEntity
    {
        public int DocumentId { get; set; }
        public ControlledDocument Document { get; set; }

        public string Revision { get; set; }
        public string ChangeSummary { get; set; }
        public Guid? FileDocumentId { get; set; }
        public ControlledDocumentStatus Status { get; set; }

        public int PreparedByUserId { get; set; }
        public int? ReviewedByUserId { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public DateTime? EffectiveDate { get; set; }

        public ICollection<DocumentDistribution> Distributions { get; set; }
    }

    public class DocumentDistribution
    {
        public int Id { get; set; }

        public int RevisionId { get; set; }
        public DocumentRevisionRecord Revision { get; set; }

        public int? RecipientUserId { get; set; }
        public string RecipientUnit { get; set; }
        public DateTimeOffset SentAt { get; set; }
        public DateTimeOffset? AcknowledgedAt { get; set; }
        public int? AcknowledgedByUserId { get; set; }
    }

    public class RetentionRule
    {
        public int Id { get; set; }
        public string RecordType { get; set; }

        /// <summary>Empty = permanent (life of the product).</summary>
        public int? Years { get; set; }

        public bool Permanent { get; set; }
        public string Source { get; set; }
    }

    public class AuditProgram : BaseEntity
    {
        public int Year { get; set; }
        public string Scope { get; set; }
        public string Note { get; set; }
    }

    public class Audit : BaseEntity
    {
        public string Number { get; set; }

        public int? ProgramId { get; set; }
        public AuditProgram Program { get; set; }

        /// <summary>Role name of the audited unit (QC, ENGINEERING, CONSTRUCTION...): the auditors must not work in it.</summary>
        public string AuditedUnit { get; set; }

        public string Criteria { get; set; }
        public DateTime PlannedDate { get; set; }
        public int LeadAuditorUserId { get; set; }

        /// <summary>User ids of the audit team, separated by commas.</summary>
        public string TeamUserIds { get; set; }

        public AuditStatus Status { get; set; }
        public Guid? ReportDocumentId { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }

        public ICollection<AuditFinding> Findings { get; set; }
    }

    public class AuditFinding : BaseEntity
    {
        public int AuditId { get; set; }
        public Audit Audit { get; set; }

        public string Clause { get; set; }
        public AuditFindingType Type { get; set; }
        public string Evidence { get; set; }

        public int? CapaId { get; set; }
        public Capa Capa { get; set; }

        public DateTime? DueDate { get; set; }
    }

    public class ManagementReview : BaseEntity
    {
        public DateTime ReviewDate { get; set; }
        public string Attendees { get; set; }
        public ReviewStatus Status { get; set; }

        /// <summary>Minutes (FO-QA-022).</summary>
        public Guid? MinutesDocumentId { get; set; }

        /// <summary>The office automation meeting (OfMeeting) whose minutes and decisions belong to this review; new reviews link here instead of keeping their own decision list.</summary>
        public int? OfMeetingId { get; set; }

        public ICollection<ManagementReviewInput> Inputs { get; set; }
        public ICollection<ManagementReviewDecision> Decisions { get; set; }
    }

    public class ManagementReviewInput
    {
        public int Id { get; set; }

        public int ReviewId { get; set; }
        public ManagementReview Review { get; set; }

        public ReviewInputCategory Category { get; set; }
        public string Text { get; set; }

        /// <summary>The text was drafted by the system from the records (counts of NCR, CAPA, audit findings, complaints).</summary>
        public bool AutoDrafted { get; set; }
    }

    public class ManagementReviewDecision
    {
        public int Id { get; set; }

        public int ReviewId { get; set; }
        public ManagementReview Review { get; set; }

        public string Text { get; set; }
        public int? ResponsibleUserId { get; set; }
        public DateTime? DueDate { get; set; }

        public int? CapaId { get; set; }
        public Capa Capa { get; set; }

        // ----- R1q D23: a decision is followed up until it is done
        public ReviewDecisionStatus Status { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public int? DoneByUserId { get; set; }
        public string DoneNote { get; set; }

        // ----- review M17: the responsible person reports the work done; QA or PMO (not he himself) confirms it
        /// <summary>The responsible person reported the decision done (the status stays Open until a reviewer confirms).</summary>
        public DateTimeOffset? DoneRequestedAt { get; set; }

        public int? DoneRequestedByUserId { get; set; }
        public string DoneRequestNote { get; set; }
    }

    /// <summary>Numbers the company can change (days, months): see <c>QualitySettingKeys</c>.</summary>
    public class QualitySetting
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public decimal Value { get; set; }
        public string Title { get; set; }
        public string Source { get; set; }
    }
}
