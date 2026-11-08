using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.EngChange
{
    // Package A2 (engineering change, effectivity and a single BOM master): the engineering change request (ECR), the engineering change order (ECO) with its computed impact snapshot,
    // the dispositions of every impacted item, the effectivity and the implementation tasks. Records are never deleted physically: a request or an order is cancelled / rejected with a reason.

    /// <summary>Class I changes form, fit, function, safety or anything the customer sees; Class II is every other change.</summary>
    public enum EcClass { ClassI = 1, ClassII = 2 }

    public enum EcUrgency { Normal = 1, Urgent = 2, Critical = 3 }

    public enum EcrStatus { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3, Cancelled = 4 }

    /// <summary>Draft and InApproval are editable by the maker only; Approved is the only state the gates accept; Closed is after every task was verified by QA.</summary>
    public enum EcoStatus { Draft = 0, InApproval = 1, Approved = 2, Rejected = 3, Closed = 4, Cancelled = 5 }

    /// <summary>What an item of the request is: a product, a part number, a BOM revision, an OPC revision, a drawing (EDMS document) or a MES routing revision.</summary>
    public enum EcTargetKind { Product = 1, Part = 2, Bom = 3, Opc = 4, Drawing = 5, Routing = 6 }

    /// <summary>The kind of revision a change of the order names.</summary>
    public enum EcChangeKind { Bom = 1, Opc = 2, Drawing = 3, Routing = 4, CharList = 5 }

    public enum EcChangeAction { NewRevision = 1, Obsolete = 2 }

    /// <summary>The lists of the computed impact analysis.</summary>
    public enum EcImpactCategory { WhereUsed = 1, Baseline = 2, Order = 3, Stock = 4, Procurement = 5, QcPoint = 6, SpecialProcess = 7, Delivered = 8 }

    public enum EcDisposition { UseAsIs = 1, Rework = 2, Scrap = 3, ReturnToSupplier = 4, CompleteOnOldRevision = 5, ChangeToNewRevision = 6, RetrofitDeliveredUnit = 7 }

    public enum EcTaskStatus { Open = 0, Done = 1, Verified = 2 }

    /// <summary>The engineering change request: why, what (products, parts, BOM / OPC / drawing revisions), class and urgency. Raised by engineering, QA, production or PMO; approved by an engineering manager who is not the originator.</summary>
    public class EcRequest : BaseEntity
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public string Reason { get; set; }
        public string Description { get; set; }
        public EcClass Classification { get; set; } = EcClass.ClassI;
        public EcUrgency Urgency { get; set; } = EcUrgency.Normal;
        public EcrStatus Status { get; set; }

        public int RequestedByUserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }

        public ICollection<EcRequestTarget> Targets { get; set; } = new List<EcRequestTarget>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>One affected product, part, BOM revision, OPC revision or drawing of a request.</summary>
    public class EcRequestTarget
    {
        public int Id { get; set; }
        public int RequestId { get; set; }
        public EcRequest Request { get; set; }

        public EcTargetKind Kind { get; set; }

        /// <summary>The part number (the assembly of a BOM, the part of an OPC chart, the part a Part target names).</summary>
        public string PartNumber { get; set; }

        public int? ProductId { get; set; }
        public int? BomHeaderId { get; set; }
        public int? OpcRoutingId { get; set; }
        public int? DeliverableId { get; set; }
        public int? RoutingRevisionId { get; set; }

        /// <summary>Text kept so that a printed request stays readable (code and revision).</summary>
        public string Reference { get; set; }

        public string Note { get; set; }
    }

    /// <summary>
    /// The engineering change order. Created from an approved request. It holds the change content (new revisions), the impact snapshot, the effectivity and the disposition of every impacted item.
    /// Approved by ENGINEERING (the engineering manager) and QA, two distinct persons who are neither the originator nor an editor, never an AI seat. PMO, the CEO and the affected project managers are told, they do not approve.
    /// </summary>
    public class EcOrder : BaseEntity
    {
        public string Number { get; set; }
        public int RequestId { get; set; }
        public EcRequest Request { get; set; }

        public string Title { get; set; }
        public string Summary { get; set; }
        public EcoStatus Status { get; set; }

        // ---- effectivity: from this production order number, this serial number or this date (any of them that is stated; the earliest point that applies wins)
        public string EffectiveFromOrderNumber { get; set; }
        public string EffectiveFromSerial { get; set; }
        public DateTime? EffectiveFromDate { get; set; }
        public string EffectivityNote { get; set; }

        /// <summary>The part numbers the order changes (comma between, comma at both ends: ",16.11.00.000.2,16.11.01.001.2,"); the effectivity gates look the part up here.</summary>
        public string AffectedParts { get; set; }

        // ---- the computed impact snapshot (never typed)
        public string ImpactJson { get; set; }
        public string ImpactSha256 { get; set; }
        public DateTimeOffset? ImpactAt { get; set; }
        public int? ImpactByUserId { get; set; }

        /// <summary>SHA-256 of the content (changes, effectivity, impact hash, dispositions) at the submission; the approvals are given to exactly this content.</summary>
        public string ContentSha256 { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public int? EngineeringApprovedByUserId { get; set; }
        public DateTimeOffset? EngineeringApprovedAt { get; set; }
        public string EngineeringNote { get; set; }
        public int? QaApprovedByUserId { get; set; }
        public DateTimeOffset? QaApprovedAt { get; set; }
        public string QaNote { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }

        public int? RejectedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }
        public string CancelReason { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }

        /// <summary>Everybody who saved the order or its dispositions, ids between commas: none of them approves it (maker-checker).</summary>
        public string EditorUserIds { get; set; }

        public ICollection<EcOrderChange> Changes { get; set; } = new List<EcOrderChange>();
        public ICollection<EcDispositionItem> Dispositions { get; set; } = new List<EcDispositionItem>();
        public ICollection<EcTask> Tasks { get; set; } = new List<EcTask>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A revision the order names: the new BOM / OPC / drawing / routing revision (it may be released only when this order is Approved), or a BOM that the order makes obsolete.</summary>
    public class EcOrderChange
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public EcOrder Order { get; set; }

        public EcChangeKind Kind { get; set; }
        public EcChangeAction Action { get; set; } = EcChangeAction.NewRevision;

        /// <summary>BOM header id, OPC routing id, document revision id or MES routing revision id of the NEW revision (the revision that goes Obsolete for that action).</summary>
        public int TargetId { get; set; }

        /// <summary>The previous revision (the one in force now), when there is one.</summary>
        public int? OldTargetId { get; set; }

        /// <summary>The BOM code, the part number of the OPC chart or the document number.</summary>
        public string Code { get; set; }

        public string PartNumber { get; set; }
        public int? OldRevision { get; set; }
        public int? NewRevision { get; set; }
        public string Reference { get; set; }
        public string Note { get; set; }
    }

    /// <summary>The disposition of one impacted item of the snapshot. Every row must carry a disposition before the order can be approved.</summary>
    public class EcDispositionItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public EcOrder Order { get; set; }

        public EcImpactCategory Category { get; set; }

        /// <summary>The stable key of the impacted item in the snapshot, for example ORDER:15.</summary>
        public string ImpactKey { get; set; }

        public string Text { get; set; }
        public string PartNumber { get; set; }
        public decimal? Quantity { get; set; }

        public EcDisposition? Disposition { get; set; }
        public string Note { get; set; }
        public int? OwnerUserId { get; set; }
        public DateTime? DueDate { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }

        /// <summary>The item no longer appears in a fresh impact analysis (kept for the history, never counted).</summary>
        public bool Stale { get; set; }
    }

    /// <summary>An implementation task of a disposition (owner + due date). The order closes only when every task is done and verified by QA.</summary>
    public class EcTask : BaseEntity
    {
        public int OrderId { get; set; }
        public EcOrder Order { get; set; }
        public int? DispositionItemId { get; set; }
        public string Title { get; set; }
        public EcDisposition Disposition { get; set; }
        public int OwnerUserId { get; set; }
        public DateTime DueDate { get; set; }
        public EcTaskStatus Status { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public int? DoneByUserId { get; set; }
        public string DoneNote { get; set; }
        public DateTimeOffset? VerifiedAt { get; set; }
        public int? VerifiedByUserId { get; set; }
        public string VerifyNote { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
