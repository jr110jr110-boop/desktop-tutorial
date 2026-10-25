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

        /// <summary>Approval of a draft: adjustments (warehouse supervisor + planning) or the QC loading approval of a delivery.</summary>
        Task<ResponseDto<DocumentDto>> ApproveAsync(int id, ApproveDocumentDto dto, CancellationToken cancellationToken);

        /// <summary>The guard checks goods leaving the premises against the confirmed document.</summary>
        Task<ResponseDto<DocumentDto>> ExitCheckAsync(int id, ExitCheckDto dto, CancellationToken cancellationToken);

        /// <summary>Documents made by quality control (release from quarantine, NCR dispositions) and by stock counts.</summary>
        Task<ResponseDto<DocumentDto>> CreateSystemDocumentAsync(SaveDocumentDto dto, SystemDocumentLinks links, bool confirm, CancellationToken cancellationToken);
    }

    /// <summary>What a system-made document belongs to.</summary>
    public class SystemDocumentLinks
    {
        public int? NcrId { get; set; }
        public int? InspectionId { get; set; }
        public int? StockCountId { get; set; }
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

        private readonly Finance.ICostingService _costing;
        private readonly Procurement.IProcurementService _procurement;
        private readonly Procurement.IPayablesService _payables;
        private readonly Quality.IReleaseService _release;

        public InventoryDocumentService(IUnitOfWork uow, IInventoryUserContext user, Finance.ICostingService costing,
            Procurement.IProcurementService procurement, Procurement.IPayablesService payables, Quality.IReleaseService release)
        {
            _release = release ?? throw new ArgumentNullException(nameof(release));
            _procurement = procurement ?? throw new ArgumentNullException(nameof(procurement));
            _payables = payables ?? throw new ArgumentNullException(nameof(payables));
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
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
                    PurchaseOrderId = d.PurchaseOrderId,
                    PurchaseOrderNumber = d.PurchaseOrder.Number,
                    WorkOrderId = d.WorkOrderId,
                    WorkOrderCode = d.WorkOrder.Code,
                    ContractId = d.ContractId,
                    ContractCode = d.Contract.Code,
                    SalesOrderId = d.SalesOrderId,
                    ServiceOrderId = d.ServiceOrderId,
                    ReferenceNumber = d.ReferenceNumber,
                    Description = d.Description,
                    CreateDate = d.CreateDate,
                    ConfirmedAt = d.ConfirmedAt,
                    CancelledAt = d.CancelledAt,
                    CancelReason = d.CancelReason,
                    SepidarStatus = d.SepidarStatus,
                    SepidarReference = d.SepidarReference,
                    WarehouseKind = d.Warehouse.Kind,
                    DestinationWarehouseKind = d.DestinationWarehouse != null ? d.DestinationWarehouse.Kind : (WarehouseKind?)null,
                    MaterialRequestId = d.MaterialRequestId,
                    MaterialRequestNumber = d.MaterialRequest.Number,
                    NcrId = d.NcrId,
                    NcrNumber = d.Ncr.Number,
                    InspectionId = d.InspectionId,
                    StockCountId = d.StockCountId,
                    DriverName = d.DriverName,
                    VehiclePlate = d.VehiclePlate,
                    ReceiverName = d.ReceiverName,
                    Destination = d.Destination,
                    ExitCheckedAt = d.ExitCheckedAt,
                    ExitCheckNote = d.ExitCheckNote,
                    QcLoadingApprovedAt = d.QcLoadingApprovedAt,
                    BuyerName = d.BuyerName,
                    MinutesNumber = d.MinutesNumber,
                    WeighbridgeDocumentId = d.WeighbridgeDocumentId,
                    DepositReference = d.DepositReference,
                    CostCenter = d.CostCenter,
                    PriceQuotes = d.PriceQuotes,
                    FinanceApprovedAt = d.FinanceApprovedAt,
                    WarehouseApprovedAt = d.WarehouseApprovedAt,
                    PlanningApprovedAt = d.PlanningApprovedAt,
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
                        PurchaseOrderLineId = l.PurchaseOrderLineId,
                        SalesOrderLineId = l.SalesOrderLineId,
                        Description = l.Description,
                        Direction = l.Direction,
                        SourceRowNumber = l.SourceRowNumber,
                        SerialNumbers = l.Serials.OrderBy(s => s.Id).Select(s => s.SerialNumber).ToList(),
                    }).ToList(),
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (document == null) return ResponseDtoHelper.NotFound<DocumentDto>(InventoryMessages.DocumentNotFound);

            // names of the users who created, confirmed or cancelled the document
            var raw = await Documents.Where(d => d.Id == id)
                .Select(d => new
                {
                    d.CreatorUserId, d.ConfirmedByUserId, d.CancelledByUserId, d.ExitCheckedByUserId, d.QcLoadingApprovedByUserId,
                    d.WarehouseApprovedByUserId, d.PlanningApprovedByUserId, d.FinanceApprovedByUserId,
                }).FirstAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[]
            {
                raw.CreatorUserId, raw.ConfirmedByUserId, raw.CancelledByUserId, raw.ExitCheckedByUserId, raw.QcLoadingApprovedByUserId,
                raw.WarehouseApprovedByUserId, raw.PlanningApprovedByUserId, raw.FinanceApprovedByUserId,
            }, cancellationToken);
            if (document.SalesOrderId.HasValue)
                document.SalesOrderNumber = await _uow.Set<Entities.Model.NasbNiro.Sales.SlSalesOrder>().Where(o => o.Id == document.SalesOrderId.Value).Select(o => o.Number).FirstOrDefaultAsync(cancellationToken);
            if (document.ServiceOrderId.HasValue)
                document.ServiceOrderNumber = await _uow.Set<Entities.Model.NasbNiro.AfterSales.SvServiceOrder>().Where(o => o.Id == document.ServiceOrderId.Value).Select(o => o.Number).FirstOrDefaultAsync(cancellationToken);
            document.CreatorName = names.Name(raw.CreatorUserId);
            document.ConfirmedByName = names.Name(raw.ConfirmedByUserId);
            document.CancelledByName = names.Name(raw.CancelledByUserId);
            document.ExitCheckedByName = names.Name(raw.ExitCheckedByUserId);
            document.QcLoadingApprovedByName = names.Name(raw.QcLoadingApprovedByUserId);
            document.WarehouseApprovedByName = names.Name(raw.WarehouseApprovedByUserId);
            document.PlanningApprovedByName = names.Name(raw.PlanningApprovedByUserId);
            document.FinanceApprovedByName = names.Name(raw.FinanceApprovedByUserId);
            return ResponseDtoHelper.Ok(document);
        }

        // -------------------------------------------------------------------------------- drafts

        public Task<ResponseDto<DocumentDto>> SaveDraftAsync(SaveDocumentDto dto, CancellationToken cancellationToken) =>
            SaveDraftCoreAsync(dto, null, cancellationToken);

        public async Task<ResponseDto<DocumentDto>> CreateSystemDocumentAsync(SaveDocumentDto dto, SystemDocumentLinks links, bool confirm, CancellationToken cancellationToken)
        {
            if (dto == null || links == null) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.InvalidData);
            dto.Id = null;
            ResponseDto<DocumentDto> saved = await SaveDraftCoreAsync(dto, links, cancellationToken);
            if (!saved.Success || !confirm) return saved;
            return await ConfirmAsync(saved.Data.Id, cancellationToken);
        }

        private async Task<ResponseDto<DocumentDto>> SaveDraftCoreAsync(SaveDocumentDto dto, SystemDocumentLinks system, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.InvalidData);
            if (!InventoryDocumentRules.IsKnown(dto.Type)) return ResponseDtoHelper.BadRequest<DocumentDto>("نوع سند نامعتبر است.");
            if (dto.DocumentDate == default) return ResponseDtoHelper.BadRequest<DocumentDto>("تاریخ سند را وارد کنید.");

            // a document made by quality control keeps its lines; only the exit data may be completed
            InventoryDocument existing = null;
            if (dto.Id.HasValue)
            {
                existing = await Documents.Include(d => d.Lines).ThenInclude(l => l.Serials)
                    .FirstOrDefaultAsync(d => d.Id == dto.Id.Value && !d.Deleted, cancellationToken);
                if (existing == null) return ResponseDtoHelper.NotFound<DocumentDto>(InventoryMessages.DocumentNotFound);
                if (existing.Status != InventoryDocumentStatus.Draft) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.OnlyDraftEditable);
                if (existing.NcrId.HasValue || existing.InspectionId.HasValue || existing.StockCountId.HasValue)
                    return await SaveSystemDraftHeaderAsync(existing, dto, cancellationToken);
            }
            if (system == null && InventoryDocumentRules.IsSystemOnly(dto.Type))
                return ResponseDtoHelper.BadRequest<DocumentDto>("سند ترخیص از قرنطینه فقط از نتیجه بازرسی کنترل کیفیت یا NCR ساخته می‌شود.");

            Warehouse warehouse = await _uow.Set<Warehouse>().FirstOrDefaultAsync(w => w.Id == dto.WarehouseId && !w.Deleted, cancellationToken);
            if (warehouse == null) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.WarehouseNotFound);

            bool conversion = InventoryDocumentRules.IsConversion(dto.Type);
            int targetWarehouseId = dto.WarehouseId;
            Warehouse destination = null;
            bool keepsDestination = InventoryDocumentRules.IsTransfer(dto.Type) || conversion ||
                                    (InventoryDocumentRules.IsIncoming(dto.Type) && warehouse.Kind == WarehouseKind.Quarantine);
            if (InventoryDocumentRules.IsTransfer(dto.Type) && !dto.DestinationWarehouseId.HasValue)
                return ResponseDtoHelper.BadRequest<DocumentDto>("انبار مقصد انتقال را انتخاب کنید.");
            if (keepsDestination && dto.DestinationWarehouseId.HasValue)
            {
                destination = await _uow.Set<Warehouse>().FirstOrDefaultAsync(w => w.Id == dto.DestinationWarehouseId.Value && !w.Deleted, cancellationToken);
                if (destination == null) return ResponseDtoHelper.BadRequest<DocumentDto>("انبار مقصد پیدا نشد.");
                if (InventoryDocumentRules.IsTransfer(dto.Type) || conversion) targetWarehouseId = destination.Id;
            }
            else
            {
                dto.DestinationWarehouseId = null;
            }

            string ruleError = CheckWarehouseRules(dto, warehouse, destination, system);
            if (ruleError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(ruleError);

            string referenceError = await CheckReferencesAsync(dto, cancellationToken);
            if (referenceError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(referenceError);

            List<SaveDocumentLineDto> lines = (dto.Lines ?? new List<SaveDocumentLineDto>()).Where(l => l != null).ToList();
            if (lines.Count == 0) return ResponseDtoHelper.BadRequest<DocumentDto>("سند حداقل یک ردیف لازم دارد.");

            string conversionError = CheckConversionLines(dto.Type, lines);
            if (conversionError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(conversionError);

            // a delivery against a sales order: the order, its lines and the open quantities
            string salesError = await Sales.SlFulfilment.CheckDraftAsync(_uow, dto, lines, cancellationToken);
            if (salesError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(salesError);

            // parts of an after-sales service order: the order must be open; an issue is charged to the after-sales cost centre of the order
            string serviceError = await AfterSales.SvDocumentLink.CheckDraftAsync(_uow, dto, cancellationToken);
            if (serviceError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(serviceError);

            MaterialRequest request = null;
            if (dto.Type == InventoryDocumentType.Issue)
            {
                if (!dto.MaterialRequestId.HasValue)
                    return ResponseDtoHelper.BadRequest<DocumentDto>("حواله فقط بر اساس درخواست کالای تاییدشده صادر می‌شود؛ درخواست را انتخاب کنید.");
                request = await _uow.Set<MaterialRequest>().Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.Id == dto.MaterialRequestId.Value && !r.Deleted, cancellationToken);
                string requestError = CheckRequest(request, lines.Select(l => (l.ItemId, l.Quantity)), dto.Id);
                if (requestError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(requestError);
                if (dto.ServiceOrderId.HasValue && request.WorkOrderId.HasValue) return ResponseDtoHelper.BadRequest<DocumentDto>("قطعات دستور خدمات با درخواست کالای بدون دستور کار (مصرف عمومی) صادر می‌شود؛ هزینه آن به مرکز هزینه پس از فروش می‌رود.");
                dto.WorkOrderId = request.WorkOrderId;
            }
            else
            {
                dto.MaterialRequestId = null;
            }

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
                bool inLine = conversion && line.Direction == DocumentLineDirection.In;
                int lineWarehouseId = inLine ? targetWarehouseId : dto.WarehouseId;
                if (line.LocationId.HasValue && (!locations.TryGetValue(line.LocationId.Value, out var loc) || loc.WarehouseId != lineWarehouseId))
                    return ResponseDtoHelper.BadRequest<DocumentDto>(row + "محل انتخاب‌شده متعلق به این انبار نیست.");
                if (!conversion) line.Direction = DocumentLineDirection.ByType;
                if (!inLine) line.SourceRowNumber = null;
                if (dto.Type == InventoryDocumentType.PurchaseReceipt && item.RequiresIncomingInspection && warehouse.Kind != WarehouseKind.Quarantine)
                    return ResponseDtoHelper.BadRequest<DocumentDto>(row + $"کالای {item.Code} بازرسی ورودی لازم دارد؛ رسید خرید آن در انبار قرنطینه ثبت می‌شود.");
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
            if (existing != null)
            {
                document = existing;
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
                    NcrId = system?.NcrId,
                    InspectionId = system?.InspectionId,
                    StockCountId = system?.StockCountId,
                };
                Documents.Add(document);
            }

            document.Type = dto.Type;
            document.DocumentDate = dto.DocumentDate.Date;
            document.WarehouseId = dto.WarehouseId;
            document.DestinationWarehouseId = dto.DestinationWarehouseId;
            document.ProviderId = dto.ProviderId;
            document.BuyOrderId = dto.BuyOrderId;
            document.PurchaseOrderId = InventoryDocumentRules.UsesPurchaseOrder(dto.Type) ? dto.PurchaseOrderId : null;
            document.WorkOrderId = dto.WorkOrderId;
            document.ContractId = dto.ContractId;
            document.SalesOrderId = dto.Type == InventoryDocumentType.CustomerDelivery ? dto.SalesOrderId : null;
            document.ServiceOrderId = dto.Type == InventoryDocumentType.CustomerDelivery || dto.Type == InventoryDocumentType.Issue ? dto.ServiceOrderId : null;
            document.ReferenceNumber = InventoryText.Clean(dto.ReferenceNumber);
            document.Description = InventoryText.Clean(dto.Description);
            document.MaterialRequestId = dto.MaterialRequestId;
            SetHeaderExtras(document, dto);
            ClearApprovals(document);
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
                    PurchaseOrderLineId = InventoryDocumentRules.UsesPurchaseOrder(dto.Type) ? line.PurchaseOrderLineId : null,
                    SalesOrderLineId = dto.Type == InventoryDocumentType.CustomerDelivery ? line.SalesOrderLineId : null,
                    Description = InventoryText.Clean(line.Description),
                    Direction = line.Direction,
                    SourceRowNumber = line.SourceRowNumber,
                    Serials = line.SerialNumbers.Select(s => new InventoryDocumentLineSerial { SerialNumber = s }).ToList(),
                });
            }

            await _uow.SaveChangesAsync(cancellationToken);
            return await GetDocumentAsync(document.Id, cancellationToken);
        }

        private async Task<ResponseDto<DocumentDto>> SaveSystemDraftHeaderAsync(InventoryDocument document, SaveDocumentDto dto, CancellationToken cancellationToken)
        {
            document.DocumentDate = dto.DocumentDate.Date;
            document.ReferenceNumber = InventoryText.Clean(dto.ReferenceNumber);
            document.Description = InventoryText.Clean(dto.Description);
            SetHeaderExtras(document, dto);
            document.ModifyDate = DateTimeOffset.Now;
            document.ModifierUserId = _user.UserId;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetDocumentAsync(document.Id, cancellationToken);
        }

        private static void SetHeaderExtras(InventoryDocument document, SaveDocumentDto dto)
        {
            document.DriverName = InventoryText.Clean(dto.DriverName);
            document.VehiclePlate = InventoryText.Clean(dto.VehiclePlate);
            document.ReceiverName = InventoryText.Clean(dto.ReceiverName);
            document.Destination = InventoryText.Clean(dto.Destination);
            document.BuyerName = InventoryText.Clean(dto.BuyerName);
            document.MinutesNumber = InventoryText.Clean(dto.MinutesNumber);
            document.WeighbridgeDocumentId = dto.WeighbridgeDocumentId;
            document.DepositReference = InventoryText.Clean(dto.DepositReference);
            document.PriceQuotes = InventoryText.Clean(dto.PriceQuotes);
            document.CostCenter = dto.Type == InventoryDocumentType.Issue ? InventoryText.Clean(dto.CostCenter) : null;
        }

        /// <summary>Number of offers in the price inquiry text (one per line or separated by "|").</summary>
        internal static int ScrapQuoteCount(string quotes) =>
            string.IsNullOrWhiteSpace(quotes) ? 0 : quotes.Split(new[] { '|', '\n', '؛' }, StringSplitOptions.RemoveEmptyEntries).Count(q => q.Trim().Length > 0);

        /// <summary>Any change to a draft cancels the approvals given to it.</summary>
        private static void ClearApprovals(InventoryDocument document)
        {
            document.WarehouseApprovedAt = null;
            document.WarehouseApprovedByUserId = null;
            document.PlanningApprovedAt = null;
            document.PlanningApprovedByUserId = null;
            document.QcLoadingApprovedAt = null;
            document.QcLoadingApprovedByUserId = null;
            document.FinanceApprovedAt = null;
            document.FinanceApprovedByUserId = null;
        }

        /// <summary>Which warehouse kinds each document type may use (quarantine, scrap, contractor, customer property).</summary>
        private static string CheckWarehouseRules(SaveDocumentDto dto, Warehouse source, Warehouse destination, SystemDocumentLinks system)
        {
            InventoryDocumentType type = dto.Type;
            bool fromQc = system?.NcrId != null || system?.InspectionId != null;
            bool takesOut = InventoryDocumentRules.IsOutgoing(type) || InventoryDocumentRules.IsTransfer(type) || InventoryDocumentRules.IsConversion(type);
            bool adjustment = InventoryDocumentRules.NeedsApprovals(type);

            if (source.Kind == WarehouseKind.Quarantine && takesOut && !fromQc && !adjustment)
                return "کالای انبار قرنطینه فقط با نتیجه بازرسی کنترل کیفیت یا تصمیم NCR خارج می‌شود.";
            if (source.Kind == WarehouseKind.Scrap && takesOut && type != InventoryDocumentType.ScrapSale && !adjustment)
                return "از انبار ضایعات فقط با سند فروش ضایعات خارج می‌شود.";
            if ((type == InventoryDocumentType.ScrapReceipt || type == InventoryDocumentType.ScrapSale) && source.Kind != WarehouseKind.Scrap)
                return "رسید و فروش ضایعات فقط در انبار ضایعات ثبت می‌شود.";
            if (source.Kind == WarehouseKind.Scrap && InventoryDocumentRules.IsIncoming(type) && type != InventoryDocumentType.ScrapReceipt && !adjustment)
                return "ورود به انبار ضایعات با رسید ضایعات یا تصمیم NCR است.";
            if (destination != null && destination.Kind == WarehouseKind.Scrap && !fromQc)
                return "انتقال به انبار ضایعات فقط با تصمیم NCR (کنترل کیفیت) انجام می‌شود؛ برای ضایعات کارگاه رسید ضایعات بزنید.";

            if (type == InventoryDocumentType.SubcontractIssue)
            {
                if (destination == null || destination.Kind != WarehouseKind.Subcontractor) return "مقصد ارسال به پیمانکار باید انبار مجازی همان پیمانکار باشد.";
                if (source.Kind == WarehouseKind.Subcontractor) return "مبدا ارسال به پیمانکار انبار شرکت است.";
                if (!dto.ProviderId.HasValue) return "پیمانکار (تامین‌کننده) را انتخاب کنید.";
            }
            else if (type == InventoryDocumentType.SubcontractReturn)
            {
                if (source.Kind != WarehouseKind.Subcontractor) return "مبدا برگشت از پیمانکار باید انبار مجازی پیمانکار باشد.";
                if (destination == null || destination.Kind == WarehouseKind.Subcontractor) return "مقصد برگشت از پیمانکار انبار شرکت (ترجیحاً قرنطینه) است.";
                if (!dto.ProviderId.HasValue) return "پیمانکار (تامین‌کننده) را انتخاب کنید.";
            }
            else if (source.Kind == WarehouseKind.Subcontractor && takesOut && !adjustment)
            {
                return "کالای نزد پیمانکار فقط با سند برگشت از پیمانکار برمی‌گردد.";
            }
            else if (destination != null && destination.Kind == WarehouseKind.Subcontractor)
            {
                return "ارسال به پیمانکار با سند «ارسال به پیمانکار» انجام می‌شود.";
            }

            if (type == InventoryDocumentType.CustomerPropertyReceipt)
            {
                if (source.Kind != WarehouseKind.CustomerProperty) return "اموال مشتری در انبار اموال مشتری نگهداری می‌شود.";
                if (!dto.ContractId.HasValue) return "قرارداد (مشتری) را انتخاب کنید.";
            }
            else if (source.Kind == WarehouseKind.CustomerProperty && InventoryDocumentRules.IsIncoming(type) && !adjustment)
            {
                return "ورود به انبار اموال مشتری فقط با رسید اموال مشتری است.";
            }

            if (type == InventoryDocumentType.CustomerDelivery && !dto.ContractId.HasValue && !dto.SalesOrderId.HasValue && !dto.ServiceOrderId.HasValue) return "قرارداد (مشتری)، سفارش فروش یا دستور خدمات تحویل را انتخاب کنید.";
            if (type == InventoryDocumentType.Issue && source.Kind == WarehouseKind.Subcontractor) return "از انبار پیمانکار حواله صادر نمی‌شود.";
            return null;
        }

        private static string CheckConversionLines(InventoryDocumentType type, List<SaveDocumentLineDto> lines)
        {
            if (!InventoryDocumentRules.IsConversion(type)) return null;
            if (!lines.Any(l => l.Direction == DocumentLineDirection.Out) || !lines.Any(l => l.Direction == DocumentLineDirection.In))
                return "تبدیل مرحله حداقل یک ردیف «خروج مرحله قبل» و یک ردیف «ورود مرحله بعد» لازم دارد.";
            if (lines.Any(l => l.Direction != DocumentLineDirection.Out && l.Direction != DocumentLineDirection.In))
                return "جهت همه ردیف‌های تبدیل مرحله (خروج یا ورود) را مشخص کنید.";
            for (int i = 0; i < lines.Count; i++)
            {
                SaveDocumentLineDto line = lines[i];
                if (line.Direction != DocumentLineDirection.In) continue;
                if (!line.SourceRowNumber.HasValue) return $"ردیف {i + 1}: ردیف مبدا (مرحله قبل) را مشخص کنید.";
                int source = line.SourceRowNumber.Value;
                if (source < 1 || source > lines.Count || lines[source - 1].Direction != DocumentLineDirection.Out)
                    return $"ردیف {i + 1}: ردیف مبدا باید یکی از ردیف‌های خروج همین سند باشد.";
            }
            return null;
        }

        /// <summary>The issue must stay within an approved request: same items, not more than what is left.</summary>
        private string CheckRequest(MaterialRequest request, IEnumerable<(int ItemId, decimal Quantity)> lines, int? documentId)
        {
            if (request == null) return "درخواست کالا پیدا نشد.";
            if (request.Status != MaterialRequestStatus.Approved)
                return $"درخواست کالای {request.Number ?? request.Id.ToString()} هنوز تایید نهایی (برنامه‌ریزی و تولید) نشده یا بسته شده است.";
            foreach (var group in lines.GroupBy(l => l.ItemId))
            {
                List<MaterialRequestLine> requested = request.Lines.Where(l => l.ItemId == group.Key).ToList();
                if (requested.Count == 0) return "کالای این ردیف در درخواست کالا نیست.";
                decimal left = requested.Sum(l => l.Quantity - l.IssuedQuantity);
                decimal wanted = group.Sum(l => l.Quantity);
                if (wanted > left)
                    return $"مقدار حواله ({InventoryText.Number(wanted)}) بیشتر از مانده درخواست ({InventoryText.Number(left)}) است.";
            }
            return null;
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
            string costCenter = InventoryText.Clean(dto.CostCenter);
            if (costCenter != null && dto.Type == InventoryDocumentType.Issue
                && !await _uow.Set<Entities.Model.NasbNiro.Finance.CostCenter>().AnyAsync(c => c.Code == costCenter && c.IsActive, cancellationToken))
                return "مرکز هزینه پیدا نشد یا غیرفعال است.";
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
            bool transfer = InventoryDocumentRules.IsTransfer(type);
            bool conversion = InventoryDocumentRules.IsConversion(type);

            Warehouse source = await _uow.Set<Warehouse>().FirstOrDefaultAsync(w => w.Id == document.WarehouseId && !w.Deleted && w.IsActive, cancellationToken);
            if (source == null) return ResponseDtoHelper.BadRequest<DocumentDto>("انبار سند فعال نیست.");
            Warehouse destination = null;
            if (document.DestinationWarehouseId.HasValue)
            {
                destination = await _uow.Set<Warehouse>().FirstOrDefaultAsync(w => w.Id == document.DestinationWarehouseId && !w.Deleted && w.IsActive, cancellationToken);
                if (destination == null) return ResponseDtoHelper.BadRequest<DocumentDto>("انبار مقصد فعال نیست.");
            }
            if (transfer && destination == null) return ResponseDtoHelper.BadRequest<DocumentDto>("انبار مقصد انتقال را انتخاب کنید.");
            // where the "in" side of the document goes
            Warehouse inWarehouse = transfer || conversion ? destination ?? source : source;

            string headerError = CheckBeforeConfirm(document) ?? await CheckPurchaseOrderAsync(document, cancellationToken);
            if (headerError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(headerError);
            // customer delivery needs a signed release note for the work order (API Q1 / ISO 9001 8.6)
            // (a delivery of spare parts against a sales order has no work order: the sales order is checked instead)
            if (document.Type == InventoryDocumentType.CustomerDelivery && document.SalesOrderId.HasValue)
            {
                string salesError = await Sales.SlFulfilment.CheckConfirmAsync(_uow, document, cancellationToken);
                if (salesError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(salesError);
            }
            else if (document.Type == InventoryDocumentType.CustomerDelivery && !document.ServiceOrderId.HasValue)
            {
                string releaseError = await _release.CheckDeliveryAsync(document.WorkOrderId, cancellationToken);
                if (releaseError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(releaseError);
            }

            // during an annual stock count the warehouse is frozen: only the count's own adjustments are posted
            if (!document.StockCountId.HasValue)
            {
                var touched = new List<int> { document.WarehouseId };
                if (document.DestinationWarehouseId.HasValue) touched.Add(document.DestinationWarehouseId.Value);
                bool frozen = await _uow.Set<StockCount>().AnyAsync(c => !c.Deleted && c.Kind == StockCountKind.Annual &&
                    (c.Status == StockCountStatus.Counting || c.Status == StockCountStatus.Submitted || c.Status == StockCountStatus.Approved) &&
                    touched.Contains(c.WarehouseId), cancellationToken);
                if (frozen) return ResponseDtoHelper.BadRequest<DocumentDto>("انبارگردانی سالانه این انبار باز است؛ تا ثبت نتیجه شمارش، سند جدید تایید نمی‌شود.");
            }

            MaterialRequest request = null;
            if (type == InventoryDocumentType.Issue)
            {
                request = await _uow.Set<MaterialRequest>().Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.Id == document.MaterialRequestId && !r.Deleted, cancellationToken);
                string requestError = CheckRequest(request, document.Lines.Select(l => (l.ItemId, l.Quantity)), document.Id);
                if (requestError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(requestError);
            }

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

                // a stage conversion takes out on its "out" lines and brings in on its "in" lines
                bool lineIn = incoming || (conversion && line.Direction == DocumentLineDirection.In);
                int lineWarehouseId = lineIn ? inWarehouse.Id : document.WarehouseId;
                InventoryDocumentLine sourceLine = conversion && lineIn && line.SourceRowNumber.HasValue
                    ? document.Lines.FirstOrDefault(l => l.RowNumber == line.SourceRowNumber.Value)
                    : null;
                if (type == InventoryDocumentType.PurchaseReceipt && item.RequiresIncomingInspection && source.Kind != WarehouseKind.Quarantine)
                { errors.Add(row + $"کالای {item.Code} بازرسی ورودی لازم دارد؛ رسید خرید آن در انبار قرنطینه ثبت می‌شود."); continue; }

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
                    else if (lineIn)
                    {
                        // no lot number typed: the company label number (YYDDDNNNN) becomes the lot number
                        if (line.LotNumber == null)
                            line.LotNumber = (await InventoryNumbers.NextLabelsAsync(_uow, document.DocumentDate, 1, cancellationToken))[0];
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
                                    ProductionDate = type == InventoryDocumentType.ProductionReceipt || conversion ? document.DocumentDate : (DateTime?)null,
                                    ExpiryDate = item.ShelfLifeDays.HasValue ? document.DocumentDate.AddDays(item.ShelfLifeDays.Value) : (DateTime?)null,
                                    ParentLotId = sourceLine?.LotId,
                                    IsCustomerProperty = source.Kind == WarehouseKind.CustomerProperty,
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
                    if (line.Serials == null) line.Serials = new List<InventoryDocumentLineSerial>();
                    // pieces made here get the company label number when no serial is typed
                    if (lineIn && line.Serials.Count == 0 && InventoryText.IsWhole(line.Quantity) && line.Quantity > 0 &&
                        type != InventoryDocumentType.ProductionReturn && type != InventoryDocumentType.AdjustmentIncrease)
                    {
                        foreach (string label in await InventoryNumbers.NextLabelsAsync(_uow, document.DocumentDate, (int)line.Quantity, cancellationToken))
                            line.Serials.Add(new InventoryDocumentLineSerial { SerialNumber = label });
                    }
                    var serials = line.Serials.ToList();
                    if (!InventoryText.IsWhole(line.Quantity) || serials.Count != (int)line.Quantity)
                    {
                        errors.Add(row + $"کالای {item.Code} سریالی است؛ تعداد سریال‌ها ({serials.Count}) باید برابر مقدار ({InventoryText.Number(line.Quantity)}) باشد.");
                        continue;
                    }

                    // a serial item may also belong to a lot (for example blades of one heat)
                    int? serialLotId = line.LotId;
                    InventoryLot serialNewLot = null;
                    if (lineIn && !serialLotId.HasValue && line.LotNumber != null)
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

                    List<InventoryDocumentLineSerial> sourceSerials = sourceLine?.Serials?.OrderBy(x => x.Id).ToList();
                    int serialIndex = 0;
                    foreach (InventoryDocumentLineSerial serialRow in serials)
                    {
                        SerialUnit unit = knownUnits.FirstOrDefault(u => u.ItemId == item.Id &&
                            string.Equals(u.SerialNumber, serialRow.SerialNumber, StringComparison.OrdinalIgnoreCase));
                        string label = $"سریال {serialRow.SerialNumber} ({item.Code})";

                        if (lineIn)
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
                                // label genealogy: the piece of the previous stage it was made from
                                if (sourceLine != null)
                                {
                                    if (sourceSerials != null && serialIndex < sourceSerials.Count)
                                    {
                                        string sourceNumber = sourceSerials[serialIndex].SerialNumber;
                                        unit.SourceSerialUnit = knownUnits.FirstOrDefault(u => u.ItemId == sourceLine.ItemId &&
                                            string.Equals(u.SerialNumber, sourceNumber, StringComparison.OrdinalIgnoreCase));
                                    }
                                    if (unit.SourceSerialUnit == null) unit.SourceLotId = sourceLine.LotId;
                                }
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
                            unit.WarehouseId = lineWarehouseId;
                            unit.LocationId = line.LocationId;
                            if (serialNewLot != null) unit.Lot = serialNewLot;
                            else if (serialLotId.HasValue) unit.LotId = serialLotId;
                            unit.ModifyDate = now;
                            unit.ModifierUserId = _user.UserId;
                            serialRow.SerialUnit = unit;

                            moves.Add(new Move
                            {
                                Line = line, ItemId = item.Id, WarehouseId = lineWarehouseId, LocationId = line.LocationId,
                                LotId = unit.LotId, NewLot = unit.Lot != null && unit.Lot.Id == 0 ? unit.Lot : null,
                                Unit = unit, Quantity = 1, UnitPrice = line.UnitPrice,
                            });
                            serialIndex++;
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
                                unit.Status = InventoryDocumentRules.OutgoingStatus(type);
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
                if (lineIn)
                {
                    moves.Add(new Move
                    {
                        Line = line, ItemId = item.Id, WarehouseId = lineWarehouseId, LocationId = line.LocationId,
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

            var posted = new List<StockTransaction>();
            foreach (Move move in moves)
            {
                StockTransaction transaction;
                _uow.Set<StockTransaction>().Add(transaction = new StockTransaction
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
                posted.Add(transaction);
            }

            // receipts into quarantine wait for incoming inspection, one inspection per line
            if (document.NcrId == null && document.StockCountId == null && type != InventoryDocumentType.QcRelease && inWarehouse.Kind == WarehouseKind.Quarantine)
            {
                foreach (InventoryDocumentLine line in document.Lines.Where(l => incoming || transfer || l.Direction == DocumentLineDirection.In).OrderBy(l => l.RowNumber))
                {
                    _uow.Set<QcInspection>().Add(new QcInspection
                    {
                        Number = await InventoryNumbers.NextNumberAsync(_uow, InventoryNumbers.Inspection, "QI", document.DocumentDate, cancellationToken),
                        ReceiptDocument = document,
                        ReceiptLineId = line.Id,
                        ItemId = line.ItemId,
                        LotId = line.Lot == null ? line.LotId : null,
                        Lot = line.Lot,
                        WarehouseId = inWarehouse.Id,
                        LocationId = transfer ? line.DestinationLocationId : line.LocationId,
                        Quantity = line.Quantity,
                        SerialNumbers = line.Serials == null || line.Serials.Count == 0 ? null : string.Join("\n", line.Serials.OrderBy(x => x.Id).Select(x => x.SerialNumber)),
                        TargetWarehouseId = incoming ? document.DestinationWarehouseId : null,
                        Status = InspectionStatus.Pending,
                        CreateDate = now,
                        CreatorUserId = _user.UserId,
                    });
                }
            }

            // what has been issued against the material request
            if (request != null)
            {
                foreach (InventoryDocumentLine line in document.Lines)
                {
                    decimal quantity = line.Quantity;
                    foreach (MaterialRequestLine requested in request.Lines.Where(l => l.ItemId == line.ItemId).OrderBy(l => l.RowNumber))
                    {
                        decimal take = Math.Min(quantity, requested.Quantity - requested.IssuedQuantity);
                        if (take <= 0) continue;
                        requested.IssuedQuantity += take;
                        quantity -= take;
                    }
                }
                if (request.Lines.All(l => l.IssuedQuantity >= l.Quantity))
                {
                    request.Status = MaterialRequestStatus.Closed;
                    request.ClosedAt = now;
                    request.ClosedByUserId = _user.UserId;
                }
            }

            if (document.NcrId.HasValue)
            {
                NonConformance ncr = await _uow.Set<NonConformance>().FirstOrDefaultAsync(n => n.Id == document.NcrId.Value, cancellationToken);
                if (ncr != null && ncr.Status == NcrStatus.Approved)
                {
                    ncr.Status = NcrStatus.Executed;
                    ncr.ResultDocumentId = document.Id;
                    ncr.ExecutedAt = now;
                    ncr.ExecutedByUserId = _user.UserId;
                }
            }

            document.Number = await NextNumberAsync(type, document.DocumentDate, cancellationToken);
            // sales order: delivered quantities, reservation and the invoice the system drafts (saved together with the stock movements)
            if (type == InventoryDocumentType.CustomerDelivery && document.SalesOrderId.HasValue)
                await Sales.SlFulfilment.ApplyConfirmAsync(_uow, _user.UserId, document, cancellationToken);
            // value (weighted average), work order cost and the accounting event for Sepidar
            await _costing.PostDocumentAsync(document, posted, cancellationToken);
            document.Status = InventoryDocumentStatus.Confirmed;
            document.ConfirmedAt = now;
            document.ConfirmedByUserId = _user.UserId;
            document.SepidarStatus = SepidarSyncStatus.Pending;
            document.ModifyDate = now;
            document.ModifierUserId = _user.UserId;

            if (type == InventoryDocumentType.SupplierReturn && document.ProviderId.HasValue)
                await _payables.OnSupplierReturnAsync(document.ProviderId.Value, document.Number, cancellationToken);

            string saveError = await SaveAsync(cancellationToken);
            if (saveError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(saveError);
            await RefreshPurchaseOrderAsync(document, cancellationToken);
            return await GetDocumentAsync(document.Id, cancellationToken);
        }

        /// <summary>After a receipt or return of an order is confirmed or cancelled: received quantity and status of the order.</summary>
        private async Task RefreshPurchaseOrderAsync(InventoryDocument document, CancellationToken cancellationToken)
        {
            if (!document.PurchaseOrderId.HasValue) return;
            await _procurement.RefreshOrderAsync(document.PurchaseOrderId.Value, cancellationToken);
            await SaveAsync(cancellationToken);
        }

        /// <summary>
        /// Purchase receipt against a PMIS purchase order (WI-WH-001 / WI-PO): the order must be released, of the same supplier,
        /// each line from the order, quantity within the open quantity plus tolerance; the price is taken from the order.
        /// Without an order only a petty-cash purchase (every line priced, total within the petty-cash limit) is accepted,
        /// unless the purchase order rule is switched off (go-live period).
        /// </summary>
        private async Task<string> CheckPurchaseOrderAsync(InventoryDocument document, CancellationToken cancellationToken)
        {
            if (!InventoryDocumentRules.UsesPurchaseOrder(document.Type)) return null;
            Dictionary<string, decimal> s = await Procurement.ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            if (!document.PurchaseOrderId.HasValue)
            {
                if (document.Lines.Any(l => l.PurchaseOrderLineId.HasValue)) return "ردیف سفارش خرید بدون سفارش خرید در سرآیند ثبت شده است.";
                if (document.Type != InventoryDocumentType.PurchaseReceipt || s[Procurement.ProcurementSettingKeys.EnforcePurchaseOrder] == 0) return null;
                if (document.Lines.Any(l => !l.UnitPrice.HasValue))
                    return "رسید خرید بدون سفارش خرید فقط برای خرید تنخواه است و فی همه ردیف‌ها لازم است.";
                decimal total = document.Lines.Sum(l => l.Quantity * l.UnitPrice.Value);
                decimal limit = s[Procurement.ProcurementSettingKeys.PettyCashLimit];
                return total > limit ? $"مبلغ این رسید ({total:0} ریال) از سقف خرید تنخواه ({limit:0} ریال) بیشتر است؛ رسید باید به سفارش خرید ابلاغ‌شده متصل باشد." : null;
            }

            Entities.Model.NasbNiro.Procurement.PurchaseOrder order = await _uow.Set<Entities.Model.NasbNiro.Procurement.PurchaseOrder>()
                .Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == document.PurchaseOrderId.Value && !o.Deleted, cancellationToken);
            if (order == null) return "سفارش خرید پیدا نشد.";
            if (document.ProviderId.HasValue && document.ProviderId != order.ProviderId) return "تامین‌کننده سند با تامین‌کننده سفارش خرید یکی نیست.";
            document.ProviderId = order.ProviderId;
            document.WorkOrderId ??= order.WorkOrderId;
            document.ContractId ??= order.ContractId;
            if (document.Type == InventoryDocumentType.PurchaseReceipt &&
                order.Status != Entities.Model.NasbNiro.Procurement.PurchaseOrderStatus.Released &&
                order.Status != Entities.Model.NasbNiro.Procurement.PurchaseOrderStatus.PartlyReceived)
                return $"سفارش خرید {order.Number} ابلاغ نشده یا تکمیل/بسته شده است.";
            decimal tolerance = s[Procurement.ProcurementSettingKeys.ReceiptTolerancePercent] / 100m;
            foreach (InventoryDocumentLine line in document.Lines)
            {
                string row = $"ردیف {line.RowNumber}: ";
                var orderLine = order.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId);
                if (orderLine == null) return row + "ردیف سفارش خرید را انتخاب کنید.";
                if (orderLine.ItemId != line.ItemId) return row + "کالای ردیف با کالای سفارش خرید یکی نیست.";
                if (document.Type == InventoryDocumentType.PurchaseReceipt)
                    line.UnitPrice = orderLine.UnitPrice; // the value of the receipt is the order price (rial)
            }
            foreach (var g in document.Lines.GroupBy(l => l.PurchaseOrderLineId.Value))
            {
                var orderLine = order.Lines.First(l => l.Id == g.Key);
                decimal quantity = g.Sum(l => l.Quantity);
                if (document.Type == InventoryDocumentType.PurchaseReceipt)
                {
                    decimal open = orderLine.Quantity * (1 + tolerance) - orderLine.ReceivedQuantity;
                    if (quantity > open)
                        return $"ردیف {orderLine.RowNumber} سفارش {order.Number}: سفارش {orderLine.Quantity}، دریافت‌شده {orderLine.ReceivedQuantity}؛ دریافت بیش از سفارش مجاز نیست.";
                }
                else if (quantity > orderLine.ReceivedQuantity)
                    return $"ردیف {orderLine.RowNumber} سفارش {order.Number}: برگشت بیش از مقدار دریافت‌شده است.";
            }
            return null;
        }

        /// <summary>Data the document needs before it is confirmed: exit data, scrap sale data, approvals.</summary>
        private static string CheckBeforeConfirm(InventoryDocument document)
        {
            InventoryDocumentType type = document.Type;
            var missing = new List<string>();
            if (InventoryDocumentRules.LeavesPremises(type))
            {
                if (document.DriverName == null) missing.Add("نام راننده");
                if (document.VehiclePlate == null) missing.Add("شماره پلاک");
                if (document.ReceiverName == null) missing.Add("تحویل‌گیرنده");
            }
            if (type == InventoryDocumentType.ScrapSale)
            {
                if (document.BuyerName == null) missing.Add("خریدار");
                if (document.MinutesNumber == null) missing.Add("شماره صورت‌جلسه فروش (FO-WH-020)");
                if (!document.WeighbridgeDocumentId.HasValue) missing.Add("تصویر قبض باسکول");
                if (document.DepositReference == null) missing.Add("شماره واریز به حساب شرکت");
                if (ScrapQuoteCount(document.PriceQuotes) < 3) missing.Add("استعلام قیمت از حداقل سه خریدار");
            }
            if (missing.Count > 0) return "پیش از تایید این موارد را کامل کنید: " + string.Join("، ", missing);

            if (type == InventoryDocumentType.ScrapSale && !document.FinanceApprovedAt.HasValue)
                return "فروش ضایعات پیش از خروج، تایید واحد مالی (قیمت و واریز به حساب شرکت) را لازم دارد.";
            if (type == InventoryDocumentType.CustomerDelivery && !document.QcLoadingApprovedAt.HasValue)
                return "تحویل به مشتری تایید بارگیری کنترل کیفیت لازم دارد.";
            if (InventoryDocumentRules.NeedsApprovals(type) && !document.StockCountId.HasValue &&
                (!document.WarehouseApprovedAt.HasValue || !document.PlanningApprovedAt.HasValue))
                return "سند تعدیل پیش از تایید، تایید سرپرست انبار و مدیر برنامه‌ریزی و کنترل پروژه را لازم دارد.";
            return null;
        }

        public async Task<ResponseDto<DocumentDto>> ApproveAsync(int id, ApproveDocumentDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.InvalidData);
            InventoryDocument document = await Documents.FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, cancellationToken);
            if (document == null) return ResponseDtoHelper.NotFound<DocumentDto>(InventoryMessages.DocumentNotFound);
            if (document.Status != InventoryDocumentStatus.Draft) return ResponseDtoHelper.BadRequest<DocumentDto>("فقط سند پیش‌نویس تایید می‌شود.");
            DateTimeOffset now = DateTimeOffset.Now;
            int me = _user.UserId;

            switch (dto.Kind)
            {
                case ApprovalKind.Warehouse:
                case ApprovalKind.Planning:
                    if (!InventoryDocumentRules.NeedsApprovals(document.Type)) return ResponseDtoHelper.BadRequest<DocumentDto>("این سند تایید جداگانه لازم ندارد.");
                    bool warehouse = dto.Kind == ApprovalKind.Warehouse;
                    if (!_user.IsInRole(warehouse ? InventoryRoles.RoleWarehouseSupervisor : InventoryRoles.RolePlanning))
                        return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.NotAllowed + (warehouse ? " (نقش سرپرست انبار لازم است)" : " (نقش برنامه‌ریزی و کنترل پروژه لازم است)"));
                    int? other = warehouse ? document.PlanningApprovedByUserId : document.WarehouseApprovedByUserId;
                    if (me == document.CreatorUserId || me == other) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.SameUser);
                    if (warehouse) { document.WarehouseApprovedAt = now; document.WarehouseApprovedByUserId = me; }
                    else { document.PlanningApprovedAt = now; document.PlanningApprovedByUserId = me; }
                    break;
                case ApprovalKind.Quality:
                    if (document.Type != InventoryDocumentType.CustomerDelivery) return ResponseDtoHelper.BadRequest<DocumentDto>("تایید بارگیری فقط برای تحویل به مشتری است.");
                    if (!_user.IsInRole(InventoryRoles.RoleQuality)) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.NotAllowed + " (نقش کنترل کیفیت لازم است)");
                    document.QcLoadingApprovedAt = now;
                    document.QcLoadingApprovedByUserId = me;
                    break;
                case ApprovalKind.Finance:
                    if (document.Type != InventoryDocumentType.ScrapSale) return ResponseDtoHelper.BadRequest<DocumentDto>("تایید مالی فقط برای فروش ضایعات است.");
                    if (!_user.IsInRole(InventoryRoles.Finance)) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.NotAllowed + " (نقش مالی لازم است)");
                    if (me == document.CreatorUserId) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.SameUser);
                    if (document.DepositReference == null || ScrapQuoteCount(document.PriceQuotes) < 3)
                        return ResponseDtoHelper.BadRequest<DocumentDto>("پیش از تایید مالی، شماره واریز به حساب شرکت و استعلام قیمت از حداقل سه خریدار ثبت شود.");
                    document.FinanceApprovedAt = now;
                    document.FinanceApprovedByUserId = me;
                    break;
                default:
                    return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.InvalidData);
            }
            document.ModifyDate = now;
            document.ModifierUserId = me;
            string saveError = await SaveAsync(cancellationToken);
            if (saveError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(saveError);
            return await GetDocumentAsync(id, cancellationToken);
        }

        public async Task<ResponseDto<DocumentDto>> ExitCheckAsync(int id, ExitCheckDto dto, CancellationToken cancellationToken)
        {
            InventoryDocument document = await Documents.FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, cancellationToken);
            if (document == null) return ResponseDtoHelper.NotFound<DocumentDto>(InventoryMessages.DocumentNotFound);
            if (!InventoryDocumentRules.LeavesPremises(document.Type)) return ResponseDtoHelper.BadRequest<DocumentDto>("این سند خروج از شرکت ندارد.");
            if (document.Status != InventoryDocumentStatus.Confirmed) return ResponseDtoHelper.BadRequest<DocumentDto>("خروج فقط برای سند تاییدشده ثبت می‌شود.");
            if (document.ExitCheckedAt.HasValue) return ResponseDtoHelper.BadRequest<DocumentDto>("خروج این سند قبلاً ثبت شده است.");
            document.ExitCheckedAt = DateTimeOffset.Now;
            document.ExitCheckedByUserId = _user.UserId;
            document.ExitCheckNote = InventoryText.Clean(dto?.Note);
            string saveError = await SaveAsync(cancellationToken);
            if (saveError != null) return ResponseDtoHelper.BadRequest<DocumentDto>(saveError);
            return await GetDocumentAsync(id, cancellationToken);
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
            if (document.Type == InventoryDocumentType.QcRelease || document.InspectionId.HasValue)
                return ResponseDtoHelper.BadRequest<DocumentDto>("سند ترخیص از قرنطینه نتیجه بازرسی کنترل کیفیت است و باطل نمی‌شود؛ در صورت اشتباه، کالا با NCR یا انتقال برگردانده شود.");
            if (document.ExitCheckedAt.HasValue)
                return ResponseDtoHelper.BadRequest<DocumentDto>("کالای این سند از شرکت خارج شده است؛ برگشت آن با سند ورود جداگانه ثبت می‌شود.");
            if (document.SalesOrderId.HasValue)
            {
                // refused before anything is changed: the invoice of the delivery is already approved or posted
                string salesRefusal = await Sales.SlFulfilment.CheckCancelAsync(_uow, document, cancellationToken);
                if (salesRefusal != null) return ResponseDtoHelper.BadRequest<DocumentDto>(salesRefusal);
            }
            List<QcInspection> inspections = await _uow.Set<QcInspection>()
                .Where(q => q.ReceiptDocumentId == id && !q.Deleted).ToListAsync(cancellationToken);
            if (inspections.Any(q => q.Status == InspectionStatus.Done))
                return ResponseDtoHelper.BadRequest<DocumentDto>("بازرسی کالای این رسید انجام شده است؛ رسید باطل نمی‌شود. کالای مردود با NCR برگشت داده می‌شود.");

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
                    bool cameIn = !InventoryDocumentRules.IsTransfer(document.Type) && mine.All(t => t.Quantity > 0);
                    if (cameIn)
                    {
                        StockTransaction before = unitHistory.Where(t => t.SerialUnitId == unit.Id && t.Id < firstOfMine.Id)
                            .OrderByDescending(t => t.Id).FirstOrDefault();
                        bool hasTraceRecords = await _uow.Set<PartOperation>().AnyAsync(o => o.SerialUnitId == unit.Id && !o.Deleted, cancellationToken)
                                               || await _uow.Set<AssemblyLink>().AnyAsync(a => a.ParentSerialUnitId == unit.Id, cancellationToken);
                        if (before == null && !hasTraceRecords)
                        {
                            unit.Delete(); // created by this receipt
                        }
                        else if (before != null && before.Quantity < 0 && !InventoryDocumentRules.IsTransfer(before.Document.Type))
                        {
                            unit.Status = InventoryDocumentRules.OutgoingStatus(before.Document.Type);
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

            var reversals = new List<StockTransaction>();
            foreach (StockTransaction t in transactions)
            {
                var reversal = new StockTransaction
                {
                    ItemId = t.ItemId, WarehouseId = t.WarehouseId, LocationId = t.LocationId, LotId = t.LotId,
                    SerialUnitId = t.SerialUnitId, Quantity = -t.Quantity, UnitPrice = t.UnitPrice,
                    TransactionDate = document.DocumentDate, DocumentId = document.Id, DocumentLineId = t.DocumentLineId,
                    IsReversal = true, CreatedAt = now, CreatedByUserId = _user.UserId,
                };
                _uow.Set<StockTransaction>().Add(reversal);
                reversals.Add(reversal);
            }
            await _costing.ReverseDocumentAsync(document, transactions, reversals, cancellationToken);

            foreach (QcInspection inspection in inspections) inspection.Delete();

            if (document.MaterialRequestId.HasValue)
            {
                MaterialRequest request = await _uow.Set<MaterialRequest>().Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.Id == document.MaterialRequestId.Value, cancellationToken);
                List<InventoryDocumentLine> lines = await _uow.Set<InventoryDocumentLine>().Where(l => l.DocumentId == id).ToListAsync(cancellationToken);
                if (request != null)
                {
                    foreach (InventoryDocumentLine line in lines)
                    {
                        decimal quantity = line.Quantity;
                        foreach (MaterialRequestLine requested in request.Lines.Where(l => l.ItemId == line.ItemId).OrderByDescending(l => l.RowNumber))
                        {
                            decimal back = Math.Min(quantity, requested.IssuedQuantity);
                            if (back <= 0) continue;
                            requested.IssuedQuantity -= back;
                            quantity -= back;
                        }
                    }
                    if (request.Status == MaterialRequestStatus.Closed && request.Lines.Any(l => l.IssuedQuantity < l.Quantity))
                    {
                        request.Status = MaterialRequestStatus.Approved;
                        request.ClosedAt = null;
                        request.ClosedByUserId = null;
                    }
                }
            }

            if (document.NcrId.HasValue)
            {
                NonConformance ncr = await _uow.Set<NonConformance>().FirstOrDefaultAsync(n => n.Id == document.NcrId.Value, cancellationToken);
                if (ncr != null && ncr.Status == NcrStatus.Executed && ncr.ResultDocumentId == document.Id)
                {
                    ncr.Status = NcrStatus.Approved;
                    ncr.ResultDocumentId = null;
                    ncr.ExecutedAt = null;
                    ncr.ExecutedByUserId = null;
                }
            }

            if (document.SalesOrderId.HasValue)
            {
                await Sales.SlFulfilment.ApplyCancelAsync(_uow, _user.UserId, document, cancellationToken);
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
            await RefreshPurchaseOrderAsync(document, cancellationToken);
            return await GetDocumentAsync(document.Id, cancellationToken);
        }
    }
}
