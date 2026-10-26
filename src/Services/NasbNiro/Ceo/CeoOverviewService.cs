using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ceo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.AfterSales;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Ceo
{
    /// <summary>The cache of the overview: 120 seconds, one entry per payroll-mask variant (a reader who sees pay-based figures and one who does not).</summary>
    public static class CeoCache
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(120);

        /// <summary>An overview with a section that could not be computed is kept only this long, so that a module that comes back shows up quickly.</summary>
        public static readonly TimeSpan FailedLifetime = TimeSpan.FromSeconds(10);

        /// <summary>The history derived from the records (see <see cref="CeoHistory"/>) is kept this long; it changes only when an old record is back-dated.</summary>
        public static readonly TimeSpan HistoryLifetime = TimeSpan.FromMinutes(30);

        public static string Key(bool seesPayroll) => seesPayroll ? "ceo:overview:payroll" : "ceo:overview:masked";

        /// <summary>Drops both variants (a new snapshot changes the trends).</summary>
        public static void Clear(IMemoryCache cache)
        {
            if (cache == null) return;
            cache.Remove(Key(true));
            cache.Remove(Key(false));
        }
    }

    public interface ICeoOverviewService
    {
        /// <param name="refresh">True skips the cache and stores the fresh answer in it.</param>
        /// <returns>A copy: the caller may change it without changing the cached overview.</returns>
        Task<ResponseDto<CeoOverviewDto>> GetOverviewAsync(bool refresh, CancellationToken ct);

        /// <summary>One section. It is taken from the cached overview when there is one (a copy), and computed now otherwise (never cached on its own). The key is one of <see cref="CeoKeys.Sections"/>.</summary>
        Task<ResponseDto<CeoSectionDto>> GetSectionAsync(string key, CancellationToken ct);

        /// <summary>One section; <paramref name="refresh"/> true computes it now even when the overview is cached.</summary>
        Task<ResponseDto<CeoSectionDto>> GetSectionAsync(string key, bool refresh, CancellationToken ct) => GetSectionAsync(key, ct);
    }

    /// <summary>The CEO dashboard could not be computed: the sections that are drawn from it are reported unavailable without trying again.</summary>
    internal sealed class CeoDashboardUnavailableException : Exception
    {
        public CeoDashboardUnavailableException(Exception inner) : base("The CEO dashboard is not available.", inner) { }
    }

    /// <summary>What one computation of the overview shares between its sections: the day, the twelve-month window, the history of the trends and the CEO dashboard with its parts.</summary>
    internal sealed class CeoContext
    {
        public CeoContext(DateTime today, bool seesPayroll)
        {
            Today = today.Date;
            SeesPayroll = seesPayroll;
            Window = CeoTrends.Window(Today);
            Labels = CeoTrends.Labels(Window);
            From = Window[0];
        }

        public DateTime Today { get; }

        /// <summary>The reader may see payroll-based figures (CEO, finance manager, payroll) and is not a seat acting for somebody else.</summary>
        public bool SeesPayroll { get; }

        /// <summary>First days of the last twelve Persian months, oldest first; the last one is the month of today.</summary>
        public List<DateTime> Window { get; }

        public List<string> Labels { get; }

        /// <summary>First day of the oldest month of the window.</summary>
        public DateTime From { get; }

        /// <summary>The day after today: the exclusive end of every "up to today" range.</summary>
        public DateTime End => Today.AddDays(1);

        /// <summary>The CEO dashboard and the module results it was made from, read once for the whole computation.</summary>
        public CeoDashboardParts Parts { get; set; }

        /// <summary>Why the CEO dashboard could not be read; once set, no section tries again.</summary>
        public Exception BaseFailure { get; set; }

        /// <summary>Month-end points of the trends by KPI key: the nightly snapshots, with the months before them filled from the records (see <see cref="CeoHistory"/>).</summary>
        public Dictionary<string, List<CeoTrendPoint>> History { get; } = new Dictionary<string, List<CeoTrendPoint>>();

        /// <summary>The drill parameters of a chart or a figure of the latest month so far (first day of the Persian month to today).</summary>
        public Dictionary<string, string> LatestMonth() => new Dictionary<string, string> { ["from"] = CeoText.Iso(CeoTrends.MonthStart(Today)), ["to"] = CeoText.Iso(Today) };
    }

    /// <summary>
    /// The CEO overview: the headline, the nine sections, the alerts and the pulse lights. Every figure is read from the module that owns it
    /// (the CEO dashboard and the results it was made from, the module services, or the records themselves), so it is the same number the module shows.
    /// The sections are built in the partial files of this class; a section that fails is reported with placeholder figures and a note and does not stop the others;
    /// when the CEO dashboard itself fails, every section is reported at once without trying again.
    /// </summary>
    public sealed partial class CeoOverviewService : ICeoOverviewService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICeoDashboardService _dashboard;
        private readonly IReceivablesService _receivables;
        private readonly IPayableAgeingService _payables;
        private readonly IPcReportService _projects;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CeoOverviewService> _logger;

        public CeoOverviewService(IUnitOfWork uow, IInventoryUserContext user, ICeoDashboardService dashboard, IReceivablesService receivables, IPayableAgeingService payables,
            IPcReportService projects, IMemoryCache cache, ILogger<CeoOverviewService> logger)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
            _receivables = receivables ?? throw new ArgumentNullException(nameof(receivables));
            _payables = payables ?? throw new ArgumentNullException(nameof(payables));
            _projects = projects ?? throw new ArgumentNullException(nameof(projects));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger;
        }

        // ================================================================================== entry points

        public async Task<ResponseDto<CeoOverviewDto>> GetOverviewAsync(bool refresh, CancellationToken ct)
        {
            if (!_user.IsInRole(CeoRoles.Readers)) return ResponseDtoHelper.BadRequest<CeoOverviewDto>(CeoText.NotAllowed);
            bool seesPayroll = SeesPayroll();
            if (!refresh && TryCached(seesPayroll, out CeoOverviewDto hit)) return ResponseDtoHelper.Ok(AsCached(hit));

            var watch = Stopwatch.StartNew();
            var ctx = new CeoContext(DateTime.Today, seesPayroll);
            await PrepareAsync(ctx, null, ct);
            var dto = new CeoOverviewDto { AsOf = ctx.Today, AsOfText = CeoText.Date(ctx.Today), GeneratedAt = DateTimeOffset.Now };
            foreach (string key in CeoKeys.Sections) dto.Sections.Add(await SectionAsync(ctx, key, ct));
            dto.Headline = CeoKeys.Headline.Select(k => HeadlineOf(dto.Sections, k, ctx.Today)).ToList();
            dto.Alerts = AlertsOf(ctx.Parts?.Dashboard);
            dto.Pulse = dto.Sections.Select(PulseOf).ToList();
            watch.Stop();
            dto.GeneratedInMs = (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);

            // an overview with a section that failed is kept only briefly: the module may be back in a moment
            bool complete = dto.Sections.All(s => string.IsNullOrEmpty(s.Note));
            _cache.Set(CeoCache.Key(seesPayroll), CeoCopy.Of(dto), complete ? CeoCache.Lifetime : CeoCache.FailedLifetime);
            return ResponseDtoHelper.Ok(dto);
        }

        public Task<ResponseDto<CeoSectionDto>> GetSectionAsync(string key, CancellationToken ct) => GetSectionAsync(key, false, ct);

        public async Task<ResponseDto<CeoSectionDto>> GetSectionAsync(string key, bool refresh, CancellationToken ct)
        {
            if (!_user.IsInRole(CeoRoles.Readers)) return ResponseDtoHelper.BadRequest<CeoSectionDto>(CeoText.NotAllowed);
            string section = (key ?? string.Empty).Trim().ToLowerInvariant();
            if (!CeoKeys.Sections.Contains(section)) return ResponseDtoHelper.NotFound<CeoSectionDto>(CeoText.UnknownSection);
            bool seesPayroll = SeesPayroll();

            // the section of the overview the screen shows (and the voice reads): only a refresh, or no overview at all, computes it again
            if (!refresh && TryCached(seesPayroll, out CeoOverviewDto hit))
            {
                CeoSectionDto cached = hit.Sections.FirstOrDefault(s => s.Key == section);
                if (cached != null) return ResponseDtoHelper.Ok(CeoCopy.Of(cached));
            }

            var ctx = new CeoContext(DateTime.Today, seesPayroll);
            await PrepareAsync(ctx, section, ct);
            return ResponseDtoHelper.Ok(await SectionAsync(ctx, section, ct));
        }

        /// <summary>CEO, finance manager and payroll see pay-based figures; a PMO reader does not, and neither does an AI seat acting as one of them.</summary>
        private bool SeesPayroll() => SvRoles.SeesPayroll(_user.IsInRole(SvRoles.PayrollSeers));

        private bool TryCached(bool seesPayroll, out CeoOverviewDto hit) =>
            _cache.TryGetValue(CeoCache.Key(seesPayroll), out hit) && hit != null && hit.AsOf == DateTime.Today;

        private static CeoOverviewDto AsCached(CeoOverviewDto source)
        {
            CeoOverviewDto copy = CeoCopy.Of(source);
            copy.Cached = true;
            return copy;
        }

        // ================================================================================== shared data

        /// <summary>
        /// Reads what the sections need, once: the CEO dashboard with its parts, then the history of the trends (of the one section when only one is asked for).
        /// When the dashboard fails nothing else is tried; the failure is remembered in the context and each section reports itself unavailable without another attempt.
        /// </summary>
        private async Task PrepareAsync(CeoContext ctx, string section, CancellationToken ct)
        {
            try { await PartsAsync(ctx, ct); }
            catch (CeoDashboardUnavailableException) { return; }
            await LoadHistoryAsync(ctx, section, ct);
        }

        /// <summary>
        /// The CEO dashboard of this moment together with the module results it was made from (quality dashboard, production load, sales pipeline and results), read once per computation.
        /// It is read exactly as the dashboard endpoint reads it (no "as of" day: production counts are for today and the SLA is judged now), so the numbers are the ones the module shows.
        /// </summary>
        private async Task<CeoDashboardParts> PartsAsync(CeoContext ctx, CancellationToken ct)
        {
            if (ctx.Parts != null) return ctx.Parts;
            if (ctx.BaseFailure != null) throw new CeoDashboardUnavailableException(ctx.BaseFailure);
            try
            {
                CeoDashboardParts parts = await _dashboard.GetPartsAsync(ct);
                if (parts?.Dashboard == null) throw new InvalidOperationException("The CEO dashboard is not available.");
                ctx.Parts = parts;
                return parts;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                ctx.BaseFailure = ex;
                _logger?.LogError(ex, "CEO overview: the CEO dashboard could not be computed; every section is reported unavailable.");
                throw new CeoDashboardUnavailableException(ex);
            }
        }

        /// <summary>
        /// The month-end points of the last twelve months for the KPI keys: the nightly snapshots, and for the four ★ keys of <see cref="CeoHistory"/> the months no snapshot covers yet,
        /// derived from the records (kept 30 minutes per key). A missing table or any read problem only means "no trend yet".
        /// </summary>
        /// <param name="section">The section whose keys are needed, or null for all of them.</param>
        private async Task LoadHistoryAsync(CeoContext ctx, string section, CancellationToken ct)
        {
            HashSet<string> wanted = (section == null ? CeoKpiSpecs.All : CeoKpiSpecs.Of(section)).Select(k => k.Key).ToHashSet();
            try
            {
                var rows = await _uow.Set<CeoKpiSnapshot>().AsNoTracking().Where(s => s.Date >= ctx.From && s.Date <= ctx.Today)
                    .Select(s => new { s.Key, s.Date, s.Value }).ToListAsync(ct);
                foreach (var group in rows.Where(r => wanted.Contains(r.Key)).GroupBy(r => r.Key))
                    ctx.History[group.Key] = CeoTrends.MonthEnds(group.Select(r => (r.Date, r.Value)), ctx.Today);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "CEO overview: the snapshots could not be read; the trends stay empty.");
                ctx.History.Clear();
            }

            // the months before the snapshots: derived from the records, for the keys that can be (a real snapshot always wins)
            foreach (string key in CeoHistory.Keys.Where(wanted.Contains))
            {
                HashSet<DateTime> covered = ctx.History.TryGetValue(key, out List<CeoTrendPoint> have) ? have.Select(p => p.Month).ToHashSet() : new HashSet<DateTime>();
                List<DateTime> missing = CeoHistory.MissingMonths(ctx.Today, covered);
                if (missing.Count == 0) continue;
                string cacheKey = "ceo:history:" + key + ":" + CeoText.Iso(ctx.Today) + ":" + string.Join(",", missing.Select(CeoText.Iso));
                if (!_cache.TryGetValue(cacheKey, out List<CeoTrendPoint> derived))
                {
                    derived = await CeoHistory.DeriveKeyAsync(_uow, key, ReceivablesOpenOnAsync, missing, _logger, ct);
                    if (derived == null) continue;     // could not be read: not remembered, the next request tries again
                    _cache.Set(cacheKey, derived, CeoCache.HistoryLifetime);
                }
                if (derived.Count == 0) continue;
                List<CeoTrendPoint> merged = have != null ? new List<CeoTrendPoint>(have) : new List<CeoTrendPoint>();
                merged.AddRange(derived.Where(p => !covered.Contains(p.Month)));
                ctx.History[key] = merged.OrderBy(p => p.Month).ToList();
            }
        }

        /// <summary>The open receivables on a day: the total of the as-of ageing of the receivables module, or null when it cannot be read.</summary>
        private async Task<decimal?> ReceivablesOpenOnAsync(DateTime day, CancellationToken ct)
        {
            ResponseDto<AgeingDto> ageing = await _receivables.GetAgeingAsync(day, ct);
            return ageing != null && ageing.Success && ageing.Data != null ? ageing.Data.Total : (decimal?)null;
        }

        // ================================================================================== sections

        private static readonly Dictionary<string, (string Title, string Link, string Prompt)> SectionInfo = new Dictionary<string, (string, string, string)>
        {
            [CeoKeys.Finance] = ("مالی", CeoLinks.FinanceDashboard,
                "بخش مالی شرکت را تحلیل کن: نقد و بانک، مطالبات، بدهی‌های سررسیدگذشته و چک‌های صادره را با ماه قبل مقایسه کن و سه نکته‌ی مهم و ریسک‌ها را با عددهای دقیق بگو."),
            [CeoKeys.Sales] = ("فروش", CeoLinks.SalesDashboard,
                "بخش فروش را تحلیل کن: ارزش خط لوله و سفارش‌های در جریان، نرخ برد و پیشنهادهای نزدیک به مهلت را با دوره‌ی قبل مقایسه کن و سه نکته‌ی مهم و ریسک‌ها را با عددهای دقیق بگو."),
            [CeoKeys.Projects] = ("پروژه‌ها", CeoLinks.Portfolio,
                "وضعیت پروژه‌ها را تحلیل کن: پروژه‌های قرمز و زرد، تأخیرها، ضمانت‌نامه‌ها و جریمه‌ها را بررسی کن و سه نکته‌ی مهم و ریسک‌ها را با عددهای دقیق بگو."),
            [CeoKeys.Production] = ("تولید", CeoLinks.ProductionDashboard,
                "وضعیت تولید را تحلیل کن: دستورهای دارای تأخیر، ماشین‌های خراب، بار مراکز کار و بهره‌وری کلی تجهیزات را بررسی کن و سه نکته‌ی مهم و ریسک‌ها را با عددهای دقیق بگو."),
            [CeoKeys.Quality] = ("کیفیت", CeoLinks.QualityDashboard,
                "وضعیت کیفیت را تحلیل کن: گزارش‌های عدم انطباق، اقدام‌های اصلاحی، کالیبراسیون و شکایت‌های مشتری را با ماه قبل مقایسه کن و سه نکته‌ی مهم و ریسک‌ها را با عددهای دقیق بگو."),
            [CeoKeys.Inventory] = ("انبار", CeoLinks.Stock,
                "وضعیت انبار را تحلیل کن: ارزش موجودی، کالاهای زیر حداقل، قرنطینه و درخواست‌های مواد را با ماه قبل مقایسه کن و سه نکته‌ی مهم و ریسک‌ها را با عددهای دقیق بگو."),
            [CeoKeys.Procurement] = ("تدارکات", CeoLinks.ProcurementOrders,
                "وضعیت تدارکات را تحلیل کن: سفارش‌های خرید باز، ردیف‌های عقب‌افتاده، فاکتورها و پرداخت‌های منتظر را بررسی کن و سه نکته‌ی مهم و ریسک‌ها را با عددهای دقیق بگو."),
            [CeoKeys.Service] = ("خدمات پس از فروش", CeoLinks.ServiceDashboard,
                "وضعیت خدمات پس از فروش را تحلیل کن: درخواست‌های باز، نقض SLA، هزینه‌ی گارانتی و رضایت مشتری را با ماه قبل مقایسه کن و سه نکته‌ی مهم و ریسک‌ها را با عددهای دقیق بگو."),
            [CeoKeys.Office] = ("دفتر", CeoLinks.OfficeInbox,
                "وضعیت دفتر را تحلیل کن: مصوبات معوق، نامه‌های منتظر امضا، درخواست‌های مرخصی و حضور و غیاب ماه را بررسی کن و سه نکته‌ی مهم و ریسک‌ها را با عددهای دقیق بگو."),
        };

        private async Task<CeoSectionDto> SectionAsync(CeoContext ctx, string key, CancellationToken ct)
        {
            (string title, string link, string prompt) = SectionInfo[key];
            CeoSectionDto section;
            if (ctx.BaseFailure != null)
            {
                // the CEO dashboard failed once in this computation: no section tries it (and the modules behind it) again
                section = Unavailable(ctx, key, CeoText.DashboardUnavailable);
            }
            else
            {
                try
                {
                    section = await BuildSectionAsync(ctx, key, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (CeoDashboardUnavailableException)
                {
                    section = Unavailable(ctx, key, CeoText.DashboardUnavailable);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "CEO overview: the section '{Section}' could not be computed.", key);
                    section = Unavailable(ctx, key, CeoText.SectionUnavailable);
                }
            }
            section.Key = key;
            section.Title = title;
            section.Link = link;
            section.AnalysisPrompt = prompt;
            section.AlertCount = section.Kpis.Count(k => k.Status == CeoStatus.Warn || k.Status == CeoStatus.Danger);
            return section;
        }

        /// <summary>
        /// A section that could not be computed keeps the shape of the contract: every KPI of the section as a placeholder (key, title, unit, link and drill kept, value null, status neutral, no trend),
        /// no charts, and a note that says why. Its pulse light is neutral.
        /// </summary>
        private static CeoSectionDto Unavailable(CeoContext ctx, string key, string note)
        {
            var section = new CeoSectionDto { Note = note };
            foreach (CeoKpiSpec spec in CeoKpiSpecs.Of(key)) section.Kpis.Add(CeoKpiSpecs.Placeholder(spec, ctx.Today));
            return section;
        }

        private Task<CeoSectionDto> BuildSectionAsync(CeoContext ctx, string key, CancellationToken ct)
        {
            switch (key)
            {
                case CeoKeys.Finance: return FinanceAsync(ctx, ct);
                case CeoKeys.Sales: return SalesAsync(ctx, ct);
                case CeoKeys.Projects: return ProjectsAsync(ctx, ct);
                case CeoKeys.Production: return ProductionAsync(ctx, ct);
                case CeoKeys.Quality: return QualityAsync(ctx, ct);
                case CeoKeys.Inventory: return InventoryAsync(ctx, ct);
                case CeoKeys.Procurement: return ProcurementAsync(ctx, ct);
                case CeoKeys.Service: return ServiceAsync(ctx, ct);
                case CeoKeys.Office: return OfficeAsync(ctx, ct);
                default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown section.");
            }
        }

        // ================================================================================== headline, alerts, pulse

        /// <summary>The headline figure of a key: a copy of the KPI of the same key in its section (a placeholder without a value when the section could not be computed).</summary>
        private static CeoKpiDto HeadlineOf(IEnumerable<CeoSectionDto> sections, string key, DateTime today)
        {
            CeoKpiDto kpi = sections.SelectMany(s => s.Kpis).FirstOrDefault(k => k.Key == key);
            return kpi != null ? CeoCopy.Of(kpi) : CeoKpiSpecs.Placeholder(CeoKpiSpecs.Get(key), today);
        }

        /// <summary>The alerts of the existing CEO dashboard, worst first (the order inside one severity is the dashboard's own).</summary>
        private static List<CeoAlertDto> AlertsOf(CeoDashboardDto dashboard)
        {
            if (dashboard?.Alerts == null) return new List<CeoAlertDto>();
            string Severity(string level) => level == "danger" ? "danger" : level == "warning" ? "warning" : "info";
            int Rank(string severity) => severity == "danger" ? 0 : severity == "warning" ? 1 : 2;
            return dashboard.Alerts.Where(a => a != null)
                .Select(a => new CeoAlertDto { Severity = Severity(a.Level), Text = a.Text, Link = a.Link })
                .Select((a, index) => (Alert: a, Index: index))
                .OrderBy(x => Rank(x.Alert.Severity)).ThenBy(x => x.Index)
                .Select(x => x.Alert).ToList();
        }

        /// <summary>The light of a section. A section that could not be computed is neutral (it has no figures to judge), not green.</summary>
        private static CeoPulseDto PulseOf(CeoSectionDto section)
        {
            bool failed = !string.IsNullOrEmpty(section.Note);
            return new CeoPulseDto
            {
                Key = section.Key, Title = section.Title, Link = section.Link,
                Status = failed ? CeoStatus.Neutral : CeoRules.Pulse(section.Kpis.Select(k => k.Status)),
                DangerCount = failed ? 0 : section.Kpis.Count(k => k.Status == CeoStatus.Danger),
                WarnCount = failed ? 0 : section.Kpis.Count(k => k.Status == CeoStatus.Warn),
            };
        }

        // ================================================================================== builders shared by the sections

        /// <summary>
        /// One KPI. Its title, unit, front link, drill key and drill parameters come from its spec (<see cref="CeoKpiSpecs"/>: the one definition shared with the placeholder of a failed section);
        /// the value text and the short text are derived from the value and the unit unless a text is given.
        /// </summary>
        private static CeoKpiDto Kpi(CeoContext ctx, string key, decimal? value, string status = null, string note = null, string valueText = null)
        {
            CeoKpiSpec spec = CeoKpiSpecs.Get(key);
            return new CeoKpiDto
            {
                Key = spec.Key, Title = spec.Title, Value = value, Unit = spec.Unit, ValueText = valueText ?? CeoText.ValueText(value, spec.Unit), ShortText = CeoText.ShortText(value, spec.Unit),
                Status = status ?? CeoStatus.Neutral, Link = spec.Link, DrillKey = spec.DrillKey, DrillParams = spec.DrillParams?.Invoke(ctx.Today), Note = note,
            };
        }

        /// <summary>A KPI the reader may not see (payroll privacy): no value, the fixed text, no status.</summary>
        private static CeoKpiDto HiddenKpi(CeoContext ctx, string key, string note = null)
        {
            CeoKpiSpec spec = CeoKpiSpecs.Get(key);
            return new CeoKpiDto
            {
                Key = spec.Key, Title = spec.Title, Value = null, Unit = spec.Unit, ValueText = CeoText.HiddenPayroll, Status = CeoStatus.Neutral, Hidden = true,
                Link = spec.Link, DrillKey = spec.DrillKey, DrillParams = spec.DrillParams?.Invoke(ctx.Today), Note = note,
            };
        }

        /// <summary>Puts the trend of the KPI's key on it (see <see cref="CeoTrends.Apply"/>): the month-end history and, as the last point, the live value; returns the KPI.</summary>
        private static CeoKpiDto Trended(CeoContext ctx, CeoKpiDto kpi, bool? higherIsBetter)
        {
            ctx.History.TryGetValue(kpi.Key, out List<CeoTrendPoint> points);
            CeoTrends.Apply(kpi, points, ctx.Today, higherIsBetter);
            return kpi;
        }

        private static CeoChartDto Chart(string key, string title, string kind, string unit, IEnumerable<string> labels, string link, string drillKey, string note, params CeoChartSeriesDto[] series)
            => new CeoChartDto
            {
                Key = key, Title = title, Kind = kind, Unit = unit, Labels = labels?.ToList() ?? new List<string>(), Series = series.ToList(), Link = link, DrillKey = drillKey, Note = note,
            };

        /// <summary>Sets the drill parameters of a chart (see <see cref="CeoChartDto.DrillParams"/>); returns the chart.</summary>
        private static CeoChartDto WithDrillParams(CeoChartDto chart, Dictionary<string, string> drillParams)
        {
            chart.DrillParams = drillParams;
            return chart;
        }

        private static CeoChartSeriesDto Series(string name, IEnumerable<decimal> values, string role = CeoRole.Primary)
            => new CeoChartSeriesDto { Name = name, Values = values?.ToList() ?? new List<decimal>(), Role = role };

        /// <summary>A chart the reader may not see: it keeps its title and key, but has no labels and no values.</summary>
        private static CeoChartDto HiddenChart(string key, string title, string kind, string unit, string link, string drillKey, string note)
            => new CeoChartDto { Key = key, Title = title, Kind = kind, Unit = unit, Link = link, DrillKey = drillKey, Note = note, Hidden = true };

        /// <summary>Counts of days into the months of the window.</summary>
        private static decimal[] CountByMonth(CeoContext ctx, IEnumerable<DateTime> days) => CeoTrends.SumByMonth(ctx.Window, (days ?? Enumerable.Empty<DateTime>()).Select(d => (d, 1m)));

        /// <summary>A sentence part such as "۳ مورد" for notes.</summary>
        private static string CountText(int n) => CeoText.Number(n) + " مورد";
    }
}
