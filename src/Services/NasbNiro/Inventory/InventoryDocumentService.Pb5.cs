using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.InventoryDtos;

namespace Services.NasbNiro.Inventory
{
    /// <summary>PB5 additions of the warehouse document service: consignment, derived states, rejection, accountant price approval, corrective documents, packing lists, project warehouses.</summary>
    public partial class InventoryDocumentService
    {
        // ------------------------------------------------------------------------------- derived state (R32)

        private static IQueryable<InventoryDocument> FilterByState(IQueryable<InventoryDocument> documents, DocumentState state)
        {
            switch (state)
            {
                case DocumentState.Cancelled:
                    return documents.Where(d => d.Status == InventoryDocumentStatus.Cancelled);
                case DocumentState.Approved:
                    return documents.Where(d => d.Status == InventoryDocumentStatus.Confirmed);
                case DocumentState.Rejected:
                    return documents.Where(d => d.Status == InventoryDocumentStatus.Draft && d.RejectedAt != null);
                case DocumentState.PendingApproval:
                    return documents.Where(d => d.Status == InventoryDocumentStatus.Draft && d.RejectedAt == null && d.StockCountId == null
                        && (d.Type == InventoryDocumentType.AdjustmentIncrease || d.Type == InventoryDocumentType.AdjustmentDecrease || d.Type == InventoryDocumentType.OpeningBalance)
                        && ((d.WarehouseApprovedAt != null ? 1 : 0) + (d.PlanningApprovedAt != null ? 1 : 0)) == 1);
                default:
                    return documents.Where(d => d.Status == InventoryDocumentStatus.Draft && d.RejectedAt == null
                        && !(d.StockCountId == null
                            && (d.Type == InventoryDocumentType.AdjustmentIncrease || d.Type == InventoryDocumentType.AdjustmentDecrease || d.Type == InventoryDocumentType.OpeningBalance)
                            && ((d.WarehouseApprovedAt != null ? 1 : 0) + (d.PlanningApprovedAt != null ? 1 : 0)) == 1));
            }
        }

        // ------------------------------------------------------------------------------- read enrichment

        private async Task EnrichPb5Async(DocumentDto document, CancellationToken ct)
        {
            var raw = await Documents.AsNoTracking().Where(d => d.Id == document.Id).Select(d => new
            {
                d.ConsignmentDueDate, d.ConsignmentOfDocumentId, d.CorrectsDocumentId, d.RejectedAt, d.RejectedByUserId, d.RejectReason,
                d.PriceApprovedAt, d.PriceApprovedByUserId, d.GoodsType, d.BillOfLadingNumber, d.StockCountId,
                Approvals = (d.WarehouseApprovedAt != null ? 1 : 0) + (d.PlanningApprovedAt != null ? 1 : 0),
            }).FirstOrDefaultAsync(ct);
            if (raw == null) return;

            document.ConsignmentDueDate = raw.ConsignmentDueDate;
            document.ConsignmentOfDocumentId = raw.ConsignmentOfDocumentId;
            document.CorrectsDocumentId = raw.CorrectsDocumentId;
            document.RejectedAt = raw.RejectedAt;
            document.RejectReason = raw.RejectReason;
            document.PriceApprovedAt = raw.PriceApprovedAt;
            document.GoodsType = raw.GoodsType;
            document.BillOfLadingNumber = raw.BillOfLadingNumber;
            document.ApprovalsGiven = raw.Approvals;
            document.State = DocumentStateRules.Derive(document.Type, document.Status, raw.Approvals, raw.RejectedAt != null, raw.StockCountId != null);
            document.StateTitle = DocumentStateRules.Title(document.State);

            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { raw.RejectedByUserId, raw.PriceApprovedByUserId }, ct);
            document.RejectedByName = names.Name(raw.RejectedByUserId);
            document.PriceApprovedByName = names.Name(raw.PriceApprovedByUserId);

