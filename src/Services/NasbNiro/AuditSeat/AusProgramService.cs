using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.AuditSeat;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Integrity;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.AuditSeatDtos;

namespace Services.NasbNiro.AuditSeat
{
    public interface IAusProgramService
    {
        Task<ResponseDto<List<AusProgramDto>>> GetAsync(CancellationToken ct);
        Task<ResponseDto<AusProgramDto>> GetOneAsync(int id, CancellationToken ct);
        Task<ResponseDto<AusProgramDto>> SaveAsync(AusProgramSaveDto dto, CancellationToken ct);
        Task<ResponseDto<AusProgramDto>> ActionAsync(AusProgramActionDto dto, CancellationToken ct);
        Task<ResponseDto<AusProgramDto>> DefaultDraftAsync(int year, CancellationToken ct);
    }

    /// <summary>
    /// ISO 19011 clause 5: the audit programme of the year (scope, criteria, objectives and the frequency per process). The PMO prepares and submits it, the QA manager approves it (another person); an approved programme never changes:
    /// a new version replaces it and the old one is marked Superseded. The daily automatic checks of the catalogue run for every process whatever the programme says; the programme plans the process audits by human auditors.
    /// </summary>
    public class AusProgramService : IAusProgramService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public AusProgramService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);

        public static string FrequencyTitle(AusFrequency f)
        {
            switch (f)
            {
                case AusFrequency.Continuous: return "پیوسته (روزانه)";
                case AusFrequency.Monthly: return "ماهانه";
                case AusFrequency.Quarterly: return "فصلی";
                case AusFrequency.HalfYearly: return "شش‌ماهه";
                default: return "سالانه";
            }
        }

        public static int DefaultInterval(AusFrequency f) => f == AusFrequency.Continuous ? 1 : f == AusFrequency.Monthly ? 30 : f == AusFrequency.Quarterly ? 91 : f == AusFrequency.HalfYearly ? 182 : 365;

        private static string StatusText(AusProgram p) => p.Status == AusProgramStatus.Draft ? (p.SubmittedAt.HasValue ? "ارسال‌شده - منتظر تایید QA" : "پیش‌نویس") : p.Status == AusProgramStatus.Approved ? "تاییدشده" : p.Status == AusProgramStatus.Superseded ? "جایگزین‌شده" : "رد شده";

        private async Task<AusProgramDto> ToDtoAsync(AusProgram p, bool withItems, CancellationToken ct)
        {
            Dictionary<int, string> names = await AusNames.LoadAsync(_uow, new int?[] { p.PreparedByUserId, p.DecidedByUserId }, ct);
            var d = new AusProgramDto
            {
                Id = p.Id, Year = p.Year, Version = p.Version, Title = p.Title, Scope = p.Scope, Criteria = p.Criteria, Objectives = p.Objectives, Status = StatusText(p), StatusValue = (int)p.Status, PreparedByName = AusNames.Of(names, p.PreparedByUserId),
                SubmittedAtText = p.SubmittedAt.HasValue ? AusRunService.Stamp(p.SubmittedAt.Value) : null, DecidedByName = AusNames.Of(names, p.DecidedByUserId), DecidedAtText = p.DecidedAt.HasValue ? AusRunService.Stamp(p.DecidedAt.Value) : null, DecisionNote = p.DecisionNote, ContentSha256 = p.ContentSha256,
            };
            if (withItems)
            {
                List<AusProgramItem> items = await _uow.Set<AusProgramItem>().AsNoTracking().Where(i => i.ProgramId == p.Id && !i.Deleted).OrderBy(i => i.Id).ToListAsync(ct);
                d.Items = items.Select(i => new AusProgramItemDto
                {
                    Id = i.Id, ProcessCode = i.ProcessCode, ProcessTitle = AusProcesses.TitleOf(i.ProcessCode), Title = i.Title, Frequency = (int)i.Frequency, FrequencyTitle = FrequencyTitle(i.Frequency), IntervalDays = i.IntervalDays, Criteria = i.Criteria,
                    FirstDue = i.FirstDue, FirstDueText = i.FirstDue.HasValue ? AusEvidence.Day(i.FirstDue.Value) : null, Rules = AusCatalog.All.Count(r => r.ProcessCode == i.ProcessCode),
                }).ToList();
            }
            bool open = p.Status == AusProgramStatus.Draft;
            if (open && !p.SubmittedAt.HasValue && _user.IsInRole(AusRoles.Pmo + "," + AusRoles.Qa)) { d.Allowed.Add("edit"); if (_user.IsInRole(AusRoles.Pmo)) d.Allowed.Add("submit"); }
            if (open && p.SubmittedAt.HasValue && _user.IsInRole(AusRoles.Qa) && p.PreparedByUserId != Me && !AiActing.IsActing) { d.Allowed.Add("approve"); d.Allowed.Add("reject"); }
            return d;
        }

        public async Task<ResponseDto<List<AusProgramDto>>> GetAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(AusRoles.Readers)) return Bad<List<AusProgramDto>>(QualityText.NotAllowed);
            List<AusProgram> rows = await _uow.Set<AusProgram>().AsNoTracking().Where(p => !p.Deleted).OrderByDescending(p => p.Year).ThenByDescending(p => p.Version).Take(100).ToListAsync(ct);
            var list = new List<AusProgramDto>();
            foreach (AusProgram p in rows) list.Add(await ToDtoAsync(p, false, ct));
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<AusProgramDto>> GetOneAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(AusRoles.Readers)) return Bad<AusProgramDto>(QualityText.NotAllowed);
            AusProgram p = await _uow.Set<AusProgram>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            return p == null ? QualityText.NotFound<AusProgramDto>("برنامه‌ی ممیزی پیدا نشد.") : ResponseDtoHelper.Ok(await ToDtoAsync(p, true, ct));
        }

        public static List<AusProgramItemDto> DefaultItems(int year)
        {
            var items = new List<AusProgramItemDto>();
            var quarterly = new HashSet<string> { "INTEGRITY", "QUALITY", "SPECIAL", "DESIGN", "CHANGE" };
            foreach (AusProcess pr in AusProcesses.All)
            {
                int n = AusCatalog.All.Count(r => r.ProcessCode == pr.Code);
                if (n > 0)
                    items.Add(new AusProgramItemDto { ProcessCode = pr.Code, Title = "کنترل خودکار روزانه‌ی " + pr.Title, Frequency = (int)AusFrequency.Continuous, IntervalDays = 1, Criteria = string.Join("، ", AusCatalog.All.Where(r => r.ProcessCode == pr.Code).Select(r => r.Standard + " " + r.Clause).Distinct().OrderBy(x => x, StringComparer.Ordinal)) });
                AusFrequency f = quarterly.Contains(pr.Code) ? AusFrequency.Quarterly : AusFrequency.Yearly;
                items.Add(new AusProgramItemDto { ProcessCode = pr.Code, Title = "ممیزی فرایند " + pr.Title, Frequency = (int)f, IntervalDays = DefaultInterval(f), Criteria = "بندهای استاندارد و رویه‌های شرکت در فهرست قاعده‌های این فرایند" });
            }
            return items;
        }

        public async Task<ResponseDto<AusProgramDto>> DefaultDraftAsync(int year, CancellationToken ct)
        {
            return await SaveAsync(new AusProgramSaveDto
            {
                Year = year, Title = "برنامه‌ی ممیزی داخلی سال " + year,
                Scope = "همه‌ی فرایندهای شرکت: " + string.Join("، ", AusProcesses.All.Select(p => p.Title)),
                Criteria = "استانداردهای ISO 9001، ISO 10007، ISO 19011، ISO/IEC 27001، ISO 21502، EIA-748 و رویه‌های مستند شرکت (فهرست بندها در قاعده‌های ممیزی)",
                Objectives = "اطمینان از اجرای رویه‌ها و استانداردها، شناسایی هر انحراف هرچند کوچک، پیگیری تا بستن با سه نفر، جلوگیری از تکرار",
                Items = DefaultItems(year),
            }, ct);
        }

        public async Task<ResponseDto<AusProgramDto>> SaveAsync(AusProgramSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<AusProgramDto>(QualityText.InvalidData);
            if (AiActing.IsActing) return Bad<AusProgramDto>("برنامه‌ی ممیزی را انسان تنظیم می‌کند؛ صندلی هوش مصنوعی آن را تایید یا تنظیم نمی‌کند.");
            if (!_user.IsInRole(AusRoles.Pmo + "," + AusRoles.Qa)) return Bad<AusProgramDto>(QualityText.NotAllowed);
            string title = QualityText.Clean(dto.Title), scope = QualityText.Clean(dto.Scope), criteria = QualityText.Clean(dto.Criteria), objectives = QualityText.Clean(dto.Objectives);
            if (dto.Year < 1400 || dto.Year > 1500) return Bad<AusProgramDto>("سال شمسی برنامه نامعتبر است.");
            if (title == null || scope == null || criteria == null || objectives == null) return Bad<AusProgramDto>("عنوان، دامنه، معیارها و اهداف برنامه را بنویسید (ISO 19011 بند 5.2 و 5.3).");
            if (dto.Items == null || dto.Items.Count == 0) return Bad<AusProgramDto>("برنامه دست‌کم یک ردیف فرایند می‌خواهد.");
            foreach (AusProgramItemDto i in dto.Items)
            {
                if (AusProcesses.Find(i.ProcessCode) == null) return Bad<AusProgramDto>("فرایند نامعتبر: " + i.ProcessCode);
                if (!Enum.IsDefined(typeof(AusFrequency), i.Frequency)) return Bad<AusProgramDto>("بسامد نامعتبر است.");
                if (QualityText.Clean(i.Title) == null) return Bad<AusProgramDto>("عنوان هر ردیف برنامه لازم است.");
                if (i.IntervalDays < 1 || i.IntervalDays > 400) return Bad<AusProgramDto>("فاصله‌ی ممیزی بین ۱ و ۴۰۰ روز باشد.");
            }
            DateTimeOffset now = DateTimeOffset.Now;
            AusProgram p;
            if (dto.Id.HasValue)
            {
                p = await _uow.Set<AusProgram>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (p == null) return QualityText.NotFound<AusProgramDto>("برنامه‌ی ممیزی پیدا نشد.");
                if (p.Status != AusProgramStatus.Draft || p.SubmittedAt.HasValue) return Bad<AusProgramDto>("برنامه‌ی ارسال‌شده یا تصمیم‌گرفته‌شده تغییر نمی‌کند؛ نسخه‌ی تازه بسازید.");
                if (p.PreparedByUserId != Me) return Bad<AusProgramDto>("پیش‌نویس را فقط تنظیم‌کننده‌اش ویرایش می‌کند.");
                List<AusProgramItem> old = await _uow.Set<AusProgramItem>().Where(i => i.ProgramId == p.Id && !i.Deleted).ToListAsync(ct);
                foreach (AusProgramItem o in old) o.Delete();
            }
            else
            {
                int version = (await _uow.Set<AusProgram>().Where(x => x.Year == dto.Year).Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
                AusProgram prev = await _uow.Set<AusProgram>().AsNoTracking().Where(x => x.Year == dto.Year && !x.Deleted && x.Status == AusProgramStatus.Approved).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
                if (await _uow.Set<AusProgram>().AnyAsync(x => x.Year == dto.Year && !x.Deleted && x.Status == AusProgramStatus.Draft, ct)) return Bad<AusProgramDto>("برای این سال پیش‌نویس یا برنامه‌ی در انتظار تایید وجود دارد.");
                p = new AusProgram { Year = dto.Year, Version = version, Status = AusProgramStatus.Draft, PreparedByUserId = Me, SupersedesId = prev?.Id, CreateDate = now };
                _uow.Set<AusProgram>().Add(p);
            }
            p.Title = title; p.Scope = scope; p.Criteria = criteria; p.Objectives = objectives; p.ModifyDate = now;
            foreach (AusProgramItemDto i in dto.Items)
                _uow.Set<AusProgramItem>().Add(new AusProgramItem { Program = p, ProcessCode = i.ProcessCode.Trim().ToUpperInvariant(), Title = QualityText.Clean(i.Title), Frequency = (AusFrequency)i.Frequency, IntervalDays = i.IntervalDays, Criteria = QualityText.Clean(i.Criteria), FirstDue = i.FirstDue?.Date, CreateDate = now, ModifyDate = now });
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<AusProgramDto>(QualityText.Concurrency); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(p, true, ct));
        }

        public async Task<ResponseDto<AusProgramDto>> ActionAsync(AusProgramActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<AusProgramDto>(QualityText.InvalidData);
            if (AiActing.IsActing) return Bad<AusProgramDto>("هوش مصنوعی برنامه‌ی ممیزی را ارسال، تایید یا رد نمی‌کند.");
            AusProgram p = await _uow.Set<AusProgram>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (p == null) return QualityText.NotFound<AusProgramDto>("برنامه‌ی ممیزی پیدا نشد.");
            string action = dto.Action.Trim().ToLowerInvariant();
            DateTimeOffset now = DateTimeOffset.Now;
            string note = QualityText.Clean(dto.Note);
            if (p.Status != AusProgramStatus.Draft) return Bad<AusProgramDto>("این برنامه دیگر در جریان تایید نیست.");
            if (action == "submit")
            {
                if (!_user.IsInRole(AusRoles.Pmo)) return Bad<AusProgramDto>(QualityText.NotAllowed);
                if (p.SubmittedAt.HasValue) return Bad<AusProgramDto>("برنامه پیش‌تر ارسال شده است.");
                if (p.PreparedByUserId != Me) return Bad<AusProgramDto>("فقط تنظیم‌کننده‌ی برنامه آن را ارسال می‌کند.");
                p.SubmittedAt = now;
            }
            else if (action == "approve" || action == "reject")
            {
                if (!_user.IsInRole(AusRoles.Qa)) return Bad<AusProgramDto>(QualityText.NotAllowed);
                if (!p.SubmittedAt.HasValue) return Bad<AusProgramDto>("برنامه هنوز ارسال نشده است.");
                if (p.PreparedByUserId == Me) return Bad<AusProgramDto>(QualityText.SameUser);
                if (action == "reject" && (note == null || note.Length < 10)) return Bad<AusProgramDto>("دلیل رد را (دست‌کم ۱۰ نویسه) بنویسید.");
                p.DecidedByUserId = Me; p.DecidedAt = now; p.DecisionNote = note;
                if (action == "reject") p.Status = AusProgramStatus.Rejected;
                else
                {
                    List<AusProgramItem> items = await _uow.Set<AusProgramItem>().AsNoTracking().Where(i => i.ProgramId == p.Id && !i.Deleted).OrderBy(i => i.Id).ToListAsync(ct);
                    string content = p.Year + "|" + p.Version + "|" + p.Title + "|" + p.Scope + "|" + p.Criteria + "|" + p.Objectives + "|" + string.Join(";", items.Select(i => i.ProcessCode + ":" + (int)i.Frequency + ":" + i.IntervalDays + ":" + i.Title));
                    p.ContentSha256 = AusEvidence.Sha256Hex(content);
                    p.Status = AusProgramStatus.Approved;
                    foreach (AusProgram old in await _uow.Set<AusProgram>().Where(x => x.Year == p.Year && x.Id != p.Id && !x.Deleted && x.Status == AusProgramStatus.Approved).ToListAsync(ct)) { old.Status = AusProgramStatus.Superseded; old.ModifyDate = now; }
                }
            }
            else return Bad<AusProgramDto>("عمل نامعتبر است.");
            p.ModifyDate = now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<AusProgramDto>(QualityText.Concurrency); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(p, true, ct));
        }
    }

    public interface IAusAuditService
    {
        Task<ResponseDto<List<AusAuditListDto>>> GetAsync(int? limit, CancellationToken ct);
        Task<ResponseDto<AusAuditDto>> GetOneAsync(int id, CancellationToken ct);
        Task<ResponseDto<AusAuditDto>> CreateAsync(AusAuditSaveDto dto, CancellationToken ct);
        Task<ResponseDto<AusAuditDto>> ActionAsync(AusAuditActionDto dto, CancellationToken ct);
        Task<ResponseDto<AusAuditDto>> SaveCheckAsync(AusCheckSaveDto dto, CancellationToken ct);
    }

    /// <summary>
    /// Process audits by human auditors (ISO 19011 clause 6): the plan, the checklist made from the catalogue for the process (with the open findings of the seat shown beside each question) and the manual findings.
    /// The lead auditor must hold the AUDITOR role and cannot be the auditee; the same person never audits his or her own process. Manual findings go through the same closure chain.
    /// </summary>
    public class AusAuditService : IAusAuditService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public AusAuditService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);

        private static string StatusText(AusAuditStatus s) => s == AusAuditStatus.Planned ? "برنامه‌ریزی‌شده" : s == AusAuditStatus.InProgress ? "در حال انجام" : s == AusAuditStatus.Completed ? "کامل شده" : "لغو شده";
        private static string ResultText(AusCheckResult r) => r == AusCheckResult.Pending ? "بررسی نشده" : r == AusCheckResult.Conform ? "منطبق" : r == AusCheckResult.NonConform ? "ناانطباق" : "مصداق ندارد";

        private async Task<AusAuditDto> ToDtoAsync(AusAudit a, CancellationToken ct)
        {
            List<AusAuditCheck> checks = await _uow.Set<AusAuditCheck>().AsNoTracking().Where(c => c.AuditId == a.Id && !c.Deleted).OrderBy(c => c.Sort).ToListAsync(ct);
            Dictionary<int, string> names = await AusNames.LoadAsync(_uow, new int?[] { a.LeadAuditorUserId, a.AuditeeUserId }, ct);
            var d = new AusAuditDto();
            Fill(d, a, names, checks);
            d.Summary = a.Summary; d.StartedAtText = a.StartedAt.HasValue ? AusRunService.Stamp(a.StartedAt.Value) : null; d.CompletedAtText = a.CompletedAt.HasValue ? AusRunService.Stamp(a.CompletedAt.Value) : null;
            var codes = checks.Select(c => c.RuleCode).Distinct().ToList();
            var counts = await _uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && codes.Contains(f.RuleCode) && f.Status != AusFindingStatus.Closed && f.Status != AusFindingStatus.Voided).GroupBy(f => f.RuleCode).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            Dictionary<string, int> open = counts.ToDictionary(x => x.Key, x => x.N);
            d.Items = checks.Select(c => new AusAuditCheckDto { Id = c.Id, Sort = c.Sort, RuleCode = c.RuleCode, Standard = c.Standard, Clause = c.Clause, Question = c.Question, OpenFindings = open.TryGetValue(c.RuleCode, out int n) ? n : 0, Result = (int)c.Result, ResultTitle = ResultText(c.Result), Note = c.Note, FindingId = c.FindingId }).ToList();
            if (!AiActing.IsActing && a.LeadAuditorUserId == Me)
            {
                if (a.Status == AusAuditStatus.Planned) d.Allowed.Add("start");
                if (a.Status == AusAuditStatus.InProgress) { d.Allowed.Add("check"); d.Allowed.Add("finding"); d.Allowed.Add("complete"); }
            }
            if (a.Status == AusAuditStatus.Planned && _user.IsInRole(AusRoles.Qa) && !AiActing.IsActing) d.Allowed.Add("cancel");
            return d;
        }

        private static void Fill(AusAuditListDto d, AusAudit a, Dictionary<int, string> names, List<AusAuditCheck> checks)
        {
            d.Id = a.Id; d.Number = a.Number; d.ProcessCode = a.ProcessCode; d.ProcessTitle = AusProcesses.TitleOf(a.ProcessCode); d.Title = a.Title; d.PlannedDate = a.PlannedDate; d.PlannedDateText = AusEvidence.Day(a.PlannedDate);
            d.Status = StatusText(a.Status); d.StatusValue = (int)a.Status; d.LeadAuditorUserId = a.LeadAuditorUserId; d.LeadAuditorName = AusNames.Of(names, a.LeadAuditorUserId); d.AuditeeUserId = a.AuditeeUserId; d.AuditeeName = AusNames.Of(names, a.AuditeeUserId); d.AuditeeUnit = a.AuditeeUnit;
            d.Checks = checks?.Count ?? 0; d.NonConform = checks?.Count(c => c.Result == AusCheckResult.NonConform) ?? 0;
        }

        public async Task<ResponseDto<List<AusAuditListDto>>> GetAsync(int? limit, CancellationToken ct)
        {
            if (!_user.IsInRole(AusRoles.Readers)) return Bad<List<AusAuditListDto>>(QualityText.NotAllowed);
            List<AusAudit> rows = await _uow.Set<AusAudit>().AsNoTracking().Where(a => !a.Deleted).OrderByDescending(a => a.Id).Take(Math.Min(Math.Max(limit ?? 100, 1), 500)).ToListAsync(ct);
            var ids = rows.Select(r => r.Id).ToList();
            var cks = await _uow.Set<AusAuditCheck>().AsNoTracking().Where(c => ids.Contains(c.AuditId) && !c.Deleted).Select(c => new { c.AuditId, c.Result }).ToListAsync(ct);
            Dictionary<int, string> names = await AusNames.LoadAsync(_uow, rows.SelectMany(r => new int?[] { r.LeadAuditorUserId, r.AuditeeUserId }), ct);
            return ResponseDtoHelper.Ok(rows.Select(r => { var d = new AusAuditListDto(); Fill(d, r, names, null); var mine = cks.Where(c => c.AuditId == r.Id).ToList(); d.Checks = mine.Count; d.NonConform = mine.Count(c => c.Result == AusCheckResult.NonConform); return d; }).ToList());
        }

        public async Task<ResponseDto<AusAuditDto>> GetOneAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(AusRoles.Readers)) return Bad<AusAuditDto>(QualityText.NotAllowed);
            AusAudit a = await _uow.Set<AusAudit>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            return a == null ? QualityText.NotFound<AusAuditDto>("ممیزی فرایند پیدا نشد.") : ResponseDtoHelper.Ok(await ToDtoAsync(a, ct));
        }

        public async Task<ResponseDto<AusAuditDto>> CreateAsync(AusAuditSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<AusAuditDto>(QualityText.InvalidData);
            if (AiActing.IsActing) return Bad<AusAuditDto>("برنامه‌ریزی ممیزی فرایند با انسان است.");
            if (!_user.IsInRole(AusRoles.Qa + "," + AusRoles.Auditor)) return Bad<AusAuditDto>(QualityText.NotAllowed);
            AusProcess proc = AusProcesses.Find(dto.ProcessCode);
            if (proc == null) return Bad<AusAuditDto>("فرایند نامعتبر است.");
            string title = QualityText.Clean(dto.Title) ?? "ممیزی فرایند " + proc.Title;
            if (dto.PlannedDate.Date < DateTime.Now.Date.AddYears(-1)) return Bad<AusAuditDto>("تاریخ برنامه نامعتبر است.");
            if (dto.LeadAuditorUserId <= 0) return Bad<AusAuditDto>("ممیز ارشد را انتخاب کنید.");
            if (dto.LeadAuditorUserId == dto.AuditeeUserId) return Bad<AusAuditDto>("ممیز و ممیزی‌شونده نمی‌توانند یک نفر باشند (ISO 19011 بند 5.4.4: استقلال ممیز).");
            List<int> auditors = await RoleUsers.IdsAsync(_uow, AusRoles.Auditor, null, ct);
            if (!auditors.Contains(dto.LeadAuditorUserId)) return Bad<AusAuditDto>("ممیز ارشد باید نقش ممیز (AUDITOR) داشته باشد.");
            if (dto.ProgramItemId.HasValue && !await _uow.Set<AusProgramItem>().AnyAsync(i => i.Id == dto.ProgramItemId.Value && !i.Deleted, ct)) return Bad<AusAuditDto>("ردیف برنامه پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            int year = AusRules.PersianYear(now.LocalDateTime);
            string prefix = "PA-" + year.ToString("0000", CultureInfo.InvariantCulture) + "-";
            int n = await _uow.Set<AusAudit>().CountAsync(a => a.Number.StartsWith(prefix), ct) + 1;
            var a = new AusAudit { Number = prefix + n.ToString("000", CultureInfo.InvariantCulture), ProgramItemId = dto.ProgramItemId, ProcessCode = proc.Code, Title = title, PlannedDate = dto.PlannedDate.Date, LeadAuditorUserId = dto.LeadAuditorUserId, AuditeeUserId = dto.AuditeeUserId, AuditeeUnit = QualityText.Clean(dto.AuditeeUnit) ?? proc.OwnerUnit, Status = AusAuditStatus.Planned, CreateDate = now, ModifyDate = now };
            _uow.Set<AusAudit>().Add(a);
            int sort = 0;
            foreach (AusRuleDef r in AusCatalog.All.Where(r => r.ProcessCode == proc.Code).OrderBy(r => r.Code, StringComparer.Ordinal))
                _uow.Set<AusAuditCheck>().Add(new AusAuditCheck { Audit = a, Sort = ++sort, RuleCode = r.Code, Standard = r.Standard, Clause = r.Clause, Question = r.Title, Result = AusCheckResult.Pending, CreateDate = now, ModifyDate = now });
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<AusAuditDto>("شماره‌ی ممیزی هم‌زمان گرفته شد؛ دوباره تلاش کنید."); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(a, ct));
        }

        public async Task<ResponseDto<AusAuditDto>> ActionAsync(AusAuditActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<AusAuditDto>(QualityText.InvalidData);
            if (AiActing.IsActing) return Bad<AusAuditDto>("ممیزی فرایند را انسان انجام می‌دهد.");
            AusAudit a = await _uow.Set<AusAudit>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (a == null) return QualityText.NotFound<AusAuditDto>("ممیزی فرایند پیدا نشد.");
            string action = dto.Action.Trim().ToLowerInvariant();
            DateTimeOffset now = DateTimeOffset.Now;
            if (action == "cancel")
            {
                if (!_user.IsInRole(AusRoles.Qa)) return Bad<AusAuditDto>(QualityText.NotAllowed);
                if (a.Status != AusAuditStatus.Planned) return Bad<AusAuditDto>("فقط ممیزی برنامه‌ریزی‌شده لغو می‌شود.");
                string why = QualityText.Clean(dto.Summary);
                if (why == null || why.Length < 10) return Bad<AusAuditDto>("دلیل لغو را (دست‌کم ۱۰ نویسه) بنویسید.");
                a.Status = AusAuditStatus.Cancelled; a.Summary = why;
            }
            else
            {
                if (a.LeadAuditorUserId != Me) return Bad<AusAuditDto>("فقط ممیز ارشد این ممیزی آن را پیش می‌برد.");
                if (a.AuditeeUserId == Me) return Bad<AusAuditDto>("ممیز نمی‌تواند ممیزی‌شونده‌ی همان ممیزی باشد.");
                if (action == "start")
                {
                    if (a.Status != AusAuditStatus.Planned) return Bad<AusAuditDto>("ممیزی در وضعیت برنامه‌ریزی‌شده نیست.");
                    a.Status = AusAuditStatus.InProgress; a.StartedAt = now;
                }
                else if (action == "complete")
                {
                    if (a.Status != AusAuditStatus.InProgress) return Bad<AusAuditDto>("ممیزی در حال انجام نیست.");
                    List<AusAuditCheck> checks = await _uow.Set<AusAuditCheck>().AsNoTracking().Where(c => c.AuditId == a.Id && !c.Deleted).ToListAsync(ct);
                    if (checks.Any(c => c.Result == AusCheckResult.Pending)) return Bad<AusAuditDto>("همه‌ی بندهای فهرست بازبینی باید بررسی شوند.");
                    if (checks.Any(c => c.Result == AusCheckResult.NonConform && c.FindingId == null)) return Bad<AusAuditDto>("برای هر بند ناانطباق باید یافته ثبت شود.");
                    string summary = QualityText.Clean(dto.Summary);
                    if (summary == null || summary.Length < 20) return Bad<AusAuditDto>("نتیجه‌ی ممیزی را (دست‌کم ۲۰ نویسه) بنویسید.");
                    a.Status = AusAuditStatus.Completed; a.CompletedAt = now; a.Summary = summary;
                }
                else return Bad<AusAuditDto>("عمل نامعتبر است.");
            }
            a.ModifyDate = now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<AusAuditDto>(QualityText.Concurrency); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(a, ct));
        }

        public async Task<ResponseDto<AusAuditDto>> SaveCheckAsync(AusCheckSaveDto dto, CancellationToken ct)
        {
            if (dto == null || !Enum.IsDefined(typeof(AusCheckResult), dto.Result) || dto.Result == (int)AusCheckResult.Pending) return Bad<AusAuditDto>(QualityText.InvalidData);
            if (AiActing.IsActing) return Bad<AusAuditDto>("بررسی بندهای ممیزی با ممیز انسانی است.");
            AusAuditCheck c = await _uow.Set<AusAuditCheck>().FirstOrDefaultAsync(x => x.Id == dto.CheckId && !x.Deleted, ct);
            if (c == null) return QualityText.NotFound<AusAuditDto>("بند پیدا نشد.");
            AusAudit a = await _uow.Set<AusAudit>().FirstOrDefaultAsync(x => x.Id == c.AuditId, ct);
            if (a.LeadAuditorUserId != Me) return Bad<AusAuditDto>("فقط ممیز ارشد بندها را بررسی می‌کند.");
            if (a.AuditeeUserId == Me) return Bad<AusAuditDto>("ممیز نمی‌تواند ممیزی‌شونده‌ی همان ممیزی باشد.");
            if (a.Status != AusAuditStatus.InProgress) return Bad<AusAuditDto>("ممیزی در حال انجام نیست.");
            var result = (AusCheckResult)dto.Result;
            if (result == AusCheckResult.NonConform && c.FindingId == null) return Bad<AusAuditDto>("ناانطباق با ثبت یافته (از همین ممیزی) ثبت می‌شود.");
            if (c.FindingId.HasValue && result != AusCheckResult.NonConform) return Bad<AusAuditDto>("برای این بند یافته ثبت شده است و نتیجه‌اش را نمی‌توان به منطبق تغییر داد.");
            c.Result = result; c.Note = QualityText.Clean(dto.Note); c.CheckedAt = DateTimeOffset.Now; c.CheckedByUserId = Me; c.ModifyDate = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(a, ct));
        }
    }
}
