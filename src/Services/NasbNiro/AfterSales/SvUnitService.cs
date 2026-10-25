using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ServiceDtos;
using Services.NasbNiro.Sales;

namespace Services.NasbNiro.AfterSales
{
    public interface ISvUnitService
    {
        Task<ResponseDto<PagedList<SvUnitDto>>> GetUnitsAsync(SvUnitQuery query, CancellationToken ct);
        Task<ResponseDto<SvUnitDto>> GetUnitAsync(int id, DateTime? asOf, CancellationToken ct);
        Task<ResponseDto<SvUnitDto>> SaveUnitAsync(SvSaveUnitDto dto, CancellationToken ct);
        Task<ResponseDto<SvUnitDto>> UnitActionAsync(SvUnitActionDto dto, CancellationToken ct);
        Task<ResponseDto<SvAsBuiltDto>> GetAsBuiltAsync(int id, CancellationToken ct);
    }

    /// <summary>The installed base: units drafted by the system at handover, confirmed and kept by service.</summary>
    public class SvUnitService : ISvUnitService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ITraceabilityService _trace;
        private readonly INotificationService _notify;

        public SvUnitService(IUnitOfWork uow, IInventoryUserContext user, ITraceabilityService trace, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _trace = trace ?? throw new ArgumentNullException(nameof(trace));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => SlText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => SlText.Bad<T>(m);

        // ================================================================================== status and dto

        /// <summary>Units covered by an active LTSA on the day.</summary>
        internal static async Task<HashSet<int>> UnderLtsaAsync(IUnitOfWork uow, DateTime on, CancellationToken ct)
        {
            DateTime day = on.Date;
            List<int> ids = await (from cu in uow.Set<SvContractUnit>().AsNoTracking()
                                   join c in uow.Set<SvContract>().AsNoTracking() on cu.ContractId equals c.Id
                                   where !c.Deleted && c.Status == SvContractStatus.Active && c.StartDate <= day && c.EndDate >= day
                                   select cu.UnitId).ToListAsync(ct);
            return ids.ToHashSet();
        }

        internal static SvUnitStatus StatusOf(SvInstalledUnit u, HashSet<int> ltsa, DateTime on) =>
            SvWarranty.Status(u.Decommissioned, ltsa.Contains(u.Id), SvWarranty.Covers(u.WarrantyStart, u.WarrantyEnd, on));

        private async Task<List<SvUnitDto>> DtosAsync(List<SvInstalledUnit> rows, DateTime asOf, CancellationToken ct)
        {
            List<int> customerIds = rows.Where(r => r.CustomerId.HasValue).Select(r => r.CustomerId.Value).Distinct().ToList();
            List<int> siteIds = rows.Where(r => r.SiteId.HasValue).Select(r => r.SiteId.Value).Distinct().ToList();
            List<int> contractIds = rows.Where(r => r.ContractId.HasValue).Select(r => r.ContractId.Value).Distinct().ToList();
            List<int> woIds = rows.Where(r => r.WorkOrderId.HasValue).Select(r => r.WorkOrderId.Value).Distinct().ToList();
            List<int> shipIds = rows.Where(r => r.ShipmentId.HasValue).Select(r => r.ShipmentId.Value).Distinct().ToList();
            List<int> serialIds = rows.Where(r => r.SerialUnitId.HasValue).Select(r => r.SerialUnitId.Value).Distinct().ToList();
            List<int> itemIds = rows.SelectMany(r => r.Spares.Select(s => s.ItemId)).Distinct().ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            Dictionary<int, string> sites = await _uow.Set<SlSite>().AsNoTracking().Where(s => siteIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Title, ct);
            Dictionary<int, string> contracts = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            Dictionary<int, string> orders = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            Dictionary<int, string> ships = await _uow.Set<PcShipment>().AsNoTracking().Where(s => shipIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Number, ct);
            Dictionary<int, string> serials = await _uow.Set<SerialUnit>().AsNoTracking().Where(s => serialIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.SerialNumber, ct);
            Dictionary<int, InventoryItem> items = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            HashSet<int> ltsa = await UnderLtsaAsync(_uow, asOf, ct);
            var spareLists = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking()
                .Where(d => !d.Deleted && contractIds.Contains(d.ContractId) && d.Title.Contains("یدکی")).Select(d => new { d.Id, d.ContractId, d.WorkOrderId, d.DocumentNumber, d.DocumentId }).ToListAsync(ct);

            return rows.Select(r =>
            {
                var list = spareLists.Where(d => r.ContractId == d.ContractId && (!d.WorkOrderId.HasValue || d.WorkOrderId == r.WorkOrderId)).OrderByDescending(d => d.WorkOrderId == r.WorkOrderId).ThenBy(d => d.Id).FirstOrDefault();
                SvUnitStatus status = StatusOf(r, ltsa, asOf);
                return new SvUnitDto
                {
                    TouchedByMe = r.EditorUserIds != null && LedgerMath.Touched(0, r.EditorUserIds, Me),
                    Id = r.Id, Tag = r.Tag, Title = r.Title, SerialUnitId = r.SerialUnitId, SerialNumber = r.SerialUnitId.HasValue ? serials.GetValueOrDefault(r.SerialUnitId.Value) : null,
                    ProductType = r.ProductType, ProductTypeText = SlText.Product(r.ProductType), CustomerId = r.CustomerId, CustomerName = r.CustomerId.HasValue ? customers.GetValueOrDefault(r.CustomerId.Value) : null,
                    SiteId = r.SiteId, SiteTitle = r.SiteId.HasValue ? sites.GetValueOrDefault(r.SiteId.Value) : null, ContractId = r.ContractId, ContractCode = r.ContractId.HasValue ? contracts.GetValueOrDefault(r.ContractId.Value) : null,
                    WorkOrderId = r.WorkOrderId, WorkOrderCode = r.WorkOrderId.HasValue ? orders.GetValueOrDefault(r.WorkOrderId.Value) : null,
                    ShipmentId = r.ShipmentId, ShipmentNumber = r.ShipmentId.HasValue ? ships.GetValueOrDefault(r.ShipmentId.Value) : null,
                    HandoverDate = r.HandoverDate, HandoverDateText = SlText.Date(r.HandoverDate), CommissioningDate = r.CommissioningDate, CommissioningDateText = SlText.Date(r.CommissioningDate),
                    WarrantyMonths = r.WarrantyMonths, WarrantyStart = r.WarrantyStart, WarrantyStartText = SlText.Date(r.WarrantyStart), WarrantyEnd = r.WarrantyEnd, WarrantyEndText = SlText.Date(r.WarrantyEnd),
                    WarrantySource = r.WarrantySource, Status = status, StatusText = SvText.UnitStatus(status), InWarranty = SvWarranty.Covers(r.WarrantyStart, r.WarrantyEnd, asOf),
                    RunningHours = r.RunningHours, RunningHoursAt = r.RunningHoursAt, RunningHoursAtText = SlText.Date(r.RunningHoursAt), Decommissioned = r.Decommissioned,
                    IsConfirmed = r.IsConfirmed, ConfirmedAtText = SlText.Stamp(r.ConfirmedAt), Note = r.Note,
                    Spares = r.Spares.OrderBy(s => s.Id).Select(s => new SvSpareDto
                    {
                        Id = s.Id, ItemId = s.ItemId, ItemCode = items.GetValueOrDefault(s.ItemId)?.Code, ItemName = items.GetValueOrDefault(s.ItemId)?.Title, Quantity = s.Quantity, Source = s.Source, Note = s.Note,
                    }).ToList(),
                    SpareListDeliverableId = list?.Id, SpareListDocumentNumber = list?.DocumentNumber, SpareListDocumentId = list?.DocumentId,
                    SpareListSource = list != null ? SvSpareSource.MdrList : (SvSpareSource?)null, SpareListSourceText = list != null ? "فهرست قطعات یدکی MDR دستور کار" : null,
                    RowVersion = SlConcurrency.Text(r.RowVersion),
                };
            }).ToList();
        }

        // ================================================================================== queries

        public async Task<ResponseDto<PagedList<SvUnitDto>>> GetUnitsAsync(SvUnitQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<PagedList<SvUnitDto>>(SlText.NotAllowed);
            query ??= new SvUnitQuery();
            DateTime asOf = (query.AsOf ?? DateTime.Today).Date;
            IQueryable<SvInstalledUnit> q = _uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => !u.Deleted);
            if (query.CustomerId.HasValue) q = q.Where(u => u.CustomerId == query.CustomerId.Value);
            if (query.ProductType.HasValue) q = q.Where(u => u.ProductType == query.ProductType.Value);
            if (query.NeedsConfirm == true) q = q.Where(u => !u.IsConfirmed && !u.Decommissioned);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(u => u.Tag.Contains(text) || u.Title.Contains(text));
            if (query.Status.HasValue)
            {
                // the status depends on the day, so it is cut in memory (the installed base is small)
                List<SvInstalledUnit> all = await q.Include(u => u.Spares).OrderByDescending(u => u.Id).ToListAsync(ct);
                HashSet<int> ltsa = await UnderLtsaAsync(_uow, asOf, ct);
                List<SvInstalledUnit> picked = all.Where(u => StatusOf(u, ltsa, asOf) == query.Status.Value).ToList();
                int size = SlText.Size(query.PageSize);
                int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
                List<SvUnitDto> dtos = await DtosAsync(picked.Skip((number - 1) * size).Take(size).ToList(), asOf, ct);
                PagedList<SvUnitDto> page = await PagedList<SvUnitDto>.CreateAsync(dtos, 1, 0, ct);
                page.TotalCount = picked.Count; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(picked.Count / (double)Math.Max(size, 1));
                return ResponseDtoHelper.Ok(page);
            }
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(u => u.Id), query.PageNumber, query.PageSize, async rows =>
            {
                List<int> ids = rows.Select(r => r.Id).ToList();
                List<SvInstalledUnit> full = await _uow.Set<SvInstalledUnit>().AsNoTracking().Include(u => u.Spares).Where(u => ids.Contains(u.Id)).ToListAsync(ct);
                return await DtosAsync(ids.Select(id => full.First(f => f.Id == id)).ToList(), asOf, ct);
            }, ct));
        }

        public async Task<ResponseDto<SvUnitDto>> GetUnitAsync(int id, DateTime? asOf, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<SvUnitDto>(SlText.NotAllowed);
            SvInstalledUnit row = await _uow.Set<SvInstalledUnit>().AsNoTracking().Include(u => u.Spares).FirstOrDefaultAsync(u => u.Id == id && !u.Deleted, ct);
            if (row == null) return SlText.NotFound<SvUnitDto>("واحد نصب‌شده پیدا نشد.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<SvInstalledUnit> { row }, (asOf ?? DateTime.Today).Date, ct))[0]);
        }

        public async Task<ResponseDto<SvAsBuiltDto>> GetAsBuiltAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<SvAsBuiltDto>(SlText.NotAllowed);
            SvInstalledUnit row = await _uow.Set<SvInstalledUnit>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && !u.Deleted, ct);
            if (row == null) return SlText.NotFound<SvAsBuiltDto>("واحد نصب‌شده پیدا نشد.");
            var dto = new SvAsBuiltDto { UnitId = row.Id, Tag = row.Tag };
            if (row.SerialUnitId.HasValue)
            {
                ResponseDto<SerialTraceDto> trace = await _trace.GetSerialTraceAsync(row.SerialUnitId.Value, ct);
                if (trace?.Data != null) dto.Trace = trace.Data;
            }
            return ResponseDtoHelper.Ok(dto);
        }

        // ================================================================================== save and actions

        public async Task<ResponseDto<SvUnitDto>> SaveUnitAsync(SvSaveUnitDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Writers)) return Bad<SvUnitDto>(SlText.NotAllowed);
            if (dto == null || Clean(dto.Title) == null) return Bad<SvUnitDto>("عنوان واحد را وارد کنید.");
            string tooLong = SvLimits.First((dto.Title, SvLimits.Title, "عنوان واحد"), (dto.Tag, SvLimits.Name, "شماره tag"), (dto.Note, SvLimits.Text, "یادداشت واحد"));
            if (tooLong != null) return Bad<SvUnitDto>(tooLong);
            string spareTooLong = (dto.Spares ?? new List<SvSaveSpareDto>()).Where(x => x != null).Select(x => SvLimits.TooLong(Clean(x.Note), SvLimits.Text, "یادداشت قطعه یدکی")).FirstOrDefault(m => m != null);
            if (spareTooLong != null) return Bad<SvUnitDto>(spareTooLong);
            if (dto.WarrantyMonths.HasValue && dto.WarrantyMonths.Value <= 0) return Bad<SvUnitDto>("ماه‌های گارانتی باید بیشتر از صفر باشد.");
            if (dto.WarrantyStart.HasValue != dto.WarrantyEnd.HasValue) return Bad<SvUnitDto>("شروع و پایان گارانتی را با هم وارد کنید.");
            if (dto.WarrantyStart.HasValue && dto.WarrantyEnd.Value.Date < dto.WarrantyStart.Value.Date) return Bad<SvUnitDto>("پایان گارانتی قبل از شروع آن است.");
            if (dto.WarrantyStart.HasValue && dto.WarrantyMonths.HasValue) return Bad<SvUnitDto>("گارانتی یا با ماه‌ها وارد می‌شود یا با تاریخ شروع و پایان، نه هر دو.");
            List<SvSaveSpareDto> spares = (dto.Spares ?? new List<SvSaveSpareDto>()).Where(s => s != null).ToList();
            if (spares.Any(s => s.Quantity <= 0)) return Bad<SvUnitDto>("تعداد هر قطعه یدکی باید بیشتر از صفر باشد.");
            if (spares.GroupBy(s => s.ItemId).Any(g => g.Count() > 1)) return Bad<SvUnitDto>("هر قطعه یدکی فقط یک بار می‌آید.");
            List<int> spareIds = spares.Select(s => s.ItemId).ToList();
            if (spareIds.Count > 0 && await _uow.Set<InventoryItem>().CountAsync(i => spareIds.Contains(i.Id) && !i.Deleted, ct) != spareIds.Count) return Bad<SvUnitDto>("کالای یکی از قطعات یدکی پیدا نشد.");
            if (dto.CustomerId.HasValue && !await _uow.Set<FnCustomer>().AnyAsync(c => c.Id == dto.CustomerId.Value, ct)) return Bad<SvUnitDto>("مشتری پیدا نشد.");
            if (dto.SiteId.HasValue)
            {
                int? siteCustomer = await _uow.Set<SlSite>().Where(s => s.Id == dto.SiteId.Value && !s.Deleted).Select(s => (int?)s.CustomerId).FirstOrDefaultAsync(ct);
                if (!siteCustomer.HasValue) return Bad<SvUnitDto>("سایت پیدا نشد.");
                if (dto.CustomerId.HasValue && siteCustomer.Value != dto.CustomerId.Value) return Bad<SvUnitDto>("این سایت مال مشتری انتخاب‌شده نیست.");
            }
            string tag = Clean(dto.Tag);
            if (tag != null && await _uow.Set<SvInstalledUnit>().AnyAsync(u => u.Tag == tag && u.Id != (dto.Id ?? 0) && !u.Deleted, ct)) return Bad<SvUnitDto>("این شماره tag برای واحد دیگری ثبت شده است.");

            DateTimeOffset now = DateTimeOffset.Now;
            SvInstalledUnit row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SvInstalledUnit>().Include(u => u.Spares).FirstOrDefaultAsync(u => u.Id == dto.Id.Value && !u.Deleted, ct);
                if (row == null) return SlText.NotFound<SvUnitDto>("واحد نصب‌شده پیدا نشد.");
                if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SvUnitDto>(SlText.InvalidData);
                if (row.Decommissioned) return Bad<SvUnitDto>("واحد از رده خارج شده است؛ ابتدا آن را دوباره فعال کنید.");
            }
            else
            {
                row = new SvInstalledUnit { CreateDate = now, CreatorUserId = Me, Tag = tag };
                row.IsConfirmed = false;
                _uow.Set<SvInstalledUnit>().Add(row);
            }
            if (!dto.Id.HasValue && tag == null) row.Tag = await InventoryNumbers.NextNumberAsync(_uow, SvNumbers.Unit, "SVU", DateTime.Today, ct);
            if (dto.Id.HasValue) row.Tag = tag ?? row.Tag;
            row.Title = Clean(dto.Title);
            if (dto.SerialUnitId.HasValue || !dto.Id.HasValue) row.SerialUnitId = dto.SerialUnitId ?? row.SerialUnitId;
            if (dto.ProductType.HasValue) row.ProductType = dto.ProductType.Value;
            if (dto.CustomerId.HasValue || !dto.Id.HasValue) row.CustomerId = dto.CustomerId ?? row.CustomerId;
            if (dto.SiteId.HasValue || !dto.Id.HasValue) row.SiteId = dto.SiteId ?? row.SiteId;
            if (dto.ContractId.HasValue || !dto.Id.HasValue) row.ContractId = dto.ContractId ?? row.ContractId;
            if (dto.WorkOrderId.HasValue || !dto.Id.HasValue) row.WorkOrderId = dto.WorkOrderId ?? row.WorkOrderId;
            if (dto.HandoverDate.HasValue) row.HandoverDate = dto.HandoverDate.Value.Date;
            if (dto.CommissioningDate.HasValue) row.CommissioningDate = dto.CommissioningDate.Value.Date;
            row.Note = Clean(dto.Note) ?? row.Note;

            if (dto.WarrantyStart.HasValue)
            {
                row.WarrantyMonths = null; row.WarrantyStart = dto.WarrantyStart.Value.Date; row.WarrantyEnd = dto.WarrantyEnd.Value.Date; row.WarrantySource = SvWarrantySource.Manual;
            }
            else if (dto.WarrantyMonths.HasValue)
            {
                DateTime? start = row.WarrantyStart ?? row.HandoverDate;
                if (!start.HasValue) return Bad<SvUnitDto>("برای محاسبه‌ی گارانتی از ماه‌ها، تاریخ تحویل واحد را وارد کنید.");
                if (row.WarrantySource != SvWarrantySource.Months) start = row.HandoverDate ?? start;
                row.WarrantyMonths = dto.WarrantyMonths; row.WarrantyStart = start.Value.Date; row.WarrantyEnd = SvWarranty.End(start.Value, dto.WarrantyMonths.Value); row.WarrantySource = SvWarrantySource.Months;
            }
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            row.ModifyDate = now; row.ModifierUserId = Me;

            if (dto.Spares != null)
            {
                foreach (SvUnitSpare old in row.Spares.Where(s => s.Source == SvSpareSource.Manual).ToList()) { row.Spares.Remove(old); _uow.Set<SvUnitSpare>().Remove(old); }
                foreach (SvSaveSpareDto s in spares) row.Spares.Add(new SvUnitSpare { ItemId = s.ItemId, Quantity = s.Quantity, Source = SvSpareSource.Manual, Note = Clean(s.Note) });
            }
            // an edit after the confirmation sends the unit back to a second person
            if (dto.Id.HasValue && row.IsConfirmed) { row.IsConfirmed = false; row.ConfirmedByUserId = null; row.ConfirmedAt = null; }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvUnitDto>(SlText.Concurrency); }
            await SvGates.UnitAsync(_notify, row, ct);
            return await GetUnitAsync(row.Id, null, ct);
        }

        public async Task<ResponseDto<SvUnitDto>> UnitActionAsync(SvUnitActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Writers)) return Bad<SvUnitDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SvUnitDto>(SlText.InvalidData);
            SvInstalledUnit row = await _uow.Set<SvInstalledUnit>().Include(u => u.Spares).FirstOrDefaultAsync(u => u.Id == dto.Id && !u.Deleted, ct);
            if (row == null) return SlText.NotFound<SvUnitDto>("واحد نصب‌شده پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SvUnitDto>(SlText.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            switch ((dto.Action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "confirm":
                    if (row.IsConfirmed) return Bad<SvUnitDto>("واحد قبلاً تأیید شده است.");
                    if (row.EditorUserIds != null && LedgerMath.Touched(0, row.EditorUserIds, Me)) return Bad<SvUnitDto>(SlText.SameUser);
                    if (Clean(row.Tag) == null) return Bad<SvUnitDto>("شماره tag واحد را وارد کنید.");
                    if (!row.CustomerId.HasValue) return Bad<SvUnitDto>("مشتری واحد را مشخص کنید.");
                    if (!row.SiteId.HasValue) return Bad<SvUnitDto>("سایت نصب واحد را مشخص کنید.");
                    row.IsConfirmed = true; row.ConfirmedByUserId = Me; row.ConfirmedAt = now;
                    break;
                case "decommission":
                    if (row.Decommissioned) return Bad<SvUnitDto>("واحد قبلاً از رده خارج شده است.");
                    row.Decommissioned = true; row.DecommissionedAt = now.LocalDateTime.Date;
                    break;
                case "reactivate":
                    if (!row.Decommissioned) return Bad<SvUnitDto>("واحد از رده خارج نشده است.");
                    row.Decommissioned = false; row.DecommissionedAt = null;
                    break;
                default:
                    return Bad<SvUnitDto>("عملیات نامعتبر است.");
            }
            row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SvUnitDto>(SlText.Concurrency); }
            await SvGates.UnitAsync(_notify, row, ct);
            return await GetUnitAsync(row.Id, null, ct);
        }
    }
}
