using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Ai;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.AiDtos
{
    // ------------------------------------------------------------------------------------------ chat

    public class AiChatRequestDto
    {
        public int? ConversationId { get; set; }

        /// <summary>1..2000 characters.</summary>
        public string Text { get; set; }

        /// <summary>Optional: where the user is in the front end (release D1, voice mode). The model is told the page and the sections it can act on.</summary>
        public AiUiContextDto UiContext { get; set; }
    }

    /// <summary>What the front end shows right now, sent with a chat or voice request so that "open this", "read the alerts" or "analyse this section" can be understood.</summary>
    public class AiUiContextDto
    {
        /// <summary>The route of the page, for example /ceo.</summary>
        public string Page { get; set; }

        /// <summary>The section keys of the page (finance, sales, projects, production, quality, inventory, procurement, service, office).</summary>
        public List<string> Sections { get; set; } = new List<string>();

        /// <summary>light or dark.</summary>
        public string Theme { get; set; }
    }

    /// <summary>
    /// A thing the front end does after the answer: the model asked for it by calling a ui_* tool (no server code ran, nothing was changed).
    /// Type is navigate (Link), openDrill (DrillKey and Params), analyze (SectionKey, empty for the whole company), read (What, and SectionKey for a section) or scroll (SectionKey).
    /// </summary>
    public class AiActionDto
    {
        public string Type { get; set; }
        public string Link { get; set; }
        public string DrillKey { get; set; }
        public Dictionary<string, string> Params { get; set; }
        public string SectionKey { get; set; }

        /// <summary>For read: section, alerts, brief or headline.</summary>
        public string What { get; set; }

        /// <summary>A short Persian text for the toast or the caption.</summary>
        public string Title { get; set; }
    }

    public class AiCitationDto
    {
        /// <summary>The kind of record, for example PurchaseOrder, ControlledDocument, Dashboard.</summary>
        public string Type { get; set; }
        public string Id { get; set; }
        public string Title { get; set; }

        /// <summary>A path of the front end, for example /sales/orders/12.</summary>
        public string Link { get; set; }
    }

    public class AiToolCallDto
    {
        public string Name { get; set; }
        public bool Allowed { get; set; }
    }

    public class AiChatResponseDto
    {
        public int ConversationId { get; set; }
        public string Answer { get; set; }
        public List<AiCitationDto> Citations { get; set; } = new List<AiCitationDto>();
        public List<AiToolCallDto> ToolCalls { get; set; } = new List<AiToolCallDto>();

        /// <summary>The ui_* tool calls of the model that succeeded, in the order they were made (empty when the answer is degraded).</summary>
        public List<AiActionDto> Actions { get; set; } = new List<AiActionDto>();

        /// <summary>
        /// WAV, base64: the answer spoken. Only set when the request carried a <see cref="AiChatRequestDto.UiContext"/> (a question typed on the voice-enabled page is spoken too);
        /// null otherwise, for a degraded answer and when the text to speech endpoint is down (the front end then reads the text itself).
        /// </summary>
        public string AudioBase64 { get; set; }
        public bool Verified { get; set; }
        public bool Degraded { get; set; }
        public string Model { get; set; }
    }

    public class AiVoiceResponseDto
    {
        public int ConversationId { get; set; }
        public string Transcript { get; set; }
        public string Answer { get; set; }

        /// <summary>WAV, base64. Empty when the speech services were not available (the answer is text only).</summary>
        public string AudioBase64 { get; set; }
        public List<AiCitationDto> Citations { get; set; } = new List<AiCitationDto>();
        public List<AiToolCallDto> ToolCalls { get; set; } = new List<AiToolCallDto>();

        /// <summary>The ui_* tool calls of the model that succeeded, in the order they were made.</summary>
        public List<AiActionDto> Actions { get; set; } = new List<AiActionDto>();
        public bool Verified { get; set; }
        public bool Degraded { get; set; }
        public string Model { get; set; }
    }

    public class AiConversationQuery : PaginationParams
    {
    }

    public class AiConversationListDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastAt { get; set; }
        public int Messages { get; set; }
    }

    public class AiMessageDto
    {
        public int Id { get; set; }
        public string Role { get; set; }
        public string Text { get; set; }
        public string ToolName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public bool? Verified { get; set; }
        public string Model { get; set; }
    }

    public class AiConversationDto : AiConversationListDto
    {
        public List<AiMessageDto> MessageList { get; set; } = new List<AiMessageDto>();
    }

    // ------------------------------------------------------------------------------------------ retrieval

    public class AiSearchHitDto
    {
        public string SourceType { get; set; }
        public int SourceId { get; set; }
        public string Revision { get; set; }
        public string Title { get; set; }
        public string Snippet { get; set; }
        public double Score { get; set; }
        public string Link { get; set; }
    }

    public class AiIndexRebuildResultDto
    {
        public int ControlledDocuments { get; set; }
        public int MeetingMinutes { get; set; }
        public int Chunks { get; set; }
        public int Failed { get; set; }
        public string Message { get; set; }
    }

    // ------------------------------------------------------------------------------------------ admin

    public class AiEndpointDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public AiEndpointKind Kind { get; set; }
        public string Url { get; set; }
        public string ModelName { get; set; }
        public int Priority { get; set; }
        public bool Enabled { get; set; }
        public int TimeoutSeconds { get; set; }
        public DateTimeOffset? LastHealthAt { get; set; }
        public bool? LastHealthOk { get; set; }
        public string LastError { get; set; }
    }

    public class AiSaveEndpointDto
    {
        public int? Id { get; set; }
        public string Name { get; set; }
        public AiEndpointKind Kind { get; set; }
        public string Url { get; set; }
        public string ModelName { get; set; }
        public int Priority { get; set; }
        public bool Enabled { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
    }

    public class AiPromptVersionDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public int Version { get; set; }
        public string Text { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string CreatedAtText { get; set; }
        public int CreatedByUserId { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
        public string ActivatedAtText { get; set; }
        public int? ActivatedByUserId { get; set; }
    }

    public class AiNewPromptDto
    {
        public string Code { get; set; }
        public string Text { get; set; }
    }

    public class AiActivatePromptDto
    {
        public int Id { get; set; }
    }

    public class AiAuditQuery : PaginationParams
    {
        public string Kind { get; set; }
        public string SeatCode { get; set; }
        public int? UserId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public class AiAuditEventDto
    {
        public int Id { get; set; }
        public DateTimeOffset At { get; set; }
        public string AtText { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string SeatCode { get; set; }
        public string Kind { get; set; }
        public int? PromptVersionId { get; set; }
        public string Model { get; set; }
        public int? LatencyMs { get; set; }
        public string Detail { get; set; }
        public string PromptHash { get; set; }
    }

    // ------------------------------------------------------------------------------------------ notes of the seats

    public class AiNoteQuery : PaginationParams
    {
        public string TargetType { get; set; }
        public int? TargetId { get; set; }
        public AiNoteStatus? Status { get; set; }
        public string Seat { get; set; }

        /// <summary>The rule that raised the note (FIN-ANOMALY: DUPLICATE, ROUND, OFFDAY, UNMATCHED, IBAN).</summary>
        public string Rule { get; set; }
    }

    public class AiNoteDto
    {
        public int Id { get; set; }
        public string SeatCode { get; set; }
        public string TargetType { get; set; }
        public int? TargetId { get; set; }
        public string RuleCode { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public AiNoteStatus Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string CreatedAtText { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecidedAtText { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecidedByName { get; set; }
        public string DecisionNote { get; set; }
        public bool Verified { get; set; }
        public int? SeatRunId { get; set; }

        /// <summary>The page of the front end that shows the record the note is about.</summary>
        public string Link { get; set; }
    }

    public class AiDecideNoteDto
    {
        public int Id { get; set; }

        /// <summary>accept or dismiss.</summary>
        public string Decision { get; set; }
        public string Note { get; set; }
    }

    public class AiBriefDto
    {
        public DateTime Date { get; set; }
        public string DateText { get; set; }
        public string Text { get; set; }
        public bool Verified { get; set; }
        public int NoteId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ seats

    public class AiSeatDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string ServiceUserName { get; set; }
        public string Roles { get; set; }
        public string Schedule { get; set; }
        public string ScheduleText { get; set; }
        public string PromptVersionCode { get; set; }
        public bool Enabled { get; set; }
        public string OutputKind { get; set; }
        public AiSeatRunDto LastRun { get; set; }
    }

    public class AiSeatRunDto
    {
        public int Id { get; set; }
        public int SeatId { get; set; }
        public string SeatCode { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public string StartedAtText { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public AiSeatRunStatus Status { get; set; }
        public string Outputs { get; set; }
        public string Error { get; set; }
        public DateTime? AsOf { get; set; }
    }

    public class AiSeatRunQuery : PaginationParams
    {
        public string Seat { get; set; }
        public AiSeatRunStatus? Status { get; set; }
    }

    public class AiRunSeatDto
    {
        public string Code { get; set; }

        /// <summary>The data day; today when empty. A day in the future needs the switch Ai:AllowFutureAsOf.</summary>
        public DateTime? AsOf { get; set; }
    }

    public class AiEnableSeatDto
    {
        public string Code { get; set; }
        public bool Enabled { get; set; }
    }

    // ------------------------------------------------------------------------------------------ evaluation

    public class AiEvalCaseDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Question { get; set; }
        public string ExpectedTools { get; set; }
        public string ExpectedFacts { get; set; }
        public bool Enabled { get; set; }
    }

    public class AiEvalFactResultDto
    {
        public string Fact { get; set; }
        public bool Ok { get; set; }
        public string Reason { get; set; }
    }

    public class AiEvalCaseResultDto
    {
        public string Code { get; set; }
        public string Question { get; set; }
        public bool Passed { get; set; }
        public string ExpectedTools { get; set; }
        public List<string> CalledTools { get; set; } = new List<string>();
        public bool ToolsOk { get; set; }
        public bool FactsOk { get; set; }
        public bool Verified { get; set; }
        public bool Degraded { get; set; }
        public int Ms { get; set; }
        public string Model { get; set; }
        public string Answer { get; set; }
        public List<AiEvalFactResultDto> Facts { get; set; } = new List<AiEvalFactResultDto>();
    }

    public class AiEvalRunDto
    {
        public int Id { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public string StartedAtText { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public int Total { get; set; }
        public int Passed { get; set; }

        /// <summary>The evaluation is still being made in the background: read the run again until this is false.</summary>
        public bool Running { get; set; }

        /// <summary>Passed divided by total, in percent (one decimal).</summary>
        public decimal PassPercent { get; set; }
        public int NumericFailures { get; set; }
        public int P50Ms { get; set; }
        public int P95Ms { get; set; }
        public string Model { get; set; }

        /// <summary>Only in the answer for one run.</summary>
        public List<AiEvalCaseResultDto> Cases { get; set; }
    }

    public class AiEvalRunQuery : PaginationParams
    {
    }
}
