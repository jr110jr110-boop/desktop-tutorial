using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Ai;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Ai
{
    /// <summary>The target types of a note.</summary>
    public static class AiNoteTypes
    {
        public const string QualityNcr = "QualityNcr";
        public const string FinanceJournal = "FinanceJournal";
        public const string BankLine = "BankLine";
        /// <summary>The IBAN of a supplier changed; TargetId is the id of the finance audit row of that change (one note per change).</summary>
        public const string SupplierIbanChange = "SupplierIbanChange";
        public const string PurchaseRfq = "PurchaseRfq";
        public const string OfficeLetter = "OfficeLetter";
        public const string OfficeMeeting = "OfficeMeeting";
        public const string PcWeeklyReport = "PcWeeklyReport";
        public const string CeoBrief = "CeoBrief";

        public static readonly string[] All =
        {
            QualityNcr, FinanceJournal, BankLine, SupplierIbanChange, PurchaseRfq, OfficeLetter, OfficeMeeting, PcWeeklyReport, CeoBrief,
        };

        /// <summary>The type needs the office service to say who may read the record.</summary>
        public static bool IsOffice(string type) => type == OfficeLetter || type == OfficeMeeting;

        /// <summary>
        /// The roles that read (and decide on) the notes of a type; null for an office type (the office service decides per record).
        /// Finance notes are for the finance roles only (★ the CEO and the PMO read the brief and the reports, not the finance findings).
        /// </summary>
        public static string Roles(string type)
        {
            switch (type)
            {
                case QualityNcr: return InventoryRoles.Qc;
                case FinanceJournal:
                case BankLine:
                case SupplierIbanChange: return "FINANCEMANAGER,FINANCIAL";
                case PurchaseRfq: return "COMMERCIAL,PMO,FINANCIAL,FINANCEMANAGER,TREASURY,ENGINEERING,QC,CONSTRUCTION,CEO";
                case PcWeeklyReport: return "PMO,PLANNING";
                case CeoBrief: return "CEO"; // the brief is written as the CEO (it may hold what only the CEO sees), so only the CEO reads it
                default: return null;
            }
        }

        public static string Link(string type, int? id)
        {
            if (!id.HasValue) return null;
            switch (type)
            {
                case QualityNcr: return "/inventory/ncr/" + id.Value.ToString(CultureInfo.InvariantCulture);
                case FinanceJournal: return "/fn/vouchers/" + id.Value.ToString(CultureInfo.InvariantCulture);
                case BankLine: return "/fn/treasury/statements";
                case SupplierIbanChange: return "/procurement/suppliers";
                case PurchaseRfq: return "/procurement/inquiries/" + id.Value.ToString(CultureInfo.InvariantCulture);
                case OfficeLetter: return "/office/letters/" + id.Value.ToString(CultureInfo.InvariantCulture);
                case OfficeMeeting: return "/office/meetings/" + id.Value.ToString(CultureInfo.InvariantCulture);
                case PcWeeklyReport: return "/project-control/reports";
                default: return null;
            }
        }
    }

    public interface IAiNoteWriter
    {
        /// <summary>Adds the note unless the seat already has one for this target (idempotent: also safe when two runs race). Returns true when a row was created.</summary>
        Task<bool> TryAddAsync(AiNote note, CancellationToken ct);

        /// <summary>The targets of a type the seat already has a note for.</summary>
        Task<HashSet<int>> ExistingTargetsAsync(string seatCode, string targetType, IEnumerable<int> targetIds, CancellationToken ct, string ruleCode = null);
    }

    public class AiNoteWriter : IAiNoteWriter
    {
        private readonly IUnitOfWork _uow;

        public AiNoteWriter(IUnitOfWork uow)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        }

        public async Task<bool> TryAddAsync(AiNote note, CancellationToken ct)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            if (note.TargetId.HasValue && await _uow.Set<AiNote>().AnyAsync(n => n.SeatCode == note.SeatCode && n.TargetType == note.TargetType && n.TargetId == note.TargetId && n.RuleCode == note.RuleCode, ct)) return false;
            note.Title = AiText.Cut(AiText.YeKe(note.Title), 300);
            note.Text = AiText.YeKe(note.Text);
            note.Status = AiNoteStatus.New;
            note.CreatedAt = DateTimeOffset.Now;
            _uow.Set<AiNote>().Add(note);
            try
            {
                await _uow.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                // the other run won the unique index: nothing to do
                _uow.Entry(note).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<HashSet<int>> ExistingTargetsAsync(string seatCode, string targetType, IEnumerable<int> targetIds, CancellationToken ct, string ruleCode = null)
        {
            List<int> ids = targetIds.Distinct().ToList();
            if (ids.Count == 0) return new HashSet<int>();
            var found = new HashSet<int>();
            // chunks keep the IN list short
            foreach (int[] chunk in ids.Chunk(500))
            {
                List<int> part = await _uow.Set<AiNote>().AsNoTracking().Where(n => n.SeatCode == seatCode && n.TargetType == targetType && n.RuleCode == ruleCode && n.TargetId.HasValue && chunk.Contains(n.TargetId.Value))
                    .Select(n => n.TargetId.Value).ToListAsync(ct);
                foreach (int x in part) found.Add(x);
            }
            return found;
        }
    }

    public interface IAiNoteService
    {
        Task<ResponseDto<PagedList<AiNoteDto>>> GetNotesAsync(AiNoteQuery query, CancellationToken ct);
        Task<ResponseDto<AiNoteDto>> GetNoteAsync(int id, CancellationToken ct);
        Task<ResponseDto<AiNoteDto>> DecideAsync(AiDecideNoteDto dto, CancellationToken ct);
        Task<ResponseDto<AiBriefDto>> LatestBriefAsync(CancellationToken ct);

        /// <summary>The latest brief as speech (WAV). The audio is made on demand and not stored.</summary>
        Task<ResponseDto<byte[]>> LatestBriefAudioAsync(CancellationToken ct);
    }

    /// <summary>
    /// What the seats left for people. A note is read and decided only by the people of the module it belongs to; accepting it never changes the record it is about
    /// (the person does that in the module, behind the module's own gate). A seat user can never decide.
    /// </summary>
    public class AiNoteService : IAiNoteService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IAiAudit _audit;
        private readonly ILetterService _letters;
        private readonly IMeetingService _meetings;
        private readonly ITextToSpeech _tts;
        private readonly IPersianNumberWords _words;
        private readonly IHttpContextAccessor _http;

        public AiNoteService(IUnitOfWork uow, IInventoryUserContext user, IAiAudit audit, ILetterService letters, IMeetingService meetings, ITextToSpeech tts,
            IPersianNumberWords words, IHttpContextAccessor http)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _letters = letters ?? throw new ArgumentNullException(nameof(letters));
            _meetings = meetings ?? throw new ArgumentNullException(nameof(meetings));
            _tts = tts ?? throw new ArgumentNullException(nameof(tts));
            _words = words ?? throw new ArgumentNullException(nameof(words));
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        private int Me => _user.UserId;

        private bool IsSeatCaller => AiActing.IsActing || AiSeatNames.IsSeatUser(_http.HttpContext?.User?.Identity?.Name);

        private async Task<bool> CanSeeOfficeAsync(AiNote n, CancellationToken ct)
        {
            if (!n.TargetId.HasValue) return false;
            if (n.TargetType == AiNoteTypes.OfficeLetter)
            {
                var r = await _letters.GetLetterAsync(n.TargetId.Value, ct);
                // the notes follow the letter: a letter the caller reads only as register data (a Secret one for the secretariat, since the office answers of 1405/08 the secretariat reads Confidential content in full) shows no notes either
                return r.Success && r.Data != null && !r.Data.MetadataOnly;
            }
            var m = await _meetings.GetMeetingAsync(n.TargetId.Value, ct);
            return m.Success && m.Data != null;
        }

        private async Task<bool> CanSeeAsync(AiNote n, CancellationToken ct)
        {
            if (AiNoteTypes.IsOffice(n.TargetType)) return await CanSeeOfficeAsync(n, ct);
            string roles = AiNoteTypes.Roles(n.TargetType);
            return roles != null && _user.IsInRole(roles);
        }

        private async Task<AiNoteDto> ToDtoAsync(AiNote n, CancellationToken ct)
        {
            AiNoteDto d = Map(n);
            if (n.DecidedByUserId.HasValue) d.DecidedByName = await NameAsync(n.DecidedByUserId.Value, ct);
            return d;
        }

        private static AiNoteDto Map(AiNote n) => new AiNoteDto
        {
            Id = n.Id, SeatCode = n.SeatCode, TargetType = n.TargetType, TargetId = n.TargetId, RuleCode = n.RuleCode, Title = n.Title, Text = n.Text, Status = n.Status, CreatedAt = n.CreatedAt,
            CreatedAtText = AiText.Stamp(n.CreatedAt), DecidedAt = n.DecidedAt, DecidedAtText = n.DecidedAt.HasValue ? AiText.Stamp(n.DecidedAt.Value) : null,
            DecidedByUserId = n.DecidedByUserId, DecisionNote = n.DecisionNote, Verified = n.Verified, SeatRunId = n.SeatRunId, Link = AiNoteTypes.Link(n.TargetType, n.TargetId),
        };

        private async Task<string> NameAsync(int userId, CancellationToken ct)
        {
            var u = await _uow.Set<User>().AsNoTracking().Where(x => x.Id == userId).Select(x => new { x.Name, x.LastName, x.UserName }).FirstOrDefaultAsync(ct);
            if (u == null) return null;
            string name = ((u.Name ?? string.Empty) + " " + (u.LastName ?? string.Empty)).Trim();
            return name.Length == 0 ? u.UserName : name;
        }

        public async Task<ResponseDto<PagedList<AiNoteDto>>> GetNotesAsync(AiNoteQuery query, CancellationToken ct)
        {
            if (Me == 0) return AiText.Bad<PagedList<AiNoteDto>>(AiText.NotAllowed);
            query ??= new AiNoteQuery();

            // types read by role; the office types are decided note by note by the office service
            string[] roleTypes = AiNoteTypes.All.Where(t => !AiNoteTypes.IsOffice(t) && _user.IsInRole(AiNoteTypes.Roles(t))).ToArray();
            IQueryable<AiNote> baseQuery = _uow.Set<AiNote>().AsNoTracking();
            // one type or several separated by commas (the finance page asks for its three types at once)
            string[] types = (AiText.Clean(query.TargetType) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (types.Length == 1) { string type = types[0]; baseQuery = baseQuery.Where(n => n.TargetType == type); }
            else if (types.Length > 1) baseQuery = baseQuery.Where(n => types.Contains(n.TargetType));
            if (query.TargetId.HasValue) baseQuery = baseQuery.Where(n => n.TargetId == query.TargetId.Value);
            if (query.Status.HasValue) baseQuery = baseQuery.Where(n => n.Status == query.Status.Value);
            string seat = AiText.Clean(query.Seat);
            if (seat != null) baseQuery = baseQuery.Where(n => n.SeatCode == seat);
            string rule = AiText.Clean(query.Rule);
            if (rule != null) baseQuery = baseQuery.Where(n => n.RuleCode == rule);

            // the office notes: the office services say which letters and meetings the caller may read, as queries that are joined to the notes (one SQL statement, no limit)
            IQueryable<AiNote> rows;
            if (types.Length == 0 || types.Any(AiNoteTypes.IsOffice))
            {
                IQueryable<int> letterIds = await _letters.ReadableLetterIdsAsync(ct);
                IQueryable<int> meetingIds = await _meetings.VisibleMeetingIdsAsync(ct);
                rows = baseQuery.Where(n => roleTypes.Contains(n.TargetType)
                    || (n.TargetType == AiNoteTypes.OfficeLetter && n.TargetId != null && letterIds.Contains(n.TargetId.Value))
                    || (n.TargetType == AiNoteTypes.OfficeMeeting && n.TargetId != null && meetingIds.Contains(n.TargetId.Value)));
            }
            else rows = baseQuery.Where(n => roleTypes.Contains(n.TargetType));
            rows = rows.OrderByDescending(n => n.Id);
            PagedList<AiNote> page = await AiText.PageAsync(rows, query.PageNumber, query.PageSize, ct);
            var names = new Dictionary<int, string>();
            foreach (int uid in page.Where(n => n.DecidedByUserId.HasValue).Select(n => n.DecidedByUserId.Value).Distinct()) names[uid] = await NameAsync(uid, ct);
            PagedList<AiNoteDto> dtos = await PagedList<AiNoteDto>.CreateAsync(page.Select(n =>
            {
                AiNoteDto d = Map(n);
                if (n.DecidedByUserId.HasValue && names.TryGetValue(n.DecidedByUserId.Value, out string nm)) d.DecidedByName = nm;
                return d;
            }).ToList(), 1, 0, ct);
            dtos.TotalCount = page.TotalCount; dtos.CurrentPage = page.CurrentPage; dtos.PageSize = page.PageSize; dtos.TotalPages = page.TotalPages;
            return ResponseDtoHelper.Ok(dtos);
        }

        public async Task<ResponseDto<AiNoteDto>> GetNoteAsync(int id, CancellationToken ct)
        {
            if (Me == 0) return AiText.Bad<AiNoteDto>(AiText.NotAllowed);
            AiNote n = await _uow.Set<AiNote>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (n == null) return AiText.NotFound<AiNoteDto>(NoteNotFound);
            // a note the caller may not read looks like a missing one
            if (!await CanSeeAsync(n, ct)) return AiText.NotFound<AiNoteDto>(NoteNotFound);
            return ResponseDtoHelper.Ok(await ToDtoAsync(n, ct));
        }

        public const string NoteNotFound = "یادداشت پیدا نشد.";

        public async Task<ResponseDto<AiNoteDto>> DecideAsync(AiDecideNoteDto dto, CancellationToken ct)
        {
            if (Me == 0 || IsSeatCaller) return AiText.Bad<AiNoteDto>(AiText.NotAllowed);
            if (dto == null || dto.Id <= 0) return AiText.Bad<AiNoteDto>(AiText.InvalidData);
            string decision = (dto.Decision ?? string.Empty).Trim().ToLowerInvariant();
            if (decision != "accept" && decision != "dismiss") return AiText.Bad<AiNoteDto>("تصمیم باید «پذیرش» (accept) یا «رد» (dismiss) باشد.");
            string note = AiText.Clean(dto.Note);
            if (note != null && note.Length > 1000) return AiText.Bad<AiNoteDto>("توضیح تصمیم نباید از ۱۰۰۰ نویسه بیشتر باشد.");

            AiNote n = await _uow.Set<AiNote>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (n == null) return AiText.NotFound<AiNoteDto>(NoteNotFound);
            if (!await CanSeeAsync(n, ct)) return AiText.Bad<AiNoteDto>(AiText.NotAllowed);
            if (n.Status != AiNoteStatus.New) return AiText.Bad<AiNoteDto>("درباره این یادداشت قبلاً تصمیم گرفته شده است.");

            // accepting or dismissing only closes the note: the record it is about is never changed here
            n.Status = decision == "accept" ? AiNoteStatus.Accepted : AiNoteStatus.Dismissed;
            n.DecidedAt = DateTimeOffset.Now;
            n.DecidedByUserId = Me;
            n.DecisionNote = AiText.YeKe(note);
            _audit.Add(AiAuditKinds.Note, $"note {n.Id} {n.TargetType}/{n.TargetId?.ToString(CultureInfo.InvariantCulture) ?? "-"} {decision}", seatCode: n.SeatCode);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(n, ct));
        }

        private async Task<(AiNote Note, string Error)> LatestBriefNoteAsync(CancellationToken ct)
        {
            if (Me == 0 || !_user.IsInRole(AiNoteTypes.Roles(AiNoteTypes.CeoBrief))) return (null, AiText.NotAllowed);
            AiNote n = await _uow.Set<AiNote>().AsNoTracking().Where(x => x.TargetType == AiNoteTypes.CeoBrief && x.TargetId != null)
                .OrderByDescending(x => x.TargetId).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            return n == null ? (null, "هنوز گزارش روزانه‌ای ساخته نشده است.") : (n, null);
        }

        /// <summary>The day of a brief: its target id is yyyymmdd.</summary>
        public static DateTime? BriefDate(int? targetId)
        {
            if (!targetId.HasValue) return null;
            int v = targetId.Value;
            int y = v / 10000, m = v / 100 % 100, d = v % 100;
            if (y < 1900 || y > 2999 || m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(y, m)) return null;
            return new DateTime(y, m, d);
        }

        public async Task<ResponseDto<AiBriefDto>> LatestBriefAsync(CancellationToken ct)
        {
            var (n, error) = await LatestBriefNoteAsync(ct);
            if (n == null) return error == AiText.NotAllowed ? AiText.Bad<AiBriefDto>(error) : AiText.NotFound<AiBriefDto>(error);
            DateTime date = BriefDate(n.TargetId) ?? n.CreatedAt.LocalDateTime.Date;
            return ResponseDtoHelper.Ok(new AiBriefDto { Date = date, DateText = InventoryText.PersianDate(date), Text = n.Text, Verified = n.Verified, NoteId = n.Id });
        }

        public async Task<ResponseDto<byte[]>> LatestBriefAudioAsync(CancellationToken ct)
        {
            var (n, error) = await LatestBriefNoteAsync(ct);
            if (n == null) return error == AiText.NotAllowed ? AiText.Bad<byte[]>(error) : AiText.NotFound<byte[]>(error);
            TtsResult tts = await _tts.SynthesizeAsync(_words.SpeechNormalize(n.Text), ct);
            if (tts == null || !tts.Ok || tts.Audio == null || tts.Audio.Length == 0) return AiText.Bad<byte[]>(AiText.VoiceNoAudio);
            return ResponseDtoHelper.Ok(tts.Audio);
        }
    }
}
