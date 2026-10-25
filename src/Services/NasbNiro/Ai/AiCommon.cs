using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Ai
{
    public static class AiRoles
    {
        /// <summary>Registers endpoints, prompt versions and rebuilds the index.</summary>
        public const string Admins = "PMO";

        /// <summary>Reads the endpoints, the prompt versions and the audit.</summary>
        public const string Readers = "PMO,CEO";
    }

    /// <summary>The configuration keys of the module (appsettings.json, section Ai).</summary>
    public static class AiConfigKeys
    {
        public const string ChatTimeoutSeconds = "Ai:ChatTimeoutSeconds";
        public const string TtsVoice = "Ai:TtsVoice";

        /// <summary>Voice requests (speech to text, chat, text to speech) share one budget. Default 90.</summary>
        public const string VoiceTimeoutSeconds = "Ai:VoiceTimeoutSeconds";

        /// <summary>An endpoint that failed is skipped for this many seconds while another one is healthy. Default 60.</summary>
        public const string BreakerSeconds = "Ai:BreakerSeconds";

        /// <summary>How long the audit rows must be kept. The rows are append-only and A1 has no delete job: this is the policy for a later archive job. Default 3 years.</summary>
        public const string AuditRetentionYears = "Ai:AuditRetentionYears";

        /// <summary>False switches the seat scheduler off (the CI runs the seats by hand).</summary>
        public const string Jobs = "Ai:Jobs";

        /// <summary>A run by hand for a day in the future needs this switch (like Service:AllowFutureAsOf).</summary>
        public const string AllowFutureAsOf = "Ai:AllowFutureAsOf";

        /// <summary>A scheduled seat that did not succeed is tried again after this many minutes. Default 5.</summary>
        public const string RetryMinutes = "Ai:RetryMinutes";

        /// <summary>A scheduled seat whose last run was skipped because the model was down waits this many minutes before it asks again. Default 30.</summary>
        public const string SkipBackoffMinutes = "Ai:SkipBackoffMinutes";

        /// <summary>FIN-ANOMALY: a posting of at least this amount (rial) that is also a round number is reported. Default 1,000,000,000.</summary>
        public const string FinRoundAmount = "Ai:FinRoundAmount";

        /// <summary>FIN-ANOMALY: a round number is a multiple of this unit (rial). Default 100,000,000.</summary>
        public const string FinRoundUnit = "Ai:FinRoundUnit";

        /// <summary>FIN-ANOMALY: how many days back the vouchers and bank lines are looked at. Default 60.</summary>
        public const string FinLookbackDays = "Ai:FinLookbackDays";
    }

    public static class AiAuditKinds
    {
        public const string Chat = "chat";
        public const string Voice = "voice";
        public const string Tool = "tool";
        public const string Unverified = "unverified";
        public const string Degraded = "degraded";
        public const string Failover = "failover";
        public const string Admin = "admin";

        /// <summary>A run of a seat (started, finished, failed, skipped).</summary>
        public const string Seat = "seat";

        /// <summary>A decision on a note of a seat.</summary>
        public const string Note = "note";

        /// <summary>An evaluation run.</summary>
        public const string Eval = "eval";
    }

    /// <summary>What a voice upload must look like: an audio media type (parameters such as codecs=opus are ignored) and the first bytes of a WAV, WebM or Ogg file.</summary>
    public static class AiAudioCheck
    {
        public const string Message = "فقط فایل صوتی webm، wav یا ogg پذیرفته می‌شود.";
        private static readonly string[] AudioTypes = { "audio/webm", "audio/wav", "audio/x-wav", "audio/wave", "audio/ogg", "video/webm" };

        /// <summary>The media type (parameters such as codecs=opus ignored) is an audio type or absent, and the first bytes are a RIFF/WAVE, EBML (webm) or OggS header.</summary>
        public static bool IsAllowed(string contentType, byte[] audio)
        {
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                if (!System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var parsed)) return false;
                string type = (parsed.MediaType ?? string.Empty).ToLowerInvariant();
                if (type != "application/octet-stream" && !AudioTypes.Contains(type)) return false;
            }
            if (audio == null || audio.Length < 12) return false;
            bool wav = audio[0] == 'R' && audio[1] == 'I' && audio[2] == 'F' && audio[3] == 'F' && audio[8] == 'W' && audio[9] == 'A' && audio[10] == 'V' && audio[11] == 'E';
            bool webm = audio[0] == 0x1A && audio[1] == 0x45 && audio[2] == 0xDF && audio[3] == 0xA3;
            bool ogg = audio[0] == 'O' && audio[1] == 'g' && audio[2] == 'g' && audio[3] == 'S';
            return wav || webm || ogg;
        }
    }

    internal static class AiText
    {
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string DegradedAnswer = "تحلیل هوشمند موقتاً در دسترس نیست؛ داشبورد اصلی همچنان در دسترس است.";
        public const string ToolForbidden = "شما به این اطلاعات دسترسی ندارید.";
        public const string Unverifiable = "پاسخ مدل قابل راستی‌آزمایی نبود؛ نتایج مستقیم سامانه:";
        public const string VoiceNoAudio = "سرویس گفتار در دسترس نیست؛ پاسخ فقط به‌صورت متن ارائه شد.";
        public const string UnknownTool = "ابزار ناشناخته";
        public const string VoiceNoTranscript = "تبدیل گفتار به متن انجام نشد؛ سرویس گفتار در دسترس نیست. متن خود را تایپ کنید.";

        public static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        public static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        public static ResponseDto<T> NotFound<T>(string message) => ResponseDtoHelper.NotFound<T>(message);

        public static string Date(DateTimeOffset d) => InventoryText.PersianDate(d.LocalDateTime);
        public static string Stamp(DateTimeOffset d) => InventoryText.PersianDate(d.LocalDateTime) + " " + d.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);

        public static string Cut(string s, int max) => s == null ? null : (s.Length <= max ? s : s.Substring(0, max));

        public static string Sha256Hex(string text)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>Like PagedList: 0 (or less) means all rows; other sizes are capped at 200.</summary>
        public static int Size(int pageSize) => pageSize <= 0 ? 0 : Math.Min(pageSize, 200);

        /// <summary>Arabic ي and ك to the Persian letters, as the context does on save, so that a hash is taken over the text that is stored.</summary>
        public static string YeKe(string s) => s?.Replace('ي', 'ی').Replace('ك', 'ک');

        public static async Task<PagedList<T>> PageAsync<T>(IQueryable<T> rows, int pageNumber, int pageSize, CancellationToken ct) =>
            await PagedList<T>.CreateAsync(rows, pageNumber, Size(pageSize), ct);
    }

    /// <summary>Writes the append-only audit rows. Rows are only ever added.</summary>
    public interface IAiAudit
    {
        /// <summary>Stages a row (the caller saves).</summary>
        void Add(string kind, string detail, string model = null, int? latencyMs = null, int? promptVersionId = null, string promptHash = null, string seatCode = null, int? userId = null);

        /// <summary>Adds a row and saves.</summary>
        Task LogAsync(string kind, string detail, CancellationToken ct, string model = null, int? latencyMs = null, int? promptVersionId = null, string promptHash = null, string seatCode = null, int? userId = null);
    }

    public class AiAudit : IAiAudit
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public AiAudit(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public void Add(string kind, string detail, string model = null, int? latencyMs = null, int? promptVersionId = null, string promptHash = null, string seatCode = null, int? userId = null)
        {
            _uow.Set<AiAuditEvent>().Add(new AiAuditEvent
            {
                At = DateTimeOffset.Now, UserId = userId ?? _user.UserId, SeatCode = seatCode ?? AiSeatContext.Code, Kind = kind, PromptVersionId = promptVersionId,
                Model = AiText.Cut(model, 200), LatencyMs = latencyMs, Detail = AiText.Cut(detail, 4000), PromptHash = promptHash,
            });
        }

        public async Task LogAsync(string kind, string detail, CancellationToken ct, string model = null, int? latencyMs = null, int? promptVersionId = null, string promptHash = null, string seatCode = null, int? userId = null)
        {
            Add(kind, detail, model, latencyMs, promptVersionId, promptHash, seatCode, userId);
            await _uow.SaveChangesAsync(ct);
        }
    }
}
