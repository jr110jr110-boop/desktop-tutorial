using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ContractsSystem;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Entities.Model.NasbNiro.Tasks;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Attachments;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.DashboardDtos;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IEpmDashboardService
    {
        Task<ResponseDto<List<DashboardIndexDto>>> IndexAsync(CancellationToken cancellationToken);
        Task<ResponseDto<DashboardDto>> GetAsync(string key, int? contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PortfolioEvmDto>> PortfolioEvmAsync(DateTime? asOf, CancellationToken cancellationToken);
        Task<ResponseDto<CommitmentsForecastDto>> CommitmentsAsync(CommitmentsQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<DelayCauseReportDto>> DelayCausesAsync(DelayCauseQuery query, CancellationToken cancellationToken);
    }

    /// <summary>Roles that see each dashboard (the owner modules' readers, narrowed to management).</summary>
    public static class EpmRoles
    {
        public const string Portfolio = "PMO,CEO,PROJECTMANAGER,PLANNING";
        public const string Project = ProjectControlRoles.Readers;
        public const string Procurement = "PMO,CEO,COMMERCIAL,PLANNING,FINANCIAL,FINANCEMANAGER,AUDITOR";
        public const string Warehouse = "PMO,CEO,WAREHOUSE,WHSUPERVISOR,PLANNING,AUDITOR";
        public const string Quality = "PMO,CEO,QC,QA,ENGINEERING,AUDITOR";
        public const string Production = "PMO,CEO,PRODUCTIONMANAGER,SHOPSUPERVISOR,PLANNING";
        public const string Tasks = "PMO,CEO,PROJECTMANAGER,PLANNING,HR,HRMANAGER";
        public const string Finance = "PMO,CEO,FINANCIAL,FINANCEMANAGER,TREASURY,AUDITOR";
        public const string Ceo = "CEO,PMO";

        /// <summary>The forecast and the delay-cause report: management and finance.</summary>
        public const string Forecast = "PMO,CEO,PROJECTMANAGER,PLANNING,FINANCIAL,FINANCEMANAGER,TREASURY,COMMERCIAL,AUDITOR";
    }

    /// <summary>
    /// The management dashboards (EPM): read only, every widget with the page it drills down to, money hidden from the roles without price access. Each figure comes from one batched query
    /// (counts grouped in the database), never from a query per row; the portfolio EVM is the one place that works per project (the EVM calculation of the module, a fixed number of queries each).
    /// </summary>
    public class EpmDashboardService : IEpmDashboardService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPcProgressService _progress;

        public EpmDashboardService(IUnitOfWork uow, IInventoryUserContext user, IPcProgressService progress)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        }

        private bool SeesPrices => _user.IsInRole(AttachmentRoles.Prices);
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);

        private static readonly (string Key, string Title, string Description, string Roles, bool NeedsContract)[] Catalog =
        {
            ("portfolio", "داشبورد سبد پروژه‌ها", "شاخص‌های EVM سبد و تفکیک پروژه‌ها، موضوعات باز و درخواست‌های تغییر", EpmRoles.Portfolio, false),
            ("project", "داشبورد پروژه", "EVM، مدارک مهندسی، موضوعات، علل تأخیر و تعهدات یک پروژه", EpmRoles.Project, true),
            ("procurement", "داشبورد تدارکات", "درخواست‌ها، سفارش‌ها، استعلام‌ها و اقلام دیرکرددار", EpmRoles.Procurement, false),
            ("warehouse", "داشبورد انبار", "موجودی، انبارها و پرموجودی‌ترین اقلام", EpmRoles.Warehouse, false),
            ("quality", "داشبورد کیفیت", "گزارش‌های عدم انطباق و نتیجه‌ی بازرسی‌ها", EpmRoles.Quality, false),
            ("production", "داشبورد تولید", "دستورهای تولید، تأخیرها و توقف‌های کیفی", EpmRoles.Production, false),
            ("tasks", "داشبورد وظایف و منابع انسانی", "وظایف باز، دیرکرد و پیشرفت", EpmRoles.Tasks, false),
            ("finance", "داشبورد مالی و نقدینگی", "تعهدات آینده‌ی سفارش‌ها، استعلام‌ها و صورت‌وضعیت‌ها", EpmRoles.Finance, false),
            ("ceo", "داشبورد مدیرعامل", "خلاصه‌ی همه‌ی داشبوردها", EpmRoles.Ceo, false),
        };

        public Task<ResponseDto<List<DashboardIndexDto>>> IndexAsync(CancellationToken ct) =>
            Task.FromResult(ResponseDtoHelper.Ok(Catalog.Where(c => _user.IsInRole(c.Roles)).Select(c => new DashboardIndexDto { Key = c.Key, Title = c.Title, Description = c.Description, NeedsContract = c.NeedsContract }).ToList()));

        public async Task<ResponseDto<DashboardDto>> GetAsync(string key, int? contractId, CancellationToken ct)
        {
            string k = (key ?? "").Trim().ToLowerInvariant();
            var entry = Catalog.FirstOrDefault(c => c.Key == k);
            if (entry.Key == null) return PcText.NotFound<DashboardDto>("داشبورد پیدا نشد.");
            if (!_user.IsInRole(entry.Roles)) return Bad<DashboardDto>(PcText.NotAllowed);
            DashboardDto d;
            switch (k)
            {
                case "portfolio": d = await PortfolioAsync(ct); break;
                case "project":
                    if (!contractId.HasValue) return Bad<DashboardDto>("پروژه را انتخاب کنید.");
                    if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == contractId.Value && !c.Deleted, ct)) return PcText.NotFound<DashboardDto>(PcText.ContractNotFound);
                    d = await ProjectAsync(contractId.Value, ct); break;
                case "procurement": d = await ProcurementAsync(ct); break;
                case "warehouse": d = await WarehouseAsync(ct); break;
                case "quality": d = await QualityAsync(ct); break;
                case "production": d = await ProductionAsync(ct); break;
                case "tasks": d = await TasksAsync(ct); break;
                case "finance": d = await FinanceAsync(ct); break;
                default: d = await CeoAsync(ct); break;
            }
            d.Key = k; d.Title = entry.Title; d.GeneratedAt = DateTimeOffset.Now; d.PricesMasked = !SeesPrices; d.ContractId = contractId;
            return ResponseDtoHelper.Ok(d);
        }

        // ============================================================================ widget helpers

        private static DashKpiDto Kpi(string key, string title, decimal? value, string unit = null, string tone = "info", string link = null, bool masked = false) =>
            new DashKpiDto { Key = key, Title = title, Value = masked ? null : value, Unit = unit, Tone = tone, Link = link, Masked = masked };

        private static DashChartDto Chart(string key, string title, string kind, IEnumerable<string> labels, IEnumerable<(string Name, IEnumerable<decimal?> Values)> series, string link = null, bool masked = false) =>
            new DashChartDto { Key = key, Title = title, Kind = kind, Labels = labels.ToList(), Link = link, Masked = masked, Series = series.Select(s => new DashSeriesDto { Name = s.Name, Values = masked ? s.Values.Select(_ => (decimal?)null).ToList() : s.Values.ToList() }).ToList() };

        private static DashChartDto CountChart(string key, string title, string kind, Dictionary<string, int> counts, string link = null) =>
            Chart(key, title, kind, counts.Keys, new[] { ("تعداد", counts.Values.Select(v => (decimal?)v)) }, link);

        private static string Date(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        private static string Num(decimal? v, int digits = 2) => v.HasValue ? Math.Round(v.Value, digits).ToString("0.##", CultureInfo.InvariantCulture) : "";

        private static string ToneOfIndex(decimal? v) => !v.HasValue ? "info" : v.Value >= 0.95m ? "ok" : v.Value >= 0.85m ? "warn" : "bad";

        private async Task<List<ContractEntity>> ActiveContractsAsync(CancellationToken ct) =>
            await _uow.Set<ContractEntity>().AsNoTracking().Where(c => !c.Deleted && c.Status == ContractStatus.Current).OrderBy(c => c.Code).Take(200).ToListAsync(ct);

        // ============================================================================ portfolio EVM (R15)

        public async Task<ResponseDto<PortfolioEvmDto>> PortfolioEvmAsync(DateTime? asOf, CancellationToken ct)
        {
            if (!_user.IsInRole(EpmRoles.Forecast)) return Bad<PortfolioEvmDto>(PcText.NotAllowed);
            return ResponseDtoHelper.Ok(await PortfolioEvmCoreAsync(asOf ?? DateTime.Today, ct));
        }

        private async Task<PortfolioEvmDto> PortfolioEvmCoreAsync(DateTime asOf, CancellationToken ct)
        {
            bool prices = SeesPrices;
            List<ContractEntity> contracts = await ActiveContractsAsync(ct);
            var dto = new PortfolioEvmDto { AsOf = asOf.Date, PricesMasked = !prices, Projects = contracts.Count };
            decimal bac = 0, pv = 0, ev = 0, ac = 0, eac = 0;
            foreach (ContractEntity c in contracts)
            {
                PcEvmDto e = await _progress.ComputeEvmAsync(c.Id, asOf.Date, ct);
                bac += e.Bac; pv += e.Pv; ev += e.Ev; ac += e.Ac; eac += e.EacTypical ?? e.Bac;
                dto.Rows.Add(new PortfolioEvmRowDto
                {
                    ContractId = c.Id, Code = c.Code, Title = c.Title, Cpi = e.Cpi, Spi = e.Spi, Tone = ToneOfIndex(e.Cpi.HasValue && e.Spi.HasValue ? Math.Min(e.Cpi.Value, e.Spi.Value) : e.Cpi ?? e.Spi),
                    Bac = prices ? e.Bac : (decimal?)null, Pv = prices ? e.Pv : (decimal?)null, Ev = prices ? e.Ev : (decimal?)null, Ac = prices ? e.Ac : (decimal?)null, EacTypical = prices ? e.EacTypical : (decimal?)null,
                });
            }
            // the portfolio indices weigh each project by its money (sum of earned value over sum of actual cost / planned value), not the mean of the project indices
            dto.Cpi = ac > 0 ? Math.Round(ev / ac, 4) : (decimal?)null;
            dto.Spi = pv > 0 ? Math.Round(ev / pv, 4) : (decimal?)null;
            if (prices) { dto.Bac = bac; dto.Pv = pv; dto.Ev = ev; dto.Ac = ac; dto.EacTypical = eac; }
            return dto;
        }

        // ============================================================================ commitments forecast (R14, R18)

        public async Task<ResponseDto<CommitmentsForecastDto>> CommitmentsAsync(CommitmentsQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EpmRoles.Forecast)) return Bad<CommitmentsForecastDto>(PcText.NotAllowed);
            query ??= new CommitmentsQuery();
            string period = (query.Period ?? "week").Trim().ToLowerInvariant();
            if (period != "week" && period != "month") return Bad<CommitmentsForecastDto>("دوره باید هفتگی (week) یا ماهانه (month) باشد.");
            int count = Math.Max(1, Math.Min(query.Count ?? (period == "week" ? 8 : 6), 52));
            DateTime from = (query.From ?? DateTime.Today).Date;
            // weeks start on Saturday (the Iranian working week)
            if (period == "week") from = from.AddDays(-(((int)from.DayOfWeek + 1) % 7));
            else from = PersianMonthStart(from);
            var dto = new CommitmentsForecastDto { Period = period, From = from, ContractId = query.ContractId, PricesMasked = !SeesPrices };
            if (!SeesPrices)
            {
                // the figures are money: the buckets are returned empty so the page shows the masked state
                for (int i = 0; i < count; i++) dto.Buckets.Add(Bucket(period, from, i));
                return ResponseDtoHelper.Ok(dto);
            }
            DateTime today = DateTime.Today;
            var items = new List<CommitmentItemDto>();

            // open orders: what is still to receive (quantity not yet accepted) at the delivery date of the line
            var lines = await _uow.Set<PurchaseOrderLine>().AsNoTracking()
                .Where(l => l.Quantity > l.ReceivedQuantity && l.PurchaseOrder.Status != PurchaseOrderStatus.Draft && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled && l.PurchaseOrder.Status != PurchaseOrderStatus.Closed && l.PurchaseOrder.Status != PurchaseOrderStatus.Received
                    && (query.ContractId == null || (l.ContractId ?? l.PurchaseOrder.ContractId) == query.ContractId))
                .Select(l => new { l.PurchaseOrder.Number, l.PurchaseOrderId, Contract = l.ContractId ?? l.PurchaseOrder.ContractId, l.Quantity, l.ReceivedQuantity, l.UnitPrice, l.DeliveryDate, l.PurchaseOrder.OrderDate, l.PurchaseOrder.Currency, l.PurchaseOrder.ExchangeRate }).ToListAsync(ct);
            // M8: every figure is in rial. A foreign-currency order is converted with the rate written on it; one without a rate is left out and counted in ExcludedForeignOrders (never added at face value)
            var excluded = new HashSet<int>();
            var rated = new List<(int OrderId, string Number, int? Contract, DateTime Date, decimal Amount)>();
            foreach (var l in lines)
            {
                decimal rate = 1m;
                if (!IsRial(l.Currency))
                {
                    if (!l.ExchangeRate.HasValue || l.ExchangeRate.Value <= 0) { excluded.Add(l.PurchaseOrderId); continue; }
                    rate = l.ExchangeRate.Value;
                }
                rated.Add((l.PurchaseOrderId, l.Number, l.Contract, (l.DeliveryDate ?? l.OrderDate.AddDays(30)).Date, (l.Quantity - l.ReceivedQuantity) * l.UnitPrice * rate));
            }
            dto.ExcludedForeignOrders = excluded.Count;
            foreach (var g in rated.GroupBy(l => new { l.OrderId, l.Number, l.Contract, l.Date }))
                items.Add(new CommitmentItemDto { Source = "order", Number = g.Key.Number, ContractId = g.Key.Contract, Date = g.Key.Date, Amount = g.Sum(l => l.Amount), Link = "/procurement/orders?number=" + g.Key.Number });

            // inquiries with a selected quote that no order has taken yet: the value of the selected quote at its delivery time
            var inquiries = await _uow.Set<PurchaseInquiry>().AsNoTracking()
                .Where(i => (i.Status == InquiryStatus.Selected || i.Status == InquiryStatus.Approved) && i.SelectedQuoteId != null
                    && !_uow.Set<PurchaseOrder>().Any(o => o.InquiryId == i.Id && o.Status != PurchaseOrderStatus.Cancelled)
                    && (query.ContractId == null || i.PurchaseRequest.ContractId == query.ContractId))
                .Select(i => new { i.Id, i.Number, i.SelectedQuoteId, i.SelectedAt, Contract = i.PurchaseRequest.ContractId }).ToListAsync(ct);
            if (inquiries.Count > 0)
            {
                List<int> quoteIds = inquiries.Select(i => i.SelectedQuoteId.Value).ToList();
                Dictionary<int, decimal> totals = (await _uow.Set<SupplierQuoteLine>().AsNoTracking().Where(l => quoteIds.Contains(l.QuoteId)).Select(l => new { l.QuoteId, V = l.Quantity * l.UnitPrice }).ToListAsync(ct))
                    .GroupBy(x => x.QuoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.V));
                Dictionary<int, int?> days = (await _uow.Set<SupplierQuote>().AsNoTracking().Where(q => quoteIds.Contains(q.Id)).Select(q => new { q.Id, q.DeliveryDays }).ToListAsync(ct)).ToDictionary(q => q.Id, q => q.DeliveryDays);
                foreach (var i in inquiries)
                    items.Add(new CommitmentItemDto { Source = "inquiry", Number = i.Number, ContractId = i.Contract, Date = ((i.SelectedAt?.LocalDateTime.Date ?? today).AddDays(days.TryGetValue(i.SelectedQuoteId.Value, out int? dd) && dd.HasValue ? dd.Value : 30)), Amount = totals.TryGetValue(i.SelectedQuoteId.Value, out decimal t) ? t : 0m, Link = "/procurement/inquiries?number=" + i.Number });
            }

            // contractor / supplier progress statements that are not paid yet: the payable amount at the sending date (or the end of the period)
            var statements = await _uow.Set<PsStatement>().AsNoTracking()
                .Where(s => !s.Deleted && s.Side == PsSide.Supplier && s.Status != PsStatus.Draft && s.Status != PsStatus.Paid && s.Status != PsStatus.Returned && s.Status != PsStatus.Rejected
                    && (query.ContractId == null || s.ContractId == query.ContractId))
                .Select(s => new { s.Number, s.ContractId, s.SendingDate, s.PeriodTo, s.PayableThisPeriod }).ToListAsync(ct);
            foreach (var s in statements)
                items.Add(new CommitmentItemDto { Source = "statement", Number = s.Number, ContractId = s.ContractId, Date = (s.SendingDate ?? s.PeriodTo).Date, Amount = s.PayableThisPeriod, Link = "/procurement/statements?number=" + s.Number });

            DateTime end = Bucket(period, from, count - 1).To;
            for (int i = 0; i < count; i++) dto.Buckets.Add(Bucket(period, from, i));
            foreach (CommitmentItemDto it in items.Where(x => x.Amount != 0).OrderBy(x => x.Date).ThenBy(x => x.Number, StringComparer.Ordinal))
            {
                // M8: what is already past due is kept apart (Overdue); it is not added to the first bucket or to the totals of the buckets
                if (it.Date < from) { dto.Overdue += it.Amount; it.IsOverdue = true; dto.Items.Add(it); continue; }
                if (it.Date > end) continue;
                CommitmentBucketDto b = dto.Buckets.First(x => it.Date >= x.From && it.Date <= x.To);
                if (it.Source == "order") b.OpenOrders += it.Amount; else if (it.Source == "inquiry") b.Inquiries += it.Amount; else b.Statements += it.Amount;
                b.Total += it.Amount;
                dto.Items.Add(it);
            }
            dto.OpenOrdersTotal = dto.Buckets.Sum(b => b.OpenOrders); dto.InquiriesTotal = dto.Buckets.Sum(b => b.Inquiries); dto.StatementsTotal = dto.Buckets.Sum(b => b.Statements); dto.Total = dto.Buckets.Sum(b => b.Total);
            dto.TotalWithOverdue = dto.Total + dto.Overdue;
            return ResponseDtoHelper.Ok(dto);
        }

        private static readonly PersianCalendar Pcal = new PersianCalendar();

        private static bool IsRial(string currency)
        {
            string c = (currency ?? "").Trim().ToUpperInvariant();
            return c.Length == 0 || c == "IRR" || c == "IRT" || c == "RIAL" || c == "ریال";
        }

        /// <summary>First day (Gregorian date) of the Persian month that holds the date.</summary>
        private static DateTime PersianMonthStart(DateTime d) => Pcal.ToDateTime(Pcal.GetYear(d), Pcal.GetMonth(d), 1, 0, 0, 0, 0);

        // M6: the monthly buckets follow the Persian months and are labelled in Persian; the dates travel as ISO and the page formats them
        private static CommitmentBucketDto Bucket(string period, DateTime from, int index)
        {
            DateTime a, b;
            if (period == "week") { a = from.AddDays(7 * index); b = a.AddDays(6); }
            else
            {
                int y = Pcal.GetYear(from), m = Pcal.GetMonth(from) - 1 + index;
                y += m / 12; m = m % 12 + 1;
                a = Pcal.ToDateTime(y, m, 1, 0, 0, 0, 0);
                b = (m == 12 ? Pcal.ToDateTime(y + 1, 1, 1, 0, 0, 0, 0) : Pcal.ToDateTime(y, m + 1, 1, 0, 0, 0, 0)).AddDays(-1);
            }
            string label = period == "week" ? "هفته‌ی " + FiscalCalendars.Current.Text(a) : FiscalCalendars.Current.PeriodTitle(Pcal.GetYear(a), Pcal.GetMonth(a));
            return new CommitmentBucketDto { From = a, To = b, Label = label };
        }

        // ============================================================================ delay causes (R19)

        public async Task<ResponseDto<DelayCauseReportDto>> DelayCausesAsync(DelayCauseQuery query, CancellationToken ct)
        {
            // the public report is for management and finance only (the front route says the same); the project dashboard reads the core below with its own gate
            if (!_user.IsInRole(EpmRoles.Forecast)) return Bad<DelayCauseReportDto>(PcText.NotAllowed);
            return await DelayCausesCoreAsync(query, ct);
        }

        private async Task<ResponseDto<DelayCauseReportDto>> DelayCausesCoreAsync(DelayCauseQuery query, CancellationToken ct)
        {
            query ??= new DelayCauseQuery();
            DateTime to = (query.To ?? DateTime.Today).Date;
            DateTime from = (query.From ?? to.AddDays(-90)).Date;
            if (from > to) return Bad<DelayCauseReportDto>("بازه‌ی تاریخ نامعتبر است.");
            string cause = PcText.Clean(query.CauseCode);
            var report = new DelayCauseReportDto { From = from, To = to, ContractId = query.ContractId };

            // the late lines of the confirmed daily reports in the range (one query), grouped by cause code
            var lines = await _uow.Set<PcDailyReportLine>().AsNoTracking()
                .Where(l => l.IsLate && l.Report.ConfirmedAt != null && !l.Report.Deleted && l.Report.ReportDate >= from && l.Report.ReportDate <= to
                    && (query.ContractId == null || l.Report.ContractId == query.ContractId) && (cause == null || l.DelayReasonCode == cause))
                .Select(l => new { l.DelayReasonCode, l.DelayReason, l.ActivityId, l.ReportId }).ToListAsync(ct);
            report.TotalLateLines = lines.Count;
            var issues = await _uow.Set<PcIssue>().AsNoTracking()
                .Where(i => !i.Deleted && i.DelayReasonCode != null && i.RaisedDate <= to && (i.ClosedAt == null || i.RaisedDate >= from || i.DueDate >= from)
                    && (query.ContractId == null || i.ContractId == query.ContractId) && (cause == null || i.DelayReasonCode == cause))
                .Select(i => new { i.DelayReasonCode, i.OwnerPositionId, i.Status, i.DueDate }).ToListAsync(ct);
            DateTime today = DateTime.Today;
            var byLines = lines.GroupBy(l => l.DelayReasonCode ?? "-").ToDictionary(g => g.Key, g => g.ToList());
            foreach (string code in issues.Select(i => i.DelayReasonCode ?? "-").Distinct()) if (!byLines.ContainsKey(code)) byLines[code] = lines.Take(0).ToList();
            foreach (var g in byLines.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new { Key = kv.Key, Items = kv.Value }))
            {
                var mine = issues.Where(i => (i.DelayReasonCode ?? "-") == g.Key).ToList();
                report.ByCause.Add(new DelayCauseRowDto
                {
                    CauseCode = g.Key, Cause = g.Items.Select(x => PcText.Clean(x.DelayReason)).FirstOrDefault(x => x != null) ?? g.Key, LateLines = g.Items.Count, Activities = g.Items.Select(x => x.ActivityId).Distinct().Count(), Reports = g.Items.Select(x => x.ReportId).Distinct().Count(),
                    OpenIssues = mine.Count(i => i.Status == PcIssueStatus.Open || i.Status == PcIssueStatus.InProgress), ClosedIssues = mine.Count(i => i.Status == PcIssueStatus.Resolved || i.Status == PcIssueStatus.Closed),
                });
            }
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, issues.Select(i => i.OwnerPositionId), ct);
            foreach (var g in issues.GroupBy(i => new { Code = i.DelayReasonCode, i.OwnerPositionId }).OrderByDescending(g => g.Count()))
                report.ByResponsible.Add(new DelayResponsibleRowDto
                {
                    CauseCode = g.Key.Code, PositionId = g.Key.OwnerPositionId, Responsible = positions.Title(g.Key.OwnerPositionId) ?? "بدون مسئول",
                    OpenIssues = g.Count(i => i.Status == PcIssueStatus.Open || i.Status == PcIssueStatus.InProgress), ClosedIssues = g.Count(i => i.Status == PcIssueStatus.Resolved || i.Status == PcIssueStatus.Closed),
                    OverdueIssues = g.Count(i => (i.Status == PcIssueStatus.Open || i.Status == PcIssueStatus.InProgress) && i.DueDate.HasValue && i.DueDate.Value.Date < today),
                });
            return ResponseDtoHelper.Ok(report);
        }

        // ============================================================================ the dashboards

        private async Task<DashboardDto> PortfolioAsync(CancellationToken ct)
        {
            PortfolioEvmDto evm = await PortfolioEvmCoreAsync(DateTime.Today, ct);
            var d = new DashboardDto();
            bool masked = !SeesPrices;
            d.Kpis.Add(Kpi("projects", "پروژه‌های جاری", evm.Projects, "پروژه", "info", "/project-control/portfolio"));
            d.Kpis.Add(Kpi("cpi", "CPI سبد (وزنی)", evm.Cpi, null, ToneOfIndex(evm.Cpi), "/project-control/portfolio"));
            d.Kpis.Add(Kpi("spi", "SPI سبد (وزنی)", evm.Spi, null, ToneOfIndex(evm.Spi), "/project-control/portfolio"));
            d.Kpis.Add(Kpi("ev", "ارزش کسب‌شده (EV)", evm.Ev, "ریال", "info", "/project-control/portfolio", masked));
            d.Kpis.Add(Kpi("ac", "هزینه واقعی (AC)", evm.Ac, "ریال", "info", "/project-control/portfolio", masked));
            List<int> ids = evm.Rows.Select(r => r.ContractId).ToList();
            var issues = (await _uow.Set<PcIssue>().AsNoTracking().Where(i => !i.Deleted && ids.Contains(i.ContractId) && (i.Status == PcIssueStatus.Open || i.Status == PcIssueStatus.InProgress)).Select(i => i.ContractId).ToListAsync(ct)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var changes = (await _uow.Set<PcChangeRequest>().AsNoTracking().Where(c => !c.Deleted && ids.Contains(c.ContractId) && c.Status != PcChangeStatus.Closed && c.Status != PcChangeStatus.Rejected && c.Status != PcChangeStatus.Draft).Select(c => c.ContractId).ToListAsync(ct)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            d.Kpis.Add(Kpi("issues", "موضوعات باز", issues.Values.Sum(), null, issues.Values.Sum() > 0 ? "warn" : "ok", "/project-control/registers?tab=issues"));
            d.Kpis.Add(Kpi("changes", "درخواست‌های تغییر در جریان", changes.Values.Sum(), null, "info", "/project-control/changes"));
            d.Charts.Add(Chart("indices", "CPI و SPI به تفکیک پروژه", "bar", evm.Rows.Select(r => r.Code), new[] { ("CPI", evm.Rows.Select(r => r.Cpi)), ("SPI", evm.Rows.Select(r => r.Spi)) }, "/project-control/portfolio"));
            var t = new DashTableDto { Key = "projects", Title = "پروژه‌ها", Columns = new List<string> { "کد", "عنوان", "CPI", "SPI", "موضوع باز", "تغییر در جریان" }, Link = "/project-control/portfolio" };
            foreach (PortfolioEvmRowDto r in evm.Rows)
            {
                t.Rows.Add(new List<string> { r.Code, r.Title, Num(r.Cpi), Num(r.Spi), (issues.TryGetValue(r.ContractId, out int a) ? a : 0).ToString(CultureInfo.InvariantCulture), (changes.TryGetValue(r.ContractId, out int b) ? b : 0).ToString(CultureInfo.InvariantCulture) });
                t.RowLinks.Add("/project-control/one-page?contractId=" + r.ContractId);
            }
            d.Tables.Add(t);
            return d;
        }

        private async Task<DashboardDto> ProjectAsync(int contractId, CancellationToken ct)
        {
            PcEvmDto e = await _progress.ComputeEvmAsync(contractId, DateTime.Today, ct);
            bool masked = !SeesPrices;
            var d = new DashboardDto();
            string pc = "/project-control/one-page?contractId=" + contractId;
            d.Kpis.Add(Kpi("cpi", "CPI", e.Cpi, null, ToneOfIndex(e.Cpi), pc));
            d.Kpis.Add(Kpi("spi", "SPI", e.Spi, null, ToneOfIndex(e.Spi), pc));
            d.Kpis.Add(Kpi("ev", "ارزش کسب‌شده", e.Ev, "ریال", "info", pc, masked));
            d.Kpis.Add(Kpi("ac", "هزینه واقعی", e.Ac, "ریال", "info", pc, masked));
            d.Kpis.Add(Kpi("eac", "برآورد در پایان (EAC)", e.EacTypical, "ریال", "info", pc, masked));
            var docs = (await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(x => !x.Deleted && x.ContractId == contractId && x.Required).Select(x => x.IssueState).ToListAsync(ct)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            string[] names = { "صادرنشده", "صدور اول", "صدور دوم", "تاییدشده" };
            d.Charts.Add(Chart("docs", "وضعیت مدارک مهندسی", "donut", Enum.GetValues(typeof(PcIssueState)).Cast<PcIssueState>().Select(s => names[(int)s]), new[] { ("تعداد", Enum.GetValues(typeof(PcIssueState)).Cast<PcIssueState>().Select(s => (decimal?)(docs.TryGetValue(s, out int n) ? n : 0))) }, "/project-control/mdr?contractId=" + contractId));
            int open = await _uow.Set<PcIssue>().CountAsync(i => !i.Deleted && i.ContractId == contractId && (i.Status == PcIssueStatus.Open || i.Status == PcIssueStatus.InProgress), ct);
            d.Kpis.Add(Kpi("issues", "موضوعات باز", open, null, open > 0 ? "warn" : "ok", "/project-control/registers?tab=issues&contractId=" + contractId));
            int cr = await _uow.Set<PcChangeRequest>().CountAsync(c => !c.Deleted && c.ContractId == contractId && c.Status != PcChangeStatus.Closed && c.Status != PcChangeStatus.Rejected && c.Status != PcChangeStatus.Draft, ct);
            d.Kpis.Add(Kpi("changes", "درخواست‌های تغییر در جریان", cr, null, "info", "/project-control/changes?contractId=" + contractId));
            DelayCauseReportDto delays = (await DelayCausesCoreAsync(new DelayCauseQuery { ContractId = contractId }, ct)).Data;
            var t = new DashTableDto { Key = "delays", Title = "علل تأخیر (۹۰ روز اخیر)", Columns = new List<string> { "علت", "ردیف‌های دیرکرد", "فعالیت", "موضوع باز" }, Link = "/project-control/delay-causes?contractId=" + contractId };
            foreach (DelayCauseRowDto r in (delays?.ByCause ?? new List<DelayCauseRowDto>()).Take(8)) t.Rows.Add(new List<string> { r.Cause, r.LateLines.ToString(CultureInfo.InvariantCulture), r.Activities.ToString(CultureInfo.InvariantCulture), r.OpenIssues.ToString(CultureInfo.InvariantCulture) });
            d.Tables.Add(t);
            return d;
        }

        // Persian labels of the status enums shown on the dashboards (M6); an unknown name is shown as it is
        private static readonly Dictionary<string, string> Fa = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Draft"] = "پیش‌نویس", ["Submitted"] = "ارسال‌شده", ["Approved"] = "تاییدشده", ["Ordered"] = "سفارش‌شده", ["Closed"] = "بسته‌شده", ["Returned"] = "برگشت‌خورده", ["Rejected"] = "ردشده",
            ["Released"] = "آزادشده", ["PartlyReceived"] = "دریافت جزئی", ["Received"] = "دریافت‌شده", ["Cancelled"] = "لغوشده", ["Open"] = "باز", ["Executed"] = "اجراشده",
            ["NotClassified"] = "طبقه‌بندی‌نشده", ["Minor"] = "جزئی", ["Major"] = "عمده", ["Critical"] = "بحرانی", ["Accept"] = "پذیرش", ["Reject"] = "رد", ["Rework"] = "بازکاری", ["Hold"] = "نگه‌داشت",
            ["InProgress"] = "در حال انجام", ["Completed"] = "تکمیل‌شده", ["Pending"] = "در انتظار", ["Assigned"] = "واگذارشده", ["Suspended"] = "معلق",
        };
        private static string FaOf(object status) { string n = status?.ToString() ?? ""; return Fa.TryGetValue(n, out string t) ? t : n; }
        private static Dictionary<string, int> Counts<T>(IEnumerable<(T Key, int Count)> rows) => rows.GroupBy(r => FaOf(r.Key)).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        private static readonly string[] RialNames = { "", "IRR", "IRT", "RIAL", "ریال" };

        private async Task<DashboardDto> ProcurementAsync(CancellationToken ct)
        {
            var d = new DashboardDto();
            bool masked = !SeesPrices;
            DateTime today = DateTime.Today;
            // M7: everything is grouped / counted in the database; the lists are limited to ten rows there
            var pr = Counts((await _uow.Set<PurchaseRequest>().AsNoTracking().Where(r => !r.Deleted).GroupBy(r => r.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)));
            var po = Counts((await _uow.Set<PurchaseOrder>().AsNoTracking().Where(o => !o.Deleted).GroupBy(o => o.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)));
            int inquiries = await _uow.Set<PurchaseInquiry>().CountAsync(i => !i.Deleted && i.Status == InquiryStatus.Open, ct);
            IQueryable<PurchaseOrderLine> openLines = _uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.Quantity > l.ReceivedQuantity
                && l.PurchaseOrder.Status != PurchaseOrderStatus.Draft && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled && l.PurchaseOrder.Status != PurchaseOrderStatus.Closed && l.PurchaseOrder.Status != PurchaseOrderStatus.Received);
            IQueryable<PurchaseOrderLine> late = openLines.Where(l => l.DeliveryDate != null && l.DeliveryDate < today);
            var overdue = await late.OrderBy(l => l.DeliveryDate).Take(10).Select(l => new { l.PurchaseOrder.Number, l.Description, l.Quantity, l.ReceivedQuantity, l.DeliveryDate }).ToListAsync(ct);
            int overdueCount = await late.CountAsync(ct);
            d.Kpis.Add(Kpi("requests", "درخواست‌های خرید", pr.Values.Sum(), null, "info", "/procurement/requests"));
            d.Kpis.Add(Kpi("orders", "سفارش‌های خرید", po.Values.Sum(), null, "info", "/procurement/orders"));
            d.Kpis.Add(Kpi("inquiries", "استعلام‌های باز", inquiries, null, inquiries > 0 ? "warn" : "ok", "/procurement/inquiries"));
            d.Kpis.Add(Kpi("overdue", "اقلام دیرکرد تحویل", overdueCount, null, overdueCount > 0 ? "bad" : "ok", "/procurement/orders"));
            // M8: rial; a foreign-currency order counts with the rate written on it, one without a rate is left out and counted apart
            decimal openValue = 0m; int noRate = 0;
            if (!masked)
            {
                openValue = (await openLines.SumAsync(l => (decimal?)((l.Quantity - l.ReceivedQuantity) * l.UnitPrice *
                    (l.PurchaseOrder.Currency == null || RialNames.Contains(l.PurchaseOrder.Currency) ? 1m : (l.PurchaseOrder.ExchangeRate ?? 0m))), ct)) ?? 0m;
                noRate = await openLines.Where(l => l.PurchaseOrder.Currency != null && !RialNames.Contains(l.PurchaseOrder.Currency) && (l.PurchaseOrder.ExchangeRate == null || l.PurchaseOrder.ExchangeRate <= 0)).Select(l => l.PurchaseOrderId).Distinct().CountAsync(ct);
            }
            d.Kpis.Add(Kpi("open-value", "ارزش سفارش‌های دریافت‌نشده (ریال)", openValue, "ریال", "info", "/procurement/orders", masked));
            if (!masked && noRate > 0) d.Kpis.Add(Kpi("no-rate", "سفارش ارزی بدون نرخ (خارج از جمع)", noRate, null, "warn", "/procurement/orders"));
            d.Charts.Add(CountChart("requests", "درخواست‌ها بر پایه وضعیت", "bar", pr, "/procurement/requests"));
            d.Charts.Add(CountChart("orders", "سفارش‌ها بر پایه وضعیت", "donut", po, "/procurement/orders"));
            var t = new DashTableDto { Key = "overdue", Title = "اقلام دیرکرد", Columns = new List<string> { "سفارش", "شرح", "مانده", "تاریخ تحویل" }, Link = "/procurement/orders" };
            foreach (var r in overdue) { t.Rows.Add(new List<string> { r.Number, r.Description, Num(r.Quantity - r.ReceivedQuantity), Date(r.DeliveryDate) }); t.RowLinks.Add("/procurement/orders?number=" + r.Number); }
            d.Tables.Add(t);
            return d;
        }

        private async Task<DashboardDto> WarehouseAsync(CancellationToken ct)
        {
            var d = new DashboardDto();
            IQueryable<StockBalance> live = _uow.Set<StockBalance>().AsNoTracking().Where(b => b.Quantity != 0);
            int rows = await live.CountAsync(ct);
            int items = await live.Select(b => b.ItemId).Distinct().CountAsync(ct);
            int whCount = await live.Select(b => b.WarehouseId).Distinct().CountAsync(ct);
            int negative = await live.CountAsync(b => b.Quantity < 0, ct);
            d.Kpis.Add(Kpi("rows", "ردیف‌های موجودی", rows, null, "info", "/inventory/stock"));
            d.Kpis.Add(Kpi("items", "اقلام موجود", items, null, "info", "/inventory/stock"));
            d.Kpis.Add(Kpi("warehouses", "انبارهای دارای موجودی", whCount, null, "info", "/inventory/warehouses"));
            d.Kpis.Add(Kpi("negative", "موجودی منفی", negative, null, negative > 0 ? "bad" : "ok", "/inventory/stock"));
            var perWh = await live.GroupBy(b => b.WarehouseId).Select(g => new { Id = g.Key, Items = g.Select(x => x.ItemId).Distinct().Count() }).OrderBy(g => g.Id).Take(50).ToListAsync(ct);
            List<int> whIds = perWh.Select(g => g.Id).ToList();
            Dictionary<int, string> whNames = (await _uow.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.Id)).Select(w => new { w.Id, w.Title }).ToListAsync(ct)).ToDictionary(w => w.Id, w => w.Title);
            d.Charts.Add(Chart("per-warehouse", "تعداد اقلام در هر انبار", "bar", perWh.Select(g => whNames.TryGetValue(g.Id, out string n) ? n : "#" + g.Id), new[] { ("اقلام", perWh.Select(g => (decimal?)g.Items)) }, "/inventory/stock"));
            var top = await live.GroupBy(b => b.ItemId).Select(g => new { ItemId = g.Key, Q = g.Sum(x => x.Quantity) }).OrderByDescending(x => x.Q).Take(10).ToListAsync(ct);
            List<int> topIds = top.Select(x => x.ItemId).ToList();
            Dictionary<int, string> codes = (await _uow.Set<InventoryItem>().AsNoTracking().Where(i => topIds.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title }).ToListAsync(ct)).ToDictionary(i => i.Id, i => i.Code + " " + i.Title);
            var t = new DashTableDto { Key = "top", Title = "پرموجودی‌ترین اقلام", Columns = new List<string> { "کالا", "موجودی" }, Link = "/inventory/stock" };
            foreach (var x in top) { t.Rows.Add(new List<string> { codes.TryGetValue(x.ItemId, out string c) ? c : "#" + x.ItemId, Num(x.Q) }); t.RowLinks.Add("/inventory/stock?itemId=" + x.ItemId); }
            d.Tables.Add(t);
            return d;
        }

        private async Task<DashboardDto> QualityAsync(CancellationToken ct)
        {
            var d = new DashboardDto();
            IQueryable<NonConformance> all = _uow.Set<NonConformance>().AsNoTracking().Where(n => !n.Deleted);
            var byStatus = (await all.GroupBy(n => n.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)).ToList();
            var bySeverity = (await all.GroupBy(n => n.Severity).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)).ToList();
            int total = byStatus.Sum(x => x.C);
            int open = byStatus.Where(x => x.Key == NcrStatus.Open || x.Key == NcrStatus.Approved || x.Key == NcrStatus.Executed).Sum(x => x.C);
            d.Kpis.Add(Kpi("ncr-open", "NCR باز", open, null, open > 0 ? "warn" : "ok", "/inventory/ncr"));
            d.Kpis.Add(Kpi("ncr-total", "کل NCR", total, null, "info", "/inventory/ncr"));
            DateTime since = DateTime.Today.AddDays(-90);
            var qc = Counts((await _uow.Set<QcRecord>().AsNoTracking().Where(q => !q.Deleted && q.RecordDate >= since).GroupBy(q => q.Result).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)));
            d.Kpis.Add(Kpi("qc", "بازرسی‌های ۹۰ روز اخیر", qc.Values.Sum(), null, "info", "/quality/records"));
            d.Charts.Add(CountChart("ncr-status", "NCR بر پایه وضعیت", "bar", Counts(byStatus), "/inventory/ncr"));
            d.Charts.Add(CountChart("ncr-severity", "NCR بر پایه شدت", "donut", Counts(bySeverity), "/inventory/ncr"));
            d.Charts.Add(CountChart("qc-result", "نتیجه‌ی بازرسی‌ها", "donut", qc, "/quality/records"));
            var t = new DashTableDto { Key = "open-ncr", Title = "NCR های باز (جدیدترین)", Columns = new List<string> { "شماره", "تاریخ", "شدت", "وضعیت" }, Link = "/inventory/ncr" };
            var latest = await all.Where(n => n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled).OrderByDescending(n => n.ReportDate).Take(10).Select(n => new { n.Number, n.Status, n.Severity, n.ReportDate }).ToListAsync(ct);
            foreach (var n in latest) { t.Rows.Add(new List<string> { n.Number, Date(n.ReportDate), FaOf(n.Severity), FaOf(n.Status) }); t.RowLinks.Add("/inventory/ncr?number=" + n.Number); }
            d.Tables.Add(t);
            return d;
        }

        private async Task<DashboardDto> ProductionAsync(CancellationToken ct)
        {
            var d = new DashboardDto();
            DateTime today = DateTime.Today;
            IQueryable<MfProductionOrder> all = _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted);
            var byStatus = (await all.GroupBy(o => o.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)).ToList();
            IQueryable<MfProductionOrder> live = all.Where(o => o.Status == MfOrderStatus.Released || o.Status == MfOrderStatus.InProgress);
            IQueryable<MfProductionOrder> late = live.Where(o => o.PlannedFinish != null && o.PlannedFinish < today);
            int liveCount = await live.CountAsync(ct), lateCount = await late.CountAsync(ct);
            int hold = await _uow.Set<MfOperation>().CountAsync(o => o.Status == MfOperationStatus.QcHold || o.Status == MfOperationStatus.Rejected, ct);
            d.Kpis.Add(Kpi("live", "دستورهای در جریان", liveCount, null, "info", "/production/orders"));
            d.Kpis.Add(Kpi("late", "دستورهای دیرکرد", lateCount, null, lateCount > 0 ? "bad" : "ok", "/production/orders"));
            d.Kpis.Add(Kpi("qc-hold", "عملیات متوقف کیفی / ردشده", hold, null, hold > 0 ? "warn" : "ok", "/production/orders"));
            d.Charts.Add(CountChart("status", "دستورهای تولید بر پایه وضعیت", "bar", Counts(byStatus), "/production/orders"));
            var t = new DashTableDto { Key = "late", Title = "دستورهای دیرکرد", Columns = new List<string> { "شماره", "پایان برنامه", "وضعیت" }, Link = "/production/orders" };
            foreach (var o in await late.OrderBy(o => o.PlannedFinish).Take(10).Select(o => new { o.Number, o.Status, o.PlannedFinish }).ToListAsync(ct)) { t.Rows.Add(new List<string> { o.Number, Date(o.PlannedFinish), FaOf(o.Status) }); t.RowLinks.Add("/production/orders?number=" + o.Number); }
            d.Tables.Add(t);
            return d;
        }

        private async Task<DashboardDto> TasksAsync(CancellationToken ct)
        {
            var d = new DashboardDto();
            DateTime today = DateTime.Today;
            IQueryable<TmTask> all = _uow.Set<TmTask>().AsNoTracking().Where(t => !t.Deleted);
            IQueryable<TmTask> open = all.Where(t => t.Status != TmTaskStatus.Closed && t.Status != TmTaskStatus.Rejected);
            IQueryable<TmTask> late = open.Where(t => t.DueDate != null && t.DueDate < today);
            var byStatus = (await all.GroupBy(t => t.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct)).Select(x => (x.Key, x.C)).ToList();
            int openCount = await open.CountAsync(ct), lateCount = await late.CountAsync(ct);
            decimal? avg = openCount == 0 ? (decimal?)null : await open.AverageAsync(t => (decimal?)t.PercentComplete, ct);
            d.Kpis.Add(Kpi("open", "وظایف باز", openCount, null, "info", "/tasks"));
            d.Kpis.Add(Kpi("late", "وظایف دیرکرد", lateCount, null, lateCount > 0 ? "bad" : "ok", "/tasks"));
            d.Kpis.Add(Kpi("avg", "میانگین پیشرفت وظایف باز", avg.HasValue ? Math.Round(avg.Value, 1) : (decimal?)null, "٪", "info", "/tasks"));
            d.Charts.Add(CountChart("status", "وظایف بر پایه وضعیت", "bar", Counts(byStatus), "/tasks"));
            var t2 = new DashTableDto { Key = "late", Title = "وظایف دیرکرد", Columns = new List<string> { "شماره", "عنوان", "سررسید", "پیشرفت" }, Link = "/tasks" };
            foreach (var t in await late.OrderBy(t => t.DueDate).Take(10).Select(t => new { t.Number, t.Title, t.DueDate, t.PercentComplete }).ToListAsync(ct)) { t2.Rows.Add(new List<string> { t.Number, t.Title, Date(t.DueDate), Num(t.PercentComplete, 0) }); t2.RowLinks.Add("/tasks?number=" + t.Number); }
            d.Tables.Add(t2);
            return d;
        }

        private async Task<DashboardDto> FinanceAsync(CancellationToken ct)
        {
            var d = new DashboardDto();
            bool masked = !SeesPrices;
            CommitmentsForecastDto f = (await CommitmentsAsync(new CommitmentsQuery { Period = "week", Count = 8 }, ct)).Data ?? new CommitmentsForecastDto();
            d.Kpis.Add(Kpi("total", "تعهدات ۸ هفته‌ی آینده", f.Total, "ریال", "info", "/project-control/commitments", masked));
            d.Kpis.Add(Kpi("overdue", "تعهدات سررسیدگذشته", f.Overdue, "ریال", f.Overdue > 0 ? "bad" : "ok", "/project-control/commitments", masked));
            d.Kpis.Add(Kpi("orders", "سفارش‌های باز", f.OpenOrdersTotal, "ریال", "info", "/procurement/orders", masked));
            d.Kpis.Add(Kpi("statements", "صورت‌وضعیت‌های پرداخت‌نشده", f.StatementsTotal, "ریال", "info", "/procurement/statements", masked));
            d.Charts.Add(Chart("weekly", "پیش‌بینی تعهدات هفتگی", "bar", f.Buckets.Select(b => b.Label),
                new[] { ("سفارش‌ها", f.Buckets.Select(b => (decimal?)b.OpenOrders)), ("استعلام‌ها", f.Buckets.Select(b => (decimal?)b.Inquiries)), ("صورت‌وضعیت‌ها", f.Buckets.Select(b => (decimal?)b.Statements)) }, "/project-control/commitments", masked));
            return d;
        }

        private async Task<DashboardDto> CeoAsync(CancellationToken ct)
        {
            var d = new DashboardDto();
            foreach (var part in new[] { ("portfolio", EpmRoles.Portfolio, "سبد"), ("procurement", EpmRoles.Procurement, "تدارکات"), ("quality", EpmRoles.Quality, "کیفیت"), ("production", EpmRoles.Production, "تولید"), ("tasks", EpmRoles.Tasks, "وظایف"), ("finance", EpmRoles.Finance, "مالی") })
            {
                if (!_user.IsInRole(part.Item2)) continue;
                DashboardDto sub = part.Item1 == "portfolio" ? await PortfolioAsync(ct) : part.Item1 == "procurement" ? await ProcurementAsync(ct) : part.Item1 == "quality" ? await QualityAsync(ct) : part.Item1 == "production" ? await ProductionAsync(ct) : part.Item1 == "tasks" ? await TasksAsync(ct) : await FinanceAsync(ct);
                foreach (DashKpiDto k in sub.Kpis.Take(3)) { k.Title = part.Item3 + ": " + k.Title; k.Link ??= "/dashboards/" + part.Item1; d.Kpis.Add(k); }
                DashChartDto c = sub.Charts.FirstOrDefault();
                if (c != null) { c.Title = part.Item3 + ": " + c.Title; d.Charts.Add(c); }
            }
            return d;
        }
    }
}
