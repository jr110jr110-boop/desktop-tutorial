using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AuditSeat;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.AuditSeatDtos;

namespace Services.NasbNiro.AuditSeat
{
    public interface IAusReportService
    {
        Task<ResponseDto<List<AusReportListDto>>> GetAsync(int? kind, int? limit, CancellationToken ct);
        Task<ResponseDto<AusReportDto>> GetOneAsync(int id, CancellationToken ct);

        /// <summary>A report of the period of the kind that ends on the day (default today). A second call for the same period returns the stored report: reports never change.</summary>
        Task<ResponseDto<AusReportDto>> GenerateAsync(AusReportGenerateDto dto, CancellationToken ct);

        /// <summary>The reports that are due now (daily digest, weekly, monthly); used by the AUDIT seat.</summary>
        Task<List<int>> GenerateDueAsync(DateTime today, bool allowModel, CancellationToken ct);
    }

    /// <summary>
    /// The reports of the AUDIT seat: the daily digest, the weekly report (PMO and QA) and the monthly report (CEO). The numbers come from the findings and the catalogue only; the text is a template.
    /// A local model may write the narrative, but it is used only when the numeric check accepts it; the stored report keeps the hash of the content and the text.
    /// </summary>
    public class AusReportService : IAusReportService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;
        private readonly IServiceProvider _services;

