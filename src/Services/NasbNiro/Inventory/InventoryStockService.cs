using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Inventory
{
    public interface IInventoryStockService
    {
        Task<ResponseDto<PagedList<StockBalanceDto>>> GetBalancesAsync(StockQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<ItemStockDto>>> GetItemStocksAsync(StockQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<KardexDto>> GetKardexAsync(KardexQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<LotDto>>> GetLotsAsync(LotQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<LotDto>> SaveLotAsync(SaveLotDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<SerialUnitDto>>> GetSerialsAsync(SerialQuery query, CancellationToken cancellationToken);

        /// <summary>R1q D32: lots on hand that are expired or expire within the warning days (company setting ExpiryWarnDays), oldest expiry first.</summary>
        Task<ResponseDto<PagedList<LotExpiryDto>>> GetExpiringLotsAsync(LotExpiryQuery query, CancellationToken cancellationToken);
    }

    public class InventoryStockService : IInventoryStockService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public InventoryStockService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public async Task<ResponseDto<PagedList<StockBalanceDto>>> GetBalancesAsync(StockQuery query, CancellationToken cancellationToken)
        {
            query ??= new StockQuery();
            IQueryable<StockBalance> balances = _uow.Set<StockBalance>().Where(b => b.Quantity != 0);
            if (query.ItemId.HasValue) balances = balances.Where(b => b.ItemId == query.ItemId.Value);
            if (query.WarehouseId.HasValue) balances = balances.Where(b => b.WarehouseId == query.WarehouseId.Value);
            if (query.ItemGroupId.HasValue) balances = balances.Where(b => b.Item.ItemGroupId == query.ItemGroupId.Value);
            string text = InventoryText.Clean(query.Contains);
            if (text != null)
                balances = balances.Where(b => b.Item.Code.Contains(text) || b.Item.Title.Contains(text) ||
                                               b.Lot.LotNumber.Contains(text) || b.Lot.HeatNumber.Contains(text));

            IQueryable<StockBalanceDto> rows = balances
                .OrderBy(b => b.Item.Code).ThenBy(b => b.Warehouse.Code).ThenBy(b => b.Location.Code).ThenBy(b => b.Lot.LotNumber)
                .Select(b => new StockBalanceDto
                {
                    ItemId = b.ItemId,
                    ItemCode = b.Item.Code,
                    ItemTitle = b.Item.Title,
                    Unit = b.Item.Unit,
                    WarehouseId = b.WarehouseId,
                    WarehouseTitle = b.Warehouse.Title,
                    LocationId = b.LocationId,
                    LocationCode = b.Location.Code,
                    LotId = b.LotId,
                    LotNumber = b.Lot.LotNumber,
                    HeatNumber = b.Lot.HeatNumber,
                    Quantity = b.Quantity,
                });
            return ResponseDtoHelper.Ok(await PagedList<StockBalanceDto>.CreateAsync(rows, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PagedList<ItemStockDto>>> GetItemStocksAsync(StockQuery query, CancellationToken cancellationToken)
        {
            query ??= new StockQuery();
            IQueryable<StockBalance> balances = _uow.Set<StockBalance>();
            if (query.WarehouseId.HasValue) balances = balances.Where(b => b.WarehouseId == query.WarehouseId.Value);

            IQueryable<InventoryItem> items = _uow.Set<InventoryItem>().Where(i => !i.Deleted && i.IsActive);
            if (query.ItemId.HasValue) items = items.Where(i => i.Id == query.ItemId.Value);
            if (query.ItemGroupId.HasValue) items = items.Where(i => i.ItemGroupId == query.ItemGroupId.Value);
            string text = InventoryText.Clean(query.Contains);
            if (text != null) items = items.Where(i => i.Code.Contains(text) || i.Title.Contains(text));

            IQueryable<ItemStockDto> rows = items
                .Select(i => new
                {
                    Item = i,
                    OnHand = balances.Where(b => b.ItemId == i.Id).Sum(b => (decimal?)b.Quantity) ?? 0,
                })
                .Where(x => !query.LowStockOnly || x.OnHand < x.Item.MinStock)
                .OrderBy(x => x.Item.Code)
                .Select(x => new ItemStockDto
                {
                    ItemId = x.Item.Id,
                    ItemCode = x.Item.Code,
                    ItemTitle = x.Item.Title,
                    Unit = x.Item.Unit,
                    ItemGroupTitle = x.Item.ItemGroup.Title,
                    TrackingMode = x.Item.TrackingMode,
                    OnHand = x.OnHand,
                    MinStock = x.Item.MinStock,
                    IsLow = x.OnHand < x.Item.MinStock,
                });
            return ResponseDtoHelper.Ok(await PagedList<ItemStockDto>.CreateAsync(rows, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<KardexDto>> GetKardexAsync(KardexQuery query, CancellationToken cancellationToken)
        {
            if (query == null || query.ItemId <= 0) return ResponseDtoHelper.BadRequest<KardexDto>("کالا را انتخاب کنید.");
            InventoryItem item = await _uow.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == query.ItemId, cancellationToken);
            if (item == null) return ResponseDtoHelper.NotFound<KardexDto>(InventoryMessages.ItemNotFound);

            IQueryable<StockTransaction> transactions = _uow.Set<StockTransaction>().Where(t => t.ItemId == query.ItemId);
            if (query.WarehouseId.HasValue) transactions = transactions.Where(t => t.WarehouseId == query.WarehouseId.Value);

            decimal opening = 0;
            if (query.From.HasValue)
            {
                DateTime from = query.From.Value.Date;
                opening = await transactions.Where(t => t.TransactionDate < from).SumAsync(t => (decimal?)t.Quantity, cancellationToken) ?? 0;
                transactions = transactions.Where(t => t.TransactionDate >= from);
            }
            if (query.To.HasValue)
            {
                DateTime to = query.To.Value.Date.AddDays(1);
                transactions = transactions.Where(t => t.TransactionDate < to);
            }

            var rows = await transactions
                .OrderBy(t => t.TransactionDate).ThenBy(t => t.Id)
                .Take(5000)
                .Select(t => new KardexRowDto
                {
                    Date = t.TransactionDate,
                    DocumentId = t.DocumentId,
                    DocumentNumber = t.Document.Number,
                    DocumentType = t.Document.Type,
                    WarehouseTitle = t.Warehouse.Title,
                    LocationCode = t.Location.Code,
                    LotNumber = t.Lot.LotNumber,
                    SerialNumber = t.SerialUnit.SerialNumber,
                    In = t.Quantity > 0 ? t.Quantity : 0,
                    Out = t.Quantity < 0 ? -t.Quantity : 0,
                    UnitPrice = t.UnitPrice,
                    IsReversal = t.IsReversal,
                })
                .ToListAsync(cancellationToken);

            decimal balance = opening;
            foreach (KardexRowDto row in rows)
            {
                balance += row.In - row.Out;
                row.Balance = balance;
            }

            return ResponseDtoHelper.Ok(new KardexDto
            {
                ItemId = item.Id,
                ItemCode = item.Code,
                ItemTitle = item.Title,
                Unit = item.Unit,
                OpeningBalance = opening,
                ClosingBalance = balance,
                Rows = rows,
            });
        }

        internal static IQueryable<LotDto> LotProjection(IQueryable<InventoryLot> lots, IQueryable<StockBalance> balances) =>
            lots.Select(l => new LotDto
            {
                Id = l.Id,
                ItemId = l.ItemId,
                ItemCode = l.Item.Code,
                ItemTitle = l.Item.Title,
                Unit = l.Item.Unit,
                LotNumber = l.LotNumber,
                HeatNumber = l.HeatNumber,
                CertificateNumber = l.CertificateNumber,
                CertificateType = l.CertificateType,
                CertificateDocumentId = l.CertificateDocumentId,
                ProviderId = l.ProviderId,
                ProviderName = l.Provider.Name,
                ProductionDate = l.ProductionDate,
                Description = l.Description,
                ExpiryDate = l.ExpiryDate,
                ParentLotId = l.ParentLotId,
                ParentLotNumber = l.ParentLot.LotNumber,
                ParentItemCode = l.ParentLot.Item.Code,
                IsCustomerProperty = l.IsCustomerProperty,
                OnHand = balances.Where(b => b.LotId == l.Id).Sum(b => (decimal?)b.Quantity) ?? 0,
            });

        public async Task<ResponseDto<PagedList<LotDto>>> GetLotsAsync(LotQuery query, CancellationToken cancellationToken)
        {
            query ??= new LotQuery();
            IQueryable<InventoryLot> lots = _uow.Set<InventoryLot>().Where(l => !l.Deleted);
            if (query.ItemId.HasValue) lots = lots.Where(l => l.ItemId == query.ItemId.Value);
            string text = InventoryText.Clean(query.Contains);
            if (text != null)
                lots = lots.Where(l => l.LotNumber.Contains(text) || l.HeatNumber.Contains(text) || l.CertificateNumber.Contains(text) ||
                                       l.Item.Code.Contains(text));
            IQueryable<LotDto> rows = LotProjection(lots.OrderByDescending(l => l.Id), _uow.Set<StockBalance>());
            return ResponseDtoHelper.Ok(await PagedList<LotDto>.CreateAsync(rows, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<LotDto>> SaveLotAsync(SaveLotDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<LotDto>(InventoryMessages.InvalidData);
            string lotNumber = InventoryText.Clean(dto.LotNumber);
            if (lotNumber == null) return ResponseDtoHelper.BadRequest<LotDto>("شماره بچ را وارد کنید.");
            InventoryItem item = await _uow.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == dto.ItemId && !i.Deleted, cancellationToken);
            if (item == null) return ResponseDtoHelper.NotFound<LotDto>(InventoryMessages.ItemNotFound);
            if (item.TrackingMode == ItemTrackingMode.None) return ResponseDtoHelper.BadRequest<LotDto>("این کالا بچ ندارد (نوع ردیابی: بدون ردیابی).");
            if (dto.ProviderId.HasValue && !await _uow.Set<Provider>().AnyAsync(p => p.Id == dto.ProviderId.Value, cancellationToken))
                return ResponseDtoHelper.BadRequest<LotDto>("تامین‌کننده پیدا نشد.");
            if (await _uow.Set<InventoryLot>().AnyAsync(l => !l.Deleted && l.ItemId == dto.ItemId && l.LotNumber == lotNumber && l.Id != (dto.Id ?? 0), cancellationToken))
                return ResponseDtoHelper.BadRequest<LotDto>("این شماره بچ برای این کالا قبلاً ثبت شده است.");

            InventoryLot lot;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                lot = await _uow.Set<InventoryLot>().FirstOrDefaultAsync(l => l.Id == dto.Id.Value && !l.Deleted, cancellationToken);
                if (lot == null) return ResponseDtoHelper.NotFound<LotDto>("بچ پیدا نشد.");
                if (lot.ItemId != dto.ItemId) return ResponseDtoHelper.BadRequest<LotDto>("کالای بچ قابل تغییر نیست.");
            }
            else
            {
                lot = new InventoryLot { ItemId = dto.ItemId, CreateDate = now, CreatorUserId = _user.UserId };
                _uow.Set<InventoryLot>().Add(lot);
            }
            lot.LotNumber = lotNumber;
            lot.HeatNumber = InventoryText.Clean(dto.HeatNumber);
            lot.CertificateNumber = InventoryText.Clean(dto.CertificateNumber);
            lot.CertificateType = InventoryText.Clean(dto.CertificateType);
            lot.CertificateDocumentId = dto.CertificateDocumentId;
            lot.ProviderId = dto.ProviderId;
            lot.ProductionDate = dto.ProductionDate;
            lot.Description = InventoryText.Clean(dto.Description);
            lot.ModifyDate = now;
            lot.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);

            LotDto saved = await LotProjection(_uow.Set<InventoryLot>().Where(l => l.Id == lot.Id), _uow.Set<StockBalance>()).FirstAsync(cancellationToken);
            return ResponseDtoHelper.Ok(saved);
        }

        public async Task<ResponseDto<PagedList<LotExpiryDto>>> GetExpiringLotsAsync(LotExpiryQuery query, CancellationToken cancellationToken)
        {
            query ??= new LotExpiryQuery();
            Dictionary<string, decimal> settings = await Quality.QualitySettingKeys.LoadAsync(_uow, cancellationToken);
            int within = query.WithinDays ?? (int)settings[Quality.QualitySettingKeys.ExpiryWarnDays];
            if (within < 0 || within > 3650) return ResponseDtoHelper.BadRequest<PagedList<LotExpiryDto>>("تعداد روز باید بین ۰ تا ۳۶۵۰ باشد.");
            DateTime today = DateTime.Today;
            DateTime cutoff = today.AddDays(within);

            // goods already in the scrap warehouse are no longer a risk; customer property is not company stock
            IQueryable<StockBalance> balances = _uow.Set<StockBalance>().Where(b => b.Quantity > 0 && b.LotId != null && b.Warehouse.Kind != WarehouseKind.Scrap &&
                !b.Lot.Deleted && !b.Lot.IsCustomerProperty && b.Lot.ExpiryDate != null && b.Lot.ExpiryDate <= cutoff);
            if (query.ItemId.HasValue) balances = balances.Where(b => b.ItemId == query.ItemId.Value);
            if (query.OnlyExpired) balances = balances.Where(b => b.Lot.ExpiryDate < today);
            var rows = await balances.Select(b => new
            {
                LotId = b.LotId.Value, b.ItemId, ItemCode = b.Item.Code, ItemTitle = b.Item.Title, b.Item.Unit,
                b.Lot.LotNumber, b.Lot.HeatNumber, Expiry = b.Lot.ExpiryDate.Value, Warehouse = b.Warehouse.Code, Location = b.Location.Code, b.Quantity,
            }).ToListAsync(cancellationToken);

            // a lot allowed for use after its expiry by an approved concession NCR on that lot is marked, not hidden
            List<int> lotIds = rows.Select(r => r.LotId).Distinct().ToList();
            var concessions = (await _uow.Set<NonConformance>()
                .Where(n => !n.Deleted && n.LotId != null && lotIds.Contains(n.LotId.Value) && n.Disposition == NcrDisposition.Concession && n.ConcessionApprovedAt != null &&
                            (n.Status == NcrStatus.Approved || n.Status == NcrStatus.Executed || n.Status == NcrStatus.Closed))
                .Select(n => new { LotId = n.LotId.Value, n.Number }).ToListAsync(cancellationToken))
                .GroupBy(x => x.LotId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Number).First().Number);

            List<LotExpiryDto> list = rows.GroupBy(r => r.LotId).Select(g =>
            {
                var first = g.First();
                return new LotExpiryDto
                {
                    LotId = first.LotId, ItemId = first.ItemId, ItemCode = first.ItemCode, ItemTitle = first.ItemTitle, Unit = first.Unit,
                    LotNumber = first.LotNumber, HeatNumber = first.HeatNumber, ExpiryDate = first.Expiry,
                    DaysLeft = (int)(first.Expiry.Date - today).TotalDays, Expired = first.Expiry.Date < today,
                    OnHand = g.Sum(x => x.Quantity),
                    Locations = string.Join("، ", g.OrderBy(x => x.Warehouse).ThenBy(x => x.Location).Select(x => $"{x.Warehouse}{(x.Location != null ? "/" + x.Location : string.Empty)} ({x.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)})")),
                    ConcessionNcrNumber = concessions.TryGetValue(first.LotId, out string number) ? number : null,
                };
            }).OrderBy(x => x.ExpiryDate).ThenBy(x => x.ItemCode).ThenBy(x => x.LotNumber).ToList();
            return ResponseDtoHelper.Ok(await PagedList<LotExpiryDto>.CreateAsync(list, query.PageNumber, query.PageSize, cancellationToken));
        }

        internal static IQueryable<SerialUnitDto> SerialProjection(IQueryable<SerialUnit> units) =>
            units.Select(u => new SerialUnitDto
            {
                Id = u.Id,
                ItemId = u.ItemId,
                ItemCode = u.Item.Code,
                ItemTitle = u.Item.Title,
                SerialNumber = u.SerialNumber,
                LotId = u.LotId,
                LotNumber = u.Lot.LotNumber,
                HeatNumber = u.Lot.HeatNumber,
                Status = u.Status,
                WarehouseId = u.WarehouseId,
                WarehouseTitle = u.Warehouse.Title,
                LocationCode = u.Location.Code,
                ParentSerialUnitId = u.ParentSerialUnitId,
                ParentSerialNumber = u.ParentSerialUnit.SerialNumber,
                ParentItemTitle = u.ParentSerialUnit.Item.Title,
                WorkOrderId = u.WorkOrderId,
                WorkOrderCode = u.WorkOrder.Code,
                Description = u.Description,
                CreateDate = u.CreateDate,
                SourceSerialUnitId = u.SourceSerialUnitId,
                SourceSerialNumber = u.SourceSerialUnit.SerialNumber,
                SourceItemCode = u.SourceSerialUnit != null ? u.SourceSerialUnit.Item.Code : u.SourceLot.Item.Code,
                SourceLotId = u.SourceLotId,
                SourceLotNumber = u.SourceLot.LotNumber,
            });

        public async Task<ResponseDto<PagedList<SerialUnitDto>>> GetSerialsAsync(SerialQuery query, CancellationToken cancellationToken)
        {
            query ??= new SerialQuery();
            IQueryable<SerialUnit> units = _uow.Set<SerialUnit>().Where(u => !u.Deleted);
            if (query.ItemId.HasValue) units = units.Where(u => u.ItemId == query.ItemId.Value);
            if (query.Status.HasValue) units = units.Where(u => u.Status == query.Status.Value);
            if (query.WarehouseId.HasValue) units = units.Where(u => u.WarehouseId == query.WarehouseId.Value);
            if (query.WorkOrderId.HasValue) units = units.Where(u => u.WorkOrderId == query.WorkOrderId.Value);
            string text = InventoryText.Clean(query.Contains);
            if (text != null)
                units = units.Where(u => u.SerialNumber.Contains(text) || u.Item.Code.Contains(text) || u.Item.Title.Contains(text) ||
                                         u.Lot.HeatNumber.Contains(text));
            IQueryable<SerialUnitDto> rows = SerialProjection(units.OrderByDescending(u => u.Id));
            return ResponseDtoHelper.Ok(await PagedList<SerialUnitDto>.CreateAsync(rows, query.PageNumber, query.PageSize, cancellationToken));
        }
    }
}
