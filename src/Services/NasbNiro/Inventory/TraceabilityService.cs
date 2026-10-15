using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Inventory
{
    public interface ITraceabilityService
    {
        Task<ResponseDto<TraceSearchResultDto>> SearchAsync(string text, CancellationToken cancellationToken);
        Task<ResponseDto<SerialTraceDto>> GetSerialTraceAsync(int serialUnitId, CancellationToken cancellationToken);
        Task<ResponseDto<LotTraceDto>> GetLotTraceAsync(int lotId, CancellationToken cancellationToken);
        Task<ResponseDto<SerialUnitDto>> CreateSerialAsync(CreateSerialDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<SerialUnitDto>> UpdateSerialAsync(UpdateSerialDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<OperationTemplateDto>>> GetTemplatesAsync(OperationTemplateQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<OperationTemplateDto>> SaveTemplateAsync(SaveOperationTemplateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteTemplateAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<List<PartOperationDto>>> ApplyRoutingAsync(ApplyRoutingDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PartOperationDto>> AddOperationAsync(AddPartOperationDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PartOperationDto>> UpdateOperationAsync(UpdatePartOperationDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PartOperationDto>> QcOperationAsync(QcPartOperationDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PartOperationDto>> AddOperationFileAsync(AddOperationFileDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PartOperationDto>>> GetOperationsAsync(PartOperationQuery query, CancellationToken cancellationToken);

        Task<ResponseDto<AssemblyLinkDto>> InstallAsync(InstallPartDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<AssemblyLinkDto>> RemoveAsync(RemovePartDto dto, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Part traceability: serial units and lots, manufacturing stages with QC, assembly genealogy
    /// (which piece is inside which assembly) and the complete history of a piece or a lot.
    /// </summary>
    public class TraceabilityService : ITraceabilityService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public TraceabilityService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private DbSet<SerialUnit> Units => _uow.Set<SerialUnit>();
        private DbSet<PartOperation> Operations => _uow.Set<PartOperation>();

        // -------------------------------------------------------------------------------- search

        public async Task<ResponseDto<TraceSearchResultDto>> SearchAsync(string text, CancellationToken cancellationToken)
        {
            text = InventoryText.Clean(text);
            if (text == null) return ResponseDtoHelper.BadRequest<TraceSearchResultDto>("عبارت جستجو را وارد کنید.");

            // labels printed by PMIS: I/<item>, S/<item>/<serial>, L/<item>/<lot>, LOC/<warehouse>/<location>
            string[] parts = text.Split('/');
            string kind = parts.Length >= 2 ? parts[0].ToUpperInvariant() : null;
            var result = new TraceSearchResultDto();

            if (kind == "S" && parts.Length >= 3)
            {
                string itemCode = parts[1], serial = string.Join("/", parts.Skip(2));
                result.Serials = await InventoryStockService.SerialProjection(
                    Units.Where(u => !u.Deleted && u.Item.Code == itemCode && u.SerialNumber == serial)).ToListAsync(cancellationToken);
                return ResponseDtoHelper.Ok(result);
            }
            if (kind == "L" && parts.Length >= 3)
            {
                string itemCode = parts[1], lot = string.Join("/", parts.Skip(2));
                result.Lots = await InventoryStockService.LotProjection(
                    _uow.Set<InventoryLot>().Where(l => !l.Deleted && l.Item.Code == itemCode && l.LotNumber == lot), _uow.Set<StockBalance>())
                    .ToListAsync(cancellationToken);
                return ResponseDtoHelper.Ok(result);
            }
            if (kind == "LOC" && parts.Length >= 3)
            {
                string warehouseCode = parts[1], locationCode = string.Join("/", parts.Skip(2));
                result.Locations = await _uow.Set<WarehouseLocation>()
                    .Where(l => !l.Deleted && l.Warehouse.Code == warehouseCode && l.Code == locationCode)
                    .Select(l => new LocationDto { Id = l.Id, WarehouseId = l.WarehouseId, WarehouseCode = l.Warehouse.Code, Code = l.Code, Title = l.Title, IsActive = l.IsActive })
                    .ToListAsync(cancellationToken);
                return ResponseDtoHelper.Ok(result);
            }
            if (kind == "I")
            {
                text = string.Join("/", parts.Skip(1));
            }

            result.Items = await _uow.Set<InventoryItem>()
                .Where(i => !i.Deleted && (i.Code == text || i.Code.Contains(text) || i.Title.Contains(text) || i.TechnicalCode.Contains(text)))
                .OrderBy(i => i.Code.Length).ThenBy(i => i.Code).Take(20)
                .Select(i => new InventoryItemDto { Id = i.Id, Code = i.Code, Title = i.Title, Unit = i.Unit, TrackingMode = i.TrackingMode })
                .ToListAsync(cancellationToken);
            result.Serials = await InventoryStockService.SerialProjection(
                    Units.Where(u => !u.Deleted && u.SerialNumber.Contains(text)).OrderBy(u => u.SerialNumber.Length).Take(30))
                .ToListAsync(cancellationToken);
            result.Lots = await InventoryStockService.LotProjection(
                    _uow.Set<InventoryLot>().Where(l => !l.Deleted && (l.LotNumber.Contains(text) || l.HeatNumber.Contains(text) || l.CertificateNumber.Contains(text)))
                        .OrderBy(l => l.LotNumber.Length).Take(30),
                    _uow.Set<StockBalance>())
                .ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(result);
        }

        // ---------------------------------------------------------------------------- projections

        private IQueryable<PartOperationDto> OperationProjection(IQueryable<PartOperation> operations) =>
            operations.Select(o => new PartOperationDto
            {
                Id = o.Id,
                SerialUnitId = o.SerialUnitId,
                SerialNumber = o.SerialUnit.SerialNumber,
                LotId = o.LotId,
                LotNumber = o.Lot.LotNumber,
                ItemCode = o.SerialUnit != null ? o.SerialUnit.Item.Code : o.Lot.Item.Code,
                ItemTitle = o.SerialUnit != null ? o.SerialUnit.Item.Title : o.Lot.Item.Title,
                OperationTemplateId = o.OperationTemplateId,
                Sequence = o.Sequence,
                Title = o.Title,
                Status = o.Status,
                StartedAt = o.StartedAt,
                FinishedAt = o.FinishedAt,
                OperatorName = o.OperatorName,
                MachineId = o.MachineId,
                MachineTitle = o.Machine.Title,
                WorkOrderId = o.WorkOrderId,
                WorkOrderCode = o.WorkOrder.Code,
                Result = o.Result,
                QcStatus = o.QcStatus,
                QcByName = o.QcByUserId.HasValue ? "#" : null,
                QcAt = o.QcAt,
                QcNote = o.QcNote,
                Notes = o.Notes,
                Files = o.Files.OrderBy(f => f.Id).Select(f => new PartOperationFileDto
                {
                    Id = f.Id, DocumentId = f.DocumentId, Title = f.Title, CreatedAt = f.CreatedAt,
                }).ToList(),
            });

        private async Task<List<PartOperationDto>> LoadOperationsAsync(IQueryable<PartOperation> source, CancellationToken cancellationToken)
        {
            var rows = await source.Select(o => new { o.Id, o.QcByUserId }).ToListAsync(cancellationToken);
            List<PartOperationDto> list = await OperationProjection(source).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => r.QcByUserId), cancellationToken);
            foreach (PartOperationDto dto in list)
                dto.QcByName = names.Name(rows.FirstOrDefault(r => r.Id == dto.Id)?.QcByUserId);
            return list;
        }

        private async Task<PartOperationDto> LoadOperationAsync(int id, CancellationToken cancellationToken) =>
            (await LoadOperationsAsync(Operations.Where(o => o.Id == id), cancellationToken)).FirstOrDefault();

        private static IQueryable<AssemblyLinkDto> LinkProjection(IQueryable<AssemblyLink> links) =>
            links.Select(a => new AssemblyLinkDto
            {
                Id = a.Id,
                ParentSerialUnitId = a.ParentSerialUnitId,
                ParentSerialNumber = a.ParentSerialUnit.SerialNumber,
                ParentItemCode = a.ParentSerialUnit.Item.Code,
                ParentItemTitle = a.ParentSerialUnit.Item.Title,
                ChildSerialUnitId = a.ChildSerialUnitId,
                ChildSerialNumber = a.ChildSerialUnit.SerialNumber,
                ChildLotId = a.ChildLotId,
                ChildLotNumber = a.ChildLot.LotNumber,
                ChildHeatNumber = a.ChildSerialUnit != null ? a.ChildSerialUnit.Lot.HeatNumber : a.ChildLot.HeatNumber,
                ChildItemCode = a.ChildSerialUnit != null ? a.ChildSerialUnit.Item.Code : a.ChildLot.Item.Code,
                ChildItemTitle = a.ChildSerialUnit != null ? a.ChildSerialUnit.Item.Title : a.ChildLot.Item.Title,
                Quantity = a.Quantity,
                InstalledAt = a.InstalledAt,
                InstalledByName = a.InstalledByUserId.ToString(),
                RemovedAt = a.RemovedAt,
                RemovedByName = a.RemovedByUserId.HasValue ? "#" : null,
                WorkOrderCode = a.WorkOrder.Code,
                Notes = a.Notes,
            });

        private async Task<List<AssemblyLinkDto>> LoadLinksAsync(IQueryable<AssemblyLink> source, CancellationToken cancellationToken)
        {
            var users = await source.Select(a => new { a.Id, a.InstalledByUserId, a.RemovedByUserId }).ToListAsync(cancellationToken);
            List<AssemblyLinkDto> list = await LinkProjection(source.OrderBy(a => a.InstalledAt)).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(
                _uow, users.SelectMany(u => new int?[] { u.InstalledByUserId, u.RemovedByUserId }), cancellationToken);
            foreach (AssemblyLinkDto dto in list)
            {
                var user = users.First(u => u.Id == dto.Id);
                dto.InstalledByName = names.Name(user.InstalledByUserId);
                dto.RemovedByName = names.Name(user.RemovedByUserId);
            }
            return list;
        }

        private IQueryable<MovementDto> MovementProjection(IQueryable<StockTransaction> transactions) =>
            transactions.OrderBy(t => t.TransactionDate).ThenBy(t => t.Id).Select(t => new MovementDto
            {
                Date = t.TransactionDate,
                DocumentId = t.DocumentId,
                DocumentNumber = t.Document.Number,
                DocumentType = t.Document.Type,
                WarehouseTitle = t.Warehouse.Title,
                LocationCode = t.Location.Code,
                SerialNumber = t.SerialUnit.SerialNumber,
                Quantity = t.Quantity,
                IsReversal = t.IsReversal,
                ProviderName = t.Document.Provider.Name,
                WorkOrderCode = t.Document.WorkOrder.Code,
            });

        // --------------------------------------------------------------------------------- traces

        public async Task<ResponseDto<SerialTraceDto>> GetSerialTraceAsync(int serialUnitId, CancellationToken cancellationToken)
        {
            SerialUnitDto unit = await InventoryStockService.SerialProjection(Units.Where(u => u.Id == serialUnitId && !u.Deleted)).FirstOrDefaultAsync(cancellationToken);
            if (unit == null) return ResponseDtoHelper.NotFound<SerialTraceDto>("قطعه سریالی پیدا نشد.");

            var trace = new SerialTraceDto { Unit = unit };
            if (unit.LotId.HasValue)
                trace.Lot = await InventoryStockService.LotProjection(_uow.Set<InventoryLot>().Where(l => l.Id == unit.LotId.Value), _uow.Set<StockBalance>())
                    .FirstOrDefaultAsync(cancellationToken);

            // up to the top assembly (for example the turbine)
            int? parentId = unit.ParentSerialUnitId;
            var visited = new HashSet<int> { unit.Id };
            while (parentId.HasValue && visited.Add(parentId.Value) && trace.Ancestors.Count < 30)
            {
                SerialUnitDto parent = await InventoryStockService.SerialProjection(Units.Where(u => u.Id == parentId.Value)).FirstOrDefaultAsync(cancellationToken);
                if (parent == null) break;
                trace.Ancestors.Add(parent);
                parentId = parent.ParentSerialUnitId;
            }

            trace.Children = await InventoryStockService.SerialProjection(
                Units.Where(u => u.ParentSerialUnitId == serialUnitId && !u.Deleted).OrderBy(u => u.Item.Code)).ToListAsync(cancellationToken);
            trace.LotComponents = await LoadLinksAsync(
                _uow.Set<AssemblyLink>().Where(a => a.ParentSerialUnitId == serialUnitId && a.ChildLotId.HasValue && a.RemovedAt == null), cancellationToken);
            trace.AssemblyHistory = await LoadLinksAsync(
                _uow.Set<AssemblyLink>().Where(a => a.ParentSerialUnitId == serialUnitId || a.ChildSerialUnitId == serialUnitId), cancellationToken);
            trace.Operations = await LoadOperationsAsync(
                Operations.Where(o => !o.Deleted && (o.SerialUnitId == serialUnitId || (unit.LotId.HasValue && o.LotId == unit.LotId)))
                    .OrderBy(o => o.Sequence).ThenBy(o => o.Id), cancellationToken);
            trace.Movements = await MovementProjection(_uow.Set<StockTransaction>().Where(t => t.SerialUnitId == serialUnitId)).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(trace);
        }

        public async Task<ResponseDto<LotTraceDto>> GetLotTraceAsync(int lotId, CancellationToken cancellationToken)
        {
            LotDto lot = await InventoryStockService.LotProjection(_uow.Set<InventoryLot>().Where(l => l.Id == lotId && !l.Deleted), _uow.Set<StockBalance>())
                .FirstOrDefaultAsync(cancellationToken);
            if (lot == null) return ResponseDtoHelper.NotFound<LotTraceDto>("بچ پیدا نشد.");

            var trace = new LotTraceDto { Lot = lot };
            trace.Balances = await _uow.Set<StockBalance>().Where(b => b.LotId == lotId && b.Quantity != 0)
                .Select(b => new StockBalanceDto
                {
                    ItemId = b.ItemId, ItemCode = b.Item.Code, ItemTitle = b.Item.Title, Unit = b.Item.Unit,
                    WarehouseId = b.WarehouseId, WarehouseTitle = b.Warehouse.Title, LocationId = b.LocationId, LocationCode = b.Location.Code,
                    LotId = b.LotId, LotNumber = b.Lot.LotNumber, HeatNumber = b.Lot.HeatNumber, Quantity = b.Quantity,
                }).ToListAsync(cancellationToken);
            trace.Movements = await MovementProjection(_uow.Set<StockTransaction>().Where(t => t.LotId == lotId)).ToListAsync(cancellationToken);
            trace.Serials = await InventoryStockService.SerialProjection(Units.Where(u => u.LotId == lotId && !u.Deleted).OrderBy(u => u.SerialNumber))
                .ToListAsync(cancellationToken);
            trace.UsedIn = await LoadLinksAsync(_uow.Set<AssemblyLink>().Where(a => a.ChildLotId == lotId), cancellationToken);
            trace.Operations = await LoadOperationsAsync(Operations.Where(o => !o.Deleted && o.LotId == lotId).OrderBy(o => o.Sequence), cancellationToken);

            // where-used: the top assemblies that contain a piece of this lot
            var starts = new List<int>();
            starts.AddRange(trace.Serials.Where(s => s.ParentSerialUnitId.HasValue).Select(s => s.ParentSerialUnitId.Value));
            starts.AddRange(trace.UsedIn.Where(u => u.RemovedAt == null).Select(u => u.ParentSerialUnitId));
            var tops = new HashSet<int>();
            foreach (int start in starts.Distinct())
                tops.Add(await TopAssemblyIdAsync(start, cancellationToken));
            if (tops.Count > 0)
                trace.TopAssemblies = await InventoryStockService.SerialProjection(Units.Where(u => tops.Contains(u.Id))).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(trace);
        }

        private async Task<int> TopAssemblyIdAsync(int serialUnitId, CancellationToken cancellationToken)
        {
            int current = serialUnitId;
            var visited = new HashSet<int>();
            while (visited.Add(current) && visited.Count < 50)
            {
                int? parent = await Units.Where(u => u.Id == current).Select(u => u.ParentSerialUnitId).FirstOrDefaultAsync(cancellationToken);
                if (!parent.HasValue) break;
                current = parent.Value;
            }
            return current;
        }

        // -------------------------------------------------------------------------------- serials

        public async Task<ResponseDto<SerialUnitDto>> CreateSerialAsync(CreateSerialDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<SerialUnitDto>(InventoryMessages.InvalidData);
            InventoryItem item = await _uow.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == dto.ItemId && !i.Deleted, cancellationToken);
            if (item == null) return ResponseDtoHelper.NotFound<SerialUnitDto>(InventoryMessages.ItemNotFound);
            if (item.TrackingMode != ItemTrackingMode.Serial) return ResponseDtoHelper.BadRequest<SerialUnitDto>($"کالای {item.Code} سریالی تعریف نشده است.");
            if (dto.LotId.HasValue && !await _uow.Set<InventoryLot>().AnyAsync(l => l.Id == dto.LotId.Value && l.ItemId == item.Id && !l.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<SerialUnitDto>("بچ متعلق به این کالا نیست.");
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && !w.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<SerialUnitDto>("دستور ساخت پیدا نشد.");

            DateTimeOffset now = DateTimeOffset.Now;
            string serialNumber = InventoryText.Clean(dto.SerialNumber);
            if (serialNumber == null)
            {
                // company label number YYDDDNNNN (the same counter as the warehouse labels)
                serialNumber = (await InventoryNumbers.NextLabelsAsync(_uow, DateTime.Now, 1, cancellationToken))[0];
            }
            if (await Units.AnyAsync(u => !u.Deleted && u.ItemId == item.Id && u.SerialNumber == serialNumber, cancellationToken))
                return ResponseDtoHelper.BadRequest<SerialUnitDto>($"سریال {serialNumber} برای این کالا قبلاً ثبت شده است.");

            var unit = new SerialUnit
            {
                ItemId = item.Id,
                SerialNumber = serialNumber,
                LotId = dto.LotId,
                Status = SerialUnitStatus.InProduction,
                WorkOrderId = dto.WorkOrderId,
                Description = InventoryText.Clean(dto.Description),
                CreateDate = now,
                CreatorUserId = _user.UserId,
                ModifyDate = now,
                ModifierUserId = _user.UserId,
            };
            Units.Add(unit);
            if (dto.ApplyRouting)
            {
                foreach (OperationTemplate template in await TemplatesForItemAsync(item, cancellationToken))
                    Operations.Add(NewOperation(template, unit, null, dto.WorkOrderId, now));
            }
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return ResponseDtoHelper.BadRequest<SerialUnitDto>("سریال تکراری است یا کاربر دیگری هم‌زمان سریال ساخت. دوباره تلاش کنید.");
            }
            return ResponseDtoHelper.Ok(await InventoryStockService.SerialProjection(Units.Where(u => u.Id == unit.Id)).FirstAsync(cancellationToken));
        }

        public async Task<ResponseDto<SerialUnitDto>> UpdateSerialAsync(UpdateSerialDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<SerialUnitDto>(InventoryMessages.InvalidData);
            SerialUnit unit = await Units.FirstOrDefaultAsync(u => u.Id == dto.Id && !u.Deleted, cancellationToken);
            if (unit == null) return ResponseDtoHelper.NotFound<SerialUnitDto>("قطعه سریالی پیدا نشد.");
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && !w.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<SerialUnitDto>("دستور ساخت پیدا نشد.");
            unit.WorkOrderId = dto.WorkOrderId;
            unit.Description = InventoryText.Clean(dto.Description);
            unit.ModifyDate = DateTimeOffset.Now;
            unit.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await InventoryStockService.SerialProjection(Units.Where(u => u.Id == unit.Id)).FirstAsync(cancellationToken));
        }

        // ------------------------------------------------------------------------------ templates

        private async Task<List<OperationTemplate>> TemplatesForItemAsync(InventoryItem item, CancellationToken cancellationToken)
        {
            DbSet<OperationTemplate> templates = _uow.Set<OperationTemplate>();
            List<OperationTemplate> own = await templates.Where(t => t.ItemId == item.Id && t.IsActive && !t.Deleted)
                .OrderBy(t => t.Sequence).ToListAsync(cancellationToken);
            if (own.Count > 0) return own;

            // otherwise the stages of the nearest group that has some
            var groups = await _uow.Set<ItemGroup>().Where(g => !g.Deleted).Select(g => new { g.Id, g.ParentId }).ToListAsync(cancellationToken);
            int? groupId = item.ItemGroupId;
            var visited = new HashSet<int>();
            while (groupId.HasValue && visited.Add(groupId.Value))
            {
                int current = groupId.Value;
                List<OperationTemplate> list = await templates.Where(t => t.ItemGroupId == current && t.ItemId == null && t.IsActive && !t.Deleted)
                    .OrderBy(t => t.Sequence).ToListAsync(cancellationToken);
                if (list.Count > 0) return list;
                groupId = groups.FirstOrDefault(g => g.Id == current)?.ParentId;
            }
            return new List<OperationTemplate>();
        }

        private PartOperation NewOperation(OperationTemplate template, SerialUnit unit, int? lotId, int? workOrderId, DateTimeOffset now) =>
            new PartOperation
            {
                SerialUnit = unit,
                LotId = unit == null ? lotId : null,
                OperationTemplateId = template.Id,
                Sequence = template.Sequence,
                Title = template.Title,
                MachineId = template.MachineId,
                WorkOrderId = workOrderId,
                Status = PartOperationStatus.Pending,
                QcStatus = template.RequiresQc ? QcStatus.Pending : QcStatus.NotRequired,
                CreateDate = now,
                CreatorUserId = _user.UserId,
                ModifyDate = now,
                ModifierUserId = _user.UserId,
            };

        public async Task<ResponseDto<List<OperationTemplateDto>>> GetTemplatesAsync(OperationTemplateQuery query, CancellationToken cancellationToken)
        {
            query ??= new OperationTemplateQuery();
            IQueryable<OperationTemplate> templates = _uow.Set<OperationTemplate>().Where(t => !t.Deleted);
            if (query.ItemId.HasValue) templates = templates.Where(t => t.ItemId == query.ItemId.Value);
            if (query.ItemGroupId.HasValue) templates = templates.Where(t => t.ItemGroupId == query.ItemGroupId.Value && t.ItemId == null);
            List<OperationTemplateDto> list = await templates
                .OrderBy(t => t.ItemGroup.Code).ThenBy(t => t.Item.Code).ThenBy(t => t.Sequence)
                .Select(t => new OperationTemplateDto
                {
                    Id = t.Id,
                    ItemGroupId = t.ItemGroupId,
                    ItemGroupTitle = t.ItemGroup.Title,
                    ItemId = t.ItemId,
                    ItemCode = t.Item.Code,
                    ItemTitle = t.Item.Title,
                    Sequence = t.Sequence,
                    Title = t.Title,
                    Description = t.Description,
                    RequiresQc = t.RequiresQc,
                    MachineId = t.MachineId,
                    MachineTitle = t.Machine.Title,
                    IsActive = t.IsActive,
                })
                .ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<OperationTemplateDto>> SaveTemplateAsync(SaveOperationTemplateDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<OperationTemplateDto>(InventoryMessages.InvalidData);
            string title = InventoryText.Clean(dto.Title);
            if (title == null) return ResponseDtoHelper.BadRequest<OperationTemplateDto>("نام مرحله را وارد کنید.");
            if (dto.ItemId.HasValue == dto.ItemGroupId.HasValue)
                return ResponseDtoHelper.BadRequest<OperationTemplateDto>("مرحله یا برای یک کالا تعریف می‌شود یا برای یک گروه کالا.");
            if (dto.ItemId.HasValue && !await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == dto.ItemId.Value && !i.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<OperationTemplateDto>(InventoryMessages.ItemNotFound);
            if (dto.ItemGroupId.HasValue && !await _uow.Set<ItemGroup>().AnyAsync(g => g.Id == dto.ItemGroupId.Value && !g.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<OperationTemplateDto>(InventoryMessages.GroupNotFound);
            if (dto.MachineId.HasValue && !await _uow.Set<Machine>().AnyAsync(m => m.Id == dto.MachineId.Value, cancellationToken))
                return ResponseDtoHelper.BadRequest<OperationTemplateDto>("دستگاه پیدا نشد.");

            DateTimeOffset now = DateTimeOffset.Now;
            OperationTemplate template;
            if (dto.Id.HasValue)
            {
                template = await _uow.Set<OperationTemplate>().FirstOrDefaultAsync(t => t.Id == dto.Id.Value && !t.Deleted, cancellationToken);
                if (template == null) return ResponseDtoHelper.NotFound<OperationTemplateDto>("مرحله پیدا نشد.");
            }
            else
            {
                template = new OperationTemplate { CreateDate = now, CreatorUserId = _user.UserId };
                _uow.Set<OperationTemplate>().Add(template);
            }
            template.ItemId = dto.ItemId;
            template.ItemGroupId = dto.ItemGroupId;
            template.Sequence = dto.Sequence;
            template.Title = title;
            template.Description = InventoryText.Clean(dto.Description);
            template.RequiresQc = dto.RequiresQc;
            template.MachineId = dto.MachineId;
            template.IsActive = dto.IsActive;
            template.ModifyDate = now;
            template.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);

            List<OperationTemplateDto> saved = (await GetTemplatesAsync(new OperationTemplateQuery { ItemId = template.ItemId, ItemGroupId = template.ItemGroupId }, cancellationToken)).Data;
            return ResponseDtoHelper.Ok(saved.First(t => t.Id == template.Id));
        }

        public async Task<ResponseDto<bool>> DeleteTemplateAsync(int id, CancellationToken cancellationToken)
        {
            OperationTemplate template = await _uow.Set<OperationTemplate>().FirstOrDefaultAsync(t => t.Id == id && !t.Deleted, cancellationToken);
            if (template == null) return ResponseDtoHelper.NotFound<bool>("مرحله پیدا نشد.");
            template.Delete(); // operations already created from it keep their own copy of the stage
            template.ModifyDate = DateTimeOffset.Now;
            template.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }

        // ----------------------------------------------------------------------------- operations

        public async Task<ResponseDto<List<PartOperationDto>>> ApplyRoutingAsync(ApplyRoutingDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || dto.SerialUnitId.HasValue == dto.LotId.HasValue)
                return ResponseDtoHelper.BadRequest<List<PartOperationDto>>("یک قطعه سریالی یا یک بچ را انتخاب کنید.");

            InventoryItem item;
            SerialUnit unit = null;
            if (dto.SerialUnitId.HasValue)
            {
                unit = await Units.Include(u => u.Item).FirstOrDefaultAsync(u => u.Id == dto.SerialUnitId.Value && !u.Deleted, cancellationToken);
                if (unit == null) return ResponseDtoHelper.NotFound<List<PartOperationDto>>("قطعه سریالی پیدا نشد.");
                item = unit.Item;
            }
            else
            {
                InventoryLot lot = await _uow.Set<InventoryLot>().Include(l => l.Item).FirstOrDefaultAsync(l => l.Id == dto.LotId.Value && !l.Deleted, cancellationToken);
                if (lot == null) return ResponseDtoHelper.NotFound<List<PartOperationDto>>("بچ پیدا نشد.");
                item = lot.Item;
            }

            List<OperationTemplate> templates = await TemplatesForItemAsync(item, cancellationToken);
            if (templates.Count == 0) return ResponseDtoHelper.BadRequest<List<PartOperationDto>>($"برای کالای {item.Code} یا گروه آن مرحله ساختی تعریف نشده است.");

            IQueryable<PartOperation> current = Operations.Where(o => !o.Deleted);
            if (unit != null)
            {
                int unitId = unit.Id;
                current = current.Where(o => o.SerialUnitId == unitId);
            }
            else
            {
                int lotId = dto.LotId.Value;
                current = current.Where(o => o.LotId == lotId);
            }
            List<int?> existing = await current.Select(o => o.OperationTemplateId).ToListAsync(cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (OperationTemplate template in templates.Where(t => !existing.Contains(t.Id)))
                Operations.Add(NewOperation(template, unit, dto.LotId, dto.WorkOrderId ?? unit?.WorkOrderId, now));
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper.Ok(await LoadOperationsAsync(current.OrderBy(o => o.Sequence).ThenBy(o => o.Id), cancellationToken));
        }

        public async Task<ResponseDto<PartOperationDto>> AddOperationAsync(AddPartOperationDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || dto.SerialUnitId.HasValue == dto.LotId.HasValue)
                return ResponseDtoHelper.BadRequest<PartOperationDto>("یک قطعه سریالی یا یک بچ را انتخاب کنید.");
            string title = InventoryText.Clean(dto.Title);
            if (title == null) return ResponseDtoHelper.BadRequest<PartOperationDto>("نام مرحله را وارد کنید.");
            if (dto.SerialUnitId.HasValue && !await Units.AnyAsync(u => u.Id == dto.SerialUnitId.Value && !u.Deleted, cancellationToken))
                return ResponseDtoHelper.NotFound<PartOperationDto>("قطعه سریالی پیدا نشد.");
            if (dto.LotId.HasValue && !await _uow.Set<InventoryLot>().AnyAsync(l => l.Id == dto.LotId.Value && !l.Deleted, cancellationToken))
                return ResponseDtoHelper.NotFound<PartOperationDto>("بچ پیدا نشد.");
            if (dto.MachineId.HasValue && !await _uow.Set<Machine>().AnyAsync(m => m.Id == dto.MachineId.Value, cancellationToken))
                return ResponseDtoHelper.BadRequest<PartOperationDto>("دستگاه پیدا نشد.");

            DateTimeOffset now = DateTimeOffset.Now;
            var operation = new PartOperation
            {
                SerialUnitId = dto.SerialUnitId,
                LotId = dto.LotId,
                Title = title,
                Sequence = dto.Sequence,
                MachineId = dto.MachineId,
                WorkOrderId = dto.WorkOrderId,
                Notes = InventoryText.Clean(dto.Notes),
                Status = PartOperationStatus.Pending,
                QcStatus = dto.RequiresQc ? QcStatus.Pending : QcStatus.NotRequired,
                CreateDate = now,
                CreatorUserId = _user.UserId,
                ModifyDate = now,
                ModifierUserId = _user.UserId,
            };
            Operations.Add(operation);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await LoadOperationAsync(operation.Id, cancellationToken));
        }

        public async Task<ResponseDto<PartOperationDto>> UpdateOperationAsync(UpdatePartOperationDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<PartOperationDto>(InventoryMessages.InvalidData);
            PartOperation operation = await Operations.FirstOrDefaultAsync(o => o.Id == dto.Id && !o.Deleted, cancellationToken);
            if (operation == null) return ResponseDtoHelper.NotFound<PartOperationDto>("مرحله پیدا نشد.");
            if (dto.MachineId.HasValue && !await _uow.Set<Machine>().AnyAsync(m => m.Id == dto.MachineId.Value, cancellationToken))
                return ResponseDtoHelper.BadRequest<PartOperationDto>("دستگاه پیدا نشد.");

            DateTimeOffset now = DateTimeOffset.Now;
            string action = (dto.Action ?? "edit").Trim().ToLowerInvariant();
            switch (action)
            {
                case "start":
                    if (operation.Status != PartOperationStatus.Pending) return ResponseDtoHelper.BadRequest<PartOperationDto>("این مرحله قبلاً شروع شده است.");
                    operation.Status = PartOperationStatus.InProgress;
                    operation.StartedAt = now;
                    operation.OperatorUserId = _user.UserId;
                    break;
                case "finish":
                    if (operation.Status == PartOperationStatus.Done) return ResponseDtoHelper.BadRequest<PartOperationDto>("این مرحله قبلاً تمام شده است.");
                    if (operation.Status == PartOperationStatus.Rejected) return ResponseDtoHelper.BadRequest<PartOperationDto>("این مرحله رد شده است؛ اول آن را دوباره باز کنید.");
                    operation.Status = PartOperationStatus.Done;
                    operation.StartedAt ??= now;
                    operation.FinishedAt = now;
                    operation.OperatorUserId ??= _user.UserId;
                    break;
                case "reopen":
                    if (operation.Status != PartOperationStatus.Done && operation.Status != PartOperationStatus.Rejected)
                        return ResponseDtoHelper.BadRequest<PartOperationDto>("فقط مرحله تمام‌شده یا ردشده دوباره باز می‌شود.");
                    operation.Status = PartOperationStatus.InProgress;
                    operation.FinishedAt = null;
                    if (operation.QcStatus != QcStatus.NotRequired)
                    {
                        operation.QcStatus = QcStatus.Pending;
                        operation.QcAt = null;
                        operation.QcByUserId = null;
                    }
                    break;
                case "edit":
                    break;
                default:
                    return ResponseDtoHelper.BadRequest<PartOperationDto>("عملیات نامعتبر است.");
            }

            if (dto.OperatorName != null) operation.OperatorName = InventoryText.Clean(dto.OperatorName);
            if (dto.MachineId.HasValue) operation.MachineId = dto.MachineId;
            if (dto.Result != null) operation.Result = InventoryText.Clean(dto.Result);
            if (dto.Notes != null) operation.Notes = InventoryText.Clean(dto.Notes);
            operation.ModifyDate = now;
            operation.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await LoadOperationAsync(operation.Id, cancellationToken));
        }

        public async Task<ResponseDto<PartOperationDto>> QcOperationAsync(QcPartOperationDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<PartOperationDto>(InventoryMessages.InvalidData);
            if (dto.Status != QcStatus.Accepted && dto.Status != QcStatus.Rejected && dto.Status != QcStatus.AcceptedWithConditions)
                return ResponseDtoHelper.BadRequest<PartOperationDto>("نتیجه QC باید تایید، رد یا تایید مشروط باشد.");
            PartOperation operation = await Operations.FirstOrDefaultAsync(o => o.Id == dto.Id && !o.Deleted, cancellationToken);
            if (operation == null) return ResponseDtoHelper.NotFound<PartOperationDto>("مرحله پیدا نشد.");
            if (operation.Status != PartOperationStatus.Done)
                return ResponseDtoHelper.BadRequest<PartOperationDto>("QC بعد از پایان مرحله ثبت می‌شود.");
            string note = InventoryText.Clean(dto.Note);
            if (dto.Status != QcStatus.Accepted && note == null)
                return ResponseDtoHelper.BadRequest<PartOperationDto>("برای رد یا تایید مشروط، توضیح لازم است.");

            DateTimeOffset now = DateTimeOffset.Now;
            operation.QcStatus = dto.Status;
            operation.QcNote = note;
            operation.QcAt = now;
            operation.QcByUserId = _user.UserId;
            if (dto.Status == QcStatus.Rejected) operation.Status = PartOperationStatus.Rejected;
            operation.ModifyDate = now;
            operation.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await LoadOperationAsync(operation.Id, cancellationToken));
        }

        public async Task<ResponseDto<PartOperationDto>> AddOperationFileAsync(AddOperationFileDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || dto.DocumentId == Guid.Empty) return ResponseDtoHelper.BadRequest<PartOperationDto>("فایل را انتخاب کنید.");
            if (!await Operations.AnyAsync(o => o.Id == dto.PartOperationId && !o.Deleted, cancellationToken))
                return ResponseDtoHelper.NotFound<PartOperationDto>("مرحله پیدا نشد.");
            if (!await _uow.Set<Entities.Model.Document>().AnyAsync(d => d.Id == dto.DocumentId, cancellationToken))
                return ResponseDtoHelper.BadRequest<PartOperationDto>("فایل بارگذاری‌شده پیدا نشد.");
            _uow.Set<PartOperationFile>().Add(new PartOperationFile
            {
                PartOperationId = dto.PartOperationId,
                DocumentId = dto.DocumentId,
                Title = InventoryText.Clean(dto.Title),
                CreatedAt = DateTimeOffset.Now,
                CreatedByUserId = _user.UserId,
            });
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await LoadOperationAsync(dto.PartOperationId, cancellationToken));
        }

        public async Task<ResponseDto<PagedList<PartOperationDto>>> GetOperationsAsync(PartOperationQuery query, CancellationToken cancellationToken)
        {
            query ??= new PartOperationQuery();
            IQueryable<PartOperation> operations = Operations.Where(o => !o.Deleted);
            if (query.QcStatus.HasValue) operations = operations.Where(o => o.QcStatus == query.QcStatus.Value);
            if (query.Status.HasValue) operations = operations.Where(o => o.Status == query.Status.Value);
            if (query.WorkOrderId.HasValue) operations = operations.Where(o => o.WorkOrderId == query.WorkOrderId.Value);
            string text = InventoryText.Clean(query.Contains);
            if (text != null)
                operations = operations.Where(o => o.Title.Contains(text) || o.SerialUnit.SerialNumber.Contains(text) || o.Lot.LotNumber.Contains(text) ||
                                                   o.SerialUnit.Item.Code.Contains(text) || o.Lot.Item.Code.Contains(text));
            PagedList<PartOperationDto> page = await PagedList<PartOperationDto>.CreateAsync(
                OperationProjection(operations.OrderByDescending(o => o.FinishedAt ?? o.StartedAt ?? o.CreateDate).ThenByDescending(o => o.Id)),
                query.PageNumber, query.PageSize, cancellationToken);
            var ids = page.Select(p => p.Id).ToList();
            var qcUsers = await Operations.Where(o => ids.Contains(o.Id)).Select(o => new { o.Id, o.QcByUserId }).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, qcUsers.Select(q => q.QcByUserId), cancellationToken);
            foreach (PartOperationDto dto in page) dto.QcByName = names.Name(qcUsers.FirstOrDefault(q => q.Id == dto.Id)?.QcByUserId);
            return ResponseDtoHelper.Ok(page);
        }

        // ------------------------------------------------------------------------------- assembly

        public async Task<ResponseDto<AssemblyLinkDto>> InstallAsync(InstallPartDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || dto.ChildSerialUnitId.HasValue == dto.ChildLotId.HasValue)
                return ResponseDtoHelper.BadRequest<AssemblyLinkDto>("یک قطعه سریالی یا یک بچ را برای نصب انتخاب کنید.");
            SerialUnit parent = await Units.Include(u => u.Item).FirstOrDefaultAsync(u => u.Id == dto.ParentSerialUnitId && !u.Deleted, cancellationToken);
            if (parent == null) return ResponseDtoHelper.NotFound<AssemblyLinkDto>("مجموعه (قطعه مادر) پیدا نشد.");
            if (parent.Status == SerialUnitStatus.Scrapped || parent.Status == SerialUnitStatus.ReturnedToSupplier || parent.Status == SerialUnitStatus.Delivered)
                return ResponseDtoHelper.BadRequest<AssemblyLinkDto>("روی این مجموعه دیگر مونتاژ ثبت نمی‌شود.");

            DateTimeOffset now = DateTimeOffset.Now;
            var link = new AssemblyLink
            {
                ParentSerialUnitId = parent.Id,
                InstalledAt = now,
                InstalledByUserId = _user.UserId,
                WorkOrderId = dto.WorkOrderId ?? parent.WorkOrderId,
                Notes = InventoryText.Clean(dto.Notes),
                CreateDate = now,
                CreatorUserId = _user.UserId,
                ModifyDate = now,
                ModifierUserId = _user.UserId,
            };

            if (dto.ChildSerialUnitId.HasValue)
            {
                SerialUnit child = await Units.FirstOrDefaultAsync(u => u.Id == dto.ChildSerialUnitId.Value && !u.Deleted, cancellationToken);
                if (child == null) return ResponseDtoHelper.NotFound<AssemblyLinkDto>("قطعه پیدا نشد.");
                if (child.Id == parent.Id) return ResponseDtoHelper.BadRequest<AssemblyLinkDto>("قطعه نمی‌تواند داخل خودش نصب شود.");
                if (child.Status == SerialUnitStatus.InStock)
                    return ResponseDtoHelper.BadRequest<AssemblyLinkDto>($"سریال {child.SerialNumber} هنوز در انبار است؛ اول با حواله به تولید تحویل شود.");
                if (child.Status != SerialUnitStatus.InProduction)
                    return ResponseDtoHelper.BadRequest<AssemblyLinkDto>($"سریال {child.SerialNumber} برای نصب آماده نیست (داخل مجموعه دیگری است یا خارج شده).");
                // no loops: the child must not be the parent's own assembly
                if (await TopAssemblyChainContainsAsync(parent.Id, child.Id, cancellationToken))
                    return ResponseDtoHelper.BadRequest<AssemblyLinkDto>("این نصب حلقه ایجاد می‌کند (مجموعه داخل قطعه خودش).");
                child.Status = SerialUnitStatus.Installed;
                child.ParentSerialUnitId = parent.Id;
                child.ModifyDate = now;
                child.ModifierUserId = _user.UserId;
                link.ChildSerialUnitId = child.Id;
                link.Quantity = 1;
            }
            else
            {
                InventoryLot lot = await _uow.Set<InventoryLot>().FirstOrDefaultAsync(l => l.Id == dto.ChildLotId.Value && !l.Deleted, cancellationToken);
                if (lot == null) return ResponseDtoHelper.NotFound<AssemblyLinkDto>("بچ پیدا نشد.");
                if (!dto.Quantity.HasValue || dto.Quantity.Value <= 0) return ResponseDtoHelper.BadRequest<AssemblyLinkDto>("مقدار مصرف‌شده از بچ را وارد کنید.");
                link.ChildLotId = lot.Id;
                link.Quantity = dto.Quantity.Value;
            }

            _uow.Set<AssemblyLink>().Add(link);
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ResponseDtoHelper.BadRequest<AssemblyLinkDto>(InventoryMessages.ConcurrencyConflict);
            }
            return ResponseDtoHelper.Ok((await LoadLinksAsync(_uow.Set<AssemblyLink>().Where(a => a.Id == link.Id), cancellationToken)).First());
        }

        private async Task<bool> TopAssemblyChainContainsAsync(int startId, int searchedId, CancellationToken cancellationToken)
        {
            int? current = startId;
            var visited = new HashSet<int>();
            while (current.HasValue && visited.Add(current.Value) && visited.Count < 50)
            {
                if (current.Value == searchedId) return true;
                int id = current.Value;
                current = await Units.Where(u => u.Id == id).Select(u => u.ParentSerialUnitId).FirstOrDefaultAsync(cancellationToken);
            }
            return false;
        }

        public async Task<ResponseDto<AssemblyLinkDto>> RemoveAsync(RemovePartDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<AssemblyLinkDto>(InventoryMessages.InvalidData);
            AssemblyLink link = await _uow.Set<AssemblyLink>().FirstOrDefaultAsync(a => a.Id == dto.AssemblyLinkId && !a.Deleted, cancellationToken);
            if (link == null) return ResponseDtoHelper.NotFound<AssemblyLinkDto>("رکورد مونتاژ پیدا نشد.");
            if (link.RemovedAt.HasValue) return ResponseDtoHelper.BadRequest<AssemblyLinkDto>("این قطعه قبلاً از مجموعه جدا شده است.");

            DateTimeOffset now = DateTimeOffset.Now;
            link.RemovedAt = now;
            link.RemovedByUserId = _user.UserId;
            string notes = InventoryText.Clean(dto.Notes);
            if (notes != null) link.Notes = string.IsNullOrEmpty(link.Notes) ? notes : link.Notes + " | " + notes;
            link.ModifyDate = now;
            link.ModifierUserId = _user.UserId;
            if (link.ChildSerialUnitId.HasValue)
            {
                SerialUnit child = await Units.FirstAsync(u => u.Id == link.ChildSerialUnitId.Value, cancellationToken);
                child.Status = SerialUnitStatus.InProduction;
                child.ParentSerialUnitId = null;
                child.ModifyDate = now;
                child.ModifierUserId = _user.UserId;
            }
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok((await LoadLinksAsync(_uow.Set<AssemblyLink>().Where(a => a.Id == link.Id), cancellationToken)).First());
        }
    }
}
