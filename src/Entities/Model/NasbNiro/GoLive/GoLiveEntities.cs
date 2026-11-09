using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.GoLive
{
    // Package G (go-live cut-over and remaining integrity gaps). The company is already testing on an installed server with real-looking data: the new gates of Package B (configuration audit, characteristic lists,
    // first article) must not stop the work that exists, and must stay strict for everything new. The cut-over is a dated, two-person setting; what is legacy is shown with a Persian note and reported by the AUDIT seat;
    // legacy items are brought under control one by one through two adoption workflows (characteristic list of a released drawing, configuration of an in-flight project).
    // No record is deleted physically; an adoption is voided with a reason.

    /// <summary>The kinds of use of a legacy exemption that the register keeps (each one is an observation of the AUDIT seat).</summary>
    public enum GlLegacyKind
    {
        /// <summary>A release note of a project work order without a product-bank baseline that was created before the cut-over: PCA_MATCH was "not applicable, legacy".</summary>
        PcaNotApplicable = 1,

        /// <summary>A QC acceptance of a part whose drawing revision was released before the cut-over and has no characteristic list (no typed nominal or tolerance, evidence in the note).</summary>
        QcAcceptWithoutList = 2,

        /// <summary>A production order of a part whose drawing revisions were released before the cut-over: the first article was not opened automatically.</summary>
        FaiNotOpened = 3,
    }

    public enum GlAdoptionStatus
    {
        /// <summary>Prepared by engineering, waiting for the checker(s).</summary>
        Requested = 0,

        /// <summary>Configuration adoption only: one of the two reviewers (engineering or PMO) has signed, the other has not.</summary>
        PartlyReviewed = 1,

        Accepted = 2,
        Void = 3,
    }

    /// <summary>The day (instant) the quality / configuration control started, recorded once, in the first start of the program after the update. One live row.</summary>
    public class GlActivation : BaseEntity
    {
        public DateTimeOffset StartAt { get; set; }
        public int? RecordedByUserId { get; set; }
        public string Note { get; set; }
    }

    /// <summary>One use of a legacy exemption (not silent): who, when, which object and the Persian note. Written once per object (unique object key).</summary>
    public class GlLegacyUse : BaseEntity
    {
        public GlLegacyKind Kind { get; set; }

        /// <summary>For example "ReleaseNote:12", "QcRecord:55", "MfProductionOrder:9": one row per object and kind.</summary>
        public string ObjectKey { get; set; }

        public int? WorkOrderId { get; set; }
        public int? ItemId { get; set; }
        public int? RevisionId { get; set; }
        public string Number { get; set; }
        public string Note { get; set; }
        public int? UsedByUserId { get; set; }
        public DateTimeOffset UsedAt { get; set; }
    }

    /// <summary>«پذیرش مشخصه‌های نقشه موجود»: a characteristic list for a drawing revision that was released before the cut-over, without an ECO. Engineering prepares, QA (another person) approves.</summary>
    public class GlCharAdoption : BaseEntity
    {
        public int DeliverableId { get; set; }
        public int RevisionId { get; set; }

        /// <summary>The characteristic list made by this adoption.</summary>
        public int? ListId { get; set; }

        public GlAdoptionStatus Status { get; set; }
        public string Reason { get; set; }
        public string EvidenceRef { get; set; }
        public int RequestedByUserId { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public int? AcceptedByUserId { get; set; }
        public DateTimeOffset? AcceptedAt { get; set; }
        public string AcceptNote { get; set; }
        public string VoidReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>
    /// «پذیرش پیکربندی پروژه موجود»: the product-bank baseline of a legacy in-flight project, built from a released product-bank BOM that stands for its legacy BOM. Engineering prepares (with the comparison against the legacy BOM),
    /// then one more engineering manager AND one PMO person review (three distinct persons); the baseline is created by the last review.
    /// </summary>
    public class GlConfigAdoption : BaseEntity
    {
        public int WorkOrderId { get; set; }
        public int ProductId { get; set; }
        public int BomHeaderId { get; set; }

        /// <summary>The baseline created by the adoption (null until both reviewers have signed).</summary>
        public int? BaselineId { get; set; }

        public GlAdoptionStatus Status { get; set; }
        public string Reason { get; set; }
        public string EvidenceRef { get; set; }

        /// <summary>How the legacy BOM was compared with the product-bank lines (the preparer's statement).</summary>
        public string ComparisonNote { get; set; }
        public int LegacyLineCount { get; set; }
        public int BankLineCount { get; set; }

        public int RequestedByUserId { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public int? EngReviewerUserId { get; set; }
        public DateTimeOffset? EngReviewedAt { get; set; }
        public string EngNote { get; set; }
        public int? PmoReviewerUserId { get; set; }
        public DateTimeOffset? PmoReviewedAt { get; set; }
        public string PmoNote { get; set; }
        public string VoidReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
