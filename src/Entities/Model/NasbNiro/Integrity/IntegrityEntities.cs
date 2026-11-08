using System;

namespace Entities.Model.NasbNiro.Integrity
{
    // Package D (records integrity and security hardening): the history of the chain verification, the result of the monthly restore test and the two-person change of a security setting.

    public enum IntegrityRunKind
    {
        /// <summary>From the checkpoint (the end of the last good run) to the end of the chain.</summary>
        Incremental = 1,

        /// <summary>The whole chain from its first row.</summary>
        Full = 2,
    }

    public enum IntegrityRunStatus
    {
        Ok = 1,

        /// <summary>The chain is broken: the first broken entry is on the row.</summary>
        Broken = 2,

        /// <summary>The check itself could not be done (database error, missing key).</summary>
        Error = 3,

        /// <summary>Fix round (M8): a full pass queued to the background job that has not finished yet.</summary>
        Running = 4,
    }

    /// <summary>
    /// One run of the verification of the HMAC chain of the audit trail (nightly incremental, weekly full, or on demand). Append-only history: a run is written once. The checkpoint of the next incremental run is
    /// the head (<see cref="HeadSeq"/> and <see cref="HeadHash"/>) of the last run whose <see cref="Status"/> is Ok.
    /// </summary>
    public class AuditChainRun
    {
        public int Id { get; set; }
        public IntegrityRunKind Kind { get; set; }

        /// <summary>True when a person started it (the on-demand endpoint), false when the nightly job did.</summary>
        public bool Manual { get; set; }

        public int? RequestedByUserId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public IntegrityRunStatus Status { get; set; }

        /// <summary>First and last place of the chain this run walked (null when the chain was empty).</summary>
        public long? FromSeq { get; set; }
        public long? ToSeq { get; set; }

        /// <summary>The number of chain entries that were verified (the entries before a break count).</summary>
        public long EntriesChecked { get; set; }

        /// <summary>The head of the chain when the run ended (place and seal): the checkpoint of the next incremental run, and the anchor that proves the end was not cut off.</summary>
        public long? HeadSeq { get; set; }
        public string HeadHash { get; set; }

        public bool KeyConfigured { get; set; }
        public long LegacyRows { get; set; }

        /// <summary>The first broken entry: kind of break, place in the chain, the id of the row, the table (record type) and action the row describes and its time.</summary>
        public string BreakKind { get; set; }
        public long? BreakSeq { get; set; }
        public int? BreakRowId { get; set; }
        public string BreakEntityType { get; set; }
        public DateTimeOffset? BreakAt { get; set; }

        /// <summary>Fix round (H1): for the break kind "record-drift", the column of the record that differs from the trail (BreakEntityType and BreakRowId are the record).</summary>
        public string BreakColumn { get; set; }

        /// <summary>Persian text for the person: the result in a sentence, or the error.</summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// The result of one run of deploy/restore-test.ps1 (the monthly restore test of the latest backup into a scratch database). The script runs under the machine account (the application pool never holds sysadmin)
    /// and writes a JSON file; the application reads the file and keeps one row per run here. The row is evidence, not a decision: the program never changes it afterwards.
    /// </summary>
    public class RestoreTestResult
    {
        public int Id { get; set; }

        /// <summary>The identifier the script gave the run (yyyyMMddTHHmmss); unique, so reading the same file twice imports it once.</summary>
        public string RunId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }

        /// <summary>True when every check passed (restored, migration history matches, key tables counted, audit chain verified, scratch database dropped).</summary>
        public bool Success { get; set; }

        /// <summary>The backup that was restored (file name only) and when it was made.</summary>
        public string BackupFile { get; set; }
        public DateTimeOffset? BackupTakenAt { get; set; }

        public bool? MigrationsMatch { get; set; }
        public bool? ChainOk { get; set; }
        public long? ChainEntriesChecked { get; set; }
        public bool? ScratchDropped { get; set; }

        /// <summary>JSON: the row counts of the key tables in the restored copy, and the list of checks with their result.</summary>
        public string RowCountsJson { get; set; }
        public string ChecksJson { get; set; }

        public string Message { get; set; }
        public DateTimeOffset ImportedAt { get; set; }
    }

