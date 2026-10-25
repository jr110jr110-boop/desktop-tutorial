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

        private int VoiceTimeoutSeconds
        {
            get
            {
                string text = _configuration?[AiConfigKeys.VoiceTimeoutSeconds];
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s > 0 ? s : 90;
            }
        }

        /// <summary>The active prompt of a code; version 1 of CHAT is seeded on first use. Two requests that seed at the same time: the loser of the unique index re-reads the winner's row.</summary>
        public static async Task<AiPromptVersion> ActivePromptAsync(IUnitOfWork uow, string code, int userId, CancellationToken ct)
        {
            AiPromptVersion active = await uow.Set<AiPromptVersion>().Where(p => p.Code == code && p.IsActive).OrderByDescending(p => p.Version).FirstOrDefaultAsync(ct);
            if (active != null) return active;
            if (code != ChatPromptCode || await uow.Set<AiPromptVersion>().AnyAsync(p => p.Code == code, ct)) return null;
            DateTimeOffset now = DateTimeOffset.Now;
            active = new AiPromptVersion { Code = code, Version = 1, Text = SeedPromptText, IsActive = true, CreatedAt = now, CreatedByUserId = userId, ActivatedAt = now, ActivatedByUserId = userId };
            uow.Set<AiPromptVersion>().Add(active);
            try
            {
                await uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                uow.Entry(active).State = EntityState.Detached;
                active = await uow.Set<AiPromptVersion>().AsNoTracking().Where(p => p.Code == code && p.IsActive).OrderByDescending(p => p.Version).FirstOrDefaultAsync(ct);
            }
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
            var earlier = new List<string>();
            if (conversation != null) await LoadHistoryAsync(conversation.Id, messages, earlier, ct);
            messages.Add(new LlmMessage { Role = "user", Content = text });

            IReadOnlyList<AiToolDefinition> offered = _tools.Offered();
            List<LlmToolSpec> specs = offered.Select(t => new LlmToolSpec { Name = t.Name, Description = t.Description, SchemaJson = t.SchemaJson }).ToList();

            AiLoopResult loop = await AiChatLoop.RunAsync(_llm, _tools, _verifier, messages, specs, text, earlier, TimeoutSeconds, ct);
            List<(AiToolResult Result, string Arguments)> results = loop.Results;
            string model = loop.Model;
            bool degraded = loop.Degraded;

            var outcome = new Outcome { Model = model, Answer = AiText.YeKe(loop.Answer), Verified = loop.Verified, Degraded = degraded };
            if (degraded) outcome.Model = null;
            var now = DateTimeOffset.Now;
            foreach (var x in results)
            {
                outcome.ToolCalls.Add(new AiToolCallDto { Name = x.Result.Name, Allowed = x.Result.Allowed });
                foreach (AiCitationDto c in x.Result.Citations)
                    if (outcome.Citations.Count < 12 && !outcome.Citations.Any(e => e.Type == c.Type && e.Id == c.Id && e.Link == c.Link)) outcome.Citations.Add(c);
            }

            // persist: the conversation, the messages, the tool call logs and the audit rows in one save.
            // The evaluation keeps no conversation (it would leave 66 per run under the evaluation user): its audit rows and its run record are enough.
            bool keepConversation = AiSeatContext.Code != AiSeatCodes.Eval;
            AiMessage assistant = null;
            if (keepConversation)
            {
                if (conversation == null)
                {
                    conversation = new AiConversation { UserId = Me, Title = AiText.Cut(AiText.YeKe(text), 80), CreatedAt = now, LastAt = now };
                    _uow.Set<AiConversation>().Add(conversation);
                }
                conversation.LastAt = now;
                _uow.Set<AiMessage>().Add(new AiMessage { Conversation = conversation, Role = AiMessageRole.User, Text = AiText.YeKe(text), CreatedAt = now });
                foreach (var x in results)
                    _uow.Set<AiMessage>().Add(new AiMessage { Conversation = conversation, Role = AiMessageRole.Tool, Text = x.Result.Json, ToolName = x.Result.Name, CreatedAt = now });
                assistant = new AiMessage { Conversation = conversation, Role = AiMessageRole.Assistant, Text = outcome.Answer, CreatedAt = now, Verified = degraded ? (bool?)null : outcome.Verified, Model = AiText.Cut(outcome.Model, 200) };
                _uow.Set<AiMessage>().Add(assistant);
            }
            foreach (var x in results)
            {
                if (keepConversation)
                    _uow.Set<AiToolCallLog>().Add(new AiToolCallLog
                    {
                        Conversation = conversation, Message = assistant, ToolName = x.Result.Name, ArgumentsJson = AiText.Cut(x.Arguments, 4000),
                        ResultHash = x.Result.Hash, ResultChars = x.Result.Chars, Allowed = x.Result.Allowed, DurationMs = x.Result.DurationMs,
                    });
                _audit.Add(AiAuditKinds.Tool, $"{x.Result.Name} {AiText.Cut(x.Arguments, 2000)} allowed={x.Result.Allowed} chars={x.Result.Chars} hash={x.Result.Hash}", model, x.Result.DurationMs, prompt?.Id, promptHash);
            }
            total.Stop();
            string tools = string.Join(",", results.Select(x => x.Result.Name));
            if (degraded) _audit.Add(AiAuditKinds.Degraded, AiText.Cut(loop.DegradedReason, 1000), null, (int)total.ElapsedMilliseconds, prompt?.Id, promptHash);
            if (loop.Verification != null && !loop.Verification.Verified)
                _audit.Add(AiAuditKinds.Unverified, "tokens: " + string.Join(" ", loop.Verification.OffendingTokens) + "; tools: " + tools, model, (int)total.ElapsedMilliseconds, prompt?.Id, promptHash);
            // the question as asked (at most 2000 characters) and the SHA-256 of the stored answer (the answer itself is the AiMessage row)
            _audit.Add(AiAuditKinds.Chat,
                $"tools: {(tools.Length == 0 ? "-" : tools)}; verified={(degraded ? "n/a" : outcome.Verified.ToString().ToLowerInvariant())}; degraded={degraded.ToString().ToLowerInvariant()}; answerSha256={AiText.Sha256Hex(outcome.Answer)}\nq: {AiText.Cut(text, MaxTextLength)}",
                model, (int)total.ElapsedMilliseconds, prompt?.Id, promptHash);
            await _uow.SaveChangesAsync(ct);

            outcome.ConversationId = conversation?.Id ?? 0;
            return (outcome, null);
        }

        /// <summary>The last ten turns as chat messages (earlier tool results are replayed as tool calls, newest first within a budget of characters) and the tool results of the window for the verifier.</summary>
        private async Task LoadHistoryAsync(int conversationId, List<LlmMessage> messages, List<string> earlierResults, CancellationToken ct)
        {
            const int ToolContextBudget = 24000;
            List<AiMessage> chat = await _uow.Set<AiMessage>().AsNoTracking().Where(m => m.ConversationId == conversationId && m.Role != AiMessageRole.Tool)
                .OrderByDescending(m => m.Id).Take(MemoryTurns * 2).ToListAsync(ct);
            if (chat.Count == 0) return;
            int minId = chat.Min(m => m.Id);
            List<AiMessage> allTools = await _uow.Set<AiMessage>().AsNoTracking().Where(m => m.ConversationId == conversationId && m.Role == AiMessageRole.Tool).OrderBy(m => m.Id).ToListAsync(ct);
            List<string> argumentsByOrder = await _uow.Set<AiToolCallLog>().AsNoTracking().Where(l => l.ConversationId == conversationId).OrderBy(l => l.Id).Select(l => l.ArgumentsJson).ToListAsync(ct);
            bool paired = argumentsByOrder.Count == allTools.Count;

            var window = new List<(AiMessage Message, string Arguments)>();
            for (int i = 0; i < allTools.Count; i++)
                if (allTools[i].Id > minId) window.Add((allTools[i], paired ? argumentsByOrder[i] : "{}"));
            foreach (var t in window) earlierResults.Add(t.Message.Text);

            var inContext = new HashSet<int>();
            int budget = ToolContextBudget;
            foreach (var t in Enumerable.Reverse(window))
            {
                budget -= t.Message.Text?.Length ?? 0;
                if (budget < 0) break;
                inContext.Add(t.Message.Id);
            }

            var ordered = chat.Concat(window.Where(t => inContext.Contains(t.Message.Id)).Select(t => t.Message)).OrderBy(m => m.Id).ToList();
            var group = new List<AiMessage>();
            void Flush()
            {
                if (group.Count == 0) return;
                messages.Add(new LlmMessage
                {
                    Role = "assistant",
                    ToolCalls = group.Select(g => new LlmToolCall { Id = "h" + g.Id, Name = g.ToolName ?? "?", ArgumentsJson = window.First(w => w.Message.Id == g.Id).Arguments ?? "{}" }).ToList(),
                });
                foreach (AiMessage g in group) messages.Add(new LlmMessage { Role = "tool", ToolCallId = "h" + g.Id, Content = g.Text });
                group.Clear();
            }
            foreach (AiMessage m in ordered)
            {
                if (m.Role == AiMessageRole.Tool) { group.Add(m); continue; }
                Flush();
                messages.Add(new LlmMessage { Role = m.Role == AiMessageRole.User ? "user" : "assistant", Content = m.Text });
            }
            Flush();
        }

        // ------------------------------------------------------------------------------------------ voice

        public async Task<ResponseDto<AiVoiceResponseDto>> VoiceAsync(byte[] audio, string fileName, string contentType, int? conversationId, CancellationToken ct)
        {
            if (Me == 0) return AiText.Bad<AiVoiceResponseDto>(AiText.NotAllowed);
            if (audio == null || audio.Length == 0) return AiText.Bad<AiVoiceResponseDto>("فایل صوتی خالی است.");
            // the conversation must be the caller's before anything else happens (also when speech to text fails)
            if (conversationId.HasValue && !await _uow.Set<AiConversation>().AsNoTracking().AnyAsync(c => c.Id == conversationId.Value && c.UserId == Me, ct))
                return AiText.NotFound<AiVoiceResponseDto>("گفتگو پیدا نشد.");
            var total = Stopwatch.StartNew();
            var dto = new AiVoiceResponseDto { ConversationId = conversationId ?? 0 };

            // speech to text, chat and text to speech share one budget
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(VoiceTimeoutSeconds));
            try
            {
                SttResult stt = await _stt.TranscribeAsync(audio, fileName, contentType, budget.Token);
                if (!stt.Ok || string.IsNullOrWhiteSpace(stt.Text))
                {
                    dto.Answer = AiText.VoiceNoTranscript;
                    dto.Degraded = true;
                    dto.Verified = true;
                    await _audit.LogAsync(AiAuditKinds.Voice, stt.Ok ? "speech to text returned no text" : "speech to text endpoint unavailable", ct, latencyMs: (int)total.ElapsedMilliseconds);
                    return ResponseDtoHelper.Ok(dto);
                }

                dto.Transcript = AiText.Cut(stt.Text.Trim(), MaxTextLength);
                (Outcome outcome, string error) = await RunAsync(dto.Transcript, conversationId, budget.Token);
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
                    TtsResult tts = await _tts.SynthesizeAsync(_words.SpeechNormalize(outcome.Answer), budget.Token);
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
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                dto.Degraded = true;
                dto.Verified = true;
                dto.Answer = dto.Transcript == null ? AiText.VoiceNoTranscript : AiText.DegradedAnswer;
                await _audit.LogAsync(AiAuditKinds.Voice, $"voice budget of {VoiceTimeoutSeconds} s exceeded", ct, latencyMs: (int)total.ElapsedMilliseconds);
                return ResponseDtoHelper.Ok(dto);
            }
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

    /// <summary>The outcome of one model loop: the answer to show, whether it passed the numeric check, the tool results of the turn.</summary>
    public sealed class AiLoopResult
    {
        public string Answer { get; set; }
        public bool Verified { get; set; }
        public bool Degraded { get; set; }
        public string DegradedReason { get; set; }
        public string Model { get; set; }
        public VerificationResult Verification { get; set; }
        public List<(AiToolResult Result, string Arguments)> Results { get; set; } = new List<(AiToolResult Result, string Arguments)>();
    }

    /// <summary>
    /// The model loop (pure given its collaborators, so it is unit tested with a fake model): at most <see cref="AiChatService.MaxToolRounds"/> rounds of tool calls, then one more
    /// call without tools that must answer; every non-degraded path goes through the verifier.
    /// </summary>
    public static class AiChatLoop
    {
        public static async Task<AiLoopResult> RunAsync(ILlmClient llm, IToolRegistry tools, IAnswerVerifier verifier, List<LlmMessage> messages, List<LlmToolSpec> specs,
            string question, IReadOnlyList<string> earlierResults, int timeoutSeconds, CancellationToken ct)
        {
            var loop = new AiLoopResult();
            string final = null;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                try
                {
                    for (int round = 1; round <= AiChatService.MaxToolRounds + 1; round++)
                    {
                        // after the last tool round the model must answer: no tools are offered
                        LlmResult r = await llm.CompleteAsync(messages, round > AiChatService.MaxToolRounds ? null : specs, timeout.Token);
                        if (!r.Ok) { loop.DegradedReason = r.Error ?? "no endpoint answered"; break; }
                        loop.Model = AiText.Cut(r.Model, 200);
                        if (r.ToolCalls.Count == 0 || round > AiChatService.MaxToolRounds) { final = r.Content ?? string.Empty; break; }
                        messages.Add(new LlmMessage { Role = "assistant", Content = r.Content, ToolCalls = r.ToolCalls });
                        foreach (LlmToolCall call in r.ToolCalls)
                        {
                            AiToolResult tr = await tools.ExecuteAsync(call.Name, call.ArgumentsJson, timeout.Token);
                            loop.Results.Add((tr, call.ArgumentsJson));
                            messages.Add(new LlmMessage { Role = "tool", ToolCallId = call.Id, Content = tr.Json });
                        }
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    loop.DegradedReason = $"timeout after {timeoutSeconds} s";
                }
            }

            if (loop.DegradedReason != null)
            {
                loop.Degraded = true;
                loop.Verified = true;
                loop.Answer = AiText.DegradedAnswer;
                return loop;
            }
            string answer = string.IsNullOrWhiteSpace(final) ? "مدل پاسخی تولید نکرد؛ دوباره تلاش کنید." : final.Trim();
            List<VerifierToolResult> input = loop.Results.Select(x => new VerifierToolResult { Name = x.Result.Name, Title = x.Result.Title, Json = x.Result.Json }).ToList();
            loop.Verification = verifier.Verify(answer, question, input, earlierResults);
            loop.Answer = loop.Verification.Answer;
            loop.Verified = loop.Verification.Verified;
            return loop;
        }
    }
}
