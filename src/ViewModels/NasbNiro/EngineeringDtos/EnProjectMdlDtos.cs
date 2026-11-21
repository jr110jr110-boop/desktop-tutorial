using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.EngineeringDtos
{
    /// <summary>One line of the MDL of a project. Origin: 0 = from the standard list of the company, 1 = added for this project. Removed: a standard line this project does not hand to the client (kept and marked, never deleted).</summary>
    public class EnProjectMdlLineDto
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public string DocumentType { get; set; }
        public string Discipline { get; set; }
        public string Title { get; set; }
        public bool Required { get; set; }
        public bool TenderStage { get; set; }
        public bool ClientSubmittal { get; set; }
        public string StandardClause { get; set; }
        public string Units { get; set; }
        public string Note { get; set; }
        public int Origin { get; set; }
        public string OriginText { get; set; }
        public bool Removed { get; set; }
    }

    /// <summary>The MDL of one project (one revision). Status: 0 Draft, 1 Checked, 2 Approved, 3 Void.</summary>
    public class EnProjectMdlDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int CaseId { get; set; }
        public int Revision { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public int? BasedOnListId { get; set; }
        public string BasedOnCode { get; set; }
        public int BasedOnRevision { get; set; }
        public Guid? SourceDocumentId { get; set; }
        public string SourceFileName { get; set; }
        public string ChangeNote { get; set; }
        public int? CheckedByUserId { get; set; }
        public string CheckedByName { get; set; }
        public string CheckedAtText { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string ApprovedByName { get; set; }
        public string ApprovedAtText { get; set; }

        /// <summary>True when the lines no longer hash to the approved hash (the approval is stale).</summary>
        public bool ApprovalStale { get; set; }
        public Guid? IssuedDocumentId { get; set; }
        public string IssuedFileName { get; set; }
        public string ClientApprovedAtText { get; set; }
        public DateTimeOffset? ClientApprovedAt { get; set; }
        public string ClientApprovalRef { get; set; }
        public Guid? ClientApprovalDocumentId { get; set; }
        public string ClientApprovalRecordedByName { get; set; }
        public string VoidReason { get; set; }

        /// <summary>Lines of the standard list that the project keeps, lines it removed, lines added for the project.</summary>
        public int StandardKeptCount { get; set; }
        public int RemovedCount { get; set; }
        public int AddedCount { get; set; }

        /// <summary>Not-removed lines (what goes to the client).</summary>
        public int LineCount { get; set; }
        public List<EnProjectMdlLineDto> Lines { get; set; } = new List<EnProjectMdlLineDto>();

        /// <summary>The token of the last change (an upload or an action sent with an old token is refused as a concurrent change; optional).</summary>
        public string RowVersion { get; set; }
    }

    /// <summary>Revision history row of the project MDL of a case (without lines).</summary>
    public class EnProjectMdlHistoryDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int Revision { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public string BasedOnCode { get; set; }
        public int BasedOnRevision { get; set; }
        public string ApprovedAtText { get; set; }
        public string VoidReason { get; set; }
        public int LineCount { get; set; }
    }

    /// <summary>The CEO's permit to send the tender documents of a special project without a project MDL. Status: 0 Requested, 1 Approved, 2 Rejected, 3 Void (withdrawn).</summary>
    public class EnMdlPermitDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int CaseId { get; set; }
        public string Reason { get; set; }
        public int Status { get; set; }
        public string StatusText { get; set; }
        public int RequestedByUserId { get; set; }
        public string RequestedByName { get; set; }
        public string RequestedAtText { get; set; }
        public string DecidedByName { get; set; }
        public string DecidedAtText { get; set; }
        public string DecisionNote { get; set; }

        /// <summary>An approved permit that was given before the latest decision of the case (changed standard or classification): it no longer opens the release.</summary>
        public bool ApprovalStale { get; set; }
    }

    /// <summary>Everything the case page needs about the project MDL (GET by-case/{caseId} and the answer of every action): the live MDL, the history, the live-or-last permit and the flags with their reasons (the same checks as the actions).</summary>
    public class EnProjectMdlCaseDto
    {
        public int CaseId { get; set; }
        public string CaseNumber { get; set; }

        /// <summary>False when the case does not need a project MDL (not a turbine case, or no tender stage).</summary>
        public bool Applies { get; set; }

        /// <summary>Why the MDL does not apply (when <see cref="Applies"/> is false).</summary>
        public string NotApplicableText { get; set; }
        public EnProjectMdlDto Mdl { get; set; }
        public List<EnProjectMdlHistoryDto> History { get; set; } = new List<EnProjectMdlHistoryDto>();
        public EnMdlPermitDto Permit { get; set; }

        /// <summary>The Persian line that says whether the tender package may be released as far as the project MDL is concerned (null when it may).</summary>
        public string ReleaseLockText { get; set; }

        public bool CanDraft { get; set; }
        public string DraftBlockedReason { get; set; }

        /// <summary>A person may create the list from a file only when there is no approved standard list to draft from.</summary>
        public bool CanDraftFromFile { get; set; }
        public string DraftFromFileBlockedReason { get; set; }

        /// <summary>True when the case's standard has an approved standard list (the agent drafts from it).</summary>
        public bool HasStandardList { get; set; }
        public bool CanUpload { get; set; }
        public string UploadBlockedReason { get; set; }
        public bool CanCheck { get; set; }
        public string CheckBlockedReason { get; set; }
        public bool CanReturn { get; set; }
        public string ReturnBlockedReason { get; set; }
        public bool CanApprove { get; set; }
        public string ApproveBlockedReason { get; set; }
        public bool CanVoid { get; set; }
        public string VoidBlockedReason { get; set; }
        public bool CanRecordClientApproval { get; set; }
        public string RecordClientApprovalBlockedReason { get; set; }
        public bool CanRequestPermit { get; set; }
        public string RequestPermitBlockedReason { get; set; }
        public bool CanDecidePermit { get; set; }
        public string DecidePermitBlockedReason { get; set; }
        public bool CanWithdrawPermit { get; set; }
        public string WithdrawPermitBlockedReason { get; set; }
    }

    /// <summary>One body for every action of the project MDL: draft (CaseId), check, approve (Id), return and void (Id, Reason), record-client-approval (Id, LetterRef, DocumentId, Note on a correction),
    /// request-permit (CaseId, Reason), decide-permit (PermitId, Approve, Note), withdraw-permit (PermitId, Note). <see cref="Action"/> is set by the route.</summary>
    public class EnProjectMdlActionDto
    {
        public string Action { get; set; }
        public int? Id { get; set; }
        public int? CaseId { get; set; }
        public int? PermitId { get; set; }
        public string Reason { get; set; }
        public string Note { get; set; }
        public string LetterRef { get; set; }
        public Guid? DocumentId { get; set; }
        public bool Approve { get; set; }
        public string RowVersion { get; set; }
    }

    /// <summary>The CSV of the current not-removed lines (the format the reference upload reads).</summary>
    public class EnProjectMdlCsvDto
    {
        public string FileName { get; set; }
        public byte[] Content { get; set; }
    }
}
