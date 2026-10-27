using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.QualityDtos
{
    public class ItpParameterDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string Source { get; set; }
        public bool RequiresLimits { get; set; }
    }

    public class ItpTemplateListDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public ItpProductType ProductType { get; set; }
        public string ApplicableCodes { get; set; }
        public string Revision { get; set; }
        public bool IsActive { get; set; }
        public int ActivityCount { get; set; }
    }

    public class ItpTemplateActivityDto
    {
        public int Id { get; set; }
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
        public bool GatesRelease { get; set; } = true;
        public List<ItpParameterDto> Parameters { get; set; } = new List<ItpParameterDto>();
    }

    public class ItpTemplateDto : ItpTemplateListDto
    {
        public List<ItpTemplateActivityDto> Activities { get; set; } = new List<ItpTemplateActivityDto>();
    }

    public class ItpTemplateQuery : PaginationParams
    {
        public ItpProductType? ProductType { get; set; }
        public string Contains { get; set; }
    }

    public class ItpReleaseDto
    {
        public int Id { get; set; }
        public ReleaseParty Party { get; set; }
        public ReleaseAction Action { get; set; }
        public string PersonName { get; set; }
        public string Organisation { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string Reference { get; set; }
        public bool WaivedByNotice { get; set; }
    }

    public class ItpActivityDto
    {
        public int Id { get; set; }
        public int ItpId { get; set; }
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
        public bool GatesRelease { get; set; } = true;
        public ItpActivityStatus Status { get; set; }
        public DateTime? PlannedDate { get; set; }
        public DateTime? NotificationSentAt { get; set; }

        /// <summary>Notification date + notice days: after it the witness is waived by notice.</summary>
        public DateTime? WitnessDeadline { get; set; }

        public DateTimeOffset? DoneAt { get; set; }
        public string StatusNote { get; set; }

        /// <summary>The outgoing letter, sent to the client of the contract, that carried the witness notice (its sent date is the notification date).</summary>
        public int? NotificationLetterId { get; set; }

        public string NotificationLetterNumber { get; set; }

        /// <summary>The client's letter that agreed a hold or witness point of the client or the third party is not applicable.</summary>
        public string NotApplicableReference { get; set; }

        /// <summary>The registered incoming letter of the client behind the not-applicable decision, and its number.</summary>
        public int? NotApplicableLetterId { get; set; }

        public string NotApplicableLetterNumber { get; set; }

        /// <summary>The client's letter is recorded but the activity stays open until the client approves the revised plan (client-approve turns it into NotApplicable).</summary>
        public bool NotApplicablePending { get; set; }

        /// <summary>Proof an activity without a required report was done.</summary>
        public Guid? EvidenceDocumentId { get; set; }

        public string EvidenceNote { get; set; }

        /// <summary>The activity has a hold point and is not finished.</summary>
        public bool HoldPending { get; set; }

        /// <summary>What still stops the activity from being done (computed, read only).</summary>
        public List<string> Blockers { get; set; } = new List<string>();

        public List<ItpParameterDto> Parameters { get; set; } = new List<ItpParameterDto>();
        public List<ItpReleaseDto> Releases { get; set; } = new List<ItpReleaseDto>();
    }

    public class ItpRevisionDto
    {
        public int Id { get; set; }
        public int Revision { get; set; }
        public string Reason { get; set; }
        public string ClientApprovalReference { get; set; }
        public DateTime? ClientApprovalDate { get; set; }
        public DateTimeOffset ChangedAt { get; set; }
        public string ChangedByName { get; set; }
    }

    public class ItpListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public string ContractCode { get; set; }
        public int Revision { get; set; }
        public ItpStatus Status { get; set; }
        public bool AutoDrafted { get; set; }
        public int ActivityCount { get; set; }
        public int DoneCount { get; set; }
    }

    public class ItpDto : ItpListDto
    {
        public int ContractId { get; set; }
        public int? TemplateId { get; set; }
        public string TemplateCode { get; set; }
        public string ClientApprovalReference { get; set; }
        public DateTime? ClientApprovalDate { get; set; }
        public int NoticeDays { get; set; }
        public string Note { get; set; }
        public List<ItpActivityDto> Activities { get; set; } = new List<ItpActivityDto>();
        public List<ItpRevisionDto> Revisions { get; set; } = new List<ItpRevisionDto>();
    }

    public class ItpQuery : PaginationParams
    {
        public ItpStatus? Status { get; set; }
        public int? WorkOrderId { get; set; }
        public string Contains { get; set; }
    }

    public class EnsureItpDto
    {
        public int WorkOrderId { get; set; }

        /// <summary>Template to copy; empty = the active template of the product type.</summary>
        public int? TemplateId { get; set; }

        /// <summary>Overrides the product type taken from the equipment of the work order.</summary>
        public ItpProductType? ProductType { get; set; }

        /// <summary>The client or TPI is outside Iran: the longer notice period applies.</summary>
        public bool Abroad { get; set; }
    }

    public class SaveItpDto
    {
        public int Id { get; set; }
        public int NoticeDays { get; set; }
        public string Note { get; set; }
    }

    public class ItpActionDto
    {
        public int Id { get; set; }

        /// <summary>
        /// submit, client-approve, use, close, revise. client-approve is recorded by somebody who did not prepare, edit, revise or submit the plan; its date is not in the future and
        /// within the back-date window; it also turns the not-applicable requests of the revision (pending) into NotApplicable.
        /// </summary>
        public string Action { get; set; }
        public string Reference { get; set; }
        public DateTime? Date { get; set; }
        public string Reason { get; set; }
    }

    public class SaveItpParameterDto
    {
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string Source { get; set; }
        public bool RequiresLimits { get; set; }
    }

    public class SaveItpActivityDto
    {
        public int? Id { get; set; }
        public int ItpId { get; set; }
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
        public bool GatesRelease { get; set; } = true;
        public DateTime? PlannedDate { get; set; }

        /// <summary>Why the approved plan changes (needed when the ITP is already approved by the client: starts a new revision).</summary>
        public string Reason { get; set; }

        public List<SaveItpParameterDto> Parameters { get; set; } = new List<SaveItpParameterDto>();
    }

    public class ItpActivityActionDto
    {
        public int Id { get; set; }

        /// <summary>notify, plan, done, not-applicable.</summary>
        public string Action { get; set; }

        /// <summary>plan: the planned date. notify: only today or empty; the notification date is the server day or the day <see cref="LetterId"/> was sent.</summary>
        public DateTime? Date { get; set; }
        public string Note { get; set; }

        /// <summary>
        /// notify: an outgoing letter of the Office, connected to the contract of the plan, addressed to the client and marked SENT; the notice counts from its sent date.
        /// not-applicable on a client or third-party hold/witness point: the registered incoming letter of the client (connected to the same contract) that agreed.
        /// </summary>
        public int? LetterId { get; set; }

        /// <summary>not-applicable: optional free reference of the client's letter (at most 300 characters); the number of the incoming letter is used when empty.</summary>
        public string Reference { get; set; }

        /// <summary>
        /// done, for an activity without a required report: the document that proves the work was done (when no accepted inspection record is linked). The file must have been
        /// uploaded within the back-date window by a QC, QA or engineering user and may prove one activity only.
        /// </summary>
        public Guid? EvidenceDocumentId { get; set; }
    }

    public class SaveItpReleaseDto
    {
        public int ActivityId { get; set; }
        public ReleaseParty Party { get; set; }
        public ReleaseAction Action { get; set; }
        public string PersonName { get; set; }
        public string Organisation { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string Reference { get; set; }
    }

    public class SetItpLimitsDto
    {
        public int ParameterId { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string Source { get; set; }
    }

    /// <summary>A hold or witness point that waits (read only list for the dashboards).</summary>
    public class PendingPointDto
    {
        public int ItpId { get; set; }
        public string ItpNumber { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int ActivityId { get; set; }
        public string ItemNo { get; set; }
        public string Activity { get; set; }
        public string Points { get; set; }
        public ItpActivityStatus Status { get; set; }
        public DateTime? PlannedDate { get; set; }
        public DateTime? WitnessDeadline { get; set; }
    }

    public class PendingPointQuery : PaginationParams
    {
        public int? WorkOrderId { get; set; }
    }
}
