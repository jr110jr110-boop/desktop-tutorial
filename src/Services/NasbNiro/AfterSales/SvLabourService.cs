using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.ServiceDtos;

namespace Services.NasbNiro.AfterSales
{
    public interface ISvLabourService
    {
        /// <summary>The monthly aggregate posting of the protected labour, by hand (finance manager, CEO): every ended month with pending orders gets its one accounting event.</summary>
        Task<ResponseDto<SvLabourAggregateResultDto>> RunAsync(SvJobsRunDto dto, CancellationToken ct);

        /// <summary>The same run for the nightly job (no user; the day is today): the first nights of a month post the month before. Returns the months posted.</summary>
        Task<int> RunNightlyAsync(CancellationToken ct);

        /// <summary>The months already posted, newest first (finance manager, CEO): rebuilt from the aggregated orders, the cost entries and the accounting events, so every browser sees the same history.</summary>
        Task<ResponseDto<List<SvLabourPeriodDto>>> HistoryAsync(CancellationToken ct);
    }

    /// <summary>The monthly aggregate posting of protected labour (see <see cref="SvLabourMonth"/>). Idempotent: an order is taken once; a month with nothing pending posts nothing.</summary>
    public class SvLabourService : ISvLabourService
    {
        public const string Runners = "FINANCEMANAGER,CEO";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;
        private readonly IConfiguration _configuration;

        public SvLabourService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing, IConfiguration configuration = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
            _configuration = configuration;
        }

