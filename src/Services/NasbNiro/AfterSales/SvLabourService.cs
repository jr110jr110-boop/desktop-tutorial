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
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Production;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.ServiceDtos;

namespace Services.NasbNiro.AfterSales
{
    public interface ISvLabourService
    {
        /// <summary>The month-end run of the service labour pool, by hand (finance manager, CEO): every ended month with pending orders is closed and its true-up posted.</summary>
        Task<ResponseDto<SvLabourAggregateResultDto>> RunAsync(SvJobsRunDto dto, CancellationToken ct);

        /// <summary>The same run for the nightly job (no user; the day is today): the first nights of a month close the month before. Returns the months closed.</summary>
        Task<int> RunNightlyAsync(CancellationToken ct);

        /// <summary>The months already closed, newest first (finance manager, CEO, finance, payroll): rebuilt from the pool rows, so every browser sees the same history.</summary>
        Task<ResponseDto<List<SvLabourPeriodDto>>> HistoryAsync(CancellationToken ct);
    }

    // ===========================================================================================================================================================================================================
    // LABOUR COST RULE OF AFTER-SALES (R1p D2) - POOLED AVERAGE RATE. It is the rule of production (see the top of MfLabourService) applied to the service department. ★ The company must confirm it.
    //
    //  1. Nothing that is published per service order is one person's pay. The labour of a closed order is hours x the pooled average rate of the SERVICE MONTH POOL, never hours x the engineer's own rate; it is spread
    //     over warranty, goodwill, LTSA and billable orders alike by their hours. (An engineer without an approved payslip is costed at the standard rate of the work centre named in the settings: not pay.)
    //  2. The pool of a Persian month = the payroll-rated engineers (an approved payslip exists) with hours on the orders closed in the month; pool average rate = pool cost / pool hours. The cost comes from payroll as before
    //     and only the pool total leaves the payroll boundary. The service department is one pool (it has no cost-centre split); with fewer than three payroll-rated engineers in the month nothing is published.
    //  3. At the close an order is absorbed at the rate of the last CLOSED service month; ★ before the first month is closed at the standard rate (the work centre of the settings). The order is marked with the month whose
    //     pool will true it up (the month of the close, or the first later month that is not closed).
    //  4. This MONTH-END run stores the pool (MfLabourPool, module Service: people, hours, cost, absorbed, the rate of the next closes) and posts the true-up, actual pool cost less absorbed, as ONE company-level line:
    //     no order, no contract. The line moves the difference between the service-labour-absorbed account and the labour-rate-variance account.
    //  5. CARRY: with fewer than three payroll-rated engineers in the month nothing is published: the orders move to the next open month and join its pool. ★ At the end of a Persian quarter the carry is posted anyway as one
    //     true-up line flagged LabourProtected (cost entry, event line, voucher line); the voucher, ledger and drill-down readers and the Sepidar events export leave it out for every role except PAYROLL,
    //     FINANCEMANAGER and CEO. A protected pool has no rate.
    //  6. The engineers named on an order are visible only to SERVICE, payroll readers (PAYROLL, FINANCEMANAGER, CEO) and the engineer himself; everybody else sees the hours without the name.
    // ===========================================================================================================================================================================================================

    /// <summary>The month-end run of the service labour pool and its true-up (see the rule above). Idempotent: an order is taken once, a month with nothing pending closes nothing.</summary>
    public class SvLabourService : ISvLabourService
    {
        public const string Runners = "FINANCEMANAGER,CEO";

        /// <summary>Who reads the closed months: the runners, finance and payroll (a protected amount stays hidden from all but the payroll readers).</summary>
        public const string Readers = Runners + ",FINANCIAL,PAYROLL";

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
            try
            {
                return ResponseDtoHelper.Ok(await PostAsync(asOf, ct));
            }
            catch (DbUpdateException)
            {
                // a second run at the same moment: the unique pool index lets only one of them close the month
                return SlText.Bad<SvLabourAggregateResultDto>(SlText.Concurrency);
            }
        }

        public async Task<int> RunNightlyAsync(CancellationToken ct) => (await PostAsync(DateTime.Today, ct)).Periods.Count;

