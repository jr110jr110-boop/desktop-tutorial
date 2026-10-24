using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production
{
    public interface IMfScheduleService
    {
        /// <summary>Runs the finite-capacity scheduler (deterministic and re-runnable). <c>AsOf</c> replaces "today".</summary>
        Task<ResponseDto<ScheduleResultDto>> RunAsync(RunScheduleDto dto, CancellationToken ct);

        /// <summary>The nightly job: the same run for the server date, without a signed-in user.</summary>
        Task<ScheduleResultDto> RunNightlyAsync(CancellationToken ct);

        Task<ResponseDto<LoadReportDto>> GetLoadAsync(LoadQuery query, CancellationToken ct);
        Task<ResponseDto<PagedList<DispatchRowDto>>> GetDispatchAsync(DispatchQuery query, CancellationToken ct);
    }

    public class MfScheduleService : IMfScheduleService
    {
        public const string RunRoles = "PRODUCTIONMANAGER,SHOPSUPERVISOR,PLANNING,PMO";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public MfScheduleService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public async Task<ResponseDto<ScheduleResultDto>> RunAsync(RunScheduleDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(RunRoles)) return MfText.Bad<ScheduleResultDto>(MfText.NotAllowed);
            try
            {
                return ResponseDtoHelper.Ok(await RunCoreAsync(dto?.AsOf?.Date ?? DateTime.Today, _user.UserId, ct));
            }
            catch (DbUpdateConcurrencyException)
            {
                return MfText.Bad<ScheduleResultDto>(MfText.Concurrency);
            }
        }

        public Task<ScheduleResultDto> RunNightlyAsync(CancellationToken ct) => RunCoreAsync(DateTime.Today, 0, ct);

        private async Task<ScheduleResultDto> RunCoreAsync(DateTime asOf, int userId, CancellationToken ct)
        {
            MfScheduleData data = await MfScheduler.ComputeAsync(_uow, asOf, ct);
            DateTimeOffset stamp = DateTimeOffset.Now;
            int operations = 0, unscheduled = 0, pushed = 0;
            var result = new ScheduleResultDto { AsOf = asOf, AsOfText = MfText.Date(asOf) };
            foreach (MfOrderPlan plan in data.Orders)
            {
                MfProductionOrder order = plan.Order;
                order.PlannedStart = plan.Start;
                order.PlannedFinish = plan.Finish;
                order.NeedDate = plan.Need;
                order.IsLate = plan.IsLate;
                order.DaysLate = plan.DaysLate;
                order.ScheduledAt = stamp;
                order.KitReadyDate = plan.KitShortage ? plan.KitReady : null;
                order.KitShortage = plan.KitShortage;
                order.ScheduleNote = Clip(plan.Note, 500);
                var item = new ScheduledOrderDto
                {
                    OrderId = order.Id, Number = order.Number, Priority = plan.Priority > 0 ? plan.Priority : (int?)null, NeedDate = plan.Need, NeedDateText = MfText.Date(plan.Need),
                    PlannedStart = plan.Start, PlannedStartText = MfText.Date(plan.Start), PlannedFinish = plan.Finish, PlannedFinishText = MfText.Date(plan.Finish), IsLate = plan.IsLate,
                    DaysLate = plan.DaysLate, KitReadyDate = plan.KitShortage ? plan.KitReady : null, KitReadyDateText = plan.KitShortage ? MfText.Date(plan.KitReady) : null,
                    KitShortage = plan.KitShortage, Note = plan.Note,
                };
                foreach (MfOpPlan p in plan.Ops)
                {
                    if (!p.Completed)
                    {
                        p.Op.PlannedStart = p.Start;
                        p.Op.PlannedFinish = p.Finish;
                        operations++;
                        if (!p.Finish.HasValue) unscheduled++;
                    }
                    data.Centers.TryGetValue(p.Op.WorkCenterId ?? 0, out MfCenter center);
                    item.Operations.Add(new ScheduledOperationDto
                    {
                        OperationId = p.Op.Id, JobCard = MfNames.Operation(order.Number, p.Op.Sequence), Sequence = p.Op.Sequence, Title = p.Op.Title, WorkCenterId = p.Op.WorkCenterId,
                        WorkCenterCode = center?.Wc.Code, Status = p.Op.Status, Hours = p.Hours, PlannedStart = p.Op.PlannedStart, PlannedStartText = MfText.Date(p.Op.PlannedStart),
                        PlannedFinish = p.Op.PlannedFinish, PlannedFinishText = MfText.Date(p.Op.PlannedFinish), Note = p.Note,
                    });
                }
                // the forecast of the project activity (the baseline is never touched)
                if (plan.Activity != null && plan.Finish.HasValue)
                {
                    plan.Activity.ForecastStart = plan.Start ?? plan.Activity.ForecastStart;
                    plan.Activity.ForecastFinish = plan.Finish;
                    pushed++;
                }
                if (plan.IsLate) result.LateOrders++;
                result.Items.Add(item);
            }
            result.Orders = data.Orders.Count;
            result.Operations = operations;
            result.Unscheduled = unscheduled;
            result.ForecastsPushed = pushed;
            var run = new MfScheduleRun { AsOfDate = asOf, RunAt = stamp, RunByUserId = userId, Orders = result.Orders, Operations = operations, LateOrders = result.LateOrders, Unscheduled = unscheduled };
            _uow.Set<MfScheduleRun>().Add(run);
            await _uow.SaveChangesAsync(ct);
            result.Id = run.Id;
            return result;
        }

        private static string Clip(string text, int max) => text == null ? null : text.Length <= max ? text : text.Substring(0, max);

        // ================================================================================== load report

        public async Task<ResponseDto<LoadReportDto>> GetLoadAsync(LoadQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Readers)) return MfText.Bad<LoadReportDto>(MfText.NotAllowed);
            query ??= new LoadQuery();
            DateTime from = query.From?.Date ?? DateTime.Today;
            int weeks = query.Weeks <= 0 ? 12 : Math.Min(query.Weeks, 52);
            decimal threshold = query.Threshold <= 0 ? 100 : query.Threshold;
            LoadReportDto report = await LoadAsync(_uow, from, weeks, threshold, query.WorkCenterId, ct);
            return ResponseDtoHelper.Ok(report);
        }

        internal static async Task<LoadReportDto> LoadAsync(IUnitOfWork uow, DateTime from, int weeks, decimal threshold, int? workCenterId, CancellationToken ct)
        {
            MfScheduleData data = await MfScheduler.ComputeAsync(uow, from, ct, true);
            var report = new LoadReportDto { From = from, FromText = MfText.Date(from), Weeks = weeks, Threshold = threshold };
            DateTime firstWeek = MfScheduler.WeekStart(from);
            foreach (MfCenter c in data.Centers.Values.Where(c => !workCenterId.HasValue || c.Wc.Id == workCenterId.Value).OrderBy(c => c.Wc.Code, StringComparer.Ordinal))
            {
                var row = new LoadRowDto { WorkCenterId = c.Wc.Id, Code = c.Wc.Code, Title = c.Wc.Title };
                for (int w = 0; w < weeks; w++)
                {
                    DateTime ws = firstWeek.AddDays(7 * w);
                    decimal capacity = 0, load = 0, scheduled = 0;
                    for (int d = 0; d < 7; d++)
                    {
                        DateTime day = ws.AddDays(d);
                        if (day < from) continue;
                        capacity += c.Capacity(day);
                        load += data.Demand.GetValueOrDefault((c.Wc.Id, day));
                        scheduled += data.Scheduled.GetValueOrDefault((c.Wc.Id, day));
                    }
                    capacity = Math.Round(capacity, 2, MidpointRounding.AwayFromZero);
                    load = Math.Round(load, 2, MidpointRounding.AwayFromZero);
                    scheduled = Math.Round(scheduled, 2, MidpointRounding.AwayFromZero);
                    decimal? percent = capacity > 0 ? Math.Round(load / capacity * 100m, 1, MidpointRounding.AwayFromZero) : (decimal?)null;
                    bool over = capacity > 0 ? load > capacity * threshold / 100m : load > 0;
                    var cell = new LoadCellDto
                    {
                        WorkCenterId = c.Wc.Id, WorkCenterCode = c.Wc.Code, WeekStart = ws, WeekStartText = MfText.Date(ws), CapacityHours = capacity, LoadHours = load, ScheduledHours = scheduled,
                        LoadPercent = percent, Overloaded = over,
                    };
                    row.Cells.Add(cell);
                    if (over) report.Overloads.Add(cell);
                }
                report.Rows.Add(row);
            }
            report.Overloads = report.Overloads.OrderByDescending(c => c.LoadPercent ?? decimal.MaxValue).ThenBy(c => c.WorkCenterCode, StringComparer.Ordinal).ThenBy(c => c.WeekStart).ToList();
            return report;
        }

        // ================================================================================== dispatch list

        public async Task<ResponseDto<PagedList<DispatchRowDto>>> GetDispatchAsync(DispatchQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return MfText.Bad<PagedList<DispatchRowDto>>(MfText.NotAllowed);
            if (query == null || query.WorkCenterId <= 0) return MfText.Bad<PagedList<DispatchRowDto>>("مرکز کار را انتخاب کنید.");
            if (!await _uow.Set<MfWorkCenter>().AnyAsync(c => c.Id == query.WorkCenterId && !c.Deleted, ct)) return MfText.NotFound<PagedList<DispatchRowDto>>("مرکز کار پیدا نشد.");
            MfOperationStatus[] open = { MfOperationStatus.Waiting, MfOperationStatus.Ready, MfOperationStatus.InProgress, MfOperationStatus.Paused };
            var rows = await (from o in _uow.Set<MfOperation>().AsNoTracking()
                              join p in _uow.Set<MfProductionOrder>().AsNoTracking() on o.OrderId equals p.Id
                              where !o.Deleted && !p.Deleted && o.WorkCenterId == query.WorkCenterId && open.Contains(o.Status) && (p.Status == MfOrderStatus.Released || p.Status == MfOrderStatus.InProgress)
                              select new { Op = o, Order = p }).ToListAsync(ct);
            string text = MfText.Clean(query.Contains);
            if (text != null) rows = rows.Where(r => r.Order.Number.Contains(text) || (r.Op.Title ?? string.Empty).Contains(text)).ToList();
            Dictionary<int, MfFlow.Urgency> urgency = await MfFlow.UrgencyAsync(_uow, ct);
            List<int> itemIds = rows.Where(r => r.Order.ItemId.HasValue).Select(r => r.Order.ItemId.Value).Distinct().ToList();
            Dictionary<int, (string Code, string Title)> items = itemIds.Count == 0 ? new Dictionary<int, (string, string)>()
                : (await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title }).ToListAsync(ct)).ToDictionary(i => i.Id, i => (i.Code, i.Title));
            var list = rows.Select(r =>
            {
                urgency.TryGetValue(r.Order.Id, out MfFlow.Urgency u);
                string stage = r.Op.Status == MfOperationStatus.InProgress || r.Op.Status == MfOperationStatus.Paused ? "Running" : r.Op.Status == MfOperationStatus.Ready ? "Ready" : "Next";
                (string Code, string Title) item = r.Order.ItemId.HasValue && items.TryGetValue(r.Order.ItemId.Value, out var it) ? it : (null, null);
                return new DispatchRowDto
                {
                    OperationId = r.Op.Id, OrderId = r.Order.Id, OrderNumber = r.Order.Number, JobCard = MfNames.Operation(r.Order.Number, r.Op.Sequence), Priority = u != null && u.Priority > 0 ? u.Priority : (int?)null,
                    Sequence = r.Op.Sequence, Title = r.Op.Title, Status = r.Op.Status, Stage = stage, ItemCode = item.Code, ItemTitle = item.Title, Quantity = r.Order.Quantity, Unit = r.Order.Unit,
                    SetupHoursPlanned = r.Op.SetupHoursPlanned, RunHoursPlanned = r.Op.RunHoursPlanned, PlannedStart = r.Op.PlannedStart, PlannedStartText = MfText.Date(r.Op.PlannedStart),
                    PlannedFinish = r.Op.PlannedFinish, PlannedFinishText = MfText.Date(r.Op.PlannedFinish), NeedDateText = MfText.Date(u?.NeedDate), OrderLate = r.Order.IsLate,
                };
            })
            .OrderBy(d => d.Stage == "Running" ? 0 : d.Stage == "Ready" ? 1 : 2).ThenBy(d => d.Priority ?? int.MaxValue).ThenBy(d => d.PlannedStart ?? DateTime.MaxValue)
            .ThenBy(d => d.OrderNumber, StringComparer.Ordinal).ThenBy(d => d.Sequence).ToList();
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<DispatchRowDto> slice = list.Skip((number - 1) * size).Take(size).ToList();
            return ResponseDtoHelper.Ok(await MfPaging.OfAsync(slice, list.Count, number, size, ct));
        }
    }
}
