using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Inventory
{
    public interface IInventoryMasterService
    {
        Task<ResponseDto<List<ItemGroupDto>>> GetGroupsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<ItemGroupDto>> SaveGroupAsync(SaveItemGroupDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteGroupAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<InventoryItemDto>>> GetItemsAsync(InventoryItemQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<InventoryItemDto>> GetItemAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<InventoryItemDto>> GetItemByCodeAsync(string code, CancellationToken cancellationToken);
        Task<ResponseDto<InventoryItemDto>> SaveItemAsync(SaveInventoryItemDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<InventoryItemDto>> SaveItemQualityAsync(SaveItemQualityDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<ItemQualityChangeDto>>> GetQualityChangesAsync(ItemQualityChangeQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ItemQualityChangeDto>> DecideQualityChangeAsync(DecideItemQualityChangeDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteItemAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<ImportItemsResultDto>> ImportItemsAsync(List<ImportItemRowDto> rows, CancellationToken cancellationToken);

        Task<ResponseDto<List<WarehouseDto>>> GetWarehousesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<WarehouseDto>> SaveWarehouseAsync(SaveWarehouseDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<LocationDto>> SaveLocationAsync(SaveLocationDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteLocationAsync(int id, CancellationToken cancellationToken);
    }

    public class InventoryMasterService : IInventoryMasterService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public InventoryMasterService(IUnitOfWork uow, IInventoryUserContext user, global::Services.NasbNiro.Office.INotificationService notify)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private DbSet<ItemGroup> Groups => _uow.Set<ItemGroup>();
        private DbSet<InventoryItem> Items => _uow.Set<InventoryItem>();
        private DbSet<Warehouse> Warehouses => _uow.Set<Warehouse>();
        private DbSet<WarehouseLocation> Locations => _uow.Set<WarehouseLocation>();

        private void Stamp(Entities.Model.BaseEntity entity, bool isNew)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (isNew)
            {
                entity.CreateDate = now;
                entity.CreatorUserId = _user.UserId;
            }
            entity.ModifyDate = now;
            entity.ModifierUserId = _user.UserId;
        }

        // -------------------------------------------------------------------------------- groups

        public async Task<ResponseDto<List<ItemGroupDto>>> GetGroupsAsync(CancellationToken cancellationToken)
        {
            List<ItemGroupDto> groups = await Groups
                .Where(g => !g.Deleted)
                .OrderBy(g => g.Code)
                .Select(g => new ItemGroupDto
                {
                    Id = g.Id,
                    Code = g.Code,
                    Title = g.Title,
                    Description = g.Description,
                    ParentId = g.ParentId,
                    ParentTitle = g.Parent.Title,
                    ItemsCount = g.Items.Count(i => !i.Deleted),
                })
                .ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(groups);
        }

        public async Task<ResponseDto<ItemGroupDto>> SaveGroupAsync(SaveItemGroupDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<ItemGroupDto>(InventoryMessages.InvalidData);
            string code = InventoryText.Clean(dto.Code);
            string title = InventoryText.Clean(dto.Title);
            if (code == null) return ResponseDtoHelper.BadRequest<ItemGroupDto>(InventoryMessages.CodeRequired);
            if (title == null) return ResponseDtoHelper.BadRequest<ItemGroupDto>(InventoryMessages.TitleRequired);

            if (await Groups.AnyAsync(g => !g.Deleted && g.Code == code && g.Id != (dto.Id ?? 0), cancellationToken))
                return ResponseDtoHelper.BadRequest<ItemGroupDto>(InventoryMessages.DuplicateCode + code);

            ItemGroup group;
            if (dto.Id.HasValue)
            {
                group = await Groups.FirstOrDefaultAsync(g => g.Id == dto.Id.Value && !g.Deleted, cancellationToken);
                if (group == null) return ResponseDtoHelper.NotFound<ItemGroupDto>(InventoryMessages.GroupNotFound);
            }
            else
            {
                group = new ItemGroup();
                Groups.Add(group);
            }

            if (dto.ParentId.HasValue)
            {
                // The parent must exist and must not be the group itself or one of its sub-groups.
                List<(int Id, int? ParentId)> all = (await Groups.Where(g => !g.Deleted).Select(g => new { g.Id, g.ParentId }).ToListAsync(cancellationToken))
                    .Select(g => (g.Id, g.ParentId)).ToList();
                if (!all.Any(g => g.Id == dto.ParentId.Value))
                    return ResponseDtoHelper.BadRequest<ItemGroupDto>(InventoryMessages.GroupNotFound);
                if (dto.Id.HasValue)
                {
                    int? cursor = dto.ParentId;
                    for (int guard = 0; cursor.HasValue && guard < 100; guard++)
                    {
                        if (cursor.Value == dto.Id.Value)
                            return ResponseDtoHelper.BadRequest<ItemGroupDto>("گروه نمی‌تواند زیرمجموعه خودش باشد.");
                        cursor = all.FirstOrDefault(g => g.Id == cursor.Value).ParentId;
                    }
                }
            }

            group.Code = code;
            group.Title = title;
            group.Description = InventoryText.Clean(dto.Description);
            group.ParentId = dto.ParentId;
            Stamp(group, !dto.Id.HasValue);
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper.Ok(new ItemGroupDto
            {
                Id = group.Id, Code = group.Code, Title = group.Title, Description = group.Description, ParentId = group.ParentId,
            });
        }

        public async Task<ResponseDto<bool>> DeleteGroupAsync(int id, CancellationToken cancellationToken)
        {
            ItemGroup group = await Groups.FirstOrDefaultAsync(g => g.Id == id && !g.Deleted, cancellationToken);
            if (group == null) return ResponseDtoHelper.NotFound<bool>(InventoryMessages.GroupNotFound);
            if (await Groups.AnyAsync(g => g.ParentId == id && !g.Deleted, cancellationToken) ||
                await Items.AnyAsync(i => i.ItemGroupId == id && !i.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<bool>("این گروه زیرگروه یا کالا دارد و حذف نمی‌شود.");
            group.Delete();
            Stamp(group, false);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }

        // --------------------------------------------------------------------------------- items

        private IQueryable<InventoryItemDto> ItemProjection(IQueryable<InventoryItem> source)
        {
            IQueryable<StockBalance> balances = _uow.Set<StockBalance>();
            return source.Select(i => new InventoryItemDto
            {
                Id = i.Id,
                Code = i.Code,
                Title = i.Title,
                ItemGroupId = i.ItemGroupId,
                ItemGroupCode = i.ItemGroup.Code,
                ItemGroupTitle = i.ItemGroup.Title,
                Unit = i.Unit,
                TrackingMode = i.TrackingMode,
                TechnicalCode = i.TechnicalCode,
                Material = i.Material,
                Standard = i.Standard,
                Specification = i.Specification,
                Weight = i.Weight,
                MinStock = i.MinStock,
                MaxStock = i.MaxStock,
                IsActive = i.IsActive,
                SepidarCode = i.SepidarCode,
                GroupCode = i.GroupCode,
                StageCode = i.StageCode,
                BaseCode = i.BaseCode,
                MescCode = i.MescCode,
                LegacyCode = i.LegacyCode,
                RequiresIncomingInspection = i.RequiresIncomingInspection,
                ShelfLifeDays = i.ShelfLifeDays,
                RequiredCertificateType = i.RequiredCertificateType,
                PendingQualityChangeId = _uow.Set<ItemQualityChange>().Where(c => c.ItemId == i.Id && c.Status == ItemQualityChangeStatus.Pending && !c.Deleted)
                    .OrderByDescending(c => c.Id).Select(c => (int?)c.Id).FirstOrDefault(),
                OnHand = balances.Where(b => b.ItemId == i.Id).Sum(b => (decimal?)b.Quantity) ?? 0,
            });
        }

        public async Task<ResponseDto<PagedList<InventoryItemDto>>> GetItemsAsync(InventoryItemQuery query, CancellationToken cancellationToken)
        {
            query ??= new InventoryItemQuery();
            IQueryable<InventoryItem> items = Items.Where(i => !i.Deleted);
            string text = InventoryText.Clean(query.Contains);
            if (text != null)
                items = items.Where(i => i.Code.Contains(text) || i.Title.Contains(text) || i.TechnicalCode.Contains(text) ||
                                         i.LegacyCode.Contains(text) || i.MescCode.Contains(text));
            string groupCode = InventoryText.Clean(query.GroupCode);
            if (groupCode != null) items = items.Where(i => i.GroupCode == groupCode);
            string baseCode = InventoryText.Clean(query.BaseCode);
            if (baseCode != null) items = items.Where(i => i.BaseCode == baseCode);
            if (query.ItemGroupId.HasValue)
            {
                List<int> groupIds = await GroupWithChildrenAsync(query.ItemGroupId.Value, cancellationToken);
                items = items.Where(i => i.ItemGroupId.HasValue && groupIds.Contains(i.ItemGroupId.Value));
            }
            if (query.TrackingMode.HasValue) items = items.Where(i => i.TrackingMode == query.TrackingMode.Value);
            if (query.IsActive.HasValue) items = items.Where(i => i.IsActive == query.IsActive.Value);

            PagedList<InventoryItemDto> page = await PagedList<InventoryItemDto>.CreateAsync(
                ItemProjection(items.OrderBy(i => i.Code)), query.PageNumber, query.PageSize, cancellationToken);
            return ResponseDtoHelper.Ok(page);
        }

        private async Task<List<int>> GroupWithChildrenAsync(int groupId, CancellationToken cancellationToken)
        {
            var all = await Groups.Where(g => !g.Deleted).Select(g => new { g.Id, g.ParentId }).ToListAsync(cancellationToken);
            var result = new List<int> { groupId };
            for (int i = 0; i < result.Count && i < 10000; i++)
            {
                int current = result[i];
                result.AddRange(all.Where(g => g.ParentId == current && !result.Contains(g.Id)).Select(g => g.Id));
            }
            return result;
        }

        public async Task<ResponseDto<InventoryItemDto>> GetItemAsync(int id, CancellationToken cancellationToken)
        {
            InventoryItemDto item = await ItemProjection(Items.Where(i => i.Id == id && !i.Deleted)).FirstOrDefaultAsync(cancellationToken);
            return item == null
                ? ResponseDtoHelper.NotFound<InventoryItemDto>(InventoryMessages.ItemNotFound)
                : ResponseDtoHelper.Ok(item);
        }

        public async Task<ResponseDto<InventoryItemDto>> GetItemByCodeAsync(string code, CancellationToken cancellationToken)
        {
            code = InventoryText.Clean(code);
            if (code == null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(InventoryMessages.CodeRequired);
            InventoryItemDto item = await ItemProjection(Items.Where(i => i.Code == code && !i.Deleted)).FirstOrDefaultAsync(cancellationToken);
            return item == null
                ? ResponseDtoHelper.NotFound<InventoryItemDto>(InventoryMessages.ItemNotFound)
                : ResponseDtoHelper.Ok(item);
        }

        public async Task<ResponseDto<InventoryItemDto>> SaveItemAsync(SaveInventoryItemDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(InventoryMessages.InvalidData);
            string code = ItemCodeRules.Normalize(dto.Code);
            string title = InventoryText.Clean(dto.Title);
            string unit = InventoryText.Clean(dto.Unit);
            if (code == null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(InventoryMessages.CodeRequired);
            if (title == null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(InventoryMessages.TitleRequired);
            if (unit == null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(InventoryMessages.UnitRequired);
            if (!Enum.IsDefined(typeof(ItemTrackingMode), dto.TrackingMode))
                return ResponseDtoHelper.BadRequest<InventoryItemDto>("نوع ردیابی نامعتبر است.");
            if (dto.MinStock < 0 || dto.MaxStock < 0)
                return ResponseDtoHelper.BadRequest<InventoryItemDto>("حداقل و حداکثر موجودی نمی‌تواند منفی باشد.");
            if (dto.ItemGroupId.HasValue && !await Groups.AnyAsync(g => g.Id == dto.ItemGroupId.Value && !g.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<InventoryItemDto>(InventoryMessages.GroupNotFound);
            if (await Items.AnyAsync(i => !i.Deleted && i.Code == code && i.Id != (dto.Id ?? 0), cancellationToken))
                return ResponseDtoHelper.BadRequest<InventoryItemDto>(InventoryMessages.DuplicateCode + code);

            InventoryItem item = null;
            if (dto.Id.HasValue)
            {
                item = await Items.FirstOrDefaultAsync(i => i.Id == dto.Id.Value && !i.Deleted, cancellationToken);
                if (item == null) return ResponseDtoHelper.NotFound<InventoryItemDto>(InventoryMessages.ItemNotFound);
            }

            // WI-WH-002: a new code (or a changed one) must follow the company coding; planning (PMO) gives the codes
            bool codeChanged = item == null || !string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase);
            List<CodingValue> codingValues = await InventoryCodingData.LoadAsync(_uow, _user.UserId, cancellationToken);
            CodeCheckDto check = ItemCodeRules.Check(code, codingValues);
            if (codeChanged)
            {
                if (!_user.IsInRole(InventoryRoles.RolePlanning))
                    return ResponseDtoHelper.BadRequest<InventoryItemDto>("کد جدید را واحد برنامه‌ریزی و کنترل پروژه می‌دهد؛ از «درخواست کد کالا» استفاده کنید.");
                if (!check.IsValid) return ResponseDtoHelper.BadRequest<InventoryItemDto>("کد مطابق دستورالعمل WI-WH-002 نیست: " + string.Join(" | ", check.Errors));
                if (check.StageNotStockable) return ResponseDtoHelper.BadRequest<InventoryItemDto>(check.Warnings.First());
            }
            string mesc = check.MescCode ?? ItemCodeRules.Normalize(dto.MescCode);
            if (mesc != null && await Items.AnyAsync(i => !i.Deleted && i.MescCode == mesc && i.Id != (dto.Id ?? 0), cancellationToken))
                return ResponseDtoHelper.BadRequest<InventoryItemDto>("قطعه‌ای با همین کد MESC قبلاً ثبت شده است.");

            if (item != null)
            {
                if (item.TrackingMode != dto.TrackingMode &&
                    await _uow.Set<StockTransaction>().AnyAsync(t => t.ItemId == item.Id, cancellationToken))
                    return ResponseDtoHelper.BadRequest<InventoryItemDto>("این کالا گردش انبار دارد و نوع ردیابی آن قابل تغییر نیست.");
            }
            else
            {
                item = new InventoryItem();
                Items.Add(item);
            }

            // R1q D11 and review M8: the quality gates of an item are changed by QC, QA or PMO only, with a reason when a control is reduced, and a reduction always waits for
            // the second approval of QA
            bool isNewItem = !dto.Id.HasValue;
            int? newShelfLife = Quality.ItemQualityRules.NormalizeShelfLife(dto.ShelfLifeDays);
            QualityApplication quality = new QualityApplication { Apply = true };
            // a new item starts with the gate of its group (the coding request of a consumable is approved by planning without any quality decision)
            bool startInspection = Quality.ItemQualityRules.DefaultInspection(check.GroupCode);
            bool qualityChanged = isNewItem
                ? dto.RequiresIncomingInspection != startInspection || dto.RequiredCertificateType.HasValue || newShelfLife.HasValue
                : dto.RequiresIncomingInspection != item.RequiresIncomingInspection || dto.RequiredCertificateType != item.RequiredCertificateType || newShelfLife != item.ShelfLifeDays;
            if (qualityChanged)
            {
                quality = await ApplyQualityChangeAsync(isNewItem ? null : item, startInspection, dto.RequiresIncomingInspection, dto.RequiredCertificateType, newShelfLife, dto.QualityChangeReason, cancellationToken);
                if (quality.Error != null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(quality.Error);
            }

            item.Code = code;
            item.Title = title;
            item.ItemGroupId = dto.ItemGroupId;
            item.Unit = unit;
            item.TrackingMode = dto.TrackingMode;
            item.TechnicalCode = InventoryText.Clean(dto.TechnicalCode);
            item.Material = InventoryText.Clean(dto.Material);
            item.Standard = InventoryText.Clean(dto.Standard);
            item.Specification = InventoryText.Clean(dto.Specification);
            item.Weight = dto.Weight;
            item.MinStock = dto.MinStock;
            item.MaxStock = dto.MaxStock;
            item.IsActive = dto.IsActive;
            item.SepidarCode = InventoryText.Clean(dto.SepidarCode);
            item.GroupCode = check.GroupCode;
            item.StageCode = check.StageCode;
            item.BaseCode = check.BaseCode;
            item.MescCode = mesc;
            item.LegacyCode = InventoryText.Clean(dto.LegacyCode) ?? item.LegacyCode;
            if (quality.Apply)
            {
                item.RequiresIncomingInspection = dto.RequiresIncomingInspection;
                item.RequiredCertificateType = dto.RequiredCertificateType;
                item.ShelfLifeDays = newShelfLife;
            }
            else if (isNewItem)
            {
                // review M8: a new item whose requested gates are weaker than the baseline of its group is created with the baseline until QA approves the request
                item.RequiresIncomingInspection = startInspection;
                item.RequiredCertificateType = null;
                item.ShelfLifeDays = null;
            }
            Stamp(item, !dto.Id.HasValue);
            if (quality.Change != null) { quality.Change.Item = item; _uow.Set<ItemQualityChange>().Add(quality.Change); }
            await _uow.SaveChangesAsync(cancellationToken);
            await SyncQualityGateAsync(quality.Change, item, cancellationToken);

            return await GetItemAsync(item.Id, cancellationToken);
        }

        // -------------------------------------------------------------------------------- quality gates of an item (R1q D11)

        private sealed class QualityApplication
        {
            public string Error { get; set; }

            /// <summary>False: the new values wait for the second approval and the item keeps its gates for now.</summary>
            public bool Apply { get; set; }

            public ItemQualityChange Change { get; set; }
        }

        /// <summary>Decides how a change of the quality fields is handled; <paramref name="item"/> is null for a new item (its baseline is the start gate set of its group).</summary>
        private async Task<QualityApplication> ApplyQualityChangeAsync(InventoryItem item, bool startInspection, bool inspection, CertificateType? certificate, int? shelfLife, string reasonText, CancellationToken cancellationToken)
        {
            var result = new QualityApplication { Apply = true };
            if (!_user.IsInRole(InventoryRoles.ItemQuality))
            {
                result.Error = "ویژگی‌های کیفیت کالا (بازرسی ورودی، نوع گواهی الزامی، مدت ماندگاری) را فقط QC یا QA تغییر می‌دهد؛ این فیلدها را بدون تغییر بفرستید.";
                return result;
            }
            bool oldInspection = item?.RequiresIncomingInspection ?? startInspection;
            CertificateType? oldCertificate = item?.RequiredCertificateType;
            int? oldShelfLife = item?.ShelfLifeDays;
            bool weakening = Quality.ItemQualityRules.IsWeakening(oldInspection, oldCertificate, oldShelfLife, inspection, certificate, shelfLife);
            string reason = InventoryText.Clean(reasonText);
            if (weakening && reason == null)
            {
                result.Error = "برای کم‌کردن یک کنترل کیفی (حذف بازرسی ورودی، کاهش نوع گواهی یا حذف/افزایش ماندگاری) دلیل تغییر را بنویسید.";
                return result;
            }
            // review M8: reducing a control ALWAYS waits for the second approval of QA (an open purchase order or not), and only one such request waits per item;
            // a stricter control applies at once and is kept in the history of the item
            if (weakening && item != null && await _uow.Set<ItemQualityChange>().AnyAsync(c => c.ItemId == item.Id && c.Status == ItemQualityChangeStatus.Pending && !c.Deleted, cancellationToken))
            {
                result.Error = "برای این کالا درخواست دیگری برای کم‌کردن کنترل کیفی در انتظار تایید QA است؛ ابتدا درباره آن تصمیم گرفته شود.";
                return result;
            }
            DateTimeOffset now = DateTimeOffset.Now;
            result.Change = new ItemQualityChange
            {
                RequiresIncomingInspection = inspection, RequiredCertificateType = certificate, ShelfLifeDays = shelfLife, Reason = reason,
                OldRequiresIncomingInspection = oldInspection, OldRequiredCertificateType = oldCertificate, OldShelfLifeDays = oldShelfLife,
                Status = weakening ? ItemQualityChangeStatus.Pending : ItemQualityChangeStatus.Approved,
                DecidedAt = weakening ? (DateTimeOffset?)null : now, DecidedByUserId = weakening ? (int?)null : _user.UserId,
                DecisionNote = weakening ? null : "کنترل کیفی سخت‌تر یا بدون کاهش شد: بدون تایید دوم اعمال شد و در سابقه ثبت است.",
                ItemId = item?.Id ?? 0, CreateDate = now, CreatorUserId = _user.UserId, ModifyDate = now, ModifierUserId = _user.UserId,
            };
            result.Apply = !weakening;
            return result;
        }

        private async Task SyncQualityGateAsync(ItemQualityChange change, InventoryItem item, CancellationToken cancellationToken)
        {
            if (change == null) return;
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "itemquality:" + change.Id, change.Status == ItemQualityChangeStatus.Pending, "inventory", "item-quality", change.Id,
                "تایید کاهش کنترل کیفی کالای " + item.Code, "/inventory/items", "QA", cancellationToken);
        }

        public async Task<ResponseDto<InventoryItemDto>> SaveItemQualityAsync(SaveItemQualityDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(InventoryMessages.InvalidData);
            if (dto.RequiredCertificateType.HasValue && !Enum.IsDefined(typeof(CertificateType), dto.RequiredCertificateType.Value))
                return ResponseDtoHelper.BadRequest<InventoryItemDto>(InventoryMessages.InvalidData);
            InventoryItem item = await Items.FirstOrDefaultAsync(i => i.Id == dto.ItemId && !i.Deleted, cancellationToken);
            if (item == null) return ResponseDtoHelper.NotFound<InventoryItemDto>(InventoryMessages.ItemNotFound);
            int? shelfLife = Quality.ItemQualityRules.NormalizeShelfLife(dto.ShelfLifeDays);
            if (dto.RequiresIncomingInspection == item.RequiresIncomingInspection && dto.RequiredCertificateType == item.RequiredCertificateType && shelfLife == item.ShelfLifeDays)
                return ResponseDtoHelper.BadRequest<InventoryItemDto>("مقدار جدیدی برای ویژگی‌های کیفی کالا وارد نشده است.");
            QualityApplication quality = await ApplyQualityChangeAsync(item, item.RequiresIncomingInspection, dto.RequiresIncomingInspection, dto.RequiredCertificateType, shelfLife, dto.Reason, cancellationToken);
            if (quality.Error != null) return ResponseDtoHelper.BadRequest<InventoryItemDto>(quality.Error);
            if (quality.Apply)
            {
                item.RequiresIncomingInspection = dto.RequiresIncomingInspection;
                item.RequiredCertificateType = dto.RequiredCertificateType;
                item.ShelfLifeDays = shelfLife;
                Stamp(item, false);
            }
            if (quality.Change != null) { quality.Change.Item = item; _uow.Set<ItemQualityChange>().Add(quality.Change); }
            await _uow.SaveChangesAsync(cancellationToken);
            await SyncQualityGateAsync(quality.Change, item, cancellationToken);
            return await GetItemAsync(item.Id, cancellationToken);
        }

        private static IQueryable<ItemQualityChangeDto> QualityChangeRows(IQueryable<ItemQualityChange> rows) =>
            rows.OrderByDescending(c => c.Id).Select(c => new ItemQualityChangeDto
            {
                Id = c.Id, ItemId = c.ItemId, ItemCode = c.Item.Code, ItemTitle = c.Item.Title, RequiresIncomingInspection = c.RequiresIncomingInspection,
                RequiredCertificateType = c.RequiredCertificateType, ShelfLifeDays = c.ShelfLifeDays, CurrentRequiresIncomingInspection = c.Item.RequiresIncomingInspection,
                CurrentRequiredCertificateType = c.Item.RequiredCertificateType, CurrentShelfLifeDays = c.Item.ShelfLifeDays,
                OldRequiresIncomingInspection = c.OldRequiresIncomingInspection, OldRequiredCertificateType = c.OldRequiredCertificateType, OldShelfLifeDays = c.OldShelfLifeDays,
                Stale = c.Status == ItemQualityChangeStatus.Pending && (c.Item.RequiresIncomingInspection != c.OldRequiresIncomingInspection ||
                    c.Item.RequiredCertificateType != c.OldRequiredCertificateType || c.Item.ShelfLifeDays != c.OldShelfLifeDays),
                Reason = c.Reason, Status = c.Status, RequestedAt = c.CreateDate, DecidedAt = c.DecidedAt, DecisionNote = c.DecisionNote,
            });

        private async Task FillQualityChangeNamesAsync(IReadOnlyCollection<ItemQualityChangeDto> rows, CancellationToken cancellationToken)
        {
            List<int> ids = rows.Select(p => p.Id).ToList();
            var users = await _uow.Set<ItemQualityChange>().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.CreatorUserId, c.DecidedByUserId }).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, users.SelectMany(u => new int?[] { u.CreatorUserId, u.DecidedByUserId }), cancellationToken);
            foreach (ItemQualityChangeDto row in rows)
            {
                var u = users.First(x => x.Id == row.Id);
                row.RequestedByName = names.Name(u.CreatorUserId);
                row.DecidedByName = names.Name(u.DecidedByUserId);
            }
        }

        public async Task<ResponseDto<PagedList<ItemQualityChangeDto>>> GetQualityChangesAsync(ItemQualityChangeQuery query, CancellationToken cancellationToken)
        {
            query ??= new ItemQualityChangeQuery();
            IQueryable<ItemQualityChange> rows = _uow.Set<ItemQualityChange>().Where(c => !c.Deleted);
            if (query.Status.HasValue) rows = rows.Where(c => c.Status == query.Status.Value);
            if (query.ItemId.HasValue) rows = rows.Where(c => c.ItemId == query.ItemId.Value);
            PagedList<ItemQualityChangeDto> page = await PagedList<ItemQualityChangeDto>.CreateAsync(QualityChangeRows(rows), query.PageNumber, query.PageSize, cancellationToken);
            await FillQualityChangeNamesAsync(page, cancellationToken);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<ItemQualityChangeDto>> DecideQualityChangeAsync(DecideItemQualityChangeDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<ItemQualityChangeDto>(InventoryMessages.InvalidData);
            if (!_user.IsInRole("QA")) return ResponseDtoHelper.BadRequest<ItemQualityChangeDto>(InventoryMessages.NotAllowed + " (تایید دوم را مدیر QA می‌دهد)");
            ItemQualityChange change = await _uow.Set<ItemQualityChange>().Include(c => c.Item).FirstOrDefaultAsync(c => c.Id == dto.Id && !c.Deleted, cancellationToken);
            if (change == null) return ResponseDtoHelper.NotFound<ItemQualityChangeDto>(InventoryMessages.NotFound);
            if (change.Status != ItemQualityChangeStatus.Pending) return ResponseDtoHelper.BadRequest<ItemQualityChangeDto>("درباره این تغییر قبلاً تصمیم گرفته شده است.");
            if (change.CreatorUserId == _user.UserId) return ResponseDtoHelper.BadRequest<ItemQualityChangeDto>(InventoryMessages.SameUser);
            string note = InventoryText.Clean(dto.Note);
            if (!dto.Approve && note == null) return ResponseDtoHelper.BadRequest<ItemQualityChangeDto>("دلیل رد تغییر را بنویسید.");
            // review M8: the request was made against the gates the item had then; if somebody changed them since, the approval would apply a stale picture
            if (dto.Approve && (change.Item.RequiresIncomingInspection != change.OldRequiresIncomingInspection || change.Item.RequiredCertificateType != change.OldRequiredCertificateType ||
                change.Item.ShelfLifeDays != change.OldShelfLifeDays))
                return ResponseDtoHelper.BadRequest<ItemQualityChangeDto>("کنترل‌های کیفی کالا پس از ثبت این درخواست تغییر کرده‌اند و تایید آن مجاز نیست؛ درخواست را رد کنید و در صورت نیاز دوباره ثبت کنید.");
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Approve)
            {
                change.Item.RequiresIncomingInspection = change.RequiresIncomingInspection;
                change.Item.RequiredCertificateType = change.RequiredCertificateType;
                change.Item.ShelfLifeDays = change.ShelfLifeDays;
                change.Item.ModifyDate = now;
                change.Item.ModifierUserId = _user.UserId;
            }
            change.Status = dto.Approve ? ItemQualityChangeStatus.Approved : ItemQualityChangeStatus.Rejected;
            change.DecidedAt = now; change.DecidedByUserId = _user.UserId; change.DecisionNote = note;
            change.ModifyDate = now; change.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);
            await SyncQualityGateAsync(change, change.Item, cancellationToken);
            ItemQualityChangeDto decided = await QualityChangeRows(_uow.Set<ItemQualityChange>().AsNoTracking().Where(c => c.Id == change.Id)).FirstAsync(cancellationToken);
            await FillQualityChangeNamesAsync(new[] { decided }, cancellationToken);
            return ResponseDtoHelper.Ok(decided);
        }

        public async Task<ResponseDto<bool>> DeleteItemAsync(int id, CancellationToken cancellationToken)
        {
            InventoryItem item = await Items.FirstOrDefaultAsync(i => i.Id == id && !i.Deleted, cancellationToken);
            if (item == null) return ResponseDtoHelper.NotFound<bool>(InventoryMessages.ItemNotFound);
            if (await _uow.Set<StockTransaction>().AnyAsync(t => t.ItemId == id, cancellationToken) ||
                await _uow.Set<InventoryDocumentLine>().AnyAsync(l => l.ItemId == id, cancellationToken) ||
                await _uow.Set<SerialUnit>().AnyAsync(s => s.ItemId == id && !s.Deleted, cancellationToken))
                return ResponseDtoHelper.BadRequest<bool>("این کالا در اسناد یا ردیابی استفاده شده است. به‌جای حذف، آن را غیرفعال کنید.");
            item.Delete();
            Stamp(item, false);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }

        private static ItemTrackingMode? ParseTracking(string value)
        {
            string text = InventoryText.Clean(value)?.ToLowerInvariant();
            switch (text)
            {
                case null:
                case "0":
                case "none":
                case "بدون":
                case "بدون ردیابی":
                case "تعدادی":
                    return ItemTrackingMode.None;
                case "1":
                case "lot":
                case "batch":
                case "بچ":
                case "سری ساخت":
                case "شماره ذوب":
                    return ItemTrackingMode.Lot;
                case "2":
                case "serial":
                case "سریال":
                case "سریالی":
                    return ItemTrackingMode.Serial;
                default:
                    return null;
            }
        }

        public async Task<ResponseDto<ImportItemsResultDto>> ImportItemsAsync(List<ImportItemRowDto> rows, CancellationToken cancellationToken)
        {
            var result = new ImportItemsResultDto();
            if (rows == null || rows.Count == 0) return ResponseDtoHelper.BadRequest<ImportItemsResultDto>("فایل هیچ ردیفی ندارد.");
            if (rows.Count > 20000) return ResponseDtoHelper.BadRequest<ImportItemsResultDto>("حداکثر ۲۰٬۰۰۰ ردیف در هر بار.");

            Dictionary<string, ItemGroup> groups = (await Groups.Where(g => !g.Deleted).ToListAsync(cancellationToken))
                .GroupBy(g => g.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, InventoryItem> items = (await Items.Where(i => !i.Deleted).ToListAsync(cancellationToken))
                .GroupBy(i => i.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            HashSet<int> itemsWithMoves = (await _uow.Set<StockTransaction>().Select(t => t.ItemId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
            List<CodingValue> codingValues = await InventoryCodingData.LoadAsync(_uow, _user.UserId, cancellationToken);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < rows.Count; index++)
            {
                ImportItemRowDto row = rows[index];
                int rowNumber = index + 2; // row 1 of the sheet is the header
                string code = ItemCodeRules.Normalize(row?.Code);
                string title = InventoryText.Clean(row?.Title);
                if (code == null && title == null) continue; // empty row
                if (code == null) { result.Errors.Add($"ردیف {rowNumber}: کد خالی است."); continue; }
                if (!seen.Add(code)) { result.Errors.Add($"ردیف {rowNumber}: کد {code} در فایل تکراری است."); continue; }
                ItemTrackingMode? tracking = ParseTracking(row.TrackingMode);
                if (tracking == null) { result.Errors.Add($"ردیف {rowNumber}: نوع ردیابی «{row.TrackingMode}» شناخته نشد."); continue; }

                ItemGroup group = null;
                string groupCode = InventoryText.Clean(row.GroupCode);
                if (groupCode != null && !groups.TryGetValue(groupCode, out group))
                {
                    group = new ItemGroup { Code = groupCode, Title = InventoryText.Clean(row.GroupTitle) ?? groupCode };
                    Stamp(group, true);
                    Groups.Add(group);
                    groups[groupCode] = group;
                    result.GroupsCreated++;
                }

                bool isNew = !items.TryGetValue(code, out InventoryItem item);
                if (isNew)
                {
                    if (title == null) { result.Errors.Add($"ردیف {rowNumber}: شرح کالای {code} خالی است."); continue; }
                    item = new InventoryItem { Code = code, Unit = "عدد", IsActive = true, TrackingMode = tracking.Value };
                    Items.Add(item);
                    items[code] = item;
                    result.Created++;
                }
                else
                {
                    if (item.TrackingMode != tracking.Value && !string.IsNullOrWhiteSpace(row.TrackingMode))
                    {
                        if (itemsWithMoves.Contains(item.Id))
                            result.Errors.Add($"ردیف {rowNumber}: کالای {code} گردش دارد؛ نوع ردیابی آن عوض نشد.");
                        else
                            item.TrackingMode = tracking.Value;
                    }
                    result.Updated++;
                }

                if (title != null) item.Title = title;
                if (group != null) item.ItemGroup = group;
                item.Unit = InventoryText.Clean(row.Unit) ?? item.Unit ?? "عدد";
                item.TechnicalCode = InventoryText.Clean(row.TechnicalCode) ?? item.TechnicalCode;
                item.Material = InventoryText.Clean(row.Material) ?? item.Material;
                item.Standard = InventoryText.Clean(row.Standard) ?? item.Standard;
                item.Specification = InventoryText.Clean(row.Specification) ?? item.Specification;
                if (row.Weight.HasValue) item.Weight = row.Weight;
                if (row.MinStock.HasValue && row.MinStock.Value >= 0) item.MinStock = row.MinStock.Value;
                item.SepidarCode = InventoryText.Clean(row.SepidarCode) ?? item.SepidarCode;
                item.LegacyCode = InventoryText.Clean(row.LegacyCode) ?? item.LegacyCode;
                // existing data is imported as it is; codes outside WI-WH-002 are listed for cleanup (data-quality report)
                CodeCheckDto check = ItemCodeRules.Check(code, codingValues);
                item.GroupCode = check.GroupCode ?? item.GroupCode;
                item.StageCode = check.StageCode;
                item.BaseCode = check.BaseCode ?? code;
                item.MescCode = check.MescCode ?? ItemCodeRules.Normalize(row.MescCode) ?? item.MescCode;
                if (isNew) item.RequiresIncomingInspection = Quality.ItemQualityRules.DefaultInspection(item.GroupCode);
                if (!check.IsValid) result.Warnings.Add($"ردیف {rowNumber}: کد {code} مطابق WI-WH-002 نیست: {string.Join("؛ ", check.Errors)}");
                else if (check.StageNotStockable) result.Warnings.Add($"ردیف {rowNumber}: کد {code} وضعیت غیرانبارشی دارد (★).");
                Stamp(item, isNew);
            }

            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(result);
        }

        // ---------------------------------------------------------------------------- warehouses

        public async Task<ResponseDto<List<WarehouseDto>>> GetWarehousesAsync(CancellationToken cancellationToken)
        {
            List<WarehouseDto> list = await Warehouses
                .Where(w => !w.Deleted)
                .OrderBy(w => w.Code)
                .Select(w => new WarehouseDto
                {
                    Id = w.Id,
                    Code = w.Code,
                    Title = w.Title,
                    Kind = w.Kind,
                    Description = w.Description,
                    IsActive = w.IsActive,
                    SepidarCode = w.SepidarCode,
                    Locations = w.Locations.Where(l => !l.Deleted).OrderBy(l => l.Code).Select(l => new LocationDto
                    {
                        Id = l.Id, WarehouseId = l.WarehouseId, WarehouseCode = w.Code, Code = l.Code, Title = l.Title, IsActive = l.IsActive,
                    }).ToList(),
                })
                .ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<WarehouseDto>> SaveWarehouseAsync(SaveWarehouseDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<WarehouseDto>(InventoryMessages.InvalidData);
            string code = InventoryText.Clean(dto.Code);
            string title = InventoryText.Clean(dto.Title);
            if (code == null) return ResponseDtoHelper.BadRequest<WarehouseDto>(InventoryMessages.CodeRequired);
            if (title == null) return ResponseDtoHelper.BadRequest<WarehouseDto>(InventoryMessages.TitleRequired);
            if (!Enum.IsDefined(typeof(WarehouseKind), dto.Kind)) return ResponseDtoHelper.BadRequest<WarehouseDto>("نوع انبار نامعتبر است.");
            if (await Warehouses.AnyAsync(w => !w.Deleted && w.Code == code && w.Id != (dto.Id ?? 0), cancellationToken))
                return ResponseDtoHelper.BadRequest<WarehouseDto>(InventoryMessages.DuplicateCode + code);

            Warehouse warehouse;
            if (dto.Id.HasValue)
            {
                warehouse = await Warehouses.FirstOrDefaultAsync(w => w.Id == dto.Id.Value && !w.Deleted, cancellationToken);
                if (warehouse == null) return ResponseDtoHelper.NotFound<WarehouseDto>(InventoryMessages.WarehouseNotFound);
            }
            else
            {
                warehouse = new Warehouse();
                Warehouses.Add(warehouse);
            }
            warehouse.Code = code;
            warehouse.Title = title;
            warehouse.Kind = dto.Kind;
            warehouse.Description = InventoryText.Clean(dto.Description);
            warehouse.IsActive = dto.IsActive;
            warehouse.SepidarCode = InventoryText.Clean(dto.SepidarCode);
            Stamp(warehouse, !dto.Id.HasValue);
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper.Ok(new WarehouseDto
            {
                Id = warehouse.Id, Code = warehouse.Code, Title = warehouse.Title, Kind = warehouse.Kind,
                Description = warehouse.Description, IsActive = warehouse.IsActive, SepidarCode = warehouse.SepidarCode,
            });
        }

        public async Task<ResponseDto<LocationDto>> SaveLocationAsync(SaveLocationDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<LocationDto>(InventoryMessages.InvalidData);
            string code = InventoryText.Clean(dto.Code);
            if (code == null) return ResponseDtoHelper.BadRequest<LocationDto>(InventoryMessages.CodeRequired);
            Warehouse warehouse = await Warehouses.FirstOrDefaultAsync(w => w.Id == dto.WarehouseId && !w.Deleted, cancellationToken);
            if (warehouse == null) return ResponseDtoHelper.NotFound<LocationDto>(InventoryMessages.WarehouseNotFound);
            if (await Locations.AnyAsync(l => !l.Deleted && l.WarehouseId == dto.WarehouseId && l.Code == code && l.Id != (dto.Id ?? 0), cancellationToken))
                return ResponseDtoHelper.BadRequest<LocationDto>(InventoryMessages.DuplicateCode + code);

            WarehouseLocation location;
            if (dto.Id.HasValue)
            {
                location = await Locations.FirstOrDefaultAsync(l => l.Id == dto.Id.Value && !l.Deleted, cancellationToken);
                if (location == null) return ResponseDtoHelper.NotFound<LocationDto>(InventoryMessages.LocationNotFound);
                if (location.WarehouseId != dto.WarehouseId &&
                    await _uow.Set<StockBalance>().AnyAsync(b => b.LocationId == location.Id && b.Quantity != 0, cancellationToken))
                    return ResponseDtoHelper.BadRequest<LocationDto>("این محل موجودی دارد و به انبار دیگری منتقل نمی‌شود.");
            }
            else
            {
                location = new WarehouseLocation();
                Locations.Add(location);
            }
            location.WarehouseId = dto.WarehouseId;
            location.Code = code;
            location.Title = InventoryText.Clean(dto.Title);
            location.IsActive = dto.IsActive;
            Stamp(location, !dto.Id.HasValue);
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper.Ok(new LocationDto
            {
                Id = location.Id, WarehouseId = location.WarehouseId, WarehouseCode = warehouse.Code,
                Code = location.Code, Title = location.Title, IsActive = location.IsActive,
            });
        }

        public async Task<ResponseDto<bool>> DeleteLocationAsync(int id, CancellationToken cancellationToken)
        {
            WarehouseLocation location = await Locations.FirstOrDefaultAsync(l => l.Id == id && !l.Deleted, cancellationToken);
            if (location == null) return ResponseDtoHelper.NotFound<bool>(InventoryMessages.LocationNotFound);
            if (await _uow.Set<StockBalance>().AnyAsync(b => b.LocationId == id && b.Quantity != 0, cancellationToken))
                return ResponseDtoHelper.BadRequest<bool>("این محل موجودی دارد و حذف نمی‌شود.");
            location.Delete();
            Stamp(location, false);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }
    }
}
