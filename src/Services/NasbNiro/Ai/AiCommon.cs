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
    }

    internal static class AiText
    {
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string DegradedAnswer = "تحلیل هوشمند موقتاً در دسترس نیست؛ داشبورد اصلی همچنان در دسترس است.";
        public const string ToolForbidden = "شما به این اطلاعات دسترسی ندارید.";
        public const string Unverifiable = "پاسخ مدل قابل راستی‌آزمایی نبود؛ نتایج مستقیم سامانه:";
        public const string VoiceNoAudio = "سرویس گفتار در دسترس نیست؛ پاسخ فقط به‌صورت متن ارائه شد.";
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

        public static int Size(int pageSize) => pageSize <= 0 ? 25 : Math.Min(pageSize, 200);

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
                At = DateTimeOffset.Now, UserId = userId ?? _user.UserId, SeatCode = seatCode, Kind = kind, PromptVersionId = promptVersionId,
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