            if (raw.ConsignmentOfDocumentId.HasValue)
                document.ConsignmentOfNumber = await Documents.AsNoTracking().Where(d => d.Id == raw.ConsignmentOfDocumentId.Value).Select(d => d.Number).FirstOrDefaultAsync(ct);
            if (raw.CorrectsDocumentId.HasValue)
                document.CorrectsDocumentNumber = await Documents.AsNoTracking().Where(d => d.Id == raw.CorrectsDocumentId.Value).Select(d => d.Number).FirstOrDefaultAsync(ct);
            var correctedBy = await Documents.AsNoTracking().Where(d => d.CorrectsDocumentId == document.Id && !d.Deleted && d.Status != InventoryDocumentStatus.Cancelled)
                .OrderBy(d => d.Id).Select(d => new { d.Id, d.Number }).FirstOrDefaultAsync(ct);
            if (correctedBy != null) { document.CorrectedByDocumentId = correctedBy.Id; document.CorrectedByDocumentNumber = correctedBy.Number; }

            if (document.Type == InventoryDocumentType.ConsignmentReceipt && document.Status == InventoryDocumentStatus.Confirmed)
            {
                Dictionary<int, decimal> open = await OpenConsignmentAsync(document.Id, null, ct);
                document.ConsignmentOpenQuantity = open.Values.Where(v => v > 0).Sum();
                if (document.ConsignmentOpenQuantity > 0 && raw.ConsignmentDueDate.HasValue)
                {
                    Dictionary<string, decimal> settings = await Quality.QualitySettingKeys.LoadAsync(_uow, ct);
                    document.ConsignmentAlertLevel = ConsignmentRules.Level(raw.ConsignmentDueDate.Value, DateTime.Today,
                        (int)settings[Quality.QualitySettingKeys.ConsignmentAlertDays1], (int)settings[Quality.QualitySettingKeys.ConsignmentAlertDays2]);
                }
            }

            document.PackingLines = await _uow.Set<InventoryPackingLine>().AsNoTracking().Where(p => p.DocumentId == document.Id).OrderBy(p => p.RowNumber)
                .Select(p => new PackingLineDto
                {
                    RowNumber = p.RowNumber, BillOfLadingNumber = p.BillOfLadingNumber, PackageNumber = p.PackageNumber, Description = p.Description,
                    ItemId = p.ItemId, ItemCode = p.Item.Code, Quantity = p.Quantity, PackageCount = p.PackageCount, GrossWeight = p.GrossWeight,
                }).ToListAsync(ct);

            bool sees = InventoryPriceAccess.SeesValues(_user);
            document.Freights = await _uow.Set<InventoryFreight>().AsNoTracking().Where(f => f.ReceiptDocumentId == document.Id && !f.Deleted).OrderBy(f => f.Id)
                .Select(f => new FreightSummaryDto { Id = f.Id, Number = f.Number, Status = f.Status, FreightDate = f.FreightDate, Amount = sees ? f.Amount : (decimal?)null })
                .ToListAsync(ct);

            InventoryPriceAccess.Mask(document, InventoryPriceAccess.SeesDocumentPrices(_user));
        }

