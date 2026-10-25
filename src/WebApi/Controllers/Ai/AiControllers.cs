using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Ai;
using ViewModels.NasbNiro.AiDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // Local AI (release A1). The assistant acts as the signed-in user; rule violations answer 200 with success=false like the other modules.

    /// <summary>Text and voice chat, the user's conversations. Any signed-in user.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/ai")]
    public class AiChatController : ControllerBase
    {
        public const int MaxAudioBytes = 10 * 1024 * 1024;
        private readonly IAiChatService _chat;

        public AiChatController(IAiChatService chat)
        {
            _chat = chat;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> ChatAsync([FromBody] AiChatRequestDto dto, CancellationToken ct) => Ok(await _chat.ChatAsync(dto, ct));

        /// <summary>Push-to-talk: speech to text, chat, text to speech. A webm or wav file up to 10 MB.</summary>
        [HttpPost("voice")]
        [RequestSizeLimit(MaxAudioBytes + 1024 * 1024)]
        public async Task<IActionResult> VoiceAsync([FromForm] IFormFile file, [FromForm] int? conversationId, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return Ok(ResponseDtoHelper.BadRequest<AiVoiceResponseDto>("فایل صوتی را بفرستید."));
            if (file.Length > MaxAudioBytes) return Ok(ResponseDtoHelper.BadRequest<AiVoiceResponseDto>("حجم فایل صوتی نباید از ۱۰ مگابایت بیشتر باشد."));
            string extension = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
            if (extension != ".webm" && extension != ".wav") return Ok(ResponseDtoHelper.BadRequest<AiVoiceResponseDto>("فقط فایل webm یا wav پذیرفته می‌شود."));
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            return Ok(await _chat.VoiceAsync(stream.ToArray(), Path.GetFileName(file.FileName), file.ContentType, conversationId, ct));
        }

        [HttpGet("conversations")]
        public async Task<IActionResult> ConversationsAsync([FromQuery] AiConversationQuery query, CancellationToken ct) => Ok((await _chat.GetConversationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("conversations/{id:int}")]
        public async Task<IActionResult> ConversationAsync(int id, CancellationToken ct) => Ok(await _chat.GetConversationAsync(id, ct));
    }

    /// <summary>Endpoints, prompt versions, the audit and the index (PMO writes; PMO and CEO read).</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/ai")]
    public class AiAdminController : ControllerBase
    {
        private readonly IAiAdminService _admin;

        public AiAdminController(IAiAdminService admin)
        {
            _admin = admin;
        }

        [HttpGet("admin/endpoints")]
        public async Task<IActionResult> EndpointsAsync(CancellationToken ct) => Ok(await _admin.GetEndpointsAsync(ct));

        [HttpPost("admin/endpoints")]
        public async Task<IActionResult> SaveEndpointAsync([FromBody] AiSaveEndpointDto dto, CancellationToken ct) => Ok(await _admin.SaveEndpointAsync(dto, ct));

        [HttpPost("admin/endpoints/health-check")]
        public async Task<IActionResult> HealthCheckAsync(CancellationToken ct) => Ok(await _admin.HealthCheckAsync(ct));

        [HttpGet("admin/prompts")]
        public async Task<IActionResult> PromptsAsync([FromQuery] string code, CancellationToken ct) => Ok(await _admin.GetPromptsAsync(code, ct));

        [HttpPost("admin/prompts")]
        public async Task<IActionResult> NewPromptAsync([FromBody] AiNewPromptDto dto, CancellationToken ct) => Ok(await _admin.NewPromptAsync(dto, ct));

        [HttpPost("admin/prompts/activate")]
        public async Task<IActionResult> ActivatePromptAsync([FromBody] AiActivatePromptDto dto, CancellationToken ct) => Ok(await _admin.ActivatePromptAsync(dto, ct));

        [HttpGet("admin/audit")]
        public async Task<IActionResult> AuditAsync([FromQuery] AiAuditQuery query, CancellationToken ct) => Ok((await _admin.GetAuditAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("index/rebuild")]
        public async Task<IActionResult> RebuildAsync(CancellationToken ct) => Ok(await _admin.RebuildIndexAsync(ct));
    }
}
