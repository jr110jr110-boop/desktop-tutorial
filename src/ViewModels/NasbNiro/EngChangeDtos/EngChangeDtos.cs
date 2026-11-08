using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.EngChange;

namespace ViewModels.NasbNiro.EngChangeDtos
{
    // Package A2: engineering change requests (ECR), orders (ECO), the computed impact, dispositions, tasks and the gate checks. All class names start with Ec so none clashes with another module.

    public class EcRequestQuery
    {
        public EcrStatus? Status { get; set; }
        public string Contains { get; set; }
        public string PartNumber { get; set; }
    }

    public class EcOrderQuery
    {
        public EcoStatus? Status { get; set; }
        public int? RequestId { get; set; }
        public string Contains { get; set; }
        public string PartNumber { get; set; }
    }

    public class EcTargetDto
    {
        public int? Id { get; set; }
        public EcTargetKind Kind { get; set; }
        public string PartNumber { get; set; }
        public int? ProductId { get; set; }
        public int? BomHeaderId { get; set; }
        public int? OpcRoutingId { get; set; }
        public int? DeliverableId { get; set; }
        public int? RoutingRevisionId { get; set; }
        public string Reference { get; set; }
        public string Note { get; set; }
    }

    public class SaveEcRequestDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public string Reason { get; set; }
        public string Description { get; set; }
        public EcClass Classification { get; set; } = EcClass.ClassI;
        public EcUrgency Urgency { get; set; } = EcUrgency.Normal;
        public List<EcTargetDto> Targets { get; set; } = new List<EcTargetDto>();
        public string RowVersion { get; set; }
    }

    public class EcRequestActionDto
    {
        public int Id { get; set; }

        /// <summary>submit, approve, reject, cancel.</summary>
        public string Action { get; set; }

        public string Note { get; set; }
    }

    public class EcRequestListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public EcClass Classification { get; set; }
        public EcUrgency Urgency { get; set; }
        public EcrStatus Status { get; set; }
        public string StatusText { get; set; }
        public string RequestedByName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string Parts { get; set; }
        public int? OrderId { get; set; }
        public string OrderNumber { get; set; }
    }

    public class EcRequestDto : EcRequestListDto
    {
        public string Reason { get; set; }
        public string Description { get; set; }
        public int RequestedByUserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string DecidedByName { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
        public List<EcTargetDto> Targets { get; set; } = new List<EcTargetDto>();
        public bool CanEdit { get; set; }
        public bool CanSubmit { get; set; }
        public bool CanDecide { get; set; }
        public bool CanCancel { get; set; }
        public bool CanCreateOrder { get; set; }
        public string RowVersion { get; set; }
    }

    public class SaveEcOrderDto
    {
        public int? Id { get; set; }

        /// <summary>For a new order: the approved request it comes from.</summary>
        public int? RequestId { get; set; }

        public string Title { get; set; }
        public string Summary { get; set; }
        public string EffectiveFromOrderNumber { get; set; }
        public string EffectiveFromSerial { get; set; }
        public DateTime? EffectiveFromDate { get; set; }
        public string EffectivityNote { get; set; }
        public string RowVersion { get; set; }
    }

    public class EcOrderChangeSaveDto
    {
        public int OrderId { get; set; }
        public EcChangeKind Kind { get; set; }
        public EcChangeAction Action { get; set; } = EcChangeAction.NewRevision;

        /// <summary>The id of the new revision (BOM header, OPC chart, document revision, MES routing revision); for Obsolete, the BOM header that goes obsolete.</summary>
        public int TargetId { get; set; }

        public string Note { get; set; }
    }

    public class EcOrderChangeRemoveDto
    {
        public int OrderId { get; set; }
        public int ChangeId { get; set; }
    }

    /// <summary>Makes the new revision (a draft copy of the revision in force) from the order and names it in the order.</summary>
    public class EcNewRevisionDto
    {
        public int OrderId { get; set; }

        /// <summary>Bom or Opc.</summary>
        public EcChangeKind Kind { get; set; }

        /// <summary>The BOM header or OPC chart in force that the new revision is made from.</summary>
        public int SourceId { get; set; }

        public string Note { get; set; }
    }

    public class EcDispositionSaveDto
    {
        public int OrderId { get; set; }
        public string ImpactKey { get; set; }
        public EcDisposition? Disposition { get; set; }
        public string Note { get; set; }
        public int? OwnerUserId { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class EcBulkDispositionDto
    {
        public int OrderId { get; set; }
        public EcImpactCategory Category { get; set; }
        public EcDisposition Disposition { get; set; }
        public string Note { get; set; }
        public int? OwnerUserId { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class EcOrderActionDto
    {
        public int Id { get; set; }

        /// <summary>refresh-impact, submit, approve-engineering, approve-qa, reject, cancel, close.</summary>
        public string Action { get; set; }

        public string Note { get; set; }
    }

    public class EcTaskActionDto
    {
        public int Id { get; set; }

        /// <summary>done, verify.</summary>
        public string Action { get; set; }

        public string Note { get; set; }
    }

    public class EcImpactItemDto
    {
        public string Key { get; set; }
        public EcImpactCategory Category { get; set; }
        public string Text { get; set; }
        public string PartNumber { get; set; }
        public decimal? Quantity { get; set; }
        public int? RefId { get; set; }
    }

    public class EcImpactCountDto
    {
        public EcImpactCategory Category { get; set; }
        public string Title { get; set; }
        public int Count { get; set; }
        public int Decided { get; set; }
        public bool Truncated { get; set; }
    }

    /// <summary>The computed impact analysis. Never typed by a person: the server builds it from the registers and stores it with the order (JSON + SHA-256).</summary>
    public class EcImpactDto
    {
        public DateTimeOffset? ComputedAt { get; set; }
        public List<string> Parts { get; set; } = new List<string>();
        public List<EcImpactCountDto> Counts { get; set; } = new List<EcImpactCountDto>();
        public List<EcImpactItemDto> Items { get; set; } = new List<EcImpactItemDto>();
        public string Sha256 { get; set; }
        public bool HashOk { get; set; } = true;
    }

    public class EcDispositionDto
    {
        public int Id { get; set; }
        public EcImpactCategory Category { get; set; }
        public string CategoryTitle { get; set; }
        public string ImpactKey { get; set; }
        public string Text { get; set; }
        public string PartNumber { get; set; }
        public decimal? Quantity { get; set; }
        public EcDisposition? Disposition { get; set; }
        public string DispositionTitle { get; set; }
        public string Note { get; set; }
        public int? OwnerUserId { get; set; }
        public string OwnerName { get; set; }
        public DateTime? DueDate { get; set; }
        public bool Stale { get; set; }
        public List<EcDisposition> Allowed { get; set; } = new List<EcDisposition>();
    }

    public class EcChangeDto
    {
        public int Id { get; set; }
        public EcChangeKind Kind { get; set; }
        public EcChangeAction Action { get; set; }
        public int TargetId { get; set; }
        public int? OldTargetId { get; set; }
        public string Code { get; set; }
        public string PartNumber { get; set; }
        public int? OldRevision { get; set; }
        public int? NewRevision { get; set; }
        public string Reference { get; set; }
        public string Note { get; set; }

        /// <summary>The state of the named revision now (Draft, UnderReview, Approved, Released ...).</summary>
        public string State { get; set; }
    }

    public class EcTaskDto
    {
        public int Id { get; set; }
        public int? DispositionItemId { get; set; }
        public string Title { get; set; }
        public EcDisposition Disposition { get; set; }
        public string DispositionTitle { get; set; }
        public int OwnerUserId { get; set; }
        public string OwnerName { get; set; }
        public DateTime DueDate { get; set; }
        public EcTaskStatus Status { get; set; }
        public string StatusText { get; set; }
        public bool Overdue { get; set; }
        public DateTimeOffset? DoneAt { get; set; }
        public string DoneByName { get; set; }
        public string DoneNote { get; set; }
        public DateTimeOffset? VerifiedAt { get; set; }
        public string VerifiedByName { get; set; }
        public string VerifyNote { get; set; }
        public bool CanDone { get; set; }
        public bool CanVerify { get; set; }
    }

    public class EcOrderListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int RequestId { get; set; }
        public string RequestNumber { get; set; }
        public string Title { get; set; }
        public EcoStatus Status { get; set; }
        public string StatusText { get; set; }
        public string Effectivity { get; set; }
        public string Parts { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int OpenTasks { get; set; }
    }

    public class EcOrderDto : EcOrderListDto
    {
        public string Summary { get; set; }
        public EcClass Classification { get; set; }
        public EcUrgency Urgency { get; set; }
        public string RequestTitle { get; set; }
        public string RequestReason { get; set; }
        public string EffectiveFromOrderNumber { get; set; }
        public string EffectiveFromSerial { get; set; }
        public DateTime? EffectiveFromDate { get; set; }
        public string EffectivityNote { get; set; }
        public int CreatorUserId { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string EngineeringApprovedByName { get; set; }
        public DateTimeOffset? EngineeringApprovedAt { get; set; }
        public string EngineeringNote { get; set; }
        public string QaApprovedByName { get; set; }
        public DateTimeOffset? QaApprovedAt { get; set; }
        public string QaNote { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string RejectReason { get; set; }
        public string CancelReason { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string ContentSha256 { get; set; }
        public EcImpactDto Impact { get; set; }
        public List<EcChangeDto> Changes { get; set; } = new List<EcChangeDto>();
        public List<EcDispositionDto> Dispositions { get; set; } = new List<EcDispositionDto>();
        public List<EcTaskDto> Tasks { get; set; } = new List<EcTaskDto>();

        /// <summary>What still stops the submission or the approval (Persian), computed now.</summary>
        public List<string> Blockers { get; set; } = new List<string>();

        public bool CanEdit { get; set; }
        public bool CanSubmit { get; set; }
        public bool CanApproveEngineering { get; set; }
        public bool CanApproveQa { get; set; }
        public bool CanReject { get; set; }
        public bool CanCancel { get; set; }
        public bool CanClose { get; set; }
        public bool CanVoid { get; set; }
        public string RowVersion { get; set; }
    }

    public class EcHistoryRowDto
    {
        public string Kind { get; set; }
        public int Id { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string StatusText { get; set; }
        public string Detail { get; set; }
        public string Effectivity { get; set; }
        public DateTimeOffset At { get; set; }
    }

    /// <summary>The answer of the gate for one revision: is an approved order needed, and is there one.</summary>
    public class EcGateCheckDto
    {
        public EcChangeKind Kind { get; set; }
        public int TargetId { get; set; }
        public bool Required { get; set; }
        public bool Ok { get; set; }
        public string OrderNumber { get; set; }
        public string Message { get; set; }
    }
}