    /// <summary>Which settings table holds the security-relevant setting.</summary>
    public enum SafetyStore
    {
        ProjectControl = 1,
        ProductBank = 2,
        Procurement = 3,
        Finance = 4,
        Quality = 5,

        /// <summary>The key is the code of an AI seat; the value is 1 (enabled) or 0 (disabled).</summary>
        AiSeat = 6,

        /// <summary>Fix round (M4): the settings of the sales module (the ceiling of a proposal the sales department approves by itself).</summary>
        Sales = 7,
    }

    public enum SafetyChangeStatus
    {
        /// <summary>Waiting for a second person (PMO or CEO, not the requester).</summary>
        Pending = 0,

        /// <summary>Approved and applied.</summary>
        Approved = 1,
        Rejected = 2,

        /// <summary>The requester withdrew it.</summary>
        Cancelled = 3,

        /// <summary>Fix round (M5): nobody decided within the time limit (72 hours); nothing was applied.</summary>
        Expired = 4,

        /// <summary>Fix round (M4): a drafting AI seat was switched OFF at once by one person; a second person has to confirm within 24 hours, otherwise the CEO is told.</summary>
        AppliedAwaitingConfirmation = 5,
    }

    /// <summary>
    /// A request to change a security-relevant setting (a setting that weakens a control when changed: see SafetyProfile). The change is applied only when a second person, a PMO or CEO user who is not the
    /// requester and never an AI seat, approves it; the request carries the reason and both people, and the setting row and this row are in the audit chain.
    /// </summary>
    public class SafetySettingChange : BaseEntity
    {
        public SafetyStore Store { get; set; }

        /// <summary>The key of the setting in its table (for an AI seat: the seat code).</summary>
        public string Key { get; set; }

        /// <summary>The value before and after, as text ("1", "0", "true", "2.5").</summary>
        public string OldValue { get; set; }
        public string NewValue { get; set; }

        /// <summary>True when the new value is further from the strict profile than the old one.</summary>
        public bool Weakening { get; set; }

        public string Reason { get; set; }
        public int RequestedByUserId { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public SafetyChangeStatus Status { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }

        /// <summary>Fix round (M4): for a change applied at once (a drafting seat switched off), the time by which a second person must confirm, and when the CEO was told that nobody did.</summary>
        public DateTimeOffset? ConfirmDueAt { get; set; }
        public DateTimeOffset? FlaggedAt { get; set; }
    }

    /// <summary>
    /// Fix round (H2): «پذیرش شکست شناخته‌شده». A known break of the chain (or a record that differs from its trail) that cannot be repaired is accepted by three distinct persons, the QA manager, the PMO and the CEO (CEO decision D12;
    /// never an AI seat). The acceptance re-anchors the verification: the walk continues after the broken place from the hash the row carries at the moment of the acceptance (a break of a record is reconciled by a new trail entry
    /// that records the current values). The acceptance is evidence and stays in the chain; nothing is deleted.
    /// </summary>
    public class ChainBreakAcceptance : BaseEntity
    {
        /// <summary>The run that found the break.</summary>
        public int BreakRunId { get; set; }
        public string BreakKind { get; set; }
        public long? BreakSeq { get; set; }
        public int? BreakRowId { get; set; }
        public string BreakEntityType { get; set; }
        public string BreakColumn { get; set; }

        public string Reason { get; set; }
        public int RequestedByUserId { get; set; }
        public DateTimeOffset RequestedAt { get; set; }

        /// <summary>The three signatures (each a different person).</summary>
        public int? QaUserId { get; set; }
        public DateTimeOffset? QaAt { get; set; }
        public int? PmoUserId { get; set; }
        public DateTimeOffset? PmoAt { get; set; }
        public int? CeoUserId { get; set; }
        public DateTimeOffset? CeoAt { get; set; }

        /// <summary>Pending, Approved (all three signed; applied) or Cancelled.</summary>
        public SafetyChangeStatus Status { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }

        /// <summary>The new start of the verification: the place and the seal of the row at the break, as they were when the third person signed (null for the break of a record).</summary>
        public long? AnchorSeq { get; set; }
        public string AnchorHash { get; set; }
    }
}
