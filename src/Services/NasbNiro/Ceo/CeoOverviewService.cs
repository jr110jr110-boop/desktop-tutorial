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
        /// <param name="refresh">True skips the 120-second cache and stores the fresh answer in it.</param>
        Task<ResponseDto<CeoOverviewDto>> GetOverviewAsync(bool refresh, CancellationToken ct);

        /// <summary>One section computed now (never cached). The key is one of <see cref="CeoKeys.Sections"/>.</summary>
        Task<ResponseDto<CeoSectionDto>> GetSectionAsync(string key, CancellationToken ct);
    }

    /// <summary>What one computation of the overview shares between its sections: the day, the twelve-month window, the snapshots and the CEO dashboard.</summary>
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

        public CeoDashboardDto Base { get; set; }

        /// <summary>Month-end points of the nightly snapshots by KPI key.</summary>
        public Dictionary<string, List<CeoTrendPoint>> Snapshots { get; } = new Dictionary<string, List<CeoTrendPoint>>();
    }

    /// <summary>
    /// The CEO overview: the headline, the nine sections, the alerts and the pulse lights. Every figure is read from the module that owns it
    /// (the CEO dashboard and the module dashboards, or the records themselves), so it is the same number the module shows.
    /// The sections are built in the partial files of this class; a section that fails is reported empty and does not stop the others.
    /// </summary>
    public sealed partial class CeoOverviewService : ICeoOverviewService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICeoDashboardService _dashboard;
        private readonly IReceivablesService _receivables;
        private readonly ISlDashboardService _sales;
        private readonly IPcReportService _projects;
        private readonly IQualityDashboardService _quality;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CeoOverviewService> _logger;

        public CeoOverviewService(IUnitOfWork uow, IInventoryUserContext user, ICeoDashboardService dashboard, IReceivablesService receivables, ISlDashboardService sales,
            IPcReportService projects, IQualityDashboardService quality, IMemoryCache cache, ILogger<CeoOverviewService> logger)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
            _receivables = receivables ?? throw new ArgumentNullException(nameof(receivables));
            _sales = sales ?? throw new ArgumentNullException(nameof(sales));
            _projects = projects ?? throw new ArgumentNullException(nameof(projects));
            _quality = quality ?? throw new ArgumentNullException(nameof(quality));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger;
        }

        // ================================================================================== entry points

        public async Task<ResponseDto<CeoOverviewDto>> GetOverviewAsync(bool refresh, CancellationToken ct)
        {
            if (!_user.IsInRole(CeoRoles.Readers)) return ResponseDtoHelper.BadRequest<CeoOverviewDto>(CeoText.NotAllowed);
            bool seesPayroll = SeesPayroll();
            string cacheKey = CeoCache.Key(seesPayroll);
            if (!refresh && _cache.TryGetValue(cacheKey, out CeoOverviewDto hit) && hit != null && hit.AsOf == DateTime.Today)
                return ResponseDtoHelper.Ok(AsCached(hit));

            var watch = Stopwatch.StartNew();
            var ctx = new CeoContext(DateTime.Today, seesPayroll);
            await LoadSnapshotsAsync(ctx, ct);
            var dto = new CeoOverviewDto { AsOf = ctx.Today, AsOfText = CeoText.Date(ctx.Today), GeneratedAt = DateTimeOffset.Now };
            foreach (string key in CeoKeys.Sections) dto.Sections.Add(await SectionAsync(ctx, key, ct));
            dto.Headline = CeoKeys.Headline.Select(k => HeadlineOf(dto.Sections, k)).ToList();
            dto.Alerts = AlertsOf(ctx.Base);
            dto.Pulse = dto.Sections.Select(PulseOf).ToList();
            watch.Stop();
            dto.GeneratedInMs = (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds);
            _cache.Set(cacheKey, dto, CeoCache.Lifetime);
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<CeoSectionDto>> GetSectionAsync(string key, CancellationToken ct)
        {
            if (!_user.IsInRole(CeoRoles.Readers)) return ResponseDtoHelper.BadRequest<CeoSectionDto>(CeoText.NotAllowed);
            string section = (key ?? string.Empty).Trim().ToLowerInvariant();
            if (!CeoKeys.Sections.Contains(section)) return ResponseDtoHelper.NotFound<CeoSectionDto>(CeoText.UnknownSection);
            var ctx = new CeoContext(DateTime.Today, SeesPayroll());
            await LoadSnapshotsAsync(ctx, ct);
            return ResponseDtoHelper.Ok(await SectionAsync(ctx, section, ct));
        }

        /// <summary>CEO, finance manager and payroll see pay-based figures; a PMO reader does not, and neither does an AI seat acting as one of them.</summary>
        private bool SeesPayroll() => SvRoles.SeesPayroll(_user.IsInRole(SvRoles.PayrollSeers));

        private static CeoOverviewDto AsCached(CeoOverviewDto source) => new CeoOverviewDto
        {
            AsOf = source.AsOf, AsOfText = source.AsOfText, GeneratedAt = source.GeneratedAt, GeneratedInMs = source.GeneratedInMs, Cached = true,
            Headline = source.Headline, Sections = source.Sections, Alerts = source.Alerts, Pulse = source.Pulse,
        };

        // ================================================================================== shared data

        /// <summary>
        /// The CEO dashboard of this moment, read once per computation (it is the source of most of the counts). It is read exactly as the dashboard endpoint reads it
        /// (no "as of" day: production counts are for today and the SLA is judged now), so the numbers are the ones the module shows.
        /// </summary>
        private async Task<CeoDashboardDto> BaseAsync(CeoContext ctx, CancellationToken ct)
        {
            if (ctx.Base != null) return ctx.Base;
            ResponseDto<CeoDashboardDto> answer = await _dashboard.GetAsync(ct);
            if (answer == null || !answer.Success || answer.Data == null) throw new InvalidOperationException("The CEO dashboard is not available: " + answer?.Message);
            ctx.Base = answer.Data;
            return ctx.Base;
        }

        /// <summary>The month-end points of the nightly snapshots of the last twelve months. A missing table or any read problem only means "no trend yet".</summary>
        private async Task LoadSnapshotsAsync(CeoContext ctx, CancellationToken ct)
        {
            try
            {
                var rows = await _uow.Set<CeoKpiSnapshot>().AsNoTracking().Where(s => s.Date >= ctx.From && s.Date <= ctx.Today)
                    .Select(s => new { s.Key, s.Date, s.Value }).ToListAsync(ct);
                foreach (var group in rows.GroupBy(r => r.Key))
                    ctx.Snapshots[group.Key] = CeoTrends.MonthEnds(group.Select(r => (r.Date, r.Value)), ctx.Today);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "CEO overview: the snapshots could not be read; the trends stay empty.");
                ctx.Snapshots.Clear();
            }
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
            try
            {
                section = await BuildSectionAsync(ctx, key, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "CEO overview: the section '{Section}' could not be computed.", key);
                section = new CeoSectionDto { Note = "اطلاعات این بخش اکنون در دسترس نیست؛ بخش‌های دیگر بدون تغییر نمایش داده می‌شوند." };
            }
            section.Key = key;
            section.Title = title;
            section.Link = link;
            section.AnalysisPrompt = prompt;
            section.AlertCount = section.Kpis.Count(k => k.Status == CeoStatus.Warn || k.Status == CeoStatus.Danger);
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

        private static readonly Dictionary<string, string> HeadlineTitles = new Dictionary<string, string>
        {
            [CeoKeys.Cash] = "نقد و بانک",
            [CeoKeys.ReceivablesOpen] = "مطالبات باز",
            [CeoKeys.PayablesOverdue] = "بدهی سررسیدگذشته",
            [CeoKeys.SalesBacklog] = "سفارش‌های در جریان (backlog)",
            [CeoKeys.ActiveProjects] = "پروژه‌های فعال",
            [CeoKeys.LateProductionOrders] = "دستورهای تولید دارای تأخیر",
            [CeoKeys.OpenNcrs] = "عدم انطباق‌های باز",
            [CeoKeys.ServiceSlaBreaches] = "درخواست‌های خارج از SLA",
        };

        private static readonly Dictionary<string, string> HeadlineUnits = new Dictionary<string, string>
        {
            [CeoKeys.Cash] = CeoUnit.Rial,
            [CeoKeys.ReceivablesOpen] = CeoUnit.Rial,
            [CeoKeys.PayablesOverdue] = CeoUnit.Rial,
            [CeoKeys.SalesBacklog] = CeoUnit.Rial,
            [CeoKeys.ActiveProjects] = CeoUnit.Count,
            [CeoKeys.LateProductionOrders] = CeoUnit.Count,
            [CeoKeys.OpenNcrs] = CeoUnit.Count,
            [CeoKeys.ServiceSlaBreaches] = CeoUnit.Count,
        };

        /// <summary>The headline figure of a key: a copy of the KPI of the same key in its section (a placeholder without a value when the section could not be computed).</summary>
        private static CeoKpiDto HeadlineOf(IEnumerable<CeoSectionDto> sections, string key)
        {
            CeoKpiDto kpi = sections.SelectMany(s => s.Kpis).FirstOrDefault(k => k.Key == key);
            if (kpi != null) return Clone(kpi);
            string unit = HeadlineUnits[key];
            return new CeoKpiDto { Key = key, Title = HeadlineTitles[key], Value = null, Unit = unit, ValueText = CeoText.NoValue, Status = CeoStatus.Neutral };
        }

        private static CeoKpiDto Clone(CeoKpiDto k) => new CeoKpiDto
        {
            Key = k.Key, Title = k.Title, Value = k.Value, Unit = k.Unit, ValueText = k.ValueText, ShortText = k.ShortText, Delta = k.Delta, DeltaText = k.DeltaText, DeltaGood = k.DeltaGood,
            Trend = k.Trend == null ? null : new List<decimal>(k.Trend), TrendLabels = k.TrendLabels == null ? null : new List<string>(k.TrendLabels),
            Status = k.Status, Hidden = k.Hidden, Link = k.Link, DrillKey = k.DrillKey, Note = k.Note,
        };

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

        private static CeoPulseDto PulseOf(CeoSectionDto section) => new CeoPulseDto
        {
            Key = section.Key, Title = section.Title, Link = section.Link,
            Status = CeoRules.Pulse(section.Kpis.Select(k => k.Status)),
            DangerCount = section.Kpis.Count(k => k.Status == CeoStatus.Danger),
            WarnCount = section.Kpis.Count(k => k.Status == CeoStatus.Warn),
        };

        // ================================================================================== builders shared by the sections

        /// <summary>One KPI; the value text and the short text are derived from the value and the unit unless a text is given.</summary>
        private static CeoKpiDto Kpi(string key, string title, decimal? value, string unit, string status = null, string link = null, string drillKey = null, string note = null, string valueText = null)
            => new CeoKpiDto
            {
                Key = key, Title = title, Value = value, Unit = unit, ValueText = valueText ?? CeoText.ValueText(value, unit), ShortText = CeoText.ShortText(value, unit),
                Status = status ?? CeoStatus.Neutral, Link = link, DrillKey = drillKey, Note = note,
            };

        /// <summary>A KPI the reader may not see (payroll privacy): no value, the fixed text, no status.</summary>
        private static CeoKpiDto HiddenKpi(string key, string title, string unit, string link, string drillKey, string note = null)
            => new CeoKpiDto { Key = key, Title = title, Value = null, Unit = unit, ValueText = CeoText.HiddenPayroll, Status = CeoStatus.Neutral, Hidden = true, Link = link, DrillKey = drillKey, Note = note };

        /// <summary>Puts the snapshot trend of the KPI's key on it (see <see cref="CeoTrends.Apply"/>); returns the KPI.</summary>
        private static CeoKpiDto Trended(CeoContext ctx, CeoKpiDto kpi, bool? higherIsBetter)
        {
            ctx.Snapshots.TryGetValue(kpi.Key, out List<CeoTrendPoint> points);
            CeoTrends.Apply(kpi, points, ctx.Today, higherIsBetter);
            return kpi;
        }

        private static CeoChartDto Chart(string key, string title, string kind, string unit, IEnumerable<string> labels, string link, string drillKey, string note, params CeoChartSeriesDto[] series)
            => new CeoChartDto
            {
                Key = key, Title = title, Kind = kind, Unit = unit, Labels = labels?.ToList() ?? new List<string>(), Series = series.ToList(), Link = link, DrillKey = drillKey, Note = note,
            };

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
