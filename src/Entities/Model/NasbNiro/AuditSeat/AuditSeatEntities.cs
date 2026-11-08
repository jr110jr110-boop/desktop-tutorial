using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.AuditSeat
{
    // Package 9 (AI internal auditor, ISO 19011): the audit programme, the runs of the AUDIT seat, the findings with their closure chain, the reports and the process audits.
    // Tables start with "Aus". Class names start with "Aus" (the quality module already has Audit, AuditProgram and AuditFinding for the manual internal audit of the QMS).
    // Document references are plain ids and numbers (the objects are of many tables); the rows are history: nothing is deleted, a finding is voided with three persons.

    /// <summary>Order matters: a recurrence raises the severity by one level (Observation to Minor to Major).</summary>
    public enum AusSeverity
    {
        Observation = 1,
        Minor = 2,
        Major = 3,
    }

    public enum AusFindingStatus
    {
        /// <summary>Waits for the action of the owner.</summary>
        Open = 1,

        /// <summary>The owner proposed root cause, correction and corrective action; the automatic re-check is next.</summary>
        ActionProposed = 2,

        /// <summary>The rule ran again on the evidence and the deviation is gone.</summary>
        Rechecked = 3,

        /// <summary>Closure proposed by the QA manager.</summary>
        QaProposed = 4,

        /// <summary>Closure proposed by the PMO manager (a different person).</summary>
        PmoProposed = 5,

        /// <summary>Approved by the CEO (a third person).</summary>
        Closed = 6,

        /// <summary>Void: QA, PMO and the CEO (three persons) agreed that the finding was never a deviation. The row stays.</summary>
        Voided = 7,
    }

    public enum AusFindingSource
    {
        /// <summary>Found by a rule of the catalogue (the AUDIT seat).</summary>
        Rule = 1,

        /// <summary>Entered by a human auditor in a process audit.</summary>
        Manual = 2,
    }

    public enum AusRunKind
    {
        Daily = 1,
        OnDemand = 2,
    }

    public enum AusRunStatus
    {
        Running = 0,
        Ok = 1,

        /// <summary>The run finished but a rule failed or was cut by its time budget (a finding about the run itself is raised).</summary>
        Partial = 2,
        Failed = 3,
    }

    public enum AusProgramStatus
    {
        Draft = 0,

        /// <summary>Approved by the QA manager (the preparer is another person).</summary>
        Approved = 1,
        Superseded = 2,
        Rejected = 3,
    }

    public enum AusFrequency
    {
        /// <summary>Automatic daily checks by the seat.</summary>
        Continuous = 0,
        Monthly = 1,
        Quarterly = 2,
        HalfYearly = 3,
        Yearly = 4,
    }

    public enum AusAuditStatus
    {
        Planned = 0,
        InProgress = 1,
        Completed = 2,
        Cancelled = 3,
    }

    public enum AusCheckResult
    {
        Pending = 0,
        Conform = 1,
        NonConform = 2,
        NotApplicable = 3,
    }

    public enum AusReportKind
    {
        Daily = 1,
        Weekly = 2,
        Monthly = 3,
    }

    /// <summary>The audit programme of one year (ISO 19011 5.x): scope, criteria and the frequency per process. Prepared by the PMO, approved by the QA manager (another person). An approved programme is never edited: a new version supersedes it.</summary>
    public class AusProgram : BaseEntity
    {
        /// <summary>Persian year.</summary>
        public int Year { get; set; }

        public int Version { get; set; }
        public string Title { get; set; }

        /// <summary>The processes and modules the programme covers.</summary>
        public string Scope { get; set; }

        /// <summary>Standards and clauses and the company procedures the audits are measured against.</summary>
        public string Criteria { get; set; }

        public string Objectives { get; set; }
        public AusProgramStatus Status { get; set; }
        public int PreparedByUserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }

        /// <summary>SHA-256 of the content at approval.</summary>
        public string ContentSha256 { get; set; }

        public int? SupersedesId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>One process of the programme: how often it is audited and by what.</summary>
    public class AusProgramItem : BaseEntity
    {
        public int ProgramId { get; set; }
        public AusProgram Program { get; set; }

        /// <summary>The process code of the catalogue (for example QUALITY, PROCUREMENT).</summary>
        public string ProcessCode { get; set; }

        public string Title { get; set; }
        public AusFrequency Frequency { get; set; }

        /// <summary>Days between two audits (1 for continuous).</summary>
        public int IntervalDays { get; set; }

        public string Criteria { get; set; }
        public DateTime? FirstDue { get; set; }
    }

    /// <summary>The state of one rule: when it ran, how long it took, whether it was cut. One row per rule.</summary>
    public class AusRuleState : BaseEntity
    {
        public string RuleCode { get; set; }
        public DateTimeOffset? LastRunAt { get; set; }

        /// <summary>The last pass without a time window (event rules run incrementally in between).</summary>
        public DateTimeOffset? LastFullAt { get; set; }

        public int LastDurationMs { get; set; }
        public int LastHits { get; set; }

        /// <summary>ok, timeout, truncated or error.</summary>
        public string LastStatus { get; set; }

        public string LastError { get; set; }
        public int TotalRuns { get; set; }
    }

    /// <summary>One run of the catalogue (the nightly run of the seat or a run on demand).</summary>
    public class AusRun : BaseEntity
    {
        public AusRunKind Kind { get; set; }
        public AusRunStatus Status { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public int RequestedByUserId { get; set; }
        public int RulesRun { get; set; }
        public int RulesFailed { get; set; }
        public int Hits { get; set; }
        public int NewFindings { get; set; }
        public int Recurrences { get; set; }
        public int Rechecked { get; set; }
        public int RecheckFailed { get; set; }

        /// <summary>One entry per rule: code, milliseconds, hits, status.</summary>
        public string ResultsJson { get; set; }

        public string Message { get; set; }
    }

    /// <summary>One deviation. The evidence is an immutable snapshot (JSON + SHA-256) taken at detection.</summary>
    public class AusFinding : BaseEntity
    {
        /// <summary>AF-{Persian year}-{sequence}.</summary>
        public string Number { get; set; }

        public int Year { get; set; }
        public int Sequence { get; set; }
        public AusFindingSource Source { get; set; }

        /// <summary>The rule of the catalogue; MANUAL for a finding of a human auditor.</summary>
        public string RuleCode { get; set; }

        public string Standard { get; set; }
        public string Clause { get; set; }
        public string Procedure { get; set; }
        public string ProcessCode { get; set; }
        public AusSeverity Severity { get; set; }

        /// <summary>The default severity of the rule (the severity above is higher after a recurrence).</summary>
        public AusSeverity BaseSeverity { get; set; }

        public string Title { get; set; }
        public string Description { get; set; }

        /// <summary>The identity of the deviation: object type, id and sub key. One open finding per (rule, object key).</summary>
        public string ObjectType { get; set; }

        public int ObjectId { get; set; }
        public string ObjectKey { get; set; }
        public string ObjectNumber { get; set; }
        public string Link { get; set; }

        public string EvidenceJson { get; set; }
        public string EvidenceSha256 { get; set; }

        public int? OwnerUserId { get; set; }
        public string OwnerRole { get; set; }
        public string OwnerUnit { get; set; }
        public DateTime DueDate { get; set; }
        public DateTimeOffset DetectedAt { get; set; }
        public int? RunId { get; set; }
        public DateTimeOffset LastSeenAt { get; set; }
        public int SeenCount { get; set; }

        public AusFindingStatus Status { get; set; }

        // the action of the owner
        public string RootCause { get; set; }
        public string Correction { get; set; }
        public string CorrectiveAction { get; set; }
        public int? CapaId { get; set; }
        public int? ActionByUserId { get; set; }
        public DateTimeOffset? ActionAt { get; set; }

        // the automatic re-check
        public DateTimeOffset? RecheckAt { get; set; }
        public bool? RecheckPassed { get; set; }
        public string RecheckSha256 { get; set; }

        // the closure chain: QA, then PMO, then the CEO; three distinct persons, none of them the owner or the actioner
        public int? QaUserId { get; set; }
        public DateTimeOffset? QaAt { get; set; }
        public string QaNote { get; set; }
        public int? PmoUserId { get; set; }
        public DateTimeOffset? PmoAt { get; set; }
        public string PmoNote { get; set; }
        public int? CeoUserId { get; set; }
        public DateTimeOffset? CeoAt { get; set; }
        public string CeoNote { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }

        // void: the same three persons
        public string VoidReason { get; set; }
        public int? VoidQaUserId { get; set; }
        public int? VoidPmoUserId { get; set; }
        public int? VoidCeoUserId { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }

        // recurrence
        public int? RecurrenceOfId { get; set; }
        public int RecurrenceCount { get; set; }

        /// <summary>The process audit that entered a manual finding.</summary>
        public int? AuditId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>The history of a finding: every step, refusal and re-check, with the person (0 = the system / the seat).</summary>
    public class AusFindingEvent : BaseEntity
    {
        public int FindingId { get; set; }
        public DateTimeOffset At { get; set; }
        public string Kind { get; set; }
        public int UserId { get; set; }
        public AusFindingStatus FromStatus { get; set; }
        public AusFindingStatus ToStatus { get; set; }
        public string Note { get; set; }
    }

    /// <summary>A report (daily digest, weekly to PMO and QA, monthly to the CEO). Stored once and never changed: a later report is a new row.</summary>
    public class AusReport : BaseEntity
    {
        public AusReportKind Kind { get; set; }
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo { get; set; }
        public string Title { get; set; }
        public string Recipients { get; set; }

        /// <summary>All numbers of the report as JSON.</summary>
        public string ContentJson { get; set; }

        public string Narrative { get; set; }

        /// <summary>template or llm (the model's text passed the number check).</summary>
        public string NarrativeSource { get; set; }

        /// <summary>SHA-256 of the content and the narrative.</summary>
        public string Sha256 { get; set; }

        public DateTimeOffset GeneratedAt { get; set; }
        public int GeneratedByUserId { get; set; }
        public int? RunId { get; set; }
    }

    /// <summary>A process audit by a human auditor, supported by the seat (checklist from the catalogue). The auditor is never the auditee.</summary>
    public class AusAudit : BaseEntity
    {
        public string Number { get; set; }
        public int? ProgramItemId { get; set; }
        public string ProcessCode { get; set; }
        public string Title { get; set; }
        public DateTime PlannedDate { get; set; }
        public int LeadAuditorUserId { get; set; }
        public int? AuditeeUserId { get; set; }
        public string AuditeeUnit { get; set; }
        public AusAuditStatus Status { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string Summary { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>One line of the checklist of a process audit (generated from the catalogue).</summary>
    public class AusAuditCheck : BaseEntity
    {
        public int AuditId { get; set; }
        public AusAudit Audit { get; set; }
        public int Sort { get; set; }
        public string RuleCode { get; set; }
        public string Standard { get; set; }
        public string Clause { get; set; }
        public string Question { get; set; }

        /// <summary>How many findings of the rule are open when the checklist was made.</summary>
        public int OpenFindings { get; set; }

        public AusCheckResult Result { get; set; }
        public string Note { get; set; }
        public int? FindingId { get; set; }
        public DateTimeOffset? CheckedAt { get; set; }
        public int? CheckedByUserId { get; set; }
    }
}
