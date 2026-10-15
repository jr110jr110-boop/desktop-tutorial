using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Inventory
{
    public interface IInventoryDocumentService
    {
        Task<ResponseDto<PagedList<DocumentListItemDto>>> GetDocumentsAsync(DocumentQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<DocumentDto>> GetDocumentAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<DocumentDto>> SaveDraftAsync(SaveDocumentDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> DeleteDraftAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<DocumentDto>> ConfirmAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<DocumentDto>> CancelAsync(int id, CancelDocumentDto dto, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Warehouse documents. A draft changes nothing; confirming posts the stock movements (ledger and balances)
    /// and moves serial units; cancelling a confirmed document writes reversing movements.
    /// Everything of one confirm or cancel is saved in one database transaction.
    /// </summary>
    public class InventoryDocumentService : IInventoryDocumentService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public InventoryDocumentService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private DbSet<InventoryDocument> Documents => _uow.Set<InventoryDocument>();

        // ------------------------------------------------------------------------------- queries

        public async Task<ResponseDto<PagedList<DocumentListItemDto>>> GetDocumentsAsync(DocumentQuery query, CancellationToken cancellationToken)
        {
            query ??= new DocumentQuery();
            IQueryable<InventoryDocument> documents = Documents.Where(d => !d.Deleted);
            if (query.Type.HasValue) documents = documents.Where(d => d.Type == query.Type.Value);
            if (query.Status.HasValue) documents = documents.Where(d => d.Status == query.Status.Value);
            if (query.WarehouseId.HasValue)
                documents = documents.Where(d => d.WarehouseId == query.WarehouseId.Value || d.DestinationWarehouseId == query.WarehouseId.Value);
            if (query.WorkOrderId.HasValue) documents = documents.Where(d => d.WorkOrderId == query.WorkOrderId.Value);
            if (query.From.HasValue) documents = documents.Where(d => d.DocumentDate >= query.From.Value.Date);
            if (query.To.HasValue) documents = documents.Where(d => d.DocumentDate < query.To.Value.Date.AddDays(1));
            string text = InventoryText.Clean(query.Contains);
            if (text != null)
                documents = documents.Where(d => d.Number.Contains(text) || d.ReferenceNumber.Contains(text) || d.Description.Contains(text) ||
                                                 d.Lines.Any(l => l.Item.Code.Contains(text) || l.Item.Title.Contains(text)));

            PagedList<DocumentListItemDto> page = await PagedList<DocumentListItemDto>.CreateAsync(
                ListProjection(documents.OrderByDescending(d => d.DocumentDate).ThenByDescending(d => d.Id)),
                query.PageNumber, query.PageSize, cancellationToken);
            return ResponseDtoHelper.Ok(page);
        }

        internal static IQueryable<DocumentListItemDto> ListProjection(IQueryable<InventoryDocument> documents) =>
            documents.Select(d => new DocumentListItemDto
            {
                Id = d.Id,
                Type = d.Type,
                Number = d.Number,
                DocumentDate = d.DocumentDate,
                Status = d.Status,
                WarehouseId = d.WarehouseId,
                WarehouseTitle = d.Warehouse.Title,
                DestinationWarehouseTitle = d.DestinationWarehouse.Title,
                ProviderName = d.Provider.Name,
                WorkOrderCode = d.WorkOrder.Code,
                ContractCode = d.Contract.Code,
                ReferenceNumber = d.ReferenceNumber,
                Description = d.Description,
                LinesCount = d.Lines.Count(),
                TotalQuantity = d.Lines.Sum(l => (decimal?)l.Quantity) ?? 0,
                SepidarStatus = d.SepidarStatus,
                SepidarExportedAt = d.SepidarExportedAt,
            });

        public async Task<ResponseDto<DocumentDto>> GetDocumentAsync(int id, CancellationToken cancellationToken)
        {
            DocumentDto document = await Documents
                .Where(d => d.Id == id && !d.Deleted)
                .Select(d => new DocumentDto
                {
                    Id = d.Id,
                    Type = d.Type,
                    Number = d.Number,
                    DocumentDate = d.DocumentDate,
                    Status = d.Status,
                    WarehouseId = d.WarehouseId,
                    WarehouseTitle = d.Warehouse.Title,
                    DestinationWarehouseId = d.DestinationWarehouseId,
                    DestinationWarehouseTitle = d.DestinationWarehouse.Title,
                    ProviderId = d.ProviderId,
                    ProviderName = d.Provider.Name,
                    BuyOrderId = d.BuyOrderId,
                    WorkOrderId = d.WorkOrderId,
                    WorkOrderCode = d.WorkOrder.Code,
                    ContractId = d.ContractId,
                    ContractCode = d.Contract.Code,
                    ReferenceNumber = d.ReferenceNumber,
                    Description = d.Description,
                    CreateDate = d.CreateDate,
                    ConfirmedAt = d.ConfirmedAt,
                    CancelledAt = d.CancelledAt,
                    CancelReason = d.CancelReason,
                    SepidarStatus = d.SepidarStatus,
                    SepidarReference = d.SepidarReference,
                    Lines = d.Lines.OrderBy(l => l.RowNumber).Select(l => new DocumentLineDto
                    {
                        Id = l.Id,
                        RowNumber = l.RowNumber,
                        ItemId = l.ItemId,
                        ItemCode = l.Item.Code,
                        ItemTitle = l.Item.Title,
                        Unit = l.Item.Unit,
                        TrackingMode = l.Item.TrackingMode,
                        Quantity = l.Quantity,
                        LotId = l.LotId,
                        LotNumber = l.Lot != null ? l.Lot.LotNumber : l.LotNumber,
                        HeatNumber = l.Lot != null ? l.Lot.HeatNumber : l.HeatNumber,
                        CertificateNumber = l.Lot != null ? l.Lot.CertificateNumber : l.CertificateNumber,
                        LocationId = l.LocationId,
                        LocationCode = l.Location.Code,
                        DestinationLocationId = l.DestinationLocationId,
                        DestinationLocationCode = l.DestinationLocation.Code,
                        UnitPrice = l.UnitPrice,
                        Description = l.Description,
                        SerialNumbers = l.Serials.OrderBy(s => s.Id).Select(s => s.SerialNumber).ToList(),
                    }).ToList(),
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (document == null) return ResponseDtoHelper.NotFound<DocumentDto>(InventoryMessages.DocumentNotFound);

            // names of the users who created, confirmed or cancelled the document
            var raw = await Documents.Where(d => d.Id == id)
                .Select(d => new { d.CreatorUserId, d.ConfirmedByUserId, d.CancelledByUserId }).FirstAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(
                _uow, new int?[] { raw.CreatorUserId, raw.ConfirmedByUserId, raw.CancelledByUserId }, cancellationToken);
            document.CreatorName = names.Name(raw.CreatorUserId);
            document.ConfirmedByName = names.Name(raw.ConfirmedByUserId);
            document.CancelledByName = names.Name(raw.CancelledByUserId);
            return ResponseDtoHelper.Ok(document);
        }

        // -------------------------------------------------------------------------------- drafts

        public async Task<ResponseDto<DocumentDto>> SaveDraftAsync(SaveDocumentDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.InvalidData);
            if (!InventoryDocumentRules.IsKnown(dto.Type)) return ResponseDtoHelper.BadRequest<DocumentDto>("نوع سند نامعتبر است.");
            if (dto.DocumentDate == default) return ResponseDtoHelper.BadRequest<DocumentDto>("تاریخ سند را وارد کنید.");

            Warehouse warehouse = await _uow.Set<Warehouse>().FirstOrDefaultAsync(w => w.Id == dto.WarehouseId && !w.Deleted, cancellationToken);
            if (warehouse == null) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.WarehouseNotFound);

            int targetWarehouseId = dto.WarehouseId;
            if (InventoryDocumentRules.IsTransfer(dto.Type))
            {
                if (!dto.DestinationWarehouseId.HasValue) return ResponseDtoHelper.BadRequest<DocumentDto>("انبار مقصد انتقال را انتخاب کنید.");
                if (!await _uow.Set<Warehouse>().AnyAsync(w => w.Id == dto.DestinationWarehouseId.Value && !w.Deleted, cancellationToken))
                    return ResponseDtoHelper.BadRequest<DocumentDto>("انبار مقصد پیدا نشد.");
                targetWarehouseId = dto.DestinationWarehouseId.Value;
            }
            else
            {
                dto.DestinationWarehouseId = null;
            }

            string referenceError = await CheckReferencesAsync(dto, cancellationToken);
            if (referenceError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(referenceError);

            List<SaveDocumentLineDto> lines = (dto.Lines ?? new List<SaveDocumentLineDto>()).Where(l => l != null).ToList();
            if (lines.Count == 0) return ResponseDtoHelper.BadRequest<DocumentDto>("سند حداقل یک ردیف لازم دارد.");

            List<int> itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
            Dictionary<int, InventoryItem> items = await _uow.Set<InventoryItem>()
                .Where(i => itemIds.Contains(i.Id) && !i.Deleted).ToDictionaryAsync(i => i.Id, cancellationToken);
            List<int> locationIds = lines.SelectMany(l => new[] { l.LocationId, l.DestinationLocationId })
                .Where(x => x.HasValue).Select(x => x.Value).Distinct().ToList();
            Dictionary<int, WarehouseLocation> locations = await _uow.Set<WarehouseLocation>()
                .Where(l => locationIds.Contains(l.Id) && !l.Deleted).ToDictionaryAsync(l => l.Id, cancellationToken);
            List<int> lotIds = lines.Where(l => l.LotId.HasValue).Select(l => l.LotId.Value).Distinct().ToList();
            Dictionary<int, InventoryLot> lots = await _uow.Set<InventoryLot>()
                .Where(l => lotIds.Contains(l.Id) && !l.Deleted).ToDictionaryAsync(l => l.Id, cancellationToken);

            var serialsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
            {
                SaveDocumentLineDto line = lines[i];
                string row = $"ردیف {i + 1}: ";
                if (!items.TryGetValue(line.ItemId, out InventoryItem item)) return ResponseDtoHelper.BadRequest<DocumentDto>(row + InventoryMessages.ItemNotFound);
                if (line.Quantity <= 0) return ResponseDtoHelper.BadRequest<DocumentDto>(row + "مقدار باید بیشتر از صفر باشد.");
                if (line.UnitPrice < 0) return ResponseDtoHelper.BadRequest<DocumentDto>(row + "فی نمی‌تواند منفی باشد.");
                if (line.LocationId.HasValue && (!locations.TryGetValue(line.LocationId.Value, out var loc) || loc.WarehouseId != dto.WarehouseId))
                    return ResponseDtoHelper.BadRequest<DocumentDto>(row + "محل انتخاب‌شده متعلق به این انبار نیست.");
                if (InventoryDocumentRules.IsTransfer(dto.Type))
                {
                    if (line.DestinationLocationId.HasValue && (!locations.TryGetValue(line.DestinationLocationId.Value, out var dest) || dest.WarehouseId != targetWarehouseId))
                        return ResponseDtoHelper.BadRequest<DocumentDto>(row + "محل مقصد متعلق به انبار مقصد نیست.");
                    if (dto.DestinationWarehouseId == dto.WarehouseId && line.LocationId == line.DestinationLocationId)
                        return ResponseDtoHelper.BadRequest<DocumentDto>(row + "مبدا و مقصد انتقال یکی است.");
                }
                else
                {
                    line.DestinationLocationId = null;
                }
                if (line.LotId.HasValue && (!lots.TryGetValue(line.LotId.Value, out var lot) || lot.ItemId != item.Id))
                    return ResponseDtoHelper.BadRequest<DocumentDto>(row + "بچ انتخاب‌شده متعلق به این کالا نیست.");

                line.SerialNumbers = (line.SerialNumbers ?? new List<string>()).Select(InventoryText.Clean).Where(s => s != null).ToList();
                if (item.TrackingMode == ItemTrackingMode.Serial)
                {
                    if (!InventoryText.IsWhole(line.Quantity)) return ResponseDtoHelper.BadRequest<DocumentDto>(row + "کالای سریالی با عدد صحیح جابه‌جا می‌شود.");
                    foreach (string serial in line.SerialNumbers)
                    {
                        if (!serialsSeen.Add(item.Id + "|" + serial))
                            return ResponseDtoHelper.BadRequest<DocumentDto>(row + $"سریال {serial} در این سند تکراری است.");
                    }
                }
                else
                {
                    line.SerialNumbers.Clear();
                }
            }

            InventoryDocument document;
            if (dto.Id.HasValue)
            {
                document = await Documents.Include(d => d.Lines).ThenInclude(l => l.Serials)
                    .FirstOrDefaultAsync(d => d.Id == dto.Id.Value && !d.Deleted, cancellationToken);
                if (document == null) return ResponseDtoHelper.NotFound<DocumentDto>(InventoryMessages.DocumentNotFound);
                if (document.Status != InventoryDocumentStatus.Draft) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.OnlyDraftEditable);
                _uow.Set<InventoryDocumentLine>().RemoveRange(document.Lines);
                document.Lines = new List<InventoryDocumentLine>();
            }
            else
            {
                document = new InventoryDocument
                {
                    Status = InventoryDocumentStatus.Draft,
                    CreateDate = DateTimeOffset.Now,
                    CreatorUserId = _user.UserId,
                    Lines = new List<InventoryDocumentLine>(),
                };
                Documents.Add(document);
            }

            document.Type = dto.Type;
            document.DocumentDate = dto.DocumentDate.Date;
            document.WarehouseId = dto.WarehouseId;
            document.DestinationWarehouseId = dto.DestinationWarehouseId;
            document.ProviderId = dto.ProviderId;
            document.BuyOrderId = dto.BuyOrderId;
            document.WorkOrderId = dto.WorkOrderId;
            document.ContractId = dto.ContractId;
            document.ReferenceNumber = InventoryText.Clean(dto.ReferenceNumber);
            document.Description = InventoryText.Clean(dto.Description);
            document.ModifyDate = DateTimeOffset.Now;
            document.ModifierUserId = _user.UserId;

            int rowNumber = 1;
            foreach (SaveDocumentLineDto line in lines)
            {
                document.Lines.Add(new InventoryDocumentLine
                {
                    RowNumber = rowNumber++,
                    ItemId = line.ItemId,
                    Quantity = line.Quantity,
                    LotId = line.LotId,
                    LotNumber = InventoryText.Clean(line.LotNumber),
                    HeatNumber = InventoryText.Clean(line.HeatNumber),
                    CertificateNumber = InventoryText.Clean(line.CertificateNumber),
                    LocationId = line.LocationId,
                    DestinationLocationId = line.DestinationLocationId,
                    UnitPrice = line.UnitPrice,
                    Description = InventoryText.Clean(line.Description),
                    Serials = line.SerialNumbers.Select(s => new InventoryDocumentLineSerial { SerialNumber = s }).ToList(),
                });
            }

            await _uow.SaveChangesAsync(cancellationToken);
            return await GetDocumentAsync(document.Id, cancellationToken);
        }

        private async Task<string> CheckReferencesAsync(SaveDocumentDto dto, CancellationToken cancellationToken)
        {
            if (dto.ProviderId.HasValue && !await _uow.Set<Provider>().AnyAsync(p => p.Id == dto.ProviderId.Value && !p.Deleted, cancellationToken))
                return "تامین‌کننده پیدا نشد.";
            if (dto.BuyOrderId.HasValue && !await _uow.Set<BuyOrder>().AnyAsync(b => b.Id == dto.BuyOrderId.Value, cancellationToken))
                return "سفارش خرید پیدا نشد.";
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && !w.Deleted, cancellationToken))
                return "دستور ساخت پیدا نشد.";
            if (dto.ContractId.HasValue && !await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId.Value && !c.Deleted, cancellationToken))
                return "قرارداد پیدا نشد.";
            return null;
        }

        public async Task<ResponseDto<bool>> DeleteDraftAsync(int id, CancellationToken cancellationToken)
        {
            InventoryDocument document = await Documents.Include(d => d.Lines).ThenInclude(l => l.Serials)
                .FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, cancellationToken);
            if (document == null) return ResponseDtoHelper.NotFound<bool>(InventoryMessages.DocumentNotFound);
            if (document.Status != InventoryDocumentStatus.Draft) return ResponseDtoHelper.BadRequest<bool>(InventoryMessages.OnlyDraftEditable);
            _uow.Set<InventoryDocumentLine>().RemoveRange(document.Lines);
            Documents.Remove(document);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true);
        }

        // ------------------------------------------------------------------------------- posting

        /// <summary>One change of stock: an item at a warehouse/location/lot, optionally one serial unit.</summary>
        private sealed class Move
        {
            public InventoryDocumentLine Line;
            public int ItemId;
            public int WarehouseId;
            public int? LocationId;
            public bool AnyLocation; // outgoing without a chosen location: taken from wherever there is stock
            public int? LotId;
            public InventoryLot NewLot;
            public SerialUnit Unit;
            public decimal Quantity;
            public decimal? UnitPrice;
        }

        private sealed class BalanceKey : IEquatable<BalanceKey>
        {
            public BalanceKey(int itemId, int warehouseId, int? locationId, int? lotId, InventoryLot newLot)
            {
                ItemId = itemId; WarehouseId = warehouseId; LocationId = locationId; LotId = newLot == null ? lotId : null; NewLot = newLot;
            }

            public int ItemId { get; }
            public int WarehouseId { get; }
            public int? LocationId { get; }
            public int? LotId { get; }
            public InventoryLot NewLot { get; }

            public bool Equals(BalanceKey other) =>
                other != null && ItemId == other.ItemId && WarehouseId == other.WarehouseId && LocationId == other.LocationId &&
                LotId == other.LotId && ReferenceEquals(NewLot, other.NewLot);

            public override bool Equals(object obj) => Equals(obj as BalanceKey);

            public override int GetHashCode() => HashCode.Combine(ItemId, WarehouseId, LocationId, LotId, NewLot);
        }

        /// <summary>Applies moves to the balances in memory and reports shortages.</summary>
        private sealed class BalanceBook
        {
            private readonly Dictionary<BalanceKey, StockBalance> _rows = new Dictionary<BalanceKey, StockBalance>();
            private readonly DbSet<StockBalance> _set;

            public BalanceBook(DbSet<StockBalance> set, IEnumerable<StockBalance> existing)
            {
                _set = set;
                foreach (StockBalance balance in existing)
                    _rows[new BalanceKey(balance.ItemId, balance.WarehouseId, balance.LocationId, balance.LotId, null)] = balance;
            }

            public IEnumerable<StockBalance> Rows => _rows.Values;

            public IEnumerable<StockBalance> Available(int itemId, int warehouseId, int? lotId) =>
                _rows.Where(r => r.Key.ItemId == itemId && r.Key.WarehouseId == warehouseId && r.Key.LotId == lotId && r.Key.NewLot == null && r.Value.Quantity > 0)
                     .OrderBy(r => r.Key.LocationId.HasValue ? 1 : 0).ThenBy(r => r.Key.LocationId).Select(r => r.Value);

            public StockBalance Apply(Move move)
            {
                var key = new BalanceKey(move.ItemId, move.WarehouseId, move.LocationId, move.LotId, move.NewLot);
                if (!_rows.TryGetValue(key, out StockBalance balance))
                {
                    balance = new StockBalance
                    {
                        ItemId = move.ItemId,
                        WarehouseId = move.WarehouseId,
                        LocationId = move.LocationId,
                        LotId = move.NewLot == null ? move.LotId : null,
                        Lot = move.NewLot,
                        Quantity = 0,
                    };
                    _set.Add(balance);
                    _rows[key] = balance;
                }
                balance.Quantity += move.Quantity;
                return balance;
            }
        }

        private async Task<BalanceBook> LoadBookAsync(IEnumerable<Move> moves, CancellationToken cancellationToken)
        {
            List<int> itemIds = moves.Select(m => m.ItemId).Distinct().ToList();
            List<int> warehouseIds = moves.Select(m => m.WarehouseId).Distinct().ToList();
            List<StockBalance> existing = await _uow.Set<StockBalance>()
                .Where(b => itemIds.Contains(b.ItemId) && warehouseIds.Contains(b.WarehouseId))
                .ToListAsync(cancellationToken);
            return new BalanceBook(_uow.Set<StockBalance>(), existing);
        }

        /// <summary>Spreads outgoing moves without a location over the locations that hold stock.</summary>
        private static string Allocate(List<Move> moves, BalanceBook book, Dictionary<int, InventoryItem> items)
        {
            var result = new List<Move>();
            // concrete moves first, so that an "any location" move sees what the others already took
            foreach (Move move in moves.Where(m => !m.AnyLocation)) { book.Apply(move); result.Add(move); }
            foreach (Move move in moves.Where(m => m.AnyLocation))
            {
                decimal needed = -move.Quantity;
                foreach (StockBalance balance in book.Available(move.ItemId, move.WarehouseId, move.LotId).ToList())
                {
                    if (needed <= 0) break;
                    decimal take = Math.Min(needed, balance.Quantity);
                    var part = new Move
                    {
                        Line = move.Line, ItemId = move.ItemId, WarehouseId = move.WarehouseId, LocationId = balance.LocationId,
                        LotId = move.LotId, Quantity = -take, UnitPrice = move.UnitPrice,
                    };
                    book.Apply(part);
                    result.Add(part);
                    needed -= take;
                }
                if (needed > 0)
                {
                    // nothing (enough) anywhere: post the rest without a location so the shortage check reports it
                    var rest = new Move
                    {
                        Line = move.Line, ItemId = move.ItemId, WarehouseId = move.WarehouseId, LocationId = null,
                        LotId = move.LotId, Quantity = -needed, UnitPrice = move.UnitPrice,
                    };
                    book.Apply(rest);
                    result.Add(rest);
                }
            }
            moves.Clear();
            moves.AddRange(result);

            // shortages
            var shortages = book.Rows.Where(b => b.Quantity < 0).ToList();
            if (shortages.Count == 0) return null;
            return "موجودی کافی نیست: " + string.Join("، ", shortages.Select(b =>
            {
                string code = items.TryGetValue(b.ItemId, out InventoryItem item) ? item.Code : b.ItemId.ToString();
                return $"کالای {code} (کمبود {InventoryText.Number(-b.Quantity)})";
            }));
        }

        private async Task<string> NextNumberAsync(InventoryDocumentType type, DateTime date, CancellationToken cancellationToken)
        {
            int year = InventoryText.PersianYear(date);
            DbSet<DocumentNumberSequence> sequences = _uow.Set<DocumentNumberSequence>();
            DocumentNumberSequence sequence = await sequences.FirstOrDefaultAsync(s => s.Type == (int)type && s.Year == year, cancellationToken);
            if (sequence == null)
            {
                sequence = new DocumentNumberSequence { Type = (int)type, Year = year, LastNumber = 0 };
                sequences.Add(sequence);
            }
            sequence.LastNumber++;
            return $"{InventoryDocumentRules.NumberPrefix(type)}-{year}-{sequence.LastNumber:00000}";
        }

        public async Task<ResponseDto<DocumentDto>> ConfirmAsync(int id, CancellationToken cancellationToken)
        {
            InventoryDocument document = await Documents
                .Include(d => d.Lines).ThenInclude(l => l.Serials)
                .FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, cancellationToken);
            if (document == null) return ResponseDtoHelper.NotFound<DocumentDto>(InventoryMessages.DocumentNotFound);
            if (document.Status != InventoryDocumentStatus.Draft) return ResponseDtoHelper.BadRequest<DocumentDto>("این سند قبلاً تایید یا باطل شده است.");
            if (document.Lines == null || document.Lines.Count == 0) return ResponseDtoHelper.BadRequest<DocumentDto>("سند ردیفی ندارد.");

            InventoryDocumentType type = document.Type;
            bool incoming = InventoryDocumentRules.IsIncoming(type);
            bool outgoing = InventoryDocumentRules.IsOutgoing(type);
            bool transfer = InventoryDocumentRules.IsTransfer(type);

            if (!await _uow.Set<Warehouse>().AnyAsync(w => w.Id == document.WarehouseId && !w.Deleted && w.IsActive, cancellationToken))
                return ResponseDtoHelper.BadRequest<DocumentDto>("انبار سند فعال نیست.");
            if (transfer && !await _uow.Set<Warehouse>().AnyAsync(w => w.Id == document.DestinationWarehouseId && !w.Deleted && w.IsActive, cancellationToken))
                return ResponseDtoHelper.BadRequest<DocumentDto>("انبار مقصد فعال نیست.");

            List<int> itemIds = document.Lines.Select(l => l.ItemId).Distinct().ToList();
            Dictionary<int, InventoryItem> items = await _uow.Set<InventoryItem>()
                .Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

            // serial units named on the document
            List<string> serialNumbers = document.Lines.SelectMany(l => l.Serials).Select(s => s.SerialNumber).Distinct().ToList();
            List<SerialUnit> knownUnits = serialNumbers.Count == 0
                ? new List<SerialUnit>()
                : await _uow.Set<SerialUnit>().Where(u => itemIds.Contains(u.ItemId) && serialNumbers.Contains(u.SerialNumber) && !u.Deleted).ToListAsync(cancellationToken);

            // lots typed on receipts
            List<InventoryLot> itemLots = await _uow.Set<InventoryLot>().Where(l => itemIds.Contains(l.ItemId) && !l.Deleted).ToListAsync(cancellationToken);
            var newLots = new Dictionary<string, InventoryLot>(StringComparer.OrdinalIgnoreCase);

            var moves = new List<Move>();
            var errors = new List<string>();
            DateTimeOffset now = DateTimeOffset.Now;

            foreach (InventoryDocumentLine line in document.Lines.OrderBy(l => l.RowNumber))
            {
                string row = $"ردیف {line.RowNumber}: ";
                if (!items.TryGetValue(line.ItemId, out InventoryItem item) || item.Deleted) { errors.Add(row + InventoryMessages.ItemNotFound); continue; }
                if (!item.IsActive) { errors.Add(row + $"کالای {item.Code} غیرفعال است."); continue; }

                // ----- lot
                int? lotId = null;
                InventoryLot newLot = null;
                if (item.TrackingMode == ItemTrackingMode.Lot)
                {
                    if (line.LotId.HasValue)
                    {
                        InventoryLot lot = itemLots.FirstOrDefault(l => l.Id == line.LotId.Value && l.ItemId == item.Id);
                        if (lot == null) { errors.Add(row + "بچ این ردیف پیدا نشد."); continue; }
                        lotId = lot.Id;
                    }
                    else if (incoming && line.LotNumber != null)
                    {
                        InventoryLot lot = itemLots.FirstOrDefault(l => string.Equals(l.LotNumber, line.LotNumber, StringComparison.OrdinalIgnoreCase));
                        if (lot != null)
                        {
                            lotId = lot.Id;
                        }
                        else
                        {
                            string key = item.Id + "|" + line.LotNumber;
                            if (!newLots.TryGetValue(key, out newLot))
                            {
                                newLot = new InventoryLot
                                {
                                    ItemId = item.Id,
                                    LotNumber = line.LotNumber,
                                    HeatNumber = line.HeatNumber,
                                    CertificateNumber = line.CertificateNumber,
                                    ProviderId = document.ProviderId,
                                    ProductionDate = type == InventoryDocumentType.ProductionReceipt ? document.DocumentDate : (DateTime?)null,
                                    CreateDate = now,
                                    CreatorUserId = _user.UserId,
                                    ModifyDate = now,
                                    ModifierUserId = _user.UserId,
                                };
                                _uow.Set<InventoryLot>().Add(newLot);
                                newLots[key] = newLot;
                            }
                        }
                    }
                    else
                    {
                        errors.Add(row + $"کالای {item.Code} با بچ ردیابی می‌شود؛ بچ (شماره ذوب) را مشخص کنید.");
                        continue;
                    }
                    if (newLot != null) line.Lot = newLot; else line.LotId = lotId;
                }
                else if (item.TrackingMode == ItemTrackingMode.None)
                {
                    line.LotId = null;
                }

                // ----- serial items: one move per piece
                if (item.TrackingMode == ItemTrackingMode.Serial)
                {
                    var serials = line.Serials?.ToList() ?? new List<InventoryDocumentLineSerial>();
                    if (!InventoryText.IsWhole(line.Quantity) || serials.Count != (int)line.Quantity)
                    {
                        errors.Add(row + $"کالای {item.Code} سریالی است؛ تعداد سریال‌ها ({serials.Count}) باید برابر مقدار ({InventoryText.Number(line.Quantity)}) باشد.");
                        continue;
                    }

                    // a serial item may also belong to a lot (for example blades of one heat)
                    int? serialLotId = line.LotId;
                    InventoryLot serialNewLot = null;
                    if (incoming && !serialLotId.HasValue && line.LotNumber != null)
                    {
                        InventoryLot lot = itemLots.FirstOrDefault(l => string.Equals(l.LotNumber, line.LotNumber, StringComparison.OrdinalIgnoreCase));
                        if (lot != null) serialLotId = lot.Id;
                        else
                        {
                            string key = item.Id + "|" + line.LotNumber;
                            if (!newLots.TryGetValue(key, out serialNewLot))
                            {
                                serialNewLot = new InventoryLot
                                {
                                    ItemId = item.Id, LotNumber = line.LotNumber, HeatNumber = line.HeatNumber,
                                    CertificateNumber = line.CertificateNumber, ProviderId = document.ProviderId,
                                    CreateDate = now, CreatorUserId = _user.UserId, ModifyDate = now, ModifierUserId = _user.UserId,
                                };
                                _uow.Set<InventoryLot>().Add(serialNewLot);
                                newLots[key] = serialNewLot;
                            }
                        }
                        if (serialNewLot != null) line.Lot = serialNewLot; else line.LotId = serialLotId;
                    }

                    foreach (InventoryDocumentLineSerial serialRow in serials)
                    {
                        SerialUnit unit = knownUnits.FirstOrDefault(u => u.ItemId == item.Id &&
                            string.Equals(u.SerialNumber, serialRow.SerialNumber, StringComparison.OrdinalIgnoreCase));
                        string label = $"سریال {serialRow.SerialNumber} ({item.Code})";

                        if (incoming)
                        {
                            if (unit == null)
                            {
                                if (type == InventoryDocumentType.ProductionReturn)
                                {
                                    errors.Add(row + label + " ثبت نشده است؛ برگشت از تولید فقط برای قطعه‌ای است که قبلاً حواله شده.");
                                    continue;
                                }
                                unit = new SerialUnit
                                {
                                    ItemId = item.Id,
                                    SerialNumber = serialRow.SerialNumber,
                                    WorkOrderId = document.WorkOrderId,
                                    CreateDate = now,
                                    CreatorUserId = _user.UserId,
                                };
                                _uow.Set<SerialUnit>().Add(unit);
                                knownUnits.Add(unit);
                            }
                            else if (unit.Status == SerialUnitStatus.InStock || unit.Status == SerialUnitStatus.Installed)
                            {
                                errors.Add(row + label + (unit.Status == SerialUnitStatus.InStock ? " همین الان در انبار است." : " داخل یک مجموعه نصب شده است."));
                                continue;
                            }
                            else if (unit.Status == SerialUnitStatus.Scrapped && type != InventoryDocumentType.AdjustmentIncrease)
                            {
                                errors.Add(row + label + " ضایعات اعلام شده است.");
                                continue;
                            }

                            unit.Status = SerialUnitStatus.InStock;
                            unit.WarehouseId = document.WarehouseId;
                            unit.LocationId = line.LocationId;
                            if (serialNewLot != null) unit.Lot = serialNewLot;
                            else if (serialLotId.HasValue) unit.LotId = serialLotId;
                            unit.ModifyDate = now;
                            unit.ModifierUserId = _user.UserId;
                            serialRow.SerialUnit = unit;

                            moves.Add(new Move
                            {
                                Line = line, ItemId = item.Id, WarehouseId = document.WarehouseId, LocationId = line.LocationId,
                                LotId = unit.LotId, NewLot = unit.Lot != null && unit.Lot.Id == 0 ? unit.Lot : null,
                                Unit = unit, Quantity = 1, UnitPrice = line.UnitPrice,
                            });
                        }
                        else
                        {
                            if (unit == null) { errors.Add(row + label + " پیدا نشد."); continue; }
                            if (unit.Status != SerialUnitStatus.InStock || unit.WarehouseId != document.WarehouseId)
                            {
                                errors.Add(row + label + " در این انبار موجود نیست.");
                                continue;
                            }
                            if (line.LocationId.HasValue && unit.LocationId != line.LocationId)
                            {
                                errors.Add(row + label + " در محل انتخاب‌شده نیست.");
                                continue;
                            }

                            moves.Add(new Move
                            {
                                Line = line, ItemId = item.Id, WarehouseId = document.WarehouseId, LocationId = unit.LocationId,
                                LotId = unit.LotId, Unit = unit, Quantity = -1, UnitPrice = line.UnitPrice,
                            });

                            if (transfer)
                            {
                                moves.Add(new Move
                                {
                                    Line = line, ItemId = item.Id, WarehouseId = document.DestinationWarehouseId.Value,
                                    LocationId = line.DestinationLocationId, LotId = unit.LotId, Unit = unit, Quantity = 1, UnitPrice = line.UnitPrice,
                                });
                                unit.WarehouseId = document.DestinationWarehouseId;
                                unit.LocationId = line.DestinationLocationId;
                            }
                            else
                            {
                                unit.Status = type == InventoryDocumentType.Issue ? SerialUnitStatus.InProduction
                                    : type == InventoryDocumentType.SupplierReturn ? SerialUnitStatus.ReturnedToSupplier
                                    : SerialUnitStatus.Scrapped;
                                unit.WarehouseId = null;
                                unit.LocationId = null;
                                if (type == InventoryDocumentType.Issue && document.WorkOrderId.HasValue) unit.WorkOrderId = document.WorkOrderId;
                            }
                            unit.ModifyDate = now;
                            unit.ModifierUserId = _user.UserId;
                            serialRow.SerialUnit = unit;
                        }
                    }
                    continue;
                }

                // ----- quantity (and lot) items
                if (incoming)
                {
                    moves.Add(new Move
                    {
                        Line = line, ItemId = item.Id, WarehouseId = document.WarehouseId, LocationId = line.LocationId,
                        LotId = lotId, NewLot = newLot, Quantity = line.Quantity, UnitPrice = line.UnitPrice,
                    });
                }
                else
                {
                    moves.Add(new Move
                    {
                        Line = line, ItemId = item.Id, WarehouseId = document.WarehouseId, LocationId = line.LocationId,
                        AnyLocation = !line.LocationId.HasValue, LotId = lotId, Quantity = -line.Quantity, UnitPrice = line.UnitPrice,
                    });
                    if (transfer)
                    {
                        moves.Add(new Move
                        {
                            Line = line, ItemId = item.Id, WarehouseId = document.DestinationWarehouseId.Value,
                            LocationId = line.DestinationLocationId, LotId = lotId, Quantity = line.Quantity, UnitPrice = line.UnitPrice,
                        });
                    }
                }
            }

            if (errors.Count > 0) return ResponseDtoHelper.BadRequest<DocumentDto>(string.Join(" | ", errors.Take(20)));

            BalanceBook book = await LoadBookAsync(moves, cancellationToken);
            string shortage = Allocate(moves, book, items);
            if (shortage != null) return ResponseDtoHelper.BadRequest<DocumentDto>(shortage);

            foreach (Move move in moves)
            {
                _uow.Set<StockTransaction>().Add(new StockTransaction
                {
                    ItemId = move.ItemId,
                    WarehouseId = move.WarehouseId,
                    LocationId = move.LocationId,
                    LotId = move.NewLot == null ? move.LotId : null,
                    Lot = move.NewLot,
                    SerialUnit = move.Unit,
                    Quantity = move.Quantity,
                    UnitPrice = move.UnitPrice,
                    TransactionDate = document.DocumentDate,
                    Document = document,
                    DocumentLineId = move.Line.Id,
                    CreatedAt = now,
                    CreatedByUserId = _user.UserId,
                });
            }

            document.Number = await NextNumberAsync(type, document.DocumentDate, cancellationToken);
            document.Status = InventoryDocumentStatus.Confirmed;
            document.ConfirmedAt = now;
            document.ConfirmedByUserId = _user.UserId;
            document.SepidarStatus = SepidarSyncStatus.Pending;
            document.ModifyDate = now;
            document.ModifierUserId = _user.UserId;

            string saveError = await SaveAsync(cancellationToken);
            if (saveError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(saveError);
            return await GetDocumentAsync(document.Id, cancellationToken);
        }

        private async Task<string> SaveAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
                return null;
            }
            catch (DbUpdateConcurrencyException)
            {
                return InventoryMessages.ConcurrencyConflict;
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("duplicate key") == true ||
                                               ex.InnerException?.Message?.Contains("UNIQUE") == true)
            {
                return "یک شماره سریال، بچ یا شماره سند تکراری است، یا کاربر دیگری هم‌زمان همین کار را انجام داد. دوباره تلاش کنید.";
            }
        }

        public async Task<ResponseDto<DocumentDto>> CancelAsync(int id, CancelDocumentDto dto, CancellationToken cancellationToken)
        {
            string reason = InventoryText.Clean(dto?.Reason);
            if (reason == null) return ResponseDtoHelper.BadRequest<DocumentDto>("علت ابطال را بنویسید.");

            InventoryDocument document = await Documents.FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, cancellationToken);
            if (document == null) return ResponseDtoHelper.NotFound<DocumentDto>(InventoryMessages.DocumentNotFound);
            if (document.Status != InventoryDocumentStatus.Confirmed) return ResponseDtoHelper.BadRequest<DocumentDto>("فقط سند تاییدشده باطل می‌شود. سند پیش‌نویس را حذف کنید.");

            List<StockTransaction> transactions = await _uow.Set<StockTransaction>()
                .Where(t => t.DocumentId == id && !t.IsReversal).ToListAsync(cancellationToken);
            int lastTransactionId = transactions.Count == 0 ? 0 : transactions.Max(t => t.Id);
            List<int> itemIds = transactions.Select(t => t.ItemId).Distinct().ToList();
            Dictionary<int, InventoryItem> items = await _uow.Set<InventoryItem>().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            var errors = new List<string>();

            // serial units: only if nothing happened to them after this document
            List<int> unitIds = transactions.Where(t => t.SerialUnitId.HasValue).Select(t => t.SerialUnitId.Value).Distinct().ToList();
            if (unitIds.Count > 0)
            {
                List<SerialUnit> units = await _uow.Set<SerialUnit>().Where(u => unitIds.Contains(u.Id)).ToListAsync(cancellationToken);
                List<StockTransaction> unitHistory = await _uow.Set<StockTransaction>()
                    .Where(t => t.SerialUnitId.HasValue && unitIds.Contains(t.SerialUnitId.Value))
                    .Include(t => t.Document).ToListAsync(cancellationToken);
                List<int> usedInAssemblies = await _uow.Set<AssemblyLink>()
                    .Where(a => a.ChildSerialUnitId.HasValue && unitIds.Contains(a.ChildSerialUnitId.Value) && a.RemovedAt == null)
                    .Select(a => a.ChildSerialUnitId.Value).ToListAsync(cancellationToken);

                foreach (SerialUnit unit in units)
                {
                    string label = $"سریال {unit.SerialNumber}";
                    if (unitHistory.Any(t => t.SerialUnitId == unit.Id && t.Id > lastTransactionId && t.DocumentId != id))
                    { errors.Add(label + " بعد از این سند در سند دیگری جابه‌جا شده است."); continue; }
                    if (usedInAssemblies.Contains(unit.Id))
                    { errors.Add(label + " در یک مجموعه نصب شده است؛ اول آن را از مجموعه جدا کنید."); continue; }

                    List<StockTransaction> mine = transactions.Where(t => t.SerialUnitId == unit.Id).OrderBy(t => t.Id).ToList();
                    StockTransaction firstOfMine = mine.First();
                    if (InventoryDocumentRules.IsIncoming(document.Type))
                    {
                        StockTransaction before = unitHistory.Where(t => t.SerialUnitId == unit.Id && t.Id < firstOfMine.Id)
                            .OrderByDescending(t => t.Id).FirstOrDefault();
                        bool hasTraceRecords = await _uow.Set<PartOperation>().AnyAsync(o => o.SerialUnitId == unit.Id && !o.Deleted, cancellationToken)
                                               || await _uow.Set<AssemblyLink>().AnyAsync(a => a.ParentSerialUnitId == unit.Id, cancellationToken);
                        if (before == null && !hasTraceRecords)
                        {
                            unit.Delete(); // created by this receipt
                        }
                        else if (before != null && before.Quantity < 0 && before.Document.Type == InventoryDocumentType.SupplierReturn)
                        {
                            unit.Status = SerialUnitStatus.ReturnedToSupplier;
                        }
                        else if (before != null && before.Quantity < 0 && before.Document.Type == InventoryDocumentType.AdjustmentDecrease)
                        {
                            unit.Status = SerialUnitStatus.Scrapped;
                        }
                        else
                        {
                            unit.Status = SerialUnitStatus.InProduction;
                        }
                        unit.WarehouseId = null;
                        unit.LocationId = null;
                    }
                    else
                    {
                        // back to where it was taken from
                        StockTransaction taken = mine.First(t => t.Quantity < 0);
                        unit.Status = SerialUnitStatus.InStock;
                        unit.WarehouseId = taken.WarehouseId;
                        unit.LocationId = taken.LocationId;
                    }
                    unit.ModifyDate = now;
                    unit.ModifierUserId = _user.UserId;
                }
            }
            if (errors.Count > 0) return ResponseDtoHelper.BadRequest<DocumentDto>("این سند باطل نمی‌شود: " + string.Join(" | ", errors.Take(20)));

            var moves = transactions.Select(t => new Move
            {
                ItemId = t.ItemId, WarehouseId = t.WarehouseId, LocationId = t.LocationId, LotId = t.LotId,
                Quantity = -t.Quantity, UnitPrice = t.UnitPrice, Line = null,
            }).ToList();
            BalanceBook book = await LoadBookAsync(moves, cancellationToken);
            foreach (Move move in moves) book.Apply(move);
            var shortages = book.Rows.Where(b => b.Quantity < 0).ToList();
            if (shortages.Count > 0)
                return ResponseDtoHelper.BadRequest<DocumentDto>("این سند باطل نمی‌شود چون کالای آن مصرف یا جابه‌جا شده است: " + string.Join("، ",
                    shortages.Select(b => items.TryGetValue(b.ItemId, out InventoryItem item) ? item.Code : b.ItemId.ToString())));

            foreach (StockTransaction t in transactions)
            {
                _uow.Set<StockTransaction>().Add(new StockTransaction
                {
                    ItemId = t.ItemId, WarehouseId = t.WarehouseId, LocationId = t.LocationId, LotId = t.LotId,
                    SerialUnitId = t.SerialUnitId, Quantity = -t.Quantity, UnitPrice = t.UnitPrice,
                    TransactionDate = document.DocumentDate, DocumentId = document.Id, DocumentLineId = t.DocumentLineId,
                    IsReversal = true, CreatedAt = now, CreatedByUserId = _user.UserId,
                });
            }

            document.Status = InventoryDocumentStatus.Cancelled;
            document.CancelledAt = now;
            document.CancelledByUserId = _user.UserId;
            document.CancelReason = reason;
            // already in Sepidar: send the reversal; otherwise nothing to send
            document.SepidarStatus = document.SepidarStatus == SepidarSyncStatus.Exported || document.SepidarStatus == SepidarSyncStatus.Sent
                ? SepidarSyncStatus.Pending
                : SepidarSyncStatus.NotRequired;
            document.ModifyDate = now;
            document.ModifierUserId = _user.UserId;

            string saveError = await SaveAsync(cancellationToken);
            if (saveError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(saveError);
            return await GetDocumentAsync(document.Id, cancellationToken);
        }
    }
}