        public async Task<ResponseDto<List<SvLabourPeriodDto>>> HistoryAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(Readers)) return SlText.Bad<List<SvLabourPeriodDto>>(SlText.NotAllowed);
            bool seesPayroll = SvRoles.SeesPayroll(_user.IsInRole(SvRoles.PayrollSeers));
            List<MfLabourPool> pools = await _uow.Set<MfLabourPool>().AsNoTracking().Where(p => p.Module == MfLabourModule.Service).OrderByDescending(p => p.Period).ToListAsync(ct);
            var rows = pools.Select(p => new SvLabourPeriodDto
            {
                Period = p.Period, PeriodText = SvLabourMonth.Text(p.Period), Orders = p.Items, People = p.People, Hours = p.Hours, Rate = p.Rate, Protected = p.Protected,
                Absorbed = p.Protected && !seesPayroll ? (decimal?)null : p.Absorbed, Variance = p.Protected && !seesPayroll ? (decimal?)null : p.Variance, EventId = p.AccountingEventId, PostedAt = p.PostedAt,
            }).ToList();
            return ResponseDtoHelper.Ok(rows);
        }

        /// <summary>The months whose pool is closed: the pool rows, and the months of the orders an earlier release aggregated (a closed month is never reopened).</summary>
        internal static async Task<HashSet<int>> ClosedMonthsAsync(IUnitOfWork uow, CancellationToken ct)
        {
            HashSet<int> closed = await MfLabourPoolStore.ClosedAsync(uow, MfLabourModule.Service, ct);
            foreach (int period in await uow.Set<SvServiceOrder>().AsNoTracking().Where(o => o.LabourPendingPeriod != null && o.LabourAggregatedAt != null).Select(o => o.LabourPendingPeriod.Value).Distinct().ToListAsync(ct))
                closed.Add(period);
            return closed;
        }

        private async Task<SvLabourAggregateResultDto> PostAsync(DateTime asOf, CancellationToken ct)
        {
            var result = new SvLabourAggregateResultDto { AsOf = asOf };
            int current = SvLabourMonth.Of(asOf);
            List<SvServiceOrder> orders = await _uow.Set<SvServiceOrder>().Include(o => o.TimeEntries)
                .Where(o => !o.Deleted && o.Status == SvOrderStatus.Closed && o.LabourPendingPeriod != null && o.LabourAggregatedAt == null && o.LabourPendingPeriod < current).ToListAsync(ct);
            if (orders.Count == 0) return result;

            await _costing.EnsureAccountRolesAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            string center = await SvSettings.EnsureCostCenterAsync(_uow, ct);
            HashSet<int> closed = await ClosedMonthsAsync(_uow, ct);
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
                List<SvTimeEntry> payroll = o.TimeEntries.Where(t => t.Hours > 0 && t.RateFromPayroll == true).ToList();
                return new MfPoolItem { Id = o.Id, Employees = payroll.Select(t => t.EmployeeId).ToHashSet(), Hours = payroll.Sum(t => t.Hours), Cost = o.LabourPayrollCost ?? 0, Absorbed = o.LabourPooledAmount ?? 0 };
            }

            var posted = new List<(SvLabourPeriodDto Dto, AccountingEvent Event)>();
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

                MfPoolPlan plan = MfLabourPooling.PlanSingle(month.OrderBy(o => o.Id).Select(Describe), period, center);
                if (plan.CarriedItemIds.Count > 0)
                {
                    HashSet<int> carried = plan.CarriedItemIds.ToHashSet();
                    int next = SvLabourMonth.NextOpen(period, closed);
                    foreach (SvServiceOrder o in month.Where(x => carried.Contains(x.Id))) { o.LabourPendingPeriod = next; o.ModifyDate = now; o.ModifierUserId = _user.UserId; Put(next, o); }
                    result.OrdersCarried += carried.Count;
                }
                if (plan.Pools.Count == 0) continue;

                DateTime end = SvLabourMonth.End(period);
                string number = SvLabourMonth.Number(period), text = SvLabourMonth.Text(period);
                MfPoolResult pool = plan.Pools.Single();
                var row = new MfLabourPool
                {
                    Module = MfLabourModule.Service, Period = period, PoolKey = center, People = pool.People, Hours = pool.Hours, Cost = pool.Cost, Absorbed = pool.Absorbed, Variance = pool.Variance,
                    Rate = pool.Rate, Protected = pool.Protected, Items = pool.ItemIds.Count, PostedAt = now, PostedByUserId = _user.UserId,
                };
                _uow.Set<MfLabourPool>().Add(row);

                // the one company-level line of the month: no order number, no contract
                AccountingEvent ev = null;
                if (pool.Variance != 0)
                {
                    _uow.Set<CostEntry>().Add(new CostEntry
                    {
                        EntryDate = end, Kind = CostKind.Labour, CostCenter = center, Quantity = 0, Amount = pool.Variance, SourceType = SvLabourMonth.SourceType, SourceId = period, SourceNumber = number,
                        LabourProtected = pool.Protected, Description = "تفاوت نرخ دستمزد خدمات پس از فروش (هزینه‌ی واقعی استخر منهای جذب‌شده) - ماه " + text, CreatedAt = now, CreatedByUserId = _user.UserId,
                    });
                    string description = "تفاوت نرخ دستمزد جذب‌شده در خدمات پس از فروش - ماه " + text;
                    List<AccountingEventLine> lines = MfLabourVariance.Lines(pool.Variance, AccountRoles.ServiceLabourAbsorbed, center, pool.Protected, description);
                    ev = _costing.AddEvent(AccountingEventType.LabourAbsorbed, end, SvLabourMonth.SourceType, period, number, "تفاوت نرخ دستمزد جذب‌شده خدمات پس از فروش (بستن ماه) - ماه " + text, lines);
                    row.AccountingEvent = ev;
                }

                // the orders of the pool take the month they are closed in (a carried order was pending in an earlier month)
                HashSet<int> done = pool.ItemIds.ToHashSet();
                foreach (SvServiceOrder o in month.Where(x => done.Contains(x.Id))) { o.LabourPendingPeriod = period; o.LabourAggregatedAt = now; o.ModifyDate = now; o.ModifierUserId = _user.UserId; }
                closed.Add(period);
                posted.Add((new SvLabourPeriodDto
                {
                    Period = period, PeriodText = text, Orders = done.Count, People = pool.People, Hours = pool.Hours, Rate = pool.Rate, Protected = pool.Protected, PostedAt = now,
                    Absorbed = pool.Protected && !seesPayroll ? (decimal?)null : pool.Absorbed, Variance = pool.Protected && !seesPayroll ? (decimal?)null : pool.Variance,
                }, ev));
            }
            await _uow.SaveChangesAsync(ct);
            foreach (var p in posted) { p.Dto.EventId = p.Event?.Id; result.Periods.Add(p.Dto); }
            return result;
        }
    }
}
