using System;
using A = Entities.Model.NasbNiro.AuditSeat;

namespace DataLayer.Context
{
    /// <summary>
    /// Package 9 (AI internal auditor): the audit programme, the runs, the findings (with the SHA-256 of the evidence, the action, the re-check and the closure chain), the history of every finding, the reports
    /// (with their hash) and the process audits join the tamper-evident chain (Package D mechanism). The evidence JSON itself is not written to the chain (its hash is); a direct edit of it in the database is
    /// found by the hash and by the record-drift check of the other columns. In its own file so that it merges cleanly with the other packages.
    /// </summary>
    internal static partial class FinanceAuditTrail
    {
        private static void AddAuditSeat()
        {
            AuditPd(typeof(A.AusProgram), new[] { "Year", "Version", "Title", "Scope", "Criteria", "Objectives", "Status", "PreparedByUserId", "SubmittedAt", "DecidedByUserId", "DecidedAt", "DecisionNote", "ContentSha256", "SupersedesId", "Deleted" }, "برنامه ممیزی داخلی");
            AuditPd(typeof(A.AusProgramItem), new[] { "ProgramId", "ProcessCode", "Title", "Frequency", "IntervalDays", "Criteria", "FirstDue", "Deleted" }, "ردیف برنامه ممیزی (فرایند)");
            AuditPd(typeof(A.AusRuleState), new[] { "RuleCode", "LastRunAt", "LastFullAt", "LastHits", "LastStatus", "LastError", "TotalRuns", "CursorId", "Deleted" }, "وضعیت اجرای قاعده ممیزی");
            AuditPd(typeof(A.AusRun), new[] { "Kind", "Status", "StartedAt", "FinishedAt", "RequestedByUserId", "RulesRun", "RulesFailed", "Hits", "NewFindings", "Recurrences", "Rechecked", "RecheckFailed", "Message" }, "اجرای ممیزی");
            AuditPd(typeof(A.AusFinding), new[]
            {
                "Number", "Source", "RuleCode", "Standard", "Clause", "Procedure", "ProcessCode", "Severity", "BaseSeverity", "Title", "ObjectType", "ObjectId", "ObjectKey", "ObjectNumber", "EvidenceSha256",
                "OwnerUserId", "OwnerRole", "OwnerUnit", "DueDate", "DetectedAt", "RunId", "Status", "RootCause", "Correction", "CorrectiveAction", "CapaId", "ActionByUserId", "ActionAt",
                "RecheckAt", "RecheckPassed", "RecheckSha256", "QaUserId", "QaAt", "QaNote", "PmoUserId", "PmoAt", "PmoNote", "CeoUserId", "CeoAt", "CeoNote", "ClosedAt",
                "VoidReason", "VoidQaUserId", "VoidPmoUserId", "VoidCeoUserId", "VoidedAt", "RecurrenceOfId", "RecurrenceCount", "AuditId", "EscalatedAt", "Deleted",
            }, "یافته ممیزی داخلی");
            AuditPd(typeof(A.AusFindingEvent), new[] { "FindingId", "At", "Kind", "UserId", "FromStatus", "ToStatus", "Note" }, "رویداد یافته ممیزی");
            AuditPd(typeof(A.AusReport), new[] { "Kind", "PeriodFrom", "PeriodTo", "Title", "Recipients", "Sha256", "NarrativeSource", "GeneratedAt", "GeneratedByUserId", "RunId" }, "گزارش ممیزی داخلی");
            AuditPd(typeof(A.AusAudit), new[] { "Number", "ProgramItemId", "ProcessCode", "Title", "PlannedDate", "LeadAuditorUserId", "AuditeeUserId", "AuditeeUnit", "Status", "StartedAt", "CompletedAt", "Summary", "Deleted" }, "ممیزی فرایند");
            AuditPd(typeof(A.AusAcceptance), new[] { "RuleCode", "ObjectKey", "ObjectNumber", "Reason", "ExpiresOn", "Status", "QaUserId", "QaAt", "PmoUserId", "PmoAt", "CeoUserId", "CeoAt", "RefusalReason", "Deleted" }, "انحراف پذیرفته‌شده موقت ممیزی");
            AuditPd(typeof(A.AusAuditCheck), new[] { "AuditId", "Sort", "RuleCode", "Clause", "Result", "Note", "FindingId", "CheckedAt", "CheckedByUserId" }, "مورد فهرست وارسی ممیزی");
        }
    }
}
