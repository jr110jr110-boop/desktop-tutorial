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
            balances = ApplyPb5Filters(balances, query);
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
            PagedList<StockBalanceDto> page = await PagedList<StockBalanceDto>.CreateAsync(rows, query.PageNumber, query.PageSize, cancellationToken);
            if (InventoryPriceAccess.SeesValues(_user))
            {
                List<int> itemIds = page.Select(r => r.ItemId).Distinct().ToList();
                Dictionary<int, decimal> avg = await _uow.Set<Entities.Model.NasbNiro.Finance.ItemCost>().AsNoTracking().Where(c => itemIds.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, c => c.AverageCost, cancellationToken);
                var customer = (await _uow.Set<Warehouse>().AsNoTracking().Where(w => w.Kind == WarehouseKind.CustomerProperty).Select(w => w.Id).ToListAsync(cancellationToken)).ToHashSet();
                foreach (StockBalanceDto r in page)
                {
                    decimal cost = customer.Contains(r.WarehouseId) ? 0m : avg.TryGetValue(r.ItemId, out decimal a) ? a : 0m;
                    r.UnitCost = cost;
                    r.Value = Math.Round(r.Quantity * cost, 0, MidpointRounding.AwayFromZero);
                }
            }
            return ResponseDtoHelper.Ok(page);
        }

        /// <summary>PB5 (R11, R1): filters of the stock lists by platform, equipment type, valve model, warehouse kind and project.</summary>
        internal static IQueryable<StockBalance> ApplyPb5Filters(IQueryable<StockBalance> balances, StockQuery query)
        {
            string platform = InventoryText.Clean(query.Platform);
            if (platform != null) balances = balances.Where(b => b.Item.Platform == platform);
            string equipment = InventoryText.Clean(query.EquipmentType);
            if (equipment != null) balances = balances.Where(b => b.Item.EquipmentType == equipment);
            string valve = InventoryText.Clean(query.ValveModel);
            if (valve != null) balances = balances.Where(b => b.Item.ValveModel == valve);
            if (query.WarehouseKind.HasValue) balances = balances.Where(b => b.Warehouse.Kind == query.WarehouseKind.Value);
            if (query.WorkOrderId.HasValue) balances = balances.Where(b => b.Warehouse.WorkOrderId == query.WorkOrderId.Value);
            return balances;
        }

        public async Task<ResponseDto<PagedList<ItemStockDto>>> GetItemStocksAsync(StockQuery query, CancellationToken cancellationToken)
        {
            query ??= new StockQuery();
            IQueryable<StockBalance> balances = _uow.Set<StockBalance>();
            if (query.WarehouseId.HasValue) balances = balances.Where(b => b.WarehouseId == query.WarehouseId.Value);
            if (query.WarehouseKind.HasValue) balances = balances.Where(b => b.Warehouse.Kind == query.WarehouseKind.Value);
            if (query.WorkOrderId.HasValue) balances = balances.Where(b => b.Warehouse.WorkOrderId == query.WorkOrderId.Value);

            IQueryable<InventoryItem> items = _uow.Set<InventoryItem>().Where(i => !i.Deleted && i.IsActive);
            if (query.ItemId.HasValue) items = items.Where(i => i.Id == query.ItemId.Value);
            if (query.ItemGroupId.HasValue) items = items.Where(i => i.ItemGroupId == query.ItemGroupId.Value);
            string platform = InventoryText.Clean(query.Platform);
            if (platform != null) items = items.Where(i => i.Platform == platform);
            string equipment = InventoryText.Clean(query.EquipmentType);
            if (equipment != null) items = items.Where(i => i.EquipmentType == equipment);
            string valve = InventoryText.Clean(query.ValveModel);
            if (valve != null) items = items.Where(i => i.ValveModel == valve);
            string text = InventoryText.Clean(query.Contains);
            if (text != null) items = items.Where(i => i.Code.Contains(text) || i.Title.Contains(text));

            // PB5 (R26): an item without its own minimum uses the default of its group (or of a parent group)
            if (query.LowStockOnly) return await LowStockAsync(items, balances, query, cancellationToken);

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
                    Platform = x.Item.Platform,
                    EquipmentType = x.Item.EquipmentType,
                    ValveModel = x.Item.ValveModel,
                });
            PagedList<ItemStockDto> page = await PagedList<ItemStockDto>.CreateAsync(rows, query.PageNumber, query.PageSize, cancellationToken);
            await FillGroupMinimumAsync(page, cancellationToken);
            await FillItemValuesAsync(page, cancellationToken);
            return ResponseDtoHelper.Ok(page);
        }

        private async Task<Dictionary<int, ReorderRules.GroupDefaults>> LoadGroupDefaultsAsync(CancellationToken ct)
        {
            var groups = await _uow.Set<ItemGroup>().AsNoTracking().Where(g => !g.Deleted)
                .Select(g => new ReorderRules.GroupDefaults { Id = g.Id, ParentId = g.ParentId, Min = g.DefaultMinStock, Max = g.DefaultMaxStock, Quantity = g.DefaultReorderQuantity, LeadDays = g.DefaultLeadTimeDays })
                .ToListAsync(ct);
            return ReorderRules.Effective(groups);
        }

        /// <summary>Shows the effective minimum (group default when the item has none) and the low flag on a page.</summary>
        private async Task FillGroupMinimumAsync(PagedList<ItemStockDto> page, CancellationToken ct)
        {
            if (page.Count == 0) return;
            List<int> ids = page.Select(r => r.ItemId).ToList();
            var own = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.MinStock, i.ItemGroupId }).ToDictionaryAsync(i => i.Id, ct);
            Dictionary<int, ReorderRules.GroupDefaults> groups = await LoadGroupDefaultsAsync(ct);
            foreach (ItemStockDto row in page)
            {
                var o = own[row.ItemId];
                ReorderRules.GroupDefaults g = o.ItemGroupId.HasValue && groups.TryGetValue(o.ItemGroupId.Value, out ReorderRules.GroupDefaults x) ? x : null;
                row.MinStock = ReorderRules.For(o.MinStock, null, null, null, g).Min;
                row.IsLow = row.OnHand < row.MinStock;
            }
        }

        private async Task FillItemValuesAsync(PagedList<ItemStockDto> page, CancellationToken ct)
        {
            if (!InventoryPriceAccess.SeesValues(_user) || page.Count == 0) return;
            List<int> ids = page.Select(r => r.ItemId).ToList();
            Dictionary<int, decimal> avg = await _uow.Set<Entities.Model.NasbNiro.Finance.ItemCost>().AsNoTracking().Where(c => ids.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, c => c.AverageCost, ct);
            foreach (ItemStockDto row in page)
            {
                decimal cost = avg.TryGetValue(row.ItemId, out decimal a) ? a : 0m;
                row.AverageCost = cost;
                row.Value = Math.Round(row.OnHand * cost, 0, MidpointRounding.AwayFromZero);
            }
        }

        private async Task<ResponseDto<PagedList<ItemStockDto>>> LowStockAsync(IQueryable<InventoryItem> items, IQueryable<StockBalance> balances, StockQuery query, CancellationToken ct)
        {
            var list = await items.Select(i => new
            {
                i.Id, i.Code, i.Title, i.Unit, GroupTitle = i.ItemGroup.Title, i.ItemGroupId, i.TrackingMode, i.MinStock, i.Platform, i.EquipmentType, i.ValveModel,
                OnHand = balances.Where(b => b.ItemId == i.Id).Sum(b => (decimal?)b.Quantity) ?? 0,
            }).ToListAsync(ct);
            Dictionary<int, ReorderRules.GroupDefaults> groups = await LoadGroupDefaultsAsync(ct);
            var low = new List<ItemStockDto>();
            foreach (var i in list)
            {
                ReorderRules.GroupDefaults g = i.ItemGroupId.HasValue && groups.TryGetValue(i.ItemGroupId.Value, out ReorderRules.GroupDefaults x) ? x : null;
                decimal min = ReorderRules.For(i.MinStock, null, null, null, g).Min;
                if (!(i.OnHand < min)) continue;
                low.Add(new ItemStockDto
                {
                    ItemId = i.Id, ItemCode = i.Code, ItemTitle = i.Title, Unit = i.Unit, ItemGroupTitle = i.GroupTitle, TrackingMode = i.TrackingMode, OnHand = i.OnHand,
                    MinStock = min, IsLow = true, Platform = i.Platform, EquipmentType = i.EquipmentType, ValveModel = i.ValveModel,
                });
            }
            PagedList<ItemStockDto> page = await PagedList<ItemStockDto>.CreateAsync(low.OrderBy(r => r.ItemCode).ToList(), query.PageNumber, query.PageSize, ct);
            await FillItemValuesAsync(page, ct);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<KardexDto>> GetKardexAsync(KardexQuery query, CancellationToken cancellationToken)
        {
            if (query == null || query.ItemId <= 0) return ResponseDtoHelper.BadRequest<KardexDto>("کالا را انتخاب کنید.");
            InventoryItem item = await _uow.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == query.ItemId, cancellationToken);
            if (item == null) return ResponseDtoHelper.NotFound<KardexDto>(InventoryMessages.ItemNotFound);

            IQueryable<StockTransaction> transactions = _uow.Set<StockTransaction>().Where(t => t.ItemId == query.ItemId);
            if (query.WarehouseId.HasValue) transactions = transactions.Where(t => t.WarehouseId == query.WarehouseId.Value);

            // PB5: landed cost (approved freight) adds value on its own date without quantity
            IQueryable<InventoryFreightAllocation> freights = _uow.Set<InventoryFreightAllocation>().Where(a => a.ItemId == query.ItemId && a.Freight.Status == FreightStatus.Approved && !a.Freight.Deleted);
            if (query.WarehouseId.HasValue) freights = freights.Where(a => a.Freight.ReceiptDocument.WarehouseId == query.WarehouseId.Value);

            decimal opening = 0, openingValue = 0;
            if (query.From.HasValue)
            {
                DateTime from = query.From.Value.Date;
                opening = await transactions.Where(t => t.TransactionDate < from).SumAsync(t => (decimal?)t.Quantity, cancellationToken) ?? 0;
                openingValue = await transactions.Where(t => t.TransactionDate < from).SumAsync(t => t.CostAmount, cancellationToken) ?? 0;
                openingValue += await freights.Where(a => a.Freight.FreightDate < from).SumAsync(a => (decimal?)a.Amount, cancellationToken) ?? 0;
                transactions = transactions.Where(t => t.TransactionDate >= from);
                freights = freights.Where(a => a.Freight.FreightDate >= from);
            }
            if (query.To.HasValue)
            {
                DateTime to = query.To.Value.Date.AddDays(1);
                transactions = transactions.Where(t => t.TransactionDate < to);
                freights = freights.Where(a => a.Freight.FreightDate < to);
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
                    UnitCost = t.UnitCost,
                    Amount = t.CostAmount,
                    Supplier = t.Lot.Provider.Name ?? t.Document.Provider.Name,
                    ReceiptNumber = t.Document.Type == InventoryDocumentType.PurchaseReceipt || t.Document.Type == InventoryDocumentType.ConsignmentReceipt ? t.Document.Number : null,
                })
                .ToListAsync(cancellationToken);

            var freightRows = await freights.Select(a => new KardexRowDto
            {
                Date = a.Freight.FreightDate,
                DocumentId = a.Freight.ReceiptDocumentId,
                DocumentNumber = a.Freight.Number,
                DocumentType = InventoryDocumentType.PurchaseReceipt,
                WarehouseTitle = a.Freight.ReceiptDocument.Warehouse.Title,
                Amount = a.Amount,
                Supplier = a.Freight.CarrierProvider.Name,
                ReceiptNumber = a.Freight.ReceiptDocument.Number,
                IsFreight = true,
                Note = "کرایه حمل (بهای تمام‌شده نهایی)",
            }).ToListAsync(cancellationToken);
            rows.AddRange(freightRows);
            rows = rows.OrderBy(r => r.Date).ThenBy(r => r.IsFreight).ToList();

            decimal balance = opening, value = openingValue;
            foreach (KardexRowDto row in rows)
            {
                balance += row.In - row.Out;
                value += row.Amount ?? 0;
                row.Balance = balance;
                row.RunningValue = value;
            }

            var kardex = new KardexDto
            {
                ItemId = item.Id,
                ItemCode = item.Code,
                ItemTitle = item.Title,
                Unit = item.Unit,
                OpeningBalance = opening,
                ClosingBalance = balance,
                OpeningValue = openingValue,
                ClosingValue = value,
                Rows = rows,
            };
            InventoryPriceAccess.Mask(kardex, InventoryPriceAccess.SeesValues(_user));
            return ResponseDtoHelper.Ok(kardex);
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
            PagedList<LotDto> page = await PagedList<LotDto>.CreateAsync(rows, query.PageNumber, query.PageSize, cancellationToken);
            await FillLotValuesAsync(page, cancellationToken);
            return ResponseDtoHelper.Ok(page);
        }

        /// <summary>PB5 (R29): value of a lot on hand (weighted-average cost of the item); only readers with price access get it. Customer property is not valued.</summary>
        private async Task FillLotValuesAsync(IEnumerable<LotDto> lots, CancellationToken ct)
        {
            if (!InventoryPriceAccess.SeesValues(_user)) return;
            List<LotDto> list = lots.ToList();
            List<int> ids = list.Select(l => l.ItemId).Distinct().ToList();
            Dictionary<int, decimal> avg = await _uow.Set<Entities.Model.NasbNiro.Finance.ItemCost>().AsNoTracking().Where(c => ids.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, c => c.AverageCost, ct);
            foreach (LotDto l in list)
                l.OnHandValue = l.IsCustomerProperty ? 0m : Math.Round(l.OnHand * (avg.TryGetValue(l.ItemId, out decimal a) ? a : 0m), 0, MidpointRounding.AwayFromZero);
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
            await FillLotValuesAsync(new[] { saved }, cancellationToken);
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

            // a lot allowed for use after its expiry by a valid concession NCR on that lot (all signatures, the permit after the QC signature, approved on or after the expiry date)
            // is marked, not hidden: the same rule the documents apply (review M13)
            List<int> lotIds = rows.Select(r => r.LotId).Distinct().ToList();
            var concessionRows = (await Quality.ConcessionRules.Valid(_uow.Set<NonConformance>().Where(n => n.LotId != null && lotIds.Contains(n.LotId.Value)), true)
                .Select(n => new { LotId = n.LotId.Value, n.Number, At = n.ConcessionApprovedAt }).ToListAsync(cancellationToken))
                .Where(x => x.At.HasValue).ToList();

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
                    ConcessionNcrNumber = concessionRows.Where(c => c.LotId == first.LotId && Quality.LotExpiryRules.ConcessionCovers(first.Expiry, c.At.Value))
                        .OrderBy(c => c.Number).Select(c => c.Number).FirstOrDefault(),
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
