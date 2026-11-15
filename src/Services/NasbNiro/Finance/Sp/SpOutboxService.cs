using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Agents;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Finance.Sp
{
    public sealed class SpPrepareResult
    {
        public int Prepared { get; set; }
        public int Corrections { get; set; }
        public int Superseded { get; set; }
        public int Unchanged { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }

        /// <summary>Items cancelled because their source document is no longer final.</summary>
        public int Cancelled { get; set; }

        /// <summary>Items stuck in "sending" that went back to "approved" (their kind is off or no sender is configured).</summary>
        public int Recovered { get; set; }

        public string Describe() => string.Create(CultureInfo.InvariantCulture, $"prepared={Prepared}; corrections={Corrections}; superseded={Superseded}; unchanged={Unchanged}; skipped={Skipped}; errors={Errors}; cancelled={Cancelled}; recovered={Recovered}");
    }

    public enum SpSendOutcome
    {
        /// <summary>Nothing was done (the item is not due, not approved, its kind is off).</summary>
        NotDue = 0,
        Sent = 1,
        Failed = 2,

        /// <summary>No sender is configured: nothing was tried and nothing was counted.</summary>
        NoSender = 3,

        /// <summary>The source changed (or is no longer final): the item was replaced instead of sent.</summary>
        Superseded = 4,

        /// <summary>Another runner took the item first.</summary>
        Lost = 5,
    }

    public interface ISpOutboxService
    {
        Task<ResponseDto<List<SpTransferTypeDto>>> GetTypesAsync(CancellationToken ct);
        Task<ResponseDto<SpTransferTypeDto>> SwitchTypeAsync(SpTypeSwitchDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<SpOutboxItemDto>>> GetItemsAsync(SpOutboxQuery query, CancellationToken ct);
        Task<ResponseDto<SpOutboxItemDetailDto>> GetItemAsync(int id, CancellationToken ct);
        Task<ResponseDto<SpOutboxItemDto>> DecideAsync(SpDecideDto dto, CancellationToken ct);
        Task<ResponseDto<SpBulkResultDto>> BulkApproveAsync(SpBulkApproveDto dto, CancellationToken ct);
        Task<ResponseDto<SpOutboxItemDto>> FixAsync(SpFixDto dto, CancellationToken ct);
        Task<ResponseDto<SpOutboxSummaryDto>> GetSummaryAsync(CancellationToken ct);

        /// <summary>The agent: prepares the items of every enabled kind (bounded, idempotent, no language model).</summary>
        Task<ResponseDto<SpPrepareResult>> PrepareAsync(CancellationToken ct);

        /// <summary>The agent: sends one approved (or due failed) item through the configured sender.</summary>
        Task<ResponseDto<SpSendOutcome>> SendAsync(int itemId, CancellationToken ct);
    }

    /// <summary>The questions that the discovery of the agent steps asks (read only, cheap, and translated to SQL).</summary>
    internal static class SpFacts
    {
        /// <summary>The most documents one kind prepares in one run (the newest first: the backlog of an old kind follows in the next runs).</summary>
        public const int Cap = 50;

        /// <summary>The largest page of the queue list (0 or less gives the default of 50).</summary>
        public const int MaxPageSize = 200;

        /// <summary>A send that started and never ended (the process died) is taken as lost after this time; the same envelope can be sent again (idempotency key).</summary>
        public static readonly TimeSpan StaleSending = TimeSpan.FromMinutes(15);

        /// <summary>The wait after the 1st, 2nd, 3rd and 4th failed attempt; later ones wait as long as the last.</summary>
        public static readonly TimeSpan[] Backoff = { TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12) };

        /// <summary>The final documents of a kind that have no live-or-handled item for their current row version.</summary>
        public static IQueryable<SpRef> NeedingWork(IUnitOfWork uow, SpSource source, DateTime? fromDate)
        {
            string code = source.Code, type = source.SourceType;
            // only the documents finalised on or after the first day of the kind are queued (no first day: nothing is queued)
            DateTimeOffset from = fromDate.HasValue ? new DateTimeOffset(fromDate.Value.Date, TimeZoneInfo.Local.GetUtcOffset(fromDate.Value.Date)) : DateTimeOffset.MaxValue;
            IQueryable<SpOutboxItem> items = uow.Set<SpOutboxItem>().AsNoTracking();
            return source.Final(uow).Where(c => c.At != null && c.At >= from && !items.Any(i => i.TransferTypeCode == code && i.SourceType == type && i.SourceId == c.Id && i.Status != SpOutboxStatus.Superseded && i.SourceRowVersion == c.RowVersion));
        }

        /// <summary>The first day of each enabled kind (code to date; null when it was never set).</summary>
        public static async Task<Dictionary<string, DateTime?>> EnabledFromAsync(IUnitOfWork uow, CancellationToken ct) =>
            (await uow.Set<SpTransferType>().AsNoTracking().Where(t => t.IsEnabled).Select(t => new { t.Code, t.FromDate }).ToListAsync(ct)).ToDictionary(t => t.Code, t => t.FromDate, StringComparer.Ordinal);

        /// <summary>Live items (waiting, approved or failed) whose source document is no longer final: the agent cancels them.</summary>
        public static IQueryable<SpOutboxItem> NoLongerFinal(IUnitOfWork uow, SpSource source)
        {
            string code = source.Code, type = source.SourceType;
            IQueryable<SpRef> finals = source.Final(uow);
            return uow.Set<SpOutboxItem>().AsNoTracking().Where(i => i.TransferTypeCode == code && i.SourceType == type
                && (i.Status == SpOutboxStatus.AwaitingApproval || i.Status == SpOutboxStatus.Approved || i.Status == SpOutboxStatus.Failed)
                && !finals.Any(f => f.Id == i.SourceId));
        }

        /// <summary>Items stuck in "sending" for longer than the stale time, of a kind that is off or while no sender is configured: they go back to "approved".</summary>
        public static IQueryable<SpOutboxItem> StaleSendingToRecover(IUnitOfWork uow, DateTimeOffset now, bool senderConfigured)
        {
            IQueryable<string> enabled = EnabledCodes(uow);
            DateTimeOffset stale = now - StaleSending;
            return uow.Set<SpOutboxItem>().AsNoTracking().Where(i => i.Status == SpOutboxStatus.Sending && i.LastAttemptAt != null && i.LastAttemptAt < stale && (!senderConfigured || !enabled.Contains(i.TransferTypeCode)));
        }

        public static IQueryable<string> EnabledCodes(IUnitOfWork uow) => uow.Set<SpTransferType>().AsNoTracking().Where(t => t.IsEnabled).Select(t => t.Code);

        /// <summary>Items of the enabled kinds in one of the given statuses.</summary>
        public static IQueryable<SpOutboxItem> OfEnabled(IUnitOfWork uow, SpOutboxStatus status)
        {
            IQueryable<string> enabled = EnabledCodes(uow);
            return uow.Set<SpOutboxItem>().AsNoTracking().Where(i => i.Status == status && enabled.Contains(i.TransferTypeCode));
        }

        /// <summary>Items that the send step may take now: approved, or failed and due, or stuck in "sending"; only for enabled kinds.</summary>
        public static IQueryable<SpOutboxItem> Sendable(IUnitOfWork uow, DateTimeOffset now)
        {
            IQueryable<string> enabled = EnabledCodes(uow);
            DateTimeOffset stale = now - StaleSending;
            return uow.Set<SpOutboxItem>().AsNoTracking().Where(i => enabled.Contains(i.TransferTypeCode)
                && (i.Status == SpOutboxStatus.Approved || (i.Status == SpOutboxStatus.Failed && i.NextAttemptAt != null && i.NextAttemptAt <= now) || (i.Status == SpOutboxStatus.Sending && i.LastAttemptAt != null && i.LastAttemptAt < stale)));
        }

        /// <summary>Items that failed their last automatic attempt: a person decides.</summary>
        public static IQueryable<SpOutboxItem> NeedingFix(IUnitOfWork uow) =>
            uow.Set<SpOutboxItem>().AsNoTracking().Where(i => i.Status == SpOutboxStatus.Failed && i.NextAttemptAt == null);

        /// <summary>The wait after the failed attempt number <paramref name="attemptsSinceRetry"/>; null when the automatic attempts are used up.</summary>
        public static TimeSpan? NextDelay(int attemptsSinceRetry, int maxAttempts)
        {
            if (attemptsSinceRetry >= maxAttempts) return null;
            return Backoff[Math.Min(Math.Max(attemptsSinceRetry, 1), Backoff.Length) - 1];
        }

        /// <summary>The target type of a send task: one per attempt number (the discriminator), so the repeat guard of the reconciler (30 minutes) does not hold back the backoff of 5 minutes; the target id stays the item id.</summary>
        public static string SendTargetType(int attemptCount) => "SpSend" + Math.Min(Math.Max(attemptCount, 0), 99).ToString(CultureInfo.InvariantCulture);

        public static bool SameBytes(byte[] a, byte[] b) => (a == null && b == null) || (a != null && b != null && a.AsSpan().SequenceEqual(b));
    }

    public class SpOutboxService : ISpOutboxService
    {
        private const string NotAllowed = "شما مجوز این کار را ندارید.";
        private const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        private const string SeatsDoNotDecide = "تایید و تصمیم با انسان است؛ ایجنت هوش مصنوعی آن را انجام نمی‌دهد.";
        private const string Concurrency = "همزمان توسط کاربر دیگری تغییر کرد؛ صفحه را تازه کنید و دوباره تلاش کنید.";
        private const string RowVersionMissing = "نسخهٔ قلم همراه درخواست نیست؛ صفحه را تازه کنید.";
        private const string NotFound = "قلم صف انتقال پیدا نشد.";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ISepidarSender _sender;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SpOutboxService> _logger;

        public SpOutboxService(IUnitOfWork uow, IInventoryUserContext user, ISepidarSender sender, IConfiguration configuration, ILogger<SpOutboxService> logger = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _sender = sender ?? throw new ArgumentNullException(nameof(sender));
            _configuration = configuration;
            _logger = logger;
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private bool Sees => MfPrivacy.SeesPayroll(_user);
        private bool CanRead => _user.IsInRole(SpRoles.Readers);
        private int MaxAttempts => SepidarConfigKeys.MaxAttemptsOf(_configuration);

        /// <summary>Every read path leaves out the items that only the readers of payroll may see.</summary>
        private IQueryable<SpOutboxItem> Visible(IQueryable<SpOutboxItem> q) => Sees ? q : q.Where(i => !i.PayrollRestricted);

        // ================================================================================== the kinds

        public async Task<ResponseDto<List<SpTransferTypeDto>>> GetTypesAsync(CancellationToken ct)
        {
            if (!CanRead) return Bad<List<SpTransferTypeDto>>(NotAllowed);
            List<SpTransferType> rows = await _uow.Set<SpTransferType>().AsNoTracking().ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => r.ChangedByUserId), ct);
            // a kind that has no row yet (nothing has switched it) is shown as the seed would make it: off
            var list = SpCatalog.All.Select(s =>
            {
                SpTransferType r = rows.FirstOrDefault(x => x.Code == s.Code);
                return r == null
                    ? new SpTransferTypeDto { Code = s.Code, Title = s.Title, IsEnabled = false, RequiresApproval = true, Note = s.Note, OverlapNote = s.OldKindsText }
                    : ToDto(r, names, s);
            }).ToList();
            return ResponseDtoHelper.Ok(list);
        }

        private static SpTransferTypeDto ToDto(SpTransferType r, Dictionary<int, string> names, SpSource source) => new SpTransferTypeDto
        {
            Code = r.Code, Title = r.Title, IsEnabled = r.IsEnabled, RequiresApproval = r.RequiresApproval, Note = r.Note, FromDate = r.FromDate, OverlapNote = source?.OldKindsText, ChangedAt = r.ChangedAt, ChangedByName = names.Name(r.ChangedByUserId),
            RowVersion = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion),
        };

        public async Task<ResponseDto<SpTransferTypeDto>> SwitchTypeAsync(SpTypeSwitchDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Code)) return Bad<SpTransferTypeDto>(InvalidData);
            if (AiActing.IsActing || !_user.IsInRole(SpRoles.TypeSwitchers)) return Bad<SpTransferTypeDto>(NotAllowed);
            SpSource source = SpCatalog.Find(dto.Code);
            if (source == null) return Bad<SpTransferTypeDto>("نوع انتقال پیدا نشد.");
            if (dto.RequiresApproval == false) return Bad<SpTransferTypeDto>("تایید انسان برای انتقال به سپیدار فعلاً الزامی است و خاموش نمی‌شود.");
            string note = Clean(dto.Note);
            if (note != null && note.Length > 500) return Bad<SpTransferTypeDto>("یادداشت حداکثر ۵۰۰ نویسه است.");
            await SpCatalog.EnsureAsync(_uow, ct);
            SpTransferType row = await _uow.Set<SpTransferType>().FirstOrDefaultAsync(t => t.Code == source.Code, ct);
            if (row == null) return Bad<SpTransferTypeDto>("نوع انتقال پیدا نشد.");
            if (!string.IsNullOrEmpty(dto.RowVersion))
            {
                byte[] seen;
                try { seen = Convert.FromBase64String(dto.RowVersion); }
                catch (FormatException) { return Bad<SpTransferTypeDto>(InvalidData); }
                if (row.RowVersion != null && !row.RowVersion.AsSpan().SequenceEqual(seen)) return Bad<SpTransferTypeDto>(Concurrency);
                if (row.RowVersion != null) _uow.Entry(row).Property(x => x.RowVersion).OriginalValue = seen;
            }
            bool was = row.IsEnabled;
            DateTime? wasFrom = row.FromDate;
            DateTime? from = dto.FromDate?.Date ?? (was ? row.FromDate : null);
            if (dto.IsEnabled)
            {
                if (!from.HasValue) return Bad<SpTransferTypeDto>("تاریخ شروع انتقال را مشخص کنید؛ فقط اسنادی که از این روز به بعد نهایی می‌شوند منتقل می‌شوند.");
                if (from.Value.Year < 2000 || from.Value > DateTime.Today.AddYears(1)) return Bad<SpTransferTypeDto>("تاریخ شروع انتقال معتبر نیست.");
                if (!was || wasFrom != from)
                {
                    string overlap = await OverlapRefusalAsync(source, from.Value, ct);
                    if (overlap != null) return Bad<SpTransferTypeDto>(overlap);
                }
                row.FromDate = from;
            }
            row.IsEnabled = dto.IsEnabled;
            row.RequiresApproval = true;
            if (note != null) row.Note = note;
            row.ChangedByUserId = Me;
            row.ChangedAt = DateTimeOffset.Now;
            if (was != dto.IsEnabled || (dto.IsEnabled && wasFrom != row.FromDate))
                AuditNotes.Add(_uow, Me, nameof(SpTransferType), row.Id, "SepidarTypeSwitch", (dto.IsEnabled ? "انتقال به سپیدار برای «" : "انتقال به سپیدار خاموش شد برای «") + row.Title + (dto.IsEnabled ? "» روشن شد (از " + row.FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")." : "»."),
                    new { code = row.Code, enabled = was, fromDate = wasFrom }, new { code = row.Code, enabled = dto.IsEnabled, fromDate = row.FromDate });
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { AgentDb.Clear(_uow); return Bad<SpTransferTypeDto>(Concurrency); }
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { Me }, ct);
            return ResponseDtoHelper.Ok(ToDto(row, names, source));
        }

        /// <summary>The old monthly package kinds that carry the same data must not cover the period (the same data would reach Sepidar twice); null when there is no overlap.</summary>
        private async Task<string> OverlapRefusalAsync(SpSource source, DateTime from, CancellationToken ct)
        {
            FnOutboxKind[] kinds = source.OldKinds;
            if (kinds == null || kinds.Length == 0) return null;
            DateTime day = from.Date;
            bool covered = await _uow.Set<FnOutboxPackage>().AsNoTracking().AnyAsync(p => kinds.Contains(p.Kind) && p.Status != FnOutboxStatus.Superseded && p.Status != FnOutboxStatus.Rejected && p.ToDate >= day, ct);
            return covered
                ? "برای این بازه پیش‌تر " + source.OldKindsText + " ساخته شده است و همین داده از راه بستهٔ ماهانه به سپیدار می‌رود؛ برای جلوگیری از دوباره‌کاری، تاریخ شروع را پس از پایان آخرین بستهٔ ماهانه بگذارید یا بسته‌های ماهانهٔ آن بازه را کنار بگذارید (منسوخ کنید)."
                : null;
        }

        // ================================================================================== reading the queue

        private async Task<List<SpOutboxItemDto>> ToDtosAsync(List<SpOutboxItem> rows, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(i => new int?[] { i.ApprovedByUserId, i.RejectedByUserId, i.CancelledByUserId }), ct);
            HashSet<string> enabled = (await SpFacts.EnabledCodes(_uow).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
            return rows.Select(i => ToDto(i, names, enabled)).ToList();
        }

        private SpOutboxItemDto Fill(SpOutboxItemDto d, SpOutboxItem i, Dictionary<int, string> names, HashSet<string> enabled)
        {
            d.Id = i.Id; d.TransferTypeCode = i.TransferTypeCode; d.TransferTypeTitle = SpCatalog.Find(i.TransferTypeCode)?.Title ?? i.TransferTypeCode; d.SourceType = i.SourceType; d.SourceId = i.SourceId;
            d.SourceNumber = i.SourceNumber; d.Title = i.Title; d.Status = i.Status; d.StatusTitle = SpCatalog.StatusTitle(i.Status); d.IsCorrection = i.IsCorrection; d.CorrectsItemId = i.CorrectsItemId;
            d.SupersededByItemId = i.SupersededByItemId; d.PayloadHash = i.PayloadHash; d.PreparedAt = i.PreparedAt; d.PreparedByAgent = i.PreparedByAgent;
            d.ApprovedByName = names.Name(i.ApprovedByUserId); d.ApprovedAt = i.ApprovedAt; d.RejectedByName = names.Name(i.RejectedByUserId); d.RejectReason = i.RejectReason;
            d.CancelledByName = names.Name(i.CancelledByUserId); d.CancelReason = i.CancelReason; d.AttemptCount = i.AttemptCount; d.LastAttemptAt = i.LastAttemptAt; d.NextAttemptAt = i.NextAttemptAt;
            d.LastError = i.LastError; d.ExternalReference = i.ExternalReference; d.SentAt = i.SentAt; d.RowVersion = i.RowVersion == null ? null : Convert.ToBase64String(i.RowVersion);
            d.CannotDecideReason = WhyNotDecide(i, enabled.Contains(i.TransferTypeCode));
            d.CanDecide = d.CannotDecideReason == null;
            d.CannotFixReason = WhyNotFix(i);
            d.CanFix = d.CannotFixReason == null;
            return d;
        }

        private SpOutboxItemDto ToDto(SpOutboxItem i, Dictionary<int, string> names, HashSet<string> enabled) => Fill(new SpOutboxItemDto(), i, names, enabled);

        /// <summary>The reason (Persian) that the viewer cannot approve or reject the item now; null when the viewer can.</summary>
        private string WhyNotDecide(SpOutboxItem i, bool typeEnabled)
        {
            if (AiActing.IsActing) return SeatsDoNotDecide;
            if (!_user.IsInRole(SpRoles.Deciders)) return "تایید و رد فقط با مدیر مالی یا مدیرعامل است.";
            if (i.Status != SpOutboxStatus.AwaitingApproval) return "فقط قلم «منتظر تایید» تایید یا رد می‌شود.";
            if (!typeEnabled) return "انتقال این نوع سند خاموش است؛ ابتدا آن را روشن کنید.";
            if (SpPayload.IsMaker(i.SourceMakerUserIds, Me)) return "تاییدکننده نباید ایجادکننده یا ویرایشگر سند منبع باشد.";
            return null;
        }

        private string WhyNotFix(SpOutboxItem i)
        {
            if (AiActing.IsActing) return SeatsDoNotDecide;
            if (!_user.IsInRole(SpRoles.Deciders)) return "تلاش دوباره و لغو فقط با مدیر مالی یا مدیرعامل است.";
            if (i.Status == SpOutboxStatus.Sending && IsStale(i, DateTimeOffset.Now)) return null;   // an interrupted send: a person may try again or cancel
            if (i.Status != SpOutboxStatus.Failed && i.Status != SpOutboxStatus.Approved) return "فقط قلم «ناموفق»، «تاییدشدهٔ در انتظار ارسال» یا «در حال ارسالِ نیمه‌کاره» دوباره امتحان یا لغو می‌شود.";
            return null;
        }

        private static bool IsStale(SpOutboxItem i, DateTimeOffset now) => i.Status == SpOutboxStatus.Sending && i.LastAttemptAt != null && i.LastAttemptAt < now - SpFacts.StaleSending;

        /// <summary>The open attempts of an interrupted send are closed (failed) when the item leaves "sending" without a result.</summary>
        private async Task CloseOpenAttemptsAsync(SpOutboxItem item, DateTimeOffset now, CancellationToken ct)
        {
            foreach (SpOutboxAttempt open in await _uow.Set<SpOutboxAttempt>().Where(a => a.ItemId == item.Id && a.FinishedAt == null).ToListAsync(ct))
            { open.FinishedAt = now; open.Outcome = SpAttemptOutcome.Failed; open.Error = "ارسال نیمه‌کاره ماند."; }
        }

        public async Task<ResponseDto<PagedList<SpOutboxItemDto>>> GetItemsAsync(SpOutboxQuery query, CancellationToken ct)
        {
            query ??= new SpOutboxQuery();
            if (!CanRead) return Bad<PagedList<SpOutboxItemDto>>(NotAllowed);
            IQueryable<SpOutboxItem> q = Visible(_uow.Set<SpOutboxItem>().AsNoTracking());
            if (query.Id.HasValue) q = q.Where(i => i.Id == query.Id.Value);
            if (query.Status.HasValue) q = q.Where(i => i.Status == query.Status.Value);
            string type = Clean(query.TypeCode)?.ToUpperInvariant();
            if (type != null) q = q.Where(i => i.TransferTypeCode == type);
            if (query.From.HasValue) { DateTimeOffset from = new DateTimeOffset(query.From.Value.Date); q = q.Where(i => i.PreparedAt >= from); }
            if (query.To.HasValue) { DateTimeOffset to = new DateTimeOffset(query.To.Value.Date.AddDays(1)); q = q.Where(i => i.PreparedAt < to); }
            string search = Clean(query.Search);
            if (search != null) q = q.Where(i => i.SourceNumber.Contains(search) || i.Title.Contains(search));
            string sourceType = Clean(query.SourceType);
            if (sourceType != null) q = q.Where(i => i.SourceType == sourceType);
            if (query.SourceId.HasValue) q = q.Where(i => i.SourceId == query.SourceId.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize <= 0 ? 50 : Math.Min(query.PageSize, SpFacts.MaxPageSize), total, out int number, out int size);
            List<SpOutboxItem> rows = await q.OrderByDescending(i => i.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await ToDtosAsync(rows, ct), total, number, size));
        }

        public async Task<ResponseDto<SpOutboxItemDetailDto>> GetItemAsync(int id, CancellationToken ct)
        {
            if (!CanRead) return Bad<SpOutboxItemDetailDto>(NotAllowed);
            SpOutboxItem i = await Visible(_uow.Set<SpOutboxItem>().AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (i == null) return ResponseDtoHelper.NotFound<SpOutboxItemDetailDto>(NotFound);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { i.ApprovedByUserId, i.RejectedByUserId, i.CancelledByUserId }, ct);
            HashSet<string> enabled = (await SpFacts.EnabledCodes(_uow).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
            var d = (SpOutboxItemDetailDto)Fill(new SpOutboxItemDetailDto(), i, names, enabled);
            d.SchemaVersion = i.SchemaVersion;
            d.PayloadPretty = SpPayload.PrettyOf(i.PayloadJson);
            d.Attempts = (await _uow.Set<SpOutboxAttempt>().AsNoTracking().Where(a => a.ItemId == id).OrderBy(a => a.AttemptNumber).ThenBy(a => a.Id).ToListAsync(ct))
                .Select(a => new SpOutboxAttemptDto { AttemptNumber = a.AttemptNumber, StartedAt = a.StartedAt, FinishedAt = a.FinishedAt, Outcome = a.Outcome, Error = a.Error, SenderName = a.SenderName, ExternalReference = a.ExternalReference }).ToList();
            return ResponseDtoHelper.Ok(d);
        }

        public async Task<ResponseDto<SpOutboxSummaryDto>> GetSummaryAsync(CancellationToken ct)
        {
            if (!CanRead) return Bad<SpOutboxSummaryDto>(NotAllowed);
            IQueryable<SpOutboxItem> q = Visible(_uow.Set<SpOutboxItem>().AsNoTracking());
            var counts = await q.GroupBy(i => i.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
            var dto = new SpOutboxSummaryDto
            {
                SenderName = _sender.Name, IsConfigured = _sender.IsConfigured, MaxAttempts = MaxAttempts,
                LastSuccessfulSendAt = await q.Where(i => i.Status == SpOutboxStatus.Sent).MaxAsync(i => i.SentAt, ct),
                EnabledTypes = await SpFacts.EnabledCodes(_uow).CountAsync(ct),
            };
            foreach (SpOutboxStatus s in Enum.GetValues(typeof(SpOutboxStatus)).Cast<SpOutboxStatus>().OrderBy(s => (int)s))
                dto.Counts.Add(new SpStatusCountDto { Status = s, StatusTitle = SpCatalog.StatusTitle(s), Count = counts.Where(c => c.Status == s).Sum(c => c.Count) });
            dto.Total = dto.Counts.Sum(c => c.Count);
            return ResponseDtoHelper.Ok(dto);
        }

        // ================================================================================== the decisions (people)

        private static bool TryVersion(string text, out byte[] seen)
        {
            seen = null;
            if (string.IsNullOrEmpty(text)) return true;
            try { seen = Convert.FromBase64String(text); return true; }
            catch (FormatException) { return false; }
        }

        /// <summary>The row version that the person saw must be the one in the database; the original value is set so that the save compares it too.</summary>
        private string CheckVersion(SpOutboxItem i, string text)
        {
            if (i.RowVersion != null && string.IsNullOrEmpty(text)) return RowVersionMissing;
            if (!TryVersion(text, out byte[] seen)) return InvalidData;
            if (seen != null && i.RowVersion != null)
            {
                if (!i.RowVersion.AsSpan().SequenceEqual(seen)) return Concurrency;
                _uow.Entry(i).Property(x => x.RowVersion).OriginalValue = seen;
            }
            return null;
        }

        public async Task<ResponseDto<SpOutboxItemDto>> DecideAsync(SpDecideDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SpOutboxItemDto>(InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "approve" && action != "reject") return Bad<SpOutboxItemDto>("عملیات نامعتبر است.");
            string reason = Clean(dto.Reason);
            if (action == "reject" && reason == null) return Bad<SpOutboxItemDto>("علت رد را بنویسید.");
            if (reason != null && reason.Length > 1000) return Bad<SpOutboxItemDto>("علت حداکثر ۱۰۰۰ نویسه است.");
            ResponseDto<SpOutboxItem> done = await DecideCoreAsync(dto.Id, action, reason, dto.RowVersion, ct);
            if (!done.Success) return Bad<SpOutboxItemDto>(done.Message);
            return ResponseDtoHelper.Ok((await ToDtosAsync(new List<SpOutboxItem> { done.Data }, ct))[0]);
        }

        /// <summary>One decision, checked and saved on its own (so a refusal of one item never touches another).</summary>
        private async Task<ResponseDto<SpOutboxItem>> DecideCoreAsync(int id, string action, string reason, string rowVersion, CancellationToken ct)
        {
            if (AiActing.IsActing) return Bad<SpOutboxItem>(SeatsDoNotDecide);
            if (!_user.IsInRole(SpRoles.Deciders)) return Bad<SpOutboxItem>(NotAllowed);
            SpOutboxItem item = await Visible(_uow.Set<SpOutboxItem>()).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (item == null) return ResponseDtoHelper.NotFound<SpOutboxItem>(NotFound);
            bool enabled = await _uow.Set<SpTransferType>().AsNoTracking().AnyAsync(t => t.Code == item.TransferTypeCode && t.IsEnabled, ct);
            string why = WhyNotDecide(item, enabled);
            if (why != null) return Bad<SpOutboxItem>(why);
            string version = CheckVersion(item, rowVersion);
            if (version != null) return Bad<SpOutboxItem>(version);
            DateTimeOffset now = DateTimeOffset.Now;
            if (action == "approve")
            {
                // the source must still be what the item holds (a changed source is replaced by a new item, which the approver then sees)
                SpSource source = SpCatalog.Find(item.TransferTypeCode);
                SpBuilt built = source == null ? null : (await source.BuildAsync(_uow, new[] { item.SourceId }, ct)).FirstOrDefault();
                if (built == null || SpPayload.Hash(SpPayload.Canonical(source.Code, source.SourceType, built.Id, built.Number, built.Data)) != item.PayloadHash)
                    return Bad<SpOutboxItem>("سند منبع پس از تهیهٔ این قلم تغییر کرده است؛ ایجنت قلم تازه می‌سازد و این قلم تایید نمی‌شود.");
                item.Status = SpOutboxStatus.Approved; item.ApprovedByUserId = Me; item.ApprovedAt = now; item.NextAttemptAt = null;
            }
            else
            {
                item.Status = SpOutboxStatus.Rejected; item.RejectedByUserId = Me; item.RejectedAt = now; item.RejectReason = reason;
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { AgentDb.Clear(_uow); return Bad<SpOutboxItem>(Concurrency); }
            return ResponseDtoHelper.Ok(item);
        }

        public async Task<ResponseDto<SpBulkResultDto>> BulkApproveAsync(SpBulkApproveDto dto, CancellationToken ct)
        {
            if (dto == null || dto.Items == null || dto.Items.Count == 0) return Bad<SpBulkResultDto>(InvalidData);
            if (dto.Items.Count > 100) return Bad<SpBulkResultDto>("در هر بار حداکثر ۱۰۰ قلم تایید می‌شود.");
            if (AiActing.IsActing) return Bad<SpBulkResultDto>(SeatsDoNotDecide);
            if (!_user.IsInRole(SpRoles.Deciders)) return Bad<SpBulkResultDto>(NotAllowed);
            var result = new SpBulkResultDto { Requested = dto.Items.Count };
            var seen = new HashSet<int>();
            foreach (SpBulkRowDto row in dto.Items)
            {
                if (row == null || !seen.Add(row.Id))
                {
                    result.Refused++; result.Rows.Add(new SpBulkRowResultDto { Id = row?.Id ?? 0, Approved = false, Message = "قلم تکراری یا نامعتبر است." });
                    continue;
                }
                ResponseDto<SpOutboxItem> one = await DecideCoreAsync(row.Id, "approve", null, row.RowVersion, ct);
                if (one.Success) result.Approved++; else result.Refused++;
                result.Rows.Add(new SpBulkRowResultDto { Id = row.Id, Approved = one.Success, Message = one.Success ? null : one.Message });
                if (!one.Success) AgentDb.Clear(_uow);   // nothing of the refused row stays staged
            }
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<SpOutboxItemDto>> FixAsync(SpFixDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SpOutboxItemDto>(InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "retry" && action != "cancel") return Bad<SpOutboxItemDto>("عملیات نامعتبر است.");
            if (AiActing.IsActing) return Bad<SpOutboxItemDto>(SeatsDoNotDecide);
            if (!_user.IsInRole(SpRoles.Deciders)) return Bad<SpOutboxItemDto>(NotAllowed);
            string reason = Clean(dto.Reason);
            if (action == "cancel" && reason == null) return Bad<SpOutboxItemDto>("علت لغو را بنویسید.");
            if (reason != null && reason.Length > 1000) return Bad<SpOutboxItemDto>("علت حداکثر ۱۰۰۰ نویسه است.");
            SpOutboxItem item = await Visible(_uow.Set<SpOutboxItem>()).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (item == null) return ResponseDtoHelper.NotFound<SpOutboxItemDto>(NotFound);
            string why = WhyNotFix(item);
            if (why != null) return Bad<SpOutboxItemDto>(why);
            string version = CheckVersion(item, dto.RowVersion);
            if (version != null) return Bad<SpOutboxItemDto>(version);
            DateTimeOffset now = DateTimeOffset.Now;
            if (action == "retry")
            {
                bool enabled = await _uow.Set<SpTransferType>().AsNoTracking().AnyAsync(t => t.Code == item.TransferTypeCode && t.IsEnabled, ct);
                if (!enabled) return Bad<SpOutboxItemDto>("انتقال این نوع سند خاموش است؛ ابتدا آن را روشن کنید.");
                if (item.Status == SpOutboxStatus.Sending) await CloseOpenAttemptsAsync(item, now, ct);
                item.Status = SpOutboxStatus.Approved; item.NextAttemptAt = null; item.RetryBaseline = item.AttemptCount; item.LastError = null;
            }
            else
            {
                if (item.Status == SpOutboxStatus.Sending) await CloseOpenAttemptsAsync(item, now, ct);
                item.Status = SpOutboxStatus.Cancelled; item.CancelledByUserId = Me; item.CancelledAt = now; item.CancelReason = reason; item.NextAttemptAt = null;
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { AgentDb.Clear(_uow); return Bad<SpOutboxItemDto>(Concurrency); }
            return ResponseDtoHelper.Ok((await ToDtosAsync(new List<SpOutboxItem> { item }, ct))[0]);
        }

        // ================================================================================== the agent: prepare

        public async Task<ResponseDto<SpPrepareResult>> PrepareAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(SpRoles.Operators)) return Bad<SpPrepareResult>(NotAllowed);
            await SpCatalog.EnsureAsync(_uow, ct);
            var result = new SpPrepareResult();
            string agent = AiSeatContext.Code ?? AgentCodes.Fin;
            DateTimeOffset start = DateTimeOffset.Now;

            // an interrupted send of a kind that is off (or while no sender is configured) goes back to "approved"; it is not an attempt
            foreach (SpOutboxItem stuck in await SpFacts.StaleSendingToRecover(_uow, start, _sender.IsConfigured).AsTracking().OrderBy(i => i.Id).Take(SpFacts.Cap).ToListAsync(ct))
            {
                await CloseOpenAttemptsAsync(stuck, start, ct);
                stuck.Status = SpOutboxStatus.Approved; stuck.NextAttemptAt = null;
                result.Recovered++;
            }
            if (result.Recovered > 0)
            {
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { AgentDb.Clear(_uow); result.Recovered = 0; result.Errors++; }
            }

            Dictionary<string, DateTime?> enabled = await SpFacts.EnabledFromAsync(_uow, ct);
            foreach (SpSource source in SpCatalog.All.Where(s => enabled.ContainsKey(s.Code)))
            {
                ct.ThrowIfCancellationRequested();
                await CancelNoLongerFinalAsync(source, result, ct);
                List<int> ids = await SpFacts.NeedingWork(_uow, source, enabled[source.Code]).OrderByDescending(r => r.Id).Select(r => r.Id).Take(SpFacts.Cap).ToListAsync(ct);
                if (ids.Count == 0) continue;
                List<SpBuilt> built = await source.BuildAsync(_uow, ids, ct);
                // a document that is no longer transferable has nothing to build: it is not prepared
                result.Skipped += ids.Count - built.Count;
                foreach (SpBuilt b in built)
                {
                    try { await PrepareOneAsync(source, b, agent, result, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (DbUpdateException ex)
                    {
                        AgentDb.Clear(_uow);
                        result.Errors++;
                        _logger?.LogWarning(ex, "Sepidar outbox: {Kind} {Id} could not be prepared", source.Code, b.Id);
                    }
                }
            }
            return ResponseDtoHelper.Ok(result);
        }

        /// <summary>An item (waiting, approved or failed) whose source is no longer final is cancelled by the agent; the decision columns stay empty (an agent seat never decides), the reason says why.</summary>
        private async Task CancelNoLongerFinalAsync(SpSource source, SpPrepareResult result, CancellationToken ct)
        {
            List<SpOutboxItem> gone = await SpFacts.NoLongerFinal(_uow, source).AsTracking().OrderBy(i => i.Id).Take(SpFacts.Cap).ToListAsync(ct);
            if (gone.Count == 0) return;
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (SpOutboxItem i in gone) { i.Status = SpOutboxStatus.Cancelled; i.CancelledAt = now; i.CancelReason = NoLongerFinalReason; i.NextAttemptAt = null; }
            try { await _uow.SaveChangesAsync(ct); result.Cancelled += gone.Count; }
            catch (DbUpdateException ex)
            {
                AgentDb.Clear(_uow);
                result.Errors++;
                _logger?.LogWarning(ex, "Sepidar outbox: items of {Kind} could not be cancelled", source.Code);
            }
        }

        /// <summary>The reason written on an item whose source document is no longer final.</summary>
        public const string NoLongerFinalReason = "سند مبدأ دیگر نهایی نیست";

        private async Task PrepareOneAsync(SpSource source, SpBuilt b, string agent, SpPrepareResult result, CancellationToken ct)
        {
            string json = SpPayload.Canonical(source.Code, source.SourceType, b.Id, b.Number, b.Data);
            string hash = SpPayload.Hash(json);
            string code = source.Code, type = source.SourceType;
            int sourceId = b.Id;
            List<SpOutboxItem> mine = await _uow.Set<SpOutboxItem>().Where(i => i.TransferTypeCode == code && i.SourceType == type && i.SourceId == sourceId).OrderBy(i => i.Id).ToListAsync(ct);
            SpOutboxItem latest = mine.LastOrDefault(i => i.Status != SpOutboxStatus.Superseded);
            SpOutboxItem lastSent = mine.LastOrDefault(i => i.Status == SpOutboxStatus.Sent);
            DateTimeOffset now = DateTimeOffset.Now;

            // an item that is being sent is not touched at all (neither its row version nor its payload)
            if (latest != null && latest.Status == SpOutboxStatus.Sending) { result.Skipped++; return; }
            if (latest != null && latest.PayloadHash == hash)
            {
                // the same content: only the bookkeeping row version moves (so the discovery stops finding the document); the payload, the status and the decision are never touched
                if (!SpFacts.SameBytes(latest.SourceRowVersion, b.RowVersion)) { latest.SourceRowVersion = b.RowVersion; await _uow.SaveChangesAsync(ct); }
                result.Unchanged++;
                return;
            }

            // the live item of an older version of the document is replaced (never one that was sent)
            SpOutboxItem old = latest != null && SpCatalog.Live.Contains(latest.Status) ? latest : null;
            if (old != null)
            {
                old.Status = SpOutboxStatus.Superseded; old.NextAttemptAt = null;
                await _uow.SaveChangesAsync(ct);
                result.Superseded++;
            }
            if (lastSent != null && lastSent.PayloadHash == hash)
            {
                // the document is back to exactly what was sent: nothing to correct
                if (!SpFacts.SameBytes(lastSent.SourceRowVersion, b.RowVersion)) { lastSent.SourceRowVersion = b.RowVersion; await _uow.SaveChangesAsync(ct); }
                result.Unchanged++;
                return;
            }

            bool correction = lastSent != null;
            var item = new SpOutboxItem
            {
                TransferTypeCode = source.Code, SourceType = source.SourceType, SourceId = b.Id, SourceNumber = AgentText.Cut(b.Number, 60), Title = AgentText.Cut((correction ? "اصلاحیه: " : string.Empty) + b.Title, 300),
                PayloadJson = json, SchemaVersion = SpPayload.SchemaVersion, PayloadHash = hash, SourceRowVersion = b.RowVersion, IsCorrection = correction, CorrectsItemId = lastSent?.Id,
                Status = SpOutboxStatus.AwaitingApproval, PreparedByAgent = agent, PreparedByUserId = Me, PreparedAt = now, SourceMakerUserIds = b.Makers, PayrollRestricted = b.Restricted,
            };
            _uow.Set<SpOutboxItem>().Add(item);
            await _uow.SaveChangesAsync(ct);
            if (old != null)
            {
                old.SupersededByItemId = item.Id;
                await _uow.SaveChangesAsync(ct);
            }
            result.Prepared++;
            if (correction) result.Corrections++;
        }

        // ================================================================================== the agent: send

        public async Task<ResponseDto<SpSendOutcome>> SendAsync(int itemId, CancellationToken ct)
        {
            if (!_user.IsInRole(SpRoles.Operators)) return Bad<SpSendOutcome>(NotAllowed);
            // no sender: nothing is tried, nothing is counted, nothing is an error
            if (!_sender.IsConfigured) return ResponseDtoHelper.Ok(SpSendOutcome.NoSender);
            DateTimeOffset now = DateTimeOffset.Now;
            SpOutboxItem item = await _uow.Set<SpOutboxItem>().FirstOrDefaultAsync(x => x.Id == itemId, ct);
            if (item == null) return ResponseDtoHelper.NotFound<SpSendOutcome>(NotFound);
            bool enabled = await _uow.Set<SpTransferType>().AsNoTracking().AnyAsync(t => t.Code == item.TransferTypeCode && t.IsEnabled, ct);
            bool stuck = item.Status == SpOutboxStatus.Sending && item.LastAttemptAt != null && item.LastAttemptAt < now - SpFacts.StaleSending;
            bool due = item.Status == SpOutboxStatus.Approved || (item.Status == SpOutboxStatus.Failed && item.NextAttemptAt != null && item.NextAttemptAt <= now) || stuck;
            if (!enabled || !due) return ResponseDtoHelper.Ok(SpSendOutcome.NotDue);

            // the item is sent only while the document is still what it holds: otherwise it is replaced (the agent prepares a new one, a person approves it)
            SpSource source = SpCatalog.Find(item.TransferTypeCode);
            SpBuilt built = source == null ? null : (await source.BuildAsync(_uow, new[] { item.SourceId }, ct)).FirstOrDefault();
            if (built == null || SpPayload.Hash(SpPayload.Canonical(source.Code, source.SourceType, built.Id, built.Number, built.Data)) != item.PayloadHash)
            {
                if (stuck) await CloseOpenAttemptsAsync(item, now, ct);
                item.Status = SpOutboxStatus.Superseded; item.NextAttemptAt = null;
                item.LastError = built == null ? "سند منبع دیگر نهایی نیست." : "سند منبع پس از تایید تغییر کرده است.";
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { AgentDb.Clear(_uow); return ResponseDtoHelper.Ok(SpSendOutcome.Lost); }
                return ResponseDtoHelper.Ok(SpSendOutcome.Superseded);
            }

            // Sending: the row version keeps two runners of the same item apart
            if (stuck) await CloseOpenAttemptsAsync(item, now, ct);
            item.Status = SpOutboxStatus.Sending; item.AttemptCount++; item.LastAttemptAt = now; item.NextAttemptAt = null;
            var attempt = new SpOutboxAttempt { Item = item, AttemptNumber = item.AttemptCount, StartedAt = now, Outcome = SpAttemptOutcome.Started, SenderName = AgentText.Cut(_sender.Name, 50) };
            _uow.Set<SpOutboxAttempt>().Add(attempt);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { AgentDb.Clear(_uow); return ResponseDtoHelper.Ok(SpSendOutcome.Lost); }

            string correctsReference = item.CorrectsItemId.HasValue ? await _uow.Set<SpOutboxItem>().AsNoTracking().Where(x => x.Id == item.CorrectsItemId.Value).Select(x => x.ExternalReference).FirstOrDefaultAsync(ct) : null;
            var envelope = new SepidarEnvelope
            {
                ItemId = item.Id, IdempotencyKey = item.Id.ToString(CultureInfo.InvariantCulture) + ":" + item.PayloadHash, TransferTypeCode = item.TransferTypeCode, SourceType = item.SourceType, SourceId = item.SourceId,
                SourceNumber = item.SourceNumber, SchemaVersion = item.SchemaVersion, PayloadHash = item.PayloadHash, PayloadJson = item.PayloadJson, IsCorrection = item.IsCorrection,
                CorrectsExternalReference = correctsReference, PreparedAt = item.PreparedAt,
            };
            SpSendResult sent;
            try { sent = await _sender.SendAsync(envelope, ct) ?? SpSendResult.Fail("فرستنده پاسخی نداد."); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // the text for people never carries the message of the exception (it may hold a path, an address or a secret); the type name goes to the log
                _logger?.LogWarning("Sepidar sender {Sender} failed with {Type}", _sender.Name, ex.GetType().Name);
                sent = SpSendResult.Fail("ارسال به سپیدار با خطا روبه‌رو شد.");
            }

            DateTimeOffset end = DateTimeOffset.Now;
            attempt.FinishedAt = end;
            if (sent.Success)
            {
                attempt.Outcome = SpAttemptOutcome.Succeeded; attempt.ExternalReference = AgentText.Cut(sent.ExternalReference, 200);
                item.Status = SpOutboxStatus.Sent; item.ExternalReference = attempt.ExternalReference; item.SentAt = end; item.LastError = null; item.NextAttemptAt = null;
            }
            else
            {
                string error = AgentText.Cut(SafeError(sent.Error), 400);
                attempt.Outcome = SpAttemptOutcome.Failed; attempt.Error = error;
                item.Status = SpOutboxStatus.Failed; item.LastError = error;
                TimeSpan? delay = SpFacts.NextDelay(item.AttemptCount - item.RetryBaseline, MaxAttempts);
                item.NextAttemptAt = delay.HasValue ? end + delay.Value : (DateTimeOffset?)null;
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { AgentDb.Clear(_uow); return ResponseDtoHelper.Ok(SpSendOutcome.Lost); }
            return ResponseDtoHelper.Ok(sent.Success ? SpSendOutcome.Sent : SpSendOutcome.Failed);
        }

        /// <summary>A short text for people: one line, no stack trace.</summary>
        private static string SafeError(string text)
        {
            string line = Clean(text) ?? "ارسال ناموفق بود.";
            int nl = line.IndexOfAny(new[] { '\r', '\n' });
            return nl >= 0 ? line.Substring(0, nl) : line;
        }
    }
}
