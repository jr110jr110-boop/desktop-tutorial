using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.ServiceDtos;

namespace Services.NasbNiro.AfterSales
{
    public interface ISvLabourService
    {
        /// <summary>
        /// The month-end run of the service labour variance, by hand (finance manager, CEO): every ended month with pending orders is closed and its variance (actual payroll cost of the payroll-rated engineers less what the
        /// orders absorbed at the standard rate) posted as one company-level line when the month passes the publication gate, else carried (or flagged at the quarter end). Refused while orders closed by the earlier
        /// release still wait for its monthly posting (see <see cref="SvLabourService.LegacyWaitingAsync"/>).
        /// </summary>
        Task<ResponseDto<SvLabourAggregateResultDto>> RunAsync(SvJobsRunDto dto, CancellationToken ct);

        /// <summary>The same run for the nightly job (no user; the day is today): the first nights of a month close the month before. Returns the months closed. Skips (and logs) while orders of the earlier release wait.</summary>
        Task<int> RunNightlyAsync(CancellationToken ct);

        /// <summary>
        /// The months, newest first (finance manager, CEO, finance, payroll). Every reader gets the state of each month and what the orders absorbed at the standard rate; the pool (people, hours, actual average rate, cost)
        /// and the variance only go to PAYROLL, FINANCEMANAGER and CEO.
        /// </summary>
        Task<ResponseDto<List<SvLabourPeriodDto>>> HistoryAsync(CancellationToken ct);
    }

    // ===========================================================================================================================================================================================================
    // LABOUR COST RULE OF AFTER-SALES (R1p D2, round 3) - STANDARD RATE ABSORPTION, ONE GATED VARIANCE A MONTH. It is the rule of production (see the top of MfLabourService) applied to the service department.
    // ★ The company must confirm the starred decisions (see the report of R1p).
    //
    //  1. ABSORPTION: the labour of a closed order is hours x a STANDARD rate that finance sets and that is not derived from anybody's pay: the hourly rate of the after-sales labour work centre named in the settings
    //     (MfWorkCenter.LabourRateRial, maintained per Persian year), else Production:StandardLabourRate ★; closing an order that has hours is refused, naming the centre, when neither exists. The same rate applies to an
    //     engineer with a payslip and to one without, so nothing published per order, contract or day is one person's pay.
    //  2. VARIANCE: the month-end run posts, once per month at company level (no order, no contract; the after-sales cost centre only), the actual payroll cost of the payroll-rated engineers (an approved payslip exists)
    //     who have hours on the orders closed in the month, less what those hours absorbed at the standard rate. The pool is stored as one MfLabourPool row per module and month (module Service, PoolKey "*").
    //     The payroll cost is the employer cost of the engineer's last approved payslip over the paid hours (★ not the payroll of the month of the order).
    //  3. PUBLICATION GATE ★: the line is an ordinary line only when the month's pool has at least Production:PoolMinPeople (default 5) payroll-rated engineers with hours AND nobody dominates it: the largest person has
    //     at most Production:PoolMaxShare (default 0.50) of the hours and the two largest together at most Production:PoolMaxTopTwoShare (default 0.80).
    //  4. CARRY: a month that does not pass the gate is not published: its orders move to the next open month and join its pool (a closed month is never reopened). ★ At the end of a Persian quarter the accumulated
    //     variance is posted anyway as ONE company line flagged LabourProtected (cost entry, event line, voucher line); the readers of vouchers, ledgers, statements, budget, consolidation, drill-downs and both Sepidar
    //     exports leave it out (or refuse) for every role except PAYROLL, FINANCEMANAGER and CEO.
    //  5. WHO SEES WHAT: the pool (people, hours, actual average rate, cost) and the variance are returned only to PAYROLL, FINANCEMANAGER and CEO; every other reader gets the month, its state (posted, protected,
    //     pending) and the absorbed total. The engineers named on an order are visible only to SERVICE, the payroll readers and the engineer himself; everybody else sees the hours without the name.
    // ===========================================================================================================================================================================================================

