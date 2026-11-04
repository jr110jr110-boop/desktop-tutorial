using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.ProjectControl
{
    // Package P-B7b: engineering document control (EDMS). Revisions of the MDR / MDL deliverables, check-out, links to parts and tags, distribution, the discipline approver matrix, the tender
    // register. Numbers are stored; never renumber.

    /// <summary>ISO 19650 style status of one revision. Released revisions never change; a newer release moves the older one to Superseded.</summary>
    public enum PcDocStatus { Draft = 1, UnderReview = 2, Approved = 3, Released = 4, Superseded = 5, Obsolete = 6, AsBuilt = 7 }

    /// <summary>What a document is linked to. BomLine keeps the part number of the product-bank line; PartNumber is the BOM part number (optionally with the two-letter OPC stage code).</summary>
    public enum PcDocLinkKind { Item = 1, BomLine = 2, PartNumber = 3, WbsNode = 4, Activity = 5, EquipmentTag = 6 }

    public enum PcDocEventKind
    {
        RevisionCreated = 1, FileAdded = 2, Submitted = 3, Reviewed = 4, Approved = 5, Rejected = 6, Released = 7, Superseded = 8, Obsoleted = 9, AsBuilt = 10,
        CheckedOut = 11, CheckedIn = 12, CheckoutCancelled = 13, Viewed = 14, Printed = 15, Linked = 16, Unlinked = 17, Acknowledged = 18, Received = 19,
    }

    public enum PcQualityLinkKind { Ncr = 1, QcRecord = 2 }

    public enum PcTenderStatus { Open = 1, Won = 2, Lost = 3, Cancelled = 4 }

    public enum PcRevisionFileRole { Native = 1, Pdf = 2, Attachment = 3 }

    /// <summary>One revision of a deliverable (MDR / MDL line). Immutable once Released.</summary>
    public class PcDocumentRevision : BaseEntity
    {
        public int DeliverableId { get; set; }
        public PcEngineeringDeliverable Deliverable { get; set; }

        /// <summary>1, 2, 3 ... in the order of creation.</summary>
        public int Sequence { get; set; }

        /// <summary>00, 01, 02 ... or A, B, C ... by the setting EdmsRevisionScheme.</summary>
        public string Code { get; set; }

        public PcDocStatus Status { get; set; }

        /// <summary>Why this revision exists (the first revision: the issue purpose).</summary>
        public string Reason { get; set; }

        public int PreparedByUserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ReviewerUserId { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string ReviewNote { get; set; }
        public int? ApproverUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ReleasedByUserId { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }

        public int? RejectedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }

        public int? SupersededByRevisionId { get; set; }
        public DateTimeOffset? SupersededAt { get; set; }
        public string ObsoleteReason { get; set; }
        public int? AsBuiltByUserId { get; set; }
        public DateTimeOffset? AsBuiltAt { get; set; }

        /// <summary>The revision changes time, cost or material: a change request is then required before the revision leaves Draft.</summary>
        public bool ChangeImpact { get; set; }
        public int? ChangeRequestId { get; set; }

        /// <summary>A document received from the client: it is recorded as it came (no internal review) and stays read only.</summary>
        public bool ReceivedFromClient { get; set; }

        public ICollection<PcDocumentRevisionFile> Files { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A file of a revision (native drawing, PDF, attachment). Files are added to a Draft revision only; a removed file is hidden with a reason, never deleted.</summary>
    public class PcDocumentRevisionFile : BaseEntity
    {
        public int RevisionId { get; set; }
        public PcDocumentRevision Revision { get; set; }
        public Guid DocumentId { get; set; }
        public PcRevisionFileRole Role { get; set; }
        public string FileName { get; set; }
        public string Title { get; set; }
        public string RemoveReason { get; set; }
    }

    /// <summary>A revision linked to a NCR or a QC record (both ways: the NCR side lists its revisions).</summary>
    public class PcDocumentQualityLink : BaseEntity
    {
        public int RevisionId { get; set; }
        public PcDocumentRevision Revision { get; set; }
        public PcQualityLinkKind Kind { get; set; }
        public int TargetId { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Document to part / item / WBS / activity / tag link. Exactly one of DeliverableId and ControlledDocumentId is set. An unlink hides the row with a reason.</summary>
    public class PcDocumentLink : BaseEntity
    {
        public int? DeliverableId { get; set; }
        public PcEngineeringDeliverable Deliverable { get; set; }
        public int? ControlledDocumentId { get; set; }

        public PcDocLinkKind Kind { get; set; }
        public int? ItemId { get; set; }
        public int? BomLineId { get; set; }

        /// <summary>The BOM part number (also filled for a BomLine link), with the optional two-letter OPC stage code.</summary>
        public string PartNumber { get; set; }
        public string StageCode2 { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public int? TagId { get; set; }

        /// <summary>The link applies to this operation template only (null: the whole part).</summary>
        public int? OperationTemplateId { get; set; }

        public string Note { get; set; }
        public string UnlinkReason { get; set; }
        public int? UnlinkedByUserId { get; set; }
    }

    /// <summary>The light equipment tag register of a project (a valve, a rotor ...).</summary>
    public class PcEquipmentTag : BaseEntity
    {
        public int ContractId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ItemId { get; set; }
        public int? WorkOrderId { get; set; }
        public string Note { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>Who gets a notice when a document of the discipline is released (null contract: every project).</summary>
    public class PcDocumentDistribution : BaseEntity
    {
        public string Discipline { get; set; }
        public int UserId { get; set; }
        public int? ContractId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>Who may review / approve / release the documents of a discipline without a DCC person (null contract: every project).</summary>
    public class PcDisciplineApprover : BaseEntity
    {
        public string Discipline { get; set; }
        public int UserId { get; set; }
        public int? ContractId { get; set; }
        public bool CanReview { get; set; } = true;
        public bool CanApprove { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>The notice of one release to one person; the person acknowledges it.</summary>
    public class PcDocumentNotice
    {
        public int Id { get; set; }
        public int RevisionId { get; set; }
        public PcDocumentRevision Revision { get; set; }
        public int UserId { get; set; }
        public DateTimeOffset NotifiedAt { get; set; }
        public DateTimeOffset? AcknowledgedAt { get; set; }
    }

    /// <summary>Append-only history of a document: who did what and when (check-out, view, print, every status step).</summary>
    public class PcDocumentEvent
    {
        public int Id { get; set; }
        public int DeliverableId { get; set; }
        public int? RevisionId { get; set; }
        public PcDocEventKind Kind { get; set; }
        public int UserId { get; set; }
        public DateTimeOffset At { get; set; }
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------ tender (before P.O.)

    /// <summary>A tender package: the technical proposal documents before the contract. Linked to the project when the tender is won.</summary>
    public class PcTenderPackage : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>The legacy tender record, when there is one.</summary>
        public int? TenderId { get; set; }
        public string TenderCode { get; set; }
        public PcTenderStatus Status { get; set; } = PcTenderStatus.Open;
        public int? ContractId { get; set; }
        public DateTimeOffset? LinkedAt { get; set; }
        public int? LinkedByUserId { get; set; }
        public string Note { get; set; }
        public ICollection<PcTenderDocument> Documents { get; set; }
    }

    public class PcTenderDocument : BaseEntity
    {
        public int PackageId { get; set; }
        public PcTenderPackage Package { get; set; }

        /// <summary>From the own series of the tender documents (TD-year-00001).</summary>
        public string Number { get; set; }
        public string Title { get; set; }
        public string Discipline { get; set; }
        public string DocumentType { get; set; }
        public string Revision { get; set; }
        public Guid? DocumentId { get; set; }
        public DateTimeOffset? IssuedAt { get; set; }
        public int? IssuedByUserId { get; set; }
        public string VoidReason { get; set; }
    }
}
