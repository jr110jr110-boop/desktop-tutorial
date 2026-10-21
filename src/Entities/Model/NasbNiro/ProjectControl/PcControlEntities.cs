using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Quality;

namespace Entities.Model.NasbNiro.ProjectControl
{
    // Project control module, part 3: progress, snapshots, daily reports, change control, MDR/MDL, reports, shipment.

    /// <summary>Percent entered by a unit manager or the PM; it counts only after the planning manager approves it (not the proposer).</summary>
    public class PcManualProgress
    {
        public int Id { get; set; }
        public int ActivityId { get; set; }
        public PcActivity Activity { get; set; }
        public decimal Percent { get; set; }
        public string Comment { get; set; }
        public int ProposedByUserId { get; set; }
        public DateTimeOffset ProposedAt { get; set; }

        /// <summary>0 = proposed, 1 = approved, 2 = rejected.</summary>
        public int State { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
    }

    /// <summary>Weekly snapshot at the data date. Rows are never changed (immutable).</summary>
    public class PcProgressSnapshot
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public DateTime DataDate { get; set; }
        public DateTimeOffset TakenAt { get; set; }

        /// <summary>0 for the server job.</summary>
        public int TakenByUserId { get; set; }
        public int? BaselineId { get; set; }

        public decimal PhysicalPercent { get; set; }
        public decimal PlannedPercent { get; set; }
        public decimal Bac { get; set; }
        public decimal Pv { get; set; }
        public decimal Ev { get; set; }
        public decimal Ac { get; set; }
        public decimal? Cpi { get; set; }
        public decimal? Spi { get; set; }
        public DateTime? ForecastFinish { get; set; }
        public string Hash { get; set; }

        public ICollection<PcProgressSnapshotLine> Lines { get; set; }
    }

    public class PcProgressSnapshotLine
    {
        public int Id { get; set; }
        public int SnapshotId { get; set; }
        public PcProgressSnapshot Snapshot { get; set; }
        public int ActivityId { get; set; }
        public decimal Percent { get; set; }
        public decimal PlannedPercent { get; set; }
        public decimal Budget { get; set; }
        public decimal Ev { get; set; }
        public decimal Pv { get; set; }
        public DateTime? ForecastFinish { get; set; }
    }

    /// <summary>Daily log sheet (R-PR-4): pre-filled from the module events, the unit manager confirms or adds the exceptions.</summary>
    public class PcDailyReport : BaseEntity
    {
        public int ContractId { get; set; }
        public string Unit { get; set; }
        public DateTime ReportDate { get; set; }

        /// <summary>0 = draft, 1 = confirmed.</summary>
        public int State { get; set; }
        public int? ConfirmedByUserId { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public ICollection<PcDailyReportLine> Lines { get; set; }
    }

    public class PcDailyReportLine
    {
        public int Id { get; set; }
        public int ReportId { get; set; }
        public PcDailyReport Report { get; set; }
        public int ActivityId { get; set; }
        public string EventText { get; set; }
        public decimal Percent { get; set; }
        public bool IsLate { get; set; }
        public string DelayReasonCode { get; set; }
        public string DelayReason { get; set; }
        public string PreventiveAction { get; set; }
        public string CorrectiveAction { get; set; }
    }

    // ------------------------------------------------------------------------------ change control (WI-PL-013)

    public class PcChangeRequest : BaseEntity
    {
        public string Number { get; set; }
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public PcChangeOrigin Origin { get; set; }
        public PcChangeType Type { get; set; }
        public int RequesterUserId { get; set; }

        /// <summary>The requester's manager who agreed (required by FO-PO-011).</summary>
        public string ManagerAgreement { get; set; }
        public string Description { get; set; }
        public string Reason { get; set; }
        public PcChangeStatus Status { get; set; }
        public DateTimeOffset? RegisteredAt { get; set; }
        public DateTime? DueDate { get; set; }
        public bool Escalated { get; set; }

        /// <summary>JSON list of the requested schedule changes: activity, duration delta, budget delta.</summary>
        public string ScenarioJson { get; set; }
        public decimal RequestedPriceDelta { get; set; }
        public int RequestedDaysDelta { get; set; }

        // impact analysis (FO-PO-015): computed by the system, reviewed by the board
        public string ImpactJson { get; set; }
        public int? ImpactDeltaDays { get; set; }
        public decimal? ImpactDeltaCost { get; set; }
        public bool? ImpactOnCriticalPath { get; set; }
        public int AffectedPoLines { get; set; }
        public int AffectedItpPoints { get; set; }
        public DateTimeOffset? AnalysedAt { get; set; }
        public string ImpactScope { get; set; }
        public string ImpactResources { get; set; }
        public string ImpactQuality { get; set; }
        public string ImpactRisk { get; set; }
        public string ImpactProcurement { get; set; }
        public string ImpactClientContract { get; set; }
        public string Recommendation { get; set; }
        public int? RecommendedByUserId { get; set; }

