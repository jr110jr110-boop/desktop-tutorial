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
        public int CreatedByUserId { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
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
        public string SeatCode { get; set; }
        public string Kind { get; set; }
        public int? PromptVersionId { get; set; }
        public string Model { get; set; }
        public int? LatencyMs { get; set; }
        public string Detail { get; set; }
        public string PromptHash { get; set; }
    }
}
