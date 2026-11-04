using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProjectControlDtos
{
    public class PcDeliverableDto
    {
        public int? Id { get; set; }
        public int ContractId { get; set; }
        public int? WorkOrderId { get; set; }
        public string DocumentNumber { get; set; }
        public string Title { get; set; }

        /// <summary>True: the line is in the MDL, the list the client receives.</summary>
        public bool ClientSubmittal { get; set; }
        public string StandardSource { get; set; }
        public string Discipline { get; set; }
        public string DocumentType { get; set; }
        public bool Required { get; set; } = true;
        public DateTime? PlannedDate { get; set; }

        /// <summary>P-B7b: weight of the document in the engineering progress (1 = same as the others).</summary>
        public decimal ProgressWeight { get; set; } = 1m;
        public int? CheckedOutByUserId { get; set; }
        public DateTimeOffset? CheckedOutAt { get; set; }
        public bool ReceivedFromClient { get; set; }
        public PcDocStatus? LatestStatus { get; set; }
        public string LatestCode { get; set; }
        public string ReleasedCode { get; set; }
        public PcIssueState IssueState { get; set; }
        public string Revision { get; set; }
        public Guid? DocumentId { get; set; }
        public int? ControlledDocumentId { get; set; }
        public string LastTransmittalNumber { get; set; }
        public DateTime? LastTransmittalDate { get; set; }
        public string ClientResponseCode { get; set; }
        public DateTime? ClientResponseDate { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public decimal ProgressPercent { get; set; }
        public string RowVersion { get; set; }
    }

    public class PcDeliverableQuery : PcContractQuery
    {
        public int? WorkOrderId { get; set; }

        /// <summary>Only the MDL lines.</summary>
        public bool ClientOnly { get; set; }
        public PcIssueState? IssueState { get; set; }
    }

    public class PcDeliverableActionDto
    {
        public int Id { get; set; }

        /// <summary>first-issue, second-issue, internal-approve (internal lines), link-document</summary>
        public string Action { get; set; }
        public string Revision { get; set; }
        public Guid? DocumentId { get; set; }
        public int? ControlledDocumentId { get; set; }
    }

    public class PcMdrDraftDto
    {
        public int ContractId { get; set; }
        public int WorkOrderId { get; set; }
    }

    public class PcMdrDraftResultDto
    {
        public int WorkOrderId { get; set; }
        public int LinesDrafted { get; set; }
        public string Message { get; set; }
    }

    public class PcMdrTemplateLineDto
    {
        public int? Id { get; set; }
        public int Seq { get; set; }
        public string DocumentType { get; set; }
        public string Discipline { get; set; }
        public string Title { get; set; }
        public bool ClientSubmittal { get; set; }
        public bool Required { get; set; } = true;
        public string StandardSource { get; set; }
    }

    public class PcMdrTemplateDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public ItpProductType ProductType { get; set; }
        public string StandardSource { get; set; }
        public string Note { get; set; }
        public bool IsActive { get; set; } = true;
        public List<PcMdrTemplateLineDto> Lines { get; set; } = new List<PcMdrTemplateLineDto>();
    }

    public class PcResponseCodeDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public bool CountsAsApproved { get; set; }
        public bool RequiresResubmit { get; set; }

        /// <summary>R1c (L2): a rejection: the document goes back to the previous issue step (and its progress falls) when the client answers with this code.</summary>
        public bool ResetsProgress { get; set; }
        public string Source { get; set; }
    }

    public class PcTransmittalLineDto
    {
        public int? Id { get; set; }
        public int DeliverableId { get; set; }
        public string DocumentNumber { get; set; }
        public string Title { get; set; }
        public string Revision { get; set; }

        /// <summary>P-B7b: the revision that is sent. A document with revisions is sent by revision id (a released one; for information also one under review or approved).</summary>
        public int? RevisionId { get; set; }

        /// <summary>P-B7b (E8): on an incoming transmittal a document that is not in the register yet is created on receipt. DeliverableId is then 0.</summary>
        public PcReceivedDocumentDto NewDocument { get; set; }
        public PcTransmittalPurpose Purpose { get; set; } = PcTransmittalPurpose.ForApproval;
        public string ResponseCode { get; set; }
        public DateTime? ResponseDate { get; set; }
        public string ResponseNote { get; set; }
    }

    public class PcTransmittalDto
    {
        public int? Id { get; set; }
        public string Number { get; set; }
        public int ContractId { get; set; }
        public PcTransmittalDirection Direction { get; set; } = PcTransmittalDirection.Outgoing;
        public DateTime TransmittalDate { get; set; }
        public DateTime? ResponseDue { get; set; }
        public string Reference { get; set; }
        public string Note { get; set; }
        public bool Overdue { get; set; }
        public List<PcTransmittalLineDto> Lines { get; set; } = new List<PcTransmittalLineDto>();
    }

    public class PcTransmittalQuery : PcContractQuery
    {
        public PcTransmittalDirection? Direction { get; set; }
        public bool OnlyOverdue { get; set; }
    }

    public class PcTransmittalResponseDto
    {
        public int TransmittalLineId { get; set; }
        public string ResponseCode { get; set; }
        public DateTime? ResponseDate { get; set; }
        public string Note { get; set; }
    }

    public class PcFinalBookRequestDto
    {
        public int WorkOrderId { get; set; }

        /// <summary>company or client</summary>
        public string Variant { get; set; }

        /// <summary>Issue the book although mandatory entries are missing (an interim issue).</summary>
        public bool AllowIncomplete { get; set; }
    }

    public class PcFinalBookDto
    {
        public int ReportIssueId { get; set; }
        public string Number { get; set; }
        public string Variant { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int? ControlledDocumentId { get; set; }
        public string DocumentCode { get; set; }
        public string Revision { get; set; }
        public string Hash { get; set; }
        public int MissingCount { get; set; }
        public int EntryCount { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }

        /// <summary>R1c (M1): the controlled-document status of the revision: InReview (0 = Draft, 1 = InReview, 2 = Approved, 3 = Effective, 4 = Obsolete, 5 = Scheduled). A revision becomes effective only when someone other than the preparer approves it.</summary>
        public int? RevisionStatus { get; set; }
        public int? PreparedByUserId { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
    }

    public class PcFinalBookQuery : PcContractQuery
    {
        public int? WorkOrderId { get; set; }
    }
}
