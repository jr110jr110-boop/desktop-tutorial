using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Inventory
{
    public interface IWarehouseRegisterService
    {
        Task<ResponseDto<PagedList<InvAssetDto>>> GetAssetsAsync(InvAssetQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<InvAssetDto>> SaveAssetAsync(SaveInvAssetDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<UnitConversionDto>>> GetConversionsAsync(int? itemId, CancellationToken cancellationToken);
        Task<ResponseDto<UnitConversionDto>> SaveConversionAsync(SaveUnitConversionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<CeilingDto>>> GetCeilingsAsync(int? itemId, int? workOrderId, CancellationToken cancellationToken);
        Task<ResponseDto<CeilingDto>> SaveCeilingAsync(SaveCeilingDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteCeilingAsync(int id, CancellationToken cancellationToken);
    }

    /// <summary>PB5 (items 13 and 9): the light asset / tool register, the units-of-measure conversions used by the material balance, and the consumption ceilings.</summary>
    public class WarehouseRegisterService : IWarehouseRegisterService
    {
        public const string AssetRoles = InventoryRoles.Documents;
        public const string RulesRoles = InventoryRoles.Planning;
        public const string LabelPrefix = "A/";

        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public WarehouseRegisterService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        // ------------------------------------------------------------------------------------------ assets

        public static string NormalizeTag(string tag) => string.IsNullOrWhiteSpace(tag) ? null : tag.Trim().ToUpperInvariant().Replace(" ", "");

        private IQueryable<InvAssetDto> AssetProjection(IQueryable<InventoryAsset> assets) =>
            assets.Select(a => new InvAssetDto
            {
                Id = a.Id, Tag = a.Tag, Title = a.Title, ItemId = a.ItemId, ItemCode = a.Item.Code, SerialUnitId = a.SerialUnitId, SerialNumber = a.SerialUnit.SerialNumber,
                WarehouseId = a.WarehouseId, WarehouseTitle = a.Warehouse.Title, LocationId = a.LocationId, LocationCode = a.Location.Code, Status = a.Status, PurchaseDate = a.PurchaseDate,
                Cost = a.Cost, CustodianUnit = a.CustodianUnit, Description = a.Description, LabelCode = LabelPrefix + a.Tag,
            });

        public async Task<ResponseDto<PagedList<InvAssetDto>>> GetAssetsAsync(InvAssetQuery query, CancellationToken ct)
        {
            query ??= new InvAssetQuery();
            IQueryable<InventoryAsset> assets = _uow.Set<InventoryAsset>().AsNoTracking().Where(a => !a.Deleted);
            if (query.Status.HasValue) assets = assets.Where(a => a.Status == query.Status.Value);
            if (query.WarehouseId.HasValue) assets = assets.Where(a => a.WarehouseId == query.WarehouseId.Value);
            string text = InventoryText.Clean(query.Contains);
            if (text != null) assets = assets.Where(a => a.Tag.Contains(text) || a.Title.Contains(text) || a.Item.Code.Contains(text) || a.SerialUnit.SerialNumber.Contains(text));
            PagedList<InvAssetDto> page = await PagedList<InvAssetDto>.CreateAsync(AssetProjection(assets.OrderBy(a => a.Tag)), query.PageNumber, query.PageSize, ct);
            if (!InventoryPriceAccess.SeesValues(_user)) foreach (InvAssetDto a in page) a.Cost = null;
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<InvAssetDto>> SaveAssetAsync(SaveInvAssetDto dto, CancellationToken ct)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<InvAssetDto>(InventoryMessages.InvalidData);
            if (!_user.IsInRole(AssetRoles)) return ResponseDtoHelper.BadRequest<InvAssetDto>(InventoryMessages.NotAllowed);
            string tag = NormalizeTag(dto.Tag);
            string title = InventoryText.Clean(dto.Title);
            if (tag == null) return ResponseDtoHelper.BadRequest<InvAssetDto>("برچسب دارایی را وارد کنید.");
            if (tag.Length > 60) return ResponseDtoHelper.BadRequest<InvAssetDto>("برچسب دارایی بلند است.");
            if (title == null) return ResponseDtoHelper.BadRequest<InvAssetDto>(InventoryMessages.TitleRequired);
            if (!Enum.IsDefined(typeof(AssetStatus), dto.Status)) return ResponseDtoHelper.BadRequest<InvAssetDto>("وضعیت نامعتبر است.");
            if ((dto.Cost ?? 0) < 0) return ResponseDtoHelper.BadRequest<InvAssetDto>("بها منفی نمی‌شود.");
            if (await _uow.Set<InventoryAsset>().AnyAsync(a => !a.Deleted && a.Tag == tag && a.Id != (dto.Id ?? 0), ct)) return ResponseDtoHelper.BadRequest<InvAssetDto>("این برچسب قبلاً ثبت شده است: " + tag);
            if (dto.ItemId.HasValue && !await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == dto.ItemId.Value && !i.Deleted, ct)) return ResponseDtoHelper.BadRequest<InvAssetDto>(InventoryMessages.ItemNotFound);
            if (dto.SerialUnitId.HasValue && !await _uow.Set<SerialUnit>().AnyAsync(s => s.Id == dto.SerialUnitId.Value && !s.Deleted, ct)) return ResponseDtoHelper.BadRequest<InvAssetDto>("سریال پیدا نشد.");
            if (dto.WarehouseId.HasValue && !await _uow.Set<Warehouse>().AnyAsync(w => w.Id == dto.WarehouseId.Value && !w.Deleted, ct)) return ResponseDtoHelper.BadRequest<InvAssetDto>(InventoryMessages.WarehouseNotFound);
            if (dto.LocationId.HasValue && !await _uow.Set<WarehouseLocation>().AnyAsync(l => l.Id == dto.LocationId.Value && !l.Deleted && (dto.WarehouseId == null || l.WarehouseId == dto.WarehouseId.Value), ct))
                return ResponseDtoHelper.BadRequest<InvAssetDto>(InventoryMessages.LocationNotFound);

            InventoryAsset asset;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                asset = await _uow.Set<InventoryAsset>().FirstOrDefaultAsync(a => a.Id == dto.Id.Value && !a.Deleted, ct);
                if (asset == null) return ResponseDtoHelper.NotFound<InvAssetDto>(InventoryMessages.NotFound);
            }
            else
            {
                asset = new InventoryAsset { CreateDate = now, CreatorUserId = _user.UserId };
                _uow.Set<InventoryAsset>().Add(asset);
            }
            asset.Tag = tag;
            asset.Title = title;
            asset.ItemId = dto.ItemId;
            asset.SerialUnitId = dto.SerialUnitId;
            asset.WarehouseId = dto.WarehouseId;
            asset.LocationId = dto.LocationId;
            asset.Status = dto.Status;
            asset.PurchaseDate = dto.PurchaseDate?.Date;
            // M4: whoever does not see values neither sets nor wipes the cost (their screen shows it empty, so an edit would send nothing)
            if (InventoryPriceAccess.SeesValues(_user)) asset.Cost = dto.Cost.HasValue ? Math.Round(dto.Cost.Value, 0, MidpointRounding.AwayFromZero) : (decimal?)null;
            asset.CustodianUnit = InventoryText.Clean(dto.CustodianUnit);
            asset.Description = InventoryText.Clean(dto.Description);
            asset.ModifyDate = now;
            asset.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            InvAssetDto saved = await AssetProjection(_uow.Set<InventoryAsset>().Where(a => a.Id == asset.Id)).FirstAsync(ct);
            if (!InventoryPriceAccess.SeesValues(_user)) saved.Cost = null;
            return ResponseDtoHelper.Ok(saved);
        }

        // ------------------------------------------------------------------------------------------ unit conversions

        public async Task<ResponseDto<List<UnitConversionDto>>> GetConversionsAsync(int? itemId, CancellationToken ct)
        {
            IQueryable<ItemUnitConversion> q = _uow.Set<ItemUnitConversion>().AsNoTracking().Where(c => !c.Deleted);
            if (itemId.HasValue) q = q.Where(c => c.ItemId == itemId.Value || c.ItemId == null);
            var list = await q.OrderBy(c => c.Item.Code).ThenBy(c => c.FromUnit).Take(2000)
                .Select(c => new UnitConversionDto { Id = c.Id, ItemId = c.ItemId, ItemCode = c.Item.Code, FromUnit = c.FromUnit, ToUnit = c.ToUnit, Factor = c.Factor, IsActive = c.IsActive }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<UnitConversionDto>> SaveConversionAsync(SaveUnitConversionDto dto, CancellationToken ct)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<UnitConversionDto>(InventoryMessages.InvalidData);
            if (!_user.IsInRole(RulesRoles)) return ResponseDtoHelper.BadRequest<UnitConversionDto>(InventoryMessages.NotAllowed);
            string from = InventoryText.Clean(dto.FromUnit), to = InventoryText.Clean(dto.ToUnit);
            if (from == null || to == null) return ResponseDtoHelper.BadRequest<UnitConversionDto>("واحد مبدا و مقصد را وارد کنید.");
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return ResponseDtoHelper.BadRequest<UnitConversionDto>("واحد مبدا و مقصد یکی است.");
            if (dto.Factor <= 0 || dto.Factor > 1000000000m) return ResponseDtoHelper.BadRequest<UnitConversionDto>("ضریب تبدیل باید بیشتر از صفر باشد.");
            if (dto.ItemId.HasValue && !await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == dto.ItemId.Value && !i.Deleted, ct)) return ResponseDtoHelper.BadRequest<UnitConversionDto>(InventoryMessages.ItemNotFound);
            if (await _uow.Set<ItemUnitConversion>().AnyAsync(c => !c.Deleted && c.ItemId == dto.ItemId && c.FromUnit == from && c.ToUnit == to && c.Id != (dto.Id ?? 0), ct))
                return ResponseDtoHelper.BadRequest<UnitConversionDto>("این تبدیل قبلاً ثبت شده است.");
            // the reverse direction is implied (1/factor): two opposite rows would be able to disagree
            if (await _uow.Set<ItemUnitConversion>().AnyAsync(c => !c.Deleted && c.ItemId == dto.ItemId && c.FromUnit == to && c.ToUnit == from && c.Id != (dto.Id ?? 0), ct))
                return ResponseDtoHelper.BadRequest<UnitConversionDto>("تبدیل معکوس از قبل ثبت شده است؛ جهت برعکس خودکار حساب می‌شود.");

            ItemUnitConversion row;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<ItemUnitConversion>().FirstOrDefaultAsync(c => c.Id == dto.Id.Value && !c.Deleted, ct);
                if (row == null) return ResponseDtoHelper.NotFound<UnitConversionDto>(InventoryMessages.NotFound);
            }
            else
            {
                row = new ItemUnitConversion { CreateDate = now, CreatorUserId = _user.UserId };
                _uow.Set<ItemUnitConversion>().Add(row);
            }
            row.ItemId = dto.ItemId;
            row.FromUnit = from;
            row.ToUnit = to;
            row.Factor = dto.Factor;
            row.IsActive = dto.IsActive;
            row.ModifyDate = now;
            row.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await _uow.Set<ItemUnitConversion>().AsNoTracking().Where(c => c.Id == row.Id)
                .Select(c => new UnitConversionDto { Id = c.Id, ItemId = c.ItemId, ItemCode = c.Item.Code, FromUnit = c.FromUnit, ToUnit = c.ToUnit, Factor = c.Factor, IsActive = c.IsActive }).FirstAsync(ct));
        }

        // ------------------------------------------------------------------------------------------ ceilings

        public async Task<ResponseDto<List<CeilingDto>>> GetCeilingsAsync(int? itemId, int? workOrderId, CancellationToken ct)
        {
            IQueryable<InventoryConsumptionCeiling> q = _uow.Set<InventoryConsumptionCeiling>().AsNoTracking().Where(c => !c.Deleted);
            if (itemId.HasValue) q = q.Where(c => c.ItemId == itemId.Value);
            if (workOrderId.HasValue) q = q.Where(c => c.WorkOrderId == workOrderId.Value);
            var list = await q.OrderBy(c => c.Item.Code).Take(2000)
                .Select(c => new CeilingDto { Id = c.Id, ItemId = c.ItemId, ItemCode = c.Item.Code, ItemTitle = c.Item.Title, WorkOrderId = c.WorkOrderId, WorkOrderCode = c.WorkOrder.Code, MaxQuantity = c.MaxQuantity, Note = c.Note }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<CeilingDto>> SaveCeilingAsync(SaveCeilingDto dto, CancellationToken ct)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<CeilingDto>(InventoryMessages.InvalidData);
            if (!_user.IsInRole(RulesRoles)) return ResponseDtoHelper.BadRequest<CeilingDto>(InventoryMessages.NotAllowed);
            if (dto.MaxQuantity <= 0) return ResponseDtoHelper.BadRequest<CeilingDto>("سقف مصرف باید بیشتر از صفر باشد.");
            if (!await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == dto.ItemId && !i.Deleted, ct)) return ResponseDtoHelper.BadRequest<CeilingDto>(InventoryMessages.ItemNotFound);
            if (dto.WorkOrderId.HasValue && !await _uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && !w.Deleted, ct)) return ResponseDtoHelper.BadRequest<CeilingDto>("دستور کار پیدا نشد.");
            if (await _uow.Set<InventoryConsumptionCeiling>().AnyAsync(c => !c.Deleted && c.ItemId == dto.ItemId && c.WorkOrderId == dto.WorkOrderId && c.Id != (dto.Id ?? 0), ct))
                return ResponseDtoHelper.BadRequest<CeilingDto>("برای این کالا و پروژه قبلاً سقف ثبت شده است.");

            InventoryConsumptionCeiling row;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<InventoryConsumptionCeiling>().FirstOrDefaultAsync(c => c.Id == dto.Id.Value && !c.Deleted, ct);
                if (row == null) return ResponseDtoHelper.NotFound<CeilingDto>(InventoryMessages.NotFound);
            }
            else
            {
                row = new InventoryConsumptionCeiling { CreateDate = now, CreatorUserId = _user.UserId };
                _uow.Set<InventoryConsumptionCeiling>().Add(row);
            }
            row.ItemId = dto.ItemId;
            row.WorkOrderId = dto.WorkOrderId;
            row.MaxQuantity = dto.MaxQuantity;
            row.Note = InventoryText.Clean(dto.Note);
            row.ModifyDate = now;
            row.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await _uow.Set<InventoryConsumptionCeiling>().AsNoTracking().Where(c => c.Id == row.Id)
                .Select(c => new CeilingDto { Id = c.Id, ItemId = c.ItemId, ItemCode = c.Item.Code, ItemTitle = c.Item.Title, WorkOrderId = c.WorkOrderId, WorkOrderCode = c.WorkOrder.Code, MaxQuantity = c.MaxQuantity, Note = c.Note }).FirstAsync(ct));
        }

        public async Task<ResponseDto<bool>> DeleteCeilingAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(RulesRoles)) return ResponseDtoHelper.BadRequest<bool>(InventoryMessages.NotAllowed);
            InventoryConsumptionCeiling row = await _uow.Set<InventoryConsumptionCeiling>().FirstOrDefaultAsync(c => c.Id == id && !c.Deleted, ct);
            if (row == null) return ResponseDtoHelper.NotFound<bool>(InventoryMessages.NotFound);
            row.Delete(); // a setting, not a business record: removed from use (the row stays for the audit)
            row.ModifyDate = DateTimeOffset.Now;
            row.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(true);
        }
    }
}
