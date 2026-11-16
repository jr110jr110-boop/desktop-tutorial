using E = Entities.Model.NasbNiro.Engineering;

namespace DataLayer.Context
{
    /// <summary>
    /// ENG-E1 (engineering module, external workflow): the reference list, the platform dossier and its lines, the case and its history, the clarifications, the handover with its lines, the emergency waiver, the document debts and the distribution
    /// join the tamper-evident chain (Package D mechanism). A direct edit of a confirmation, an approval, a classification, a waiver decision or a debt status in the database is then a row of the chain that nobody wrote.
    /// In its own file so that it merges cleanly with the other packages that add entries to <c>FinanceAuditTrail.cs</c>.
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        private static void AddEngineering()
        {
            AuditPd(typeof(E.EnReferenceList), new[] { "Code", "Title", "StandardSource", "Revision", "Status", "PreparedByUserId", "EditorUserIds", "SubmittedAt", "ApprovedByUserId", "ApprovedAt", "ContentHash", "Note", "VoidReason", "Deleted" }, "فهرست مدارک مرجع مهندسی");
            AuditPd(typeof(E.EnReferenceLine), new[] { "ListId", "Seq", "DocumentType", "Discipline", "Title", "Required", "TenderStage", "ClientSubmittal", "StandardClause", "Note", "Units" }, "ردیف فهرست مدارک مرجع");
            AuditPd(typeof(E.EnPlatformDossier), new[] { "ProductId", "ReferenceCode", "StandardBasis", "ReferenceListId", "ReferenceRevision", "Status", "AssessedAt", "LastEditedByUserId", "EditorUserIds", "ConfirmedByUserId", "ConfirmedAt", "ConfirmedHash", "Note", "Deleted" }, "پرونده مدارک پلتفرم");
            AuditPd(typeof(E.EnPlatformDocument), new[] { "DossierId", "ReferenceLineId", "Seq", "DocumentType", "Discipline", "Title", "Required", "TenderStage", "State", "DocumentId", "ControlledDocumentId", "Revision", "NotApplicableReason", "Units", "SystemProven", "DeviationId", "SuppliedByUserId", "SuppliedAt" }, "مدرک پرونده پلتفرم");
            AuditPd(typeof(E.EnCase), new[] { "Number", "Title", "Kind", "OpinionText", "OpinionByUserId", "OpinionAt", "HandoverListCode", "InquiryId", "CustomerRequirementNote", "ApprovedGapHash", "ContractId", "Status", "Classification", "ProductId", "PlatformWithChanges", "ReferenceCode", "StandardEvidence", "StandardAttachmentId", "ChangeNote", "ProposedClassification", "ProposedProductId", "ProposedWithChanges", "ProposedChangeNote", "ProposalBasis", "ProposedByUserId", "ProposedAt", "EditorUserIds", "EngConfirmedByUserId", "EngConfirmedAt", "CeoApprovedByUserId", "CeoApprovedAt", "ClassificationHash", "ReturnReason", "TenderPackageId", "TenderReleasedByUserId", "TenderReleasedAt", "TenderHash", "ConceptNote", "CharterReviewOutcome", "CharterReviewedByUserId", "CharterReviewedAt", "CharterHash", "CharterReviewNote", "ClosedReason", "Deleted" }, "پرونده مهندسی");
            AuditPd(typeof(E.EnCaseEvent), new[] { "CaseId", "Kind", "At", "UserId", "Note" }, "رویداد پرونده مهندسی");
            AuditPd(typeof(E.EnClarification), new[] { "Number", "CaseId", "FollowsId", "Question", "QuestionAttachmentIds", "AskedByUserId", "AskedAt", "Status", "SentToClientByUserId", "SentToClientAt", "ClientLetterRef", "Answer", "AnswerAttachmentIds", "AnswerRecordedByUserId", "AnswerRecordedAt", "ClosedByUserId", "ClosedAt", "VoidReason", "Deleted" }, "ابهام‌زدایی از طریق بازرگانی");
            AuditPd(typeof(E.EnHandover), new[] { "Number", "CaseId", "ContractId", "Round", "Status", "PreparedByUserId", "EditorUserIds", "Complete", "MissingRequiredCount", "WaiverId", "SubmitConfirmedByUserId", "SubmittedAt", "AcceptedByUserId", "AcceptedAt", "ReturnedByUserId", "ReturnedAt", "ReturnReason", "ReleasedByUserId", "ReleasedAt", "ContentHash", "Note", "VoidReason", "Deleted" }, "تحویل مدارک مهندسی به PMO");
            AuditPd(typeof(E.EnHandoverLine), new[] { "HandoverId", "Seq", "DocumentType", "Discipline", "Title", "Required", "State", "DocumentId", "Revision", "PlatformDocumentId", "DeliverableId", "LinkPath", "Units", "Note" }, "ردیف تحویل مدارک مهندسی");
            AuditPd(typeof(E.EnWaiver), new[] { "Number", "HandoverId", "CaseId", "Reason", "MissingSnapshot", "DebtHours", "DebtDueAt", "Status", "RequestedByUserId", "RequestedAt", "DecidedByUserId", "DecidedAt", "DecisionNote", "UsedAt", "Deleted" }, "مجوز اضطرار قفل تحویل مدارک");
            AuditPd(typeof(E.EnDocumentDebt), new[] { "WaiverId", "HandoverLineId", "CaseId", "ContractId", "ProductId", "DocumentType", "Title", "DueAt", "Status", "SettledAt", "SettledByUserId", "SettledDocumentId", "Deleted" }, "بدهی مدرک مهندسی");
            AuditPd(typeof(E.EnDistribution), new[] { "HandoverId", "CaseId", "ContractId", "Unit", "Roles", "Status", "ScopeNote", "DocumentCount", "NotifiedAt", "NoticeSentAt", "ReceiptConfirmedByUserId", "AcknowledgedAt", "Deleted" }, "ابلاغ مدارک به واحد");
            AuditPd(typeof(E.EnDeviation), new[] { "Number", "Kind", "TargetType", "TargetId", "CaseId", "ProductId", "StandardCode", "StandardClause", "Requirement", "Instruction", "RequestedByUserId", "RequestedAt", "ManagerApprovedByUserId", "ManagerApprovedAt", "CeoApprovedByUserId", "CeoApprovedAt", "RejectedByUserId", "RejectedAt", "Status", "DecisionNote", "ContentHash", "Deleted" }, "انحراف از استاندارد");
            AuditPd(typeof(E.EnStandardGap), new[] { "Number", "CaseId", "ProductId", "BaseCode", "TargetCode", "BaseRevision", "TargetRevision", "Status", "AddedCount", "RemovedCount", "RequiredChangedCount", "ReusableCount", "Summary", "EngineeringImpactNote", "ConfirmedByUserId", "ConfirmedAt", "ContentHash", "Deleted" }, "گزارش تفاوت استاندارد");
            AuditPd(typeof(E.EnPlatformRegistration), new[] { "Number", "CaseId", "ProductId", "ReferenceCode", "StandardBasis", "Rationale", "ContentHash", "Status", "ProposedByUserId", "ProposedAt", "ApprovedByUserId", "ApprovedAt", "RejectedByUserId", "RejectedAt", "DecisionNote", "DossierId", "Deleted" }, "ثبت طرح جدید به عنوان پلتفرم");
            AuditPd(typeof(E.EnInfoRequest), new[] { "Number", "HandoverId", "CaseId", "ContractId", "Unit", "Question", "AskedByUserId", "AskedAt", "Status", "Answer", "AnsweredByUserId", "AnsweredAt", "ClosedByUserId", "ClosedAt", "Deleted" }, "پرسش واحد از PMO دربارهٔ مدارک مهندسی");
            AuditPd(typeof(E.EnStandardGapLine), new[] { "GapId", "Seq", "Kind", "DocumentType", "Discipline", "Title", "BaseRequired", "TargetRequired", "ReusableFromDossierLineId" }, "ردیف گزارش تفاوت استاندارد");
        }
    }
}
