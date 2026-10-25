using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Ai
{
    public interface IAiChatService
    {
        Task<ResponseDto<AiChatResponseDto>> ChatAsync(AiChatRequestDto dto, CancellationToken ct);

        /// <param name="audio">webm or wav, at most 10 MB (the controller checks).</param>
        Task<ResponseDto<AiVoiceResponseDto>> VoiceAsync(byte[] audio, string fileName, string contentType, int? conversationId, CancellationToken ct);
        Task<ResponseDto<PagedList<AiConversationListDto>>> GetConversationsAsync(AiConversationQuery query, CancellationToken ct);
        Task<ResponseDto<AiConversationDto>> GetConversationAsync(int id, CancellationToken ct);
    }

    /// <summary>
    /// The assistant. It acts as the signed-in user: the tools call the existing services in this request scope. The model picks tools and writes Persian text;
    /// every number of the answer must come from a tool result of this turn (see <see cref="AnswerVerifier"/>).
    /// </summary>
    public class AiChatService : IAiChatService
    {
        public const string ChatPromptCode = "CHAT";
        public const int MaxToolRounds = 6;
        public const int MemoryTurns = 10;
        public const int MaxTextLength = 2000;

        public const string SeedPromptText =
            "تو «دستیار هوشمند PMIS» شرکت هستی؛ دستیاری برای مدیران و کارکنان که اطلاعات سامانه‌ی یکپارچه‌ی مدیریت پروژه و عملیات شرکت (PMIS) را می‌خواند و توضیح می‌دهد.\n" +
            "اصول الزامی:\n" +
            "۱. هر عدد، مبلغ، تعداد، درصد یا تاریخی که در پاسخ می‌نویسی باید عیناً از نتیجه‌ی یکی از ابزارها در همین گفت‌وگو آمده باشد. هرگز عدد از خودت نساز، حدس نزن و محاسبه‌ی تازه (جمع، تفاضل، درصد) انجام نده؛ اگر عددی لازم است که ابزارها نداده‌اند، ابزار مناسب را صدا بزن یا بگو اطلاعاتش در دسترس نیست.\n" +
            "۲. برای هر پرسش درباره‌ی داده‌های شرکت ابتدا ابزار مناسب را فراخوانی کن؛ فقط از ابزارهایی استفاده کن که در فهرست تو هست.\n" +
            "۳. هرگز چیزی را تأیید، نهایی، پرداخت، آزاد یا حذف نمی‌کنی و نمی‌گویی که انجام داده‌ای؛ تأیید همیشه یک اقدام انسانی در خود سامانه است. اگر کاربر چنین چیزی خواست، توضیح بده که باید از صفحه‌ی مربوط در سامانه انجام شود.\n" +
            "۴. پاسخ کوتاه، دقیق و فارسی باشد (حداکثر چند جمله یا یک فهرست کوتاه). مبلغ‌ها را به ریال بنویس. تاریخ‌ها را همان‌طور که ابزار داده (شمسی) بنویس؛ هرگز تاریخ میلادی ننویس.\n" +
            "۵. همیشه منبع پاسخ را نام ببر: عنوان یا شماره‌ی رکوردهایی که ابزار داده است؛ پیوندها به‌صورت خودکار نمایش داده می‌شود.\n" +
            "۶. اگر نتیجه‌ی ابزار «forbidden» بود یا ابزار در دسترس نبود، فقط بگو کاربر به آن اطلاعات دسترسی ندارد و چیز دیگری حدس نزن.\n" +
            "۷. گذرواژه، شماره‌ی شبا و اطلاعات شخصی حقوق را نمی‌دانی و نمی‌نویسی.";

        private sealed class Outcome
        {
            public int ConversationId;
            public string Answer;
            public List<AiCitationDto> Citations = new List<AiCitationDto>();
            public List<AiToolCallDto> ToolCalls = new List<AiToolCallDto>();
            public bool Verified;
            public bool Degraded;
            public string Model;
        }

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ILlmClient _llm;
        private readonly IToolRegistry _tools;
        private readonly IAnswerVerifier _verifier;
        private readonly IAiAudit _audit;
        private readonly ISpeechToText _stt;
        private readonly ITextToSpeech _tts;
        private readonly IPersianNumberWords _words;
        private readonly IConfiguration _configuration;

        public AiChatService(IUnitOfWork uow, IInventoryUserContext user, ILlmClient llm, IToolRegistry tools, IAnswerVerifier verifier, IAiAudit audit,
            ISpeechToText stt, ITextToSpeech tts, IPersianNumberWords words, IConfiguration configuration)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _stt = stt ?? throw new ArgumentNullException(nameof(stt));
            _tts = tts ?? throw new ArgumentNullException(nameof(tts));
            _words = words ?? throw new ArgumentNullException(nameof(words));
            _configuration = configuration;
        }

        private int Me => _user.UserId;

        private int TimeoutSeconds
        {
            get
            {
                string text = _configuration?[AiConfigKeys.ChatTimeoutSeconds];
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s > 0 ? s : 60;
            }
        }

        /// <summary>The active prompt of a code; version 1 of CHAT is seeded on first use.</summary>
        public static async Task<AiPromptVersion> ActivePromptAsync(IUnitOfWork uow, string code, int userId, CancellationToken ct)
        {
            AiPromptVersion active = await uow.Set<AiPromptVersion>().Where(p => p.Code == code && p.IsActive).OrderByDescending(p => p.Version).FirstOrDefaultAsync(ct);
            if (active != null) return active;
            if (code != ChatPromptCode || await uow.Set<AiPromptVersion>().AnyAsync(p => p.Code == code, ct)) return null;
            DateTimeOffset now = DateTimeOffset.Now;
            active = new AiPromptVersion { Code = code, Version = 1, Text = SeedPromptText, IsActive = true, CreatedAt = now, CreatedByUserId = userId, ActivatedAt = now, ActivatedByUserId = userId };
            uow.Set<AiPromptVersion>().Add(active);
            await uow.SaveChangesAsync(ct);
            return active;
        }

        // ------------------------------------------------------------------------------------------ chat

        public async Task<ResponseDto<AiChatResponseDto>> ChatAsync(AiChatRequestDto dto, CancellationToken ct)
        {
            if (Me == 0) return AiText.Bad<AiChatResponseDto>(AiText.NotAllowed);
            string text = AiText.Clean(dto?.Text);
            if (text == null || text.Length > MaxTextLength) return AiText.Bad<AiChatResponseDto>("متن پرسش باید بین ۱ تا ۲۰۰۰ نویسه باشد.");
            (Outcome outcome, string error) = await RunAsync(text, dto.ConversationId, ct);
            if (error != null) return AiText.NotFound<AiChatResponseDto>(error);
            return ResponseDtoHelper.Ok(new AiChatResponseDto
            {
                ConversationId = outcome.ConversationId, Answer = outcome.Answer, Citations = outcome.Citations, ToolCalls = outcome.ToolCalls,
                Verified = outcome.Verified, Degraded = outcome.Degraded, Model = outcome.Model,
            });
        }

        private async Task<(Outcome Outcome, string Error)> RunAsync(string text, int? conversationId, CancellationToken ct)
        {
            var total = Stopwatch.StartNew();
            AiConversation conversation = null;
            if (conversationId.HasValue)
            {
                conversation = await _uow.Set<AiConversation>().FirstOrDefaultAsync(c => c.Id == conversationId.Value && c.UserId == Me, ct);
                if (conversation == null) return (null, "گفتگو پیدا نشد.");
            }

            AiPromptVersion prompt = await ActivePromptAsync(_uow, ChatPromptCode, Me, ct);
            string systemPrompt = prompt?.Text ?? SeedPromptText;
            string promptHash = AiText.Sha256Hex(systemPrompt + text);

            var messages = new List<LlmMessage> { new LlmMessage { Role = "system", Content = systemPrompt } };
            if (conversation != null)
            {
                // the last ten turns (a turn is a question and its answer)
                List<AiMessage> history = await _uow.Set<AiMessage>().AsNoTracking().Where(m => m.ConversationId == conversation.Id && m.Role != AiMessageRole.Tool)
                    .OrderByDescending(m => m.Id).Take(MemoryTurns * 2).ToListAsync(ct);
                foreach (AiMessage m in history.OrderBy(m => m.Id))
                    messages.Add(new LlmMessage { Role = m.Role == AiMessageRole.User ? "user" : "assistant", Content = m.Text });
            }
            messages.Add(new LlmMessage { Role = "user", Content = text });

            IReadOnlyList<AiToolDefinition> offered = _tools.Offered();
            List<LlmToolSpec> specs = offered.Select(t => new LlmToolSpec { Name = t.Name, Description = t.Description, SchemaJson = t.SchemaJson }).ToList();

            var results = new List<(AiToolResult Result, string Arguments)>();
            string model = null;
            string final = null;
            string degradedReason = null;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                try
                {
                    for (int round = 1; round <= MaxToolRounds + 1; round++)
                    {
                        // after the last tool round the model must answer: no tools are offered
                        LlmResult r = await _llm.CompleteAsync(messages, round > MaxToolRounds ? null : specs, timeout.Token);
                        if (!r.Ok) { degradedReason = r.Error ?? "no endpoint answered"; break; }
                        model = r.Model;
                        if (r.ToolCalls.Count == 0 || round > MaxToolRounds) { final = r.Content ?? string.Empty; break; }
                        messages.Add(new LlmMessage { Role = "assistant", Content = r.Content, ToolCalls = r.ToolCalls });
                        foreach (LlmToolCall call in r.ToolCalls)
                        {
                            AiToolResult tr = await _tools.ExecuteAsync(call.Name, call.ArgumentsJson, timeout.Token);
                            results.Add((tr, call.ArgumentsJson));
                            messages.Add(new LlmMessage { Role = "tool", ToolCallId = call.Id, Content = tr.Json });
                        }
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    degradedReason = $"timeout after {TimeoutSeconds} s";
                }
            }

            var outcome = new Outcome { Model = model };
            var now = DateTimeOffset.Now;
            bool degraded = degradedReason != null;
            List<VerifierToolResult> verifierInput = results.Select(x => new VerifierToolResult { Name = x.Result.Name, Title = x.Result.Title, Json = x.Result.Json }).ToList();
            VerificationResult verification = null;
            if (degraded)
            {
                outcome.Degraded = true;
                outcome.Verified = true;
                outcome.Answer = AiText.DegradedAnswer;
                outcome.Model = null;
            }
            else
            {
                string answer = string.IsNullOrWhiteSpace(final) ? "مدل پاسخی تولید نکرد؛ دوباره تلاش کنید." : final.Trim();
                verification = _verifier.Verify(answer, text, verifierInput);
                outcome.Answer = verification.Answer;
                outcome.Verified = verification.Verified;
            }
            foreach (var x in results)
            {
                outcome.ToolCalls.Add(new AiToolCallDto { Name = x.Result.Name, Allowed = x.Result.Allowed });
                foreach (AiCitationDto c in x.Result.Citations)
                    if (outcome.Citations.Count < 12 && !outcome.Citations.Any(e => e.Type == c.Type && e.Id == c.Id && e.Link == c.Link)) outcome.Citations.Add(c);
            }

            // persist: the conversation, the messages, the tool call logs and the audit rows in one save
            if (conversation == null)
            {
                conversation = new AiConversation { UserId = Me, Title = AiText.Cut(text, 80), CreatedAt = now, LastAt = now };
                _uow.Set<AiConversation>().Add(conversation);
            }
            conversation.LastAt = now;
            _uow.Set<AiMessage>().Add(new AiMessage { Conversation = conversation, Role = AiMessageRole.User, Text = text, CreatedAt = now });
            foreach (var x in results)
                _uow.Set<AiMessage>().Add(new AiMessage { Conversation = conversation, Role = AiMessageRole.Tool, Text = x.Result.Json, ToolName = x.Result.Name, CreatedAt = now });
            var assistant = new AiMessage { Conversation = conversation, Role = AiMessageRole.Assistant, Text = outcome.Answer, CreatedAt = now, Verified = degraded ? (bool?)null : outcome.Verified, Model = outcome.Model };
            _uow.Set<AiMessage>().Add(assistant);
            foreach (var x in results)
            {
                _uow.Set<AiToolCallLog>().Add(new AiToolCallLog
                {
                    Conversation = conversation, Message = assistant, ToolName = x.Result.Name, ArgumentsJson = AiText.Cut(x.Arguments, 4000),
                    ResultHash = x.Result.Hash, ResultChars = x.Result.Chars, Allowed = x.Result.Allowed, DurationMs = x.Result.DurationMs,
                });
                _audit.Add(AiAuditKinds.Tool, $"{x.Result.Name} {AiText.Cut(x.Arguments, 500)} allowed={x.Result.Allowed} chars={x.Result.Chars} hash={x.Result.Hash}", model, x.Result.DurationMs, prompt?.Id, promptHash);
            }
            total.Stop();
            string tools = string.Join(",", results.Select(x => x.Result.Name));
            if (degraded) _audit.Add(AiAuditKinds.Degraded, AiText.Cut(degradedReason, 1000), null, (int)total.ElapsedMilliseconds, prompt?.Id, promptHash);
            if (verification != null && !verification.Verified)
                _audit.Add(AiAuditKinds.Unverified, "tokens: " + string.Join(" ", verification.OffendingTokens) + "; tools: " + tools, model, (int)total.ElapsedMilliseconds, prompt?.Id, promptHash);
            _audit.Add(AiAuditKinds.Chat, $"tools: {(tools.Length == 0 ? "-" : tools)}; verified={(degraded ? "n/a" : outcome.Verified.ToString().ToLowerInvariant())}; degraded={degraded.ToString().ToLowerInvariant()}", model, (int)total.ElapsedMilliseconds, prompt?.Id, promptHash);
            await _uow.SaveChangesAsync(ct);

            outcome.ConversationId = conversation.Id;
            return (outcome, null);
        }

        // ------------------------------------------------------------------------------------------ voice

        public async Task<ResponseDto<AiVoiceResponseDto>> VoiceAsync(byte[] audio, string fileName, string contentType, int? conversationId, CancellationToken ct)
        {
            if (Me == 0) return AiText.Bad<AiVoiceResponseDto>(AiText.NotAllowed);
            if (audio == null || audio.Length == 0) return AiText.Bad<AiVoiceResponseDto>("فایل صوتی خالی است.");
            var total = Stopwatch.StartNew();
            var dto = new AiVoiceResponseDto();

            SttResult stt = await _stt.TranscribeAsync(audio, fileName, contentType, ct);
            if (!stt.Ok || string.IsNullOrWhiteSpace(stt.Text))
            {
                dto.Answer = AiText.VoiceNoTranscript;
                dto.Degraded = true;
                dto.Verified = true;
                dto.ConversationId = conversationId ?? 0;
                await _audit.LogAsync(AiAuditKinds.Voice, stt.Ok ? "speech to text returned no text" : "speech to text endpoint unavailable", ct, latencyMs: (int)total.ElapsedMilliseconds);
                return ResponseDtoHelper.Ok(dto);
            }

            dto.Transcript = AiText.Cut(stt.Text.Trim(), MaxTextLength);
            (Outcome outcome, string error) = await RunAsync(dto.Transcript, conversationId, ct);
            if (error != null) return AiText.NotFound<AiVoiceResponseDto>(error);
            dto.ConversationId = outcome.ConversationId;
            dto.Answer = outcome.Answer;
            dto.Citations = outcome.Citations;
            dto.ToolCalls = outcome.ToolCalls;
            dto.Verified = outcome.Verified;
            dto.Degraded = outcome.Degraded;
            dto.Model = outcome.Model;

            string ttsState = "skipped";
            if (!outcome.Degraded)
            {
                TtsResult tts = await _tts.SynthesizeAsync(_words.SpeechNormalize(outcome.Answer), ct);
                if (tts.Ok)
                {
                    dto.AudioBase64 = Convert.ToBase64String(tts.Audio);
                    ttsState = "ok";
                }
                else
                {
                    dto.Degraded = true;
                    dto.Answer = outcome.Answer + "\n" + AiText.VoiceNoAudio;
                    ttsState = "unavailable";
                }
            }
            await _audit.LogAsync(AiAuditKinds.Voice, $"stt=ok; tts={ttsState}; chars={dto.Transcript.Length}", ct, model: outcome.Model, latencyMs: (int)total.ElapsedMilliseconds);
            return ResponseDtoHelper.Ok(dto);
        }

        // ------------------------------------------------------------------------------------------ conversations

        public async Task<ResponseDto<PagedList<AiConversationListDto>>> GetConversationsAsync(AiConversationQuery query, CancellationToken ct)
        {
            if (Me == 0) return AiText.Bad<PagedList<AiConversationListDto>>(AiText.NotAllowed);
            query ??= new AiConversationQuery();
            IQueryable<AiConversationListDto> rows = _uow.Set<AiConversation>().AsNoTracking().Where(c => c.UserId == Me).OrderByDescending(c => c.LastAt).ThenByDescending(c => c.Id)
                .Select(c => new AiConversationListDto { Id = c.Id, Title = c.Title, CreatedAt = c.CreatedAt, LastAt = c.LastAt, Messages = c.Messages.Count(m => m.Role != AiMessageRole.Tool) });
            return ResponseDtoHelper.Ok(await AiText.PageAsync(rows, query.PageNumber, query.PageSize, ct));
        }

        public async Task<ResponseDto<AiConversationDto>> GetConversationAsync(int id, CancellationToken ct)
        {
            if (Me == 0) return AiText.Bad<AiConversationDto>(AiText.NotAllowed);
            AiConversation c = await _uow.Set<AiConversation>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == Me, ct);
            if (c == null) return AiText.NotFound<AiConversationDto>("گفتگو پیدا نشد.");
            List<AiMessage> messages = await _uow.Set<AiMessage>().AsNoTracking().Where(m => m.ConversationId == id).OrderBy(m => m.Id).ToListAsync(ct);
            var dto = new AiConversationDto { Id = c.Id, Title = c.Title, CreatedAt = c.CreatedAt, LastAt = c.LastAt, Messages = messages.Count(m => m.Role != AiMessageRole.Tool) };
            dto.MessageList = messages.Select(m => new AiMessageDto
            {
                Id = m.Id, Role = m.Role.ToString(), Text = m.Text, ToolName = m.ToolName, CreatedAt = m.CreatedAt, Verified = m.Verified, Model = m.Model,
            }).ToList();
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