        public AusReportService(IUnitOfWork uow, IInventoryUserContext user, IServiceProvider services = null, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _services = services;
            _notify = notify;
        }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);

        // ---------- periods

        public static (DateTime From, DateTime To) Period(AusReportKind kind, DateTime day)
        {
            day = day.Date;
            if (kind == AusReportKind.Daily) return (day, day);
            if (kind == AusReportKind.Weekly) return (day.AddDays(-6), day);
            var pc = new PersianCalendar();
            DateTime thisMonth = day.AddDays(1 - pc.GetDayOfMonth(day));
            DateTime to = thisMonth.AddDays(-1);
            DateTime from = to.AddDays(1 - pc.GetDayOfMonth(to));
            return (from, to);
        }

        private static string KindTitle(AusReportKind k) => k == AusReportKind.Daily ? "خلاصه‌ی روزانه‌ی ممیزی" : k == AusReportKind.Weekly ? "گزارش هفتگی ممیزی" : "گزارش ماهانه‌ی ممیزی";
        private static string Recipients(AusReportKind k) => k == AusReportKind.Daily ? "CEO,PMO,QA" : k == AusReportKind.Weekly ? "PMO,QA" : "CEO";

        // ---------- content

        public async Task<AusReportContentDto> BuildContentAsync(DateTime from, DateTime to, CancellationToken ct)
        {
            DateTime today = DateTime.Now.Date;
            DateTimeOffset f0 = new DateTimeOffset(from, TimeZoneInfo.Local.GetUtcOffset(from)), t1 = new DateTimeOffset(to.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(to.AddDays(1)));
            int len = (int)(to - from).TotalDays + 1;
            DateTime pFrom = from.AddDays(-len), pTo = from.AddDays(-1);
            DateTimeOffset pf0 = new DateTimeOffset(pFrom, TimeZoneInfo.Local.GetUtcOffset(pFrom));

            var all = await _uow.Set<AusFinding>().AsNoTracking().Where(f => !f.Deleted && (f.Status != AusFindingStatus.Closed && f.Status != AusFindingStatus.Voided || f.DetectedAt >= pf0 || (f.ClosedAt != null && f.ClosedAt >= pf0)))
                .Select(f => new { f.Id, f.Number, f.Title, f.RuleCode, f.ProcessCode, f.Severity, f.Status, f.DueDate, f.DetectedAt, f.ClosedAt, f.RecurrenceOfId, f.OwnerUserId, f.OwnerRole, f.Standard, f.Clause, f.ObjectNumber, f.Source, f.RecurrenceCount }).ToListAsync(ct);
            var open = all.Where(f => !AusRules.IsTerminal(f.Status)).ToList();
            var created = all.Where(f => f.DetectedAt >= f0 && f.DetectedAt < t1).ToList();
            var closed = all.Where(f => f.Status == AusFindingStatus.Closed && f.ClosedAt != null && f.ClosedAt >= f0 && f.ClosedAt < t1).ToList();
            var prevCreated = all.Where(f => f.DetectedAt >= pf0 && f.DetectedAt < f0).ToList();
            var prevClosed = all.Where(f => f.Status == AusFindingStatus.Closed && f.ClosedAt != null && f.ClosedAt >= pf0 && f.ClosedAt < f0).ToList();

            var c = new AusReportContentDto { PeriodFrom = AusEvidence.Day(from), PeriodTo = AusEvidence.Day(to) };
            c.Open = open.Count; c.OpenMajor = open.Count(f => f.Severity == AusSeverity.Major); c.OpenMinor = open.Count(f => f.Severity == AusSeverity.Minor); c.OpenObservation = open.Count(f => f.Severity == AusSeverity.Observation);
            c.NewInPeriod = created.Count; c.NewMajor = created.Count(f => f.Severity == AusSeverity.Major); c.NewMinor = created.Count(f => f.Severity == AusSeverity.Minor); c.NewObservation = created.Count(f => f.Severity == AusSeverity.Observation);
            c.ClosedInPeriod = closed.Count; c.Overdue = open.Count(f => f.DueDate < today); c.Recurrences = created.Count(f => f.RecurrenceOfId != null);
            c.PreviousNew = prevCreated.Count; c.PreviousClosed = prevClosed.Count;
            c.PreviousOpen = all.Count(f => f.DetectedAt < f0 && (f.ClosedAt == null || f.ClosedAt >= f0));
            c.Trend = created.Count > prevCreated.Count ? "افزایش" : created.Count < prevCreated.Count ? "کاهش" : "بدون تغییر";
            c.AvgClosureDays = closed.Count == 0 ? (decimal?)null : Math.Round((decimal)closed.Average(f => (f.ClosedAt.Value - f.DetectedAt).TotalDays), 1);
            foreach (var f in open)
            {
                int age = Math.Max(0, (int)(today - f.DetectedAt.LocalDateTime.Date).TotalDays);
                if (age <= 7) c.Age0To7++; else if (age <= 30) c.Age8To30++; else if (age <= 90) c.Age31To90++; else c.AgeOver90++;
            }
            c.ByProcess = open.GroupBy(f => f.ProcessCode).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => new AusCountDto { Key = g.Key, Title = AusProcesses.TitleOf(g.Key), Count = g.Count() }).ToList();
            c.ByStatus = open.GroupBy(f => f.Status).OrderBy(g => (int)g.Key).Select(g => new AusCountDto { Key = g.Key.ToString(), Title = AusRules.StatusText(g.Key), Count = g.Count() }).ToList();
            c.TopRules = created.GroupBy(f => f.RuleCode).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(10).Select(g => new AusCountDto { Key = g.Key, Title = AusCatalog.Find(g.Key)?.Title ?? g.Key, Count = g.Count() }).ToList();
            DateTime todayDay = today;
            List<int?> ownerIds = open.Where(f => f.Severity == AusSeverity.Major).OrderBy(f => f.DueDate).Take(20).Select(f => f.OwnerUserId).ToList();
            Dictionary<int, string> names = await AusNames.LoadAsync(_uow, ownerIds, ct);
            c.MajorOpen = open.Where(f => f.Severity == AusSeverity.Major).OrderBy(f => f.DueDate).ThenBy(f => f.Id).Take(20).Select(f => new AusFindingListDto
            {
                Id = f.Id, Number = f.Number, RuleCode = f.RuleCode, Title = f.Title, Standard = f.Standard, Clause = f.Clause, ProcessCode = f.ProcessCode, ProcessTitle = AusProcesses.TitleOf(f.ProcessCode), Severity = AusRules.SeverityText(f.Severity), SeverityValue = (int)f.Severity,
                Status = AusRules.StatusText(f.Status), StatusValue = (int)f.Status, ObjectNumber = f.ObjectNumber, OwnerUserId = f.OwnerUserId, OwnerName = AusNames.Of(names, f.OwnerUserId), OwnerRole = f.OwnerRole, DueDate = f.DueDate, DueDateText = AusEvidence.Day(f.DueDate),
                Overdue = f.DueDate < todayDay, AgeDays = Math.Max(0, (int)(todayDay - f.DetectedAt.LocalDateTime.Date).TotalDays), DetectedAtText = AusRunService.Stamp(f.DetectedAt), RecurrenceCount = f.RecurrenceCount, RecurrenceOfId = f.RecurrenceOfId,
            }).ToList();

            // the compliance score per standard clause: a rule is met when it is enabled, ran without a problem and has no open finding
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            Dictionary<string, AusRuleState> states = await _uow.Set<AusRuleState>().AsNoTracking().Where(s => !s.Deleted).ToDictionaryAsync(s => s.RuleCode, ct);
            HashSet<string> withOpen = open.Select(f => f.RuleCode).ToHashSet();
            bool master = st[AusSettingKeys.Master] != 0m;
            Func<AusRuleDef, bool> enabled = r => master && !(st.TryGetValue(AusSettingKeys.RuleKey(r.Code), out decimal on) && on == 0m);
            c.RulesTotal = AusCatalog.All.Count; c.RulesEnabled = AusCatalog.All.Count(enabled);
            Func<AusRuleDef, bool> met = r => enabled(r) && states.TryGetValue(r.Code, out AusRuleState s) && s.LastRunAt != null && s.LastStatus == "ok" && !withOpen.Contains(r.Code);
            c.Scores = AusCatalog.All.GroupBy(r => r.Standard + "|" + r.Clause).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
            {
                int total = g.Count(), ok = g.Count(met);
                return new AusScoreDto { Standard = g.First().Standard, Clause = g.First().Clause, Rules = total, Met = ok, Not = total - ok, Result = ok == total ? "برآورده" : ok == 0 ? "برآورده نشده" : "نسبی" };
            }).ToList();
            c.ScoreMet = c.Scores.Count(s => s.Result == "برآورده"); c.ScoreNot = c.Scores.Count(s => s.Result == "برآورده نشده");
            return c;
        }

        public static string Template(AusReportKind kind, AusReportContentDto c)
        {
            string p = c.PeriodFrom == c.PeriodTo ? c.PeriodFrom : c.PeriodFrom + " تا " + c.PeriodTo;
            string s = $"{KindTitle(kind)} ({p}): در این دوره {c.NewInPeriod} یافته‌ی تازه ثبت شد ({c.NewMajor} عمده، {c.NewMinor} جزئی، {c.NewObservation} مشاهده) و {c.ClosedInPeriod} یافته بسته شد. "
                + $"اکنون {c.Open} یافته باز است ({c.OpenMajor} عمده، {c.OpenMinor} جزئی، {c.OpenObservation} مشاهده) که {c.Overdue} مورد از مهلت گذشته است. {c.Recurrences} مورد تکرار بوده است. "
                + $"نسبت به دوره‌ی پیشین ({c.PreviousNew} یافته‌ی تازه) روند یافته‌های تازه: {c.Trend}. ";
            if (c.AvgClosureDays.HasValue) s += $"میانگین زمان بستن: {c.AvgClosureDays.Value.ToString("0.#", CultureInfo.InvariantCulture)} روز. ";
            s += $"از {c.Scores.Count} بند استاندارد، {c.ScoreMet} بند برآورده و {c.ScoreNot} بند برآورده نشده است ({c.RulesEnabled} از {c.RulesTotal} قاعده فعال).";
            return s;
        }

        private async Task<(string Text, string Source)> NarrativeAsync(AusReportKind kind, AusReportContentDto c, bool allowModel, CancellationToken ct)
        {
            string template = Template(kind, c);
            if (!allowModel || _services == null) return (template, "template");
            IAiSeatAsk ask = _services.GetService(typeof(IAiSeatAsk)) as IAiSeatAsk;
            if (ask == null) return (template, "template");
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                string q = "برای مدیر، یک بند کوتاه فارسی (حداکثر چهار جمله) از این گزارش ممیزی بنویس. فقط عددهای همین متن را به کار ببر و عدد تازه نساز.\n" + template;
                AiSeatAnswer a = await ask.AskAsync(q, cts.Token, withTools: false);
                if (a != null && a.Verified && !a.Degraded && !string.IsNullOrWhiteSpace(a.Text)) return (a.Text.Trim(), "llm");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested) { }
            return (template, "template");
        }

        // ---------- generate

        public async Task<ResponseDto<AusReportDto>> GenerateAsync(AusReportGenerateDto dto, CancellationToken ct)
        {
            if (dto == null || dto.Kind < 1 || dto.Kind > 3) return Bad<AusReportDto>(QualityText.InvalidData);
            if (AiActing.IsActing) return Bad<AusReportDto>("تولید دستی گزارش با انسان است.");
            if (!_user.IsInRole(AusRoles.Runners)) return Bad<AusReportDto>(QualityText.NotAllowed);
            int id = await GenerateCoreAsync((AusReportKind)dto.Kind, (dto.Day ?? DateTime.Now).Date, _user.UserId, false, ct);
            return await GetOneAsync(id, ct);
        }

        private async Task<int> GenerateCoreAsync(AusReportKind kind, DateTime day, int by, bool allowModel, CancellationToken ct)
        {
            var (from, to) = Period(kind, day);
            AusReport existing = await _uow.Set<AusReport>().AsNoTracking().Where(r => !r.Deleted && r.Kind == kind && r.PeriodFrom == from && r.PeriodTo == to).OrderBy(r => r.Id).FirstOrDefaultAsync(ct);
            if (existing != null) return existing.Id;
            AusReportContentDto content = await BuildContentAsync(from, to, ct);
            var (text, source) = await NarrativeAsync(kind, content, allowModel, ct);
            string json = JsonSerializer.Serialize(content, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            string title = KindTitle(kind) + " " + AusEvidence.Day(from) + (from == to ? "" : " تا " + AusEvidence.Day(to));
            DateTimeOffset now = DateTimeOffset.Now;
            var r = new AusReport
            {
                Kind = kind, PeriodFrom = from, PeriodTo = to, Title = title, Recipients = Recipients(kind), ContentJson = json, Narrative = text, NarrativeSource = source,
                Sha256 = AusEvidence.Sha256Hex(title + "|" + from.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|" + to.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|" + json + "|" + text), GeneratedAt = now, GeneratedByUserId = by, CreateDate = now, ModifyDate = now,
            };
            _uow.Set<AusReport>().Add(r);
            await _uow.SaveChangesAsync(ct);
            if (_notify != null)
                await _notify.NotifyAsync(new NotifyRequest { SourceKey = "aus-report:" + r.Id, Module = "AUDIT", SourceType = "AusReport", SourceId = r.Id, Kind = OfInboxKind.Alert, Title = title, Link = "/audit/reports/" + r.Id, Roles = r.Recipients }, ct);
            return r.Id;
        }

        public async Task<List<int>> GenerateDueAsync(DateTime today, bool allowModel, CancellationToken ct)
        {
            today = today.Date;
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            var made = new List<int>();
            made.Add(await GenerateCoreAsync(AusReportKind.Daily, today, 0, allowModel, ct));
            int weekIndex = ((int)today.DayOfWeek + 1) % 7;   // Saturday = 0 ... Friday = 6
            if (weekIndex == (int)st[AusSettingKeys.WeeklyDay]) made.Add(await GenerateCoreAsync(AusReportKind.Weekly, today, 0, allowModel, ct));
            if (new PersianCalendar().GetDayOfMonth(today) == (int)st[AusSettingKeys.MonthlyDay]) made.Add(await GenerateCoreAsync(AusReportKind.Monthly, today, 0, allowModel, ct));
            return made;
        }

        // ---------- read

        public async Task<ResponseDto<List<AusReportListDto>>> GetAsync(int? kind, int? limit, CancellationToken ct)
        {
            if (!_user.IsInRole(AusRoles.Readers)) return Bad<List<AusReportListDto>>(QualityText.NotAllowed);
            IQueryable<AusReport> q = _uow.Set<AusReport>().AsNoTracking().Where(r => !r.Deleted);
            if (kind.HasValue) { var k = (AusReportKind)kind.Value; q = q.Where(r => r.Kind == k); }
            List<AusReport> rows = await q.OrderByDescending(r => r.Id).Take(Math.Min(Math.Max(limit ?? 100, 1), 500)).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(ToListDto).ToList());
        }

        private static AusReportListDto ToListDto(AusReport r) => new AusReportListDto
        {
            Id = r.Id, Kind = r.Kind == AusReportKind.Daily ? "روزانه" : r.Kind == AusReportKind.Weekly ? "هفتگی" : "ماهانه", KindValue = (int)r.Kind, Title = r.Title, PeriodFromText = AusEvidence.Day(r.PeriodFrom), PeriodToText = AusEvidence.Day(r.PeriodTo),
            GeneratedAtText = AusRunService.Stamp(r.GeneratedAt), Recipients = r.Recipients, Sha256 = r.Sha256, NarrativeSource = r.NarrativeSource,
        };

        public async Task<ResponseDto<AusReportDto>> GetOneAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(AusRoles.Readers)) return Bad<AusReportDto>(QualityText.NotAllowed);
            AusReport r = await _uow.Set<AusReport>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (r == null) return QualityText.NotFound<AusReportDto>("گزارش پیدا نشد.");
            var d = new AusReportDto { Id = r.Id, Kind = ToListDto(r).Kind, KindValue = (int)r.Kind, Title = r.Title, PeriodFromText = AusEvidence.Day(r.PeriodFrom), PeriodToText = AusEvidence.Day(r.PeriodTo), GeneratedAtText = AusRunService.Stamp(r.GeneratedAt), Recipients = r.Recipients, Sha256 = r.Sha256, NarrativeSource = r.NarrativeSource, Narrative = r.Narrative };
            d.Content = JsonSerializer.Deserialize<AusReportContentDto>(r.ContentJson);
            return ResponseDtoHelper.Ok(d);
        }
    }
}
