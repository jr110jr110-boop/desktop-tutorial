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
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.OfficeDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;
using DocumentDto = ViewModels.NasbNiro.InventoryDtos.DocumentDto;
using SaveDocumentDto = ViewModels.NasbNiro.InventoryDtos.SaveDocumentDto;
using SaveDocumentLineDto = ViewModels.NasbNiro.InventoryDtos.SaveDocumentLineDto;

namespace Services.NasbNiro.Production
{
    public interface IMfShopFloorService
    {
        Task<ResponseDto<PagedList<TerminalOperationDto>>> MyOperationsAsync(TerminalQuery query, CancellationToken ct);
        Task<ResponseDto<PagedList<TerminalOperationDto>>> ScanAsync(TerminalQuery query, CancellationToken ct);
        Task<ResponseDto<TerminalOperationDto>> StartAsync(StartOperationDto dto, CancellationToken ct);
        Task<ResponseDto<TerminalOperationDto>> PauseAsync(PauseOperationDto dto, CancellationToken ct);
        Task<ResponseDto<TerminalOperationDto>> ReportQuantityAsync(ReportQuantityDto dto, CancellationToken ct);
        Task<ResponseDto<TerminalOperationDto>> FinishAsync(FinishOperationDto dto, CancellationToken ct);
        Task<ResponseDto<TerminalOperationDto>> RequestQcAsync(RequestQcDto dto, CancellationToken ct);
        Task<ResponseDto<MaintenanceRequestDto>> MachineDownAsync(MachineDownDto dto, CancellationToken ct);
        Task<ResponseDto<PagedList<MaintenanceRequestDto>>> GetMaintenanceRequestsAsync(MaintenanceRequestQuery query, CancellationToken ct);

        Task<ResponseDto<PagedList<BookingDto>>> GetBookingsAsync(BookingQuery query, CancellationToken ct);
        Task<ResponseDto<BookingDto>> CreateBookingAsync(SaveBookingDto dto, CancellationToken ct);
        Task<ResponseDto<BookingDto>> EditBookingAsync(EditBookingDto dto, CancellationToken ct);

        /// <summary>R1p D39: an extra booking on an operation of a completed order (its own bookings are frozen), by the shop supervisor with a reason.</summary>
        Task<ResponseDto<BookingDto>> CorrectBookingAsync(SaveCorrectionBookingDto dto, CancellationToken ct);

        /// <summary>R1p D39: a production manager other than the creator approves (or rejects) a correction booking; before that it counts in no cost run, hour report or OEE.</summary>
        Task<ResponseDto<BookingDto>> ApproveCorrectionAsync(ApproveCorrectionDto dto, CancellationToken ct);

        /// <summary>R1p D1: booked hours per work centre and day without any name (the readers who may not see names use this).</summary>
        Task<ResponseDto<List<BookingHoursDto>>> GetBookingHoursAsync(BookingHoursQuery query, CancellationToken ct);

        Task<ResponseDto<PagedList<AttendanceDifferenceDto>>> GetAttendanceDifferencesAsync(AttendanceDifferenceQuery query, CancellationToken ct);
    }

    /// <summary>The shop-floor terminal (scan, start, pause, finish, quantities, QC request, machine down), the time bookings and the attendance check.</summary>
    public class MfShopFloorService : IMfShopFloorService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IAttendanceService _attendance;
        private readonly INotificationService _notify;
        private readonly IInventoryDocumentService _documents;
        private readonly IConfiguration _configuration;
        private readonly MfFlow _flow;

        /// <summary>The days a correction booking may reach back (R1p D39, ★ default 7).</summary>
        public const string MaxBackdateKey = "Production:MaxBackdateDays";
        public const int MaxBackdateDefault = 7;

        public MfShopFloorService(IUnitOfWork uow, IInventoryUserContext user, IInventoryDocumentService documents, IAttendanceService attendance, INotificationService notify = null, IConfiguration configuration = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _attendance = attendance ?? throw new ArgumentNullException(nameof(attendance));
            _notify = notify;
            _documents = documents;
            _configuration = configuration;
            _flow = new MfFlow(uow, user, documents, notify);
        }

        private int MaxBackdateDays => int.TryParse(_configuration?[MaxBackdateKey], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int days) && days >= 0 ? days : MaxBackdateDefault;