        public PcChangeDecision Decision { get; set; }
        public string DecisionNote { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecidedByRole { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public int? BaselineId { get; set; }
        public PcBaseline Baseline { get; set; }

        public ICollection<PcChangeBoardMember> Board { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PcChangeBoardMember
    {
        public int Id { get; set; }
        public int ChangeRequestId { get; set; }
        public PcChangeRequest ChangeRequest { get; set; }
        public string Role { get; set; }
        public int? UserId { get; set; }
        public PcVote Vote { get; set; }
        public DateTimeOffset? VotedAt { get; set; }
        public string Comment { get; set; }
    }

    /// <summary>Who may approve a change, by value and delay (configurable data).</summary>
    public class PcApprovalAuthority
    {
        public int Id { get; set; }
        public string Role { get; set; }

        /// <summary>False: the role can only recommend.</summary>
        public bool CanDecide { get; set; }

        /// <summary>Null = no limit.</summary>
        public decimal? MaxAmount { get; set; }
        public int? MaxDelayDays { get; set; }
        public string Note { get; set; }
    }

    public class PcClaim : BaseEntity
    {
        public string Number { get; set; }
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public PcClaimDirection Direction { get; set; }
        public string Issue { get; set; }
        public string Basis { get; set; }
        public int? DaysQuantified { get; set; }
        public decimal? AmountQuantified { get; set; }
        public PcClaimStatus Status { get; set; }
        public bool IsCrossClaim { get; set; }
        public bool PmoApproved { get; set; }
        public bool DesignManufacturingApproved { get; set; }
        public string ReviewNote { get; set; }
        public int? ReviewedByUserId { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
    }

    // ------------------------------------------------------------------------------ MDR / MDL (company decision)

    /// <summary>One line of the master document register. Lines with ClientSubmittal form the MDL (the client list).</summary>
    public class PcEngineeringDeliverable : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public string DocumentNumber { get; set; }
        public string Title { get; set; }
        public bool ClientSubmittal { get; set; }
        public string StandardSource { get; set; }
        public string Discipline { get; set; }
        public string DocumentType { get; set; }
        public bool Required { get; set; } = true;
        public DateTime? PlannedDate { get; set; }

        public PcIssueState IssueState { get; set; }
        public string Revision { get; set; }
        public int? TemplateLineId { get; set; }

        /// <summary>The file of the latest revision (uploaded through Documents).</summary>
        public Guid? DocumentId { get; set; }
        public int? ControlledDocumentId { get; set; }

        public string LastTransmittalNumber { get; set; }
        public DateTime? LastTransmittalDate { get; set; }
        public string ClientResponseCode { get; set; }
        public DateTime? ClientResponseDate { get; set; }

        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public int? IssuedByUserId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PcClientResponseCode
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>A and B: the document counts as approved by the client.</summary>
        public bool CountsAsApproved { get; set; }
        public bool RequiresResubmit { get; set; }
        public string Source { get; set; }
    }

    public class PcTransmittal : BaseEntity
    {
        public string Number { get; set; }
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public PcTransmittalDirection Direction { get; set; }
        public DateTime TransmittalDate { get; set; }
        public DateTime? ResponseDue { get; set; }
        public string Reference { get; set; }
        public string Note { get; set; }
        public ICollection<PcTransmittalLine> Lines { get; set; }
    }

    public class PcTransmittalLine
    {
        public int Id { get; set; }
        public int TransmittalId { get; set; }
        public PcTransmittal Transmittal { get; set; }
        public int DeliverableId { get; set; }
        public PcEngineeringDeliverable Deliverable { get; set; }
        public string Revision { get; set; }
        public PcTransmittalPurpose Purpose { get; set; }
        public string ResponseCode { get; set; }
        public DateTime? ResponseDate { get; set; }
        public string ResponseNote { get; set; }
    }

    public class PcMdrTemplate : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public ItpProductType ProductType { get; set; }
        public string StandardSource { get; set; }
        public string Note { get; set; }
        public bool IsActive { get; set; } = true;
        public ICollection<PcMdrTemplateLine> Lines { get; set; }
    }

    public class PcMdrTemplateLine
    {
        public int Id { get; set; }
        public int TemplateId { get; set; }
        public PcMdrTemplate Template { get; set; }
        public int Seq { get; set; }
        public string DocumentType { get; set; }
        public string Discipline { get; set; }
        public string Title { get; set; }
        public bool ClientSubmittal { get; set; }
        public bool Required { get; set; } = true;
        public string StandardSource { get; set; }
    }

    // ------------------------------------------------------------------------------ reports and shipment

    /// <summary>A generated report, stored as issued. Content and hash never change; only the release flag does.</summary>
    public class PcReportIssue
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public PcReportKind Kind { get; set; }
        public int ContractId { get; set; }
        public int? WorkOrderId { get; set; }
        public DateTime DataDate { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }
        public int GeneratedByUserId { get; set; }
        public string Hash { get; set; }
        public string Content { get; set; }
        public int? ControlledDocumentId { get; set; }
        public string ControlledRevision { get; set; }

        /// <summary>Client report: the PM releases it before DCC sends it.</summary>
        public DateTimeOffset? ReleasedAt { get; set; }
        public int? ReleasedByUserId { get; set; }
    }

    public class PcShipment : BaseEntity
    {
        public string Number { get; set; }
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public int WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }
        public int? ReleaseNoteId { get; set; }
        public PcShipmentStatus Status { get; set; }
        public string PackingList { get; set; }
        public string PackingVerification { get; set; }
        public Guid? PackingPhotoDocumentId { get; set; }
        public string ShippingDocuments { get; set; }
        public string Waybill { get; set; }
        public DateTime? ShippedAt { get; set; }
        public string DeliveryReceiptNumber { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public Guid? SignedReceiptDocumentId { get; set; }
        public string HandoverMinutesNumber { get; set; }
        public DateTime? HandoverAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
