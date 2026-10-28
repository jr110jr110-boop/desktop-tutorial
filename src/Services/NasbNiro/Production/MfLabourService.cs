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
using Services.NasbNiro.AfterSales;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production
{
    public interface IMfLabourService
    {
        /// <summary>
        /// The month-end run of the labour pools, by hand (finance manager, finance, production manager): every ended month that still has payroll-rated bookings is closed, i.e. its pools are stored (the rates of the next days)
        /// and the true-up (pool cost less what the days absorbed) is posted as one company-level line per pool.
        /// </summary>
        Task<ResponseDto<MfLabourMonthResultDto>> RunAsync(MfLabourMonthRunDto dto, CancellationToken ct);

        /// <summary>The same run for the nightly job (no user; the day is today): the first nights of a month close the month before. Returns the months closed.</summary>
        Task<int> RunNightlyAsync(CancellationToken ct);

        /// <summary>The months already closed, newest first; the amounts of a protected pool are hidden from everybody but PAYROLL, FINANCEMANAGER and CEO.</summary>
        Task<ResponseDto<List<MfLabourPeriodDto>>> HistoryAsync(CancellationToken ct);
    }

    // ===========================================================================================================================================================================================================
    // LABOUR COST RULE OF PRODUCTION (R1p D1) - POOLED AVERAGE RATE. The company must confirm the starred decisions (see the report of R1p).
    //
    //  1. Nothing that is published per work order, WBS node, work centre or day is one person's pay. Labour is absorbed at a pooled average rate: booked hours x the rate of a POOL, never x the employee's own rate.
    //     (Per-person hours next to per-group amounts, or any total next to the per-group amounts, would otherwise let a reader divide or subtract his way to one person's hourly pay.)
    //  2. The pool of a Persian month = the payroll-rated employees (an approved payslip exists) with booked hours in the month. ★ It is formed per COST CENTRE (the cost centre of the work centre) when the cost centre has at
    //     least three such people; the people of the cost centres that have fewer form the COMPANY pool when they are at least three themselves; when they are one or two, all pools of that month dissolve into one company
    //     pool (two rates published side by side would let anybody subtract the one from the other and isolate the one or two left over). Pool average rate = pool cost / pool hours, where the pool cost comes from payroll
    //     exactly as before (the employer cost of the last approved payslip over the paid hours, per person) and ONLY the pool total ever leaves the payroll boundary.
    //  3. DAILY cost runs (MfCostEngine) absorb a payroll-rated booking at the rate of the last CLOSED month of its pool (the cost centre's own pool of that month, else the company pool of that month); ★ until a month is
    //     closed at the standard labour rate (setting Production:StandardLabourRate, else the standard rate of the work centre). An employee without a payslip is costed at the work-centre standard rate (not pay).
    //  4. This MONTH-END run (a month is closed when it has ended) stores the pools (MfLabourPool: people, hours, cost, absorbed, rate for the next days) and posts the true-up, actual pool cost less absorbed, as ONE
    //     company-level labour-rate-variance line per pool: no work order, no WBS node, no day. The line moves the unabsorbed (or over-absorbed) amount from the absorbed-labour account to the variance account.
    //  5. CARRY: when the whole company has fewer than three payroll-rated people with hours in the month, nothing is published: the bookings move to the next open month and join its pool (a closed month is never
    //     reopened). ★ At the end of a Persian quarter (Khordad, Shahrivar, Azar, Esfand) the carry is posted anyway as one company-wide true-up line flagged LabourProtected; the flag goes to the cost entry, the event
    //     line and the voucher line, the readers of vouchers, ledgers, drill-downs and the Sepidar events export leave it out for every role except PAYROLL, FINANCEMANAGER and CEO. A protected pool has no rate.
    //  6. A booking whose month is already closed (a late booking) goes to the first later month that is not closed (MfLabourMonth.Assign): a closed month is never touched again.
    // ===========================================================================================================================================================================================================

    /// <summary>
    /// The month-end run of the production labour pools and their true-up (see the rule above). Idempotent: a booking is taken once, a month with nothing pending closes nothing, and a pool row exists once per
    /// module, month and pool (unique index).
    /// </summary>
    public class MfLabourService : IMfLabourService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;
        private readonly IConfiguration _configuration;

        public MfLabourService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing, IConfiguration configuration = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
            _configuration = configuration;
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

        public async Task<int> RunNightlyAsync(CancellationToken ct) => (await PostAsync(DateTime.Today, ct)).Periods.Count;

        public async Task<ResponseDto<List<MfLabourPeriodDto>>> HistoryAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(MfCostService.ReadRoles)) return MfText.Bad<List<MfLabourPeriodDto>>(MfText.NotAllowed);
            bool seesPayroll = MfPrivacy.SeesPayroll(_user);
            List<MfLabourPool> pools = await _uow.Set<MfLabourPool>().AsNoTracking().Where(p => p.Module == MfLabourModule.Production).OrderByDescending(p => p.Period).ThenBy(p => p.PoolKey).ToListAsync(ct);
            var rows = pools.GroupBy(p => p.Period).OrderByDescending(g => g.Key).Select(g =>
            {
                bool flagged = g.Any(p => p.Protected);
                bool hide = flagged && !seesPayroll;
                return new MfLabourPeriodDto
                {
                    Period = g.Key, PeriodText = MfLabourMonth.Text(g.Key), Bookings = g.Sum(p => p.Items), Protected = flagged,
                    Variance = hide ? (decimal?)null : g.Sum(p => p.Variance), EventId = g.Select(p => p.AccountingEventId).FirstOrDefault(e => e.HasValue), PostedAt = g.Max(p => p.PostedAt),
                    Pools = g.Select(p => new MfLabourPoolDto
                    {
                        PoolKey = p.PoolKey, IsCompany = p.PoolKey == MfLabourPooling.CompanyKey, People = p.People, Hours = p.Hours, Rate = p.Rate, Protected = p.Protected,
                        Absorbed = p.Protected && !seesPayroll ? (decimal?)null : p.Absorbed, Variance = p.Protected && !seesPayroll ? (decimal?)null : p.Variance,
                    }).ToList(),
                };
            }).ToList();
            return ResponseDtoHelper.Ok(rows);
        }

        private async Task<MfLabourMonthResultDto> PostAsync(DateTime asOf, CancellationToken ct)
        {
            var result = new MfLabourMonthResultDto { AsOf = asOf };
            int current = MfLabourMonth.Of(asOf);
            List<MfTimeBooking> bookings = await _uow.Set<MfTimeBooking>()
                .Where(b => !b.Deleted && b.LabourPendingPeriod != null && b.LabourAggregatedAt == null && b.LabourPendingPeriod < current).ToListAsync(ct);
            if (bookings.Count == 0)
            {
                result.Message = "ثبت زمانِ منتظر بستن ماه (دستمزد جذب‌شده با نرخ استخر) که ماه آن تمام شده باشد وجود ندارد.";
                return result;
            }
            await _costing.EnsureAccountRolesAsync(ct);

            // the cost centre of the work centre of each booking
            List<int> opIds = bookings.Select(b => b.OperationId).Distinct().ToList();
            Dictionary<int, int?> workCenterOfOperation = await _uow.Set<MfOperation>().AsNoTracking().Where(o => opIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.WorkCenterId, ct);
            List<int> wcIds = workCenterOfOperation.Values.Where(w => w.HasValue).Select(w => w.Value).Distinct().ToList();
            Dictionary<int, string> costCenters = wcIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => wcIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.CostCenter, ct);

            MfPoolItem Describe(MfTimeBooking b)
            {
                int? wc = workCenterOfOperation.GetValueOrDefault(b.OperationId);
                return new MfPoolItem
                {
                    Id = b.Id, CostCenter = wc.HasValue ? MfLabourMonth.CostCenterOf(costCenters.GetValueOrDefault(wc.Value)) : null, Employees = new HashSet<int> { b.EmployeeId },
                    Hours = b.Hours, Cost = b.PayrollCost ?? 0, Absorbed = b.AbsorbedLabour ?? 0,
                };
            }

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

            var posted = new List<(MfLabourPeriodDto Dto, AccountingEvent Event, List<MfLabourPool> Rows)>();
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

                MfPoolPlan plan = MfLabourPooling.Plan(month.OrderBy(b => b.Id).Select(Describe), period, MfLabourMonth.DefaultCostCenter);

                // fewer than three payroll-rated people in the whole month: the bookings wait in the next open month and join its pool
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
                if (plan.Pools.Count == 0) continue;

                DateTime end = MfLabourMonth.End(period);
                string number = MfLabourMonth.Number(period), text = MfLabourMonth.Text(period);
                var lines = new List<AccountingEventLine>();
                var rows = new List<MfLabourPool>();
                foreach (MfPoolResult pool in plan.Pools)
                {
                    bool company = pool.Key == MfLabourPooling.CompanyKey;
                    string label = company ? "استخر شرکت" : "مرکز هزینه " + pool.Key;
                    var row = new MfLabourPool
                    {
                        Module = MfLabourModule.Production, Period = period, PoolKey = pool.Key, People = pool.People, Hours = pool.Hours, Cost = pool.Cost, Absorbed = pool.Absorbed, Variance = pool.Variance,
                        Rate = pool.Rate, Protected = pool.Protected, Items = pool.ItemIds.Count, PostedAt = now, PostedByUserId = Me,
                    };
                    _uow.Set<MfLabourPool>().Add(row);
                    rows.Add(row);
                    if (pool.Variance != 0)
                    {
                        // the one company-level line of the pool: no work order, no WBS node, no day
                        _uow.Set<CostEntry>().Add(new CostEntry
                        {
                            EntryDate = end, Kind = CostKind.Labour, CostCenter = company ? null : pool.Key, Quantity = 0, Amount = pool.Variance, LabourProtected = pool.Protected,
                            SourceType = MfLabourMonth.SourceType, SourceId = period, SourceNumber = number,
                            Description = $"تفاوت نرخ دستمزد مستقیم ساخت (هزینه‌ی واقعی استخر منهای جذب‌شده) - ماه {text} - {label}", CreatedAt = now, CreatedByUserId = Me,
                        });
                        string description = $"تفاوت نرخ دستمزد جذب‌شده - ماه {text} - {label}";
                        string costCenter = company ? null : pool.Key;
                        // under-absorbed (variance > 0): the variance account takes the difference, the absorbed-labour account is topped up; over-absorbed: the other way round
                        lines.AddRange(MfLabourVariance.Lines(pool.Variance, AccountRoles.LabourAbsorbed, costCenter, pool.Protected, description));
                    }
                }
                AccountingEvent ev = lines.Count == 0 ? null
                    : _costing.AddEvent(AccountingEventType.LabourAbsorbed, end, MfLabourMonth.SourceType, period, number, "تفاوت نرخ دستمزد جذب‌شده (بستن ماه استخرهای دستمزد) - ماه " + text, lines);
                foreach (MfLabourPool row in rows) row.AccountingEvent = ev;

                // the bookings of the pools take the month they are closed in (a carried booking was pending in an earlier month)
                HashSet<int> done = plan.Pools.SelectMany(p => p.ItemIds).ToHashSet();
                foreach (MfTimeBooking b in month.Where(x => done.Contains(x.Id)))
                {
                    b.LabourPendingPeriod = period;
                    b.LabourAggregatedAt = now;
                    b.ModifyDate = now;
                }
                closed.Add(period);
                bool seesPayroll = MfPrivacy.SeesPayroll(_user);
                bool flagged = plan.Pools.Any(p => p.Protected);
                posted.Add((new MfLabourPeriodDto
                {
                    Period = period, PeriodText = text, Bookings = done.Count, Protected = flagged, PostedAt = now,
                    Variance = flagged && !seesPayroll ? (decimal?)null : plan.Pools.Sum(p => p.Variance),
                    Pools = plan.Pools.Select(p => new MfLabourPoolDto
                    {
                        PoolKey = p.Key, IsCompany = p.Key == MfLabourPooling.CompanyKey, People = p.People, Hours = p.Hours, Rate = p.Rate, Protected = p.Protected,
                        Absorbed = p.Protected && !seesPayroll ? (decimal?)null : p.Absorbed, Variance = p.Protected && !seesPayroll ? (decimal?)null : p.Variance,
                    }).ToList(),
                }, ev, rows));
            }
            await _uow.SaveChangesAsync(ct);
            foreach (var p in posted)
            {
                p.Dto.EventId = p.Event?.Id;
                result.Periods.Add(p.Dto);
            }
            result.Message = result.Periods.Count > 0
                ? $"دستمزد {MfText.Number(result.Periods.Count)} ماه بسته شد." + (result.BookingsCarried > 0 ? $" {MfText.Number(result.BookingsCarried)} ثبت زمان به ماه بعد منتقل شد (کمتر از سه نفر پرسنل با نرخ حقوق در شرکت)." : string.Empty)
                : result.BookingsCarried > 0 ? $"{MfText.Number(result.BookingsCarried)} ثبت زمان به ماه بعد منتقل شد؛ پایان فصل با علامت حفاظت‌شده بسته می‌شود." : "چیزی برای بستن نبود.";
            return result;
        }
    }
}
