using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.AfterSales;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production
{
    public interface IMfLabourService
    {
        /// <summary>
        /// The month-end run of the labour variance, by hand (finance manager, finance, production manager): every ended month that still has bookings of payroll-rated people is closed, i.e. its pool is stored and the
        /// variance (actual payroll cost less what the days absorbed at the standard rates) is posted as ONE company-level line when the month passes the publication gate, else carried (or flagged at the quarter end).
        /// </summary>
        Task<ResponseDto<MfLabourMonthResultDto>> RunAsync(MfLabourMonthRunDto dto, CancellationToken ct);

        /// <summary>The same run for the nightly job (no user; the day is today): the first nights of a month close the month before. Returns the months closed.</summary>
        Task<int> RunNightlyAsync(CancellationToken ct);

        /// <summary>
        /// The months, newest first. Every reader of the screen gets the state of each month (posted, protected or pending) and the amount the days absorbed at the standard rates; the pool (people, hours, actual average rate,
        /// cost) and the variance only go to PAYROLL, FINANCEMANAGER and CEO.
        /// </summary>
        Task<ResponseDto<List<MfLabourPeriodDto>>> HistoryAsync(CancellationToken ct);
    }

    // ===========================================================================================================================================================================================================
    // LABOUR COST RULE OF PRODUCTION (R1p D1) - STANDARD RATE ABSORPTION, ONE GATED VARIANCE A MONTH. ★ The company must confirm the starred decisions (see the report of R1p).
    //
    //  1. ABSORPTION: labour is absorbed at a STANDARD RATE that finance sets and that is not derived from anybody's pay: booked hours x the standard rate of the work centre (MfWorkCenter.LabourRateRial, maintained per Persian
    //     year by finance), else the setting Production:StandardLabourRate ★; when neither exists the cost run refuses and names the work centre. The same rate applies to a person with a payslip and to one without, so
    //     nothing posted per work order, WBS node, work centre or day is one person's pay and the daily postings are deterministic. The AFTER-SALES module does the same with the standard rate of its labour work centre.
    //  2. VARIANCE: the month-end run (a month is closed when it has ended) posts, per MODULE and month, the variance = the actual payroll cost of the payroll-rated people (an approved payslip exists) who have hours in the
    //     month, less what was absorbed for their hours at the standard rate. ONE company-level line pair (labour rate variance 70114 / absorbed labour): no work order, no WBS node, no day, no cost centre. The pool is stored
    //     as one MfLabourPool row per module and month (PoolKey "*"). The payroll cost is the employer cost of the person's last approved payslip over the paid hours (★ not the payroll of the booking's month).
    //  3. PUBLICATION GATE ★: the line is an ordinary line only when the month's pool has at least Production:PoolMinPeople (default 5) payroll-rated people with hours AND nobody dominates it: the largest person has at most
    //     Production:PoolMaxShare (default 0.50) of the hours and the two largest together at most Production:PoolMaxTopTwoShare (default 0.80). A pool that is too small, or dominated by one or two people, would let a reader
    //     divide the variance back into a person's pay.
    //  4. CARRY: a month that does not pass the gate is not published: its bookings move to the next open month and join its pool (a closed month is never reopened). ★ At the end of a Persian quarter (Khordad, Shahrivar,
    //     Azar, Esfand) the accumulated variance is posted anyway as ONE company line flagged LabourProtected; the flag goes to the cost entry, the event line and the voucher line, and the readers of vouchers, ledgers,
    //     statements, budget, consolidation, drill-downs and both Sepidar exports leave it out (or refuse) for every role except PAYROLL, FINANCEMANAGER and CEO.
    //  5. WHO SEES WHAT: the pool (people, hours, actual average rate, cost, variance) is returned only to PAYROLL, FINANCEMANAGER and CEO; every other reader of the screen gets the month, its state (posted, protected,
    //     pending) and the absorbed total, which is hours x a standard. Named hours stay visible to the production roles: hours are not pay and the rate is a standard.
    //  RESIDUAL RISK (accepted, ★): the variance of consecutive months that pass the gate with nearly the same people could still be differenced by somebody who knows the hours and the pay of all the others but one.
    // ===========================================================================================================================================================================================================

    /// <summary>
    /// The month-end run of the production labour variance (see the rule above). Idempotent: a booking is taken once, a month with nothing pending closes nothing, and a pool row exists once per module and month
    /// (unique index).
    /// </summary>
    public class MfLabourService : IMfLabourService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MfLabourService> _logger;

        public MfLabourService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing, IConfiguration configuration = null, ILogger<MfLabourService> logger = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
            _configuration = configuration;
            _logger = logger;
        }

        private int Me => _user.UserId;

        public async Task<ResponseDto<MfLabourMonthResultDto>> RunAsync(MfLabourMonthRunDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(MfCostService.PostRoles)) return MfText.Bad<MfLabourMonthResultDto>(MfText.NotAllowed);
            DateTime asOf = (dto?.AsOf ?? DateTime.Today).Date;
            bool allowFuture = string.Equals(_configuration?[SvJobsGuard.AllowFutureKey], "true", StringComparison.OrdinalIgnoreCase);
            string refusal = SvJobsGuard.Refusal(asOf, DateTime.Today, allowFuture);
            if (refusal != null) return MfText.Bad<MfLabourMonthResultDto>(refusal);
            try
            {
                return ResponseDtoHelper.Ok(await PostAsync(asOf, ct));
            }
            catch (DbUpdateException)
            {
                // a second run at the same moment: the unique pool index (or the row version) lets only one of them close the month
                return MfText.Bad<MfLabourMonthResultDto>(MfText.Concurrency);
            }
        }

        public async Task<int> RunNightlyAsync(CancellationToken ct)
        {
            try
            {
                return (await PostAsync(DateTime.Today, ct)).Periods.Count(p => p.State != LabourStates.Pending);
            }
            catch (DbUpdateException ex)
            {
                // a manual run closed the month at the same moment (unique pool index): nothing is lost, the next night finds the month closed
                _logger?.LogInformation(ex, "Production labour month: a concurrent run closed the month first; nothing to do.");
                return 0;
            }
        }

        public async Task<ResponseDto<List<MfLabourPeriodDto>>> HistoryAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(MfCostService.ReadRoles)) return MfText.Bad<List<MfLabourPeriodDto>>(MfText.NotAllowed);
            bool seesPayroll = MfPrivacy.SeesPayroll(_user);
            List<MfLabourPool> pools = await _uow.Set<MfLabourPool>().AsNoTracking().Where(p => p.Module == MfLabourModule.Production).ToListAsync(ct);
            var rows = pools.Select(p => LabourStates.Dto(p, seesPayroll)).ToList();

            // the months that wait: bookings of payroll-rated people not yet taken into a pool (the month has not ended, or the gate was not passed)
            var waiting = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.LabourPendingPeriod != null && b.LabourAggregatedAt == null)
                .GroupBy(b => b.LabourPendingPeriod.Value).Select(g => new { Period = g.Key, Count = g.Count(), Absorbed = g.Sum(x => x.AbsorbedLabour ?? 0m) }).ToListAsync(ct);
            foreach (var w in waiting.Where(w => rows.All(r => r.Period != w.Period)))
                rows.Add(LabourStates.PendingDto(w.Period, MfLabourMonth.Text(w.Period), w.Count, w.Absorbed));
            return ResponseDtoHelper.Ok(rows.OrderByDescending(r => r.Period).ToList());
        }

        private async Task<MfLabourMonthResultDto> PostAsync(DateTime asOf, CancellationToken ct)
        {
            var result = new MfLabourMonthResultDto { AsOf = asOf };
            int current = MfLabourMonth.Of(asOf);
            List<MfTimeBooking> bookings = await _uow.Set<MfTimeBooking>()
                .Where(b => !b.Deleted && b.LabourPendingPeriod != null && b.LabourAggregatedAt == null && b.LabourPendingPeriod < current).ToListAsync(ct);
            if (bookings.Count == 0)
            {
                result.Message = "ثبت زمانِ منتظر بستن ماه (تفاوت دستمزد با نرخ استاندارد) که ماه آن تمام شده باشد وجود ندارد.";
                return result;
            }
            await _costing.EnsureAccountRolesAsync(ct);

            MfLabourGateSettings gate = MfLabourGateSettings.Read(_configuration);
            bool seesPayroll = MfPrivacy.SeesPayroll(_user);
            DateTimeOffset now = DateTimeOffset.Now;
            HashSet<int> closed = await MfLabourPoolStore.ClosedAsync(_uow, MfLabourModule.Production, ct);

            // the bookings by the month whose pool takes them, oldest month first; a carried month joins the next one, so the loop runs over those too as long as their month has ended
            var months = new SortedDictionary<int, List<MfTimeBooking>>();
            void Put(int period, MfTimeBooking b)
            {
                if (!months.TryGetValue(period, out List<MfTimeBooking> list)) months[period] = list = new List<MfTimeBooking>();
                list.Add(b);
            }
            foreach (MfTimeBooking b in bookings) Put(b.LabourPendingPeriod.Value, b);

            MfPoolItem Describe(MfTimeBooking b) => MfPoolItem.Of(b.Id, b.EmployeeId, b.Hours, b.PayrollCost ?? 0, b.AbsorbedLabour ?? 0);

            var posted = new List<(MfLabourPool Row, int Period, int Items)>();
            while (months.Count > 0 && months.Keys.First() < current)
            {
                int period = months.Keys.First();
                List<MfTimeBooking> month = months[period];
                months.Remove(period);

                // a month that is closed is never reopened: whatever still points to it goes to the first later month that is open (cannot happen through the posting; this keeps the unique pool index safe)
                if (closed.Contains(period))
                {
                    int open = MfLabourMonth.NextOpen(period, closed);
                    foreach (MfTimeBooking b in month) { b.LabourPendingPeriod = open; b.ModifyDate = now; Put(open, b); }
                    continue;
                }

                MfPoolPlan plan = MfLabourPooling.Plan(month.OrderBy(b => b.Id).Select(Describe), period, gate);

                // the gate is not passed (too few payroll-rated people, or one or two of them dominate the hours): nothing is published, the bookings wait in the next open month and join its pool
                if (plan.CarriedItemIds.Count > 0)
                {
                    HashSet<int> carried = plan.CarriedItemIds.ToHashSet();
                    int next = MfLabourMonth.NextOpen(period, closed);
                    foreach (MfTimeBooking b in month.Where(x => carried.Contains(x.Id)))
                    {
                        b.LabourPendingPeriod = next;
                        b.ModifyDate = now;
                        Put(next, b);
                    }
                    result.BookingsCarried += carried.Count;
                }
                if (plan.Pool == null) continue;
                MfPoolResult pool = plan.Pool;

                DateTime end = MfLabourMonth.End(period);
                string number = MfLabourMonth.Number(period), text = MfLabourMonth.Text(period);
                var row = new MfLabourPool
                {
                    Module = MfLabourModule.Production, Period = period, PoolKey = pool.Key, People = pool.People, Hours = pool.Hours, Cost = pool.Cost, Absorbed = pool.Absorbed, Variance = pool.Variance,
                    Rate = pool.Rate, Protected = pool.Protected, Items = pool.ItemIds.Count, PostedAt = now, PostedByUserId = Me,
                };
                _uow.Set<MfLabourPool>().Add(row);
                if (pool.Variance != 0)
                {
                    // the one company-level line of the month: no work order, no WBS node, no day, no cost centre
                    _uow.Set<CostEntry>().Add(new CostEntry
                    {
                        EntryDate = end, Kind = CostKind.Labour, CostCenter = null, Quantity = 0, Amount = pool.Variance, LabourProtected = pool.Protected,
                        SourceType = MfLabourMonth.SourceType, SourceId = period, SourceNumber = number,
                        Description = $"تفاوت دستمزد مستقیم ساخت (هزینه‌ی واقعی حقوق منهای جذب‌شده با نرخ استاندارد) - ماه {text}", CreatedAt = now, CreatedByUserId = Me,
                    });
                    // under-absorbed (variance > 0): the variance account takes the difference, the absorbed-labour account is topped up; over-absorbed: the other way round
                    List<AccountingEventLine> lines = MfLabourVariance.Lines(pool.Variance, AccountRoles.LabourAbsorbed, null, pool.Protected, $"تفاوت دستمزد جذب‌شده با نرخ استاندارد - ماه {text}");
                    row.AccountingEvent = _costing.AddEvent(AccountingEventType.LabourAbsorbed, end, MfLabourMonth.SourceType, period, number, "تفاوت دستمزد جذب‌شده با نرخ استاندارد (بستن ماه) - ماه " + text, lines);
                }

                // the bookings of the pool take the month they are closed in (a carried booking was pending in an earlier month)
                HashSet<int> done = pool.ItemIds.ToHashSet();
                foreach (MfTimeBooking b in month.Where(x => done.Contains(x.Id)))
                {
                    b.LabourPendingPeriod = period;
                    b.LabourAggregatedAt = now;
                    b.ModifyDate = now;
                }
                closed.Add(period);
                posted.Add((row, period, done.Count));
            }
            await _uow.SaveChangesAsync(ct);
            foreach (var p in posted) result.Periods.Add(LabourStates.Dto(p.Row, seesPayroll));
            result.Message = result.Periods.Count > 0
                ? $"تفاوت دستمزد {MfText.Number(result.Periods.Count)} ماه بسته شد." + (result.BookingsCarried > 0 ? $" {MfText.Number(result.BookingsCarried)} ثبت زمان به ماه بعد منتقل شد (تعداد افراد دارای فیش حقوقی کم است یا ساعت یک یا دو نفر غالب است)." : string.Empty)
                : result.BookingsCarried > 0 ? $"{MfText.Number(result.BookingsCarried)} ثبت زمان به ماه بعد منتقل شد؛ پایان فصل با علامت محرمانه بسته می‌شود." : "چیزی برای بستن نبود.";
            return result;
        }
    }

    /// <summary>The state names of a labour month and the role-dependent shape of its row (production and after-sales share them).</summary>
    internal static class LabourStates
    {
        public const string Posted = "Posted";
        public const string Protected = "Protected";
        public const string Pending = "Pending";

        public static string Text(string state) => state == Protected ? "بسته‌شده با علامت محرمانه (پایان فصل)" : state == Pending ? "منتظر بستن ماه" : "بسته‌شده";

        /// <summary>A closed month. The payroll figures (pool, variance) are filled for <paramref name="seesPayroll"/> only.</summary>
        public static MfLabourPeriodDto Dto(MfLabourPool p, bool seesPayroll)
        {
            string state = p.Protected ? Protected : Posted;
            var dto = new MfLabourPeriodDto
            {
                Period = p.Period, PeriodText = MfLabourMonth.Text(p.Period), State = state, StateText = Text(state), Bookings = p.Items, Absorbed = p.Absorbed, Protected = p.Protected,
                EventId = p.AccountingEventId, PostedAt = p.PostedAt,
            };
            if (seesPayroll)
            {
                dto.Variance = p.Variance;
                dto.Pools.Add(new MfLabourPoolDto
                {
                    PoolKey = p.PoolKey, IsCompany = p.PoolKey == MfLabourPooling.CompanyKey, People = p.People, Hours = p.Hours, Rate = p.Rate, Cost = p.Cost, Absorbed = p.Absorbed, Variance = p.Variance, Protected = p.Protected,
                });
            }
            return dto;
        }

        /// <summary>A month that waits: bookings of payroll-rated people not yet in a pool. Nothing of payroll in it.</summary>
        public static MfLabourPeriodDto PendingDto(int period, string text, int bookings, decimal absorbed) =>
            new MfLabourPeriodDto { Period = period, PeriodText = text, State = Pending, StateText = Text(Pending), Bookings = bookings, Absorbed = absorbed };
    }
}
