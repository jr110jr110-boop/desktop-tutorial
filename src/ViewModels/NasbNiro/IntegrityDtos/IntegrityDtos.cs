using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.IntegrityDtos
{
    /// <summary>One run of the verification of the audit chain (page «سلامت سوابق»).</summary>
    public class AuditChainRunDto
    {
        public int Id { get; set; }

        /// <summary>Incremental or Full.</summary>
        public string Kind { get; set; }
        public string KindText { get; set; }
        public bool Manual { get; set; }
        public int? RequestedByUserId { get; set; }
        public string RequestedByName { get; set; }
        public DateTimeOffset StartedAt { get; set; }

        /// <summary>Persian date and time.</summary>
        public string StartedAtText { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }

        /// <summary>Ok, Broken, Error or Running (a full pass queued to the background job).</summary>
        public string Status { get; set; }
        public string StatusText { get; set; }
        public long? FromSeq { get; set; }
        public long? ToSeq { get; set; }

        /// <summary>The number of chain entries this run verified.</summary>
        public long EntriesChecked { get; set; }
        public long? HeadSeq { get; set; }
        public string HeadHash { get; set; }
        public bool KeyConfigured { get; set; }
        public long LegacyRows { get; set; }

        /// <summary>The first broken entry (all null when the chain is sound).</summary>
        public string BreakKind { get; set; }
        public long? BreakSeq { get; set; }
        public int? BreakRowId { get; set; }
        public string BreakEntityType { get; set; }
        public string BreakEntityTypeText { get; set; }

        /// <summary>For the break kind "record-drift": the column of the record that differs from the trail.</summary>
        public string BreakColumn { get; set; }
        public DateTimeOffset? BreakAt { get; set; }
        public string BreakAtText { get; set; }
        public string Message { get; set; }
    }

    public class AuditChainRunRequestDto
    {
        /// <summary>"incremental" (default) or "full".</summary>
        public string Mode { get; set; }
    }

    public class AuditChainRunQuery : ViewModels.NasbNiro.Pagination.PaginationParams
    {
        public string Status { get; set; }
    }

    public class RestoreCheckDto
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>The result of one monthly restore test (deploy/restore-test.ps1).</summary>
    public class RestoreTestDto
    {
        public int Id { get; set; }
        public string RunId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public string StartedAtText { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public bool Success { get; set; }
        public string BackupFile { get; set; }
        public DateTimeOffset? BackupTakenAt { get; set; }
        public string BackupTakenAtText { get; set; }
        public bool? MigrationsMatch { get; set; }
        public bool? ChainOk { get; set; }
        public long? ChainEntriesChecked { get; set; }
        public bool? ScratchDropped { get; set; }
        public Dictionary<string, long> RowCounts { get; set; } = new Dictionary<string, long>();
        public List<RestoreCheckDto> Checks { get; set; } = new List<RestoreCheckDto>();
        public string Message { get; set; }
    }

    public class RestoreImportDto
    {
        /// <summary>Result files found, and how many of them were new.</summary>
        public int Found { get; set; }
        public int Imported { get; set; }
        public int Skipped { get; set; }

        /// <summary>Persian text; for example why no folder is set.</summary>
        public string Message { get; set; }
    }

    /// <summary>The two lights of the CEO dashboard and of the page «سلامت سوابق».</summary>
    public class IntegritySummaryDto
    {
        /// <summary>Ok, Broken, Error or NeverRun.</summary>
        public string ChainStatus { get; set; }
        public string ChainStatusText { get; set; }
        public AuditChainRunDto LastChainRun { get; set; }
        public AuditChainRunDto LastGoodChainRun { get; set; }
        public AuditChainRunDto LastFullRun { get; set; }

        /// <summary>Ok, Failed, Stale (the last good test is older than <see cref="RestoreWarnDays"/>) or Never.</summary>
        public string RestoreStatus { get; set; }
        public string RestoreStatusText { get; set; }
        public RestoreTestDto LastRestoreTest { get; set; }
        public DateTimeOffset? LastRestoreSuccessAt { get; set; }
        public string LastRestoreSuccessAtText { get; set; }
        public int? DaysSinceRestoreSuccess { get; set; }
        public int RestoreWarnDays { get; set; }

        /// <summary>True (show red) when the chain is broken or could not be checked, or the last good restore test is older than the limit or there is none.</summary>
        public bool Red { get; set; }
        public DateTimeOffset AsOf { get; set; }

        /// <summary>The broken run that no later full pass has cleared (null when there is none).</summary>
        public AuditChainRunDto OpenBreak { get; set; }

        /// <summary>The request of «پذیرش شکست شناخته‌شده» that waits for signatures.</summary>
        public int? PendingAcceptanceId { get; set; }
    }
}

namespace ViewModels.NasbNiro.IntegrityDtos
{
    /// <summary>One security-relevant setting with its strict value and its value now.</summary>
    public class SafetyItemDto
    {
        /// <summary>ProjectControl, ProductBank, Procurement, Finance, Quality or AiSeat.</summary>
        public string Store { get; set; }
        public string Key { get; set; }
        public string Title { get; set; }
        public string Strict { get; set; }
        public string Current { get; set; }

        /// <summary>The value now differs from the strict profile.</summary>
        public bool Deviates { get; set; }

        /// <summary>It differs in the direction that weakens the control (a looser tolerance, a gate switched off).</summary>
        public bool Weaker { get; set; }
        public string Note { get; set; }
        public int? PendingRequestId { get; set; }
    }

    public class SafetyRequestDto
    {
        public string Store { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
        public string Reason { get; set; }
    }

    public class SafetyDecisionDto
    {
        public int Id { get; set; }
        public bool Approve { get; set; }
        public string Note { get; set; }
    }

    public class SafetyChangeDto
    {
        public int Id { get; set; }
        public string Store { get; set; }
        public string Key { get; set; }
        public string Title { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public bool Weakening { get; set; }
        public string Reason { get; set; }
        public int RequestedByUserId { get; set; }
        public string RequestedByName { get; set; }
        public string RequestedAtText { get; set; }
        public string Status { get; set; }
        public string StatusText { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecidedByName { get; set; }
        public string DecidedAtText { get; set; }
        public string DecisionNote { get; set; }
        public string AppliedAtText { get; set; }
    }

    public class SafetyChangeQuery : ViewModels.NasbNiro.Pagination.PaginationParams
    {
        /// <summary>Pending, Approved, Rejected or Cancelled; empty = all.</summary>
        public string Status { get; set; }
    }

    /// <summary>The check endpoint (also for the AI internal auditor): the settings that differ from the strict profile.</summary>
    public class SafetyDeviationsDto
    {
        public DateTimeOffset AsOf { get; set; }
        public int Total { get; set; }
        public int Deviations { get; set; }
        public int Weaker { get; set; }
        public List<SafetyItemDto> Items { get; set; } = new List<SafetyItemDto>();
    }
}

namespace ViewModels.NasbNiro.IntegrityDtos
{
    /// <summary>«پذیرش شکست شناخته‌شده»: a known break that three distinct persons (QA, PMO, CEO) accept.</summary>
    public class ChainBreakAcceptanceDto
    {
        public int Id { get; set; }
        public int BreakRunId { get; set; }
        public string BreakKind { get; set; }
        public long? BreakSeq { get; set; }
        public int? BreakRowId { get; set; }
        public string BreakEntityType { get; set; }
        public string BreakEntityTypeText { get; set; }
        public string BreakColumn { get; set; }
        public string Reason { get; set; }
        public int RequestedByUserId { get; set; }
        public string RequestedByName { get; set; }
        public string RequestedAtText { get; set; }
        public string QaName { get; set; }
        public string QaAtText { get; set; }
        public string PmoName { get; set; }
        public string PmoAtText { get; set; }
        public string CeoName { get; set; }
        public string CeoAtText { get; set; }
        public int Signatures { get; set; }

        /// <summary>Pending, Approved or Cancelled.</summary>
        public string Status { get; set; }
        public string StatusText { get; set; }
        public string AppliedAtText { get; set; }
        public long? AnchorSeq { get; set; }
    }

    public class ChainBreakAcceptanceRequestDto
    {
        /// <summary>The broken run.</summary>
        public int RunId { get; set; }
        public string Reason { get; set; }
    }

    public class ChainBreakAcceptanceSignDto
    {
        public int Id { get; set; }

        /// <summary>The seat the signer takes: QA, PMO or CEO (the signer must hold that role).</summary>
        public string Role { get; set; }
    }
}
