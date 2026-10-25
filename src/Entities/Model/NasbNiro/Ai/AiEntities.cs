using System;
using System.Collections.Generic;

namespace Entities.Model.NasbNiro.Ai
{
    // Local AI (release A1). All tables start with "Ai". The AI works for the logged-in user through the existing services; numbers come from PMIS, never from the model.

    public enum AiEndpointKind
    {
        Chat = 1,
        Embeddings = 2,
        Stt = 3,
        Tts = 4,
    }

    public enum AiMessageRole
    {
        User = 1,
        Assistant = 2,
        Tool = 3,
    }

    public enum AiSeatRunStatus
    {
        Running = 1,
        Succeeded = 2,
        Failed = 3,
        Skipped = 4,
    }

    public enum AiNoteStatus
    {
        New = 1,
        Accepted = 2,
        Dismissed = 3,
    }

    /// <summary>An OpenAI-compatible inference endpoint on the company's own server (no key: local network only).</summary>
    public class AiModelEndpoint
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public AiEndpointKind Kind { get; set; }

        /// <summary>Base URL without the /v1 part, for example http://127.0.0.1:5091.</summary>
        public string Url { get; set; }
        public string ModelName { get; set; }

        /// <summary>Lower is tried first.</summary>
        public int Priority { get; set; }
        public bool Enabled { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
        public DateTimeOffset? LastHealthAt { get; set; }
        public bool? LastHealthOk { get; set; }
        public string LastError { get; set; }
    }

    /// <summary>A versioned system prompt. One version per code is active.</summary>
    public class AiPromptVersion
    {
        public int Id { get; set; }

        /// <summary>For example CHAT.</summary>
        public string Code { get; set; }
        public int Version { get; set; }
        public string Text { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
        public int? ActivatedByUserId { get; set; }
    }

    public class AiConversation
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastAt { get; set; }
        public ICollection<AiMessage> Messages { get; set; }
    }

    public class AiMessage
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public AiConversation Conversation { get; set; }
        public AiMessageRole Role { get; set; }
        public string Text { get; set; }

        /// <summary>Set on a tool message.</summary>
        public string ToolName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>Assistant messages: did every number pass the numeric check. Null on other messages.</summary>
        public bool? Verified { get; set; }
        public string Model { get; set; }
    }

    /// <summary>One tool call of the model. The result itself is not kept here: only its hash and length.</summary>
    public class AiToolCallLog
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public AiConversation Conversation { get; set; }
        public int? MessageId { get; set; }
        public AiMessage Message { get; set; }
        public string ToolName { get; set; }
        public string ArgumentsJson { get; set; }

        /// <summary>SHA-256 (hex) of the result text the model received.</summary>
        public string ResultHash { get; set; }
        public int ResultChars { get; set; }
        public bool Allowed { get; set; }
        public int DurationMs { get; set; }
    }

    /// <summary>Append-only: rows are never changed or deleted (the data layer refuses it).</summary>
    public class AiAuditEvent
    {
        public int Id { get; set; }
        public DateTimeOffset At { get; set; }
        public int UserId { get; set; }
        public string SeatCode { get; set; }

        /// <summary>chat, voice, tool, unverified, degraded, failover or admin.</summary>
        public string Kind { get; set; }
        public int? PromptVersionId { get; set; }
        public string Model { get; set; }
        public int? LatencyMs { get; set; }
        public string Detail { get; set; }
        public string PromptHash { get; set; }
    }

    /// <summary>A chunk of an indexed source (controlled document, approved minutes) with its embedding (float32, little endian).</summary>
    public class AiDocumentChunk
    {
        public int Id { get; set; }
        public string SourceType { get; set; }
        public int SourceId { get; set; }
        public string Revision { get; set; }
        public int ChunkIndex { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public byte[] Embedding { get; set; }
        public int Dim { get; set; }
        public string Model { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    // ------------------------------------------------------------------------------------------ release A1 part B (logic follows; tables are created together)

    public class AiSeat
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>The dedicated service user the seat runs as.</summary>
        public string ServiceUserName { get; set; }

        /// <summary>The only roles the seat acts with (comma separated).</summary>
        public string Roles { get; set; }
        public string Schedule { get; set; }
        public string PromptVersionCode { get; set; }
        public bool Enabled { get; set; }
        public string OutputKind { get; set; }
    }

    public class AiSeatRun
    {
        public int Id { get; set; }
        public int SeatId { get; set; }
        public AiSeat Seat { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public AiSeatRunStatus Status { get; set; }
        public string Outputs { get; set; }
        public string Error { get; set; }

        /// <summary>The data day the run was made for.</summary>
        public DateTime? AsOf { get; set; }

        /// <summary>Started by the scheduler (only these runs decide whether a scheduled slot is still due; a run by hand for another day does not).</summary>
        public bool IsScheduled { get; set; }
    }

    public class AiEvalCase
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Question { get; set; }
        public string ExpectedTools { get; set; }
        public string ExpectedFacts { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public class AiEvalRun
    {
        public int Id { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public int Total { get; set; }
        public int Passed { get; set; }
        public int NumericFailures { get; set; }
        public int P50Ms { get; set; }
        public int P95Ms { get; set; }
        public string Model { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>
    /// What a seat leaves in the gate of a module: a memo, a suggestion, a finding. It is never a final record: a human accepts or dismisses it and acts in the module.
    /// One note per seat and target (the unique index makes the seats idempotent).
    /// </summary>
    public class AiNote
    {
        public int Id { get; set; }
        public string SeatCode { get; set; }

        /// <summary>For example PurchaseRfq, QualityNcr, FinanceJournal, BankLine, Supplier, OfficeLetter, OfficeMeeting, PcWeeklyReport, CeoBrief.</summary>
        public string TargetType { get; set; }

        /// <summary>The record the note is about (the day as yyyymmdd for the CEO brief).</summary>
        public int? TargetId { get; set; }

        /// <summary>The rule that raised the note (FIN-ANOMALY: DUPLICATE, ROUND, OFFDAY, UNMATCHED, IBAN); null for the other seats. A record can have one note per rule.</summary>
        public string RuleCode { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public AiNoteStatus Status { get; set; } = AiNoteStatus.New;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecisionNote { get; set; }

        /// <summary>The text passed the numeric check (or is built from the data by a template).</summary>
        public bool Verified { get; set; }
        public int? SeatRunId { get; set; }
    }
}
