using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Production;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ServiceDtos;

namespace Services.NasbNiro.AfterSales
{
    public interface ISvOrderService
    {
        Task<ResponseDto<PagedList<SvOrderDto>>> GetOrdersAsync(SvOrderQuery query, CancellationToken ct);
        Task<ResponseDto<SvOrderDto>> GetOrderAsync(int id, CancellationToken ct);
        Task<ResponseDto<SvOrderDto>> SaveOrderAsync(SvSaveOrderDto dto, CancellationToken ct);
        Task<ResponseDto<SvOrderDto>> OrderActionAsync(SvOrderActionDto dto, CancellationToken ct);
        Task<ResponseDto<SvOrderDto>> SaveTimeEntryAsync(SvSaveTimeEntryDto dto, CancellationToken ct);
        Task<ResponseDto<SvOrderDto>> RemoveTimeEntryAsync(SvRemoveRowDto dto, CancellationToken ct);
        Task<ResponseDto<SvOrderDto>> SaveMeasurementAsync(SvSaveMeasurementDto dto, CancellationToken ct);
        Task<ResponseDto<SvOrderDto>> RemoveMeasurementAsync(SvRemoveRowDto dto, CancellationToken ct);
        Task<ResponseDto<SvOrderDto>> DecideAsync(SvDecisionDto dto, CancellationToken ct);
        Task<ResponseDto<SvOrderDto>> RequestGoodwillAsync(SvGoodwillRequestDto dto, CancellationToken ct);
        Task<ResponseDto<SvOrderDto>> DraftInvoiceAsync(SvDraftInvoiceDto dto, CancellationToken ct);
    }

    /// <summary>Service orders: field or workshop work on an installed unit, from planning to the signed report, the cost posting, the warranty decision and the invoice of billable work.</summary>
    public class SvOrderService : ISvOrderService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;
        private readonly INotificationService _notify;

        public SvOrderService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => SlText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => SlText.Bad<T>(m);

        // ================================================================================== dto

        private static bool Open(SvOrderStatus s) => s == SvOrderStatus.Draft || s == SvOrderStatus.Planned || s == SvOrderStatus.InProgress;