    /// <summary>The month-end run of the service labour variance (see the rule above). Idempotent: an order is taken once, a month with nothing pending closes nothing.</summary>
    public class SvLabourService : ISvLabourService
    {
        public const string Runners = "FINANCEMANAGER,CEO";

        /// <summary>Who reads the closed months: the runners, finance and payroll (the pool and the variance stay hidden from all but the payroll readers).</summary>
        public const string Readers = Runners + ",FINANCIAL,PAYROLL";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SvLabourService> _logger;

        public SvLabourService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing, IConfiguration configuration = null, ILogger<SvLabourService> logger = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ResponseDto<SvLabourAggregateResultDto>> RunAsync(SvJobsRunDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(Runners)) return SlText.Bad<SvLabourAggregateResultDto>(SlText.NotAllowed);
            DateTime asOf = (dto?.AsOf ?? DateTime.Today).Date;
            bool allowFuture = string.Equals(_configuration?[SvJobsGuard.AllowFutureKey], "true", StringComparison.OrdinalIgnoreCase);
            string refusal = SvJobsGuard.Refusal(asOf, DateTime.Today, allowFuture);
            if (refusal != null) return SlText.Bad<SvLabourAggregateResultDto>(refusal);
            try
            {
                // R1p H4: orders of the earlier release that still wait for its monthly posting are not taken into the new variance (their labour was held back at the engineers' own pay); the data step must convert them first
                List<(string Number, int Period)> waiting = await LegacyWaitingAsync(asOf, ct);
                if (waiting.Count > 0) return SlText.Bad<SvLabourAggregateResultDto>(LegacyRefusal(waiting));
                return ResponseDtoHelper.Ok(await PostAsync(asOf, ct));
            }
            catch (DbUpdateException)
            {
                // a second run at the same moment: the unique pool index lets only one of them close the month
                return SlText.Bad<SvLabourAggregateResultDto>(SlText.Concurrency);
            }
        }

        public async Task<int> RunNightlyAsync(CancellationToken ct)
        {
            try
            {
                List<(string Number, int Period)> waiting = await LegacyWaitingAsync(DateTime.Today, ct);
                if (waiting.Count > 0)
                {
                    _logger?.LogWarning("Service labour month skipped: orders closed by the earlier release wait for its monthly posting ({Orders}); run the ProductionR1 data step first.",
                        string.Join(", ", waiting.Select(w => $"{w.Number} ({SvLabourMonth.Text(w.Period)})")));
                    return 0;
                }
                return (await PostAsync(DateTime.Today, ct)).Periods.Count(p => p.State != LabourStates.Pending);
            }
            catch (DbUpdateException ex)
            {
                // a manual run closed the month at the same moment (unique pool index): nothing is lost, the next night finds the month closed
                _logger?.LogInformation(ex, "Service labour month: a concurrent run closed the month first; nothing to do.");
                return 0;
            }
        }