        public async Task<ResponseDto<SvLabourAggregateResultDto>> RunAsync(SvJobsRunDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(Runners)) return SlText.Bad<SvLabourAggregateResultDto>(SlText.NotAllowed);
            DateTime asOf = (dto?.AsOf ?? DateTime.Today).Date;
            bool allowFuture = string.Equals(_configuration?[SvJobsGuard.AllowFutureKey], "true", StringComparison.OrdinalIgnoreCase);
            string refusal = SvJobsGuard.Refusal(asOf, DateTime.Today, allowFuture);
            if (refusal != null) return SlText.Bad<SvLabourAggregateResultDto>(refusal);
            return ResponseDtoHelper.Ok(await PostAsync(asOf, ct));
        }

        public async Task<int> RunNightlyAsync(CancellationToken ct) => (await PostAsync(DateTime.Today, ct)).Periods.Count;

        /// <summary>One month of the monthly labour postings in the books: the amount and how many of its entries are protected (a plain integer sum; the flag is derived in memory).</summary>
        internal sealed class CostMonthRow
        {
            public int Period { get; set; }
            public decimal Amount { get; set; }
            public int ProtectedEntries { get; set; }
        }

        internal static IQueryable<CostMonthRow> CostByPeriod(IQueryable<CostEntry> entries) =>
            entries.Where(c => c.SourceType == SvLabourMonth.SourceType).GroupBy(c => c.SourceId)
                .Select(g => new CostMonthRow { Period = g.Key, Amount = g.Sum(c => c.Amount), ProtectedEntries = g.Sum(c => c.LabourProtected ? 1 : 0) });

        public async Task<ResponseDto<List<SvLabourPeriodDto>>> HistoryAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(Runners)) return SlText.Bad<List<SvLabourPeriodDto>>(SlText.NotAllowed);
            var orders = await _uow.Set<SvServiceOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.LabourAggregatedAt != null && o.LabourPendingPeriod != null)
                .Select(o => new { Period = o.LabourPendingPeriod.Value, o.Decision, o.LabourAmount, o.LabourAggregatedAt }).ToListAsync(ct);
            var costs = (await CostByPeriod(_uow.Set<CostEntry>().AsNoTracking()).ToListAsync(ct))
                .Select(c => new { c.Period, c.Amount, Flagged = c.ProtectedEntries > 0 }).ToList();
            var events = await _uow.Set<AccountingEvent>().AsNoTracking().Where(e => e.SourceType == SvLabourMonth.SourceType && e.ReversesEventId == null)
                .Select(e => new { e.Id, e.SourceId }).ToListAsync(ct);
            var rows = orders.GroupBy(o => o.Period).OrderByDescending(g => g.Key).Select(g => new SvLabourPeriodDto
            {
                Period = g.Key, PeriodText = SvLabourMonth.Text(g.Key), Orders = g.Count(),
                EventAmount = g.Where(o => o.Decision == SvWarrantyDecision.Warranty || o.Decision == SvWarrantyDecision.Goodwill).Sum(o => o.LabourAmount),
                CostAmount = costs.Where(c => c.Period == g.Key).Sum(c => c.Amount), Protected = costs.Any(c => c.Period == g.Key && c.Flagged),
                EventId = events.Where(e => e.SourceId == g.Key).OrderByDescending(e => e.Id).Select(e => (int?)e.Id).FirstOrDefault(),
                PostedAt = g.Max(o => o.LabourAggregatedAt),
            }).ToList();
            return ResponseDtoHelper.Ok(rows);
        }

        private async Task<SvLabourAggregateResultDto> PostAsync(DateTime asOf, CancellationToken ct)
        {
            var result = new SvLabourAggregateResultDto { AsOf = asOf };
            int current = SvLabourMonth.Of(asOf);
            List<SvServiceOrder> orders = await _uow.Set<SvServiceOrder>().Include(o => o.TimeEntries)
                .Where(o => !o.Deleted && o.Status == SvOrderStatus.Closed && o.LabourPendingPeriod != null && o.LabourAggregatedAt == null && o.LabourPendingPeriod < current).ToListAsync(ct);
            if (orders.Count == 0) return result;

            DateTimeOffset now = DateTimeOffset.Now;
            string center = await SvSettings.EnsureCostCenterAsync(_uow, ct);
            HashSet<int> postedPeriods = (await _uow.Set<SvServiceOrder>().AsNoTracking().Where(o => o.LabourPendingPeriod != null && o.LabourAggregatedAt != null).Select(o => o.LabourPendingPeriod.Value).Distinct().ToListAsync(ct)).ToHashSet();

            // The pools by month, oldest first. R1p D2: a pool that is itself protected (fewer than three payroll-rated employees behind the month total) is carried to the next month and joins that pool;
            // so the loop runs over the carried pools too, as long as their month has ended.
            var pools = new SortedDictionary<int, List<SvServiceOrder>>();
            foreach (SvServiceOrder o in orders)
            {
                if (!pools.TryGetValue(o.LabourPendingPeriod.Value, out List<SvServiceOrder> list)) pools[o.LabourPendingPeriod.Value] = list = new List<SvServiceOrder>();
                list.Add(o);
            }

            var posted = new List<(SvLabourPeriodDto Dto, AccountingEvent Event)>();
            while (pools.Count > 0 && pools.Keys.First() < current)
            {
                int period = pools.Keys.First();
                List<SvServiceOrder> month = pools[period];
                pools.Remove(period);
                List<SvPendingLabour> pending = month.OrderBy(o => o.Id).Select(o => new SvPendingLabour
                {
                    OrderId = o.Id, Decision = o.Decision, ContractId = o.ContractId, CostCenter = o.CostCenterCode ?? center, Labour = o.LabourAmount,
                    Employees = o.TimeEntries.Where(t => t.Hours > 0).Select(t => t.EmployeeId).ToHashSet(),
                    PayrollEmployees = o.TimeEntries.Where(t => t.Hours > 0 && t.RateFromPayroll == true).Select(t => t.EmployeeId).ToHashSet(),
                }).ToList();

                Production.MfLabourMonthAction action = SvLabourMonth.Decide(pending, period);
                if (action == Production.MfLabourMonthAction.Carry)
                {
                    int next = SvLabourMonth.NextOpen(period, postedPeriods);
                    foreach (SvServiceOrder o in month) { o.LabourPendingPeriod = next; o.ModifyDate = now; o.ModifierUserId = _user.UserId; }
                    if (!pools.TryGetValue(next, out List<SvServiceOrder> target)) pools[next] = target = new List<SvServiceOrder>();
                    target.AddRange(month);
                    result.OrdersCarried += month.Count;
                    continue;
                }
                bool flagged = action == Production.MfLabourMonthAction.PostProtected; // the quarter ended: posted, but only payroll, the finance manager and the CEO read the amount
                DateTime end = SvLabourMonth.End(period);

                // the one accounting event of the month: no order number, no per-order line
                AccountingEvent ev = null;
                var lines = SvLabourMonth.EventLines(pending, period)
                    .Select(l => new AccountingEventLine { AccountRole = l.Role, Debit = l.Debit, Credit = l.Credit, CostCenter = center, Description = l.Description, LabourProtected = flagged }).ToList();
                if (lines.Count > 0)
                    ev = _costing.AddEvent(AccountingEventType.WarrantyCost, end, SvLabourMonth.SourceType, period, SvLabourMonth.Number(period), "دستمزد جذب‌شده در خدمات پس از فروش - ماه " + SvLabourMonth.Text(period), lines);

                // the labour cost entries of the month: one per contract (small shares without a contract), the hours stay on the orders
                var groups = SvLabourMonth.CostGroups(pending);
                foreach (var g in groups)
                    _uow.Set<CostEntry>().Add(new CostEntry
                    {
                        EntryDate = end, Kind = CostKind.Labour, ContractId = g.ContractId, CostCenter = g.CostCenter, Quantity = 0, Amount = g.Amount,
                        SourceType = SvLabourMonth.SourceType, SourceId = period, SourceNumber = SvLabourMonth.Number(period), LabourProtected = flagged,
                        Description = "دستمزد ماهانه خدمات پس از فروش - ماه " + SvLabourMonth.Text(period), CreatedAt = now, CreatedByUserId = _user.UserId,
                    });

                // the orders of the pool take the month they are posted in (a carried order was pending in an earlier month)
                foreach (SvServiceOrder o in month) { o.LabourPendingPeriod = period; o.LabourAggregatedAt = now; o.ModifyDate = now; o.ModifierUserId = _user.UserId; }
                postedPeriods.Add(period);
                posted.Add((new SvLabourPeriodDto
                {
                    Period = period, PeriodText = SvLabourMonth.Text(period), Orders = month.Count, PostedAt = now, CostAmount = groups.Sum(g => g.Amount), Protected = flagged,
                    EventAmount = pending.Where(p => p.Decision == SvWarrantyDecision.Warranty || p.Decision == SvWarrantyDecision.Goodwill).Sum(p => p.Labour),
                }, ev));
            }
            await _uow.SaveChangesAsync(ct);
            foreach (var p in posted) { p.Dto.EventId = p.Event?.Id; result.Periods.Add(p.Dto); }
            return result;
        }
    }
}
