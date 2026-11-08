using Entities.Model.NasbNiro.ProductBank;

namespace DataLayer.Context
{
    /// <summary>
    /// Package A1 (design and development control): the design plan, the design input register and the design reviews join the tamper-evident chain (Package D mechanism).
    /// A direct edit of an approval, an acceptance criterion, a review outcome or a finding status in the database is then a row of the chain that nobody wrote.
    /// In its own file so that it merges cleanly with the other packages that add entries to <c>FinanceAuditTrail.cs</c>.
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        private static void AddDesignControl()
        {
            AuditPd(typeof(PbDesignPlan), new[] { "ProductId", "ProductRevision", "Version", "Number", "Title", "Scope", "Status", "ValidationRequired", "ValidationWaiverReason", "PreparedByUserId", "SubmittedAt", "ApprovedByUserId", "ApprovedAt", "ReturnNote", "InputsCompleteByUserId", "InputsCompleteAt", "InputsCompleteNote", "NotApplicableKinds", "SupersedesPlanId", "SupersededAt", "VoidReason", "ContentHash", "Deleted" }, "برنامه طراحی");
            AuditPd(typeof(PbDesignStage), new[] { "PlanId", "Seq", "Kind", "Title", "ResponsibleRole", "ResponsibleUserId", "PlannedStart", "PlannedEnd", "Status", "ActualEnd", "CompletedByUserId", "Note", "Deleted" }, "مرحله برنامه طراحی");
            AuditPd(typeof(PbDesignInterface), new[] { "PlanId", "FromGroup", "ToGroup", "Subject", "ResponsibleRole", "ResponsibleUserId", "Deleted" }, "ارتباط گروه‌ها در برنامه طراحی");
            AuditPd(typeof(PbDesignInput), new[] { "ProductId", "ProductRevision", "Code", "Kind", "Statement", "Source", "AcceptanceCriterion", "StandardEdition", "PreviousDesignRef", "RefDeliverableId", "RefRevisionId", "RefText", "RefRevisionText", "FailureConsequence", "Status", "ConflictWithInputId", "ConflictNote", "ConflictMarkedByUserId", "ConflictMarkedAt", "ConflictResolvedByUserId", "ConflictResolvedAt", "ConflictResolution", "SupersedesInputId", "VoidReason", "Deleted" }, "ورودی طراحی");
            AuditPd(typeof(PbDesignReview), new[] { "PlanId", "ProductId", "ProductRevision", "Number", "Type", "Stage", "Title", "ScheduledDate", "Status", "HeldDate", "HeldByUserId", "HeldAt", "Minutes", "Outcome", "OutcomeNote", "ClosedByUserId", "ClosedAt", "PreviousReviewId", "VoidReason", "ValidationMethod", "ValidationEvidence", "CustomerAcceptanceRef", "CustomerAcceptanceNotApplicable", "CustomerAcceptanceNote", "Deleted" }, "بازبینی طراحی");
            AuditPd(typeof(PbDesignReviewParticipant), new[] { "ReviewId", "UserId", "RoleText", "Independent", "Deleted" }, "شرکت‌کننده بازبینی طراحی");
            AuditPd(typeof(PbDesignReviewInput), new[] { "ReviewId", "InputId", "Deleted" }, "ورودی پوشش‌داده‌شده بازبینی طراحی");
            AuditPd(typeof(PbDesignReviewOutput), new[] { "ReviewId", "Kind", "DeliverableId", "RevisionId", "BomHeaderId", "OpcRoutingId", "Label", "RevisionText", "ContentHash", "Deleted" }, "خروجی بازبینی طراحی");
            AuditPd(typeof(PbDesignTrace), new[] { "ReviewId", "InputId", "OutputId", "Result", "Evidence", "EvidenceRef", "JudgedByUserId", "JudgedAt", "Deleted" }, "ماتریس ردیابی طراحی");
            AuditPd(typeof(PbDesignFinding), new[] { "ReviewId", "Seq", "Description", "OwnerUserId", "DueDate", "Status", "RaisedByUserId", "DoneAt", "DoneNote", "ClosedByUserId", "ClosedAt", "CloseNote", "VoidReason", "Deleted" }, "یافته بازبینی طراحی");
        }
    }
}
