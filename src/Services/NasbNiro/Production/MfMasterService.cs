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
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Providers;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production
{
    public interface IMfMasterService
    {
        Task<ResponseDto<PagedList<WorkCenterDto>>> GetWorkCentersAsync(WorkCenterQuery query, CancellationToken ct);
        Task<ResponseDto<WorkCenterDto>> GetWorkCenterAsync(int id, CancellationToken ct);
        Task<ResponseDto<WorkCenterDto>> SaveWorkCenterAsync(SaveWorkCenterDto dto, CancellationToken ct);
        Task<ResponseDto<WorkCenterDto>> SaveWorkCenterRatesAsync(SaveWorkCenterRatesDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<MachineDto>>> GetMachinesAsync(MachineQuery query, CancellationToken ct);
        Task<ResponseDto<MachineDto>> SaveMachineInfoAsync(SaveMachineInfoDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<OperationStandardDto>>> GetStandardsAsync(OperationStandardQuery query, CancellationToken ct);
        Task<ResponseDto<OperationStandardDto>> SaveStandardAsync(SaveOperationStandardDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<RoutingRevisionDto>>> GetRoutingsAsync(RoutingQuery query, CancellationToken ct);
        Task<ResponseDto<RoutingRevisionDto>> GetRoutingAsync(int id, CancellationToken ct);
        Task<ResponseDto<RoutingRevisionDto>> SaveRoutingAsync(SaveRoutingRevisionDto dto, CancellationToken ct);
        Task<ResponseDto<RoutingRevisionDto>> RoutingActionAsync(RoutingActionDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<ReasonCodeDto>>> GetReasonCodesAsync(ReasonCodeQuery query, CancellationToken ct);
        Task<ResponseDto<ReasonCodeDto>> SaveReasonCodeAsync(SaveReasonCodeDto dto, CancellationToken ct);
    }

    /// <summary>Work centres, machine side data, operation standards, routing revisions (two-person approval) and reason codes.</summary>
    public class MfMasterService : IMfMasterService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public MfMasterService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => MfText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);
        private bool CanSeeRates => _user.IsInRole("FINANCEMANAGER,FINANCIAL,PRODUCTIONMANAGER,CEO,PMO,AUDITOR");

        // ================================================================================== work centres

        private async Task<List<WorkCenterDto>> WorkCenterDtosAsync(List<MfWorkCenter> rows, CancellationToken ct)
        {
            List<int> ids = rows.Select(r => r.Id).ToList();
            List<int> calendarIds = rows.Where(r => r.CalendarId.HasValue).Select(r => r.CalendarId.Value).Distinct().ToList();
            Dictionary<int, string> calendars = calendarIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<PcCalendar>().AsNoTracking().Where(c => calendarIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            var machineCounts = await _uow.Set<MfMachineInfo>().AsNoTracking().Where(i => !i.Deleted && i.WorkCenterId != null && ids.Contains(i.WorkCenterId.Value))
                .GroupBy(i => i.WorkCenterId.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);
            bool rates = CanSeeRates;
            return rows.Select(r => new WorkCenterDto
            {
                Id = r.Id, Code = r.Code, Title = r.Title, Kind = r.Kind, CalendarId = r.CalendarId,
                CalendarName = r.CalendarId.HasValue && calendars.TryGetValue(r.CalendarId.Value, out string cn) ? cn : null,
                ShiftsPerDay = r.ShiftsPerDay, HoursPerShift = r.HoursPerShift, EfficiencyPercent = r.EfficiencyPercent, UtilisationPercent = r.UtilisationPercent,
                ParallelResources = r.ParallelResources, MachineRateRial = rates ? r.MachineRateRial : null, LabourRateRial = rates ? r.LabourRateRial : null,
                CostCenter = r.CostCenter, QueueDaysDefault = r.QueueDaysDefault, IsActive = r.IsActive, RequiresMachine = r.RequiresMachine,
                MachineCount = machineCounts.FirstOrDefault(m => m.Id == r.Id)?.Count ?? 0, RowVersion = MfConcurrency.Text(r.RowVersion),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<WorkCenterDto>>> GetWorkCentersAsync(WorkCenterQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.WorkCenterReaders)) return Bad<PagedList<WorkCenterDto>>(MfText.NotAllowed);
            query ??= new WorkCenterQuery();
            IQueryable<MfWorkCenter> q = _uow.Set<MfWorkCenter>().AsNoTracking().Where(w => !w.Deleted);
            if (query.Kind.HasValue) q = q.Where(w => w.Kind == query.Kind.Value);
            if (query.IsActive.HasValue) q = q.Where(w => w.IsActive == query.IsActive.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(w => w.Code.Contains(text) || w.Title.Contains(text));
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfWorkCenter> rows = await q.OrderBy(w => w.Code).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            List<WorkCenterDto> dtos = await WorkCenterDtosAsync(rows, ct);
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(dtos, 1, int.MaxValue, ct), total, number, size));
        }

        /// <summary>A page that was cut in the database: the rows of the page plus the total.</summary>
        private static PagedList<T> AsPage<T>(PagedList<T> all, int total, int number, int size)
        {
            all.TotalCount = total;
            all.CurrentPage = number;
            all.PageSize = size;
            all.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return all;
        }

        public async Task<ResponseDto<WorkCenterDto>> GetWorkCenterAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.WorkCenterReaders)) return Bad<WorkCenterDto>(MfText.NotAllowed);
            MfWorkCenter row = await _uow.Set<MfWorkCenter>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == id && !w.Deleted, ct);
            if (row == null) return MfText.NotFound<WorkCenterDto>("مرکز کار پیدا نشد.");
            return ResponseDtoHelper.Ok((await WorkCenterDtosAsync(new List<MfWorkCenter> { row }, ct))[0]);
        }

        public async Task<ResponseDto<WorkCenterDto>> SaveWorkCenterAsync(SaveWorkCenterDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterEditors)) return Bad<WorkCenterDto>(MfText.NotAllowed);
            if (dto == null) return Bad<WorkCenterDto>(MfText.InvalidData);
            string code = Clean(dto.Code), title = Clean(dto.Title);
            if (code == null || title == null) return Bad<WorkCenterDto>("کد و عنوان مرکز کار را وارد کنید.");
            if (!Enum.IsDefined(typeof(MfWorkCenterKind), dto.Kind)) return Bad<WorkCenterDto>(MfText.InvalidData);
            if (dto.ShiftsPerDay < 1 || dto.ShiftsPerDay > 3) return Bad<WorkCenterDto>("تعداد شیفت در روز بین ۱ تا ۳ است.");
            if (dto.HoursPerShift <= 0 || dto.HoursPerShift > 12) return Bad<WorkCenterDto>("ساعت هر شیفت باید بیشتر از صفر و حداکثر ۱۲ باشد.");
            if (dto.EfficiencyPercent <= 0 || dto.EfficiencyPercent > 200) return Bad<WorkCenterDto>("بازده باید بین ۰ و ۲۰۰ درصد باشد.");
            if (dto.UtilisationPercent <= 0 || dto.UtilisationPercent > 100) return Bad<WorkCenterDto>("بهره‌برداری باید بین ۰ و ۱۰۰ درصد باشد.");
            if (dto.ParallelResources < 1 || dto.ParallelResources > 1000) return Bad<WorkCenterDto>("تعداد منابع موازی باید حداقل ۱ باشد.");
            if (dto.QueueDaysDefault < 0 || dto.QueueDaysDefault > 365) return Bad<WorkCenterDto>("روزهای صف نامعتبر است.");
            if (dto.CalendarId.HasValue && !await _uow.Set<PcCalendar>().AnyAsync(c => c.Id == dto.CalendarId.Value && !c.Deleted, ct)) return Bad<WorkCenterDto>("تقویم کاری پیدا نشد.");
            if (await _uow.Set<MfWorkCenter>().AnyAsync(w => w.Code == code && !w.Deleted && w.Id != (dto.Id ?? 0), ct)) return Bad<WorkCenterDto>("این کد مرکز کار قبلاً ثبت شده است.");

            DateTimeOffset now = DateTimeOffset.Now;
            MfWorkCenter row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<MfWorkCenter>().FirstOrDefaultAsync(w => w.Id == dto.Id.Value && !w.Deleted, ct);
                if (row == null) return MfText.NotFound<WorkCenterDto>("مرکز کار پیدا نشد.");
                if (!MfConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<WorkCenterDto>(MfText.InvalidData);
            }
            else
            {
                row = new MfWorkCenter { CreateDate = now, CreatorUserId = Me };
                _uow.Set<MfWorkCenter>().Add(row);
            }
            row.Code = code; row.Title = title; row.Kind = dto.Kind; row.CalendarId = dto.CalendarId; row.ShiftsPerDay = dto.ShiftsPerDay; row.HoursPerShift = dto.HoursPerShift;
            row.EfficiencyPercent = dto.EfficiencyPercent; row.UtilisationPercent = dto.UtilisationPercent; row.ParallelResources = dto.ParallelResources;
            row.CostCenter = Clean(dto.CostCenter); row.QueueDaysDefault = dto.QueueDaysDefault; row.IsActive = dto.IsActive; row.RequiresMachine = dto.RequiresMachine;
            row.ModifyDate = now; row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<WorkCenterDto>(MfText.Concurrency);
            }
            return await GetWorkCenterAsync(row.Id, ct);
        }

        public async Task<ResponseDto<WorkCenterDto>> SaveWorkCenterRatesAsync(SaveWorkCenterRatesDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.RateSetters)) return Bad<WorkCenterDto>("نرخ ساعتی را فقط مالی تعیین می‌کند.");
            if (dto == null) return Bad<WorkCenterDto>(MfText.InvalidData);
            if ((dto.MachineRateRial ?? 0) < 0 || (dto.LabourRateRial ?? 0) < 0) return Bad<WorkCenterDto>("نرخ نمی‌تواند منفی باشد.");
            MfWorkCenter row = await _uow.Set<MfWorkCenter>().FirstOrDefaultAsync(w => w.Id == dto.Id && !w.Deleted, ct);
            if (row == null) return MfText.NotFound<WorkCenterDto>("مرکز کار پیدا نشد.");
            row.MachineRateRial = dto.MachineRateRial;
            row.LabourRateRial = dto.LabourRateRial;
            row.ModifyDate = DateTimeOffset.Now;
            row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return await GetWorkCenterAsync(row.Id, ct);
        }

        // ================================================================================== machines

        private async Task<List<MachineDto>> MachineRowsAsync(IQueryable<Machine> machines, CancellationToken ct)
        {
            var rows = await (from m in machines
                              join i in _uow.Set<MfMachineInfo>().Where(x => !x.Deleted) on m.Id equals i.MachineId into gj
                              from i in gj.DefaultIfEmpty()
                              orderby m.Title, m.Id
                              select new { m.Id, m.Title, m.IsActive, Info = i }).AsNoTracking().ToListAsync(ct);
            List<int> centerIds = rows.Where(r => r.Info != null && r.Info.WorkCenterId.HasValue).Select(r => r.Info.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, string> centers = centerIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            return rows.Select(r => new MachineDto
            {
                MachineId = r.Id, Title = r.Title, IsActive = r.IsActive, InfoId = r.Info?.Id, WorkCenterId = r.Info?.WorkCenterId,
                WorkCenterCode = r.Info?.WorkCenterId != null && centers.TryGetValue(r.Info.WorkCenterId.Value, out string c) ? c : null,
                AssetId = r.Info?.AssetId, Maker = r.Info?.Maker, Model = r.Info?.Model, SerialNumber = r.Info?.SerialNumber, Capabilities = r.Info?.Capabilities,
                Status = r.Info?.Status ?? MfMachineStatus.Available,
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<MachineDto>>> GetMachinesAsync(MachineQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return Bad<PagedList<MachineDto>>(MfText.NotAllowed);
            query ??= new MachineQuery();
            IQueryable<Machine> machines = _uow.Set<Machine>().AsNoTracking();
            string text = Clean(query.Contains);
            if (text != null) machines = machines.Where(m => m.Title.Contains(text));
            if (query.WorkCenterId.HasValue)
            {
                int wc = query.WorkCenterId.Value;
                machines = machines.Where(m => _uow.Set<MfMachineInfo>().Any(i => i.MachineId == m.Id && !i.Deleted && i.WorkCenterId == wc));
            }
            if (query.Status.HasValue)
            {
                MfMachineStatus status = query.Status.Value;
                machines = status == MfMachineStatus.Available
                    ? machines.Where(m => !_uow.Set<MfMachineInfo>().Any(i => i.MachineId == m.Id && !i.Deleted && i.Status != MfMachineStatus.Available))
                    : machines.Where(m => _uow.Set<MfMachineInfo>().Any(i => i.MachineId == m.Id && !i.Deleted && i.Status == status));
            }
            int total = await machines.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MachineDto> rows = await MachineRowsAsync(machines.OrderBy(m => m.Title).ThenBy(m => m.Id).Skip((number - 1) * size).Take(size), ct);
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(rows, 1, int.MaxValue, ct), total, number, size));
        }

        public async Task<ResponseDto<MachineDto>> SaveMachineInfoAsync(SaveMachineInfoDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterEditors + "," + ProductionRoles.Maintenance)) return Bad<MachineDto>(MfText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(MfMachineStatus), dto.Status)) return Bad<MachineDto>(MfText.InvalidData);
            if (!await _uow.Set<Machine>().AnyAsync(m => m.Id == dto.MachineId, ct)) return MfText.NotFound<MachineDto>("ماشین پیدا نشد.");
            if (dto.WorkCenterId.HasValue && !await _uow.Set<MfWorkCenter>().AnyAsync(w => w.Id == dto.WorkCenterId.Value && !w.Deleted, ct)) return Bad<MachineDto>("مرکز کار پیدا نشد.");
            if (dto.AssetId.HasValue && !await _uow.Set<FnAsset>().AnyAsync(a => a.Id == dto.AssetId.Value, ct)) return Bad<MachineDto>("دارایی ثابت پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            MfMachineInfo info = await _uow.Set<MfMachineInfo>().FirstOrDefaultAsync(i => i.MachineId == dto.MachineId && !i.Deleted, ct);
            if (info == null)
            {
                info = new MfMachineInfo { MachineId = dto.MachineId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<MfMachineInfo>().Add(info);
            }
            info.WorkCenterId = dto.WorkCenterId; info.AssetId = dto.AssetId; info.Maker = Clean(dto.Maker); info.Model = Clean(dto.Model);
            info.SerialNumber = Clean(dto.SerialNumber); info.Capabilities = Clean(dto.Capabilities); info.Status = dto.Status;
            info.ModifyDate = now; info.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await MachineRowsAsync(_uow.Set<Machine>().Where(m => m.Id == dto.MachineId), ct))[0]);
        }

        // ================================================================================== operation standards

        private async Task<List<OperationStandardDto>> StandardRowsAsync(IQueryable<OperationTemplate> templates, CancellationToken ct)
        {
            var rows = await (from t in templates
                              join s in _uow.Set<MfOperationStandard>().Where(x => !x.Deleted) on t.Id equals s.OperationTemplateId into gj
                              from s in gj.DefaultIfEmpty()
                              select new { T = t, ItemCode = t.Item.Code, GroupCode = t.ItemGroup.Code, S = s }).AsNoTracking().ToListAsync(ct);
            List<int> centerIds = rows.Where(r => r.S != null && r.S.WorkCenterId.HasValue).Select(r => r.S.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, string> centers = centerIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            List<int> supplierIds = rows.Where(r => r.S != null && r.S.DefaultSupplierId.HasValue).Select(r => r.S.DefaultSupplierId.Value).Distinct().ToList();
            Dictionary<int, string> suppliers = supplierIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<Provider>().AsNoTracking().Where(p => supplierIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            List<int> wpsIds = rows.Where(r => r.S != null && r.S.WpsId.HasValue).Select(r => r.S.WpsId.Value).Distinct().ToList();
            Dictionary<int, string> wpsNumbers = wpsIds.Count == 0 ? new Dictionary<int, string>()
                : (await _uow.Set<Wps>().AsNoTracking().Where(w => wpsIds.Contains(w.Id)).Select(w => new { w.Id, w.Number, w.Revision }).ToListAsync(ct)).ToDictionary(w => w.Id, w => w.Number + "/" + w.Revision);
            return rows.Select(r => new OperationStandardDto
            {
                OperationTemplateId = r.T.Id, ItemId = r.T.ItemId, ItemCode = r.ItemCode, ItemGroupId = r.T.ItemGroupId, ItemGroupCode = r.GroupCode, Sequence = r.T.Sequence,
                TemplateTitle = r.T.Title, RequiresQc = r.T.RequiresQc, Id = r.S?.Id, WorkCenterId = r.S?.WorkCenterId,
                WorkCenterCode = r.S?.WorkCenterId != null && centers.TryGetValue(r.S.WorkCenterId.Value, out string cc) ? cc : null,
                SetupHours = r.S?.SetupHours ?? 0, RunHoursPerUnit = r.S?.RunHoursPerUnit ?? 0, MoveDays = r.S?.MoveDays ?? 0, QueueDays = r.S?.QueueDays ?? 0,
                IsOutside = r.S?.IsOutside ?? false, DefaultSupplierId = r.S?.DefaultSupplierId,
                DefaultSupplierName = r.S?.DefaultSupplierId != null && suppliers.TryGetValue(r.S.DefaultSupplierId.Value, out string sn) ? sn : null,
                OutsideLeadDays = r.S?.OutsideLeadDays ?? 0, WorkInstructionDocumentId = r.S?.WorkInstructionDocumentId, DrawingReferenceRule = r.S?.DrawingReferenceRule,
                RequiredQualificationKind = r.S?.RequiredQualificationKind, RequiredQualificationProcess = r.S?.RequiredQualificationProcess,
                InspectionRequirement = r.S?.InspectionRequirement ?? MfInspectionRequirement.None, ItpTestType = r.S?.ItpTestType, FirstArticleRequired = r.S?.FirstArticleRequired ?? false,
                IsWelding = r.S?.IsWelding ?? false, WpsId = r.S?.WpsId, WpsNumber = r.S?.WpsId != null && wpsNumbers.TryGetValue(r.S.WpsId.Value, out string wn) ? wn : null,
            }).OrderBy(d => d.ItemCode ?? d.ItemGroupCode).ThenBy(d => d.Sequence).ToList();
        }

        public async Task<ResponseDto<PagedList<OperationStandardDto>>> GetStandardsAsync(OperationStandardQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return Bad<PagedList<OperationStandardDto>>(MfText.NotAllowed);
            query ??= new OperationStandardQuery();
            IQueryable<OperationTemplate> q = _uow.Set<OperationTemplate>().Where(t => t.IsActive && !t.Deleted);
            if (query.ItemId.HasValue) q = q.Where(t => t.ItemId == query.ItemId.Value);
            if (query.ItemGroupId.HasValue) q = q.Where(t => t.ItemGroupId == query.ItemGroupId.Value && t.ItemId == null);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(t => t.Title.Contains(text) || t.Item.Code.Contains(text) || t.ItemGroup.Code.Contains(text));
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<OperationStandardDto> rows = await StandardRowsAsync(q.OrderBy(t => t.ItemId).ThenBy(t => t.ItemGroupId).ThenBy(t => t.Sequence).ThenBy(t => t.Id).Skip((number - 1) * size).Take(size), ct);
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(rows, 1, int.MaxValue, ct), total, number, size));
        }

        public async Task<ResponseDto<OperationStandardDto>> SaveStandardAsync(SaveOperationStandardDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.StandardEditors)) return Bad<OperationStandardDto>(MfText.NotAllowed);
            if (dto == null) return Bad<OperationStandardDto>(MfText.InvalidData);
            OperationTemplate template = await _uow.Set<OperationTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.OperationTemplateId && !t.Deleted, ct);
            if (template == null) return MfText.NotFound<OperationStandardDto>("مرحله ساخت (قالب عملیات) پیدا نشد.");
            if (dto.SetupHours < 0 || dto.RunHoursPerUnit < 0 || dto.MoveDays < 0 || dto.QueueDays < 0 || dto.OutsideLeadDays < 0) return Bad<OperationStandardDto>("زمان‌ها نمی‌تواند منفی باشد.");
            if (dto.SetupHours > 10000 || dto.RunHoursPerUnit > 10000 || dto.MoveDays > 365 || dto.QueueDays > 365 || dto.OutsideLeadDays > 730) return Bad<OperationStandardDto>("زمان واردشده از حد مجاز بیشتر است.");
            if (!Enum.IsDefined(typeof(MfInspectionRequirement), dto.InspectionRequirement)) return Bad<OperationStandardDto>(MfText.InvalidData);
            if (dto.ItpTestType.HasValue && !Enum.IsDefined(typeof(QaTestType), dto.ItpTestType.Value)) return Bad<OperationStandardDto>("نوع آزمون ITP نامعتبر است.");
            if (dto.InspectionRequirement == MfInspectionRequirement.ItpPoint && !dto.ItpTestType.HasValue) return Bad<OperationStandardDto>("برای بازرسی از نوع نقطه ITP، نوع آزمون ITP را مشخص کنید.");
            if (dto.WorkCenterId.HasValue && !await _uow.Set<MfWorkCenter>().AnyAsync(w => w.Id == dto.WorkCenterId.Value && !w.Deleted, ct)) return Bad<OperationStandardDto>("مرکز کار پیدا نشد.");
            if (!dto.IsOutside && !dto.WorkCenterId.HasValue) return Bad<OperationStandardDto>("مرکز کار مرحله را انتخاب کنید (یا مرحله را «بیرونی» علامت بزنید).");
            if (dto.DefaultSupplierId.HasValue && !await _uow.Set<Provider>().AnyAsync(p => p.Id == dto.DefaultSupplierId.Value && !p.Deleted, ct)) return Bad<OperationStandardDto>("تامین‌کننده پیدا نشد.");
            // R1p D12: a welding step names the welding procedure it is welded by; the terminal starts it only while that WPS is approved
            if (dto.IsWelding && dto.IsOutside) return Bad<OperationStandardDto>("مرحله جوشکاری بیرونی روی ترمینال شروع نمی‌شود؛ علامت «جوشکاری» فقط برای مرحله داخلی است.");
            if (dto.IsWelding && !dto.WpsId.HasValue) return Bad<OperationStandardDto>("برای مرحله جوشکاری، WPS (دستورالعمل جوشکاری) را انتخاب کنید.");
            if (dto.IsWelding && !await _uow.Set<Wps>().AnyAsync(w => w.Id == dto.WpsId.Value && !w.Deleted, ct)) return Bad<OperationStandardDto>("WPS انتخاب‌شده پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            MfOperationStandard row = await _uow.Set<MfOperationStandard>().FirstOrDefaultAsync(s => s.OperationTemplateId == dto.OperationTemplateId && !s.Deleted, ct);
            if (row == null)
            {
                row = new MfOperationStandard { OperationTemplateId = dto.OperationTemplateId, CreateDate = now, CreatorUserId = Me };
                _uow.Set<MfOperationStandard>().Add(row);
            }
            row.WorkCenterId = dto.WorkCenterId; row.SetupHours = dto.SetupHours; row.RunHoursPerUnit = dto.RunHoursPerUnit; row.MoveDays = dto.MoveDays; row.QueueDays = dto.QueueDays;
            row.IsOutside = dto.IsOutside; row.DefaultSupplierId = dto.DefaultSupplierId; row.OutsideLeadDays = dto.OutsideLeadDays; row.WorkInstructionDocumentId = dto.WorkInstructionDocumentId;
            row.DrawingReferenceRule = Clean(dto.DrawingReferenceRule); row.RequiredQualificationKind = Clean(dto.RequiredQualificationKind);
            row.RequiredQualificationProcess = Clean(dto.RequiredQualificationProcess); row.InspectionRequirement = dto.InspectionRequirement; row.ItpTestType = dto.ItpTestType;
            row.FirstArticleRequired = dto.FirstArticleRequired; row.IsWelding = dto.IsWelding; row.WpsId = dto.IsWelding ? dto.WpsId : null; row.ModifyDate = now; row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await StandardRowsAsync(_uow.Set<OperationTemplate>().Where(t => t.Id == dto.OperationTemplateId), ct))[0]);
        }

        // ================================================================================== routing revisions

        private async Task<RoutingRevisionDto> RoutingDtoAsync(MfRoutingRevision r, bool withSteps, CancellationToken ct)
        {
            InventoryItem item = r.ItemId.HasValue ? await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == r.ItemId.Value, ct) : null;
            ItemGroup group = r.ItemGroupId.HasValue ? await _uow.Set<ItemGroup>().AsNoTracking().FirstOrDefaultAsync(g => g.Id == r.ItemGroupId.Value, ct) : null;
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.CreatorUserId, r.ProductionApprovedByUserId, r.EngineeringApprovedByUserId }, ct);
            var dto = new RoutingRevisionDto
            {
                Id = r.Id, ItemId = r.ItemId, ItemCode = item?.Code, ItemTitle = item?.Title, ItemGroupId = r.ItemGroupId, ItemGroupCode = group?.Code, Revision = r.Revision,
                Status = r.Status, Note = r.Note, ContentHash = r.ContentHash, CreatedByName = names.Name(r.CreatorUserId), CreatedAtText = MfText.Stamp(r.CreateDate),
                ProductionApprovedByName = names.Name(r.ProductionApprovedByUserId), ProductionApprovedAtText = MfText.Stamp(r.ProductionApprovedAt),
                EngineeringApprovedByName = names.Name(r.EngineeringApprovedByUserId), EngineeringApprovedAtText = MfText.Stamp(r.EngineeringApprovedAt),
                ApprovedAtText = MfText.Stamp(r.ApprovedAt), RowVersion = MfConcurrency.Text(r.RowVersion),
            };
            if (!withSteps) return dto;
            List<OperationTemplate> steps = await MfRouting.StepsOfKeyAsync(_uow, r.ItemId, r.ItemGroupId, false, ct);
            Dictionary<int, MfOperationStandard> standards = await MfRouting.StandardsAsync(_uow, steps.Select(s => s.Id), ct);
            string hash = await MfRouting.HashAsync(_uow, steps, standards, ct);
            dto.ContentMatches = r.Status == MfRoutingStatus.Approved && r.ContentHash == hash;
            List<int> centerIds = standards.Values.Where(s => s.WorkCenterId.HasValue).Select(s => s.WorkCenterId.Value).Distinct().ToList();
            Dictionary<int, string> centers = centerIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            dto.Steps = steps.Select(t =>
            {
                standards.TryGetValue(t.Id, out MfOperationStandard s);
                return new RoutingStepDto
                {
                    TemplateId = t.Id, Sequence = t.Sequence, Title = t.Title, RequiresQc = t.RequiresQc, StandardId = s?.Id,
                    WorkCenterCode = s?.WorkCenterId != null && centers.TryGetValue(s.WorkCenterId.Value, out string c) ? c : null,
                    SetupHours = s?.SetupHours ?? 0, RunHoursPerUnit = s?.RunHoursPerUnit ?? 0, IsOutside = s?.IsOutside ?? false,
                    InspectionRequirement = s?.InspectionRequirement ?? MfInspectionRequirement.None,
                };
            }).ToList();
            return dto;
        }

        public async Task<ResponseDto<PagedList<RoutingRevisionDto>>> GetRoutingsAsync(RoutingQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Readers)) return Bad<PagedList<RoutingRevisionDto>>(MfText.NotAllowed);
            query ??= new RoutingQuery();
            IQueryable<MfRoutingRevision> q = _uow.Set<MfRoutingRevision>().AsNoTracking().Where(r => !r.Deleted);
            if (query.ItemId.HasValue) q = q.Where(r => r.ItemId == query.ItemId.Value);
            if (query.ItemGroupId.HasValue) q = q.Where(r => r.ItemGroupId == query.ItemGroupId.Value);
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null)
                q = q.Where(r => _uow.Set<InventoryItem>().Any(i => i.Id == r.ItemId && (i.Code.Contains(text) || i.Title.Contains(text)))
                    || _uow.Set<ItemGroup>().Any(g => g.Id == r.ItemGroupId && (g.Code.Contains(text) || g.Title.Contains(text))));
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfRoutingRevision> rows = await q.OrderByDescending(r => r.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            var dtos = new List<RoutingRevisionDto>();
            foreach (MfRoutingRevision r in rows) dtos.Add(await RoutingDtoAsync(r, false, ct));
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(dtos, 1, int.MaxValue, ct), total, number, size));
        }

        public async Task<ResponseDto<RoutingRevisionDto>> GetRoutingAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Readers)) return Bad<RoutingRevisionDto>(MfText.NotAllowed);
            MfRoutingRevision r = await _uow.Set<MfRoutingRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (r == null) return MfText.NotFound<RoutingRevisionDto>("بازنگری مسیر ساخت پیدا نشد.");
            return ResponseDtoHelper.Ok(await RoutingDtoAsync(r, true, ct));
        }

        public async Task<ResponseDto<RoutingRevisionDto>> SaveRoutingAsync(SaveRoutingRevisionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.RoutingWriters)) return Bad<RoutingRevisionDto>(MfText.NotAllowed);
            if (dto == null || dto.ItemId.HasValue == dto.ItemGroupId.HasValue) return Bad<RoutingRevisionDto>("کالا یا گروه کالا را (فقط یکی) انتخاب کنید.");
            if (dto.ItemId.HasValue && !await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == dto.ItemId.Value && !i.Deleted, ct)) return Bad<RoutingRevisionDto>("کالا پیدا نشد.");
            if (dto.ItemGroupId.HasValue && !await _uow.Set<ItemGroup>().AnyAsync(g => g.Id == dto.ItemGroupId.Value && !g.Deleted, ct)) return Bad<RoutingRevisionDto>("گروه کالا پیدا نشد.");
            List<OperationTemplate> steps = await MfRouting.StepsOfKeyAsync(_uow, dto.ItemId, dto.ItemGroupId, false, ct);
            if (steps.Count == 0) return Bad<RoutingRevisionDto>("برای این کالا یا گروه هنوز مرحله ساخت تعریف نشده است.");
            Dictionary<int, MfOperationStandard> standards = await MfRouting.StandardsAsync(_uow, steps.Select(s => s.Id), ct);
            OperationTemplate noStandard = steps.FirstOrDefault(s => !standards.ContainsKey(s.Id));
            if (noStandard != null) return Bad<RoutingRevisionDto>($"برای مرحله {noStandard.Sequence} ({noStandard.Title}) استاندارد زمان و مرکز کار ثبت نشده است.");
            IQueryable<MfRoutingRevision> same = _uow.Set<MfRoutingRevision>().Where(r => !r.Deleted);
            same = dto.ItemId.HasValue ? same.Where(r => r.ItemId == dto.ItemId.Value) : same.Where(r => r.ItemGroupId == dto.ItemGroupId.Value);
            if (await same.AnyAsync(r => r.Status == MfRoutingStatus.Draft, ct)) return Bad<RoutingRevisionDto>("یک بازنگری پیش‌نویس باز برای این کالا وجود دارد؛ ابتدا آن را تأیید یا حذف کنید.");
            int next = (await same.MaxAsync(r => (int?)r.Revision, ct) ?? 0) + 1;
            DateTimeOffset now = DateTimeOffset.Now;
            var row = new MfRoutingRevision
            {
                ItemId = dto.ItemId, ItemGroupId = dto.ItemGroupId, Revision = next, Status = MfRoutingStatus.Draft, Note = Clean(dto.Note),
                CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<MfRoutingRevision>().Add(row);
            await _uow.SaveChangesAsync(ct);
            await SyncGateAsync(row, ct);
            return ResponseDtoHelper.Ok(await RoutingDtoAsync(row, true, ct));
        }

        public async Task<ResponseDto<RoutingRevisionDto>> RoutingActionAsync(RoutingActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<RoutingRevisionDto>(MfText.InvalidData);
            MfRoutingRevision r = await _uow.Set<MfRoutingRevision>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return MfText.NotFound<RoutingRevisionDto>("بازنگری مسیر ساخت پیدا نشد.");
            if (!MfConcurrency.Apply(_uow, r, dto.RowVersion)) return Bad<RoutingRevisionDto>(MfText.InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "approve-production" && action != "approve-engineering" && action != "delete") return Bad<RoutingRevisionDto>("عمل نامعتبر است.");
            if (r.Status != MfRoutingStatus.Draft) return Bad<RoutingRevisionDto>("این بازنگری قبلاً تأیید شده و قابل تغییر نیست؛ بازنگری جدید بسازید.");
            DateTimeOffset now = DateTimeOffset.Now;

            if (action == "delete")
            {
                if (!_user.IsInRole(ProductionRoles.RoutingWriters)) return Bad<RoutingRevisionDto>(MfText.NotAllowed);
                r.Delete();
                r.ModifyDate = now; r.ModifierUserId = Me;
                await _uow.SaveChangesAsync(ct);
                await SyncGateAsync(r, ct, false);
                return ResponseDtoHelper.Ok(await RoutingDtoAsync(r, false, ct));
            }

            bool production = action == "approve-production";
            if (!_user.IsInRole(production ? ProductionRoles.Manager : ProductionRoles.Engineering))
                return Bad<RoutingRevisionDto>(production ? "تأیید تولیدی فقط توسط مدیر تولید انجام می‌شود." : "تأیید مهندسی فقط توسط مهندسی انجام می‌شود.");
            List<OperationTemplate> steps = await MfRouting.StepsOfKeyAsync(_uow, r.ItemId, r.ItemGroupId, false, ct);
            if (steps.Count == 0) return Bad<RoutingRevisionDto>("این مسیر ساخت مرحله‌ای ندارد.");
            Dictionary<int, MfOperationStandard> standards = await MfRouting.StandardsAsync(_uow, steps.Select(s => s.Id), ct);
            OperationTemplate noStandard = steps.FirstOrDefault(s => !standards.ContainsKey(s.Id));
            if (noStandard != null) return Bad<RoutingRevisionDto>($"برای مرحله {noStandard.Sequence} ({noStandard.Title}) استاندارد ثبت نشده است.");
            if (r.CreatorUserId == Me) return Bad<RoutingRevisionDto>(MfText.SameUser + " (سازنده بازنگری نمی‌تواند آن را تأیید کند)");
            HashSet<int> editors = await MfRouting.EditorsAsync(_uow, steps, ct);
            if (editors.Contains(Me)) return Bad<RoutingRevisionDto>(MfText.SameUser + " (نویسنده یا ویرایشگر مراحل نمی‌تواند تأیید کند)");
            if (production ? r.EngineeringApprovedByUserId == Me : r.ProductionApprovedByUserId == Me) return Bad<RoutingRevisionDto>(MfText.SameUser + " (دو تأیید باید از دو نفر مختلف باشد)");

            string hash = await MfRouting.HashAsync(_uow, steps, standards, ct);
            string message = null;
            if (r.ContentHash != null && r.ContentHash != hash)
            {
                // the steps changed after the first approval: that approval is void
                r.ProductionApprovedByUserId = null; r.ProductionApprovedAt = null; r.EngineeringApprovedByUserId = null; r.EngineeringApprovedAt = null;
                message = "مراحل پس از تأیید قبلی تغییر کرده بود؛ تأیید قبلی باطل شد و این تأیید اولین تأیید است.";
            }
            r.ContentHash = hash;
            if (production) { r.ProductionApprovedByUserId = Me; r.ProductionApprovedAt = now; }
            else { r.EngineeringApprovedByUserId = Me; r.EngineeringApprovedAt = now; }
            if (r.ProductionApprovedByUserId.HasValue && r.EngineeringApprovedByUserId.HasValue)
            {
                r.Status = MfRoutingStatus.Approved;
                r.ApprovedAt = now;
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<RoutingRevisionDto>(MfText.Concurrency);
            }
            await SyncGateAsync(r, ct);
            RoutingRevisionDto result = await RoutingDtoAsync(r, true, ct);
            return message == null ? ResponseDtoHelper.Ok(result) : ResponseDtoHelper.Ok(result, message);
        }

        private Task SyncGateAsync(MfRoutingRevision r, CancellationToken ct, bool? waiting = null) =>
            OfficeGates.SyncAsync(_notify, "mf:routing:" + r.Id, waiting ?? r.Status == MfRoutingStatus.Draft, "production", "routing", r.Id,
                $"بازنگری {r.Revision} مسیر ساخت منتظر تأیید است", "/production/routings/" + r.Id, ProductionRoles.Manager + "," + ProductionRoles.Engineering, ct);

        // ================================================================================== reason codes

        private static ReasonCodeDto ToDto(MfReasonCode r) => new ReasonCodeDto { Id = r.Id, Code = r.Code, Title = r.Title, Kind = r.Kind, IsActive = r.IsActive };

        public async Task<ResponseDto<PagedList<ReasonCodeDto>>> GetReasonCodesAsync(ReasonCodeQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return Bad<PagedList<ReasonCodeDto>>(MfText.NotAllowed);
            query ??= new ReasonCodeQuery();
            IQueryable<MfReasonCode> q = _uow.Set<MfReasonCode>().AsNoTracking().Where(r => !r.Deleted);
            if (query.Kind.HasValue) q = q.Where(r => r.Kind == query.Kind.Value);
            if (query.IsActive.HasValue) q = q.Where(r => r.IsActive == query.IsActive.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(r => r.Code.Contains(text) || r.Title.Contains(text));
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfReasonCode> rows = await q.OrderBy(r => r.Kind).ThenBy(r => r.Code).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(rows.Select(ToDto).ToList(), 1, int.MaxValue, ct), total, number, size));
        }

        public async Task<ResponseDto<ReasonCodeDto>> SaveReasonCodeAsync(SaveReasonCodeDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterEditors + "," + ProductionRoles.Supervisor)) return Bad<ReasonCodeDto>(MfText.NotAllowed);
            if (dto == null || !Enum.IsDefined(typeof(MfReasonKind), dto.Kind)) return Bad<ReasonCodeDto>(MfText.InvalidData);
            string code = Clean(dto.Code), title = Clean(dto.Title);
            if (code == null || title == null) return Bad<ReasonCodeDto>("کد و عنوان دلیل را وارد کنید.");
            if (await _uow.Set<MfReasonCode>().AnyAsync(r => r.Code == code && r.Kind == dto.Kind && !r.Deleted && r.Id != (dto.Id ?? 0), ct)) return Bad<ReasonCodeDto>("این کد دلیل قبلاً ثبت شده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            MfReasonCode row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<MfReasonCode>().FirstOrDefaultAsync(r => r.Id == dto.Id.Value && !r.Deleted, ct);
                if (row == null) return MfText.NotFound<ReasonCodeDto>("دلیل پیدا نشد.");
            }
            else
            {
                row = new MfReasonCode { CreateDate = now, CreatorUserId = Me };
                _uow.Set<MfReasonCode>().Add(row);
            }
            row.Code = code; row.Title = title; row.Kind = dto.Kind; row.IsActive = dto.IsActive; row.ModifyDate = now; row.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(row));
        }
    }
}
