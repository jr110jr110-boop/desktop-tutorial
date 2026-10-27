using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production
{
    public interface IMfCostService
    {
        /// <summary>Posts the labour and machine cost of the closed time bookings of a day (finance or production manager). Idempotent: a posted booking is never posted again.</summary>
        Task<ResponseDto<CostRunResultDto>> RunAsync(RunCostDto dto, CancellationToken ct);

        /// <summary>The nightly job: the same posting without a signed-in user.</summary>
        Task<CostRunResultDto> RunNightlyAsync(CancellationToken ct);

        Task<ResponseDto<PagedList<CostRunDto>>> GetRunsAsync(CostRunQuery query, CancellationToken ct);
        Task<ResponseDto<PagedList<CostPostingDto>>> GetPostingsAsync(CostPostingQuery query, CancellationToken ct);
    }

    /// <summary>
    /// Daily cost posting. Labour = booked hours x the hourly cost rate of the employee (the total employer cost of the last approved payroll month divided by the paid hours;
    /// until a payroll exists the standard labour rate of the work centre). Machine overhead = booked hours of the machine x the machine rate of the work centre. A missing rate is a
    /// clear error and the booking stays unposted (never a zero-cost posting). The cost is written per work order, WBS node, work centre and day (never per person) as
    /// <see cref="CostEntry"/> rows, and one accounting event "labour absorbed" debits work in progress and credits the absorbed-labour and absorbed-overhead accounts.
    /// </summary>
    public class MfCostService : IMfCostService
    {
        public const string RunSource = "MfCostRun";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;

        public MfCostService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
        }

        public const string PostRoles = "FINANCEMANAGER,FINANCIAL,PRODUCTIONMANAGER";
        public const string ReadRoles = "FINANCEMANAGER,FINANCIAL,PRODUCTIONMANAGER,CEO,AUDITOR,PMO";

        /// <summary>The only roles that see a labour amount that would reveal one person's payroll cost (the payroll readers of finance).</summary>
        public const string PayrollRoles = "PAYROLL,FINANCEMANAGER,CEO";

        // an AI seat never sees the payroll-derived amounts (the same rule as the after-sales module)
        private bool SeesPayroll => MfPrivacy.SeesPayroll(_user);

        public async Task<ResponseDto<CostRunResultDto>> RunAsync(RunCostDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(PostRoles)) return MfText.Bad<CostRunResultDto>(MfText.NotAllowed);
            DateTime? day = dto?.Date?.Date;
            if (day.HasValue && day.Value >= DateTime.Today) return MfText.Bad<CostRunResultDto>("هزینه فقط برای روزهایی که تمام شده‌اند ثبت می‌شود؛ تاریخ باید قبل از امروز باشد.");
            CostRunResultDto result;
            try
            {
                result = await new MfCostEngine(_uow, _costing, _user.UserId, SeesPayroll).RunAsync(day, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return MfText.Bad<CostRunResultDto>(MfText.Concurrency);
            }
            // nothing could be posted because a rate is missing: a refusal that names what finance has to enter
            if (result.BookingsPosted == 0 && result.BookingsSkipped > 0) return MfText.Bad<CostRunResultDto>(result.Message);
            return ResponseDtoHelper.Ok(result, result.Message);
        }

        public Task<CostRunResultDto> RunNightlyAsync(CancellationToken ct) => new MfCostEngine(_uow, _costing, 0, false).RunAsync(null, ct);

        public async Task<ResponseDto<PagedList<CostRunDto>>> GetRunsAsync(CostRunQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ReadRoles)) return MfText.Bad<PagedList<CostRunDto>>(MfText.NotAllowed);
            query ??= new CostRunQuery();
            IQueryable<MfCostRun> q = _uow.Set<MfCostRun>().AsNoTracking();
            if (query.From.HasValue) q = q.Where(r => r.PostingDate >= query.From.Value.Date);
            if (query.To.HasValue) q = q.Where(r => r.PostingDate <= query.To.Value.Date);
            string text = MfText.Clean(query.Contains);
            if (text != null) q = q.Where(r => r.Number.Contains(text));
            var rows = q.OrderByDescending(r => r.PostingDate).ThenByDescending(r => r.Id);
            PagedList<MfCostRun> page = await MfText.PageAsync(rows, query.PageNumber <= 0 ? 1 : query.PageNumber, query.PageSize, ct);
            return ResponseDtoHelper.Ok(await Map(page, page.Select(r => MfCostEngine.RunDto(r, SeesPayroll)).ToList(), ct));
        }

        public async Task<ResponseDto<PagedList<CostPostingDto>>> GetPostingsAsync(CostPostingQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ReadRoles)) return MfText.Bad<PagedList<CostPostingDto>>(MfText.NotAllowed);
            query ??= new CostPostingQuery();
            IQueryable<MfCostPosting> q = _uow.Set<MfCostPosting>().AsNoTracking();
            if (query.RunId.HasValue) q = q.Where(p => p.RunId == query.RunId.Value);
            if (query.WorkOrderId.HasValue) q = q.Where(p => p.WorkOrderId == query.WorkOrderId.Value);
            if (query.From.HasValue) q = q.Where(p => p.PostingDate >= query.From.Value.Date);
            if (query.To.HasValue) q = q.Where(p => p.PostingDate <= query.To.Value.Date);
            PagedList<MfCostPosting> page = await MfText.PageAsync(q.OrderByDescending(p => p.PostingDate).ThenBy(p => p.WorkOrderId).ThenBy(p => p.Id), query.PageNumber <= 0 ? 1 : query.PageNumber, query.PageSize, ct);
            List<int> runIds = page.Select(p => p.RunId).Distinct().ToList();
            Dictionary<int, string> runs = await _uow.Set<MfCostRun>().AsNoTracking().Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, ct);
            List<int> woIds = page.Select(p => p.WorkOrderId).Distinct().ToList();
            Dictionary<int, string> wos = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            List<int> wbsIds = page.Where(p => p.WbsNodeId.HasValue).Select(p => p.WbsNodeId.Value).Distinct().ToList();
            Dictionary<int, string> wbs = wbsIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<PcWbsNode>().AsNoTracking().Where(n => wbsIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, n => n.Code, ct);
            List<int> wcIds = page.Where(p => p.WorkCenterId.HasValue).Select(p => p.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, string> wcs = wcIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => wcIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            bool seesPayroll = SeesPayroll;
            var dtos = page.Select(p => new CostPostingDto
            {
                Id = p.Id, RunId = p.RunId, RunNumber = runs.GetValueOrDefault(p.RunId), PostingDate = p.PostingDate, PostingDateText = MfText.Date(p.PostingDate), WorkOrderId = p.WorkOrderId,
                WorkOrderCode = wos.GetValueOrDefault(p.WorkOrderId), ContractId = p.ContractId, WbsNodeId = p.WbsNodeId, WbsCode = p.WbsNodeId.HasValue ? wbs.GetValueOrDefault(p.WbsNodeId.Value) : null,
                WorkCenterId = p.WorkCenterId, WorkCenterCode = p.WorkCenterId.HasValue ? wcs.GetValueOrDefault(p.WorkCenterId.Value) : null, BookingCount = p.BookingCount,
                LabourHours = p.LabourHours, MachineHours = p.MachineHours, OverheadAmount = p.OverheadAmount, LabourDeferred = p.LabourDeferred,
                LabourAmount = MfCostMask.Labour(p.LabourAmount, MfCostMask.IsProtected(p.EmployeeCount, p.PayrollEmployeeCount), seesPayroll), LabourHidden = !seesPayroll && MfCostMask.IsProtected(p.EmployeeCount, p.PayrollEmployeeCount),
            }).ToList();
            return ResponseDtoHelper.Ok(await Map(page, dtos, ct));
        }

        internal static Task<PagedList<TOut>> Map<TIn, TOut>(PagedList<TIn> page, List<TOut> items, CancellationToken ct) => MfPaging.OfAsync(items, page.TotalCount, page.CurrentPage, page.PageSize, ct);
    }

    /// <summary>
    /// Privacy of the labour amount. A posting (or a run) with fewer than three distinct employees whose rate came from payroll would show one person's hourly payroll cost
    /// (amount / hours); that amount is hidden from every role except PAYROLL, FINANCEMANAGER and CEO.
    /// </summary>
    internal static class MfCostMask
    {
        public const int MinEmployees = 3;

        public static bool IsProtected(int employees, int payrollEmployees) => payrollEmployees > 0 && Math.Min(employees, payrollEmployees) < MinEmployees;

        public static decimal? Labour(decimal amount, bool isProtected, bool seesPayroll) => isProtected && !seesPayroll ? (decimal?)null : amount;
    }

    internal static class MfPaging
    {
        /// <summary>A page whose items are already cut (the totals come from the query).</summary>
        public static async Task<PagedList<T>> OfAsync<T>(List<T> items, int total, int number, int size, CancellationToken ct)
        {
            PagedList<T> all = await PagedList<T>.CreateAsync(items, 1, Math.Max(items.Count, 1), ct);
            all.TotalCount = total;
            all.CurrentPage = number;
            all.PageSize = size;
            all.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return all;
        }
    }

    /// <summary>The posting itself; used by the service (a person) and by the nightly job (no user).</summary>
    internal sealed class MfCostEngine
    {
        private readonly IUnitOfWork _uow;
        private readonly ICostingService _costing;
        private readonly int _userId;

        private readonly bool _seesPayroll;

        public MfCostEngine(IUnitOfWork uow, ICostingService costing, int userId, bool seesPayroll)
        {
            _uow = uow;
            _costing = costing;
            _userId = userId;
            _seesPayroll = seesPayroll;
        }

        internal static CostRunDto RunDto(MfCostRun r, bool seesPayroll) => new CostRunDto
        {
            Id = r.Id, Number = r.Number, PostingDate = r.PostingDate, PostingDateText = MfText.Date(r.PostingDate), RunAtText = MfText.Stamp(r.RunAt), BookingCount = r.BookingCount,
            LabourHours = r.LabourHours, MachineHours = r.MachineHours, LabourAmount = MfCostMask.Labour(r.LabourAmount, r.LabourProtected, seesPayroll), LabourHidden = r.LabourProtected && !seesPayroll,
            OverheadAmount = r.OverheadAmount, AccountingEventId = r.AccountingEventId,
        };

        /// <summary>The hourly labour cost of one employee (also used by the after-sales service orders).</summary>
        internal sealed class Rate
        {
            public decimal PerHour;
            public bool FromPayroll;
            public string Error;
        }

        private sealed class Group
        {
            public int WorkOrderId;
            public int? ContractId;
            public int? WbsNodeId;
            public int? WorkCenterId;
            public int Bookings;
            public decimal LabourHours;
            public decimal MachineHours;
            public decimal Labour;
            public decimal Overhead;
            public readonly HashSet<int> Employees = new HashSet<int>();
            public readonly HashSet<int> PayrollEmployees = new HashSet<int>();

            /// <summary>R1p D1: fewer than three payroll-rated employees behind the group: its labour waits for the monthly aggregate posting.</summary>
            public bool Deferred;

            public readonly List<(MfTimeBooking Booking, decimal Labour, bool FromPayroll)> Items = new List<(MfTimeBooking, decimal, bool)>();
        }

        private static decimal R(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);

        public async Task<CostRunResultDto> RunAsync(DateTime? onlyDay, CancellationToken ct)
        {
            var result = new CostRunResultDto();
            DateTime today = DateTime.Today;
            IQueryable<MfTimeBooking> q = _uow.Set<MfTimeBooking>().Where(b => !b.Deleted && b.EndAt != null && b.CostPostedAt == null);
            q = onlyDay.HasValue ? q.Where(b => b.StartAt >= onlyDay.Value && b.StartAt < onlyDay.Value.AddDays(1)) : q.Where(b => b.StartAt < today);
            List<MfTimeBooking> bookings = await q.OrderBy(b => b.StartAt).ThenBy(b => b.Id).ToListAsync(ct);
            if (bookings.Count == 0)
            {
                result.Message = "رزرو زمانی پست‌نشده‌ای برای ثبت هزینه وجود ندارد.";
                result.LabourAmount = 0; // nothing posted: zero, not hidden (null means hidden payroll cost)
                return result;
            }
            await _costing.EnsureAccountRolesAsync(ct);

            List<int> opIds = bookings.Select(b => b.OperationId).Distinct().ToList();
            Dictionary<int, MfOperation> ops = await _uow.Set<MfOperation>().AsNoTracking().Where(o => opIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
            List<int> orderIds = ops.Values.Select(o => o.OrderId).Distinct().ToList();
            Dictionary<int, MfProductionOrder> orders = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
            List<int> actIds = orders.Values.Where(o => o.ActivityId.HasValue).Select(o => o.ActivityId.Value).Distinct().ToList();
            Dictionary<int, int?> wbsOfActivity = actIds.Count == 0 ? new Dictionary<int, int?>() : await _uow.Set<PcActivity>().AsNoTracking().Where(a => actIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.WbsNodeId, ct);
            List<int> wcIds = ops.Values.Where(o => o.WorkCenterId.HasValue).Select(o => o.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, MfWorkCenter> centers = wcIds.Count == 0 ? new Dictionary<int, MfWorkCenter>() : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => wcIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
            var rates = new Dictionary<int, Rate>();
            var errors = new Dictionary<string, int>(StringComparer.Ordinal);
            void Err(string message) { errors[message] = errors.GetValueOrDefault(message) + 1; }

            var byDay = new SortedDictionary<DateTime, Dictionary<(int, int?, int?), Group>>();
            var posted = new Dictionary<DateTime, List<MfTimeBooking>>();
            var zero = new List<MfTimeBooking>();
            foreach (MfTimeBooking b in bookings)
            {
                if (b.Hours <= 0) { zero.Add(b); continue; }
                if (!ops.TryGetValue(b.OperationId, out MfOperation op) || !orders.TryGetValue(op.OrderId, out MfProductionOrder order)) { Err($"رزرو {b.Id}: عملیات یا دستور تولید آن پیدا نشد."); result.BookingsSkipped++; continue; }
                MfWorkCenter wc = op.WorkCenterId.HasValue ? centers.GetValueOrDefault(op.WorkCenterId.Value) : null;
                string where = MfNames.Operation(order.Number, op.Sequence);

                Rate rate = await LabourRateAsync(b.EmployeeId, wc, where, rates, ct);
                if (rate.Error != null) { Err(rate.Error); result.BookingsSkipped++; continue; }

                decimal machineRate = 0;
                bool usesMachine = b.MachineId.HasValue && (b.Kind == MfBookingKind.Setup || b.Kind == MfBookingKind.Run || b.Kind == MfBookingKind.Rework);
                if (usesMachine)
                {
                    if (wc == null) { Err($"عملیات {where}: مرکز کار ندارد و نرخ ساعتی ماشین مشخص نیست؛ هزینه ثبت نشد."); result.BookingsSkipped++; continue; }
                    if (!wc.MachineRateRial.HasValue || wc.MachineRateRial.Value <= 0)
                    {
                        Err($"نرخ ساعتی ماشین مرکز کار {wc.Code} ثبت نشده است؛ امور مالی نرخ را وارد کند. رزروهای این مرکز ثبت هزینه نشدند.");
                        result.BookingsSkipped++;
                        continue;
                    }
                    machineRate = wc.MachineRateRial.Value;
                }

                DateTime day = b.StartAt.Date;
                if (!byDay.TryGetValue(day, out var groups)) { groups = new Dictionary<(int, int?, int?), Group>(); byDay[day] = groups; posted[day] = new List<MfTimeBooking>(); }
                int? wbs = order.ActivityId.HasValue ? wbsOfActivity.GetValueOrDefault(order.ActivityId.Value) : null;
                var key = (order.WorkOrderId, wbs, op.WorkCenterId);
                if (!groups.TryGetValue(key, out Group g)) { g = new Group { WorkOrderId = order.WorkOrderId, ContractId = order.ContractId, WbsNodeId = wbs, WorkCenterId = op.WorkCenterId }; groups[key] = g; }
                g.Bookings++;
                g.Employees.Add(b.EmployeeId);
                if (rate.FromPayroll) g.PayrollEmployees.Add(b.EmployeeId);
                g.LabourHours += b.Hours;
                g.Labour += b.Hours * rate.PerHour;
                g.Items.Add((b, b.Hours * rate.PerHour, rate.FromPayroll));
                if (usesMachine) { g.MachineHours += b.Hours; g.Overhead += b.Hours * machineRate; }
                posted[day].Add(b);
                if (rate.FromPayroll) result.LabourRateFromPayroll++; else result.LabourRateFromWorkCenter++;
            }

            DateTimeOffset stamp = DateTimeOffset.Now;
            foreach (MfTimeBooking b in zero)
            {
                b.CostPostedAt = stamp;
                b.ModifyDate = stamp;
                result.ZeroHourBookings++;
            }

            var events = new List<(MfCostRun Run, AccountingEvent Event)>();
            foreach (var dayGroups in byDay)
            {
                DateTime day = dayGroups.Key;
                List<Group> list = dayGroups.Value.Values.OrderBy(g => g.WorkOrderId).ThenBy(g => g.WbsNodeId ?? 0).ThenBy(g => g.WorkCenterId ?? 0).ToList();
                foreach (Group g in list)
                {
                    g.Overhead = R(g.Overhead);
                    // R1p D1: a group of fewer than three payroll-rated employees would show one person's pay as its daily labour amount, so that labour is not posted per day. Its bookings carry the amount
                    // (unrounded) and the month they belong to; the monthly aggregate posting takes it (per work order, else per cost centre, else carried on; see MfLabourService).
                    g.Deferred = MfCostMask.IsProtected(g.Employees.Count, g.PayrollEmployees.Count);
                    if (g.Deferred)
                    {
                        result.BookingsDeferred += g.Bookings;
                        foreach (var item in g.Items)
                        {
                            item.Booking.LabourPendingPeriod = MfLabourMonth.Of(day);
                            item.Booking.PendingLabour = item.Labour;
                            item.Booking.RateFromPayroll = item.FromPayroll;
                        }
                        g.Labour = 0;
                    }
                    else g.Labour = R(g.Labour);
                }
                var run = new MfCostRun
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, MfNumbers.CostRun, "MCR", day, ct), PostingDate = day, RunAt = stamp, RunByUserId = _userId, BookingCount = posted[day].Count,
                    LabourHours = list.Sum(g => g.LabourHours), MachineHours = list.Sum(g => g.MachineHours), LabourAmount = list.Sum(g => g.Labour), OverheadAmount = list.Sum(g => g.Overhead),
                    EmployeeCount = list.SelectMany(g => g.Employees).Distinct().Count(), PayrollEmployeeCount = list.SelectMany(g => g.PayrollEmployees).Distinct().Count(),
                    LabourProtected = false,   // the protected groups are not in the amounts of the run any more, so the run total shows nobody's pay
                };
                _uow.Set<MfCostRun>().Add(run);
                foreach (Group g in list)
                {
                    _uow.Set<MfCostPosting>().Add(new MfCostPosting
                    {
                        Run = run, PostingDate = day, WorkOrderId = g.WorkOrderId, ContractId = g.ContractId, WbsNodeId = g.WbsNodeId, WorkCenterId = g.WorkCenterId, BookingCount = g.Bookings,
                        LabourHours = g.LabourHours, MachineHours = g.MachineHours, LabourAmount = g.Labour, OverheadAmount = g.Overhead, EmployeeCount = g.Employees.Count, PayrollEmployeeCount = g.PayrollEmployees.Count,
                        LabourDeferred = g.Deferred,
                    });
                    string wcCode = g.WorkCenterId.HasValue ? centers.GetValueOrDefault(g.WorkCenterId.Value)?.Code : null;
                    string text = $"{run.Number} - {MfText.Date(day)}" + (wcCode != null ? " - " + wcCode : string.Empty);
                    if (g.Labour != 0)
                        _uow.Set<CostEntry>().Add(new CostEntry
                        {
                            EntryDate = day, Kind = CostKind.Labour, WorkOrderId = g.WorkOrderId, ContractId = g.ContractId, WbsNodeId = g.WbsNodeId, Quantity = 0, Amount = g.Labour,
                            SourceType = MfCostService.RunSource, SourceId = DayKey(day), SourceNumber = run.Number, Description = "دستمزد مستقیم ساخت - " + text, CreatedAt = stamp, CreatedByUserId = _userId,
                        });
                    if (g.Overhead != 0)
                        _uow.Set<CostEntry>().Add(new CostEntry
                        {
                            EntryDate = day, Kind = CostKind.Overhead, WorkOrderId = g.WorkOrderId, ContractId = g.ContractId, WbsNodeId = g.WbsNodeId, Quantity = g.MachineHours, Amount = g.Overhead,
                            SourceType = MfCostService.RunSource, SourceId = DayKey(day), SourceNumber = run.Number, Description = "سربار ماشین - " + text, CreatedAt = stamp, CreatedByUserId = _userId,
                        });
                }

                // Dr work in progress per work order and project, Cr absorbed labour and absorbed overhead
                var lines = new List<AccountingEventLine>();
                foreach (Group g in list)
                {
                    if (g.Labour + g.Overhead == 0) continue;
                    lines.Add(new AccountingEventLine { AccountRole = AccountRoles.Wip, Debit = g.Labour + g.Overhead, WorkOrderId = g.WorkOrderId, ContractId = g.ContractId, Description = "دستمزد و سربار جذب‌شده در کار در جریان" });
                }
                decimal labourTotal = list.Sum(g => g.Labour), overheadTotal = list.Sum(g => g.Overhead);
                if (labourTotal != 0) lines.Add(new AccountingEventLine { AccountRole = AccountRoles.LabourAbsorbed, Credit = labourTotal, Description = "دستمزد مستقیم جذب‌شده" });
                if (overheadTotal != 0) lines.Add(new AccountingEventLine { AccountRole = AccountRoles.OverheadAbsorbed, Credit = overheadTotal, Description = "سربار ماشین جذب‌شده" });
                AccountingEvent ev = _costing.AddEvent(AccountingEventType.LabourAbsorbed, day, MfCostService.RunSource, DayKey(day), run.Number, $"دستمزد و سربار جذب‌شده {MfText.Date(day)}", lines);
                run.AccountingEvent = ev;
                events.Add((run, ev));

                foreach (MfTimeBooking b in posted[day])
                {
                    b.CostRun = run;
                    b.CostPostedAt = stamp;
                    b.ModifyDate = stamp;
                }
            }

            await _uow.SaveChangesAsync(ct);

            foreach (var (run, _) in events) result.Runs.Add(RunDto(run, _seesPayroll));
            result.BookingsPosted = events.Sum(e => e.Run.BookingCount);
            bool hidden = !_seesPayroll && events.Any(e => e.Run.LabourProtected);
            result.LabourHidden = hidden;
            result.LabourAmount = hidden ? (decimal?)null : events.Sum(e => e.Run.LabourAmount);
            result.OverheadAmount = events.Sum(e => e.Run.OverheadAmount);
            foreach (var kv in errors.OrderBy(k => k.Key, StringComparer.Ordinal)) result.Errors.Add(kv.Value > 1 ? $"{kv.Key} ({kv.Value} رزرو)" : kv.Key);
            result.Message = result.BookingsPosted > 0
                ? $"هزینه {MfText.Number(result.BookingsPosted)} رزرو ثبت شد." + (result.BookingsSkipped > 0 ? $" {MfText.Number(result.BookingsSkipped)} رزرو به‌دلیل نبودن نرخ ثبت نشد." : string.Empty)
                    + (result.BookingsDeferred > 0 ? $" دستمزد {MfText.Number(result.BookingsDeferred)} رزرو که کمتر از سه نفر پرسنل با نرخ حقوق پشت آن است به ثبت ماهانه موکول شد." : string.Empty)
                : result.BookingsSkipped > 0 ? "هزینه‌ای ثبت نشد: " + string.Join("؛ ", result.Errors) : "رزرو دارای ساعت برای ثبت هزینه وجود نداشت.";
            return result;
        }

        private static int DayKey(DateTime day) => day.Year * 10000 + day.Month * 100 + day.Day;

        /// <summary>The hourly labour cost of an employee: payroll when there is an approved payslip, else the work-centre standard labour rate.</summary>
        internal async Task<Rate> LabourRateAsync(int employeeId, MfWorkCenter wc, string where, Dictionary<int, Rate> cache, CancellationToken ct)
        {
            // payroll rates are per employee; the work-centre rate is per work centre
            if (!cache.TryGetValue(employeeId, out Rate payroll))
            {
                payroll = await PayrollRateAsync(employeeId, ct);
                cache[employeeId] = payroll;
            }
            if (payroll.FromPayroll || payroll.Error != null) return payroll;
            if (wc == null) return new Rate { Error = $"عملیات {where}: مرکز کار ندارد و برای این پرسنل حقوق تاییدشده‌ای نیست؛ نرخ ساعتی دستمزد مشخص نیست و هزینه ثبت نشد." };
            if (!wc.LabourRateRial.HasValue || wc.LabourRateRial.Value <= 0)
                return new Rate { Error = $"نرخ ساعتی دستمزد استاندارد مرکز کار {wc.Code} ثبت نشده است و برای پرسنل حقوق تاییدشده‌ای نیست؛ امور مالی نرخ را وارد کند. رزروهای این مرکز ثبت هزینه نشدند." };
            return new Rate { PerHour = wc.LabourRateRial.Value };
        }

        private async Task<Rate> PayrollRateAsync(int employeeId, CancellationToken ct)
        {
            var slip = await (from s in _uow.Set<FnPayslip>().AsNoTracking()
                              join r in _uow.Set<FnPayrollRun>().AsNoTracking() on s.RunId equals r.Id
                              where s.EmployeeId == employeeId && (r.Status == FnPayrollStatus.Approved || r.Status == FnPayrollStatus.Posted || r.Status == FnPayrollStatus.Paid)
                              orderby r.Year descending, r.Month descending, r.Id descending
                              select new { s.GrossEarnings, s.EmployerInsurance, s.Provisions, s.DaysPaid, r.Year, r.Month }).FirstOrDefaultAsync(ct);
            if (slip == null) return new Rate();
            string code = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => e.Id == employeeId).Select(e => e.PersonnelCode).FirstOrDefaultAsync(ct);
            decimal? hoursPerDay = await _uow.Set<FnPayrollRateTable>().AsNoTracking().Where(t => t.Key == "DailyWorkHours" && (t.Year == slip.Year || t.Year == 0))
                .OrderByDescending(t => t.Year).Select(t => (decimal?)t.Value).FirstOrDefaultAsync(ct);
            if (!hoursPerDay.HasValue || hoursPerDay.Value <= 0)
                return new Rate { Error = $"ساعت کار روزانه (DailyWorkHours) در جدول نرخ‌های حقوق ثبت نشده است؛ نرخ ساعتی پرسنل {code} محاسبه نشد و هزینه ثبت نشد." };
            var sheet = await _uow.Set<FnAttendanceSheet>().AsNoTracking().Where(a => a.EmployeeId == employeeId && a.Year == slip.Year && a.Month == slip.Month)
                .Select(a => new { a.OvertimeHours, a.HolidayHours }).FirstOrDefaultAsync(ct);
            decimal paidHours = PaidHours(slip.DaysPaid, hoursPerDay.Value, sheet?.OvertimeHours ?? 0, sheet?.HolidayHours ?? 0);
            if (paidHours <= 0) return new Rate { Error = $"ساعت پرداخت‌شده آخرین فیش حقوقی پرسنل {code} صفر است؛ نرخ ساعتی محاسبه نشد و هزینه ثبت نشد." };
            decimal cost = slip.GrossEarnings + slip.EmployerInsurance + slip.Provisions;
            if (cost <= 0) return new Rate { Error = $"هزینه کارفرمای آخرین فیش حقوقی پرسنل {code} صفر است؛ نرخ ساعتی محاسبه نشد و هزینه ثبت نشد." };
            return new Rate { PerHour = HourlyRate(cost, paidHours), FromPayroll = true };
        }

        /// <summary>The hours the employer cost of a payroll month paid for: paid days x daily work hours + overtime hours + holiday hours of the month's attendance sheet.</summary>
        internal static decimal PaidHours(decimal daysPaid, decimal dailyWorkHours, decimal overtimeHours, decimal holidayHours) => daysPaid * dailyWorkHours + overtimeHours + holidayHours;

        /// <summary>Total employer cost of the month (earnings + employer insurance + provisions) divided by the paid hours.</summary>
        internal static decimal HourlyRate(decimal employerCost, decimal paidHours) => employerCost / paidHours;
    }
}
