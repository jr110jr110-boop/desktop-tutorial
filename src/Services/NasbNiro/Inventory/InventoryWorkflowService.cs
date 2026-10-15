using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Inventory
{
    public interface IInventoryWorkflowService
    {
        Task<ResponseDto<PagedList<MaterialRequestListItemDto>>> GetRequestsAsync(MaterialRequestQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<MaterialRequestDto>> GetRequestAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<MaterialRequestDto>> SaveRequestAsync(SaveMaterialRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<MaterialRequestDto>> RequestActionAsync(MaterialRequestActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteRequestAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<List<StockCountListItemDto>>> GetCountsAsync(int? warehouseId, CancellationToken cancellationToken);
        Task<ResponseDto<StockCountDto>> GetCountAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<StockCountDto>> StartCountAsync(StartStockCountDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<StockCountDto>> SaveCountLinesAsync(SaveCountLinesDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<StockCountDto>> CountActionAsync(StockCountActionDto dto, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Material requests (no issue without an approved request) and stock counts (cycle and annual) whose differences
    /// become adjustment documents only after the warehouse supervisor and the planning manager approve them.
    /// </summary>
    public class InventoryWorkflowService : IInventoryWorkflowService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IInventoryDocumentService _documents;

        public InventoryWorkflowService(IUnitOfWork uow, IInventoryUserContext user, IInventoryDocumentService documents)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        }

        private DbSet<MaterialRequest> Requests => _uow.Set<MaterialRequest>();
        private DbSet<StockCount> Counts => _uow.Set<StockCount>();

        /// <summary>Warehouses whose stock can be issued (not quarantine, scrap, contractor or customer property).</summary>
        internal static bool IsUsable(WarehouseKind kind) =>
            kind != WarehouseKind.Quarantine && kind != WarehouseKind.Scrap && kind != WarehouseKind.Subcontractor && kind != WarehouseKind.CustomerProperty;

        // ------------------------------------------------------------------------ material requests

        public async Task<ResponseDto<PagedList<MaterialRequestListItemDto>>> GetRequestsAsync(MaterialRequestQuery query, CancellationToken cancellationToken)
        {
            query ??= new MaterialRequestQuery();
            IQueryable<MaterialRequest> requests = Requests.Where(r => !r.Deleted);
            if (query.Status.HasValue) requests = requests.Where(r => r.Status == query.Status.Value);
            if (query.WorkOrderId.HasValue) requests = requests.Where(r => r.WorkOrderId == query.WorkOrderId.Value);
            string text = InventoryText.Clean(query.Contains);
            if (text != null)
                requests = requests.Where(r => r.Number.Contains(text) || r.Purpose.Contains(text) || r.WorkOrder.Code.Contains(text) ||
                                               r.Lines.Any(l => l.Item.Code.Contains(text) || l.Item.Title.Contains(text)));
            IQueryable<MaterialRequestListItemDto> rows = requests.OrderByDescending(r => r.RequestDate).ThenByDescending(r => r.Id)
                .Select(r => new MaterialRequestListItemDto
                {
                    Id = r.Id, Number = r.Number, RequestDate = r.RequestDate, Status = r.Status, WorkOrderId = r.WorkOrderId,
                    WorkOrderCode = r.WorkOrder.Code, WarehouseTitle = r.Warehouse.Title, Purpose = r.Purpose, LinesCount = r.Lines.Count(),
                    PlanningApproved = r.PlanningApprovedAt != null, ManufacturingApproved = r.ManufacturingApprovedAt != null,
                });
            return ResponseDtoHelper.Ok(await PagedList<MaterialRequestListItemDto>.CreateAsync(rows, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<MaterialRequestDto>> GetRequestAsync(int id, CancellationToken cancellationToken)
        {
            MaterialRequestDto request = await Requests.Where(r => r.Id == id && !r.Deleted).Select(r => new MaterialRequestDto
            {
                Id = r.Id, Number = r.Number, RequestDate = r.RequestDate, Status = r.Status, WorkOrderId = r.WorkOrderId,
                WorkOrderCode = r.WorkOrder.Code, WarehouseId = r.WarehouseId, WarehouseTitle = r.Warehouse.Title, Purpose = r.Purpose,
                LinesCount = r.Lines.Count(), PlanningApproved = r.PlanningApprovedAt != null, ManufacturingApproved = r.ManufacturingApprovedAt != null,
                CreateDate = r.CreateDate, SubmittedAt = r.SubmittedAt, PlanningApprovedAt = r.PlanningApprovedAt,
                ManufacturingApprovedAt = r.ManufacturingApprovedAt, RejectedAt = r.RejectedAt, RejectReason = r.RejectReason, ClosedAt = r.ClosedAt,
                Lines = r.Lines.OrderBy(l => l.RowNumber).Select(l => new MaterialRequestLineDto
                {
                    Id = l.Id, RowNumber = l.RowNumber, ItemId = l.ItemId, ItemCode = l.Item.Code, ItemTitle = l.Item.Title, Unit = l.Item.Unit,
                    TrackingMode = l.Item.TrackingMode, Quantity = l.Quantity, IssuedQuantity = l.IssuedQuantity, Description = l.Description,
                }).ToList(),
            }).FirstOrDefaultAsync(cancellationToken);
            if (request == null) return ResponseDtoHelper.NotFound<MaterialRequestDto>("درخواست کالا پیدا نشد.");

            // availability for the warehouse
            List<int> itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();
            var stock = await _uow.Set<StockBalance>()
                .Where(b => itemIds.Contains(b.ItemId) && b.Quantity > 0 && (!request.WarehouseId.HasValue || b.WarehouseId == request.WarehouseId.Value))
                .Select(b => new { b.ItemId, b.Quantity, b.Warehouse.Kind }).ToListAsync(cancellationToken);
            foreach (var group in request.Lines.GroupBy(l => l.ItemId))
            {
                decimal available = stock.Where(s => s.ItemId == group.Key && IsUsable(s.Kind)).Sum(s => s.Quantity);
                foreach (MaterialRequestLineDto line in group.OrderBy(l => l.RowNumber))
                {
                    line.Remaining = Math.Max(0, line.Quantity - line.IssuedQuantity);
                    line.Available = available;
                    line.Shortage = Math.Max(0, line.Remaining - available);
                    available = Math.Max(0, available - line.Remaining);
                }
            }

            request.Issues = await _uow.Set<InventoryDocument>()
                .Where(d => d.MaterialRequestId == id && !d.Deleted && d.Status != InventoryDocumentStatus.Draft)
                .OrderBy(d => d.Id).Select(d => d.Number + (d.Status == InventoryDocumentStatus.Cancelled ? " (باطل)" : "")).ToListAsync(cancellationToken);

            var raw = await Requests.Where(r => r.Id == id).Select(r => new
            {
                r.CreatorUserId, r.PlanningApprovedByUserId, r.ManufacturingApprovedByUserId, r.RejectedByUserId,
            }).FirstAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[]
            {
                raw.CreatorUserId, raw.PlanningApprovedByUserId, raw.ManufacturingApprovedByUserId, raw.RejectedByUserId,
            }, cancellationToken);
            request.CreatorName = names.Name(raw.CreatorUserId);
            request.PlanningApprovedByName = names.Name(raw.PlanningApprovedByUserId);
            request.ManufacturingApprovedByName = names.Name(raw.ManufacturingApprovedByUserId);
            request.RejectedByName = names.Name(raw.RejectedByUserId);
            return ResponseDtoHelper.Ok(request);
        }

        public async Task<ResponseDto<MaterialRequestDto>> SaveRequestAsync(SaveMaterialRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<MaterialRequestDto>(InventoryMessages.InvalidData);
            if (dto.RequestDate == default) return ResponseDtoHelper.BadRequest<MaterialRequestDto>("تاریخ درخواست را وارد کنید.");
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && !w.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<MaterialRequestDto>("دستور ساخت پیدا نشد.");
            if (!dto.WorkOrderId.HasValue && InventoryText.Clean(dto.Purpose) == null)
                return ResponseDtoHelper.BadRequest<MaterialRequestDto>("دستور ساخت (پروژه) را انتخاب کنید؛ برای مصرف عمومی یا تعمیرات، محل مصرف را در «شرح» بنویسید.");
            if (dto.WarehouseId.HasValue && !await _uow.Set<Warehouse>().AnyAsync(w => w.Id == dto.WarehouseId.Value && !w.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<MaterialRequestDto>(InventoryMessages.WarehouseNotFound);
            List<SaveMaterialRequestLineDto> lines = (dto.Lines ?? new List<SaveMaterialRequestLineDto>()).Where(l => l != null).ToList();
            if (lines.Count == 0) return ResponseDtoHelper.BadRequest<MaterialRequestDto>("درخواست حداقل یک ردیف لازم دارد.");
            List<int> itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
            int found = await _uow.Set<InventoryItem>().CountAsync(i => itemIds.Contains(i.Id) && !i.Deleted && i.IsActive, cancellationToken);
            if (found != itemIds.Count) return ResponseDtoHelper.BadRequest<MaterialRequestDto>("یکی از کالاها پیدا نشد یا غیرفعال است.");
            if (lines.Any(l => l.Quantity <= 0)) return ResponseDtoHelper.BadRequest<MaterialRequestDto>("مقدار همه ردیف‌ها باید بیشتر از صفر باشد.");

            MaterialRequest request;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                request = await Requests.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == dto.Id.Value && !r.Deleted, cancellationToken);
                if (request == null) return ResponseDtoHelper.NotFound<MaterialRequestDto>("درخواست کالا پیدا نشد.");
                if (request.Status != MaterialRequestStatus.Draft) return ResponseDtoHelper.BadRequest<MaterialRequestDto>("فقط درخواست پیش‌نویس ویرایش می‌شود.");
                _uow.Set<MaterialRequestLine>().RemoveRange(request.Lines);
                request.Lines = new List<MaterialRequestLine>();
            }
            else
            {
                request = new MaterialRequest { Status = MaterialRequestStatus.Draft, CreateDate = now, CreatorUserId = _user.UserId, Lines = new List<MaterialRequestLine>() };
                Requests.Add(request);
            }
            request.RequestDate = dto.RequestDate.Date;
            request.WorkOrderId = dto.WorkOrderId;
            request.WarehouseId = dto.WarehouseId;
            request.Purpose = InventoryText.Clean(dto.Purpose);
            request.ModifyDate = now;
            request.ModifierUserId = _user.UserId;
            int row = 1;
            foreach (SaveMaterialRequestLineDto line in lines)
                request.Lines.Add(new MaterialRequestLine { RowNumber = row++, ItemId = line.ItemId, Quantity = line.Quantity, Description = InventoryText.Clean(line.Description) });
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetRequestAsync(request.Id, cancellationToken);
        }

        public async Task<ResponseDto<bool>> DeleteRequestAsync(int id, CancellationToken cancellationToken)
        {
            MaterialRequest request = await Requests.FirstOrDefaultAsync(r => r.Id == id && !r.Deleted, cancellationToken);
            if (request == null) return ResponseDtoHelper.NotFound<bool>("درخواست کالا پیدا نشد.");
            if (request.Status != MaterialRequestStatus.Draft) return ResponseDtoHelper.BadRequest<bool>("فقط درخواست پیش‌نویس حذف می‌شود.");
            request.Delete();
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }

        public async Task<ResponseDto<MaterialRequestDto>> RequestActionAsync(MaterialRequestActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<MaterialRequestDto>(InventoryMessages.InvalidData);
            MaterialRequest request = await Requests.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == dto.Id && !r.Deleted, cancellationToken);
            if (request == null) return ResponseDtoHelper.NotFound<MaterialRequestDto>("درخواست کالا پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            int me = _user.UserId;
            switch ((dto.Action ?? "").Trim().ToLowerInvariant())
            {
                case "submit":
                    if (request.Status != MaterialRequestStatus.Draft) return ResponseDtoHelper.BadRequest<MaterialRequestDto>("درخواست قبلاً ارسال شده است.");
                    request.Status = MaterialRequestStatus.Submitted;
                    request.SubmittedAt = now;
                    request.Number ??= await InventoryNumbers.NextNumberAsync(_uow, InventoryNumbers.MaterialRequest, "MR", request.RequestDate, cancellationToken);
                    break;
                case "approve-planning":
                case "approve-manufacturing":
                {
                    if (request.Status != MaterialRequestStatus.Submitted) return ResponseDtoHelper.BadRequest<MaterialRequestDto>("فقط درخواست ارسال‌شده تایید می‌شود.");
                    bool planning = dto.Action.Trim().ToLowerInvariant() == "approve-planning";
                    if (!_user.IsInRole(planning ? InventoryRoles.RolePlanning : InventoryRoles.RoleManufacturing))
                        return ResponseDtoHelper.BadRequest<MaterialRequestDto>(InventoryMessages.NotAllowed + (planning ? " (برنامه‌ریزی و کنترل پروژه)" : " (مدیر تولید)"));
                    int? other = planning ? request.ManufacturingApprovedByUserId : request.PlanningApprovedByUserId;
                    if (other == me) return ResponseDtoHelper.BadRequest<MaterialRequestDto>(InventoryMessages.SameUser);
                    if (planning) { request.PlanningApprovedAt = now; request.PlanningApprovedByUserId = me; }
                    else { request.ManufacturingApprovedAt = now; request.ManufacturingApprovedByUserId = me; }
                    if (request.PlanningApprovedAt.HasValue && request.ManufacturingApprovedAt.HasValue) request.Status = MaterialRequestStatus.Approved;
                    break;
                }
                case "reject":
                {
                    if (request.Status != MaterialRequestStatus.Submitted) return ResponseDtoHelper.BadRequest<MaterialRequestDto>("فقط درخواست ارسال‌شده رد می‌شود.");
                    if (!_user.IsInRole(InventoryRoles.RolePlanning) && !_user.IsInRole(InventoryRoles.RoleManufacturing))
                        return ResponseDtoHelper.BadRequest<MaterialRequestDto>(InventoryMessages.NotAllowed);
                    string reason = InventoryText.Clean(dto.Reason);
                    if (reason == null) return ResponseDtoHelper.BadRequest<MaterialRequestDto>("علت رد درخواست را بنویسید.");
                    request.Status = MaterialRequestStatus.Rejected;
                    request.RejectedAt = now;
                    request.RejectedByUserId = me;
                    request.RejectReason = reason;
                    break;
                }
                case "close":
                    if (request.Status != MaterialRequestStatus.Approved) return ResponseDtoHelper.BadRequest<MaterialRequestDto>("فقط درخواست تاییدشده بسته می‌شود.");
                    if (!_user.IsInRole(InventoryRoles.RolePlanning) && !_user.IsInRole(InventoryRoles.RoleWarehouse))
                        return ResponseDtoHelper.BadRequest<MaterialRequestDto>(InventoryMessages.NotAllowed);
                    request.Status = MaterialRequestStatus.Closed;
                    request.ClosedAt = now;
                    request.ClosedByUserId = me;
                    break;
                default:
                    return ResponseDtoHelper.BadRequest<MaterialRequestDto>("عملیات نامعتبر است.");
            }
            request.ModifyDate = now;
            request.ModifierUserId = me;
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ResponseDtoHelper.BadRequest<MaterialRequestDto>(InventoryMessages.ConcurrencyConflict);
            }
            return await GetRequestAsync(request.Id, cancellationToken);
        }

        // ----------------------------------------------------------------------------- stock counts

        private static decimal? Difference(StockCountLine l)
        {
            decimal? counted = l.RecountedQuantity ?? l.CountedQuantity;
            return counted.HasValue ? counted.Value - l.SystemQuantity : (decimal?)null;
        }

        public async Task<ResponseDto<List<StockCountListItemDto>>> GetCountsAsync(int? warehouseId, CancellationToken cancellationToken)
        {
            IQueryable<StockCount> counts = Counts.Where(c => !c.Deleted);
            if (warehouseId.HasValue) counts = counts.Where(c => c.WarehouseId == warehouseId.Value);
            List<StockCountListItemDto> rows = await counts.OrderByDescending(c => c.Id).Take(300).Select(c => new StockCountListItemDto
            {
                Id = c.Id, Number = c.Number, Kind = c.Kind, Status = c.Status, CountDate = c.CountDate, WarehouseId = c.WarehouseId,
                WarehouseTitle = c.Warehouse.Title, GroupCode = c.GroupCode, LinesCount = c.Lines.Count(),
                DifferencesCount = c.Lines.Count(l => (l.RecountedQuantity ?? l.CountedQuantity) != null && (l.RecountedQuantity ?? l.CountedQuantity) != l.SystemQuantity),
            }).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(rows);
        }

        public async Task<ResponseDto<StockCountDto>> GetCountAsync(int id, CancellationToken cancellationToken)
        {
            StockCount count = await Counts.Include(c => c.Warehouse).Include(c => c.Lines).ThenInclude(l => l.Item)
                .Include(c => c.Lines).ThenInclude(l => l.Location).Include(c => c.Lines).ThenInclude(l => l.Lot)
                .AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && !c.Deleted, cancellationToken);
            if (count == null) return ResponseDtoHelper.NotFound<StockCountDto>("شمارش پیدا نشد.");
            var docIds = new[] { count.IncreaseDocumentId, count.DecreaseDocumentId }.Where(x => x.HasValue).Select(x => x.Value).ToList();
            Dictionary<int, string> numbers = await _uow.Set<InventoryDocument>().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow,
                new int?[] { count.CreatorUserId, count.WarehouseApprovedByUserId, count.PlanningApprovedByUserId }, cancellationToken);
            var dto = new StockCountDto
            {
                Id = count.Id, Number = count.Number, Kind = count.Kind, Status = count.Status, CountDate = count.CountDate,
                WarehouseId = count.WarehouseId, WarehouseTitle = count.Warehouse.Title, GroupCode = count.GroupCode, LocationId = count.LocationId,
                Description = count.Description, SnapshotAt = count.SnapshotAt, CreatorName = names.Name(count.CreatorUserId),
                WarehouseApprovedAt = count.WarehouseApprovedAt, WarehouseApprovedByName = names.Name(count.WarehouseApprovedByUserId),
                PlanningApprovedAt = count.PlanningApprovedAt, PlanningApprovedByName = names.Name(count.PlanningApprovedByUserId),
                PostedAt = count.PostedAt, IncreaseDocumentId = count.IncreaseDocumentId, DecreaseDocumentId = count.DecreaseDocumentId,
                IncreaseDocumentNumber = count.IncreaseDocumentId.HasValue && numbers.TryGetValue(count.IncreaseDocumentId.Value, out string inc) ? inc : null,
                DecreaseDocumentNumber = count.DecreaseDocumentId.HasValue && numbers.TryGetValue(count.DecreaseDocumentId.Value, out string dec) ? dec : null,
                Lines = count.Lines.OrderBy(l => l.Location?.Code).ThenBy(l => l.Item.Code).ThenBy(l => l.Lot?.LotNumber).Select(l => new StockCountLineDto
                {
                    Id = l.Id, ItemId = l.ItemId, ItemCode = l.Item.Code, ItemTitle = l.Item.Title, Unit = l.Item.Unit,
                    LocationId = l.LocationId, LocationCode = l.Location?.Code, LotId = l.LotId, LotNumber = l.Lot?.LotNumber,
                    SystemQuantity = l.SystemQuantity, CountedQuantity = l.CountedQuantity, RecountedQuantity = l.RecountedQuantity,
                    Difference = Difference(l), Note = l.Note,
                }).ToList(),
            };
            dto.LinesCount = dto.Lines.Count;
            dto.DifferencesCount = dto.Lines.Count(l => l.Difference.HasValue && l.Difference.Value != 0);
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<StockCountDto>> StartCountAsync(StartStockCountDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<StockCountDto>(InventoryMessages.InvalidData);
            if (!Enum.IsDefined(typeof(StockCountKind), dto.Kind)) return ResponseDtoHelper.BadRequest<StockCountDto>("نوع شمارش را انتخاب کنید.");
            if (dto.CountDate == default) return ResponseDtoHelper.BadRequest<StockCountDto>("تاریخ شمارش را وارد کنید.");
            Warehouse warehouse = await _uow.Set<Warehouse>().FirstOrDefaultAsync(w => w.Id == dto.WarehouseId && !w.Deleted, cancellationToken);
            if (warehouse == null) return ResponseDtoHelper.BadRequest<StockCountDto>(InventoryMessages.WarehouseNotFound);
            if (await Counts.AnyAsync(c => !c.Deleted && c.WarehouseId == dto.WarehouseId &&
                                           (c.Status == StockCountStatus.Counting || c.Status == StockCountStatus.Submitted || c.Status == StockCountStatus.Approved), cancellationToken))
                return ResponseDtoHelper.BadRequest<StockCountDto>("برای این انبار یک شمارش باز وجود دارد؛ اول آن را ثبت یا ابطال کنید.");

            string groupCode = InventoryText.Clean(dto.GroupCode);
            IQueryable<StockBalance> balances = _uow.Set<StockBalance>().Where(b => b.WarehouseId == dto.WarehouseId && b.Quantity != 0);
            if (dto.LocationId.HasValue) balances = balances.Where(b => b.LocationId == dto.LocationId.Value);
            if (groupCode != null) balances = balances.Where(b => b.Item.Code.StartsWith(groupCode));
            var snapshot = await balances.Select(b => new { b.ItemId, b.LocationId, b.LotId, b.Quantity }).ToListAsync(cancellationToken);

            DateTimeOffset now = DateTimeOffset.Now;
            var count = new StockCount
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, InventoryNumbers.StockCount, "CNT", dto.CountDate, cancellationToken),
                Kind = dto.Kind,
                Status = StockCountStatus.Counting,
                CountDate = dto.CountDate.Date,
                WarehouseId = dto.WarehouseId,
                LocationId = dto.LocationId,
                GroupCode = groupCode,
                Description = InventoryText.Clean(dto.Description),
                SnapshotAt = now,
                CreateDate = now,
                CreatorUserId = _user.UserId,
                Lines = snapshot.Select(s => new StockCountLine { ItemId = s.ItemId, LocationId = s.LocationId, LotId = s.LotId, SystemQuantity = s.Quantity }).ToList(),
            };
            Counts.Add(count);
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetCountAsync(count.Id, cancellationToken);
        }

        public async Task<ResponseDto<StockCountDto>> SaveCountLinesAsync(SaveCountLinesDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<StockCountDto>(InventoryMessages.InvalidData);
            StockCount count = await Counts.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == dto.Id && !c.Deleted, cancellationToken);
            if (count == null) return ResponseDtoHelper.NotFound<StockCountDto>("شمارش پیدا نشد.");
            if (count.Status != StockCountStatus.Counting) return ResponseDtoHelper.BadRequest<StockCountDto>("شمارش ارسال شده و قابل تغییر نیست.");
            foreach (CountLineEntryDto entry in dto.Lines ?? new List<CountLineEntryDto>())
            {
                if (entry == null) continue;
                if (entry.CountedQuantity < 0 || entry.RecountedQuantity < 0) return ResponseDtoHelper.BadRequest<StockCountDto>("مقدار شمارش منفی نمی‌شود.");
                StockCountLine line;
                if (entry.LineId.HasValue)
                {
                    line = count.Lines.FirstOrDefault(l => l.Id == entry.LineId.Value);
                    if (line == null) return ResponseDtoHelper.BadRequest<StockCountDto>("ردیف شمارش پیدا نشد.");
                }
                else
                {
                    // found in the warehouse but not in the system
                    if (!entry.ItemId.HasValue) return ResponseDtoHelper.BadRequest<StockCountDto>("کالای ردیف جدید را انتخاب کنید.");
                    if (!await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == entry.ItemId.Value && !i.Deleted, cancellationToken))
                        return ResponseDtoHelper.BadRequest<StockCountDto>(InventoryMessages.ItemNotFound);
                    if (entry.LocationId.HasValue && !await _uow.Set<WarehouseLocation>().AnyAsync(l => l.Id == entry.LocationId.Value && l.WarehouseId == count.WarehouseId, cancellationToken))
                        return ResponseDtoHelper.BadRequest<StockCountDto>("محل انتخاب‌شده متعلق به این انبار نیست.");
                    if (entry.LotId.HasValue && !await _uow.Set<InventoryLot>().AnyAsync(l => l.Id == entry.LotId.Value && l.ItemId == entry.ItemId.Value, cancellationToken))
                        return ResponseDtoHelper.BadRequest<StockCountDto>("بچ انتخاب‌شده متعلق به این کالا نیست.");
                    line = count.Lines.FirstOrDefault(l => l.ItemId == entry.ItemId && l.LocationId == entry.LocationId && l.LotId == entry.LotId);
                    if (line == null)
                    {
                        line = new StockCountLine { ItemId = entry.ItemId.Value, LocationId = entry.LocationId, LotId = entry.LotId, SystemQuantity = 0 };
                        count.Lines.Add(line);
                    }
                }
                line.CountedQuantity = entry.CountedQuantity;
                line.RecountedQuantity = entry.RecountedQuantity;
                line.Note = InventoryText.Clean(entry.Note);
                line.CountedByUserId = _user.UserId;
            }
            count.ModifyDate = DateTimeOffset.Now;
            count.ModifierUserId = _user.UserId;
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ResponseDtoHelper.BadRequest<StockCountDto>(InventoryMessages.ConcurrencyConflict);
            }
            return await GetCountAsync(count.Id, cancellationToken);
        }

        public async Task<ResponseDto<StockCountDto>> CountActionAsync(StockCountActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<StockCountDto>(InventoryMessages.InvalidData);
            StockCount count = await Counts.Include(c => c.Lines).ThenInclude(l => l.Item).FirstOrDefaultAsync(c => c.Id == dto.Id && !c.Deleted, cancellationToken);
            if (count == null) return ResponseDtoHelper.NotFound<StockCountDto>("شمارش پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            int me = _user.UserId;
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            switch (action)
            {
                case "submit":
                    if (count.Status != StockCountStatus.Counting) return ResponseDtoHelper.BadRequest<StockCountDto>("شمارش قبلاً ارسال شده است.");
                    if (count.Lines.Any(l => !l.CountedQuantity.HasValue)) return ResponseDtoHelper.BadRequest<StockCountDto>("همه ردیف‌ها باید شمرده شوند (برای کالای پیدا نشده صفر بزنید).");
                    count.Status = StockCountStatus.Submitted;
                    count.SubmittedAt = now;
                    break;
                case "approve-warehouse":
                case "approve-planning":
                {
                    if (count.Status != StockCountStatus.Submitted) return ResponseDtoHelper.BadRequest<StockCountDto>("فقط شمارش ارسال‌شده تایید می‌شود.");
                    bool warehouse = action == "approve-warehouse";
                    if (!_user.IsInRole(warehouse ? InventoryRoles.RoleWarehouseSupervisor : InventoryRoles.RolePlanning))
                        return ResponseDtoHelper.BadRequest<StockCountDto>(InventoryMessages.NotAllowed + (warehouse ? " (سرپرست انبار)" : " (برنامه‌ریزی و کنترل پروژه)"));
                    int? other = warehouse ? count.PlanningApprovedByUserId : count.WarehouseApprovedByUserId;
                    if (me == count.CreatorUserId || me == other) return ResponseDtoHelper.BadRequest<StockCountDto>(InventoryMessages.SameUser);
                    if (warehouse) { count.WarehouseApprovedAt = now; count.WarehouseApprovedByUserId = me; }
                    else { count.PlanningApprovedAt = now; count.PlanningApprovedByUserId = me; }
                    if (count.WarehouseApprovedAt.HasValue && count.PlanningApprovedAt.HasValue) count.Status = StockCountStatus.Approved;
                    break;
                }
                case "post":
                    return await PostAsync(count, cancellationToken);
                case "cancel":
                    if (count.Status == StockCountStatus.Posted || count.Status == StockCountStatus.Cancelled)
                        return ResponseDtoHelper.BadRequest<StockCountDto>("این شمارش قبلاً ثبت یا ابطال شده است.");
                    count.Status = StockCountStatus.Cancelled;
                    break;
                default:
                    return ResponseDtoHelper.BadRequest<StockCountDto>("عملیات نامعتبر است.");
            }
            count.ModifyDate = now;
            count.ModifierUserId = me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetCountAsync(count.Id, cancellationToken);
        }

        /// <summary>Approved differences become one increase and one decrease adjustment document, confirmed at once.</summary>
        private async Task<ResponseDto<StockCountDto>> PostAsync(StockCount count, CancellationToken cancellationToken)
        {
            if (count.Status != StockCountStatus.Approved) return ResponseDtoHelper.BadRequest<StockCountDto>("شمارش پیش از ثبت باید تایید انبار و برنامه‌ریزی را داشته باشد.");
            var differences = count.Lines.Select(l => new { Line = l, Diff = Difference(l) ?? 0 }).Where(x => x.Diff != 0).ToList();
            var serial = differences.FirstOrDefault(x => x.Line.Item.TrackingMode == ItemTrackingMode.Serial);
            if (serial != null)
                return ResponseDtoHelper.BadRequest<StockCountDto>($"کالای سریالی {serial.Line.Item.Code} اختلاف دارد؛ ★ اختلاف کالای سریالی با سند تعدیل جداگانه و ذکر شماره سریال اصلاح می‌شود. مقدار شمارش آن را پس از اصلاح برابر کنید.");

            DateTimeOffset now = DateTimeOffset.Now;
            var links = new SystemDocumentLinks { StockCountId = count.Id };
            string description = $"اختلاف شمارش {count.Number}";

            var decreases = differences.Where(x => x.Diff < 0).ToList();
            if (decreases.Count > 0 && !count.DecreaseDocumentId.HasValue)
            {
                ResponseDto<DocumentDto> doc = await _documents.CreateSystemDocumentAsync(new SaveDocumentDto
                {
                    Type = InventoryDocumentType.AdjustmentDecrease, DocumentDate = count.CountDate, WarehouseId = count.WarehouseId,
                    ReferenceNumber = count.Number, Description = description,
                    Lines = decreases.Select(x => new SaveDocumentLineDto { ItemId = x.Line.ItemId, Quantity = -x.Diff, LotId = x.Line.LotId, LocationId = x.Line.LocationId, Description = x.Line.Note }).ToList(),
                }, links, true, cancellationToken);
                if (!doc.Success) return await PostFailedAsync(count, doc.Message, cancellationToken);
                count.DecreaseDocumentId = doc.Data.Id;
            }
            var increases = differences.Where(x => x.Diff > 0).ToList();
            if (increases.Count > 0 && !count.IncreaseDocumentId.HasValue)
            {
                ResponseDto<DocumentDto> doc = await _documents.CreateSystemDocumentAsync(new SaveDocumentDto
                {
                    Type = InventoryDocumentType.AdjustmentIncrease, DocumentDate = count.CountDate, WarehouseId = count.WarehouseId,
                    ReferenceNumber = count.Number, Description = description,
                    Lines = increases.Select(x => new SaveDocumentLineDto { ItemId = x.Line.ItemId, Quantity = x.Diff, LotId = x.Line.LotId, LocationId = x.Line.LocationId, Description = x.Line.Note }).ToList(),
                }, links, true, cancellationToken);
                if (!doc.Success) return await PostFailedAsync(count, doc.Message, cancellationToken);
                count.IncreaseDocumentId = doc.Data.Id;
            }

            count.Status = StockCountStatus.Posted;
            count.PostedAt = now;
            count.PostedByUserId = _user.UserId;
            count.ModifyDate = now;
            count.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetCountAsync(count.Id, cancellationToken);
        }

        private async Task<ResponseDto<StockCountDto>> PostFailedAsync(StockCount count, string message, CancellationToken cancellationToken)
        {
            List<int> drafts = await _uow.Set<InventoryDocument>()
                .Where(d => d.StockCountId == count.Id && d.Status == InventoryDocumentStatus.Draft && !d.Deleted).Select(d => d.Id).ToListAsync(cancellationToken);
            foreach (int id in drafts) await _documents.DeleteDraftAsync(id, cancellationToken);
            if (count.DecreaseDocumentId.HasValue) await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.BadRequest<StockCountDto>("ثبت اختلاف انجام نشد: " + message);
        }
    }
}
