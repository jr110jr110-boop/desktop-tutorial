using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.EngineeringDtos
{
    public class EnCaseQuery
    {
        public int? Status { get; set; }
        public int? Classification { get; set; }
        public int? InquiryId { get; set; }
        public int? ContractId { get; set; }
        public bool OnlyOpen { get; set; }
        public string Contains { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class EnCaseEventDto
    {
        public string Kind { get; set; }
        public DateTimeOffset At { get; set; }
        public string AtText { get; set; }
        public int? UserId { get; set; }
        public string UserName { get; set; }
        public string Note { get; set; }
    }

    public class EnCaseDto
    {
        public int Id { get; set; }
        public string ReferenceCode { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public int? InquiryId { get; set; }
        public string InquiryNumber { get; set; }
        public int? InquiryStatus { get; set; }
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public string CustomerName { get; set; }
        public string CustomerRequirementNote { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }

        public int Classification { get; set; }
        public string ClassificationText { get; set; }
        public int? ProductId { get; set; }
        public string ProductCode { get; set; }
        public bool PlatformWithChanges { get; set; }
        public string ChangeNote { get; set; }

        public int ProposedClassification { get; set; }
        public string ProposedClassificationText { get; set; }
        public int? ProposedProductId { get; set; }
        public string ProposedProductCode { get; set; }
        public bool ProposedWithChanges { get; set; }
        public string ProposedChangeNote { get; set; }
        public string ProposalBasis { get; set; }
        public int? ProposedByUserId { get; set; }
        public string ProposedByName { get; set; }
        public bool ProposedByAgent { get; set; }
        public string ProposedAtText { get; set; }

        public string EngConfirmedByName { get; set; }
        public string EngConfirmedAtText { get; set; }
        public string CeoApprovedByName { get; set; }
        public string CeoApprovedAtText { get; set; }
        public string ReturnReason { get; set; }

        /// <summary>Point A warning: a platform whose dossier is not complete (never blocks the tender).</summary>
        public bool PlatformIncomplete { get; set; }
        public int PlatformMissingRequired { get; set; }
        public string PlatformWarning { get; set; }

        public int? TenderPackageId { get; set; }
        public string TenderPackageCode { get; set; }
        public int TenderDocumentCount { get; set; }
        public string TenderReleasedByName { get; set; }
        public string TenderReleasedAtText { get; set; }
        public bool TenderStale { get; set; }
        public string ConceptNote { get; set; }

        public int CharterReviewOutcome { get; set; }
        public string CharterReviewOutcomeText { get; set; }
        public string CharterReviewedByName { get; set; }
        public string CharterReviewedAtText { get; set; }
        public string CharterReviewNote { get; set; }
        public bool CharterExists { get; set; }
        public bool CharterStale { get; set; }

        public int OpenClarifications { get; set; }
        public int? OpenHandoverId { get; set; }
        public string ClosedReason { get; set; }
        public bool TouchedByMe { get; set; }
        public List<EnCaseEventDto> Events { get; set; } = new List<EnCaseEventDto>();
        public string RowVersion { get; set; }

        public bool CanPropose { get; set; }
        public string ProposeBlockedReason { get; set; }
        public bool CanChange { get; set; }
        public string ChangeBlockedReason { get; set; }
        public bool CanConfirm { get; set; }
        public string ConfirmBlockedReason { get; set; }
        public bool CanApprove { get; set; }
        public string ApproveBlockedReason { get; set; }
        public bool CanReturn { get; set; }
        public string ReturnBlockedReason { get; set; }
        public bool CanReclassify { get; set; }
        public string ReclassifyBlockedReason { get; set; }
        public bool CanSaveConcept { get; set; }
        public string SaveConceptBlockedReason { get; set; }
        public bool CanReleaseTender { get; set; }
        public string ReleaseTenderBlockedReason { get; set; }
        public bool CanCharterReview { get; set; }
        public string CharterReviewBlockedReason { get; set; }
        public bool CanCancel { get; set; }
        public string CancelBlockedReason { get; set; }
    }

    /// <summary>Action: open, propose, change, confirm, return, approve, reclassify, save-concept, draft-tender, revalidate-tender, release-tender, award, close, cancel, charter-confirm, charter-return.</summary>
    public class EnCaseActionDto
    {
        public int? Id { get; set; }
        public int? InquiryId { get; set; }
        public int? ContractId { get; set; }
        public string Action { get; set; }

        /// <summary>For propose and change: 0 none (propose: the analysis only), 1 platform, 2 new design.</summary>
        public int Classification { get; set; }
        public int? ProductId { get; set; }
        public bool WithChanges { get; set; }
        public string ChangeNote { get; set; }
        public string Basis { get; set; }
        public string Note { get; set; }
        public string ConceptNote { get; set; }

        /// <summary>At the confirm of a New design: the Code of the reference list (the standard basis); required when more than one Code is approved.</summary>
        public string ReferenceCode { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>The state of the engineering gate of an inquiry or a contract: for the sales and project screens (legacy shown with its note).</summary>
    public class EnGateStateDto
    {
        public bool Controlled { get; set; }
        public bool Legacy { get; set; }
        public string LegacyNote { get; set; }
        public int? CaseId { get; set; }
        public string CaseNumber { get; set; }
        public int? CaseStatus { get; set; }
        public string CaseStatusText { get; set; }
        public bool Passed { get; set; }
        public string BlockedReason { get; set; }
    }

    public class EnClarificationQuery
    {
        public int? CaseId { get; set; }
        public int? Status { get; set; }
        public bool OnlyOpen { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class EnClarificationDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int CaseId { get; set; }
        public string CaseNumber { get; set; }
        public int? FollowsId { get; set; }
        public string Question { get; set; }
        public List<Guid> QuestionAttachments { get; set; } = new List<Guid>();
        public string AskedByName { get; set; }
        public string AskedAtText { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public string SentToClientByName { get; set; }
        public string SentToClientAtText { get; set; }
        public string ClientLetterRef { get; set; }
        public string Answer { get; set; }
        public List<Guid> AnswerAttachments { get; set; } = new List<Guid>();
        public string AnswerRecordedByName { get; set; }
        public string AnswerRecordedAtText { get; set; }
        public string ClosedByName { get; set; }
        public string ClosedAtText { get; set; }
        public string VoidReason { get; set; }
        public string RowVersion { get; set; }

        public bool CanSave { get; set; }
        public string SaveBlockedReason { get; set; }
        public bool CanSubmit { get; set; }
        public string SubmitBlockedReason { get; set; }
        public bool CanSend { get; set; }
        public string SendBlockedReason { get; set; }
        public bool CanAnswer { get; set; }
        public string AnswerBlockedReason { get; set; }
        public bool CanClose { get; set; }
        public string CloseBlockedReason { get; set; }
        public bool CanReask { get; set; }
        public string ReaskBlockedReason { get; set; }
        public bool CanVoid { get; set; }
        public string VoidBlockedReason { get; set; }
    }

    /// <summary>Engineering writes the question (and the attachments of the question); the sent-to-client and the answer fields exist only in the commercial actions.</summary>
    public class EnSaveClarificationDto
    {
        public int? Id { get; set; }
        public int CaseId { get; set; }
        public string Question { get; set; }
        public List<Guid> QuestionAttachments { get; set; } = new List<Guid>();
        public string RowVersion { get; set; }
    }

    /// <summary>Action: submit (engineering to commercial), send (commercial: ClientLetterRef), answer (commercial: Answer and AnswerAttachments), close, reask (engineering: a new clarification that follows this one; Question is the new question), void.</summary>
    public class EnClarificationActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string ClientLetterRef { get; set; }
        public string Answer { get; set; }
        public List<Guid> AnswerAttachments { get; set; } = new List<Guid>();
        public string Question { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }
}
