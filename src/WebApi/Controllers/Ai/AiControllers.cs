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
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            byte[] audio = stream.ToArray();
            if (!AiAudioCheck.IsAllowed(file.ContentType, audio)) return Ok(ResponseDtoHelper.BadRequest<AiVoiceResponseDto>(AiAudioCheck.Message));
            return Ok(await _chat.VoiceAsync(audio, Path.GetFileName(file.FileName), file.ContentType, conversationId, ct));
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

    /// <summary>
    /// What the seats left for people, and the CEO's brief. A note is read and decided only by the people of the module it belongs to (the service decides);
    /// accepting a note never changes the record it is about.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/ai")]
    public class AiNotesController : ControllerBase
    {
        private readonly IAiNoteService _notes;

        public AiNotesController(IAiNoteService notes)
        {
            _notes = notes;
        }

        [HttpGet("notes")]
        public async Task<IActionResult> NotesAsync([FromQuery] AiNoteQuery query, CancellationToken ct) => Ok((await _notes.GetNotesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("notes/{id:int}")]
        public async Task<IActionResult> NoteAsync(int id, CancellationToken ct) => Ok(await _notes.GetNoteAsync(id, ct));

        [HttpPost("notes/decide")]
        public async Task<IActionResult> DecideAsync([FromBody] AiDecideNoteDto dto, CancellationToken ct) => Ok(await _notes.DecideAsync(dto, ct));

        /// <summary>The latest daily brief of the CEO seat (CEO and PMO).</summary>
        [HttpGet("brief/latest")]
        public async Task<IActionResult> LatestBriefAsync(CancellationToken ct) => Ok(await _notes.LatestBriefAsync(ct));

        /// <summary>The latest brief spoken (audio/wav). Made on demand; a Persian message as a normal answer when there is none.</summary>
        [HttpGet("brief/latest/audio")]
        public async Task<IActionResult> LatestBriefAudioAsync(CancellationToken ct)
        {
            var result = await _notes.LatestBriefAudioAsync(ct);
            if (!result.Success) return Ok(result);
            return File(result.Data, "audio/wav");
        }
    }

    /// <summary>The seats (PMO runs and switches them, PMO and CEO read) and the evaluation.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/ai")]
    public class AiSeatsController : ControllerBase
    {
        private readonly IAiSeatAdminService _seats;
        private readonly IAiEvalService _eval;

        public AiSeatsController(IAiSeatAdminService seats, IAiEvalService eval)
        {
            _seats = seats;
            _eval = eval;
        }

        [HttpGet("admin/seats")]
        public async Task<IActionResult> SeatsAsync(CancellationToken ct) => Ok(await _seats.GetSeatsAsync(ct));

        [HttpPost("admin/seats/enable")]
        public async Task<IActionResult> EnableAsync([FromBody] AiEnableSeatDto dto, CancellationToken ct) => Ok(await _seats.EnableAsync(dto, ct));

        [HttpPost("admin/seats/run")]
        public async Task<IActionResult> RunAsync([FromBody] AiRunSeatDto dto, CancellationToken ct) => Ok(await _seats.RunAsync(dto, ct));

        [HttpGet("admin/seats/runs/{id:int}")]
        public async Task<IActionResult> RunAsync(int id, CancellationToken ct) => Ok(await _seats.GetRunAsync(id, ct));

        [HttpGet("admin/seats/runs")]
        public async Task<IActionResult> RunsAsync([FromQuery] AiSeatRunQuery query, CancellationToken ct) => Ok((await _seats.GetRunsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("admin/eval/cases")]
        public async Task<IActionResult> EvalCasesAsync(CancellationToken ct) => Ok(await _eval.GetCasesAsync(ct));

        [HttpPost("admin/eval/run")]
        public async Task<IActionResult> EvalRunAsync(CancellationToken ct) => Ok(await _eval.RunAsync(ct));

        [HttpGet("admin/eval/runs")]
        public async Task<IActionResult> EvalRunsAsync([FromQuery] AiEvalRunQuery query, CancellationToken ct) => Ok((await _eval.GetRunsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("admin/eval/runs/{id:int}")]
        public async Task<IActionResult> EvalRunAsync(int id, CancellationToken ct) => Ok(await _eval.GetRunAsync(id, ct));
    }
}
