using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
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
        /// <summary>The monthly aggregate posting of the labour the daily cost posting left out (a group of fewer than three payroll-rated employees), by hand (finance, production manager).</summary>
        Task<ResponseDto<MfLabourMonthResultDto>> RunAsync(MfLabourMonthRunDto dto, CancellationToken ct);

        /// <summary>The same run for the nightly job (no user; the day is today): the first nights of a month post the month before. Returns the months posted.</summary>
        Task<int> RunNightlyAsync(CancellationToken ct);

        /// <summary>The months already posted, newest first; a protected amount is hidden from everybody but PAYROLL, FINANCEMANAGER and CEO.</summary>
        Task<ResponseDto<List<MfLabourPeriodDto>>> HistoryAsync(CancellationToken ct);
    }

    /// <summary>
    /// R1p D1: the monthly aggregate posting of protected production labour. The daily cost posting leaves out the labour of a group (work order, WBS node, work centre, day) with fewer than three payroll-rated
    /// employees, because its amount would be one person's pay; the bookings keep the amount and the month (<c>LabourPendingPeriod</c>). When the month has ended this service posts the pool of the month
    /// with <see cref="MfLabourPlanner"/>: per work order and WBS node where enough people are behind it, else per cost centre, else it carries the bookings to the next month, and at the end of a Persian
    /// quarter posts what is left per work order, flagged as protected. One accounting event per month. Idempotent: a booking is taken once; a month with nothing pending posts nothing.
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
            catch (DbUpdateConcurrencyException)
            {
                return MfText.Bad<MfLabourMonthResultDto>(MfText.Concurrency);
            }
        }

        public async Task<int> RunNightlyAsync(CancellationToken ct) => (await PostAsync(DateTime.Today, ct)).Periods.Count;

        public async Task<ResponseDto<List<MfLabourPeriodDto>>> HistoryAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(MfCostService.ReadRoles)) return MfText.Bad<List<MfLabourPeriodDto>>(MfText.NotAllowed);
            bool seesPayroll = MfPrivacy.SeesPayroll(_user);
            var bookings = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.LabourAggregatedAt != null && b.LabourPendingPeriod != null)
                .Select(b => new { Period = b.LabourPendingPeriod.Value, b.LabourAggregatedAt }).ToListAsync(ct);
            var costRows = await _uow.Set<CostEntry>().AsNoTracking().Where(c => c.SourceType == MfLabourMonth.SourceType)
                .Select(c => new { Period = c.SourceId, c.Amount, c.LabourProtected, c.WorkOrderId }).ToListAsync(ct);
            var costs = costRows.GroupBy(c => c.Period).Select(g => new
            {
                Period = g.Key, Amount = g.Sum(c => c.Amount), Flagged = g.Any(c => c.LabourProtected), WorkOrders = g.Where(c => c.WorkOrderId.HasValue).Select(c => c.WorkOrderId.Value).Distinct().Count(),
            }).ToList();
            var events = await _uow.Set<AccountingEvent>().AsNoTracking().Where(e => e.SourceType == MfLabourMonth.SourceType && e.ReversesEventId == null).Select(e => new { e.Id, e.SourceId }).ToListAsync(ct);
            var rows = bookings.GroupBy(b => b.Period).OrderByDescending(g => g.Key).Select(g =>
            {
                var cost = costs.FirstOrDefault(c => c.Period == g.Key);
                bool flagged = cost != null && cost.Flagged;
                return new MfLabourPeriodDto
                {
                    Period = g.Key, PeriodText = MfLabourMonth.Text(g.Key), Bookings = g.Count(), WorkOrders = cost?.WorkOrders ?? 0, Amount = flagged && !seesPayroll ? (decimal?)null : cost?.Amount ?? 0,
                    Protected = flagged, EventId = events.Where(e => e.SourceId == g.Key).OrderByDescending(e => e.Id).Select(e => (int?)e.Id).FirstOrDefault(), PostedAt = g.Max(b => b.LabourAggregatedAt),
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
                result.Message = "دستمزد معوقی که ماه آن تمام شده باشد برای ثبت ماهانه وجود ندارد.";
                return result;
            }
            await _costing.EnsureAccountRolesAsync(ct);

            // where each booking belongs: work order, WBS node and the cost centre of its work centre
            List<int> opIds = bookings.Select(b => b.OperationId).Distinct().ToList();
            Dictionary<int, MfOperation> ops = await _uow.Set<MfOperation>().AsNoTracking().Where(o => opIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
            List<int> orderIds = ops.Values.Select(o => o.OrderId).Distinct().ToList();
            Dictionary<int, MfProductionOrder> orders = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
            List<int> actIds = orders.Values.Where(o => o.ActivityId.HasValue).Select(o => o.ActivityId.Value).Distinct().ToList();
            Dictionary<int, int?> wbsOfActivity = actIds.Count == 0 ? new Dictionary<int, int?>() : await _uow.Set<PcActivity>().AsNoTracking().Where(a => actIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.WbsNodeId, ct);
            List<int> wcIds = ops.Values.Where(o => o.WorkCenterId.HasValue).Select(o => o.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, string> costCenters = wcIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => wcIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.CostCenter, ct);

            MfPendingBooking Describe(MfTimeBooking b)
            {
                MfOperation op = ops[b.OperationId];
                MfProductionOrder order = orders[op.OrderId];
                return new MfPendingBooking
                {
                    BookingId = b.Id, WorkOrderId = order.WorkOrderId, ContractId = order.ContractId, WbsNodeId = order.ActivityId.HasValue ? wbsOfActivity.GetValueOrDefault(order.ActivityId.Value) : null,
                    CostCenter = op.WorkCenterId.HasValue ? MfText.Clean(costCenters.GetValueOrDefault(op.WorkCenterId.Value)) : null, EmployeeId = b.EmployeeId, FromPayroll = b.RateFromPayroll, Labour = b.PendingLabour ?? 0,
                };
            }

            DateTimeOffset now = DateTimeOffset.Now;
            HashSet<int> postedPeriods = (await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => b.LabourPendingPeriod != null && b.LabourAggregatedAt != null).Select(b => b.LabourPendingPeriod.Value).Distinct().ToListAsync(ct)).ToHashSet();

            // the pools by month, oldest first; a carried pool joins the pool of the next month, so the loop runs over those too as long as their month has ended
            var pools = new SortedDictionary<int, List<MfTimeBooking>>();
            foreach (MfTimeBooking b in bookings)
            {
                if (!pools.TryGetValue(b.LabourPendingPeriod.Value, out List<MfTimeBooking> list)) pools[b.LabourPendingPeriod.Value] = list = new List<MfTimeBooking>();
                list.Add(b);
            }

            bool seesPayroll = MfPrivacy.SeesPayroll(_user);
            var posted = new List<(MfLabourPeriodDto Dto, AccountingEvent Event)>();
            while (pools.Count > 0 && pools.Keys.First() < current)
            {
                int period = pools.Keys.First();
                List<MfTimeBooking> month = pools[period];
                pools.Remove(period);
                MfLabourPlan plan = MfLabourPlanner.Plan(month.OrderBy(b => b.Id).Select(Describe), period);

                // the bookings that stay pending go to the next month without a posting
                if (plan.CarriedBookingIds.Count > 0)
                {
                    HashSet<int> carried = plan.CarriedBookingIds.ToHashSet();
                    int next = MfLabourMonth.NextOpen(period, postedPeriods);
                    if (!pools.TryGetValue(next, out List<MfTimeBooking> target)) pools[next] = target = new List<MfTimeBooking>();
                    foreach (MfTimeBooking b in month.Where(x => carried.Contains(x.Id)))
                    {
                        b.LabourPendingPeriod = next;
                        b.ModifyDate = now;
                        target.Add(b);
                    }
                    result.BookingsCarried += carried.Count;
                }
                if (plan.Groups.Count == 0) continue;

                DateTime end = MfLabourMonth.End(period);
                string number = MfLabourMonth.Number(period), text = MfLabourMonth.Text(period);
                bool flagged = plan.Groups.Any(g => g.Protected);
                var lines = new List<AccountingEventLine>();
                foreach (MfLabourPlanGroup g in plan.Groups)
                {
                    _uow.Set<CostEntry>().Add(new CostEntry
                    {
                        EntryDate = end, Kind = CostKind.Labour, WorkOrderId = g.WorkOrderId, ContractId = g.ContractId, WbsNodeId = g.WbsNodeId, CostCenter = g.CostCenter, Quantity = 0, Amount = g.Amount,
                        SourceType = MfLabourMonth.SourceType, SourceId = period, SourceNumber = number, LabourProtected = g.Protected,
                        Description = (g.WorkOrderId.HasValue ? "دستمزد مستقیم ماهانه ساخت - ماه " : "دستمزد مستقیم ماهانه ساخت (مرکز هزینه) - ماه ") + text, CreatedAt = now, CreatedByUserId = Me,
                    });
                    lines.Add(new AccountingEventLine
                    {
                        AccountRole = AccountRoles.Wip, Debit = g.Amount, WorkOrderId = g.WorkOrderId, ContractId = g.ContractId, CostCenter = g.CostCenter, LabourProtected = g.Protected,
                        Description = "دستمزد جذب‌شده ماهانه در کار در جریان - ماه " + text,
                    });
                }
                decimal total = plan.Groups.Sum(g => g.Amount);
                lines.Add(new AccountingEventLine { AccountRole = AccountRoles.LabourAbsorbed, Credit = total, LabourProtected = flagged, Description = "دستمزد مستقیم جذب‌شده - ماه " + text });
                AccountingEvent ev = _costing.AddEvent(AccountingEventType.LabourAbsorbed, end, MfLabourMonth.SourceType, period, number, "دستمزد جذب‌شده ماهانه (گروه‌های حفاظت‌شده) - ماه " + text, lines);

                // the bookings of the groups take the month they are posted in (a carried booking was pending in an earlier month)
                HashSet<int> done = plan.Groups.SelectMany(g => g.BookingIds).ToHashSet();
                foreach (MfTimeBooking b in month.Where(x => done.Contains(x.Id)))
                {
                    b.LabourPendingPeriod = period;
                    b.LabourAggregatedAt = now;
                    b.ModifyDate = now;
                }
                postedPeriods.Add(period);
                posted.Add((new MfLabourPeriodDto
                {
                    Period = period, PeriodText = text, Bookings = done.Count, WorkOrders = plan.Groups.Where(g => g.WorkOrderId.HasValue).Select(g => g.WorkOrderId.Value).Distinct().Count(),
                    Amount = flagged && !seesPayroll ? (decimal?)null : total, Protected = flagged, PostedAt = now,
                }, ev));
            }
            await _uow.SaveChangesAsync(ct);
            foreach (var p in posted)
            {
                p.Dto.EventId = p.Event?.Id;
                result.Periods.Add(p.Dto);
            }
            result.Message = result.Periods.Count > 0
                ? $"دستمزد {MfText.Number(result.Periods.Count)} ماه ثبت شد." + (result.BookingsCarried > 0 ? $" {MfText.Number(result.BookingsCarried)} رزرو به ماه بعد منتقل شد (کمتر از سه نفر پرسنل با نرخ حقوق پشت آن است)." : string.Empty)
                : result.BookingsCarried > 0 ? $"{MfText.Number(result.BookingsCarried)} رزرو به ماه بعد منتقل شد؛ پایان فصل با علامت حفاظت‌شده ثبت می‌شود." : "چیزی برای ثبت نبود.";
            return result;
        }
    }
}
