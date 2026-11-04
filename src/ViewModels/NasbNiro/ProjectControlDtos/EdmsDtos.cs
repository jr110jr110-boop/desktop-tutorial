using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    // Package P-B7b: engineering document control (EDMS) and the tender register.

    /// <summary>A document received from the client, created on receipt (E8).</summary>
    public class PcReceivedDocumentDto
    {
        public string DocumentNumber { get; set; }
        public string Title { get; set; }
        public string Discipline { get; set; }
        public string DocumentType { get; set; }

        /// <summary>The client's revision code, as it is written on the document.</summary>
        public string Revision { get; set; }
        public Guid? DocumentId { get; set; }
        public string FileName { get; set; }
    }

    public class PcRevisionFileDto
    {
        public int Id { get; set; }
        public Guid DocumentId { get; set; }
        public PcRevisionFileRole Role { get; set; }
        public string FileName { get; set; }
        public string Title { get; set; }
    }

    public class PcRevisionQualityLinkDto
    {
        public int Id { get; set; }
        public PcQualityLinkKind Kind { get; set; }
        public int TargetId { get; set; }
        public string TargetNumber { get; set; }
        public string Note { get; set; }
    }

    public class PcRevisionDto
    {
        public int Id { get; set; }
        public int DeliverableId { get; set; }
        public string DocumentNumber { get; set; }
        public string Title { get; set; }
        public string Discipline { get; set; }
        public int Sequence { get; set; }
        public string Code { get; set; }
        public PcDocStatus Status { get; set; }
        public string Reason { get; set; }
        public int PreparedByUserId { get; set; }
        public string PreparedByName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ReviewerUserId { get; set; }
        public string ReviewerName { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string ReviewNote { get; set; }
        public int? ApproverUserId { get; set; }
        public string ApproverName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ReleasedByUserId { get; set; }
        public string ReleasedByName { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public string RejectReason { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public int? SupersededByRevisionId { get; set; }
        public DateTimeOffset? SupersededAt { get; set; }
        public string ObsoleteReason { get; set; }
        public bool ChangeImpact { get; set; }
        public int? ChangeRequestId { get; set; }
        public string ChangeRequestNumber { get; set; }
        public bool ReceivedFromClient { get; set; }
        public List<PcRevisionFileDto> Files { get; set; } = new List<PcRevisionFileDto>();
        public List<PcRevisionQualityLinkDto> QualityLinks { get; set; } = new List<PcRevisionQualityLinkDto>();

        /// <summary>What the caller may do now (the UI shows the buttons, the service checks again).</summary>
        public List<string> Actions { get; set; } = new List<string>();
        public string RowVersion { get; set; }
    }

    public class PcDocumentEventDto
    {
        public PcDocEventKind Kind { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public DateTimeOffset At { get; set; }
        public int? RevisionId { get; set; }
        public string Note { get; set; }
    }

    public class PcDeliverableHistoryDto
    {
        public int DeliverableId { get; set; }
        public string DocumentNumber { get; set; }
        public string Title { get; set; }
        public string Discipline { get; set; }
        public string RevisionScheme { get; set; }
        public int? CheckedOutByUserId { get; set; }
        public string CheckedOutByName { get; set; }
        public DateTimeOffset? CheckedOutAt { get; set; }
        public string CheckOutNote { get; set; }
        public List<string> DeliverableActions { get; set; } = new List<string>();
        public List<PcRevisionDto> Revisions { get; set; } = new List<PcRevisionDto>();
        public List<PcDocumentEventDto> Events { get; set; } = new List<PcDocumentEventDto>();
        public List<PcDocLinkDto> Links { get; set; } = new List<PcDocLinkDto>();
    }

    public class PcRevisionCreateDto
    {
        public int DeliverableId { get; set; }
        public string Reason { get; set; }
        public bool ChangeImpact { get; set; }
        public int? ChangeRequestId { get; set; }
    }

    public class PcRevisionFileAddDto
    {
        public int RevisionId { get; set; }
        public Guid DocumentId { get; set; }
        public PcRevisionFileRole Role { get; set; } = PcRevisionFileRole.Native;
        public string FileName { get; set; }
        public string Title { get; set; }
    }

    public class PcRevisionFileRemoveDto
    {
        public int FileId { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>submit | review | approve | reject | release | as-built | obsolete | link-change | link-quality</summary>
    public class PcRevisionActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
        public int? ChangeRequestId { get; set; }
        public bool? ChangeImpact { get; set; }
        public PcQualityLinkKind? QualityKind { get; set; }
        public int? QualityTargetId { get; set; }
    }

    public class PcRevisionDiffDto
    {
        public string Field { get; set; }
        public string A { get; set; }
        public string B { get; set; }
    }

    public class PcRevisionCompareDto
    {
        public PcRevisionDto A { get; set; }
        public PcRevisionDto B { get; set; }
        public List<PcRevisionDiffDto> Differences { get; set; } = new List<PcRevisionDiffDto>();
    }

    public class PcCheckOutDto
    {
        public int DeliverableId { get; set; }
        public string Note { get; set; }

        /// <summary>Cancel by someone else: the reason is required.</summary>
        public string Reason { get; set; }
    }

    // ------------------------------------------------------------------------------------------ links and tags

    public class PcDocLinkSaveDto
    {
        public int? DeliverableId { get; set; }
        public int? ControlledDocumentId { get; set; }
        public PcDocLinkKind Kind { get; set; }
        public int? ItemId { get; set; }
        public int? BomLineId { get; set; }
        public string PartNumber { get; set; }
        public string StageCode2 { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public int? TagId { get; set; }
        public int? OperationTemplateId { get; set; }
        public string Note { get; set; }
    }

    public class PcDocLinkDto
    {
        public int Id { get; set; }
        public int? DeliverableId { get; set; }
        public int? ControlledDocumentId { get; set; }
        public string DocumentNumber { get; set; }
        public string Title { get; set; }
        public PcDocLinkKind Kind { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public int? BomLineId { get; set; }
        public string PartNumber { get; set; }
        public string StageCode2 { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public int? TagId { get; set; }
        public string TagCode { get; set; }
        public int? OperationTemplateId { get; set; }
        public string Note { get; set; }
    }

    public class PcDocLinkVoidDto
    {
        public int Id { get; set; }
        public string Reason { get; set; }
    }

    public class PcDocLinkQuery
    {
        public int? ItemId { get; set; }
        public string PartNumber { get; set; }
        public string StageCode2 { get; set; }
        public int? TagId { get; set; }
        public int? ActivityId { get; set; }
        public int? WbsNodeId { get; set; }
        public int? DeliverableId { get; set; }
    }

    /// <summary>One document found for a part, valve or tag, with its latest released revision.</summary>
    public class PcPartDocumentDto
    {
        public int? DeliverableId { get; set; }
        public int? ControlledDocumentId { get; set; }
        public string DocumentNumber { get; set; }
        public string Title { get; set; }
        public string Discipline { get; set; }
        public string DocumentType { get; set; }
        public PcDocLinkKind Via { get; set; }
        public string ViaText { get; set; }
        public int? ReleasedRevisionId { get; set; }
        public string ReleasedCode { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public PcDocStatus? LatestStatus { get; set; }
        public List<PcRevisionFileDto> Files { get; set; } = new List<PcRevisionFileDto>();
    }

    public class PcTagSaveDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ItemId { get; set; }
        public int? WorkOrderId { get; set; }
        public string Note { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PcTagDto : PcTagSaveDto
    {
        public string ItemCode { get; set; }
        public string WorkOrderCode { get; set; }
        public int DocumentCount { get; set; }
    }

    // ------------------------------------------------------------------------------------------ distribution, matrix, notices

    public class PcDistributionSaveDto
    {
        public int? Id { get; set; }
        public string Discipline { get; set; }
        public int UserId { get; set; }
        public int? ContractId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PcDistributionDto : PcDistributionSaveDto
    {
        public string UserName { get; set; }
    }

    public class PcApproverSaveDto
    {
        public int? Id { get; set; }
        public string Discipline { get; set; }
        public int UserId { get; set; }
        public int? ContractId { get; set; }
        public bool CanReview { get; set; } = true;
        public bool CanApprove { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PcApproverDto : PcApproverSaveDto
    {
        public string UserName { get; set; }
    }

    public class PcNoticeDto
    {
        public int Id { get; set; }
        public int RevisionId { get; set; }
        public int DeliverableId { get; set; }
        public string DocumentNumber { get; set; }
        public string Title { get; set; }
        public string RevisionCode { get; set; }
        public DateTimeOffset NotifiedAt { get; set; }
        public DateTimeOffset? AcknowledgedAt { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public bool Overdue { get; set; }
    }

    public class PcDocNoticeQuery : PaginationParams
    {
        public bool MineOnly { get; set; } = true;
        public bool OpenOnly { get; set; }
        public int? DeliverableId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ shop floor

    /// <summary>A document of the order's part or operation: only its latest released revision.</summary>
    public class PcShopDocumentDto
    {
        public int DeliverableId { get; set; }
        public int RevisionId { get; set; }
        public string DocumentNumber { get; set; }
        public string Title { get; set; }
        public string DocumentType { get; set; }
        public string Code { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public string ViaText { get; set; }
        public List<PcRevisionFileDto> Files { get; set; } = new List<PcRevisionFileDto>();
    }

    /// <summary>The stamp of a controlled copy (E11). No PDF library exists on the server, so the page prints the file under an HTML wrapper that carries these lines.</summary>
    public class PcControlledCopyRequestDto
    {
        public int RevisionId { get; set; }
        public Guid? DocumentId { get; set; }
    }

    public class PcControlledCopyDto
    {
        public int RevisionId { get; set; }
        public string DocumentNumber { get; set; }
        public string Title { get; set; }
        public string Code { get; set; }
        public DateTimeOffset PrintedAt { get; set; }
        public int PrintedByUserId { get; set; }
        public string PrintedByName { get; set; }
        public string StampText { get; set; }
        public Guid DocumentId { get; set; }
        public string FileName { get; set; }
    }

    // ------------------------------------------------------------------------------------------ import / export

    public class PcMdrImportResultDto
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------------------------------ tender register

    public class PcTenderPackageSaveDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public int? TenderId { get; set; }
        public string TenderCode { get; set; }
        public string Note { get; set; }
    }

    public class PcTenderPackageDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? TenderId { get; set; }
        public string TenderCode { get; set; }
        public PcTenderStatus Status { get; set; }
        public int? ContractId { get; set; }
        public string ContractTitle { get; set; }
        public DateTimeOffset? LinkedAt { get; set; }
        public string Note { get; set; }
        public int DocumentCount { get; set; }
        public List<PcTenderDocumentDto> Documents { get; set; } = new List<PcTenderDocumentDto>();
    }

    public class PcTenderDocumentSaveDto
    {
        public int? Id { get; set; }
        public int PackageId { get; set; }
        public string Title { get; set; }
        public string Discipline { get; set; }
        public string DocumentType { get; set; }
        public string Revision { get; set; }
        public Guid? DocumentId { get; set; }
    }

    public class PcTenderDocumentDto
    {
        public int Id { get; set; }
        public int PackageId { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Discipline { get; set; }
        public string DocumentType { get; set; }
        public string Revision { get; set; }
        public Guid? DocumentId { get; set; }
        public DateTimeOffset? IssuedAt { get; set; }
        public int? IssuedByUserId { get; set; }
        public string VoidReason { get; set; }
        public bool Voided { get; set; }
    }

    /// <summary>issue | void | win | lose | cancel | link-contract</summary>
    public class PcTenderActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
        public int? ContractId { get; set; }
        public int? DocumentId { get; set; }
    }

    public class PcTenderQuery : PaginationParams
    {
        public int? ContractId { get; set; }
        public string Contains { get; set; }
    }
}