        /// <summary>Received minus returned quantity per item of one consignment receipt (confirmed documents only).</summary>
        private async Task<Dictionary<int, decimal>> OpenConsignmentAsync(int receiptId, int? excludeDocumentId, CancellationToken ct)
        {
            Dictionary<int, decimal> received = await _uow.Set<InventoryDocumentLine>().AsNoTracking()
                .Where(l => l.DocumentId == receiptId).GroupBy(l => l.ItemId).Select(g => new { g.Key, Q = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
            var returned = await _uow.Set<InventoryDocumentLine>().AsNoTracking()
                .Where(l => l.Document.ConsignmentOfDocumentId == receiptId && l.Document.Type == InventoryDocumentType.ConsignmentIssue && !l.Document.Deleted
                            && l.Document.Status == InventoryDocumentStatus.Confirmed && l.DocumentId != (excludeDocumentId ?? 0))
                .GroupBy(l => l.ItemId).Select(g => new { g.Key, Q = g.Sum(x => x.Quantity) }).ToListAsync(ct);
            foreach (var r in returned) if (received.ContainsKey(r.Key)) received[r.Key] -= r.Q;
            return received;
        }

        // ------------------------------------------------------------------------------- draft checks

        private async Task<string> CheckConsignmentDraftAsync(SaveDocumentDto dto, Warehouse warehouse, int? existingId, CancellationToken ct)
        {
            if (dto.Type == InventoryDocumentType.ConsignmentReceipt)
            {
                if (warehouse.Kind != WarehouseKind.CustomerProperty) return "رسید امانی در انبار اموال مشتری / امانی ثبت می‌شود (کالای امانی ارزش‌گذاری نمی‌شود).";
                if (!dto.ProviderId.HasValue) return "صاحب کالای امانی (تامین‌کننده) را انتخاب کنید.";
                if (dto.ConsignmentDueDate.HasValue && dto.ConsignmentDueDate.Value.Date < dto.DocumentDate.Date) return "سررسید عودت پیش از تاریخ رسید است.";
                if (dto.PurchaseOrderId.HasValue) return "رسید امانی سفارش خرید ندارد.";
                return null;
            }
            if (dto.Type == InventoryDocumentType.ConsignmentIssue)
            {
                if (warehouse.Kind != WarehouseKind.CustomerProperty) return "حواله امانی از انبار اموال مشتری / امانی صادر می‌شود.";
                if (!dto.ConsignmentOfDocumentId.HasValue) return "رسید امانی که کالا به آن برمی‌گردد را انتخاب کنید.";
                var receipt = await Documents.AsNoTracking().Where(d => d.Id == dto.ConsignmentOfDocumentId.Value && !d.Deleted)
                    .Select(d => new { d.Type, d.Status, d.WarehouseId, d.ProviderId }).FirstOrDefaultAsync(ct);
                if (receipt == null || receipt.Type != InventoryDocumentType.ConsignmentReceipt) return "رسید امانی پیدا نشد.";
                if (receipt.Status != InventoryDocumentStatus.Confirmed) return "رسید امانی تاییدشده نیست.";
                if (receipt.WarehouseId != dto.WarehouseId) return "حواله امانی باید از همان انبار رسید امانی صادر شود.";
                if (dto.ProviderId.HasValue && dto.ProviderId != receipt.ProviderId) return "صاحب کالا با رسید امانی یکی نیست.";
                dto.ProviderId = receipt.ProviderId;
                Dictionary<int, decimal> open = await OpenConsignmentAsync(dto.ConsignmentOfDocumentId.Value, existingId, ct);
                foreach (var g in (dto.Lines ?? new List<SaveDocumentLineDto>()).Where(l => l != null).GroupBy(l => l.ItemId))
                    if (!open.TryGetValue(g.Key, out decimal left) || g.Sum(l => l.Quantity) > left)
                        return "مقدار حواله امانی از مانده‌ی عودت‌نشده‌ی رسید امانی بیشتر است.";
                return null;
            }
            dto.ConsignmentDueDate = null;
            dto.ConsignmentOfDocumentId = null;
            return null;
        }

        private async Task<string> CheckConsignmentConfirmAsync(InventoryDocument document, CancellationToken ct)
        {
            if (document.Type != InventoryDocumentType.ConsignmentIssue || !document.ConsignmentOfDocumentId.HasValue) return null;
            Dictionary<int, decimal> open = await OpenConsignmentAsync(document.ConsignmentOfDocumentId.Value, document.Id, ct);
            var lines = await _uow.Set<InventoryDocumentLine>().AsNoTracking().Where(l => l.DocumentId == document.Id).GroupBy(l => l.ItemId)
                .Select(g => new { g.Key, Q = g.Sum(x => x.Quantity) }).ToListAsync(ct);
            foreach (var l in lines)
                if (!open.TryGetValue(l.Key, out decimal left) || l.Q > left) return "مقدار حواله امانی از مانده‌ی عودت‌نشده‌ی رسید امانی بیشتر است.";
            return null;
        }

        private async Task<string> CheckCorrectsAsync(SaveDocumentDto dto, int? existingId, CancellationToken ct)
        {
            if (!dto.CorrectsDocumentId.HasValue) return null;
            var original = await Documents.AsNoTracking().Where(d => d.Id == dto.CorrectsDocumentId.Value && !d.Deleted).Select(d => new { d.Status, d.Type }).FirstOrDefaultAsync(ct);
            if (original == null) return "سند ابطال‌شده‌ی مرجع پیدا نشد.";
            if (original.Status != InventoryDocumentStatus.Cancelled) return "سند اصلاحی فقط برای سند ابطال‌شده ثبت می‌شود.";
            if (original.Type != dto.Type) return "نوع سند اصلاحی با سند ابطال‌شده یکی نیست.";
            bool taken = await Documents.AsNoTracking().AnyAsync(d => d.CorrectsDocumentId == dto.CorrectsDocumentId.Value && !d.Deleted && d.Status != InventoryDocumentStatus.Cancelled && d.Id != (existingId ?? 0), ct);
            return taken ? "برای این سند قبلاً سند اصلاحی ثبت شده است." : null;
        }

        /// <summary>Project, project-consumable and point-of-use warehouses belong to one work order: only that order's issues leave them.</summary>
        private static string CheckProjectWarehouses(SaveDocumentDto dto, Warehouse source, Warehouse destination)
        {
            if (source.WorkOrderId.HasValue && dto.Type == InventoryDocumentType.Issue && dto.WorkOrderId.HasValue && dto.WorkOrderId != source.WorkOrderId)
                return "انبار پروژه فقط به دستور کار همان پروژه حواله می‌دهد.";
            if (destination != null && destination.WorkOrderId.HasValue && dto.WorkOrderId.HasValue && dto.WorkOrderId != destination.WorkOrderId && InventoryDocumentRules.IsTransfer(dto.Type))
                return "انبار مقصد به پروژه‌ی دیگری تعلق دارد.";
            return null;
        }

        private async Task ApplyPb5HeaderAsync(InventoryDocument document, SaveDocumentDto dto, bool isEdit, CancellationToken ct)
        {
            if (dto.Type == InventoryDocumentType.ConsignmentReceipt)
            {
                Dictionary<string, decimal> settings = await Quality.QualitySettingKeys.LoadAsync(_uow, ct);
                document.ConsignmentDueDate = (dto.ConsignmentDueDate ?? ConsignmentRules.DefaultDue(dto.DocumentDate, (int)settings[Quality.QualitySettingKeys.ConsignmentReturnDays])).Date;
            }
            else document.ConsignmentDueDate = null;
            document.ConsignmentOfDocumentId = dto.Type == InventoryDocumentType.ConsignmentIssue ? dto.ConsignmentOfDocumentId : null;
            document.CorrectsDocumentId = dto.CorrectsDocumentId;
            document.GoodsType = InventoryText.Clean(dto.GoodsType);
            document.BillOfLadingNumber = InventoryText.Clean(dto.BillOfLadingNumber);
            // an edit sends a rejected draft back into the normal flow and takes away the accountant's price approval
            document.RejectedAt = null;
            document.RejectedByUserId = null;
            document.RejectReason = null;
            document.PriceApprovedAt = null;
            document.PriceApprovedByUserId = null;

            if (isEdit && document.Id != 0)
            {
                List<InventoryPackingLine> old = await _uow.Set<InventoryPackingLine>().Where(p => p.DocumentId == document.Id).ToListAsync(ct);
                _uow.Set<InventoryPackingLine>().RemoveRange(old);
            }
            if (InventoryDocumentRules.LeavesPremises(dto.Type))
            {
                int row = 1;
                foreach (PackingLineDto p in (dto.PackingLines ?? new List<PackingLineDto>()).Where(x => x != null))
                {
                    if (string.IsNullOrWhiteSpace(p.PackageNumber) && string.IsNullOrWhiteSpace(p.Description) && !p.ItemId.HasValue) continue;
                    _uow.Set<InventoryPackingLine>().Add(new InventoryPackingLine
                    {
                        Document = document, RowNumber = row++, BillOfLadingNumber = InventoryText.Clean(p.BillOfLadingNumber), PackageNumber = InventoryText.Clean(p.PackageNumber),
                        Description = InventoryText.Clean(p.Description), ItemId = p.ItemId, Quantity = p.Quantity, PackageCount = p.PackageCount, GrossWeight = p.GrossWeight,
                    });
                }
            }
        }

        private async Task<string> CheckPb5CancelAsync(InventoryDocument document, CancellationToken ct)
        {
            if (document.Type == InventoryDocumentType.ConsignmentReceipt &&
                await Documents.AnyAsync(d => d.ConsignmentOfDocumentId == document.Id && !d.Deleted && d.Status == InventoryDocumentStatus.Confirmed, ct))
                return "برای این رسید امانی حواله امانی (عودت) تاییدشده وجود دارد؛ ابتدا آن را ابطال کنید.";
            if (await _uow.Set<InventoryFreight>().AnyAsync(f => f.ReceiptDocumentId == document.Id && !f.Deleted && f.Status == FreightStatus.Approved, ct))
            {
                string closed = await ClosedPeriodGuard.MessageAsync(_uow, DateTime.Today, ct);
                if (closed != null) return "کرایه تاییدشده‌ی این رسید در دوره‌ی بسته‌شده برگشت نمی‌خورد: " + closed;
            }
            return null;
        }

        // ------------------------------------------------------------------------------- reject, price approval, correction

        public async Task<ResponseDto<DocumentDto>> RejectAsync(int id, RejectDocumentDto dto, CancellationToken cancellationToken)
        {
            string reason = InventoryText.Clean(dto?.Reason);
            if (reason == null) return ResponseDtoHelper.BadRequest<DocumentDto>("علت رد را بنویسید.");
            if (Ai.AiActing.IsActing) return ResponseDtoHelper.BadRequest<DocumentDto>("رد یا تایید سند کار انسان است.");
            if (!_user.IsInRole(InventoryRoles.Approvers + ",WHSUPERVISOR")) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.NotAllowed);
            InventoryDocument document = await Documents.FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, cancellationToken);
            if (document == null) return ResponseDtoHelper.NotFound<DocumentDto>(InventoryMessages.DocumentNotFound);
            if (document.Status != InventoryDocumentStatus.Draft) return ResponseDtoHelper.BadRequest<DocumentDto>("فقط سند پیش‌نویس رد می‌شود.");
            int me = _user.UserId;
            if (me == document.CreatorUserId || me == document.ModifierUserId) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.SameUser);
            document.RejectedAt = DateTimeOffset.Now;
            document.RejectedByUserId = me;
            document.RejectReason = reason.Length > 500 ? reason.Substring(0, 500) : reason;
            document.WarehouseApprovedAt = null; document.WarehouseApprovedByUserId = null;
            document.PlanningApprovedAt = null; document.PlanningApprovedByUserId = null;
            string error = await SaveAsync(cancellationToken);
            if (error != null) return ResponseDtoHelper.BadRequest<DocumentDto>(error);
            return await GetDocumentAsync(id, cancellationToken);
        }

        public async Task<ResponseDto<DocumentDto>> ApprovePriceAsync(int id, CancellationToken cancellationToken)
        {
            if (Ai.AiActing.IsActing) return ResponseDtoHelper.BadRequest<DocumentDto>("تایید نهایی قیمت کار انسان است.");
            if (!_user.IsInRole(FreightRoles.Approvers)) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.NotAllowed + " (نقش مالی لازم است)");
            InventoryDocument document = await Documents.FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, cancellationToken);
            if (document == null) return ResponseDtoHelper.NotFound<DocumentDto>(InventoryMessages.DocumentNotFound);
            if (document.Type != InventoryDocumentType.PurchaseReceipt || document.Status != InventoryDocumentStatus.Confirmed)
                return ResponseDtoHelper.BadRequest<DocumentDto>("تایید نهایی قیمت فقط برای رسید خرید تاییدشده است.");
            if (document.PriceApprovedAt != null) return ResponseDtoHelper.BadRequest<DocumentDto>("قیمت این رسید قبلاً تایید شده است.");
            int me = _user.UserId;
            if (me == document.CreatorUserId || me == document.ModifierUserId || me == document.ConfirmedByUserId) return ResponseDtoHelper.BadRequest<DocumentDto>(InventoryMessages.SameUser);
            document.PriceApprovedAt = DateTimeOffset.Now;
            document.PriceApprovedByUserId = me;
            string error = await SaveAsync(cancellationToken);
            if (error != null) return ResponseDtoHelper.BadRequest<DocumentDto>(error);
            return await GetDocumentAsync(id, cancellationToken);
        }

        public async Task<ResponseDto<DocumentDto>> CreateCorrectionAsync(int cancelledId, CancellationToken cancellationToken)
        {
            InventoryDocument source = await Documents.AsNoTracking().Include(d => d.Lines).ThenInclude(l => l.Serials)
                .FirstOrDefaultAsync(d => d.Id == cancelledId && !d.Deleted, cancellationToken);
            if (source == null) return ResponseDtoHelper.NotFound<DocumentDto>(InventoryMessages.DocumentNotFound);
            if (source.Status != InventoryDocumentStatus.Cancelled) return ResponseDtoHelper.BadRequest<DocumentDto>("سند اصلاحی فقط از سند ابطال‌شده ساخته می‌شود.");
            if (InventoryDocumentRules.IsSystemOnly(source.Type) || source.NcrId.HasValue || source.InspectionId.HasValue || source.StockCountId.HasValue)
                return ResponseDtoHelper.BadRequest<DocumentDto>("سند سیستمی (کنترل کیفیت یا شمارش) سند اصلاحی ندارد؛ از همان فرایند اصلاح شود.");
            var dto = new SaveDocumentDto
            {
                Type = source.Type, DocumentDate = DateTime.Today, WarehouseId = source.WarehouseId, DestinationWarehouseId = source.DestinationWarehouseId,
                ProviderId = source.ProviderId, BuyOrderId = source.BuyOrderId, PurchaseOrderId = source.PurchaseOrderId, WorkOrderId = source.WorkOrderId, ContractId = source.ContractId,
                SalesOrderId = source.SalesOrderId, ServiceOrderId = source.ServiceOrderId, ReferenceNumber = source.ReferenceNumber,
                Description = "اصلاح سند ابطال‌شده " + source.Number + (string.IsNullOrWhiteSpace(source.Description) ? "" : " | " + source.Description),
                MaterialRequestId = source.MaterialRequestId, DriverName = source.DriverName, VehiclePlate = source.VehiclePlate, ReceiverName = source.ReceiverName,
                Destination = source.Destination, BuyerName = source.BuyerName, MinutesNumber = source.MinutesNumber, DepositReference = source.DepositReference,
                PriceQuotes = source.PriceQuotes, CostCenter = source.CostCenter, ConsignmentOfDocumentId = source.ConsignmentOfDocumentId,
                ConsignmentDueDate = source.ConsignmentDueDate, GoodsType = source.GoodsType, BillOfLadingNumber = source.BillOfLadingNumber, CorrectsDocumentId = source.Id,
                Lines = new List<SaveDocumentLineDto>(),
            };
            foreach (InventoryDocumentLine l in source.Lines.OrderBy(x => x.RowNumber))
                dto.Lines.Add(new SaveDocumentLineDto
                {
                    ItemId = l.ItemId, Quantity = l.Quantity, LotId = l.LotId, LotNumber = l.LotNumber, HeatNumber = l.HeatNumber, CertificateNumber = l.CertificateNumber,
                    LocationId = l.LocationId, DestinationLocationId = l.DestinationLocationId, UnitPrice = l.UnitPrice, PurchaseOrderLineId = l.PurchaseOrderLineId,
                    SalesOrderLineId = l.SalesOrderLineId, Description = l.Description, Direction = l.Direction, SourceRowNumber = l.SourceRowNumber,
                    SerialNumbers = l.Serials.Select(s => s.SerialNumber).ToList(),
                });
            return await SaveDraftCoreAsync(dto, null, cancellationToken);
        }
    }
}
