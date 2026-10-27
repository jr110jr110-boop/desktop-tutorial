using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production
{
    public interface IMfMaintenanceService
    {
        Task<ResponseDto<PagedList<MaintenancePlanDto>>> GetPlansAsync(MaintenancePlanQuery query, CancellationToken ct);
        Task<ResponseDto<MaintenancePlanDto>> SavePlanAsync(SaveMaintenancePlanDto dto, CancellationToken ct);

        /// <summary>Drafts a planned maintenance order for every plan that is due within the lead days (config Production:MaintenanceLeadDays, default 7).</summary>
        Task<ResponseDto<DraftDueResultDto>> DraftDueAsync(DraftDueDto dto, CancellationToken ct);

        /// <summary>The nightly job: the same check for the server date without a signed-in user.</summary>
        Task<DraftDueResultDto> DraftDueNightlyAsync(CancellationToken ct);

        Task<ResponseDto<PagedList<MaintenanceOrderListDto>>> GetOrdersAsync(MaintenanceOrderQuery query, CancellationToken ct);
        Task<ResponseDto<MaintenanceOrderDto>> GetOrderAsync(int id, CancellationToken ct);
        Task<ResponseDto<MaintenanceOrderDto>> CreateOrderAsync(CreateMaintenanceOrderDto dto, CancellationToken ct);
        Task<ResponseDto<MaintenanceOrderDto>> SaveOrderAsync(SaveMaintenanceOrderDto dto, CancellationToken ct);
        Task<ResponseDto<MaintenanceOrderDto>> ActionAsync(MaintenanceOrderActionDto dto, CancellationToken ct);
        Task<ResponseDto<MaintenanceOrderDto>> AddLabourAsync(AddMaintenanceLabourDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<MachineKpiDto>>> GetMachineKpisAsync(KpiQuery query, CancellationToken ct);
        Task<ResponseDto<List<OeeDto>>> GetOeeAsync(KpiQuery query, CancellationToken ct);
    }

    /// <summary>Preventive and breakdown maintenance of the machines: plans, orders, spare parts, labour, cause and action, and the KPIs.</summary>
    public class MfMaintenanceService : IMfMaintenanceService
    {
        /// <summary>Maintenance does the work; the production manager sees and does everything.</summary>
        public const string WriteRoles = "MAINTENANCE,PRODUCTIONMANAGER";

        public const int DefaultLeadDays = 7;

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IInventoryDocumentService _documents;
        private readonly INotificationService _notify;
        private readonly IConfiguration _configuration;

        public MfMaintenanceService(IUnitOfWork uow, IInventoryUserContext user, IInventoryDocumentService documents, INotificationService notify = null, IConfiguration configuration = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
            _notify = notify;
            _configuration = configuration;
        }

        private int Me => _user.UserId;
        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);
        private static string Clean(string s) => MfText.Clean(s);

        private int LeadDays
        {
            get
            {
                string text = _configuration?["Production:MaintenanceLeadDays"];
                return int.TryParse(text, out int days) && days >= 0 && days <= 60 ? days : DefaultLeadDays;
            }
        }

        private static DateTime Now()
        {
            DateTime n = DateTime.Now;
            return new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second, n.Kind);
        }

        // ================================================================================== plans

        private static DateTime? NextDue(MfMaintenancePlan p) =>
            p.IntervalKind == MfMaintenanceIntervalKind.Days ? (p.LastDoneAt ?? p.StartDate).Date.AddDays((double)Math.Ceiling(p.IntervalValue)) : (DateTime?)null;

        private async Task<decimal> RunningHoursSinceAsync(MfMaintenancePlan p, CancellationToken ct)
        {
            DateTime since = (p.LastDoneAt ?? p.StartDate).Date;
            MfBookingKind[] machineKinds = { MfBookingKind.Setup, MfBookingKind.Run, MfBookingKind.Rework };
            return await _uow.Set<MfTimeBooking>().AsNoTracking()
                .Where(b => !b.Deleted && b.MachineId == p.MachineId && b.EndAt != null && b.StartAt >= since && machineKinds.Contains(b.Kind)).SumAsync(b => (decimal?)b.Hours, ct) ?? 0m;
        }

        private async Task<List<MaintenancePlanDto>> PlanDtosAsync(List<MfMaintenancePlan> plans, CancellationToken ct)
        {
            List<int> machineIds = plans.Select(p => p.MachineId).Distinct().ToList();
            Dictionary<int, string> machines = machineIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<Machine>().AsNoTracking().Where(m => machineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
            List<int> planIds = plans.Select(p => p.Id).ToList();
            var open = await _uow.Set<MfMaintenanceOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.PlanId != null && planIds.Contains(o.PlanId.Value) && (o.Status == MfMaintenanceOrderStatus.Open || o.Status == MfMaintenanceOrderStatus.InProgress))
                .Select(o => new { PlanId = o.PlanId.Value, o.Id }).ToListAsync(ct);
            var list = new List<MaintenancePlanDto>();
            foreach (MfMaintenancePlan p in plans)
            {
                list.Add(new MaintenancePlanDto
                {
                    Id = p.Id, MachineId = p.MachineId, MachineTitle = machines.GetValueOrDefault(p.MachineId), Task = p.Task, IntervalKind = p.IntervalKind, IntervalValue = p.IntervalValue,
                    StartDate = p.StartDate, StartDateText = MfText.Date(p.StartDate), LastDoneAt = p.LastDoneAt, LastDoneAtText = MfText.Date(p.LastDoneAt), NextDueDate = p.NextDueDate,
                    NextDueDateText = MfText.Date(p.NextDueDate), RunningHoursSince = p.IntervalKind == MfMaintenanceIntervalKind.RunningHours ? await RunningHoursSinceAsync(p, ct) : (decimal?)null,
                    Checklist = p.Checklist, EstimatedHours = p.EstimatedHours, IsActive = p.IsActive, OpenOrderId = open.Where(o => o.PlanId == p.Id).Select(o => (int?)o.Id).FirstOrDefault(),
                    RowVersion = MfConcurrency.Text(p.RowVersion),
                });
            }
            return list;
        }

        public async Task<ResponseDto<PagedList<MaintenancePlanDto>>> GetPlansAsync(MaintenancePlanQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return Bad<PagedList<MaintenancePlanDto>>(MfText.NotAllowed);
            query ??= new MaintenancePlanQuery();
            IQueryable<MfMaintenancePlan> q = _uow.Set<MfMaintenancePlan>().AsNoTracking().Where(p => !p.Deleted);
            if (query.MachineId.HasValue) q = q.Where(p => p.MachineId == query.MachineId.Value);
            if (query.IsActive.HasValue) q = q.Where(p => p.IsActive == query.IsActive.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(p => p.Task.Contains(text));
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfMaintenancePlan> rows = await q.OrderBy(p => p.MachineId).ThenBy(p => p.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(await MfPaging.OfAsync(await PlanDtosAsync(rows, ct), total, number, size, ct));
        }

        public async Task<ResponseDto<MaintenancePlanDto>> SavePlanAsync(SaveMaintenancePlanDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(WriteRoles)) return Bad<MaintenancePlanDto>(MfText.NotAllowed);
            if (dto == null || Clean(dto.Task) == null) return Bad<MaintenancePlanDto>("شرح کار نگهداری را بنویسید.");
            if (dto.IntervalValue <= 0) return Bad<MaintenancePlanDto>("فاصله سرویس باید بیشتر از صفر باشد.");
            if (!Enum.IsDefined(typeof(MfMaintenanceIntervalKind), dto.IntervalKind)) return Bad<MaintenancePlanDto>(MfText.InvalidData);
            if (dto.EstimatedHours < 0) return Bad<MaintenancePlanDto>("ساعت برآوردی نمی‌تواند منفی باشد.");
            if (!await _uow.Set<Machine>().AnyAsync(m => m.Id == dto.MachineId, ct)) return MfText.NotFound<MaintenancePlanDto>("ماشین پیدا نشد.");
            DateTimeOffset stamp = DateTimeOffset.Now;
            MfMaintenancePlan plan;
            if (dto.Id.HasValue)
            {
                plan = await _uow.Set<MfMaintenancePlan>().FirstOrDefaultAsync(p => p.Id == dto.Id.Value && !p.Deleted, ct);
                if (plan == null) return MfText.NotFound<MaintenancePlanDto>("برنامه نگهداری پیدا نشد.");
                if (!MfConcurrency.Apply(_uow, plan, dto.RowVersion)) return Bad<MaintenancePlanDto>(MfText.InvalidData);
                plan.ModifyDate = stamp;
                plan.ModifierUserId = Me;
            }
            else
            {
                plan = new MfMaintenancePlan { CreateDate = stamp, ModifyDate = stamp, CreatorUserId = Me, ModifierUserId = Me };
                _uow.Set<MfMaintenancePlan>().Add(plan);
            }
            plan.MachineId = dto.MachineId;
            plan.Task = Clean(dto.Task);
            plan.IntervalKind = dto.IntervalKind;
            plan.IntervalValue = dto.IntervalValue;
            plan.StartDate = (dto.StartDate ?? (plan.Id > 0 ? plan.StartDate : DateTime.Today)).Date;
            plan.Checklist = Clean(dto.Checklist);
            plan.EstimatedHours = dto.EstimatedHours;
            plan.IsActive = dto.IsActive;
            plan.NextDueDate = NextDue(plan);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<MaintenancePlanDto>(MfText.Concurrency);
            }
            return ResponseDtoHelper.Ok((await PlanDtosAsync(new List<MfMaintenancePlan> { plan }, ct))[0]);
        }

        // ================================================================================== drafting due plans

        public async Task<ResponseDto<DraftDueResultDto>> DraftDueAsync(DraftDueDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(WriteRoles)) return Bad<DraftDueResultDto>(MfText.NotAllowed);
            return ResponseDtoHelper.Ok(await DraftDueCoreAsync(dto?.AsOf?.Date ?? DateTime.Today, ct));
        }

        public Task<DraftDueResultDto> DraftDueNightlyAsync(CancellationToken ct) => DraftDueCoreAsync(DateTime.Today, ct);

        private async Task<DraftDueResultDto> DraftDueCoreAsync(DateTime asOf, CancellationToken ct)
        {
            var result = new DraftDueResultDto { AsOf = asOf };
            List<MfMaintenancePlan> plans = await _uow.Set<MfMaintenancePlan>().Where(p => !p.Deleted && p.IsActive).OrderBy(p => p.Id).ToListAsync(ct);
            result.PlansChecked = plans.Count;
            if (plans.Count == 0) return result;
            List<int> planIds = plans.Select(p => p.Id).ToList();
            HashSet<int> hasOpen = (await _uow.Set<MfMaintenanceOrder>().AsNoTracking()
                .Where(o => !o.Deleted && o.PlanId != null && planIds.Contains(o.PlanId.Value) && (o.Status == MfMaintenanceOrderStatus.Open || o.Status == MfMaintenanceOrderStatus.InProgress))
                .Select(o => o.PlanId.Value).ToListAsync(ct)).ToHashSet();
            List<int> machineIds = plans.Select(p => p.MachineId).Distinct().ToList();
            Dictionary<int, MfMachineInfo> infos = await _uow.Set<MfMachineInfo>().AsNoTracking().Where(i => !i.Deleted && machineIds.Contains(i.MachineId)).ToDictionaryAsync(i => i.MachineId, ct);
            Dictionary<int, string> titles = await _uow.Set<Machine>().AsNoTracking().Where(m => machineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
            DateTimeOffset stamp = DateTimeOffset.Now;
            int lead = LeadDays;
            var made = new List<MfMaintenanceOrder>();
            foreach (MfMaintenancePlan p in plans)
            {
                if (hasOpen.Contains(p.Id)) continue;
                infos.TryGetValue(p.MachineId, out MfMachineInfo info);
                if (info != null && info.Status == MfMachineStatus.Retired) continue;
                DateTime due;
                if (p.IntervalKind == MfMaintenanceIntervalKind.Days)
                {
                    due = NextDue(p).Value;
                    p.NextDueDate = due;
                    if (asOf < due.AddDays(-lead)) continue;
                }
                else
                {
                    if (await RunningHoursSinceAsync(p, ct) < p.IntervalValue) continue;
                    due = asOf;
                }
                MfWorkCenter center = info?.WorkCenterId == null ? null : await _uow.Set<MfWorkCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == info.WorkCenterId.Value, ct);
                var order = new MfMaintenanceOrder
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, MfNumbers.MaintenanceOrder, "MWO", asOf, ct), MachineId = p.MachineId, WorkCenterId = info?.WorkCenterId, Kind = MfMaintenanceKind.Planned,
                    Status = MfMaintenanceOrderStatus.Open, PlanId = p.Id, Task = p.Task, Checklist = p.Checklist, DueDate = due, EstimatedHours = p.EstimatedHours, CostCenter = center?.CostCenter,
                    CreateDate = stamp, ModifyDate = stamp, CreatorUserId = Me, ModifierUserId = Me,
                };
                _uow.Set<MfMaintenanceOrder>().Add(order);
                made.Add(order);
                // numbers are taken from the counter row; save per order so two orders never share a number
                await _uow.SaveChangesAsync(ct);
            }
            if (made.Count == 0)
            {
                await _uow.SaveChangesAsync(ct); // the next-due dates of the day plans
                return result;
            }
            await _uow.SaveChangesAsync(ct);
            foreach (MfMaintenanceOrder o in made)
            {
                await MfGates.MaintenanceOrderAsync(_notify, o, titles.GetValueOrDefault(o.MachineId), ct);
                result.Orders.Add(OrderListDto(o, titles.GetValueOrDefault(o.MachineId), asOf));
            }
            result.Drafted = made.Count;
            return result;
        }

        // ================================================================================== orders

        private static MaintenanceOrderListDto OrderListDto(MfMaintenanceOrder o, string machine, DateTime today) => new MaintenanceOrderListDto
        {
            Id = o.Id, Number = o.Number, MachineId = o.MachineId, MachineTitle = machine, WorkCenterId = o.WorkCenterId, Kind = o.Kind, Status = o.Status, PlanId = o.PlanId, RequestId = o.RequestId,
            Task = o.Task, DueDate = o.DueDate, DueDateText = MfText.Date(o.DueDate), Overdue = IsOverdue(o, today), EstimatedHours = o.EstimatedHours, DowntimeStart = o.DowntimeStart,
            DowntimeStartText = MfText.Stamp(o.DowntimeStart), DowntimeEnd = o.DowntimeEnd, DowntimeEndText = MfText.Stamp(o.DowntimeEnd), CostCenter = o.CostCenter,
        };

        internal static bool IsOpen(MfMaintenanceOrder o) => o.Status == MfMaintenanceOrderStatus.Open || o.Status == MfMaintenanceOrderStatus.InProgress;

        internal static bool IsOverdue(MfMaintenanceOrder o, DateTime today) => IsOpen(o) && o.DueDate.HasValue && o.DueDate.Value.Date < today.Date;

        public async Task<ResponseDto<PagedList<MaintenanceOrderListDto>>> GetOrdersAsync(MaintenanceOrderQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return Bad<PagedList<MaintenanceOrderListDto>>(MfText.NotAllowed);
            query ??= new MaintenanceOrderQuery();
            IQueryable<MfMaintenanceOrder> q = _uow.Set<MfMaintenanceOrder>().AsNoTracking().Where(o => !o.Deleted);
            if (query.MachineId.HasValue) q = q.Where(o => o.MachineId == query.MachineId.Value);
            if (query.Kind.HasValue) q = q.Where(o => o.Kind == query.Kind.Value);
            if (query.Status.HasValue) q = q.Where(o => o.Status == query.Status.Value);
            if (query.OpenOnly == true) q = q.Where(o => o.Status == MfMaintenanceOrderStatus.Open || o.Status == MfMaintenanceOrderStatus.InProgress);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(o => o.Number.Contains(text) || o.Task.Contains(text));
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfMaintenanceOrder> rows = await q.OrderByDescending(o => o.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            List<int> machineIds = rows.Select(o => o.MachineId).Distinct().ToList();
            Dictionary<int, string> machines = machineIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<Machine>().AsNoTracking().Where(m => machineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
            return ResponseDtoHelper.Ok(await MfPaging.OfAsync(rows.Select(o => OrderListDto(o, machines.GetValueOrDefault(o.MachineId), DateTime.Today)).ToList(), total, number, size, ct));
        }

        private async Task<MaintenanceOrderDto> OrderDtoAsync(MfMaintenanceOrder o, CancellationToken ct)
        {
            string machine = await _uow.Set<Machine>().AsNoTracking().Where(m => m.Id == o.MachineId).Select(m => m.Title).FirstOrDefaultAsync(ct);
            MaintenanceOrderListDto head = OrderListDto(o, machine, DateTime.Today);
            var dto = new MaintenanceOrderDto
            {
                Id = head.Id, Number = head.Number, MachineId = head.MachineId, MachineTitle = head.MachineTitle, WorkCenterId = head.WorkCenterId, Kind = head.Kind, Status = head.Status, PlanId = head.PlanId,
                RequestId = head.RequestId, Task = head.Task, DueDate = head.DueDate, DueDateText = head.DueDateText, Overdue = head.Overdue, EstimatedHours = head.EstimatedHours,
                DowntimeStart = head.DowntimeStart, DowntimeStartText = head.DowntimeStartText, DowntimeEnd = head.DowntimeEnd, DowntimeEndText = head.DowntimeEndText, CostCenter = head.CostCenter,
                Checklist = o.Checklist, Cause = o.Cause, ActionTaken = o.ActionTaken, MaterialRequestId = o.MaterialRequestId, IssueDocumentId = o.IssueDocumentId,
                StartedAtText = MfText.Stamp(o.StartedAt), DoneAtText = MfText.Stamp(o.DoneAt), CancelReason = o.CancelReason, RowVersion = MfConcurrency.Text(o.RowVersion),
            };
            if (o.DowntimeStart.HasValue && o.DowntimeEnd.HasValue) dto.DowntimeHours = Math.Round(Math.Max(0, (decimal)(o.DowntimeEnd.Value - o.DowntimeStart.Value).TotalHours), 2, MidpointRounding.AwayFromZero);
            if (o.MaterialRequestId.HasValue) dto.MaterialRequestNumber = await _uow.Set<MaterialRequest>().AsNoTracking().Where(r => r.Id == o.MaterialRequestId.Value).Select(r => r.Number).FirstOrDefaultAsync(ct);
            if (o.IssueDocumentId.HasValue) dto.IssueDocumentNumber = await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => d.Id == o.IssueDocumentId.Value).Select(d => d.Number).FirstOrDefaultAsync(ct);
            List<MfMaintenanceLabour> labour = await _uow.Set<MfMaintenanceLabour>().AsNoTracking().Where(l => !l.Deleted && l.OrderId == o.Id).OrderBy(l => l.WorkDate).ThenBy(l => l.Id).ToListAsync(ct);
            List<int> employeeIds = labour.Select(l => l.EmployeeId).Distinct().ToList();
            Dictionary<int, string> names = employeeIds.Count == 0 ? new Dictionary<int, string>() : MfNames.ToDictionary(await _uow.Set<FnEmployee>().AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToListAsync(ct));
            dto.Labour = labour.Select(l => new MaintenanceLabourDto { Id = l.Id, EmployeeId = l.EmployeeId, EmployeeName = names.GetValueOrDefault(l.EmployeeId), WorkDate = l.WorkDate, WorkDateText = MfText.Date(l.WorkDate), Hours = l.Hours, Note = l.Note }).ToList();
            dto.LabourHours = labour.Sum(l => l.Hours);
            var parts = await (from p in _uow.Set<MfMaintenancePart>().AsNoTracking()
                               join i in _uow.Set<InventoryItem>().AsNoTracking() on p.ItemId equals i.Id
                               where p.OrderId == o.Id
                               orderby p.Id
                               select new MaintenancePartDto { ItemId = p.ItemId, ItemCode = i.Code, ItemTitle = i.Title, Quantity = p.Quantity, Note = p.Note }).ToListAsync(ct);
            dto.Parts = parts;
            return dto;
        }

        public async Task<ResponseDto<MaintenanceOrderDto>> GetOrderAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return Bad<MaintenanceOrderDto>(MfText.NotAllowed);
            MfMaintenanceOrder o = await _uow.Set<MfMaintenanceOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (o == null) return MfText.NotFound<MaintenanceOrderDto>("دستور نگهداری پیدا نشد.");
            return ResponseDtoHelper.Ok(await OrderDtoAsync(o, ct));
        }

        /// <summary>The machine becomes available again when no breakdown order is open any more.</summary>
        private async Task ReleaseMachineAsync(MfMaintenanceOrder order, CancellationToken ct)
        {
            if (order.Kind != MfMaintenanceKind.Breakdown) return;
            bool otherOpen = await _uow.Set<MfMaintenanceOrder>().AnyAsync(o => o.Id != order.Id && !o.Deleted && o.MachineId == order.MachineId && o.Kind == MfMaintenanceKind.Breakdown
                && (o.Status == MfMaintenanceOrderStatus.Open || o.Status == MfMaintenanceOrderStatus.InProgress), ct);
            if (otherOpen) return;
            MfMachineInfo info = await _uow.Set<MfMachineInfo>().FirstOrDefaultAsync(i => i.MachineId == order.MachineId && !i.Deleted, ct);
            if (info != null && info.Status == MfMachineStatus.Down)
            {
                info.Status = MfMachineStatus.Available;
                info.ModifyDate = DateTimeOffset.Now;
                info.ModifierUserId = Me;
            }
        }

        public async Task<ResponseDto<MaintenanceOrderDto>> CreateOrderAsync(CreateMaintenanceOrderDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(WriteRoles)) return Bad<MaintenanceOrderDto>(MfText.NotAllowed);
            if (dto == null || Clean(dto.Task) == null) return Bad<MaintenanceOrderDto>("شرح کار نگهداری را بنویسید.");
            if (!Enum.IsDefined(typeof(MfMaintenanceKind), dto.Kind)) return Bad<MaintenanceOrderDto>(MfText.InvalidData);
            if (dto.EstimatedHours < 0) return Bad<MaintenanceOrderDto>("ساعت برآوردی نمی‌تواند منفی باشد.");
            Machine machine = await _uow.Set<Machine>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == dto.MachineId, ct);
            if (machine == null) return MfText.NotFound<MaintenanceOrderDto>("ماشین پیدا نشد.");
            MfMaintenanceRequest request = null;
            if (dto.RequestId.HasValue)
            {
                request = await _uow.Set<MfMaintenanceRequest>().FirstOrDefaultAsync(r => r.Id == dto.RequestId.Value && !r.Deleted, ct);
                if (request == null) return MfText.NotFound<MaintenanceOrderDto>("گزارش خرابی پیدا نشد.");
                if (request.MachineId != dto.MachineId) return Bad<MaintenanceOrderDto>("گزارش خرابی برای ماشین دیگری است.");
                if (dto.Kind != MfMaintenanceKind.Breakdown) return Bad<MaintenanceOrderDto>("دستور ساخته‌شده از گزارش خرابی باید از نوع خرابی باشد.");
                if (await _uow.Set<MfMaintenanceOrder>().AnyAsync(o => o.RequestId == request.Id && !o.Deleted && o.Status != MfMaintenanceOrderStatus.Cancelled, ct))
                    return Bad<MaintenanceOrderDto>("برای این گزارش خرابی قبلا دستور نگهداری ساخته شده است.");
            }
            string costCenter = Clean(dto.CostCenter);
            if (costCenter != null && !await _uow.Set<CostCenter>().AnyAsync(c => c.Code == costCenter && c.IsActive, ct)) return Bad<MaintenanceOrderDto>("مرکز هزینه پیدا نشد یا غیرفعال است.");
            DateTimeOffset stamp = DateTimeOffset.Now;
            MfMachineInfo info = await _uow.Set<MfMachineInfo>().FirstOrDefaultAsync(i => i.MachineId == machine.Id && !i.Deleted, ct);
            MfWorkCenter center = info?.WorkCenterId == null ? null : await _uow.Set<MfWorkCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == info.WorkCenterId.Value, ct);
            DateTime now = Now();
            var order = new MfMaintenanceOrder
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, MfNumbers.MaintenanceOrder, "MWO", now, ct), MachineId = machine.Id, WorkCenterId = info?.WorkCenterId, Kind = dto.Kind,
                Status = MfMaintenanceOrderStatus.Open, RequestId = request?.Id, Task = Clean(dto.Task), Checklist = Clean(dto.Checklist), DueDate = dto.DueDate?.Date, EstimatedHours = dto.EstimatedHours,
                CostCenter = costCenter ?? center?.CostCenter, CreateDate = stamp, ModifyDate = stamp, CreatorUserId = Me, ModifierUserId = Me,
            };
            if (dto.Kind == MfMaintenanceKind.Breakdown)
            {
                order.DowntimeStart = dto.DowntimeStart ?? request?.ReportedAt ?? now;
                order.DueDate ??= order.DowntimeStart.Value.Date;
                if (info == null)
                {
                    info = new MfMachineInfo { MachineId = machine.Id, CreateDate = stamp, CreatorUserId = Me };
                    _uow.Set<MfMachineInfo>().Add(info);
                }
                info.Status = MfMachineStatus.Down;
                info.ModifyDate = stamp;
                info.ModifierUserId = Me;
                if (request != null && request.Status == MfMaintenanceRequestStatus.Open) { request.Status = MfMaintenanceRequestStatus.InProgress; request.ModifyDate = stamp; request.ModifierUserId = Me; }
            }
            _uow.Set<MfMaintenanceOrder>().Add(order);
            await _uow.SaveChangesAsync(ct);
            await MfGates.MaintenanceOrderAsync(_notify, order, machine.Title, ct);
            if (request != null) await MfGates.MaintenanceAsync(_notify, request, machine.Title, ct);
            return ResponseDtoHelper.Ok(await OrderDtoAsync(order, ct));
        }

        /// <summary>Called by the machine-down report: the breakdown order is drafted at once (one per report).</summary>
        internal static async Task<MfMaintenanceOrder> DraftBreakdownAsync(IUnitOfWork uow, int userId, MfMaintenanceRequest request, string machineTitle, INotificationService notify, CancellationToken ct)
        {
            if (await uow.Set<MfMaintenanceOrder>().AnyAsync(o => o.RequestId == request.Id && !o.Deleted, ct)) return null;
            MfMachineInfo info = await uow.Set<MfMachineInfo>().AsNoTracking().FirstOrDefaultAsync(i => i.MachineId == request.MachineId && !i.Deleted, ct);
            MfWorkCenter center = info?.WorkCenterId == null ? null : await uow.Set<MfWorkCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == info.WorkCenterId.Value, ct);
            DateTimeOffset stamp = DateTimeOffset.Now;
            var order = new MfMaintenanceOrder
            {
                Number = await InventoryNumbers.NextNumberAsync(uow, MfNumbers.MaintenanceOrder, "MWO", request.ReportedAt, ct), MachineId = request.MachineId, WorkCenterId = info?.WorkCenterId,
                Kind = MfMaintenanceKind.Breakdown, Status = MfMaintenanceOrderStatus.Open, RequestId = request.Id, Task = request.Description, DueDate = request.ReportedAt.Date, DowntimeStart = request.ReportedAt,
                CostCenter = center?.CostCenter, CreateDate = stamp, ModifyDate = stamp, CreatorUserId = userId, ModifierUserId = userId,
            };
            uow.Set<MfMaintenanceOrder>().Add(order);
            await uow.SaveChangesAsync(ct);
            await MfGates.MaintenanceOrderAsync(notify, order, machineTitle, ct);
            return order;
        }

        public async Task<ResponseDto<MaintenanceOrderDto>> SaveOrderAsync(SaveMaintenanceOrderDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(WriteRoles)) return Bad<MaintenanceOrderDto>(MfText.NotAllowed);
            if (dto == null) return Bad<MaintenanceOrderDto>(MfText.InvalidData);
            MfMaintenanceOrder order = await _uow.Set<MfMaintenanceOrder>().FirstOrDefaultAsync(o => o.Id == dto.Id && !o.Deleted, ct);
            if (order == null) return MfText.NotFound<MaintenanceOrderDto>("دستور نگهداری پیدا نشد.");
            if (!IsOpen(order)) return Bad<MaintenanceOrderDto>("دستور نگهداری بسته شده است و ویرایش نمی‌شود.");
            if (!MfConcurrency.Apply(_uow, order, dto.RowVersion)) return Bad<MaintenanceOrderDto>(MfText.InvalidData);
            if (dto.Task != null && Clean(dto.Task) == null) return Bad<MaintenanceOrderDto>("شرح کار نگهداری را بنویسید.");
            if (dto.EstimatedHours.HasValue && dto.EstimatedHours.Value < 0) return Bad<MaintenanceOrderDto>("ساعت برآوردی نمی‌تواند منفی باشد.");
            string costCenter = Clean(dto.CostCenter);
            if (costCenter != null && !await _uow.Set<CostCenter>().AnyAsync(c => c.Code == costCenter && c.IsActive, ct)) return Bad<MaintenanceOrderDto>("مرکز هزینه پیدا نشد یا غیرفعال است.");
            DateTime? start = dto.DowntimeStart ?? order.DowntimeStart;
            if (dto.DowntimeEnd.HasValue && (!start.HasValue || dto.DowntimeEnd.Value < start.Value)) return Bad<MaintenanceOrderDto>("پایان توقف باید بعد از شروع توقف باشد.");
            if (dto.Parts != null)
            {
                if (order.MaterialRequestId.HasValue) return Bad<MaintenanceOrderDto>("درخواست کالای قطعات ساخته شده است و فهرست قطعات تغییر نمی‌کند.");
                if (dto.Parts.Any(p => p == null || p.Quantity <= 0)) return Bad<MaintenanceOrderDto>("مقدار هر قطعه باید بیشتر از صفر باشد.");
                List<int> ids = dto.Parts.Select(p => p.ItemId).Distinct().ToList();
                if (await _uow.Set<InventoryItem>().CountAsync(i => ids.Contains(i.Id) && !i.Deleted, ct) != ids.Count) return Bad<MaintenanceOrderDto>("یکی از قطعات پیدا نشد.");
                List<MfMaintenancePart> old = await _uow.Set<MfMaintenancePart>().Where(p => p.OrderId == order.Id).ToListAsync(ct);
                _uow.Set<MfMaintenancePart>().RemoveRange(old);
                foreach (var g in dto.Parts.GroupBy(p => p.ItemId))
                    _uow.Set<MfMaintenancePart>().Add(new MfMaintenancePart { OrderId = order.Id, ItemId = g.Key, Quantity = g.Sum(p => p.Quantity), Note = Clean(g.First().Note) });
            }
            if (dto.Task != null) order.Task = Clean(dto.Task);
            if (dto.DueDate.HasValue) order.DueDate = dto.DueDate.Value.Date;
            if (dto.EstimatedHours.HasValue) order.EstimatedHours = dto.EstimatedHours.Value;
            if (dto.Checklist != null) order.Checklist = Clean(dto.Checklist);
            if (costCenter != null) order.CostCenter = costCenter;
            if (dto.DowntimeStart.HasValue) order.DowntimeStart = dto.DowntimeStart;
            if (dto.DowntimeEnd.HasValue) order.DowntimeEnd = dto.DowntimeEnd;
            if (dto.Cause != null) order.Cause = Clean(dto.Cause);
            if (dto.ActionTaken != null) order.ActionTaken = Clean(dto.ActionTaken);
            order.ModifyDate = DateTimeOffset.Now;
            order.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<MaintenanceOrderDto>(MfText.Concurrency);
            }
            return ResponseDtoHelper.Ok(await OrderDtoAsync(order, ct));
        }

        public async Task<ResponseDto<MaintenanceOrderDto>> ActionAsync(MaintenanceOrderActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(WriteRoles)) return Bad<MaintenanceOrderDto>(MfText.NotAllowed);
            if (dto == null) return Bad<MaintenanceOrderDto>(MfText.InvalidData);
            MfMaintenanceOrder order = await _uow.Set<MfMaintenanceOrder>().FirstOrDefaultAsync(o => o.Id == dto.Id && !o.Deleted, ct);
            if (order == null) return MfText.NotFound<MaintenanceOrderDto>("دستور نگهداری پیدا نشد.");
            if (!MfConcurrency.Apply(_uow, order, dto.RowVersion)) return Bad<MaintenanceOrderDto>(MfText.InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (!IsOpen(order)) return Bad<MaintenanceOrderDto>("دستور نگهداری بسته شده است.");
            string machineTitle = await _uow.Set<Machine>().AsNoTracking().Where(m => m.Id == order.MachineId).Select(m => m.Title).FirstOrDefaultAsync(ct);
            DateTimeOffset stamp = DateTimeOffset.Now;
            MfMaintenanceRequest request = order.RequestId.HasValue ? await _uow.Set<MfMaintenanceRequest>().FirstOrDefaultAsync(r => r.Id == order.RequestId.Value, ct) : null;
            switch (action)
            {
                case "start":
                    if (order.Status != MfMaintenanceOrderStatus.Open) return Bad<MaintenanceOrderDto>("کار نگهداری قبلا شروع شده است.");
                    order.Status = MfMaintenanceOrderStatus.InProgress;
                    order.StartedAt = stamp;
                    order.StartedByUserId = Me;
                    if (request != null && request.Status == MfMaintenanceRequestStatus.Open) { request.Status = MfMaintenanceRequestStatus.InProgress; request.ModifyDate = stamp; request.ModifierUserId = Me; }
                    break;
                case "complete":
                {
                    string problem = CompleteProblem(order, dto);
                    if (problem != null) return Bad<MaintenanceOrderDto>(problem);
                    DateTime end = dto.DowntimeEnd ?? order.DowntimeEnd ?? Now();
                    if (order.DowntimeStart.HasValue) order.DowntimeEnd = end;
                    order.Status = MfMaintenanceOrderStatus.Done;
                    order.StartedAt ??= stamp;
                    order.StartedByUserId ??= Me;
                    order.DoneAt = stamp;
                    order.DoneByUserId = Me;
                    if (request != null) { request.Status = MfMaintenanceRequestStatus.Done; request.ModifyDate = stamp; request.ModifierUserId = Me; }
                    if (order.PlanId.HasValue)
                    {
                        MfMaintenancePlan plan = await _uow.Set<MfMaintenancePlan>().FirstOrDefaultAsync(p => p.Id == order.PlanId.Value, ct);
                        if (plan != null)
                        {
                            plan.LastDoneAt = stamp.LocalDateTime.Date;
                            plan.NextDueDate = NextDue(plan);
                            plan.ModifyDate = stamp;
                            plan.ModifierUserId = Me;
                        }
                    }
                    await ReleaseMachineAsync(order, ct);
                    break;
                }
                case "cancel":
                {
                    string reason = Clean(dto.Reason);
                    if (reason == null) return Bad<MaintenanceOrderDto>("دلیل لغو را بنویسید.");
                    order.Status = MfMaintenanceOrderStatus.Cancelled;
                    order.CancelReason = reason;
                    if (request != null) { request.Status = MfMaintenanceRequestStatus.Cancelled; request.ModifyDate = stamp; request.ModifierUserId = Me; }
                    await ReleaseMachineAsync(order, ct);
                    break;
                }
                case "request-parts":
                {
                    ResponseDto<MaintenanceOrderDto> failed = await RequestPartsAsync(order, stamp, ct);
                    if (failed != null) return failed;
                    break;
                }
                case "draft-issue":
                {
                    ResponseDto<MaintenanceOrderDto> failed = await DraftIssueAsync(order, ct);
                    if (failed != null) return failed;
                    break;
                }
                default:
                    return Bad<MaintenanceOrderDto>("عمل نامعتبر است؛ یکی از start، complete، cancel، request-parts یا draft-issue را بفرستید.");
            }
            order.ModifyDate = stamp;
            order.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<MaintenanceOrderDto>(MfText.Concurrency);
            }
            await MfGates.MaintenanceOrderAsync(_notify, order, machineTitle, ct);
            if (request != null) await MfGates.MaintenanceAsync(_notify, request, machineTitle, ct);
            return ResponseDtoHelper.Ok(await OrderDtoAsync(order, ct));
        }

        private static string CompleteProblem(MfMaintenanceOrder order, MaintenanceOrderActionDto dto)
        {
            if (order.Kind == MfMaintenanceKind.Breakdown && Clean(order.Cause) == null) return "علت خرابی را بنویسید (در ویرایش دستور) و بعد دستور را ببندید.";
            if (Clean(order.ActionTaken) == null) return "اقدام انجام‌شده را بنویسید (در ویرایش دستور) و بعد دستور را ببندید.";
            DateTime? start = order.DowntimeStart;
            DateTime? end = dto.DowntimeEnd ?? order.DowntimeEnd;
            if (start.HasValue && end.HasValue && end.Value < start.Value) return "پایان توقف باید بعد از شروع توقف باشد.";
            return null;
        }

        private async Task<ResponseDto<MaintenanceOrderDto>> RequestPartsAsync(MfMaintenanceOrder order, DateTimeOffset stamp, CancellationToken ct)
        {
            if (order.MaterialRequestId.HasValue) return Bad<MaintenanceOrderDto>("درخواست کالای قطعات قبلا ساخته شده است.");
            List<MfMaintenancePart> parts = await _uow.Set<MfMaintenancePart>().Where(p => p.OrderId == order.Id).OrderBy(p => p.Id).ToListAsync(ct);
            if (parts.Count == 0) return Bad<MaintenanceOrderDto>("فهرست قطعات خالی است؛ قطعات مورد نیاز را در ویرایش دستور وارد کنید.");
            DateTime today = DateTime.Today;
            var request = new MaterialRequest
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, InventoryNumbers.MaterialRequest, "MR", today, ct), RequestDate = today, Status = MaterialRequestStatus.Draft,
                Purpose = $"قطعات یدکی دستور نگهداری {order.Number}", CreateDate = stamp, ModifyDate = stamp, CreatorUserId = Me, ModifierUserId = Me, Lines = new List<MaterialRequestLine>(),
            };
            int row = 1;
            foreach (MfMaintenancePart p in parts) request.Lines.Add(new MaterialRequestLine { RowNumber = row++, ItemId = p.ItemId, Quantity = p.Quantity, Description = $"دستور نگهداری {order.Number}" });
            _uow.Set<MaterialRequest>().Add(request);
            await _uow.SaveChangesAsync(ct);
            order.MaterialRequestId = request.Id;
            return null;
        }

        private async Task<ResponseDto<MaintenanceOrderDto>> DraftIssueAsync(MfMaintenanceOrder order, CancellationToken ct)
        {
            if (!order.MaterialRequestId.HasValue) return Bad<MaintenanceOrderDto>("ابتدا درخواست کالای قطعات را بسازید (request-parts).");
            if (order.IssueDocumentId.HasValue) return Bad<MaintenanceOrderDto>("حواله قطعات قبلا پیش‌نویس شده است.");
            if (Clean(order.CostCenter) == null) return Bad<MaintenanceOrderDto>("مرکز هزینه نگهداری مشخص نیست؛ در ویرایش دستور مرکز هزینه را وارد کنید.");
            MaterialRequest request = await _uow.Set<MaterialRequest>().AsNoTracking().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == order.MaterialRequestId.Value && !r.Deleted, ct);
            if (request == null) return MfText.NotFound<MaintenanceOrderDto>("درخواست کالا پیدا نشد.");
            if (request.Status != MaterialRequestStatus.Approved)
                return Bad<MaintenanceOrderDto>($"درخواست کالای {request.Number} هنوز تایید نهایی (برنامه‌ریزی و تولید) نشده است؛ حواله بعد از تایید ساخته می‌شود.");
            List<MaterialRequestLine> left = request.Lines.Where(l => l.Quantity > l.IssuedQuantity).ToList();
            if (left.Count == 0) return Bad<MaintenanceOrderDto>("همه اقلام این درخواست قبلا حواله شده است.");
            WarehouseKind[] kinds = { WarehouseKind.Parts, WarehouseKind.General, WarehouseKind.RawMaterial };
            int? warehouseId = request.WarehouseId;
            if (!warehouseId.HasValue)
            {
                List<Warehouse> warehouses = await _uow.Set<Warehouse>().AsNoTracking().Where(w => w.IsActive && !w.Deleted && kinds.Contains(w.Kind)).OrderBy(w => w.Id).ToListAsync(ct);
                warehouseId = kinds.Select(k => warehouses.FirstOrDefault(w => w.Kind == k)?.Id).FirstOrDefault(i => i.HasValue);
            }
            if (!warehouseId.HasValue) return Bad<MaintenanceOrderDto>("انبار قطعات یدکی پیدا نشد؛ در درخواست کالا انبار را مشخص کنید.");
            ResponseDto<DocumentDto> saved = await _documents.SaveDraftAsync(new SaveDocumentDto
            {
                Type = InventoryDocumentType.Issue, DocumentDate = DateTime.Today, WarehouseId = warehouseId.Value, MaterialRequestId = request.Id, ReferenceNumber = order.Number, CostCenter = order.CostCenter,
                Description = $"قطعات یدکی دستور نگهداری {order.Number}",
                Lines = left.Select(l => new SaveDocumentLineDto { ItemId = l.ItemId, Quantity = l.Quantity - l.IssuedQuantity, Description = $"دستور نگهداری {order.Number}" }).ToList(),
            }, ct);
            if (saved == null || !saved.Success || saved.Data == null) return Bad<MaintenanceOrderDto>("حواله قطعات ساخته نشد: " + (saved?.Message ?? "خطای نامشخص"));
            order.IssueDocumentId = saved.Data.Id;
            return null;
        }

        public async Task<ResponseDto<MaintenanceOrderDto>> AddLabourAsync(AddMaintenanceLabourDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(WriteRoles)) return Bad<MaintenanceOrderDto>(MfText.NotAllowed);
            if (dto == null || dto.Hours <= 0 || dto.Hours > 24) return Bad<MaintenanceOrderDto>("ساعت کار باید بین صفر و ۲۴ باشد.");
            MfMaintenanceOrder order = await _uow.Set<MfMaintenanceOrder>().FirstOrDefaultAsync(o => o.Id == dto.OrderId && !o.Deleted, ct);
            if (order == null) return MfText.NotFound<MaintenanceOrderDto>("دستور نگهداری پیدا نشد.");
            if (!IsOpen(order)) return Bad<MaintenanceOrderDto>("دستور نگهداری بسته شده است.");
            int employeeId;
            if (dto.EmployeeId.HasValue)
            {
                if (!await _uow.Set<FnEmployee>().AnyAsync(e => e.Id == dto.EmployeeId.Value && e.IsActive, ct)) return MfText.NotFound<MaintenanceOrderDto>("پرونده پرسنلی پیدا نشد.");
                employeeId = dto.EmployeeId.Value;
            }
            else
            {
                FnEmployee mine = await MfText.EmployeeOfUserAsync(_uow, Me, ct);
                if (mine == null) return Bad<MaintenanceOrderDto>("برای شما پرونده پرسنلی ثبت نشده است؛ با واحد منابع انسانی هماهنگ کنید.");
                employeeId = mine.Id;
            }
            DateTime day = (dto.WorkDate ?? DateTime.Today).Date;
            if (day > DateTime.Today) return Bad<MaintenanceOrderDto>("تاریخ کار نمی‌تواند در آینده باشد.");
            DateTimeOffset stamp = DateTimeOffset.Now;
            _uow.Set<MfMaintenanceLabour>().Add(new MfMaintenanceLabour { OrderId = order.Id, EmployeeId = employeeId, WorkDate = day, Hours = dto.Hours, Note = Clean(dto.Note), CreateDate = stamp, ModifyDate = stamp, CreatorUserId = Me, ModifierUserId = Me });
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await OrderDtoAsync(order, ct));
        }

        // ================================================================================== KPIs

        private static (DateTime From, DateTime To) Period(KpiQuery q)
        {
            DateTime to = q?.To?.Date ?? DateTime.Today;
            DateTime from = q?.From?.Date ?? to.AddDays(-29);
            return from > to ? (to, from) : (from, to);
        }

        public async Task<ResponseDto<PagedList<MachineKpiDto>>> GetMachineKpisAsync(KpiQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Readers)) return Bad<PagedList<MachineKpiDto>>(MfText.NotAllowed);
            query ??= new KpiQuery();
            (DateTime from, DateTime to) = Period(query);
            List<MachineKpiDto> all = await MfKpi.MachinesAsync(_uow, from, to, query.WorkCenterId, query.MachineId, ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            return ResponseDtoHelper.Ok(await MfPaging.OfAsync(all.Skip((number - 1) * size).Take(size).ToList(), all.Count, number, size, ct));
        }

        public async Task<ResponseDto<List<OeeDto>>> GetOeeAsync(KpiQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Readers)) return Bad<List<OeeDto>>(MfText.NotAllowed);
            query ??= new KpiQuery();
            (DateTime from, DateTime to) = Period(query);
            return ResponseDtoHelper.Ok(await MfKpi.OeeAsync(_uow, from, to, query.WorkCenterId, ct));
        }
    }

    /// <summary>Availability, MTBF, MTTR and OEE. Planned time is the working days of the work-centre calendar x its shift hours; downtime is counted on working days only, at most one shift a day.</summary>
    internal static class MfKpi
    {
        public static decimal Pct(decimal part, decimal whole) => whole <= 0 ? 0 : Math.Round(part / whole * 100m, 1, MidpointRounding.AwayFromZero);

        /// <summary>Planned hours of one resource of the centre in the period.</summary>
        public static decimal PlannedHours(MfCenter c, DateTime from, DateTime to)
        {
            decimal hours = 0;
            for (DateTime d = from.Date; d <= to.Date; d = d.AddDays(1)) if (c.Calendar.IsWorking(d)) hours += c.ShiftHours;
            return hours;
        }

        /// <summary>Hours a downtime interval takes from the working time of the period: per working day the overlap with the shift window of that day, at most one shift.</summary>
        public static decimal DowntimeHours(MfCenter c, DateTime start, DateTime? end, DateTime from, DateTime to)
        {
            DateTime stop = end ?? to.Date.AddDays(1);
            decimal hours = 0;
            for (DateTime d = (start.Date < from.Date ? from.Date : start.Date); d <= to.Date && d <= stop.Date; d = d.AddDays(1))
            {
                if (!c.Calendar.IsWorking(d)) continue;
                hours += Math.Min(c.ShiftHours, c.ShiftOverlapHours(d, start, stop));
            }
            return Math.Round(hours, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// R1p D29: the OEE after ISO 22400-2 (see <see cref="MfOeeMath"/>): planned busy time = planned time less planned maintenance; availability = run time / planned busy time; performance = ideal cycle time x
        /// produced count / run time; quality = good / produced. The run time is the booked run hours (hours of people, so two people on one machine book twice - the product of the three parts is still right).
        /// The figure of the earlier releases (availability from downtime only) stays one release as <c>OeeLegacy</c>.
        /// </summary>
        public static OeeDto Compute(OeeDto o)
        {
            o.RunHours = o.BookedRunHours;
            MfOeeMath.Result r = MfOeeMath.Compute(new MfOeeMath.Input
            {
                PlannedHours = o.PlannedHours, PlannedMaintenanceHours = o.PlannedMaintenanceHours, BreakdownHours = o.BreakdownHours, RunHours = o.BookedRunHours, SetupHours = o.SetupHours, WaitingHours = o.WaitingHours,
                OtherBookedHours = o.OtherBookedHours, StandardHours = o.StandardHours, GoodQty = o.GoodQty, ScrapQty = o.ScrapQty,
            });
            o.PlannedBusyHours = r.PlannedBusyHours;
            o.IdleHours = r.IdleHours;
            o.AvailabilityPercent = r.AvailabilityPercent;
            o.PerformancePercent = r.PerformancePercent;
            o.QualityPercent = r.QualityPercent;
            o.OeePercent = r.OeePercent;
            o.OeeLegacy = MfOeeMath.Legacy(o.PlannedHours, o.DowntimeHours, o.BookedRunHours, o.StandardHours, o.GoodQty, o.ScrapQty);
            return o;
        }

        /// <summary>The OEE of several work centres together: the hours and the pieces are added, then the same formula runs once.</summary>
        public static OeeDto Total(IEnumerable<OeeDto> rows)
        {
            List<OeeDto> list = rows.ToList();
            return Compute(new OeeDto
            {
                PlannedHours = list.Sum(o => o.PlannedHours), DowntimeHours = list.Sum(o => o.DowntimeHours), PlannedMaintenanceHours = list.Sum(o => o.PlannedMaintenanceHours), BreakdownHours = list.Sum(o => o.BreakdownHours),
                BookedRunHours = list.Sum(o => o.BookedRunHours), SetupHours = list.Sum(o => o.SetupHours), WaitingHours = list.Sum(o => o.WaitingHours), OtherBookedHours = list.Sum(o => o.OtherBookedHours),
                StandardHours = list.Sum(o => o.StandardHours), GoodQty = list.Sum(o => o.GoodQty), ScrapQty = list.Sum(o => o.ScrapQty),
            });
        }

        public static async Task<List<MachineKpiDto>> MachinesAsync(IUnitOfWork uow, DateTime from, DateTime to, int? workCenterId, int? machineId, CancellationToken ct)
        {
            IQueryable<MfMachineInfo> iq = uow.Set<MfMachineInfo>().AsNoTracking().Where(i => !i.Deleted && i.WorkCenterId != null);
            if (workCenterId.HasValue) iq = iq.Where(i => i.WorkCenterId == workCenterId.Value);
            if (machineId.HasValue) iq = iq.Where(i => i.MachineId == machineId.Value);
            List<MfMachineInfo> infos = await iq.OrderBy(i => i.MachineId).ToListAsync(ct);
            if (infos.Count == 0) return new List<MachineKpiDto>();
            List<int> wcIds = infos.Select(i => i.WorkCenterId.Value).Distinct().ToList();
            List<MfWorkCenter> wcs = await uow.Set<MfWorkCenter>().AsNoTracking().Where(c => wcIds.Contains(c.Id)).ToListAsync(ct);
            Dictionary<int, MfCenter> centers = await MfScheduler.BuildCentersAsync(uow, wcs, ct);
            List<int> machineIds = infos.Select(i => i.MachineId).ToList();
            Dictionary<int, string> titles = await uow.Set<Machine>().AsNoTracking().Where(m => machineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
            DateTime end = to.Date.AddDays(1);
            List<MfMaintenanceOrder> orders = await uow.Set<MfMaintenanceOrder>().AsNoTracking()
                .Where(o => !o.Deleted && machineIds.Contains(o.MachineId) && o.Status != MfMaintenanceOrderStatus.Cancelled && o.DowntimeStart != null && o.DowntimeStart < end).ToListAsync(ct);
            var list = new List<MachineKpiDto>();
            foreach (MfMachineInfo info in infos)
            {
                MfCenter c = centers[info.WorkCenterId.Value];
                decimal planned = PlannedHours(c, from, to);
                decimal down = 0;
                int breakdowns = 0;
                var repairs = new List<(DateTime Start, DateTime? End)>();
                foreach (MfMaintenanceOrder o in orders.Where(x => x.MachineId == info.MachineId))
                {
                    DateTime? stop = o.DowntimeEnd;
                    if (stop.HasValue && stop.Value < from.Date) continue;
                    decimal h = DowntimeHours(c, o.DowntimeStart.Value, stop, from, to);
                    down += h;
                    if (o.Kind == MfMaintenanceKind.Breakdown && o.DowntimeStart.Value.Date >= from.Date)
                    {
                        breakdowns++;
                        repairs.Add((o.DowntimeStart.Value, stop));
                    }
                }
                down = Math.Min(down, planned);
                decimal up = Math.Max(0, planned - down);
                list.Add(new MachineKpiDto
                {
                    MachineId = info.MachineId, MachineTitle = titles.GetValueOrDefault(info.MachineId), WorkCenterId = c.Wc.Id, WorkCenterCode = c.Wc.Code, PlannedHours = planned, DowntimeHours = down,
                    AvailabilityPercent = planned > 0 ? Pct(up, planned) : (decimal?)null, Breakdowns = breakdowns,
                    MtbfHours = breakdowns > 0 ? Math.Round(up / breakdowns, 2, MidpointRounding.AwayFromZero) : (decimal?)null,
                    // R1p D37: the repair time is the clock time from the stop to the end of the repair (a repair over the night is not cut to the shift hours); only the breakdowns that started in the period and were repaired by its end
                    MttrHours = MfOeeMath.Mttr(repairs, from, end),
                });
            }
            return list;
        }

        public static async Task<List<OeeDto>> OeeAsync(IUnitOfWork uow, DateTime from, DateTime to, int? workCenterId, CancellationToken ct)
        {
            IQueryable<MfWorkCenter> wq = uow.Set<MfWorkCenter>().AsNoTracking().Where(c => !c.Deleted && c.IsActive);
            if (workCenterId.HasValue) wq = wq.Where(c => c.Id == workCenterId.Value);
            List<MfWorkCenter> wcs = await wq.OrderBy(c => c.Code).ToListAsync(ct);
            if (wcs.Count == 0) return new List<OeeDto>();
            Dictionary<int, MfCenter> centers = await MfScheduler.BuildCentersAsync(uow, wcs, ct);
            List<int> wcIds = wcs.Select(c => c.Id).ToList();
            DateTime end = to.Date.AddDays(1);
            DateTime firstDay = from.Date;
            var machines = await uow.Set<MfMachineInfo>().AsNoTracking().Where(i => !i.Deleted && i.WorkCenterId != null && wcIds.Contains(i.WorkCenterId.Value)).Select(i => new { i.MachineId, WorkCenterId = i.WorkCenterId.Value }).ToListAsync(ct);
            List<int> machineIds = machines.Select(m => m.MachineId).ToList();
            List<MfMaintenanceOrder> orders = machineIds.Count == 0 ? new List<MfMaintenanceOrder>()
                : await uow.Set<MfMaintenanceOrder>().AsNoTracking().Where(o => !o.Deleted && machineIds.Contains(o.MachineId) && o.Status != MfMaintenanceOrderStatus.Cancelled && o.DowntimeStart != null && o.DowntimeStart < end).ToListAsync(ct);
            // R1p D29: every kind of booking is read (setup, waiting, rework and cleaning are loss categories of the planned busy time); only the run bookings are production time and carry the pieces
            var bookings = await (from b in uow.Set<MfTimeBooking>().AsNoTracking()
                                  join o in uow.Set<MfOperation>().AsNoTracking() on b.OperationId equals o.Id
                                  join p in uow.Set<MfProductionOrder>().AsNoTracking() on o.OrderId equals p.Id
                                  where !b.Deleted && b.EndAt != null && b.StartAt >= firstDay && b.StartAt < end && o.WorkCenterId != null && wcIds.Contains(o.WorkCenterId.Value)
                                  select new { WorkCenterId = o.WorkCenterId.Value, b.Kind, b.Hours, b.GoodQty, b.ScrapQty, o.OperationTemplateId, o.RunHoursPlanned, OrderQty = p.Quantity }).ToListAsync(ct);
            List<int> templateIds = bookings.Where(b => b.Kind == MfBookingKind.Run && b.OperationTemplateId.HasValue).Select(b => b.OperationTemplateId.Value).Distinct().ToList();
            Dictionary<int, decimal> standard = templateIds.Count == 0 ? new Dictionary<int, decimal>()
                : await uow.Set<MfOperationStandard>().AsNoTracking().Where(s => !s.Deleted && templateIds.Contains(s.OperationTemplateId)).ToDictionaryAsync(s => s.OperationTemplateId, s => s.RunHoursPerUnit, ct);
            var result = new List<OeeDto>();
            foreach (MfWorkCenter wc in wcs)
            {
                MfCenter c = centers[wc.Id];
                var dto = new OeeDto { WorkCenterId = wc.Id, WorkCenterCode = wc.Code, WorkCenterTitle = wc.Title };
                dto.PlannedHours = PlannedHours(c, from, to) * Math.Max(wc.ParallelResources, 1);
                int machineCount = machines.Count(x => x.WorkCenterId == wc.Id);
                decimal share = MfScheduler.MachineShare(wc.ParallelResources, machineCount);
                // R1p D29: planned maintenance (a planned order with a downtime window) is taken out of the planned busy time; a breakdown is a loss inside it. Both stay in DowntimeHours, as before.
                decimal plannedMaintenance = 0, breakdown = 0;
                foreach (var m in machines.Where(x => x.WorkCenterId == wc.Id))
                    foreach (MfMaintenanceOrder o in orders.Where(x => x.MachineId == m.MachineId))
                        if (!(o.DowntimeEnd.HasValue && o.DowntimeEnd.Value < from.Date))
                        {
                            decimal h = DowntimeHours(c, o.DowntimeStart.Value, o.DowntimeEnd, from, to) * share;
                            if (o.Kind == MfMaintenanceKind.Planned) plannedMaintenance += h; else breakdown += h;
                        }
                dto.PlannedMaintenanceHours = Math.Round(Math.Min(plannedMaintenance, dto.PlannedHours), 2, MidpointRounding.AwayFromZero);
                dto.BreakdownHours = Math.Round(Math.Min(breakdown, dto.PlannedHours - dto.PlannedMaintenanceHours), 2, MidpointRounding.AwayFromZero);
                dto.DowntimeHours = dto.PlannedMaintenanceHours + dto.BreakdownHours;
                foreach (var b in bookings.Where(x => x.WorkCenterId == wc.Id))
                {
                    if (b.Kind == MfBookingKind.Run)
                    {
                        decimal perPiece = b.OperationTemplateId.HasValue && standard.TryGetValue(b.OperationTemplateId.Value, out decimal s) && s > 0 ? s : (b.OrderQty > 0 ? b.RunHoursPlanned / b.OrderQty : 0);
                        dto.BookedRunHours += b.Hours;
                        dto.GoodQty += b.GoodQty;
                        dto.ScrapQty += b.ScrapQty;
                        dto.StandardHours += perPiece * (b.GoodQty + b.ScrapQty);
                    }
                    else if (b.Kind == MfBookingKind.Setup) dto.SetupHours += b.Hours;
                    else if (b.Kind == MfBookingKind.Waiting) dto.WaitingHours += b.Hours;
                    else dto.OtherBookedHours += b.Hours; // rework and cleaning
                }
                dto.StandardHours = Math.Round(dto.StandardHours, 2, MidpointRounding.AwayFromZero);
                result.Add(Compute(dto));
            }
            return result;
        }
    }
}