        private int Me => _user.UserId;
        private static string Clean(string s) => MfText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);
        private static string Join(IEnumerable<string> parts) => string.Join("؛ ", parts);
        private const string NoEmployee = "برای شما پرونده پرسنلی ثبت نشده است؛ با واحد منابع انسانی هماهنگ کنید.";

        private static PagedList<T> AsPage<T>(PagedList<T> all, int total, int number, int size)
        {
            all.TotalCount = total;
            all.CurrentPage = number;
            all.PageSize = size;
            all.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return all;
        }

        private static DateTime Now()
        {
            DateTime n = DateTime.Now;
            return new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second, n.Kind);
        }

        private static int MinutesBetween(DateTime start, DateTime end) => (int)Math.Max(0, Math.Round((end - start).TotalMinutes, MidpointRounding.AwayFromZero));

        private static void Close(MfTimeBooking b, DateTime end)
        {
            b.EndAt = end;
            b.Minutes = MinutesBetween(b.StartAt, end);
            b.Hours = MfText.HoursOf(b.Minutes);
            b.ModifyDate = DateTimeOffset.Now;
        }

        // ================================================================================== terminal lists

        private async Task<List<TerminalOperationDto>> TerminalDtosAsync(List<MfOperation> ops, int? employeeId, CancellationToken ct)
        {
            List<OperationDto> baseDtos = await MfMapping.OperationDtosAsync(_uow, ops, ct);
            List<int> orderIds = ops.Select(o => o.OrderId).Distinct().ToList();
            List<MfProductionOrder> orders = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToListAsync(ct);
            List<int> itemIds = orders.Where(o => o.ItemId.HasValue).Select(o => o.ItemId.Value).Distinct().ToList();
            var items = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title }).ToListAsync(ct);
            Dictionary<int, MfFlow.Urgency> urgency = await MfFlow.UrgencyAsync(_uow, ct);
            List<int> opIds = ops.Select(o => o.Id).ToList();
            // R1p D43: the work centres that make the machine mandatory at the terminal (they require one and still have a machine that is not retired)
            List<int> centerIds = ops.Where(o => o.WorkCenterId.HasValue).Select(o => o.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, bool> requiresMachine = centerIds.Count == 0 ? new Dictionary<int, bool>()
                : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.RequiresMachine, ct);
            HashSet<int> withMachines = centerIds.Count == 0 ? new HashSet<int>()
                : (await _uow.Set<MfMachineInfo>().AsNoTracking().Where(i => !i.Deleted && i.WorkCenterId != null && centerIds.Contains(i.WorkCenterId.Value) && i.Status != MfMachineStatus.Retired)
                    .Select(i => i.WorkCenterId.Value).Distinct().ToListAsync(ct)).ToHashSet();
            HashSet<int> booked = employeeId.HasValue
                ? (await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.EmployeeId == employeeId.Value && opIds.Contains(b.OperationId)).Select(b => b.OperationId).Distinct().ToListAsync(ct)).ToHashSet()
                : new HashSet<int>();
            var mine = employeeId.HasValue
                ? await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.EndAt == null && b.EmployeeId == employeeId.Value && opIds.Contains(b.OperationId)).ToListAsync(ct)
                : new List<MfTimeBooking>();
            return baseDtos.Select(d =>
            {
                MfProductionOrder order = orders.First(o => o.Id == d.OrderId);
                var item = items.FirstOrDefault(i => i.Id == order.ItemId);
                urgency.TryGetValue(order.Id, out MfFlow.Urgency u);
                MfTimeBooking running = mine.FirstOrDefault(b => b.OperationId == d.Id);
                return new TerminalOperationDto
                {
                    Id = d.Id, OrderId = d.OrderId, OrderNumber = d.OrderNumber, JobCard = d.JobCard, Sequence = d.Sequence, Title = d.Title, WorkCenterId = d.WorkCenterId,
                    WorkCenterCode = d.WorkCenterCode, WorkCenterTitle = d.WorkCenterTitle, SetupHoursPlanned = d.SetupHoursPlanned, RunHoursPlanned = d.RunHoursPlanned,
                    PlannedStart = d.PlannedStart, PlannedStartText = d.PlannedStartText, PlannedFinish = d.PlannedFinish, PlannedFinishText = d.PlannedFinishText, Status = d.Status,
                    GoodQty = d.GoodQty, ScrapQty = d.ScrapQty, ReworkQty = d.ReworkQty, InspectionRequirement = d.InspectionRequirement, ItpTestType = d.ItpTestType,
                    ItpActivityId = d.ItpActivityId, QcAccepted = d.QcAccepted, QcRequestedAtText = d.QcRequestedAtText, QcRecordId = d.QcRecordId, QcRecordNumber = d.QcRecordNumber,
                    NcrId = d.NcrId, NcrNumber = d.NcrNumber, IsOutside = d.IsOutside, OutsideSupplierId = d.OutsideSupplierId, PurchaseRequestId = d.PurchaseRequestId,
                    PurchaseRequestNumber = d.PurchaseRequestNumber, PurchaseRequestLineId = d.PurchaseRequestLineId, PurchaseOrderLineId = d.PurchaseOrderLineId,
                    SubcontractIssueId = d.SubcontractIssueId, SubcontractIssueNumber = d.SubcontractIssueNumber, IsRework = d.IsRework, ReworkOfOperationId = d.ReworkOfOperationId,
                    SkipReason = d.SkipReason, StartedAtText = d.StartedAtText, FinishedAtText = d.FinishedAtText, Note = d.Note, RunningBookings = d.RunningBookings, HoursBooked = d.HoursBooked,
                    ItemCode = item?.Code, ItemTitle = item?.Title, OrderQuantity = order.Quantity, Unit = order.Unit, OrderPriority = u != null && u.Priority > 0 ? u.Priority : (int?)null,
                    NeedDateText = MfText.Date(u?.NeedDate), MyRunning = running != null, MyRunningSinceText = MfText.Stamp(running?.StartAt), MyRunningSince = running?.StartAt,
                    ExpectedQty = d.ExpectedQty, IsWelding = d.IsWelding, WpsId = d.WpsId, WpsNumber = d.WpsNumber, WeldLogId = d.WeldLogId, AcceptedByConcession = d.AcceptedByConcession,
                    AcceptanceStale = d.AcceptanceStale,
                    MachineRequired = d.WorkCenterId.HasValue && MfTerminalRules.MachineRequired(requiresMachine.GetValueOrDefault(d.WorkCenterId.Value), withMachines.Contains(d.WorkCenterId.Value) ? 1 : 0, MfBookingKind.Run),
                    HasMyBooking = booked.Contains(d.Id),
                };
            }).ToList();
        }

        private static readonly MfOperationStatus[] Workable = { MfOperationStatus.Ready, MfOperationStatus.InProgress, MfOperationStatus.Paused };

        public async Task<ResponseDto<PagedList<TerminalOperationDto>>> MyOperationsAsync(TerminalQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Terminal)) return Bad<PagedList<TerminalOperationDto>>(MfText.NotAllowed);
            query ??= new TerminalQuery();
            FnEmployee me = await MfText.EmployeeOfUserAsync(_uow, Me, ct);
            IQueryable<MfOperation> q = from o in _uow.Set<MfOperation>().AsNoTracking()
                                        join p in _uow.Set<MfProductionOrder>().AsNoTracking() on o.OrderId equals p.Id
                                        where !o.Deleted && !p.Deleted && (p.Status == MfOrderStatus.Released || p.Status == MfOrderStatus.InProgress) && !o.IsOutside
                                        select o;
            int? employeeId = me?.Id;
            // what I am running now, plus what is ready in the work centre asked for (or all work centres)
            q = q.Where(o => (Workable.Contains(o.Status) && (!query.WorkCenterId.HasValue || o.WorkCenterId == query.WorkCenterId.Value))
                || _uow.Set<MfTimeBooking>().Any(b => !b.Deleted && b.EndAt == null && b.OperationId == o.Id && employeeId != null && b.EmployeeId == employeeId.Value));
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfOperation> rows = await q.OrderBy(o => o.OrderId).ThenBy(o => o.Sequence).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            List<TerminalOperationDto> dtos = await TerminalDtosAsync(rows, employeeId, ct);
            // most urgent order first, my running work on top
            dtos = dtos.OrderByDescending(d => d.MyRunning).ThenBy(d => d.OrderPriority ?? int.MaxValue).ThenBy(d => d.OrderId).ThenBy(d => d.Sequence).ToList();
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(dtos, 1, int.MaxValue, ct), total, number, size));
        }

        public async Task<ResponseDto<PagedList<TerminalOperationDto>>> ScanAsync(TerminalQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Terminal)) return Bad<PagedList<TerminalOperationDto>>(MfText.NotAllowed);
            string code = Clean(query?.Code);
            if (code == null) return Bad<PagedList<TerminalOperationDto>>("کد برگه کار یا شماره سریال را وارد کنید.");
            FnEmployee me = await MfText.EmployeeOfUserAsync(_uow, Me, ct);
            var opIds = new List<int>();

            // job card: order number or order number / sequence (the -R suffix is the rework operation)
            string orderPart = code, sequencePart = null;
            int slash = code.LastIndexOf('/');
            if (slash > 0) { orderPart = code.Substring(0, slash); sequencePart = code.Substring(slash + 1); }
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Number == orderPart && !o.Deleted, ct);
            if (order != null)
            {
                IQueryable<MfOperation> ops = _uow.Set<MfOperation>().AsNoTracking().Where(o => o.OrderId == order.Id && !o.Deleted);
                if (sequencePart != null)
                {
                    bool rework = sequencePart.EndsWith("-R", StringComparison.OrdinalIgnoreCase);
                    string digits = rework ? sequencePart.Substring(0, sequencePart.Length - 2) : sequencePart;
                    if (int.TryParse(digits, out int sequence)) ops = ops.Where(o => o.Sequence == sequence && o.IsRework == rework);
                }
                opIds = await ops.Select(o => o.Id).ToListAsync(ct);
            }
            else
            {
                // serial number or lot number of a part on the floor
                List<int> units = await _uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && u.SerialNumber == code).Select(u => u.Id).ToListAsync(ct);
                List<int> lots = await _uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && l.LotNumber == code).Select(l => l.Id).ToListAsync(ct);
                opIds = await (from l in _uow.Set<MfOperationPart>().AsNoTracking()
                               join p in _uow.Set<PartOperation>().AsNoTracking() on l.PartOperationId equals p.Id
                               where (p.SerialUnitId != null && units.Contains(p.SerialUnitId.Value)) || (p.LotId != null && lots.Contains(p.LotId.Value))
                               select l.OperationId).Distinct().ToListAsync(ct);
            }
            if (opIds.Count == 0) return MfText.NotFound<PagedList<TerminalOperationDto>>("برگه کار یا سریالی با این کد پیدا نشد.");
            IQueryable<MfOperation> found = _uow.Set<MfOperation>().AsNoTracking().Where(o => opIds.Contains(o.Id) && !o.Deleted);
            int total = await found.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfOperation> rows = await found.OrderBy(o => o.OrderId).ThenBy(o => o.Sequence).ThenBy(o => o.IsRework ? 1 : 0).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(await TerminalDtosAsync(rows, me?.Id, ct), 1, int.MaxValue, ct), total, number, size));
        }

        // ================================================================================== terminal actions

        private sealed class Loaded
        {
            public MfOperation Op;
            public MfProductionOrder Order;
            public List<MfOperation> All;
            public FnEmployee Employee;
            public string Error;
        }

        private async Task<Loaded> LoadAsync(int operationId, bool needEmployee, CancellationToken ct)
        {
            var l = new Loaded();
            l.Op = await _uow.Set<MfOperation>().FirstOrDefaultAsync(o => o.Id == operationId && !o.Deleted, ct);
            if (l.Op == null) { l.Error = "عملیات پیدا نشد."; return l; }
            l.Order = await _uow.Set<MfProductionOrder>().FirstAsync(o => o.Id == l.Op.OrderId, ct);
            if (l.Order.Status != MfOrderStatus.Released && l.Order.Status != MfOrderStatus.InProgress) { l.Error = "دستور تولید این عملیات در حال اجرا نیست."; return l; }
            l.All = await _uow.Set<MfOperation>().Where(o => o.OrderId == l.Order.Id && !o.Deleted).ToListAsync(ct);
            if (needEmployee)
            {
                l.Employee = await MfText.EmployeeOfUserAsync(_uow, Me, ct);
                if (l.Employee == null) l.Error = NoEmployee;
            }
            return l;
        }

        private async Task<TerminalOperationDto> OneAsync(MfOperation op, int? employeeId, CancellationToken ct) => (await TerminalDtosAsync(new List<MfOperation> { op }, employeeId, ct))[0];

        private async Task<MfTimeBooking> RunningOfAsync(int employeeId, CancellationToken ct) =>
            await _uow.Set<MfTimeBooking>().FirstOrDefaultAsync(b => !b.Deleted && b.EmployeeId == employeeId && b.EndAt == null, ct);

        private async Task<bool> OtherRunningAsync(int operationId, int exceptBookingId, CancellationToken ct) =>
            await _uow.Set<MfTimeBooking>().AnyAsync(b => !b.Deleted && b.OperationId == operationId && b.EndAt == null && b.Id != exceptBookingId, ct);

        public async Task<ResponseDto<TerminalOperationDto>> StartAsync(StartOperationDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Terminal)) return Bad<TerminalOperationDto>(MfText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(MfBookingKind), dto.Kind)) return Bad<TerminalOperationDto>(MfText.InvalidData);
            Loaded l = await LoadAsync(dto.OperationId, true, ct);
            if (l.Error != null) return Bad<TerminalOperationDto>(l.Error);
            if (l.Op.IsOutside) return Bad<TerminalOperationDto>("عملیات بیرونی روی ترمینال کارگاه شروع نمی‌شود.");

            // the kit may have been issued since the last look
            await _flow.ApplyAsync(l.Order, l.All, ct);
            if (l.Op.Status == MfOperationStatus.Waiting)
                return Bad<TerminalOperationDto>("این مرحله هنوز آماده نیست: مرحله قبل تمام نشده، بازرسی آن پذیرفته نشده یا کیت مواد تحویل نشده است.");
            if (l.Op.Status == MfOperationStatus.QcHold) return Bad<TerminalOperationDto>("این مرحله در انتظار بازرسی کنترل کیفیت است.");
            if (!Workable.Contains(l.Op.Status)) return Bad<TerminalOperationDto>("این مرحله قابل شروع نیست (وضعیت: " + MfText.OperationStatus(l.Op.Status) + ").");

            // R1p D43: a work centre with machines needs the machine chosen at the terminal (machine hours, machine overhead and OEE come from it); a waiting booking needs none
            string machineNeeded = await MachineRequirementAsync(l.Op, dto.Kind, dto.MachineId, ct);
            if (machineNeeded != null) return Bad<TerminalOperationDto>(machineNeeded);
            if (dto.MachineId.HasValue)
            {
                string problem = await CheckMachineAsync(dto.MachineId.Value, l.Op, true, ct);
                if (problem != null) return Bad<TerminalOperationDto>(problem);
            }
            // R1p D12: a welding step starts only with the approved WPS of the step and a welder qualified for its process
            if (l.Op.IsWelding)
            {
                var weld = await MfWelding.CheckStartAsync(_uow, l.Op, l.Employee, dto.Kind, DateTime.Today, ct);
                if (weld.Error != null) return Bad<TerminalOperationDto>(weld.Error);
            }
            MfTimeBooking running = await RunningOfAsync(l.Employee.Id, ct);
            DateTime now = Now();
            if (running != null)
            {
                if (running.OperationId == l.Op.Id && running.Kind == dto.Kind) return Bad<TerminalOperationDto>("این کار هم‌اکنون برای شما در حال اجراست.");
                // starting a new booking stops the previous one
                Close(running, now);
                MfOperation previous = running.OperationId == l.Op.Id ? l.Op : await _uow.Set<MfOperation>().FirstAsync(o => o.Id == running.OperationId, ct);
                if (previous.Status == MfOperationStatus.InProgress && !await OtherRunningAsync(previous.Id, running.Id, ct))
                {
                    previous.Status = MfOperationStatus.Paused;
                    previous.ModifyDate = DateTimeOffset.Now;
                }
                try
                {
                    await _uow.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Bad<TerminalOperationDto>(MfText.Concurrency);
                }
            }

            DateTimeOffset stamp = DateTimeOffset.Now;
            _uow.Set<MfTimeBooking>().Add(new MfTimeBooking
            {
                OperationId = l.Op.Id, EmployeeId = l.Employee.Id, MachineId = dto.MachineId, Kind = dto.Kind, StartAt = now, Source = MfBookingSource.Terminal, Note = Clean(dto.Note),
                CreateDate = stamp, ModifyDate = stamp, CreatorUserId = Me, ModifierUserId = Me,
            });
            l.Op.Status = MfOperationStatus.InProgress;
            l.Op.StartedAt ??= stamp;
            l.Op.ModifyDate = stamp;
            l.Op.ModifierUserId = Me;
            await _flow.ApplyAsync(l.Order, l.All, ct);
            await _flow.PartSyncAsync(l.Op, ct);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<TerminalOperationDto>(MfText.Concurrency);
            }
            catch (DbUpdateException)
            {
                return Bad<TerminalOperationDto>("برای شما ثبت زمانِ در حال اجرای دیگری وجود دارد؛ آن را پایان دهید.");
            }
            await _flow.AfterSaveAsync(ct);
            return ResponseDtoHelper.Ok(await OneAsync(l.Op, l.Employee.Id, ct));
        }

        /// <summary>
        /// R1p D43 (round 3): a work centre with machines to choose from (<see cref="MfWorkCenter.RequiresMachine"/>) needs the machine named wherever time is booked: at the terminal, in a booking a supervisor makes for somebody
        /// else, in an edit and in a correction. A waiting booking needs none. Null = nothing missing; otherwise the Persian reason.
        /// </summary>
        private async Task<string> MachineRequirementAsync(MfOperation op, MfBookingKind kind, int? machineId, CancellationToken ct)
        {
            if (machineId.HasValue || !op.WorkCenterId.HasValue) return null;
            MfWorkCenter centre = await _uow.Set<MfWorkCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == op.WorkCenterId.Value, ct);
            if (centre == null) return null;
            int machines = await _uow.Set<MfMachineInfo>().CountAsync(i => !i.Deleted && i.WorkCenterId == centre.Id && i.Status != MfMachineStatus.Retired, ct);
            return MfTerminalRules.MachineRequired(centre.RequiresMachine, machines, kind)
                ? $"برای مرکز کار {centre.Code} انتخاب ماشین الزامی است؛ ماشین را انتخاب کنید (ساعت ماشین، سربار ماشین و OEE از آن محاسبه می‌شود)."
                : null;
        }

        /// <summary>
        /// The machine of a booking: it exists and belongs to the work centre of the operation. A booking that starts now (terminal) also needs the machine to be active and available;
        /// a booking of past time (a supervisor's booking or a correction) does not, because the machine may have broken down or been retired since (R1p review 9).
        /// </summary>
        private async Task<string> CheckMachineAsync(int machineId, MfOperation op, bool startsNow, CancellationToken ct)
        {
            Machine machine = await _uow.Set<Machine>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == machineId, ct);
            if (machine == null) return "ماشین پیدا نشد.";
            if (startsNow && !machine.IsActive) return "ماشین غیرفعال است.";
            MfMachineInfo info = await _uow.Set<MfMachineInfo>().AsNoTracking().FirstOrDefaultAsync(i => i.MachineId == machineId && !i.Deleted, ct);
            if (startsNow && info != null && info.Status != MfMachineStatus.Available) return $"ماشین {machine.Title} در دسترس نیست ({(info.Status == MfMachineStatus.Down ? "خراب" : info.Status == MfMachineStatus.UnderMaintenance ? "در تعمیر" : "بازنشسته")}).";
            if (info?.WorkCenterId != null && op.WorkCenterId.HasValue && info.WorkCenterId != op.WorkCenterId) return "این ماشین متعلق به مرکز کار این عملیات نیست.";
            return null;
        }

        private async Task<string> CheckReasonAsync(int? id, MfReasonKind[] kinds, CancellationToken ct)
        {
            if (!id.HasValue) return null;
            return await _uow.Set<MfReasonCode>().AnyAsync(r => r.Id == id.Value && !r.Deleted && r.IsActive && kinds.Contains(r.Kind), ct) ? null : "دلیل انتخاب‌شده معتبر نیست.";
        }

        public async Task<ResponseDto<TerminalOperationDto>> PauseAsync(PauseOperationDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Terminal)) return Bad<TerminalOperationDto>(MfText.NotAllowed);
            if (dto == null) return Bad<TerminalOperationDto>(MfText.InvalidData);
            Loaded l = await LoadAsync(dto.OperationId, true, ct);
            if (l.Error != null) return Bad<TerminalOperationDto>(l.Error);
            string reasonProblem = await CheckReasonAsync(dto.ReasonCodeId, new[] { MfReasonKind.Downtime, MfReasonKind.Waiting }, ct);
            if (reasonProblem != null) return Bad<TerminalOperationDto>(reasonProblem);
            MfTimeBooking running = await _uow.Set<MfTimeBooking>().FirstOrDefaultAsync(b => !b.Deleted && b.EmployeeId == l.Employee.Id && b.OperationId == l.Op.Id && b.EndAt == null, ct);
            if (running == null) return Bad<TerminalOperationDto>("برای شما روی این عملیات کاری در حال اجرا نیست.");
            Close(running, Now());
            running.ReasonCodeId = dto.ReasonCodeId;
            if (Clean(dto.Note) != null) running.Note = Clean(dto.Note);
            if (l.Op.Status == MfOperationStatus.InProgress && !await OtherRunningAsync(l.Op.Id, running.Id, ct)) l.Op.Status = MfOperationStatus.Paused;
            l.Op.ModifyDate = DateTimeOffset.Now;
            l.Op.ModifierUserId = Me;
            await _flow.PartSyncAsync(l.Op, ct);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<TerminalOperationDto>(MfText.Concurrency);
            }
            return ResponseDtoHelper.Ok(await OneAsync(l.Op, l.Employee.Id, ct));
        }

        private async Task<string> AddQuantityAsync(Loaded l, MfTimeBooking booking, ReportQuantityDto dto, CancellationToken ct)
        {
            if (dto.GoodQty < 0 || dto.ScrapQty < 0 || dto.ReworkQty < 0) return "تعداد نمی‌تواند منفی باشد.";
            if (dto.GoodQty == 0 && dto.ScrapQty == 0 && dto.ReworkQty == 0) return null;
            if (dto.ScrapQty > 0 && !dto.ScrapReasonCodeId.HasValue) return "برای ضایعات، دلیل ضایعات را انتخاب کنید.";
            string reasonProblem = await CheckReasonAsync(dto.ScrapReasonCodeId, new[] { MfReasonKind.Scrap }, ct);
            if (reasonProblem != null) return reasonProblem;
            decimal expected = ExpectedQuantity(l);
            decimal after = l.Op.GoodQty + l.Op.ScrapQty + dto.GoodQty + dto.ScrapQty;
            if (after > expected) return $"مجموع سالم و ضایعات ({MfFlow.Num(after)}) از تعداد این عملیات ({MfFlow.Num(expected)}) بیشتر می‌شود.";
            l.Op.GoodQty += dto.GoodQty;
            l.Op.ScrapQty += dto.ScrapQty;
            l.Op.ReworkQty += dto.ReworkQty;
            booking.GoodQty += dto.GoodQty;
            booking.ScrapQty += dto.ScrapQty;
            booking.ReworkQty += dto.ReworkQty;
            if (dto.ScrapQty > 0) booking.ScrapReasonCodeId = dto.ScrapReasonCodeId;
            if (Clean(dto.Note) != null) booking.Note = Clean(dto.Note);
            if (dto.ScrapQty > 0) return await QueueScrapAsync(l.Order, l.Op, dto.ScrapQty, dto.ScrapReasonCodeId, dto.ScrapSerials, ct);
            return null;
        }

        /// <summary>R1p D7: what the operation has to report is the good quantity of the operation before it (the first: the order quantity), not the full order quantity again after earlier scrap.</summary>
        private static decimal ExpectedQuantity(Loaded l) => MfQuantityRules.Expected(l.All, l.Op, l.Order.Quantity);

        // ================================================================================== terminal scrap (R1p D33)

        private readonly List<MfWriteOff> _writeOffs = new List<MfWriteOff>();

        /// <summary>
        /// Scrap reported on the shop floor is a write-off like the scrap decision of an NCR (R1p D6/D33): a draft non-conformance report (the lot of the order, or the scrapped serials) is added to the same save as the
        /// quantity, the order's written-off quantity and planned serials follow, a draft replacement order is made, the report is tied to the step, and after the save <see cref="ScrapDocumentsAsync"/> drafts the scrap
        /// receipt of the inventory module (workshop scrap goes to the scrap warehouse). Everything stays a draft: quality classifies and decides the report, the warehouse confirms the receipt, people release the
        /// replacement. ★ provisional decision: one report, one replacement and one receipt per scrap report. Returns the persian reason when the scrap cannot be taken (the serials of a serial-tracked item cannot be told).
        /// </summary>
        private async Task<string> QueueScrapAsync(MfProductionOrder order, MfOperation op, decimal quantity, int? reasonCodeId, string namedSerials, CancellationToken ct)
        {
            if (quantity <= 0) return null;
            string job = MfMapping.JobCard(order.Number, op);
            var w = new MfWriteOff { Order = order, Op = op, Quantity = quantity, Job = job, NcrIsNew = order.ItemId.HasValue };
            if (order.ItemId.HasValue)
            {
                (int? lotId, List<string> _) = await MfTrace.OfOrderAsync(_uow, order, ct);
                string reason = reasonCodeId.HasValue ? await _uow.Set<MfReasonCode>().AsNoTracking().Where(r => r.Id == reasonCodeId.Value).Select(r => r.Title).FirstOrDefaultAsync(ct) : null;
                w.Ncr = await QualityNcrFactory.DraftAsync(_uow, Me, NcrSource.InProcess, order.ItemId.Value, lotId, quantity, null, order.WorkOrderId, null,
                    $"ضایعات {MfFlow.Num(quantity)} {order.Unit} در عملیات {job} ({op.Title}) دستور تولید {order.Number}" + (reason == null ? string.Empty : $"؛ دلیل: {reason}") + "؛ گزارش خودکار از ثبت ضایعات ترمینال کارگاه",
                    job, null, ct);
            }
            string problem = await MfWriteOffs.ApplyAsync(_uow, Me, w, namedSerials, ct);
            if (problem != null)
            {
                if (w.Ncr != null) _uow.Entry(w.Ncr).State = EntityState.Detached;   // a refused write-off leaves no report behind
                return problem;
            }
            if (w.Ncr != null && w.Serials.Count > 0) w.Ncr.SerialNumbers = MfSerialRules.PerLine(w.Serials);
            _writeOffs.Add(w);
            return null;
        }

        /// <summary>Call after the caller's save went through: the draft scrap receipts, the report's tie to the step and the inbox items of the replacement orders of the scrap reported in this request. A problem never fails the operation; it is returned as a note.</summary>
        private async Task<List<string>> ScrapDocumentsAsync(CancellationToken ct)
        {
            List<MfWriteOff> list = _writeOffs.ToList();
            _writeOffs.Clear();
            return await MfWriteOffs.AfterSaveAsync(_uow, _documents, _notify, list, ct);
        }

        /// <summary>
        /// R1p D12: the weld log drafted from the booking when a welding step finishes (the welder is the employee with most run hours on the step). QC completes it in the quality module.
        /// A problem never fails the finish; it is returned as a note.
        /// </summary>
        private async Task<List<string>> WeldLogAsync(Loaded l, CancellationToken ct)
        {
            var notes = new List<string>();
            if (!l.Op.IsWelding || l.Op.WeldLogId.HasValue) return notes;
            var hours = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.OperationId == l.Op.Id && (b.Kind == MfBookingKind.Run || b.Kind == MfBookingKind.Rework))
                .GroupBy(b => b.EmployeeId).Select(g => new { Id = g.Key, Hours = g.Sum(b => b.Hours) }).ToListAsync(ct);
            int welderId = hours.OrderByDescending(h => h.Hours).ThenBy(h => h.Id).Select(h => h.Id).FirstOrDefault();
            FnEmployee welder = welderId == 0 ? l.Employee : await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == welderId, ct) ?? l.Employee;
            (WeldLog log, string note) = await MfWelding.DraftWeldLogAsync(_uow, Me, l.Order, l.Op, welder, ct);
            if (log == null)
            {
                notes.Add("پیش‌نویس گزارش جوش ساخته نشد: " + note);
                return notes;
            }
            try
            {
                await _uow.SaveChangesAsync(ct);   // the log gets its number
                l.Op.WeldLogId = log.Id;
                l.Op.ModifyDate = DateTimeOffset.Now;
                await _uow.SaveChangesAsync(ct);
                notes.Add("پیش‌نویس گزارش جوش از ثبت زمان ساخته شد؛ QC بازرسی چشمی و مشخصات جوش را در ماژول کیفیت تکمیل کند.");
            }
            catch (DbUpdateException)
            {
                _uow.Entry(log).State = EntityState.Detached;
                notes.Add("پیش‌نویس گزارش جوش ثبت نشد؛ QC آن را در ماژول کیفیت وارد کند.");
            }
            return notes;
        }

        private async Task<MfTimeBooking> MyBookingAsync(Loaded l, CancellationToken ct) =>
            await _uow.Set<MfTimeBooking>().Where(b => !b.Deleted && b.EmployeeId == l.Employee.Id && b.OperationId == l.Op.Id).OrderBy(b => b.EndAt == null ? 0 : 1).ThenByDescending(b => b.Id).FirstOrDefaultAsync(ct);

        public async Task<ResponseDto<TerminalOperationDto>> ReportQuantityAsync(ReportQuantityDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Terminal)) return Bad<TerminalOperationDto>(MfText.NotAllowed);
            if (dto == null) return Bad<TerminalOperationDto>(MfText.InvalidData);
            Loaded l = await LoadAsync(dto.OperationId, true, ct);
            if (l.Error != null) return Bad<TerminalOperationDto>(l.Error);
            if (l.Op.Status != MfOperationStatus.InProgress && l.Op.Status != MfOperationStatus.Paused) return Bad<TerminalOperationDto>("تعداد فقط برای عملیاتی که شروع شده گزارش می‌شود.");
            if (dto.GoodQty == 0 && dto.ScrapQty == 0 && dto.ReworkQty == 0) return Bad<TerminalOperationDto>("تعداد سالم، ضایعات یا دوباره‌کاری را وارد کنید.");
            MfTimeBooking booking = await MyBookingAsync(l, ct);
            if (booking == null) return Bad<TerminalOperationDto>("ابتدا کار را شروع کنید تا تعداد روی ثبت زمان شما بنشیند.");
            string problem = await AddQuantityAsync(l, booking, dto, ct);
            if (problem != null) return Bad<TerminalOperationDto>(problem);
            l.Op.ModifyDate = DateTimeOffset.Now;
            l.Op.ModifierUserId = Me;
            booking.ModifyDate = DateTimeOffset.Now;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<TerminalOperationDto>(MfText.Concurrency);
            }
            List<string> scrapNotes = await ScrapDocumentsAsync(ct);
            return ResponseDtoHelper.Ok(await OneAsync(l.Op, l.Employee.Id, ct), scrapNotes.Count == 0 ? null : Join(scrapNotes));
        }

        public async Task<ResponseDto<TerminalOperationDto>> FinishAsync(FinishOperationDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Terminal)) return Bad<TerminalOperationDto>(MfText.NotAllowed);
            if (dto == null) return Bad<TerminalOperationDto>(MfText.InvalidData);
            Loaded l = await LoadAsync(dto.OperationId, true, ct);
            if (l.Error != null) return Bad<TerminalOperationDto>(l.Error);
            if (l.Op.Status != MfOperationStatus.InProgress && l.Op.Status != MfOperationStatus.Paused) return Bad<TerminalOperationDto>("فقط عملیاتی که شروع شده پایان می‌یابد.");
            MfTimeBooking booking = await MyBookingAsync(l, ct);
            if (booking == null) return Bad<TerminalOperationDto>("ابتدا کار را شروع کنید.");
            string problem = await AddQuantityAsync(l, booking, dto, ct);
            if (problem != null) return Bad<TerminalOperationDto>(problem);
            decimal expected = ExpectedQuantity(l);
            decimal reported = l.Op.GoodQty + l.Op.ScrapQty;
            if (reported != expected) return Bad<TerminalOperationDto>($"مجموع سالم و ضایعات ({MfFlow.Num(reported)}) با تعداد این عملیات ({MfFlow.Num(expected)}) برابر نیست؛ تعداد را کامل گزارش کنید.");
            if (await _uow.Set<MfTimeBooking>().AnyAsync(b => !b.Deleted && b.OperationId == l.Op.Id && b.EndAt == null && b.EmployeeId != l.Employee.Id, ct))
                return Bad<TerminalOperationDto>("هنوز شخص دیگری روی این عملیات در حال کار است؛ او باید ابتدا کار خود را متوقف کند.");

            DateTimeOffset stamp = DateTimeOffset.Now;
            if (booking.EndAt == null) Close(booking, Now());
            l.Op.FinishedAt = stamp;
            bool inspected = l.Op.InspectionRequirement == MfInspectionRequirement.Qc || l.Op.InspectionRequirement == MfInspectionRequirement.ItpPoint;
            var staleNotes = new List<string>();
            // R1p D38: an early QC acceptance holds only for the quantities QC saw; other quantities at the finish need a new acceptance
            bool accepted = l.Op.QcAccepted && MfTerminalRules.AcceptanceStillHolds(l.Op);
            if (l.Op.QcAccepted && !accepted)
            {
                l.Op.QcAccepted = false;
                l.Op.QcAcceptedGoodQty = null;
                l.Op.QcAcceptedScrapQty = null;
                l.Op.QcAcceptedReworkQty = null;
                staleNotes.Add("تعداد گزارش‌شده پس از پذیرش بازرسی تغییر کرده است؛ بازرسی QC دوباره لازم است.");
            }
            if (inspected && !accepted)
            {
                l.Op.Status = MfOperationStatus.QcHold;
                l.Op.QcRequestedAt = stamp;
            }
            else l.Op.Status = MfOperationStatus.Done;
            l.Op.ModifyDate = stamp;
            l.Op.ModifierUserId = Me;
            List<string> notes = await _flow.ApplyAsync(l.Order, l.All, ct);
            notes.AddRange(staleNotes);
            await _flow.PartSyncAsync(l.Op, ct);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<TerminalOperationDto>(MfText.Concurrency);
            }
            notes.AddRange(await _flow.AfterSaveAsync(ct));
            notes.AddRange(await ScrapDocumentsAsync(ct));
            notes.AddRange(await WeldLogAsync(l, ct));
            await MfGates.QcAsync(_notify, l.Op, l.Order.Number, ct);
            return ResponseDtoHelper.Ok(await OneAsync(l.Op, l.Employee.Id, ct), notes.Count == 0 ? null : Join(notes));
        }

        public async Task<ResponseDto<TerminalOperationDto>> RequestQcAsync(RequestQcDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Terminal)) return Bad<TerminalOperationDto>(MfText.NotAllowed);
            if (dto == null) return Bad<TerminalOperationDto>(MfText.InvalidData);
            Loaded l = await LoadAsync(dto.OperationId, true, ct);
            if (l.Error != null) return Bad<TerminalOperationDto>(l.Error);
            if (l.Op.InspectionRequirement != MfInspectionRequirement.Qc && l.Op.InspectionRequirement != MfInspectionRequirement.ItpPoint) return Bad<TerminalOperationDto>("این عملیات بازرسی QC ندارد.");
            if (l.Op.Status == MfOperationStatus.QcHold) return Bad<TerminalOperationDto>("درخواست بازرسی قبلاً ثبت شده است.");
            if (l.Op.Status != MfOperationStatus.InProgress && l.Op.Status != MfOperationStatus.Paused) return Bad<TerminalOperationDto>("درخواست بازرسی فقط برای عملیات شروع‌شده ثبت می‌شود.");
            DateTimeOffset stamp = DateTimeOffset.Now;
            MfTimeBooking running = await _uow.Set<MfTimeBooking>().FirstOrDefaultAsync(b => !b.Deleted && b.EmployeeId == l.Employee.Id && b.OperationId == l.Op.Id && b.EndAt == null, ct);
            if (running != null) Close(running, Now());
            l.Op.Status = MfOperationStatus.QcHold;
            l.Op.QcRequestedAt = stamp;
            if (Clean(dto.Note) != null) l.Op.Note = Clean(dto.Note);
            l.Op.ModifyDate = stamp;
            l.Op.ModifierUserId = Me;
            await _flow.PartSyncAsync(l.Op, ct);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<TerminalOperationDto>(MfText.Concurrency);
            }
            await MfGates.QcAsync(_notify, l.Op, l.Order.Number, ct);
            return ResponseDtoHelper.Ok(await OneAsync(l.Op, l.Employee.Id, ct));
        }

        // ================================================================================== machine down

        private async Task<MaintenanceRequestDto> RequestDtoAsync(MfMaintenanceRequest r, CancellationToken ct)
        {
            string machine = await _uow.Set<Machine>().AsNoTracking().Where(m => m.Id == r.MachineId).Select(m => m.Title).FirstOrDefaultAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.ReportedByUserId }, ct);
            return new MaintenanceRequestDto
            {
                Id = r.Id, Number = r.Number, MachineId = r.MachineId, MachineTitle = machine, WorkCenterId = r.WorkCenterId, OperationId = r.OperationId, ReasonCodeId = r.ReasonCodeId,
                ReportedByUserId = r.ReportedByUserId, ReportedByName = names.Name(r.ReportedByUserId), ReportedAt = r.ReportedAt, ReportedAtText = r.ReportedAtText,
                Description = r.Description, Status = r.Status,
            };
        }

        public async Task<ResponseDto<MaintenanceRequestDto>> MachineDownAsync(MachineDownDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Terminal + "," + ProductionRoles.Maintenance)) return Bad<MaintenanceRequestDto>(MfText.NotAllowed);
            if (dto == null || Clean(dto.Description) == null) return Bad<MaintenanceRequestDto>("شرح خرابی را بنویسید.");
            Machine machine = await _uow.Set<Machine>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == dto.MachineId, ct);
            if (machine == null) return MfText.NotFound<MaintenanceRequestDto>("ماشین پیدا نشد.");
            if (dto.OperationId.HasValue && !await _uow.Set<MfOperation>().AnyAsync(o => o.Id == dto.OperationId.Value && !o.Deleted, ct)) return Bad<MaintenanceRequestDto>("عملیات پیدا نشد.");
            string reasonProblem = await CheckReasonAsync(dto.ReasonCodeId, new[] { MfReasonKind.Downtime }, ct);
            if (reasonProblem != null) return Bad<MaintenanceRequestDto>(reasonProblem);
            DateTimeOffset stamp = DateTimeOffset.Now;
            DateTime now = Now();
            MfMachineInfo info = await _uow.Set<MfMachineInfo>().FirstOrDefaultAsync(i => i.MachineId == machine.Id && !i.Deleted, ct);
            if (info == null)
            {
                info = new MfMachineInfo { MachineId = machine.Id, CreateDate = stamp, CreatorUserId = Me };
                _uow.Set<MfMachineInfo>().Add(info);
            }
            info.Status = MfMachineStatus.Down;
            info.ModifyDate = stamp;
            info.ModifierUserId = Me;
            var request = new MfMaintenanceRequest
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, MfNumbers.MaintenanceRequest, "MNT", now, ct), MachineId = machine.Id, WorkCenterId = info.WorkCenterId,
                OperationId = dto.OperationId, ReasonCodeId = dto.ReasonCodeId, ReportedByUserId = Me, ReportedAt = now, ReportedAtText = MfText.Stamp(now),
                Description = Clean(dto.Description), Status = MfMaintenanceRequestStatus.Open, CreateDate = stamp, ModifyDate = stamp, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<MfMaintenanceRequest>().Add(request);
            await _uow.SaveChangesAsync(ct);
            await MfGates.MaintenanceAsync(_notify, request, machine.Title, ct);
            // the breakdown order of the maintenance module is drafted at once (the machine stays down while it is open)
            await MfMaintenanceService.DraftBreakdownAsync(_uow, Me, request, machine.Title, _notify, ct);
            return ResponseDtoHelper.Ok(await RequestDtoAsync(request, ct));
        }

        public async Task<ResponseDto<PagedList<MaintenanceRequestDto>>> GetMaintenanceRequestsAsync(MaintenanceRequestQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return Bad<PagedList<MaintenanceRequestDto>>(MfText.NotAllowed);
            query ??= new MaintenanceRequestQuery();
            IQueryable<MfMaintenanceRequest> q = _uow.Set<MfMaintenanceRequest>().AsNoTracking().Where(r => !r.Deleted);
            if (query.MachineId.HasValue) q = q.Where(r => r.MachineId == query.MachineId.Value);
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(r => r.Number.Contains(text) || r.Description.Contains(text));
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfMaintenanceRequest> rows = await q.OrderByDescending(r => r.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            var dtos = new List<MaintenanceRequestDto>();
            foreach (MfMaintenanceRequest r in rows) dtos.Add(await RequestDtoAsync(r, ct));
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(dtos, 1, int.MaxValue, ct), total, number, size));
        }

        // ================================================================================== bookings

        private static bool DayClosed(MfTimeBooking b) => b.StartAt.Date < DateTime.Today;

        private async Task<List<BookingDto>> BookingDtosAsync(List<MfTimeBooking> rows, CancellationToken ct)
        {
            if (rows.Count == 0) return new List<BookingDto>();
            // R1p D1: who worked is shown only to the shop management, payroll, finance, the CEO and the employee himself; everybody else sees the hours without the name
            bool named = MfPrivacy.SeesNames(_user);
            FnEmployee viewer = named ? null : await MfText.EmployeeOfUserAsync(_uow, Me, ct);
            bool Visible(MfTimeBooking b) => MfBookingRules.SeesName(named, viewer?.Id, b.EmployeeId);
            List<int> employeeIds = rows.Where(Visible).Select(r => r.EmployeeId).Distinct().ToList();
            Dictionary<int, string> employees = employeeIds.Count == 0 ? new Dictionary<int, string>()
                : MfNames.ToDictionary(await _uow.Set<FnEmployee>().AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToListAsync(ct));
            List<int> machineIds = rows.Where(r => r.MachineId.HasValue).Select(r => r.MachineId.Value).Distinct().ToList();
            Dictionary<int, string> machines = machineIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<Machine>().AsNoTracking().Where(m => machineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Title, ct);
            List<int> opIds = rows.Select(r => r.OperationId).Distinct().ToList();
            List<MfOperation> ops = await _uow.Set<MfOperation>().AsNoTracking().Where(o => opIds.Contains(o.Id)).ToListAsync(ct);
            List<int> orderIds = ops.Select(o => o.OrderId).Distinct().ToList();
            Dictionary<int, string> orders = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Number, ct);
            return rows.Select(b =>
            {
                MfOperation op = ops.First(o => o.Id == b.OperationId);
                orders.TryGetValue(op.OrderId, out string number);
                bool visible = Visible(b);
                return new BookingDto
                {
                    Id = b.Id, OperationId = b.OperationId, OrderNumber = number, JobCard = MfMapping.JobCard(number, op), OperationTitle = op.Title, EmployeeId = visible ? b.EmployeeId : 0,
                    EmployeeName = visible && employees.TryGetValue(b.EmployeeId, out string en) ? en : null, EmployeeHidden = !visible, IsCorrection = b.IsCorrection, CorrectsBookingId = b.CorrectsBookingId, CorrectionPending = b.IsCorrection && b.CorrectionApprovedByUserId == null,
                    CorrectionApprovedAtText = MfText.Stamp(b.CorrectionApprovedAt), MachineId = b.MachineId, MachineTitle = b.MachineId.HasValue && machines.TryGetValue(b.MachineId.Value, out string mt) ? mt : null,
                    Kind = b.Kind, StartAt = b.StartAt, StartAtText = MfText.Stamp(b.StartAt), EndAt = b.EndAt, EndAtText = MfText.Stamp(b.EndAt), Minutes = b.Minutes, Hours = b.Hours,
                    GoodQty = b.GoodQty, ScrapQty = b.ScrapQty, ScrapReasonCodeId = b.ScrapReasonCodeId, ReworkQty = b.ReworkQty, ReasonCodeId = b.ReasonCodeId, Note = b.Note, Source = b.Source,
                    SupervisorReason = b.SupervisorReason, EditReason = b.EditReason, Running = b.EndAt == null, DayClosed = DayClosed(b), CostPosted = b.CostPostedAt != null, RowVersion = b.RowVersion == null ? null : Convert.ToBase64String(b.RowVersion),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<BookingDto>>> GetBookingsAsync(BookingQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(MfPrivacy.BookingReaders)) return Bad<PagedList<BookingDto>>(MfText.NotAllowed);
            query ??= new BookingQuery();
            // R1p D1: names behind the hours are for PRODUCTIONMANAGER, SHOPSUPERVISOR, PAYROLL, FINANCEMANAGER, CEO and the employee himself
            bool named = MfPrivacy.SeesNames(_user);
            FnEmployee me = named ? null : await MfText.EmployeeOfUserAsync(_uow, Me, ct);
            IQueryable<MfTimeBooking> q = _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted);
            if (!named && !_user.IsInRole(ProductionRoles.Readers))
            {
                // an operator sees his own bookings only
                if (me == null) return Bad<PagedList<BookingDto>>(NoEmployee);
                q = q.Where(b => b.EmployeeId == me.Id);
            }
            if (query.OperationId.HasValue) q = q.Where(b => b.OperationId == query.OperationId.Value);
            if (query.OrderId.HasValue) q = q.Where(b => _uow.Set<MfOperation>().Any(o => o.Id == b.OperationId && o.OrderId == query.OrderId.Value));
            if (query.EmployeeId.HasValue)
            {
                // a filter by person would name the person behind the rows it returns
                if (!named && query.EmployeeId.Value != me?.Id)
                    return Bad<PagedList<BookingDto>>("جست‌وجوی ثبت زمان بر اساس شخص فقط برای مدیر تولید، سرپرست کارگاه، حقوق و دستمزد، مدیر مالی، مدیرعامل و خود شخص ممکن است؛ ساعت ثبت‌شده به تفکیک مرکز کار را از گزارش ساعت‌ها ببینید.");
                q = q.Where(b => b.EmployeeId == query.EmployeeId.Value);
            }
            if (query.WorkCenterId.HasValue) q = q.Where(b => _uow.Set<MfOperation>().Any(o => o.Id == b.OperationId && o.WorkCenterId == query.WorkCenterId.Value));
            if (query.From.HasValue) { DateTime f = query.From.Value.Date; q = q.Where(b => b.StartAt >= f); }
            if (query.To.HasValue) { DateTime t = query.To.Value.Date.AddDays(1); q = q.Where(b => b.StartAt < t); }
            if (query.RunningOnly == true) q = q.Where(b => b.EndAt == null);
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfTimeBooking> rows = await q.OrderByDescending(b => b.StartAt).ThenByDescending(b => b.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(await BookingDtosAsync(rows, ct), 1, int.MaxValue, ct), total, number, size));
        }

        private async Task<string> OverlapAsync(int employeeId, DateTime start, DateTime end, int exceptId, CancellationToken ct)
        {
            DateTime now = Now();
            bool overlap = await _uow.Set<MfTimeBooking>().AnyAsync(b => !b.Deleted && b.EmployeeId == employeeId && b.Id != exceptId && b.StartAt < end && (b.EndAt ?? now) > start, ct);
            return overlap ? "این بازه با ثبت زمان دیگری از همین پرسنل همپوشانی دارد." : null;
        }

        public async Task<ResponseDto<BookingDto>> CreateBookingAsync(SaveBookingDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Supervisors)) return Bad<BookingDto>(MfText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(MfBookingKind), dto.Kind)) return Bad<BookingDto>(MfText.InvalidData);
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<BookingDto>("ثبت زمان برای شخص دیگر دلیل لازم دارد.");
            DateTime start = new DateTime(dto.StartAt.Year, dto.StartAt.Month, dto.StartAt.Day, dto.StartAt.Hour, dto.StartAt.Minute, dto.StartAt.Second);
            DateTime end = new DateTime(dto.EndAt.Year, dto.EndAt.Month, dto.EndAt.Day, dto.EndAt.Hour, dto.EndAt.Minute, dto.EndAt.Second);
            if (end <= start) return Bad<BookingDto>("پایان باید بعد از شروع باشد.");
            if (end > DateTime.Now) return Bad<BookingDto>("پایان ثبت زمان نمی‌تواند در آینده باشد.");
            if ((end - start).TotalHours > 16) return Bad<BookingDto>("یک ثبت زمان بیش از ۱۶ ساعت نمی‌شود.");
            FnEmployee worker = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId && e.IsActive, ct);
            if (worker == null) return Bad<BookingDto>("پرسنل پیدا نشد یا غیرفعال است.");
            Loaded l = await LoadAsync(dto.OperationId, false, ct);
            if (l.Error != null) return Bad<BookingDto>(l.Error);
            if (l.Op.Status == MfOperationStatus.Waiting || l.Op.Status == MfOperationStatus.Done || l.Op.Status == MfOperationStatus.Skipped || l.Op.Status == MfOperationStatus.Rejected)
                return Bad<BookingDto>("روی این عملیات (با وضعیت فعلی) نمی‌توان زمان ثبت کرد.");
            // R1p D12: the same welding rule as on the terminal (approved WPS, qualified welder) also binds a booking a supervisor makes for somebody else
            if (l.Op.IsWelding)
            {
                var weld = await MfWelding.CheckStartAsync(_uow, l.Op, worker, dto.Kind, start.Date, ct);
                if (weld.Error != null) return Bad<BookingDto>(weld.Error);
            }
            if (dto.MachineId.HasValue)
            {
                string machineProblem = await CheckMachineAsync(dto.MachineId.Value, l.Op, false, ct);
                if (machineProblem != null) return Bad<BookingDto>(machineProblem);
            }
            // R1p D43 (round 3): the machine requirement binds a booking a supervisor makes for somebody else as well
            string machineNeeded = await MachineRequirementAsync(l.Op, dto.Kind, dto.MachineId, ct);
            if (machineNeeded != null) return Bad<BookingDto>(machineNeeded);
            if (dto.GoodQty < 0 || dto.ScrapQty < 0 || dto.ReworkQty < 0) return Bad<BookingDto>("تعداد نمی‌تواند منفی باشد.");
            if (dto.ScrapQty > 0 && !dto.ScrapReasonCodeId.HasValue) return Bad<BookingDto>("برای ضایعات، دلیل ضایعات را انتخاب کنید.");
            string reasonProblem = await CheckReasonAsync(dto.ScrapReasonCodeId, new[] { MfReasonKind.Scrap }, ct);
            reasonProblem ??= await CheckReasonAsync(dto.ReasonCodeId, new[] { MfReasonKind.Downtime, MfReasonKind.Waiting }, ct);
            if (reasonProblem != null) return Bad<BookingDto>(reasonProblem);
            string overlap = await OverlapAsync(dto.EmployeeId, start, end, 0, ct);
            if (overlap != null) return Bad<BookingDto>(overlap);
            if (l.Op.GoodQty + l.Op.ScrapQty + dto.GoodQty + dto.ScrapQty > ExpectedQuantity(l)) return Bad<BookingDto>("مجموع سالم و ضایعات از تعداد این عملیات بیشتر می‌شود.");

            DateTimeOffset stamp = DateTimeOffset.Now;
            int minutes = MinutesBetween(start, end);
            var booking = new MfTimeBooking
            {
                OperationId = l.Op.Id, EmployeeId = dto.EmployeeId, MachineId = dto.MachineId, Kind = dto.Kind, StartAt = start, EndAt = end, Minutes = minutes, Hours = MfText.HoursOf(minutes),
                GoodQty = dto.GoodQty, ScrapQty = dto.ScrapQty, ScrapReasonCodeId = dto.ScrapReasonCodeId, ReworkQty = dto.ReworkQty, ReasonCodeId = dto.ReasonCodeId, Note = Clean(dto.Note),
                Source = MfBookingSource.Supervisor, SupervisorReason = reason, CreateDate = stamp, ModifyDate = stamp, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<MfTimeBooking>().Add(booking);
            l.Op.GoodQty += dto.GoodQty;
            l.Op.ScrapQty += dto.ScrapQty;
            l.Op.ReworkQty += dto.ReworkQty;
            if (dto.ScrapQty > 0)
            {
                string scrapProblem = await QueueScrapAsync(l.Order, l.Op, dto.ScrapQty, dto.ScrapReasonCodeId, dto.ScrapSerials, ct);
                if (scrapProblem != null) return Bad<BookingDto>(scrapProblem);
            }
            if (l.Op.Status == MfOperationStatus.Ready) l.Op.Status = MfOperationStatus.Paused;
            l.Op.StartedAt ??= new DateTimeOffset(start);
            l.Op.ModifyDate = stamp;
            l.Op.ModifierUserId = Me;
            await _flow.ApplyAsync(l.Order, l.All, ct);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<BookingDto>(MfText.Concurrency);
            }
            catch (DbUpdateException)
            {
                return Bad<BookingDto>("ثبت زمان ممکن نشد؛ دوباره تلاش کنید.");
            }
            List<string> notes = await _flow.AfterSaveAsync(ct);
            notes.AddRange(await ScrapDocumentsAsync(ct));
            return ResponseDtoHelper.Ok((await BookingDtosAsync(new List<MfTimeBooking> { booking }, ct))[0], notes.Count == 0 ? null : Join(notes));
        }

        public async Task<ResponseDto<BookingDto>> EditBookingAsync(EditBookingDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Supervisors)) return Bad<BookingDto>(MfText.NotAllowed);
            if (dto == null) return Bad<BookingDto>(MfText.InvalidData);
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<BookingDto>("دلیل ویرایش را بنویسید.");
            MfTimeBooking b = await _uow.Set<MfTimeBooking>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (b == null) return MfText.NotFound<BookingDto>("ثبت زمان پیدا نشد.");
            if (!MfConcurrency.Apply(_uow, b, dto.RowVersion)) return Bad<BookingDto>(MfText.InvalidData);
            if (b.EndAt == null) return Bad<BookingDto>("ثبت زمانِ در حال اجرا ویرایش نمی‌شود؛ ابتدا آن را پایان دهید.");
            if (b.CostPostedAt != null) return Bad<BookingDto>("هزینه این ثبت زمان در حسابداری ثبت شده است و ویرایش نمی‌شود؛ اصلاح از طریق امور مالی انجام می‌شود.");
            if (b.IsCorrection) return Bad<BookingDto>("ثبت زمان اصلاحی ویرایش نمی‌شود؛ اگر نادرست است، مدیر تولید آن را رد کند و ثبت زمان اصلاحی تازه‌ای بنویسید.");
            if (DayClosed(b) && !_user.IsInRole(ProductionRoles.Supervisor)) return Bad<BookingDto>("روز این ثبت زمان بسته شده است؛ فقط سرپرست کارگاه با ذکر دلیل می‌تواند آن را تغییر دهد.");
            MfOperation op = await _uow.Set<MfOperation>().FirstAsync(o => o.Id == b.OperationId, ct);
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().FirstAsync(o => o.Id == op.OrderId, ct);
            // R1p D39: the bookings of a completed, closed or cancelled order are frozen; the supervisor adds a correction booking with a reason instead, and a second production manager approves it
            if (MfBookingRules.IsFrozen(order.Status))
                return Bad<BookingDto>("ثبت زمان‌های دستور تولید تکمیل‌شده، بسته یا لغوشده تغییر نمی‌کند؛ برای اصلاح، سرپرست کارگاه «ثبت زمان اصلاحی» را با ذکر دلیل اضافه کند و مدیر تولید دیگری آن را تأیید کند.");

            DateTime start = dto.StartAt.HasValue ? new DateTime(dto.StartAt.Value.Year, dto.StartAt.Value.Month, dto.StartAt.Value.Day, dto.StartAt.Value.Hour, dto.StartAt.Value.Minute, dto.StartAt.Value.Second) : b.StartAt;
            DateTime end = dto.EndAt.HasValue ? new DateTime(dto.EndAt.Value.Year, dto.EndAt.Value.Month, dto.EndAt.Value.Day, dto.EndAt.Value.Hour, dto.EndAt.Value.Minute, dto.EndAt.Value.Second) : b.EndAt.Value;
            // R1p L8 (round 3): the time window is checked only when the edit sends times; an edit of quantities or of the machine leaves the stored times alone (a zero-length booking can still have its quantities edited)
            bool timesSent = dto.StartAt.HasValue || dto.EndAt.HasValue;
            if (timesSent)
            {
                if (end <= start) return Bad<BookingDto>("پایان باید بعد از شروع باشد.");
                if (end > DateTime.Now) return Bad<BookingDto>("پایان ثبت زمان نمی‌تواند در آینده باشد.");
                if ((end - start).TotalHours > 16) return Bad<BookingDto>("یک ثبت زمان بیش از ۱۶ ساعت نمی‌شود.");
            }
            if (dto.MachineId.HasValue && dto.MachineId != b.MachineId)
            {
                string machineProblem = await CheckMachineAsync(dto.MachineId.Value, op, false, ct);
                if (machineProblem != null) return Bad<BookingDto>(machineProblem);
            }
            decimal good = dto.GoodQty ?? b.GoodQty, scrap = dto.ScrapQty ?? b.ScrapQty, rework = dto.ReworkQty ?? b.ReworkQty;
            if (good < 0 || scrap < 0 || rework < 0) return Bad<BookingDto>("تعداد نمی‌تواند منفی باشد.");
            int? scrapReason = dto.ScrapReasonCodeId ?? b.ScrapReasonCodeId;
            if (scrap > 0 && !scrapReason.HasValue) return Bad<BookingDto>("برای ضایعات، دلیل ضایعات را انتخاب کنید.");
            string reasonProblem = await CheckReasonAsync(dto.ScrapReasonCodeId, new[] { MfReasonKind.Scrap }, ct);
            if (reasonProblem != null) return Bad<BookingDto>(reasonProblem);
            if (timesSent)
            {
                string overlap = await OverlapAsync(b.EmployeeId, start, end, b.Id, ct);
                if (overlap != null) return Bad<BookingDto>(overlap);
            }
            decimal opGood = op.GoodQty - b.GoodQty + good, opScrap = op.ScrapQty - b.ScrapQty + scrap;
            List<MfOperation> allOps = await _uow.Set<MfOperation>().Where(o => o.OrderId == order.Id && !o.Deleted).ToListAsync(ct);
            // R1p D7: against the quantity this operation has to report (the good quantity of the operation before it), not the full order quantity
            if (opGood + opScrap > MfQuantityRules.Expected(allOps, op, order.Quantity)) return Bad<BookingDto>("مجموع سالم و ضایعات از تعداد این عملیات (تعداد سالم مرحله قبل) بیشتر می‌شود.");
            if (good != b.GoodQty || scrap != b.ScrapQty || rework != b.ReworkQty)
            {
                // R1p D7/D38 (review 7): the quantities of a step are final once QC accepted them, an NCR or a concession decided about them, or the next step started with them
                string frozen = MfBookingRules.QuantityFrozen(op, allOps);
                if (frozen != null) return Bad<BookingDto>(frozen);
                // R1p review 12: scrap that was reported has a report, a write-off and a replacement order; it is not taken back by editing the booking
                if (scrap < b.ScrapQty)
                    return Bad<BookingDto>("ضایعاتِ ثبت‌شده کم نمی‌شود؛ برای آن گزارش عدم انطباق، کسر از تعداد دستور و دستور جایگزین ساخته شده است و با گزارش عدم انطباق (کیفیت) رسیدگی می‌شود.");
            }
            // R1p D12 (review 8): the welding rule that let this booking start (approved WPS, qualified welder) is checked again when the edit changes the kind of work or the day
            MfBookingKind kind = dto.Kind.HasValue && Enum.IsDefined(typeof(MfBookingKind), dto.Kind.Value) ? dto.Kind.Value : b.Kind;
            // R1p D43 (round 3): after the edit the booking still names its machine where the work centre requires one
            string machineNeeded = await MachineRequirementAsync(op, kind, dto.MachineId ?? b.MachineId, ct);
            if (machineNeeded != null) return Bad<BookingDto>(machineNeeded);
            if (op.IsWelding && (kind != b.Kind || start.Date != b.StartAt.Date))
            {
                FnEmployee welder = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == b.EmployeeId, ct);
                if (welder == null) return Bad<BookingDto>("پرسنل پیدا نشد.");
                var weld = await MfWelding.CheckStartAsync(_uow, op, welder, kind, start.Date, ct);
                if (weld.Error != null) return Bad<BookingDto>(weld.Error);
            }
            if (scrap > b.ScrapQty)
            {
                string scrapProblem = await QueueScrapAsync(order, op, scrap - b.ScrapQty, scrapReason, dto.ScrapSerials, ct);
                if (scrapProblem != null) return Bad<BookingDto>(scrapProblem);
            }

            op.GoodQty = opGood;
            op.ScrapQty = opScrap;
            op.ReworkQty = op.ReworkQty - b.ReworkQty + rework;
            b.Kind = kind;
            b.StartAt = start;
            b.EndAt = end;
            b.Minutes = MinutesBetween(start, end);
            b.Hours = MfText.HoursOf(b.Minutes);
            if (dto.MachineId.HasValue) b.MachineId = dto.MachineId;
            b.GoodQty = good; b.ScrapQty = scrap; b.ReworkQty = rework; b.ScrapReasonCodeId = scrapReason;
            if (Clean(dto.Note) != null) b.Note = Clean(dto.Note);
            b.EditReason = reason;
            // every supervisor correction is audited, also when it repeats the reason of the previous one (the audit trail writes a booking only when EditReason is marked modified)
            _uow.Entry(b).Property(x => x.EditReason).IsModified = true;
            b.ModifyDate = DateTimeOffset.Now;
            b.ModifierUserId = Me;
            op.ModifyDate = DateTimeOffset.Now;
            op.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<BookingDto>(MfText.Concurrency);
            }
            List<string> editNotes = await ScrapDocumentsAsync(ct);
            return ResponseDtoHelper.Ok((await BookingDtosAsync(new List<MfTimeBooking> { b }, ct))[0], editNotes.Count == 0 ? null : Join(editNotes));
        }

        /// <summary>
        /// R1p review 9: the hours this person worked on that day, in minutes, less what is booked already; a correction must fit in what is left of the attendance. The booked minutes include corrections that wait for approval,
        /// so two corrections for the same stretch of the day cannot both pass.
        /// </summary>
        private async Task<string> AttendanceProblemAsync(int employeeId, DateTime day, int correctionMinutes, CancellationToken ct)
        {
            ResponseDto<List<WorkedHoursDto>> worked = await _attendance.GetWorkedHoursAsync(new WorkedHoursQuery { EmployeeIds = new List<int> { employeeId }, From = day.Date, To = day.Date }, ct);
            if (worked == null || !worked.Success) return worked?.Message ?? "تردد این پرسنل خوانده نشد.";
            int attended = (worked.Data ?? new List<WorkedHoursDto>()).Where(w => w.Date.Date == day.Date).Sum(w => w.WorkedMinutes);
            DateTime next = day.Date.AddDays(1);
            int booked = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(x => !x.Deleted && x.EmployeeId == employeeId && x.EndAt != null && x.StartAt >= day.Date && x.StartAt < next).SumAsync(x => x.Minutes, ct);
            return MfBookingRules.AttendanceRefusal(attended, booked, correctionMinutes);
        }

        public async Task<ResponseDto<BookingDto>> CorrectBookingAsync(SaveCorrectionBookingDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Supervisors)) return Bad<BookingDto>(MfText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(MfBookingKind), dto.Kind)) return Bad<BookingDto>(MfText.InvalidData);
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<BookingDto>("برای ثبت زمان اصلاحی، دلیل را بنویسید.");
            DateTime start = new DateTime(dto.StartAt.Year, dto.StartAt.Month, dto.StartAt.Day, dto.StartAt.Hour, dto.StartAt.Minute, dto.StartAt.Second);
            DateTime end = new DateTime(dto.EndAt.Year, dto.EndAt.Month, dto.EndAt.Day, dto.EndAt.Hour, dto.EndAt.Minute, dto.EndAt.Second);
            if (end <= start) return Bad<BookingDto>("پایان باید بعد از شروع باشد.");
            if (end > DateTime.Now) return Bad<BookingDto>("پایان ثبت زمان نمی‌تواند در آینده باشد.");
            if ((end - start).TotalHours > 16) return Bad<BookingDto>("یک ثبت زمان بیش از ۱۶ ساعت نمی‌شود.");
            // R1p review 9: a correction reaches back a limited number of days (Production:MaxBackdateDays, ★ 7)
            string backdate = MfBookingRules.BackdateRefusal(start, DateTime.Today, MaxBackdateDays);
            if (backdate != null) return Bad<BookingDto>(backdate);
            MfOperation op = await _uow.Set<MfOperation>().FirstOrDefaultAsync(o => o.Id == dto.OperationId && !o.Deleted, ct);
            if (op == null) return MfText.NotFound<BookingDto>("عملیات پیدا نشد.");
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().FirstAsync(o => o.Id == op.OrderId, ct);
            if (order.Status == MfOrderStatus.Released || order.Status == MfOrderStatus.InProgress)
                return Bad<BookingDto>("این دستور تولید هنوز در حال اجراست؛ «ثبت زمان» عادی را به کار ببرید. ثبت زمان اصلاحی فقط برای دستور تکمیل‌شده است.");
            if (order.Status != MfOrderStatus.Completed)
                return Bad<BookingDto>("ثبت زمان اصلاحی فقط برای دستور تولید تکمیل‌شده است؛ اصلاح هزینه دستور بسته یا لغوشده از طریق امور مالی انجام می‌شود.");
            FnEmployee worker = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId && e.IsActive, ct);
            if (worker == null) return Bad<BookingDto>("پرسنل پیدا نشد یا غیرفعال است.");
            if (dto.CorrectsBookingId.HasValue && !await _uow.Set<MfTimeBooking>().AnyAsync(x => x.Id == dto.CorrectsBookingId.Value && x.OperationId == op.Id && !x.Deleted, ct))
                return Bad<BookingDto>("ثبت زمانی که این اصلاح به آن مربوط است در همین عملیات پیدا نشد.");
            if (dto.MachineId.HasValue)
            {
                string machineProblem = await CheckMachineAsync(dto.MachineId.Value, op, false, ct);
                if (machineProblem != null) return Bad<BookingDto>(machineProblem);
            }
            // R1p D43 (round 3): a correction names the machine as well where the work centre requires one
            string machineNeeded = await MachineRequirementAsync(op, dto.Kind, dto.MachineId, ct);
            if (machineNeeded != null) return Bad<BookingDto>(machineNeeded);
            string overlap = await OverlapAsync(dto.EmployeeId, start, end, 0, ct);
            if (overlap != null) return Bad<BookingDto>(overlap);
            int minutes = MinutesBetween(start, end);
            string attendanceProblem = await AttendanceProblemAsync(dto.EmployeeId, start, minutes, ct);
            if (attendanceProblem != null) return Bad<BookingDto>(attendanceProblem);
            if (op.IsWelding)
            {
                var weld = await MfWelding.CheckStartAsync(_uow, op, worker, dto.Kind, start.Date, ct);
                if (weld.Error != null) return Bad<BookingDto>(weld.Error);
            }
            DateTimeOffset stamp = DateTimeOffset.Now;
            var booking = new MfTimeBooking
            {
                OperationId = op.Id, EmployeeId = dto.EmployeeId, MachineId = dto.MachineId, Kind = dto.Kind, StartAt = start, EndAt = end, Minutes = minutes, Hours = MfText.HoursOf(minutes), Note = Clean(dto.Note),
                Source = MfBookingSource.Supervisor, SupervisorReason = reason, IsCorrection = true, CorrectsBookingId = dto.CorrectsBookingId, CreateDate = stamp, ModifyDate = stamp, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<MfTimeBooking>().Add(booking);
            op.ModifyDate = stamp;
            op.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<BookingDto>(MfText.Concurrency);
            }
            catch (DbUpdateException)
            {
                return Bad<BookingDto>("ثبت زمان اصلاحی ممکن نشد؛ دوباره تلاش کنید.");
            }
            await MfGates.CorrectionAsync(_notify, booking, MfMapping.JobCard(order.Number, op), ct);
            return ResponseDtoHelper.Ok((await BookingDtosAsync(new List<MfTimeBooking> { booking }, ct))[0],
                "ثبت زمان اصلاحی تا تأیید مدیر تولید دیگری (غیر از ثبت‌کننده) در هیچ گزارش هزینه، ساعت و OEE نمی‌آید.");
        }

        public async Task<ResponseDto<BookingDto>> ApproveCorrectionAsync(ApproveCorrectionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Manager)) return Bad<BookingDto>("تأیید ثبت زمان اصلاحی فقط با مدیر تولید است.");
            if (dto == null) return Bad<BookingDto>(MfText.InvalidData);
            MfTimeBooking b = await _uow.Set<MfTimeBooking>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (b == null) return MfText.NotFound<BookingDto>("ثبت زمان پیدا نشد.");
            if (!MfConcurrency.Apply(_uow, b, dto.RowVersion)) return Bad<BookingDto>(MfText.InvalidData);
            if (!b.IsCorrection) return Bad<BookingDto>("این ثبت زمان اصلاحی نیست.");
            if (b.CorrectionApprovedByUserId.HasValue) return Bad<BookingDto>("این ثبت زمان اصلاحی قبلاً تأیید شده است.");
            if (b.CostPostedAt != null) return Bad<BookingDto>("هزینه این ثبت زمان در حسابداری ثبت شده است.");
            MfOperation op = await _uow.Set<MfOperation>().FirstAsync(o => o.Id == b.OperationId, ct);
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().FirstAsync(o => o.Id == op.OrderId, ct);
            DateTimeOffset stamp = DateTimeOffset.Now;
            if (dto.Approve)
            {
                string refusal = MfBookingRules.ApprovalRefusal(b, Me);
                if (refusal != null) return Bad<BookingDto>(refusal);
                b.CorrectionApprovedByUserId = Me;
                b.CorrectionApprovedAt = stamp;
            }
            else
            {
                string reason = Clean(dto.Reason);
                if (reason == null) return Bad<BookingDto>("برای رد ثبت زمان اصلاحی، دلیل را بنویسید.");
                b.Delete();
                b.EditReason = "رد ثبت زمان اصلاحی: " + reason;
            }
            // the audit trail writes a booking only when EditReason is marked modified; the approval is audited the same way as an edit
            b.EditReason ??= "تأیید ثبت زمان اصلاحی";
            _uow.Entry(b).Property(x => x.EditReason).IsModified = true;
            b.ModifyDate = stamp;
            b.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<BookingDto>(MfText.Concurrency);
            }
            await MfGates.CorrectionAsync(_notify, b, MfMapping.JobCard(order.Number, op), ct);
            return ResponseDtoHelper.Ok((await BookingDtosAsync(new List<MfTimeBooking> { b }, ct))[0],
                dto.Approve ? "ثبت زمان اصلاحی تأیید شد؛ از اجرای بعدی هزینه روزانه در هزینه دستور تولید و گزارش‌ها می‌آید." : "ثبت زمان اصلاحی رد شد.");
        }

        internal sealed class BookingHoursRow
        {
            public int? WorkCenterId { get; set; }
            public DateTime Day { get; set; }
            public int Bookings { get; set; }
            public decimal Hours { get; set; }
        }

        /// <summary>
        /// The closed bookings that start in [<paramref name="since"/>, <paramref name="until"/>) grouped by work centre and day, in the database (a year of bookings is never read row by row, R1p review 19).
        /// Corrections that wait for approval are not in it.
        /// </summary>
        internal static IQueryable<BookingHoursRow> HoursByCentreAndDay(IQueryable<MfTimeBooking> bookings, IQueryable<MfOperation> operations, DateTime since, DateTime until, int? workCenterId, int? orderId) =>
            from b in MfBookingRules.Effective(bookings.Where(x => !x.Deleted && x.EndAt != null && x.StartAt >= since && x.StartAt < until))
            join o in operations on b.OperationId equals o.Id
            where (!workCenterId.HasValue || o.WorkCenterId == workCenterId.Value) && (!orderId.HasValue || o.OrderId == orderId.Value)
            group b by new { o.WorkCenterId, Day = b.StartAt.Date } into g
            select new BookingHoursRow { WorkCenterId = g.Key.WorkCenterId, Day = g.Key.Day, Bookings = g.Count(), Hours = g.Sum(x => x.Hours) };

        public async Task<ResponseDto<List<BookingHoursDto>>> GetBookingHoursAsync(BookingHoursQuery query, CancellationToken ct)
        {
            // the operators see their own bookings only, so the hours of everybody else are not for them
            if (!_user.IsInRole(ProductionRoles.Readers + ",PAYROLL")) return Bad<List<BookingHoursDto>>(MfText.NotAllowed);
            query ??= new BookingHoursQuery();
            DateTime lastDay = (query.To ?? DateTime.Today).Date;
            DateTime firstDay = (query.From ?? lastDay.AddDays(-30)).Date;
            if (lastDay < firstDay || (lastDay - firstDay).TotalDays > 366) return Bad<List<BookingHoursDto>>("بازه باید حداکثر ۳۶۷ روز باشد.");
            DateTime upper = lastDay.AddDays(1);
            // R1p review 19: grouped in the database (a year of bookings is not read row by row); corrections count after their approval only
            List<BookingHoursRow> rows = await HoursByCentreAndDay(_uow.Set<MfTimeBooking>().AsNoTracking(), _uow.Set<MfOperation>().AsNoTracking(), firstDay, upper, query.WorkCenterId, query.OrderId).ToListAsync(ct);
            List<int> centerIds = rows.Where(r => r.WorkCenterId.HasValue).Select(r => r.WorkCenterId.Value).Distinct().ToList();
            var centers = centerIds.Count == 0 ? new Dictionary<int, MfWorkCenter>() : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
            List<BookingHoursDto> list = rows.OrderBy(r => r.Day).ThenBy(r => r.WorkCenterId ?? 0).Select(r =>
            {
                MfWorkCenter wc = r.WorkCenterId.HasValue && centers.TryGetValue(r.WorkCenterId.Value, out MfWorkCenter c) ? c : null;
                return new BookingHoursDto
                {
                    WorkCenterId = r.WorkCenterId, WorkCenterCode = wc?.Code, WorkCenterTitle = wc?.Title, Date = r.Day, DateText = MfText.Date(r.Day),
                    Bookings = r.Bookings, Hours = Math.Round(r.Hours, 2, MidpointRounding.AwayFromZero),
                };
            }).ToList();
            return ResponseDtoHelper.Ok(list);
        }

        // ================================================================================== attendance versus booking

        public async Task<ResponseDto<PagedList<AttendanceDifferenceDto>>> GetAttendanceDifferencesAsync(AttendanceDifferenceQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.AttendanceCheck)) return Bad<PagedList<AttendanceDifferenceDto>>(MfText.NotAllowed);
            if (query == null) return Bad<PagedList<AttendanceDifferenceDto>>(MfText.InvalidData);
            DateTime from = query.From.Date, to = query.To.Date;
            if (to < from || (to - from).TotalDays > 92) return Bad<PagedList<AttendanceDifferenceDto>>("بازه باید حداکثر ۹۳ روز باشد.");
            if (query.ToleranceMinutes < 0 || query.ToleranceMinutes > 600) return Bad<PagedList<AttendanceDifferenceDto>>("حد مجاز اختلاف نامعتبر است.");
            DateTime upper = to.AddDays(1);

            IQueryable<MfTimeBooking> bookings = MfBookingRules.Effective(_uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.EndAt != null && b.StartAt >= from && b.StartAt < upper));
            List<int> wanted = (query.EmployeeIds ?? new List<int>()).ToList();
            if (query.EmployeeId.HasValue) wanted.Add(query.EmployeeId.Value);
            if (wanted.Count > 0) bookings = bookings.Where(b => wanted.Contains(b.EmployeeId));
            var booked = (await bookings.Select(b => new { b.EmployeeId, b.StartAt, b.Minutes }).ToListAsync(ct))
                .GroupBy(b => new { b.EmployeeId, Date = b.StartAt.Date }).ToDictionary(g => (g.Key.EmployeeId, g.Key.Date), g => g.Sum(x => x.Minutes));

            var employeeIds = new HashSet<int>(booked.Keys.Select(k => k.EmployeeId));
            foreach (int id in wanted) employeeIds.Add(id);
            var attended = new Dictionary<(int, DateTime), int>();
            if (employeeIds.Count > 0)
            {
                ResponseDto<List<WorkedHoursDto>> worked = await _attendance.GetWorkedHoursAsync(new WorkedHoursQuery { EmployeeIds = employeeIds.ToList(), From = from, To = to }, ct);
                if (!worked.Success) return Bad<PagedList<AttendanceDifferenceDto>>(worked.Message);
                foreach (WorkedHoursDto w in worked.Data ?? new List<WorkedHoursDto>()) attended[(w.EmployeeId, w.Date.Date)] = w.WorkedMinutes;
            }

            Dictionary<int, string> names = employeeIds.Count == 0 ? new Dictionary<int, string>()
                : MfNames.ToDictionary(await _uow.Set<FnEmployee>().AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToListAsync(ct));
            var rows = new List<AttendanceDifferenceDto>();
            foreach ((int employeeId, DateTime date) in attended.Keys.Union(booked.Keys).Distinct().OrderBy(k => k.Item1).ThenBy(k => k.Item2))
            {
                attended.TryGetValue((employeeId, date), out int attMinutes);
                booked.TryGetValue((employeeId, date), out int bookedMinutes);
                int diff = attMinutes - bookedMinutes;
                if (Math.Abs(diff) <= query.ToleranceMinutes) continue;
                string kind = attMinutes == 0 ? "NoAttendance" : diff > 0 ? "Unbooked" : "OverBooked";
                rows.Add(new AttendanceDifferenceDto
                {
                    EmployeeId = employeeId, EmployeeName = names.TryGetValue(employeeId, out string n) ? n : null, Date = date, DateText = MfText.Date(date),
                    AttendanceHours = MfText.HoursOf(attMinutes), BookedHours = MfText.HoursOf(bookedMinutes), DifferenceHours = MfText.HoursOf(attMinutes) - MfText.HoursOf(bookedMinutes), Kind = kind,
                    Message = kind == "NoAttendance" ? "زمان ثبت شده ولی تردد ثبت نشده است."
                        : kind == "Unbooked" ? $"{MfText.HoursOf(diff)} ساعت از حضور روی هیچ عملیاتی ثبت نشده است."
                        : $"{MfText.HoursOf(-diff)} ساعت بیشتر از حضور روی عملیات‌ها ثبت شده است.",
                });
            }
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<AttendanceDifferenceDto> page = rows.Skip((number - 1) * size).Take(size).ToList();
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(page, 1, int.MaxValue, ct), rows.Count, number, size));
        }
    }
}
