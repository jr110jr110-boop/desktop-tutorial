using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Quality
{
    // Package B (inspection and release integrity): the characteristic list of a drawing revision, the "no drawing" flag of a part, the first article inspection and the as-built record.
    // Records are never deleted physically: a list is superseded or voided, a flag is voided, a first article is rejected or voided, an as-built record is superseded by a new version.

    /// <summary>Life of a characteristic list. Draft and Submitted are editable by the maker only; Approved waits for the release of the drawing revision; Released and later never change.</summary>
    public enum CharListStatus { Draft = 0, Submitted = 1, Approved = 2, Released = 3, Superseded = 4, Void = 5 }

    /// <summary>Critical / major / minor class of a characteristic (the class decides the default sampling and whether the characteristic must be measured).</summary>
    public enum CharClass { Critical = 1, Major = 2, Minor = 3 }

    public enum NoDrawingStatus { Pending = 0, Active = 1, Rejected = 2, Voided = 3 }

    public enum FaiStatus { Open = 0, Measured = 1, QcAccepted = 2, Approved = 3, Rejected = 4, Voided = 5 }

    public enum AsBuiltStatus { Current = 0, Superseded = 1 }

    /// <summary>
    /// The list of inspection characteristics of ONE drawing revision (EDMS revision). Prepared by engineering, checked and approved by a second person (maker-checker), released together with the
    /// drawing revision and immutable after that. A correction is a new version of the list (supersede) with a reason.
    /// </summary>
    public class QcCharacteristicList : BaseEntity
    {
        public int DeliverableId { get; set; }

        /// <summary>The revision of the document (PcDocumentRevision) the list belongs to.</summary>
        public int RevisionId { get; set; }

        /// <summary>The document number and the revision code at the time of the list (kept so that a printed record stays readable).</summary>
        public string DocumentNumber { get; set; }
        public string RevisionCode { get; set; }

        /// <summary>1, 2, 3 ... per revision.</summary>
        public int Version { get; set; } = 1;

        public CharListStatus Status { get; set; }

        /// <summary>Why this version exists (required from version 2).</summary>
        public string Reason { get; set; }

        public int? SupersedesListId { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }

        /// <summary>SHA-256 of the characteristics at the approval; a released list is verified against it.</summary>
        public string ContentHash { get; set; }

        public string ReturnNote { get; set; }
        public string VoidReason { get; set; }

        /// <summary>Everybody who saved the list, ids between commas: none of them approves it.</summary>
        public string EditorUserIds { get; set; }

        public ICollection<QcCharacteristic> Items { get; set; } = new List<QcCharacteristic>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>One characteristic of the drawing: nominal with tolerances, class, the instrument class and method, the sampling and the operation where it is inspected.</summary>
    public class QcCharacteristic
    {
        public int Id { get; set; }

        public int ListId { get; set; }
        public QcCharacteristicList List { get; set; }

        public int RowNumber { get; set; }

        /// <summary>The number on the drawing (balloon): 1, 2A, 12 ...</summary>
        public string No { get; set; }
        public string Name { get; set; }

        public decimal Nominal { get; set; }

        /// <summary>Upper deviation allowed (positive number).</summary>
        public decimal TolPlus { get; set; }

        /// <summary>Lower deviation allowed (positive number).</summary>
        public decimal TolMinus { get; set; }

        public string Unit { get; set; }
        public CharClass Class { get; set; }

        /// <summary>The class of instrument that may measure it (Other = any class).</summary>
        public EquipmentType InstrumentClass { get; set; }

        public string Method { get; set; }

        /// <summary>Percentage of the pieces that are measured (100 = every piece; critical is always at least the company setting). 0 = not required (minor only).</summary>
        public int SamplePercent { get; set; }

        /// <summary>The step (sequence number of the OPC routing) where it is inspected; empty = final / receiving inspection.</summary>
        public int? OperationSeq { get; set; }

        /// <summary>The two-letter stage code of that step, when the routing has one.</summary>
        public string StageCode2 { get; set; }

        public decimal Lower => Nominal - TolMinus;
        public decimal Upper => Nominal + TolPlus;
    }

    /// <summary>
    /// Engineering flags a make part as «بدون نقشه» (no drawing exists or is needed): the MES release gate then passes without a released drawing. A request with a reason, activated by a second person; audited; voided, never deleted.
    /// </summary>
    public class NoDrawingFlag : BaseEntity
    {
        public int ItemId { get; set; }
        public string PartNumber { get; set; }
        public string Reason { get; set; }
        public NoDrawingStatus Status { get; set; }

        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }

        public int? VoidedByUserId { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public string VoidReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>
    /// First article inspection (AS9102 style). Required for the first production order of a part after a new released drawing / BOM / routing revision (and for a routing step that is flagged): all characteristics of the released list
    /// are measured (one quality record of type FirstArticle), accepted by QC and approved by QA, three different persons.
    /// </summary>
    public class QcFirstArticle : BaseEntity
    {
        public string Number { get; set; }

        public int ItemId { get; set; }
        public string PartNumber { get; set; }

        public int? WorkOrderId { get; set; }
        public int? ProductionOrderId { get; set; }

        /// <summary>The configuration this FAI covers: drawing revisions, routing revision, BOM revision (see FaiRules.Fingerprint). A new fingerprint needs a new FAI.</summary>
        public string Fingerprint { get; set; }

        /// <summary>The same in words (drawing numbers with revisions, routing revision, BOM).</summary>
        public string FingerprintText { get; set; }

        public int? CharacteristicListId { get; set; }
        public int? RoutingRevisionId { get; set; }

        /// <summary>Why a FAI is needed: new revision, flagged step.</summary>
        public string Trigger { get; set; }

        public FaiStatus Status { get; set; }

        /// <summary>The measurement record (QcRecord of type FirstArticle).</summary>
        public int? QcRecordId { get; set; }
        public int? MeasuredByUserId { get; set; }
        public DateTimeOffset? MeasuredAt { get; set; }

        public int? QcAcceptedByUserId { get; set; }
        public DateTimeOffset? QcAcceptedAt { get; set; }
        public string QcAcceptNote { get; set; }

        public int? QaApprovedByUserId { get; set; }
        public DateTimeOffset? QaApprovedAt { get; set; }
        public string QaApproveNote { get; set; }

        public int? RejectedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }

        public string VoidReason { get; set; }
        public string Note { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>
    /// The as-built record of one released unit (a serial number, or a quantity line of a lot): a snapshot (JSON + SHA-256) of the product, BOM and routing revisions, drawing revisions, installed serials / lots / heat numbers,
    /// deviations and concessions, NCRs and FAI references at the moment of release. Immutable; a correction is a new version with a reason that supersedes the old one.
    /// </summary>
    public class AsBuiltRecord : BaseEntity
    {
        public int WorkOrderId { get; set; }
        public int ReleaseNoteId { get; set; }

        public int ItemId { get; set; }
        public int? SerialUnitId { get; set; }
        public string SerialNumber { get; set; }

        public int Version { get; set; } = 1;
        public AsBuiltStatus Status { get; set; }
        public string Reason { get; set; }

        /// <summary>Product code and the BOM revision, for the lists.</summary>
        public string ProductCode { get; set; }
        public string BomText { get; set; }

        /// <summary>The snapshot (JSON). Never changed.</summary>
        public string SnapshotJson { get; set; }

        /// <summary>SHA-256 (hex) of <see cref="SnapshotJson"/>.</summary>
        public string Sha256 { get; set; }

        public int? SupersededByVersion { get; set; }
    }
}
