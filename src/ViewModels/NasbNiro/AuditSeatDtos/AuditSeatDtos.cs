using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.AuditSeatDtos
{
    // Package 9 (AI internal auditor). Every name starts with "Aus" so that no class name repeats one of the other modules (Swagger schema ids).
    // Lists are plain arrays, newest first, bounded by Limit (default 200, at most 1000).

    public class AusRuleDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Standard { get; set; }
        public string Clause { get; set; }
        public string Procedure { get; set; }
        public string ProcessCode { get; set; }
        public string ProcessTitle { get; set; }
        public string Severity { get; set; }
        public int SeverityValue { get; set; }
        public bool Incremental { get; set; }
        public bool Enabled { get; set; }
        public string SettingKey { get; set; }
        public string OwnerRole { get; set; }
        public string LastRunAtText { get; set; }
        public string LastStatus { get; set; }
        public int LastHits { get; set; }
        public int LastDurationMs { get; set; }
        public int TotalRuns { get; set; }
        public int OpenFindings { get; set; }
    }

    public class AusRunDto
    {
        public int Id { get; set; }
        public string Kind { get; set; }
        public string Status { get; set; }
        public int StatusValue { get; set; }
        public string StartedAtText { get; set; }
        public string FinishedAtText { get; set; }
        public int RulesRun { get; set; }
        public int RulesFailed { get; set; }
        public int Hits { get; set; }
        public int NewFindings { get; set; }
        public int Recurrences { get; set; }
        public int Rechecked { get; set; }
        public int RecheckFailed { get; set; }
        public string Message { get; set; }
        public List<AusRunRuleDto> Rules { get; set; } = new List<AusRunRuleDto>();
    }

    public class AusRunRuleDto
    {
        public string Code { get; set; }
        public string Status { get; set; }
        public int Hits { get; set; }
        public int New { get; set; }
        public int Ms { get; set; }
    }

    public class AusFindingListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string RuleCode { get; set; }
        public string Title { get; set; }
        public string Standard { get; set; }
        public string Clause { get; set; }
        public string ProcessCode { get; set; }
        public string ProcessTitle { get; set; }
        public string Severity { get; set; }
        public int SeverityValue { get; set; }
        public string Status { get; set; }
        public int StatusValue { get; set; }
        public string ObjectNumber { get; set; }
        public int? OwnerUserId { get; set; }
        public string OwnerName { get; set; }
        public string OwnerRole { get; set; }
        public string DueDateText { get; set; }
        public DateTime DueDate { get; set; }
        public bool Overdue { get; set; }
        public int AgeDays { get; set; }
        public string DetectedAtText { get; set; }
        public int RecurrenceCount { get; set; }
        public int? RecurrenceOfId { get; set; }
        public string Source { get; set; }
    }

    public class AusFindingDto : AusFindingListDto
    {
        public string Description { get; set; }
        public string Procedure { get; set; }
        public string ObjectType { get; set; }
        public int ObjectId { get; set; }
        public string Link { get; set; }
        public string EvidenceJson { get; set; }
        public string EvidenceSha256 { get; set; }
        public string OwnerUnit { get; set; }
        public int SeenCount { get; set; }
        public string LastSeenAtText { get; set; }
        public string RootCause { get; set; }
        public string Correction { get; set; }
        public string CorrectiveAction { get; set; }
        public int? CapaId { get; set; }
        public string ActionByName { get; set; }
        public string ActionAtText { get; set; }
        public string RecheckAtText { get; set; }
        public bool? RecheckPassed { get; set; }
        public string RecheckSha256 { get; set; }
        public string QaName { get; set; }
        public string QaAtText { get; set; }
        public string QaNote { get; set; }
        public string PmoName { get; set; }
        public string PmoAtText { get; set; }
        public string PmoNote { get; set; }
        public string CeoName { get; set; }
        public string CeoAtText { get; set; }
        public string CeoNote { get; set; }
        public string ClosedAtText { get; set; }
        public string VoidReason { get; set; }
        public string VoidedAtText { get; set; }
        public string NextStep { get; set; }
        public List<string> Allowed { get; set; } = new List<string>();
        public List<AusFindingEventDto> Events { get; set; } = new List<AusFindingEventDto>();
        public List<AusFindingListDto> Related { get; set; } = new List<AusFindingListDto>();
    }

    public class AusFindingEventDto
    {
        public string AtText { get; set; }
        public string Kind { get; set; }
        public string KindTitle { get; set; }
        public string UserName { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Note { get; set; }
    }

    public class AusFindingQuery
    {
        public string Status { get; set; }
        public string Severity { get; set; }
        public string ProcessCode { get; set; }
        public string RuleCode { get; set; }
        public string Text { get; set; }
        public bool? OpenOnly { get; set; }
        public bool? Overdue { get; set; }
        public bool? Mine { get; set; }
        public int? Limit { get; set; }
    }

    public class AusActionDto
    {
        public int FindingId { get; set; }
        public string RootCause { get; set; }
        public string Correction { get; set; }
        public string CorrectiveAction { get; set; }
        public int? CapaId { get; set; }
    }

    /// <summary>One step of the closure chain: step is "qa", "pmo", "ceo" or, for the void chain, "void-qa", "void-pmo", "void-ceo"; refuse sends the finding back to Open with the reason.</summary>
    public class AusStepDto
    {
        public int FindingId { get; set; }
        public string Step { get; set; }
        public bool Refuse { get; set; }
        public string Note { get; set; }
    }

    public class AusManualFindingDto
    {
        public int AuditId { get; set; }
        public int? CheckId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Standard { get; set; }
        public string Clause { get; set; }
        public int Severity { get; set; }
        public int? OwnerUserId { get; set; }
        public string ObjectNumber { get; set; }
    }

    public class AusManualRecheckDto
    {
        public int FindingId { get; set; }
        public bool Passed { get; set; }
        public string Note { get; set; }
    }

    public class AusProgramDto
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public int Version { get; set; }
        public string Title { get; set; }
        public string Scope { get; set; }
        public string Criteria { get; set; }
        public string Objectives { get; set; }
        public string Status { get; set; }
        public int StatusValue { get; set; }
        public string PreparedByName { get; set; }
        public string SubmittedAtText { get; set; }
        public string DecidedByName { get; set; }
        public string DecidedAtText { get; set; }
        public string DecisionNote { get; set; }
        public string ContentSha256 { get; set; }
        public List<AusProgramItemDto> Items { get; set; } = new List<AusProgramItemDto>();
        public List<string> Allowed { get; set; } = new List<string>();
    }

    public class AusProgramItemDto
    {
        public int? Id { get; set; }
        public string ProcessCode { get; set; }
        public string ProcessTitle { get; set; }
        public string Title { get; set; }
        public int Frequency { get; set; }
        public string FrequencyTitle { get; set; }
        public int IntervalDays { get; set; }
        public string Criteria { get; set; }
        public DateTime? FirstDue { get; set; }
        public string FirstDueText { get; set; }
        public int Rules { get; set; }
    }

    public class AusProgramSaveDto
    {
        public int? Id { get; set; }
        public int Year { get; set; }
        public string Title { get; set; }
        public string Scope { get; set; }
        public string Criteria { get; set; }
        public string Objectives { get; set; }
        public List<AusProgramItemDto> Items { get; set; } = new List<AusProgramItemDto>();
    }

    public class AusProgramActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Note { get; set; }
    }

    public class AusAuditListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string ProcessCode { get; set; }
        public string ProcessTitle { get; set; }
        public string Title { get; set; }
        public DateTime PlannedDate { get; set; }
        public string PlannedDateText { get; set; }
        public string Status { get; set; }
        public int StatusValue { get; set; }
        public int LeadAuditorUserId { get; set; }
        public string LeadAuditorName { get; set; }
        public int? AuditeeUserId { get; set; }
        public string AuditeeName { get; set; }
        public string AuditeeUnit { get; set; }
        public int Checks { get; set; }
        public int NonConform { get; set; }
    }

    public class AusAuditDto : AusAuditListDto
    {
        public string Summary { get; set; }
        public string StartedAtText { get; set; }
        public string CompletedAtText { get; set; }
        public List<AusAuditCheckDto> Items { get; set; } = new List<AusAuditCheckDto>();
        public List<string> Allowed { get; set; } = new List<string>();
    }

    public class AusAuditCheckDto
    {
        public int Id { get; set; }
        public int Sort { get; set; }
        public string RuleCode { get; set; }
        public string Standard { get; set; }
        public string Clause { get; set; }
        public string Question { get; set; }
        public int OpenFindings { get; set; }
        public int Result { get; set; }
        public string ResultTitle { get; set; }
        public string Note { get; set; }
        public int? FindingId { get; set; }
    }

    public class AusAuditSaveDto
    {
        public int? ProgramItemId { get; set; }
        public string ProcessCode { get; set; }
        public string Title { get; set; }
        public DateTime PlannedDate { get; set; }
        public int LeadAuditorUserId { get; set; }
        public int? AuditeeUserId { get; set; }
        public string AuditeeUnit { get; set; }
    }

    public class AusAuditActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Summary { get; set; }
    }

    public class AusCheckSaveDto
    {
        public int CheckId { get; set; }
        public int Result { get; set; }
        public string Note { get; set; }
    }

    public class AusReportListDto
    {
        public int Id { get; set; }
        public string Kind { get; set; }
        public int KindValue { get; set; }
        public string Title { get; set; }
        public string PeriodFromText { get; set; }
        public string PeriodToText { get; set; }
        public string GeneratedAtText { get; set; }
        public string Recipients { get; set; }
        public string Sha256 { get; set; }
        public string NarrativeSource { get; set; }
    }

    public class AusReportDto : AusReportListDto
    {
        public string Narrative { get; set; }
        public AusReportContentDto Content { get; set; }
    }

    public class AusCountDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public int Count { get; set; }
    }

    public class AusScoreDto
    {
        public string Standard { get; set; }
        public string Clause { get; set; }
        public int Rules { get; set; }
        public int Met { get; set; }
        public int Not { get; set; }
        public string Result { get; set; }
    }

    public class AusReportContentDto
    {
        public string PeriodFrom { get; set; }
        public string PeriodTo { get; set; }
        public int Open { get; set; }
        public int OpenMajor { get; set; }
        public int OpenMinor { get; set; }
        public int OpenObservation { get; set; }
        public int NewInPeriod { get; set; }
        public int ClosedInPeriod { get; set; }
        public int Overdue { get; set; }
        public int Recurrences { get; set; }
        public int NewMajor { get; set; }
        public int NewMinor { get; set; }
        public int NewObservation { get; set; }
        public int PreviousNew { get; set; }
        public int PreviousClosed { get; set; }
        public int PreviousOpen { get; set; }
        public string Trend { get; set; }
        public decimal? AvgClosureDays { get; set; }
        public int Age0To7 { get; set; }
        public int Age8To30 { get; set; }
        public int Age31To90 { get; set; }
        public int AgeOver90 { get; set; }
        public int RulesEnabled { get; set; }
        public int RulesTotal { get; set; }
        public int ScoreMet { get; set; }
        public int ScoreNot { get; set; }
        public List<AusCountDto> ByProcess { get; set; } = new List<AusCountDto>();
        public List<AusCountDto> ByStatus { get; set; } = new List<AusCountDto>();
        public List<AusCountDto> TopRules { get; set; } = new List<AusCountDto>();
        public List<AusScoreDto> Scores { get; set; } = new List<AusScoreDto>();
        public List<AusFindingListDto> MajorOpen { get; set; } = new List<AusFindingListDto>();
    }

    public class AusReportGenerateDto
    {
        public int Kind { get; set; }
        public DateTime? Day { get; set; }
    }

    public class AusStatusDto
    {
        public bool Enabled { get; set; }
        public int Open { get; set; }
        public int OpenMajor { get; set; }
        public int OpenMinor { get; set; }
        public int OpenObservation { get; set; }
        public int Overdue { get; set; }
        public int WaitingQa { get; set; }
        public int WaitingPmo { get; set; }
        public int WaitingCeo { get; set; }
        public int MyTurn { get; set; }
        public string LastRunAtText { get; set; }
        public string LastRunStatus { get; set; }
        public int RulesEnabled { get; set; }
        public int RulesTotal { get; set; }
    }

    public class AusRunRequestDto
    {
        public string Note { get; set; }
    }
}
