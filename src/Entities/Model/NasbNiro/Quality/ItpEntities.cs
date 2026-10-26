using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace Entities.Model.NasbNiro.Quality
{
    // Inspection and test plans (WI-QC-004, spec gap G-08): a template per product type, copied to an ITP per work order.

    public class ItpTemplate : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public ItpProductType ProductType { get; set; }

        /// <summary>Codes and standards the template is written for (API 611, ASME VIII...).</summary>
        public string ApplicableCodes { get; set; }

        public string Revision { get; set; }
        public bool IsActive { get; set; } = true;

        public ICollection<ItpTemplateActivity> Activities { get; set; }
    }

    public class ItpTemplateActivity
    {
        public int Id { get; set; }

        public int TemplateId { get; set; }
        public ItpTemplate Template { get; set; }

        public int Seq { get; set; }

        /// <summary>Number in the source plan, for example "3-1".</summary>
        public string ItemNo { get; set; }

        /// <summary>Group or stage, for example "NDT".</summary>
        public string Stage { get; set; }

        public string Activity { get; set; }
        public QaTestType TestType { get; set; }
        public string AcceptanceReference { get; set; }
        public ItpPoint PointVendor { get; set; }
        public ItpPoint PointTpi { get; set; }
        public ItpPoint PointClient { get; set; }
        public string DocumentProduced { get; set; }
        public TestReportType? RequiredReportType { get; set; }
        public string Remark { get; set; }

        /// <summary>False for the activities that follow the release itself (final book, release note, certificate of conformity).</summary>
        public bool GatesRelease { get; set; } = true;

        public ICollection<ItpTemplateParameter> Parameters { get; set; }
    }

    public class ItpTemplateParameter
    {
        public int Id { get; set; }

        public int ActivityId { get; set; }
        public ItpTemplateActivity Activity { get; set; }

        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }

        /// <summary>The clause or document the limit comes from.</summary>
        public string Source { get; set; }

        /// <summary>The limit is not known yet: engineering must set it before a report can be submitted.</summary>
        public bool RequiresLimits { get; set; }
    }

    public class Itp : BaseEntity
    {
        public string Number { get; set; }

        public int WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        public int? TemplateId { get; set; }
        public ItpTemplate Template { get; set; }

        public int Revision { get; set; }
        public ItpStatus Status { get; set; }

        public string ClientApprovalReference { get; set; }
        public DateTime? ClientApprovalDate { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? SubmittedByUserId { get; set; }

        /// <summary>Notice before a witness point: 10 days in Iran, 60 days abroad (WI-QC-004). Editable per ITP.</summary>
        public int NoticeDays { get; set; }

        /// <summary>The system drafted the plan without being asked (a record was entered for the work order).</summary>
        public bool AutoDrafted { get; set; }

        public string Note { get; set; }

        public ICollection<ItpActivity> Activities { get; set; }
        public ICollection<ItpRevision> Revisions { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>History of an ITP: a snapshot of the plan as it was before a revision was started.</summary>
    public class ItpRevision
    {
        public int Id { get; set; }

        public int ItpId { get; set; }
        public Itp Itp { get; set; }

        public int Revision { get; set; }
        public string Reason { get; set; }
        public string ClientApprovalReference { get; set; }
        public DateTime? ClientApprovalDate { get; set; }

        /// <summary>JSON of the activities and their parameters when the revision was closed.</summary>
        public string Snapshot { get; set; }

        public DateTimeOffset ChangedAt { get; set; }
        public int ChangedByUserId { get; set; }
    }

    public class ItpActivity
    {
        public int Id { get; set; }

        public int ItpId { get; set; }
        public Itp Itp { get; set; }

        public int Seq { get; set; }
        public string ItemNo { get; set; }
        public string Stage { get; set; }
        public string Activity { get; set; }
        public QaTestType TestType { get; set; }
        public string AcceptanceReference { get; set; }
        public ItpPoint PointVendor { get; set; }
        public ItpPoint PointTpi { get; set; }
        public ItpPoint PointClient { get; set; }
        public string DocumentProduced { get; set; }
        public TestReportType? RequiredReportType { get; set; }
        public string Remark { get; set; }

        /// <summary>False for the activities that follow the release itself (final book, release note, certificate of conformity).</summary>
        public bool GatesRelease { get; set; } = true;

        public ItpActivityStatus Status { get; set; }
        public DateTime? PlannedDate { get; set; }

        /// <summary>Date the parties were told about the witness point.</summary>
        public DateTime? NotificationSentAt { get; set; }

        public DateTimeOffset? DoneAt { get; set; }
        public int? DoneByUserId { get; set; }
        public string StatusNote { get; set; }

        // ----- R1q
        /// <summary>The client's (or third party's) letter that agrees the hold or witness point is not applicable (R1q D9).</summary>
        public string NotApplicableReference { get; set; }

        /// <summary>The signed outgoing letter (OfLetter) that told the parties about the witness point; its signing date is the notification date (R1q D9).</summary>
        public int? NotificationLetterId { get; set; }

        /// <summary>Proof that an activity without a required report was done: a document or a written note (R1q D18).</summary>
        public Guid? EvidenceDocumentId { get; set; }

        public string EvidenceNote { get; set; }

        public ICollection<ItpActivityParameter> Parameters { get; set; }
        public ICollection<ItpPointRelease> Releases { get; set; }
    }

    public class ItpActivityParameter
    {
        public int Id { get; set; }

        public int ActivityId { get; set; }
        public ItpActivity Activity { get; set; }

        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string Source { get; set; }
        public bool RequiresLimits { get; set; }
    }

    /// <summary>One party's release of a hold or witness point.</summary>
    public class ItpPointRelease
    {
        public int Id { get; set; }

        public int ActivityId { get; set; }
        public ItpActivity Activity { get; set; }

        public ReleaseParty Party { get; set; }
        public ReleaseAction Action { get; set; }
        public string PersonName { get; set; }
        public string Organisation { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string Reference { get; set; }

        /// <summary>The system recorded that the witness did not come within the notice period.</summary>
        public bool WaivedByNotice { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
    }
}