        public async Task<ResponseDto<List<SvLabourPeriodDto>>> HistoryAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(Readers)) return SlText.Bad<List<SvLabourPeriodDto>>(SlText.NotAllowed);
            bool seesPayroll = SvRoles.SeesPayroll(_user.IsInRole(SvRoles.PayrollSeers));
            List<MfLabourPool> pools = await _uow.Set<MfLabourPool>().AsNoTracking().Where(p => p.Module == MfLabourModule.Service).ToListAsync(ct);
            var rows = pools.Select(p => SvLabourStates.Dto(p, seesPayroll)).ToList();

            // the months that wait: closed orders with payroll-rated hours not yet taken into a pool (the month has not ended, or the gate was not passed)
            var waiting = await _uow.Set<SvServiceOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.Status == SvOrderStatus.Closed && o.LabourPendingPeriod != null && o.LabourAggregatedAt == null && o.LabourPayrollCost != null)
                .GroupBy(o => o.LabourPendingPeriod.Value).Select(g => new { Period = g.Key, Count = g.Count(), Absorbed = g.Sum(x => x.LabourPooledAmount ?? 0m) }).ToListAsync(ct);
            foreach (var w in waiting.Where(w => rows.All(r => r.Period != w.Period)))
                rows.Add(SvLabourStates.PendingDto(w.Period, w.Count, w.Absorbed));
            return ResponseDtoHelper.Ok(rows.OrderByDescending(r => r.Period).ToList());
        }

        /// <summary>The months whose pool is closed: the pool rows, and the months of the orders an earlier release aggregated (a closed month is never reopened).</summary>
        internal static async Task<HashSet<int>> ClosedMonthsAsync(IUnitOfWork uow, CancellationToken ct)
        {
            HashSet<int> closed = await MfLabourPoolStore.ClosedAsync(uow, MfLabourModule.Service, ct);
            foreach (int period in await uow.Set<SvServiceOrder>().AsNoTracking().Where(o => o.LabourPendingPeriod != null && o.LabourAggregatedAt != null).Select(o => o.LabourPendingPeriod.Value).Distinct().ToListAsync(ct))
                closed.Add(period);
            return closed;
        }

        /// <summary>
        /// R1p H4: the closed orders of the earlier release that still wait for its monthly posting and whose month the run would take (<c>LabourPayrollCost</c> is empty: the earlier close stored only the labour at
        /// the engineers' own pay and held it back). An order of this release with payroll-rated hours always has a payroll cost. The data step (docs/production-r1-backfill.md, step 4) gives each of them one.
        /// </summary>
        internal async Task<List<(string Number, int Period)>> LegacyWaitingAsync(DateTime asOf, CancellationToken ct)
        {
            int current = SvLabourMonth.Of(asOf);
            var rows = await _uow.Set<SvServiceOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.Status == SvOrderStatus.Closed && o.LabourPendingPeriod != null && o.LabourAggregatedAt == null && o.LabourPayrollCost == null && o.LabourPendingPeriod < current)
                .OrderBy(o => o.LabourPendingPeriod).ThenBy(o => o.Id).Select(o => new { o.Number, Period = o.LabourPendingPeriod.Value }).ToListAsync(ct);
            return rows.Select(r => (r.Number, r.Period)).ToList();
        }

        /// <summary>The Persian refusal that names the waiting orders and their months (at most ten orders are named).</summary>
        internal static string LegacyRefusal(List<(string Number, int Period)> waiting)
        {
            string named = string.Join("، ", waiting.Take(10).Select(w => $"{w.Number} (ماه {SvLabourMonth.Text(w.Period)})"));
            string more = waiting.Count > 10 ? $" و {SlText.Number(waiting.Count - 10)} دستور دیگر" : string.Empty;
            return $"بستن ماه ممکن نیست: {SlText.Number(waiting.Count)} دستور خدمات با قاعده‌ی نسخه‌ی قبل بسته شده‌اند و دستمزدشان هنوز در انتظار ثبت ماهانه‌ی قدیمی است ({named}{more}). "
                + "ابتدا مرحله‌ی تبدیل این دستورها را در راهنمای انتقال داده‌ی نسخه اجرا کنید (مرحله‌ی ۴)؛ سپس ماه بسته می‌شود.";
        }

        private async Task<SvLabourAggregateResultDto> PostAsync(DateTime asOf, CancellationToken ct)
        {
            var result = new SvLabourAggregateResultDto { AsOf = asOf };
            int current = SvLabourMonth.Of(asOf);
            List<SvServiceOrder> orders = await _uow.Set<SvServiceOrder>().Include(o => o.TimeEntries)
                .Where(o => !o.Deleted && o.Status == SvOrderStatus.Closed && o.LabourPendingPeriod != null && o.LabourAggregatedAt == null && o.LabourPayrollCost != null && o.LabourPendingPeriod < current).ToListAsync(ct);
            if (orders.Count == 0)
            {
                result.Message = "دستور خدمات منتظر بستن ماه (تفاوت دستمزد با نرخ استاندارد) که ماه آن تمام شده باشد وجود ندارد.";
                return result;
            }

            await _costing.EnsureAccountRolesAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            string center = await SvSettings.EnsureCostCenterAsync(_uow, ct);
            HashSet<int> closed = await ClosedMonthsAsync(_uow, ct);
            MfLabourGateSettings gate = MfLabourGateSettings.Read(_configuration);
            bool seesPayroll = SvRoles.SeesPayroll(_user.IsInRole(SvRoles.PayrollSeers));

            // the orders by the month whose pool takes them, oldest first; a carried month joins the next one, so the loop runs over those too as long as their month has ended
            var months = new SortedDictionary<int, List<SvServiceOrder>>();
            void Put(int period, SvServiceOrder o)
            {
                if (!months.TryGetValue(period, out List<SvServiceOrder> list)) months[period] = list = new List<SvServiceOrder>();
                list.Add(o);
            }
            foreach (SvServiceOrder o in orders) Put(o.LabourPendingPeriod.Value, o);

            MfPoolItem Describe(SvServiceOrder o)
            {
                var item = new MfPoolItem { Id = o.Id, Cost = o.LabourPayrollCost ?? 0, Absorbed = o.LabourPooledAmount ?? 0 };
                foreach (var engineer in o.TimeEntries.Where(t => t.Hours > 0 && t.RateFromPayroll == true).GroupBy(t => t.EmployeeId))
                    item.PersonHours[engineer.Key] = engineer.Sum(t => t.Hours);
                return item;
            }

            var posted = new List<MfLabourPool>();
            while (months.Count > 0 && months.Keys.First() < current)
            {
                int period = months.Keys.First();
                List<SvServiceOrder> month = months[period];
                months.Remove(period);

                // a closed month is never reopened (cannot happen through the close; this keeps the unique pool index safe)
                if (closed.Contains(period))
                {
                    int open = SvLabourMonth.NextOpen(period, closed);
                    foreach (SvServiceOrder o in month) { o.LabourPendingPeriod = open; o.ModifyDate = now; o.ModifierUserId = _user.UserId; Put(open, o); }
                    continue;
                }

                MfPoolPlan plan = MfLabourPooling.Plan(month.OrderBy(o => o.Id).Select(Describe), period, gate);

                // the gate is not passed (too few payroll-rated engineers, or one or two of them dominate the hours): nothing is published, the orders wait in the next open month and join its pool
                if (plan.CarriedItemIds.Count > 0)
                {
                    HashSet<int> carried = plan.CarriedItemIds.ToHashSet();
                    int next = SvLabourMonth.NextOpen(period, closed);
                    foreach (SvServiceOrder o in month.Where(x => carried.Contains(x.Id))) { o.LabourPendingPeriod = next; o.ModifyDate = now; o.ModifierUserId = _user.UserId; Put(next, o); }
                    result.OrdersCarried += carried.Count;
                }
                if (plan.Pool == null) continue;
                MfPoolResult pool = plan.Pool;

                DateTime end = SvLabourMonth.End(period);
                string number = SvLabourMonth.Number(period), text = SvLabourMonth.Text(period);
                var row = new MfLabourPool
                {
                    Module = MfLabourModule.Service, Period = period, PoolKey = pool.Key, People = pool.People, Hours = pool.Hours, Cost = pool.Cost, Absorbed = pool.Absorbed, Variance = pool.Variance,
                    Rate = pool.Rate, Protected = pool.Protected, Items = pool.ItemIds.Count, PostedAt = now, PostedByUserId = _user.UserId,
                };
                _uow.Set<MfLabourPool>().Add(row);

                // the one company-level line of the month: no order number, no contract
                if (pool.Variance != 0)
                {
                    _uow.Set<CostEntry>().Add(new CostEntry
                    {
                        EntryDate = end, Kind = CostKind.Labour, CostCenter = center, Quantity = 0, Amount = pool.Variance, SourceType = SvLabourMonth.SourceType, SourceId = period, SourceNumber = number,
                        LabourProtected = pool.Protected, Description = "تفاوت دستمزد خدمات پس از فروش (هزینه‌ی واقعی حقوق منهای جذب‌شده با نرخ استاندارد) - ماه " + text, CreatedAt = now, CreatedByUserId = _user.UserId,
                    });
                    string description = "تفاوت دستمزد جذب‌شده در خدمات پس از فروش با نرخ استاندارد - ماه " + text;
                    List<AccountingEventLine> lines = MfLabourVariance.Lines(pool.Variance, AccountRoles.ServiceLabourAbsorbed, center, pool.Protected, description);
                    row.AccountingEvent = _costing.AddEvent(AccountingEventType.LabourAbsorbed, end, SvLabourMonth.SourceType, period, number, "تفاوت دستمزد جذب‌شده خدمات پس از فروش با نرخ استاندارد (بستن ماه) - ماه " + text, lines);
                }

                // the orders of the pool take the month they are closed in (a carried order was pending in an earlier month)
                HashSet<int> done = pool.ItemIds.ToHashSet();
                foreach (SvServiceOrder o in month.Where(x => done.Contains(x.Id))) { o.LabourPendingPeriod = period; o.LabourAggregatedAt = now; o.ModifyDate = now; o.ModifierUserId = _user.UserId; }
                closed.Add(period);
                posted.Add(row);
            }
            await _uow.SaveChangesAsync(ct);
            foreach (MfLabourPool row in posted) result.Periods.Add(SvLabourStates.Dto(row, seesPayroll));
            result.Message = result.Periods.Count > 0
                ? $"تفاوت دستمزد {SlText.Number(result.Periods.Count)} ماه بسته شد." + (result.OrdersCarried > 0 ? $" {SlText.Number(result.OrdersCarried)} دستور به ماه بعد منتقل شد (تعداد مهندسان دارای فیش حقوقی کم است یا ساعت یک یا دو نفر غالب است)." : string.Empty)
                : result.OrdersCarried > 0 ? $"{SlText.Number(result.OrdersCarried)} دستور به ماه بعد منتقل شد؛ پایان فصل با علامت محرمانه بسته می‌شود." : "چیزی برای بستن نبود.";
            return result;
        }
    }

    /// <summary>The role-dependent shape of a service labour month (see <see cref="SvLabourPeriodDto"/>): the state and the absorbed total for every reader, the pool and the variance for the payroll readers only.</summary>
    internal static class SvLabourStates
    {
        public static SvLabourPeriodDto Dto(MfLabourPool p, bool seesPayroll)
        {
            string state = p.Protected ? LabourStates.Protected : LabourStates.Posted;
            var dto = new SvLabourPeriodDto
            {
                Period = p.Period, PeriodText = SvLabourMonth.Text(p.Period), State = state, StateText = LabourStates.Text(state), Orders = p.Items, Absorbed = p.Absorbed, Protected = p.Protected,
                EventId = p.AccountingEventId, PostedAt = p.PostedAt,
            };
            if (seesPayroll)
            {
                dto.People = p.People;
                dto.Hours = p.Hours;
                dto.Rate = p.Rate;
                dto.Cost = p.Cost;
                dto.Variance = p.Variance;
            }
            return dto;
        }

        /// <summary>A month that waits: closed orders with payroll-rated hours that are not in a pool yet. Nothing of payroll in it.</summary>
        public static SvLabourPeriodDto PendingDto(int period, int orders, decimal absorbed) =>
            new SvLabourPeriodDto { Period = period, PeriodText = SvLabourMonth.Text(period), State = LabourStates.Pending, StateText = LabourStates.Text(LabourStates.Pending), Orders = orders, Absorbed = absorbed };
    }
}