        private async Task<List<SvOrderDto>> DtosAsync(List<SvServiceOrder> rows, CancellationToken ct)
        {
            List<int> ids = rows.Select(r => r.Id).ToList();
            List<int> unitIds = rows.Select(r => r.UnitId).Distinct().ToList();
            List<int> customerIds = rows.Where(r => r.CustomerId.HasValue).Select(r => r.CustomerId.Value).Distinct().ToList();
            List<int> requestIds = rows.Where(r => r.RequestId.HasValue).Select(r => r.RequestId.Value).Distinct().ToList();
            List<int> ltsaIds = rows.Where(r => r.LtsaContractId.HasValue).Select(r => r.LtsaContractId.Value).Distinct().ToList();
            List<int> employeeIds = rows.SelectMany(r => r.Engineers.Select(e => e.EmployeeId).Concat(r.TimeEntries.Select(t => t.EmployeeId))).Distinct().ToList();
            List<int> invoiceIds = rows.Where(r => r.InvoiceId.HasValue).Select(r => r.InvoiceId.Value).Distinct().ToList();
            Dictionary<int, string> units = await _uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Tag, ct);
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            Dictionary<int, string> requests = await _uow.Set<SvServiceRequest>().AsNoTracking().Where(r => requestIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, ct);
            Dictionary<int, string> ltsas = await _uow.Set<SvContract>().AsNoTracking().Where(c => ltsaIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, ct);
            Dictionary<int, string> invoices = await _uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
            var employees = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => employeeIds.Contains(e.Id)).Select(e => new { e.Id, e.FirstName, e.LastName, e.PersonnelCode }).ToListAsync(ct);
            Dictionary<int, string> employeeNames = employees.ToDictionary(e => e.Id, e => $"{e.FirstName} {e.LastName}".Trim() is { Length: > 0 } n ? n : e.PersonnelCode);

            var docs = await _uow.Set<InventoryDocument>().AsNoTracking().Include(d => d.Lines)
                .Where(d => d.ServiceOrderId != null && ids.Contains(d.ServiceOrderId.Value) && !d.Deleted && d.Status != InventoryDocumentStatus.Cancelled).OrderBy(d => d.Id).ToListAsync(ct);
            List<int> itemIds = docs.SelectMany(d => d.Lines.Select(l => l.ItemId)).Distinct().ToList();
            Dictionary<int, InventoryItem> items = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            bool seesPayroll = _user.IsInRole(SvRoles.PayrollSeers);

            return rows.Select(r =>
            {
                bool prot = SvPrivacy.LabourProtected(r.LabourEmployeeCount, r.LabourPayrollEmployeeCount);
                decimal? labour = r.CostPostedAt.HasValue ? MfCostMask.Labour(r.LabourAmount, prot, seesPayroll) : (decimal?)0m;
                bool hidden = r.CostPostedAt.HasValue && prot && !seesPayroll;
                decimal travel = r.TimeEntries.Where(t => t.Kind == SvTimeKind.Travel).Sum(t => t.Hours);
                decimal work = r.TimeEntries.Where(t => t.Kind == SvTimeKind.Work).Sum(t => t.Hours);
                decimal waiting = r.TimeEntries.Where(t => t.Kind == SvTimeKind.Waiting).Sum(t => t.Hours);
                return new SvOrderDto
                {
                    TouchedByMe = LedgerMath.Touched(r.CreatorUserId, r.EditorUserIds, Me),
                    Id = r.Id, Number = r.Number, RequestId = r.RequestId, RequestNumber = r.RequestId.HasValue ? requests.GetValueOrDefault(r.RequestId.Value) : null, UnitId = r.UnitId, UnitTag = units.GetValueOrDefault(r.UnitId),
                    CustomerId = r.CustomerId, CustomerName = r.CustomerId.HasValue ? customers.GetValueOrDefault(r.CustomerId.Value) : null, ContractId = r.ContractId, LtsaContractId = r.LtsaContractId,
                    LtsaNumber = r.LtsaContractId.HasValue ? ltsas.GetValueOrDefault(r.LtsaContractId.Value) : null, Kind = r.Kind, Status = r.Status, StatusText = SvText.OrderStatus(r.Status), Title = r.Title,
                    PlannedStart = r.PlannedStart, PlannedStartText = SlText.Date(r.PlannedStart), PlannedEnd = r.PlannedEnd, PlannedEndText = SlText.Date(r.PlannedEnd), TravelNote = r.TravelNote,
                    StartedAt = r.StartedAt, StartedAtText = SlText.Date(r.StartedAt), CostCenterCode = r.CostCenterCode,
                    Decision = r.Decision, DecisionText = SvText.Decision(r.Decision), CauseIsMisuse = r.CauseIsMisuse, DecisionNote = r.DecisionNote, DecidedAtText = SlText.Stamp(r.DecidedAt), DecidedByUserId = r.DecidedByUserId,
                    GoodwillRequested = r.GoodwillRequestedAt.HasValue, GoodwillReason = r.GoodwillReason, WarrantyTestedOn = r.WarrantyTestedOn, WarrantyTestedOnText = SlText.Date(r.WarrantyTestedOn),
                    ReportDocumentId = r.ReportDocumentId, Findings = r.Findings, Recommendations = r.Recommendations, RunningHoursReading = r.RunningHoursReading, ClosedAtText = SlText.Stamp(r.ClosedAt),
                    Engineers = r.Engineers.OrderBy(e => e.Id).Select(e => new SvEngineerDto { EmployeeId = e.EmployeeId, Name = employeeNames.GetValueOrDefault(e.EmployeeId) }).ToList(),
                    Tasks = r.Tasks.OrderBy(t => t.RowNumber).Select(t => new SvTaskDto { Id = t.Id, RowNumber = t.RowNumber, Description = t.Description, IsDone = t.IsDone }).ToList(),
                    TimeEntries = r.TimeEntries.OrderBy(t => t.WorkDate).ThenBy(t => t.Id).Select(t => new SvTimeEntryDto
                    {
                        Id = t.Id, EmployeeId = t.EmployeeId, EmployeeName = employeeNames.GetValueOrDefault(t.EmployeeId), WorkDate = t.WorkDate, WorkDateText = SlText.Date(t.WorkDate), Hours = t.Hours, Kind = t.Kind, KindText = SvText.TimeKind(t.Kind), Note = t.Note,
                    }).ToList(),
                    Measurements = r.Measurements.OrderBy(m => m.Id).Select(m => new SvMeasurementDto { Id = m.Id, Name = m.Name, Value = m.Value, Unit = m.Unit, Note = m.Note }).ToList(),
                    Parts = docs.Where(d => d.ServiceOrderId == r.Id).Select(d => new SvPartDocumentDto
                    {
                        DocumentId = d.Id, Number = d.Number, Type = d.Type, Status = d.Status,
                        Lines = d.Lines.OrderBy(l => l.RowNumber).Select(l => new SvPartLineDto { ItemId = l.ItemId, ItemCode = items.GetValueOrDefault(l.ItemId)?.Code, ItemName = items.GetValueOrDefault(l.ItemId)?.Title, Quantity = l.Quantity }).ToList(),
                    }).ToList(),
                    TravelHours = travel, WorkHours = work, WaitingHours = waiting, TotalHours = travel + work + waiting,
                    LabourHours = r.CostPostedAt.HasValue ? r.LabourHours : travel + work + waiting, LabourAmount = labour, LabourHidden = hidden, LabourAggregatePending = r.LabourPendingPeriod.HasValue && !r.LabourAggregatedAt.HasValue, LabourAggregatePeriod = r.LabourPendingPeriod, PartsAmount = r.PartsAmount,
                    TotalCost = labour.HasValue ? labour.Value + r.PartsAmount : (decimal?)null, CostPosted = r.CostPostedAt.HasValue,
                    BillableLabourRateRial = r.BillableLabourRateRial, InvoiceId = r.InvoiceId, InvoiceNumber = r.InvoiceId.HasValue ? invoices.GetValueOrDefault(r.InvoiceId.Value) : null, DraftNote = r.DraftNote,
                    RowVersion = SlConcurrency.Text(r.RowVersion),
                };
            }).ToList();
        }

        private IQueryable<SvServiceOrder> Full() => _uow.Set<SvServiceOrder>().Include(o => o.Engineers).Include(o => o.Tasks).Include(o => o.TimeEntries).Include(o => o.Measurements);

        public async Task<ResponseDto<PagedList<SvOrderDto>>> GetOrdersAsync(SvOrderQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<PagedList<SvOrderDto>>(SlText.NotAllowed);
            query ??= new SvOrderQuery();
            IQueryable<SvServiceOrder> q = _uow.Set<SvServiceOrder>().AsNoTracking().Where(o => !o.Deleted);
            if (query.Status.HasValue) q = q.Where(o => o.Status == query.Status.Value);
            if (query.RequestId.HasValue) q = q.Where(o => o.RequestId == query.RequestId.Value);
            if (query.UnitId.HasValue) q = q.Where(o => o.UnitId == query.UnitId.Value);
            if (query.ContractId.HasValue) q = q.Where(o => o.ContractId == query.ContractId.Value);
            if (query.Decision.HasValue) q = q.Where(o => o.Decision == query.Decision.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(o => o.Number.Contains(text) || o.Title.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(o => o.Id), query.PageNumber, query.PageSize, async rows =>
            {
                List<int> ids = rows.Select(r => r.Id).ToList();
                List<SvServiceOrder> full = await Full().AsNoTracking().Where(o => ids.Contains(o.Id)).ToListAsync(ct);
                return await DtosAsync(ids.Select(id => full.First(f => f.Id == id)).ToList(), ct);
            }, ct));
        }

        public async Task<ResponseDto<SvOrderDto>> GetOrderAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<SvOrderDto>(SlText.NotAllowed);
            SvServiceOrder row = await Full().AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && !o.Deleted, ct);
            if (row == null) return SlText.NotFound<SvOrderDto>("دستور خدمات پیدا نشد.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<SvServiceOrder> { row }, ct))[0]);
        }

        private async Task<ResponseDto<SvOrderDto>> SavedAsync(SvServiceOrder row, CancellationToken ct)
        {
            await SvGates.GoodwillAsync(_notify, row, ct);
            await SvGates.CloseAsync(_notify, row, ct);
            await SvGates.VisitAsync(_notify, row, ct);
            return await GetOrderAsync(row.Id, ct);
        }

        private async Task<(SvServiceOrder Order, ResponseDto<SvOrderDto> Error)> LoadOpenAsync(int id, bool forDecision, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Writers) && !(forDecision && _user.IsInRole(SvRoles.GoodwillDeciders))) return (null, Bad<SvOrderDto>(SlText.NotAllowed));
            SvServiceOrder row = await Full().FirstOrDefaultAsync(o => o.Id == id && !o.Deleted, ct);
            if (row == null) return (null, SlText.NotFound<SvOrderDto>("دستور خدمات پیدا نشد."));
            if (!Open(row.Status)) return (null, Bad<SvOrderDto>($"دستور خدمات در وضعیت «{SvText.OrderStatus(row.Status)}» است و تغییر نمی‌کند."));
            return (row, null);
        }

        // ================================================================================== save

        public async Task<ResponseDto<SvOrderDto>> SaveOrderAsync(SvSaveOrderDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Writers)) return Bad<SvOrderDto>(SlText.NotAllowed);
            if (dto == null || Clean(dto.Title) == null) return Bad<SvOrderDto>("عنوان دستور خدمات را وارد کنید.");
            if (!Enum.IsDefined(typeof(SvOrderKind), dto.Kind)) return Bad<SvOrderDto>(SlText.InvalidData);
            if (dto.PlannedStart.HasValue && dto.PlannedEnd.HasValue && dto.PlannedEnd.Value.Date < dto.PlannedStart.Value.Date) return Bad<SvOrderDto>("پایان برنامه قبل از شروع آن است.");
            if (dto.BillableLabourRateRial.HasValue && dto.BillableLabourRateRial.Value <= 0) return Bad<SvOrderDto>("نرخ ساعتی صورتحساب باید بیشتر از صفر باشد.");
            if (dto.RunningHoursReading.HasValue && dto.RunningHoursReading.Value < 0) return Bad<SvOrderDto>("عدد کارکرد نمی‌تواند منفی باشد.");
            string tooLong = SvLimits.First((dto.Title, SvLimits.Title, "عنوان دستور خدمات"), (dto.TravelNote, SvLimits.Text, "یادداشت سفر"), (dto.Findings, SvLimits.LongText, "یافته‌ها"), (dto.Recommendations, SvLimits.LongText, "توصیه‌ها"));
            if (tooLong != null) return Bad<SvOrderDto>(tooLong);
            SvInstalledUnit unit = await _uow.Set<SvInstalledUnit>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == dto.UnitId && !u.Deleted, ct);
            if (unit == null) return Bad<SvOrderDto>("واحد نصب‌شده پیدا نشد.");
            if (unit.Decommissioned) return Bad<SvOrderDto>("واحد از رده خارج شده است.");
            if (dto.RunningHoursReading.HasValue && dto.RunningHoursReading.Value < unit.RunningHours) return Bad<SvOrderDto>($"عدد کارکرد ({SlText.Number(dto.RunningHoursReading.Value)}) از کارکرد ثبت‌شده‌ی واحد ({SlText.Number(unit.RunningHours)}) کمتر است.");
            SvServiceRequest request = null;
            if (dto.RequestId.HasValue)
            {
                request = await _uow.Set<SvServiceRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.RequestId.Value && !r.Deleted, ct);
                if (request == null) return Bad<SvOrderDto>("درخواست خدمات پیدا نشد.");
                if (request.Status == SvRequestStatus.Closed || request.Status == SvRequestStatus.Resolved) return Bad<SvOrderDto>("درخواست خدمات رفع یا بسته شده است.");
                if (request.UnitId.HasValue && request.UnitId.Value != dto.UnitId) return Bad<SvOrderDto>("واحد دستور خدمات با واحد درخواست یکی نیست.");
            }
            List<int> engineerIds = (dto.EngineerEmployeeIds ?? new List<int>()).Distinct().ToList();
            if (engineerIds.Count > 0 && await _uow.Set<FnEmployee>().CountAsync(e => engineerIds.Contains(e.Id), ct) != engineerIds.Count) return Bad<SvOrderDto>("یکی از مهندسان پیدا نشد.");
            List<SvSaveTaskDto> tasks = (dto.Tasks ?? new List<SvSaveTaskDto>()).Where(t => t != null).ToList();
            if (tasks.Any(t => Clean(t.Description) == null)) return Bad<SvOrderDto>("شرح هر وظیفه را وارد کنید.");
            if (tasks.Select(t => SvLimits.TooLong(Clean(t.Description), SvLimits.Text, "شرح وظیفه")).FirstOrDefault(m => m != null) is string taskTooLong) return Bad<SvOrderDto>(taskTooLong);
            if (dto.ReportDocumentId.HasValue && !await _uow.Set<Entities.Model.Document>().AnyAsync(d => d.Id == dto.ReportDocumentId.Value, ct)) return Bad<SvOrderDto>("فایل گزارش پیدا نشد.");

            DateTimeOffset now = DateTimeOffset.Now;
            SvServiceOrder row;
            if (dto.Id.HasValue)
            {
                (SvServiceOrder order, ResponseDto<SvOrderDto> error) = await LoadOpenAsync(dto.Id.Value, false, ct);
                if (error != null) return error;
                row = order;
                if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SvOrderDto>(SlText.InvalidData);
                if (row.UnitId != dto.UnitId && row.TimeEntries.Count > 0) return Bad<SvOrderDto>("واحد دستور خدماتِ دارای ساعت کار تغییر نمی‌کند.");
                if (row.Status == SvOrderStatus.InProgress && engineerIds.Count > 0)
                {
                    List<int> removed = row.Engineers.Select(e => e.EmployeeId).Except(engineerIds).ToList();
                    if (row.TimeEntries.Any(t => removed.Contains(t.EmployeeId))) return Bad<SvOrderDto>("مهندسی که ساعت کار دارد از دستور حذف نمی‌شود.");
                }
            }
            else
            {
                row = new SvServiceOrder
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, SvNumbers.Order, "SVO", DateTime.Today, ct), Status = SvOrderStatus.Planned,
                    CostCenterCode = await SvSettings.EnsureCostCenterAsync(_uow, ct), CreateDate = now, CreatorUserId = Me,
                };
                _uow.Set<SvServiceOrder>().Add(row);
            }
            row.RequestId = dto.RequestId; row.UnitId = dto.UnitId; row.CustomerId = request?.CustomerId ?? unit.CustomerId; row.ContractId = unit.ContractId; row.Kind = dto.Kind; row.Title = Clean(dto.Title);
            row.PlannedStart = dto.PlannedStart?.Date; row.PlannedEnd = dto.PlannedEnd?.Date; row.TravelNote = Clean(dto.TravelNote);
            row.WarrantyTestedOn = (request?.ReceivedAt.Date) ?? row.PlannedStart ?? row.WarrantyTestedOn ?? DateTime.Today;
            row.Findings = Clean(dto.Findings); row.Recommendations = Clean(dto.Recommendations); row.RunningHoursReading = dto.RunningHoursReading; row.ReportDocumentId = dto.ReportDocumentId;
            if (dto.BillableLabourRateRial.HasValue || !dto.Id.HasValue) row.BillableLabourRateRial = dto.BillableLabourRateRial;
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            row.ModifyDate = now; row.ModifierUserId = Me;

            if (dto.EngineerEmployeeIds != null)
            {
                foreach (SvOrderEngineer e in row.Engineers.Where(e => !engineerIds.Contains(e.EmployeeId)).ToList()) { row.Engineers.Remove(e); _uow.Set<SvOrderEngineer>().Remove(e); }
                foreach (int id in engineerIds.Where(id => row.Engineers.All(e => e.EmployeeId != id))) row.Engineers.Add(new SvOrderEngineer { EmployeeId = id });
            }
            if (dto.Tasks != null)
            {
                foreach (SvOrderTask t in row.Tasks.ToList()) { row.Tasks.Remove(t); _uow.Set<SvOrderTask>().Remove(t); }
                int n = 0;
                foreach (SvSaveTaskDto t in tasks) row.Tasks.Add(new SvOrderTask { RowNumber = ++n, Description = Clean(t.Description), IsDone = t.IsDone });
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvOrderDto>(SlText.Concurrency); }
            return await SavedAsync(row, ct);
        }

        // ================================================================================== time entries and measurements

        public async Task<ResponseDto<SvOrderDto>> SaveTimeEntryAsync(SvSaveTimeEntryDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SvOrderDto>(SlText.InvalidData);
            (SvServiceOrder row, ResponseDto<SvOrderDto> error) = await LoadOpenAsync(dto.OrderId, false, ct);
            if (error != null) return error;
            if (row.Status == SvOrderStatus.Draft) return Bad<SvOrderDto>("دستور پیش‌نویس ابتدا باید تأیید شود.");
            if (!Enum.IsDefined(typeof(SvTimeKind), dto.Kind)) return Bad<SvOrderDto>(SlText.InvalidData);
            if (dto.Hours <= 0 || dto.Hours > 24) return Bad<SvOrderDto>("ساعت هر ردیف باید بیشتر از صفر و حداکثر ۲۴ باشد.");
            string noteTooLong = SvLimits.TooLong(Clean(dto.Note), SvLimits.Text, "یادداشت ردیف ساعت");
            if (noteTooLong != null) return Bad<SvOrderDto>(noteTooLong);
            if (row.Engineers.All(e => e.EmployeeId != dto.EmployeeId)) return Bad<SvOrderDto>("این کارمند مهندس دستور خدمات نیست.");
            DateTime day = (dto.WorkDate ?? DateTime.Today).Date;
            if (day > DateTime.Today.AddDays(1)) return Bad<SvOrderDto>("تاریخ کار در آینده است.");
            decimal that = row.TimeEntries.Where(t => t.EmployeeId == dto.EmployeeId && t.WorkDate == day).Sum(t => t.Hours);
            if (that + dto.Hours > 24) return Bad<SvOrderDto>("جمع ساعت‌های یک کارمند در یک روز بیشتر از ۲۴ می‌شود.");
            row.TimeEntries.Add(new SvTimeEntry { EmployeeId = dto.EmployeeId, WorkDate = day, Hours = dto.Hours, Kind = dto.Kind, Note = Clean(dto.Note), CreatedByUserId = Me });
            Touch(row);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvOrderDto>(SlText.Concurrency); }
            return await GetOrderAsync(row.Id, ct);
        }

        public async Task<ResponseDto<SvOrderDto>> RemoveTimeEntryAsync(SvRemoveRowDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SvOrderDto>(SlText.InvalidData);
            (SvServiceOrder row, ResponseDto<SvOrderDto> error) = await LoadOpenAsync(dto.OrderId, false, ct);
            if (error != null) return error;
            SvTimeEntry entry = row.TimeEntries.FirstOrDefault(t => t.Id == dto.RowId);
            if (entry == null) return SlText.NotFound<SvOrderDto>("ردیف ساعت پیدا نشد.");
            row.TimeEntries.Remove(entry);
            _uow.Set<SvTimeEntry>().Remove(entry);
            Touch(row);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvOrderDto>(SlText.Concurrency); }
            return await GetOrderAsync(row.Id, ct);
        }

        public async Task<ResponseDto<SvOrderDto>> SaveMeasurementAsync(SvSaveMeasurementDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SvOrderDto>(SlText.InvalidData);
            (SvServiceOrder row, ResponseDto<SvOrderDto> error) = await LoadOpenAsync(dto.OrderId, false, ct);
            if (error != null) return error;
            if (Clean(dto.Name) == null || Clean(dto.Unit) == null) return Bad<SvOrderDto>("نام و واحد اندازه‌گیری را وارد کنید.");
            string measureTooLong = SvLimits.First((dto.Name, SvLimits.Name, "نام اندازه‌گیری"), (dto.Unit, SvLimits.Code, "واحد اندازه‌گیری"), (dto.Note, SvLimits.Text, "یادداشت اندازه‌گیری"));
            if (measureTooLong != null) return Bad<SvOrderDto>(measureTooLong);
            row.Measurements.Add(new SvMeasurement { Name = Clean(dto.Name), Value = dto.Value, Unit = Clean(dto.Unit), Note = Clean(dto.Note) });
            Touch(row);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvOrderDto>(SlText.Concurrency); }
            return await GetOrderAsync(row.Id, ct);
        }

        public async Task<ResponseDto<SvOrderDto>> RemoveMeasurementAsync(SvRemoveRowDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SvOrderDto>(SlText.InvalidData);
            (SvServiceOrder row, ResponseDto<SvOrderDto> error) = await LoadOpenAsync(dto.OrderId, false, ct);
            if (error != null) return error;
            SvMeasurement m = row.Measurements.FirstOrDefault(x => x.Id == dto.RowId);
            if (m == null) return SlText.NotFound<SvOrderDto>("ردیف اندازه‌گیری پیدا نشد.");
            row.Measurements.Remove(m);
            _uow.Set<SvMeasurement>().Remove(m);
            Touch(row);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvOrderDto>(SlText.Concurrency); }
            return await GetOrderAsync(row.Id, ct);
        }

        private void Touch(SvServiceOrder row)
        {
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            row.ModifyDate = DateTimeOffset.Now;
            row.ModifierUserId = Me;
        }

        // ================================================================================== warranty decision

        private async Task<bool> CoveredAsync(SvServiceOrder row, CancellationToken ct)
        {
            SvInstalledUnit unit = await _uow.Set<SvInstalledUnit>().AsNoTracking().FirstAsync(u => u.Id == row.UnitId, ct);
            return !unit.Decommissioned && SvWarranty.Covers(unit.WarrantyStart, unit.WarrantyEnd, row.WarrantyTestedOn ?? DateTime.Today);
        }

        public async Task<ResponseDto<SvOrderDto>> RequestGoodwillAsync(SvGoodwillRequestDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SvOrderDto>(SlText.InvalidData);
            (SvServiceOrder row, ResponseDto<SvOrderDto> error) = await LoadOpenAsync(dto.OrderId, false, ct);
            if (error != null) return error;
            if (Clean(dto.Reason) == null) return Bad<SvOrderDto>("دلیل درخواست حسن نیت را وارد کنید.");
            string reasonTooLong = SvLimits.TooLong(Clean(dto.Reason), SvLimits.Text, "دلیل درخواست حسن نیت");
            if (reasonTooLong != null) return Bad<SvOrderDto>(reasonTooLong);
            row.GoodwillRequestedAt = DateTimeOffset.Now; row.GoodwillRequestedByUserId = Me; row.GoodwillReason = Clean(dto.Reason);
            Touch(row);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvOrderDto>(SlText.Concurrency); }
            return await SavedAsync(row, ct);
        }

        public async Task<ResponseDto<SvOrderDto>> DecideAsync(SvDecisionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SvOrderDto>(SlText.InvalidData);
            (SvServiceOrder row, ResponseDto<SvOrderDto> error) = await LoadOpenAsync(dto.OrderId, true, ct);
            if (error != null) return error;
            // a goodwill decision is the CEO's: once taken, nobody else changes it
            if (row.Decision == SvWarrantyDecision.Goodwill && !_user.IsInRole(SvRoles.GoodwillDeciders)) return Bad<SvOrderDto>("تصمیم حسن نیت گرفته شده است و آن را فقط مدیرعامل تغییر می‌دهد.");
            string noteTooLong = SvLimits.TooLong(Clean(dto.Note), SvLimits.Text, "یادداشت تصمیم");
            if (noteTooLong != null) return Bad<SvOrderDto>(noteTooLong);
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SvOrderDto>(SlText.InvalidData);
            if (dto.Decision == SvWarrantyDecision.Goodwill)
            {
                if (!_user.IsInRole(SvRoles.GoodwillDeciders)) return Bad<SvOrderDto>("تصمیم حسن نیت را فقط مدیرعامل می‌گیرد.");
            }
            else if (dto.Decision == SvWarrantyDecision.Warranty || dto.Decision == SvWarrantyDecision.Billable)
            {
                if (!_user.IsInRole(SvRoles.Writers)) return Bad<SvOrderDto>(SlText.NotAllowed);
                if (LedgerMath.Touched(row.CreatorUserId, row.EditorUserIds, Me)) return Bad<SvOrderDto>(SlText.SameUser);
            }
            else return Bad<SvOrderDto>("تصمیم گارانتی نامعتبر است.");
            string rule = SvWarranty.CheckDecision(dto.Decision, await CoveredAsync(row, ct), dto.CauseIsMisuse, row.GoodwillRequestedAt.HasValue);
            if (rule != null) return Bad<SvOrderDto>(rule);
            row.Decision = dto.Decision; row.CauseIsMisuse = dto.CauseIsMisuse; row.DecisionNote = Clean(dto.Note); row.DecidedByUserId = Me; row.DecidedAt = DateTimeOffset.Now;
            row.ModifyDate = DateTimeOffset.Now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvOrderDto>(SlText.Concurrency); }
            return await SavedAsync(row, ct);
        }

        // ================================================================================== actions

        public async Task<ResponseDto<SvOrderDto>> OrderActionAsync(SvOrderActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SvOrderDto>(SlText.InvalidData);
            (SvServiceOrder row, ResponseDto<SvOrderDto> error) = await LoadOpenAsync(dto.Id, false, ct);
            if (error != null) return error;
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SvOrderDto>(SlText.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            switch ((dto.Action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "confirm":
                    if (row.Status != SvOrderStatus.Draft) return Bad<SvOrderDto>("فقط دستور پیش‌نویس تأیید می‌شود.");
                    row.Status = SvOrderStatus.Planned;
                    break;
                case "start":
                    if (row.Status != SvOrderStatus.Planned) return Bad<SvOrderDto>("فقط دستور برنامه‌ریزی‌شده شروع می‌شود.");
                    if (row.Engineers.Count == 0) return Bad<SvOrderDto>("حداقل یک مهندس برای دستور انتخاب کنید.");
                    row.Status = SvOrderStatus.InProgress; row.StartedAt = now.LocalDateTime.Date;
                    break;
                case "cancel":
                    if (Clean(dto.Reason) == null) return Bad<SvOrderDto>("دلیل لغو را وارد کنید.");
                    if (await _uow.Set<InventoryDocument>().AnyAsync(d => d.ServiceOrderId == row.Id && !d.Deleted && d.Status == InventoryDocumentStatus.Confirmed, ct))
                        return Bad<SvOrderDto>("دستور سند قطعات تأییدشده دارد؛ دستور بسته می‌شود، لغو نمی‌شود.");
                    if (await _uow.Set<InventoryDocument>().AnyAsync(d => d.ServiceOrderId == row.Id && !d.Deleted && d.Status == InventoryDocumentStatus.Draft, ct))
                        return Bad<SvOrderDto>("دستور سند قطعات پیش‌نویس دارد؛ آن را تأیید یا حذف کنید و سپس دستور را لغو کنید.");
                    row.Status = SvOrderStatus.Cancelled;
                    break;
                case "close":
                    ResponseDto<SvOrderDto> closed = await CloseAsync(row, ct);
                    return closed;
                default:
                    return Bad<SvOrderDto>("عملیات نامعتبر است.");
            }
            Touch(row);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvOrderDto>(SlText.Concurrency); }
            return await SavedAsync(row, ct);
        }

        // ================================================================================== closing and cost

        private async Task<ResponseDto<SvOrderDto>> CloseAsync(SvServiceOrder row, CancellationToken ct)
        {
            if (row.Status != SvOrderStatus.InProgress) return Bad<SvOrderDto>("فقط دستورِ در حال انجام بسته می‌شود.");
            if (!row.ReportDocumentId.HasValue) return Bad<SvOrderDto>("برای بستن، گزارش خدمات امضاشده توسط مشتری را پیوست کنید.");
            if (row.Decision == SvWarrantyDecision.None && !row.LtsaContractId.HasValue) return Bad<SvOrderDto>("برای بستن، تصمیم گارانتی (گارانتی، قابل‌فاکتور یا حسن نیت) گرفته شود.");
            if (row.Decision == SvWarrantyDecision.Warranty)
            {
                string rule = SvWarranty.CheckDecision(SvWarrantyDecision.Warranty, await CoveredAsync(row, ct), row.CauseIsMisuse, row.GoodwillRequestedAt.HasValue);
                if (rule != null) return Bad<SvOrderDto>(rule);
            }
            if (await _uow.Set<InventoryDocument>().AnyAsync(d => d.ServiceOrderId == row.Id && !d.Deleted && d.Status == InventoryDocumentStatus.Draft, ct))
                return Bad<SvOrderDto>("سند قطعاتِ تأییدنشده دارد؛ آن را تأیید یا باطل کنید.");
            SvInstalledUnit unit = await _uow.Set<SvInstalledUnit>().FirstAsync(u => u.Id == row.UnitId, ct);
            if (row.RunningHoursReading.HasValue && row.RunningHoursReading.Value < unit.RunningHours) return Bad<SvOrderDto>("عدد کارکرد از کارکرد ثبت‌شده‌ی واحد کمتر است.");

            DateTime today = DateTime.Today;
            DateTimeOffset now = DateTimeOffset.Now;

            // labour: hours x the hourly cost (payroll when an approved payslip exists, else the standard rate of the work centre of the settings)
            string wcCode = await SvSettings.TextAsync(_uow, SvSettingKeys.LabourWorkCenterCode, ct);
            MfWorkCenter wc = wcCode != null ? await _uow.Set<MfWorkCenter>().AsNoTracking().FirstOrDefaultAsync(w => w.Code == wcCode && !w.Deleted, ct) : null;
            var engine = new MfCostEngine(_uow, _costing, Me, false);
            var cache = new Dictionary<int, MfCostEngine.Rate>();
            decimal labour = 0, hours = 0;
            var employees = new HashSet<int>();
            var payroll = new HashSet<int>();
            foreach (var g in row.TimeEntries.Where(t => t.Hours > 0).GroupBy(t => t.EmployeeId))
            {
                MfCostEngine.Rate rate = await engine.LabourRateAsync(g.Key, wc, "دستور خدمات " + row.Number, cache, ct);
                if (rate.Error != null) return Bad<SvOrderDto>(rate.Error);
                decimal h = g.Sum(t => t.Hours);
                labour += h * rate.PerHour;
                hours += h;
                employees.Add(g.Key);
                if (rate.FromPayroll) payroll.Add(g.Key);
                foreach (SvTimeEntry t in g) t.RateFromPayroll = rate.FromPayroll;
            }
            labour = SlText.R0(labour);

            // parts: the cost entries of the warehouse documents of this order (issue and customer delivery, reversals included), split by the kind of document:
            // the parts consumed (issue) and the parts left on site (customer delivery, whose own posting went to cost of sales)
            var docs = await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => d.ServiceOrderId == row.Id && !d.Deleted).Select(d => new { d.Id, d.Type }).ToListAsync(ct);
            List<int> docIds = docs.Select(d => d.Id).ToList();
            decimal consumedRaw = 0, leftRaw = 0;
            if (docIds.Count > 0)
            {
                var bySource = await _uow.Set<CostEntry>().AsNoTracking().Where(c => c.SourceType == CostingService.InventoryDocumentSource && docIds.Contains(c.SourceId) && c.Kind == CostKind.Material)
                    .GroupBy(c => c.SourceId).Select(g => new { Id = g.Key, Amount = g.Sum(c => c.Amount) }).ToListAsync(ct);
                foreach (var x in bySource)
                    if (docs.First(d => d.Id == x.Id).Type == InventoryDocumentType.CustomerDelivery) leftRaw += x.Amount; else consumedRaw += x.Amount;
            }
            decimal parts = SlText.R0(consumedRaw + leftRaw);
            decimal partsLeftOnSite = SlText.R0(leftRaw);
            decimal partsConsumed = parts - partsLeftOnSite;

            row.LabourHours = hours; row.LabourAmount = labour; row.LabourEmployeeCount = employees.Count; row.LabourPayrollEmployeeCount = payroll.Count; row.PartsAmount = parts;
            row.CostPostedAt = now; row.Status = SvOrderStatus.Closed; row.ClosedAt = now; row.ClosedByUserId = Me;
            Touch(row);
            string center = row.CostCenterCode ?? await SvSettings.EnsureCostCenterAsync(_uow, ct);
            row.CostCenterCode = center;

            // Protected labour (one or two payroll-rated people behind it) is posted neither as a cost entry nor in the event of the order: the amount next to the visible hours of the order would show one person's hourly pay.
            // It is marked pending for a Persian month and the monthly aggregate posting (SvLabourService) carries it: one event and one cost entry per contract and month. The labour hours stay on the order.
            bool protectedLabour = SvPrivacy.LabourProtected(employees.Count, payroll.Count);
            bool deferred = SvLabourMonth.Deferred(labour, protectedLabour);
            if (deferred)
            {
                var posted = await _uow.Set<SvServiceOrder>().AsNoTracking().Where(o => o.LabourPendingPeriod != null && o.LabourAggregatedAt != null).Select(o => o.LabourPendingPeriod.Value).Distinct().ToListAsync(ct);
                row.LabourPendingPeriod = SvLabourMonth.Assign(today, posted); // a month already posted is never reopened: a later close goes to the next month's run
            }

            // otherwise the labour is one aggregated cost entry of the order (never one per person), without the hours (as the production cost run does): the hours stay on the order
            if (labour != 0 && !deferred)
                _uow.Set<CostEntry>().Add(new CostEntry
                {
                    EntryDate = today, Kind = CostKind.Labour, ContractId = row.ContractId, CostCenter = center, Quantity = 0, Amount = labour, SourceType = SvCostSource.Order, SourceId = row.Id, SourceNumber = row.Number,
                    Description = "دستمزد دستور خدمات پس از فروش " + row.Number, CreatedAt = now, CreatedByUserId = Me,
                });

            // Only warranty and goodwill orders post an accounting event: the brief asks for the event on the warranty provision (warranty) and for a draft invoice (billable work). A billable order earns its revenue
            // through the invoice, and the visit of a service agreement (LTSA) is paid by the agreement, so neither takes a cost out of the provision or the goodwill expense; their labour stays a cost entry of the cost centre and the contract.
            // A protected order posts its parts part now and no labour (no event at all when it has no parts).
            AccountingEvent ev = null;
            decimal eventLabour = deferred ? 0m : labour;
            if ((row.Decision == SvWarrantyDecision.Warranty || row.Decision == SvWarrantyDecision.Goodwill) && eventLabour + parts != 0)
            {
                var lines = SvPosting.WarrantyLines(row.Decision == SvWarrantyDecision.Warranty, eventLabour, partsConsumed, partsLeftOnSite, row.Number)
                    .Select(l => new AccountingEventLine { AccountRole = l.Role, Debit = l.Debit, Credit = l.Credit, ContractId = row.ContractId, CostCenter = center, Description = l.Description }).ToList();
                ev = _costing.AddEvent(AccountingEventType.WarrantyCost, today, SvCostSource.Order, row.Id, row.Number, "هزینه خدمات پس از فروش " + row.Number, lines);
                row.WarrantyEvent = ev;
            }

            // a billable order drafts its invoice (the invoice and the event are linked through navigations: one save, no second one for the ids)
            if (row.Decision == SvWarrantyDecision.Billable) row.Invoice = await DraftInvoiceCoreAsync(row, ct);

            // the satisfaction survey of the closed order
            if (row.CustomerId.HasValue) await SvSurveyDraft.ForOrderAsync(_uow, row, today, Me, ct);

            // the unit follows the meter reading of the visit
            if (row.RunningHoursReading.HasValue) { unit.RunningHours = row.RunningHoursReading.Value; unit.RunningHoursAt = today; unit.ModifyDate = now; unit.ModifierUserId = Me; }

            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvOrderDto>(SlText.Concurrency); }
            SlSurvey survey = await _uow.Set<SlSurvey>().AsNoTracking().FirstOrDefaultAsync(x => x.ServiceOrderId == row.Id && !x.Deleted, ct);
            if (survey != null) await SvGates.SurveyAsync(_notify, survey, ct);
            return await SavedAsync(row, ct);
        }

        // ================================================================================== invoice of billable work

        public async Task<ResponseDto<SvOrderDto>> DraftInvoiceAsync(SvDraftInvoiceDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Writers)) return Bad<SvOrderDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SvOrderDto>(SlText.InvalidData);
            SvServiceOrder row = await Full().FirstOrDefaultAsync(o => o.Id == dto.Id && !o.Deleted, ct);
            if (row == null) return SlText.NotFound<SvOrderDto>("دستور خدمات پیدا نشد.");
            if (row.Status != SvOrderStatus.Closed || row.Decision != SvWarrantyDecision.Billable) return Bad<SvOrderDto>("فقط برای دستور بسته‌شده‌ی قابل‌فاکتور پیش‌نویس فاکتور ساخته می‌شود.");
            if (row.InvoiceId.HasValue) return Bad<SvOrderDto>("برای این دستور قبلاً پیش‌نویس فاکتور ساخته شده است.");
            if (dto.BillableLabourRateRial.HasValue)
            {
                if (dto.BillableLabourRateRial.Value <= 0) return Bad<SvOrderDto>("نرخ ساعتی صورتحساب باید بیشتر از صفر باشد.");
                row.BillableLabourRateRial = dto.BillableLabourRateRial;
            }
            FnSalesInvoice invoice = await DraftInvoiceCoreAsync(row, ct);
            if (invoice == null && row.DraftNote != null) return Bad<SvOrderDto>(row.DraftNote);
            // one save: the invoice is linked to the order through the navigation; the row version of the order makes a second click (a retry) fail instead of drafting a second invoice
            row.Invoice = invoice;
            Touch(row);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvOrderDto>(SlText.Concurrency); }
            return await GetOrderAsync(row.Id, ct);
        }

        /// <summary>
        /// Drafts the invoice of a billable order: the work and travel hours at the rate entered on the order, and the spare parts of its confirmed documents (consumed and left on site) at the price of the approved price list valid today.
        /// Nothing is invented: without a VAT rate, a customer, an hourly rate (when there are hours) or a price for every part no invoice is made and the order says why.
        /// </summary>
        private async Task<FnSalesInvoice> DraftInvoiceCoreAsync(SvServiceOrder row, CancellationToken ct)
        {
            decimal billableHours = SvCostMath.BillableHours(row.TimeEntries.Where(t => t.Kind == SvTimeKind.Travel).Sum(t => t.Hours), row.TimeEntries.Where(t => t.Kind == SvTimeKind.Work).Sum(t => t.Hours));
            DateTime date = DateTime.Today;

            // the parts: quantity per item over the confirmed documents of the order
            var partLines = await _uow.Set<InventoryDocumentLine>().AsNoTracking()
                .Where(l => l.Document.ServiceOrderId == row.Id && !l.Document.Deleted && l.Document.Status == InventoryDocumentStatus.Confirmed)
                .GroupBy(l => l.ItemId).Select(g => new { ItemId = g.Key, Quantity = g.Sum(l => l.Quantity) }).ToListAsync(ct);
            List<int> itemIds = partLines.Select(p => p.ItemId).ToList();
            Dictionary<int, InventoryItem> items = itemIds.Count == 0 ? new Dictionary<int, InventoryItem>() : await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            var parts = partLines.Where(p => p.Quantity > 0).OrderBy(p => items.GetValueOrDefault(p.ItemId)?.Code, StringComparer.Ordinal).ThenBy(p => p.ItemId).ToList();

            if (billableHours > 0 && !row.BillableLabourRateRial.HasValue) { row.DraftNote = "نرخ ساعتی صورتحساب روی دستور وارد نشده است؛ پس از ثبت نرخ، «پیش‌نویس فاکتور» را بزنید."; return null; }
            if (!row.CustomerId.HasValue) { row.DraftNote = "مشتری دستور مشخص نیست؛ پیش‌نویس فاکتور ساخته نشد."; return null; }
            if (billableHours <= 0 && parts.Count == 0) { row.DraftNote = "ساعت کار یا سفرِ قابل‌فاکتور و قطعه‌ی مصرفی ثبت نشده است؛ پیش‌نویس فاکتور ساخته نشد."; return null; }
            decimal? rate = await TaxRates.RateAtAsync(_uow, FnTaxKind.Vat, date, ct);
            if (!rate.HasValue) { row.DraftNote = "نرخ مالیات بر ارزش افزوده برای این تاریخ در جدول نرخ‌ها ثبت نشده است؛ پس از ثبت نرخ، «پیش‌نویس فاکتور» را بزنید."; return null; }
            FnCustomer customer = await _uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == row.CustomerId.Value, ct);
            if (customer == null || !customer.IsActive) { row.DraftNote = "مشتری پیدا نشد یا غیرفعال است؛ پیش‌نویس فاکتور ساخته نشد."; return null; }

            List<SlPriceList> lists = parts.Count == 0 ? new List<SlPriceList>() : await _uow.Set<SlPriceList>().AsNoTracking().Include(p => p.Lines)
                .Where(p => !p.Deleted && p.Status == SlPriceListStatus.Approved && p.ValidFrom <= date && (p.ValidTo == null || p.ValidTo >= date)).ToListAsync(ct);
            var priced = parts.Select(p => new { p.ItemId, p.Quantity, Item = items.GetValueOrDefault(p.ItemId), Price = SvPricing.PriceOf(lists, p.ItemId, p.Quantity) }).ToList();
            var unpriced = priced.Where(p => !p.Price.HasValue).ToList();
            if (unpriced.Count > 0)
            {
                row.DraftNote = "برای قطعه‌ی " + string.Join("، ", unpriced.Select(p => p.Item?.Code ?? p.ItemId.ToString())) + " در لیست قیمت تأییدشده‌ی امروز قیمتی نیست؛ فروش قیمت را در لیست قیمت وارد و تأیید کند و سپس «پیش‌نویس فاکتور» را بزنید.";
                return null;
            }

            int? termDays = await _uow.Set<SlCustomerProfile>().AsNoTracking().Where(p => p.CustomerId == row.CustomerId.Value && !p.Deleted).Select(p => p.PaymentTermDays).FirstOrDefaultAsync(ct);
            var invoice = new FnSalesInvoice
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, FinanceNumbers.SalesInvoice, "SI", date, ct), Date = date, DueDate = termDays.HasValue ? date.AddDays(termDays.Value) : (DateTime?)null,
                CustomerId = row.CustomerId.Value, ContractId = row.ContractId, Type = billableHours > 0 ? FnInvoiceType.Service : FnInvoiceType.Goods, Status = FnDocStatus.Draft, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, EditorUserIds = LedgerMath.WithEditor(null, Me),
                Description = $"پیش‌نویس خودکار از دستور خدمات پس از فروش {row.Number}", Lines = new List<FnSalesInvoiceLine>(),
            };
            int rowNumber = 0;
            void AddLine(string description, string itemCode, decimal quantity, string unit, decimal unitPrice)
            {
                var (_, discount, amount) = SlText.LineAmount(quantity, unitPrice, 0);
                decimal vat = SlText.R0(amount * rate.Value / 100m);
                invoice.Lines.Add(new FnSalesInvoiceLine
                {
                    RowNumber = ++rowNumber, Description = description, ItemCode = itemCode, Quantity = quantity, Unit = unit, UnitPrice = unitPrice,
                    Discount = discount, Amount = amount, VatRatePercent = rate.Value, VatAmount = vat, Total = amount + vat,
                });
            }
            if (billableHours > 0) AddLine("خدمات پس از فروش - ساعت کار و سفر " + row.Number, null, billableHours, "ساعت", row.BillableLabourRateRial.Value);
            foreach (var p in priced) AddLine(p.Item?.Title ?? "قطعه یدکی", p.Item?.Code, p.Quantity, p.Item?.Unit, p.Price.Value);
            invoice.NetAmount = invoice.Lines.Sum(l => l.Amount); invoice.DiscountAmount = invoice.Lines.Sum(l => l.Discount); invoice.VatAmount = invoice.Lines.Sum(l => l.VatAmount);
            invoice.GrossAmount = invoice.NetAmount + invoice.VatAmount; invoice.ReceivableAmount = invoice.GrossAmount;
            _uow.Set<FnSalesInvoice>().Add(invoice);
            row.DraftNote = null;
            return invoice;
        }
    }

    internal static class SvCostSource
    {
        public const string Order = "ServiceOrder";
    }
}
