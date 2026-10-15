using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.InventoryDtos
{
    // ------------------------------------------------------------------------------------------
    // Master data
    // ------------------------------------------------------------------------------------------

    public class SaveItemGroupDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int? ParentId { get; set; }
    }

    public class ItemGroupDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int? ParentId { get; set; }
        public string ParentTitle { get; set; }
        public int ItemsCount { get; set; }
    }

    public class SaveInventoryItemDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ItemGroupId { get; set; }
        public string Unit { get; set; }
        public ItemTrackingMode TrackingMode { get; set; }
        public string TechnicalCode { get; set; }
        public string Material { get; set; }
        public string Standard { get; set; }
        public string Specification { get; set; }
        public decimal? Weight { get; set; }
        public decimal MinStock { get; set; }
        public decimal? MaxStock { get; set; }
        public bool IsActive { get; set; } = true;
        public string SepidarCode { get; set; }
        public string MescCode { get; set; }
        public string LegacyCode { get; set; }
        public bool RequiresIncomingInspection { get; set; } = true;
        public int? ShelfLifeDays { get; set; }
    }

    public class InventoryItemDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ItemGroupId { get; set; }
        public string ItemGroupCode { get; set; }
        public string ItemGroupTitle { get; set; }
        public string Unit { get; set; }
        public ItemTrackingMode TrackingMode { get; set; }
        public string TechnicalCode { get; set; }
        public string Material { get; set; }
        public string Standard { get; set; }
        public string Specification { get; set; }
        public decimal? Weight { get; set; }
        public decimal MinStock { get; set; }
        public decimal? MaxStock { get; set; }
        public bool IsActive { get; set; }
        public string SepidarCode { get; set; }
        public decimal OnHand { get; set; }
        public string GroupCode { get; set; }
        public string StageCode { get; set; }
        public string BaseCode { get; set; }
        public string MescCode { get; set; }
        public string LegacyCode { get; set; }
        public bool RequiresIncomingInspection { get; set; }
        public int? ShelfLifeDays { get; set; }
    }

    public class InventoryItemQuery : PaginationParams
    {
        public string Contains { get; set; }
        public string GroupCode { get; set; }
        public string BaseCode { get; set; }
        public int? ItemGroupId { get; set; }
        public ItemTrackingMode? TrackingMode { get; set; }
        public bool? IsActive { get; set; }
    }

    /// <summary>One row of the part coding import (from the Excel file of the current warehouse software).</summary>
    public class ImportItemRowDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string GroupCode { get; set; }
        public string GroupTitle { get; set; }
        public string Unit { get; set; }

        /// <summary>"none"/"lot"/"serial", the Persian words, or 0/1/2.</summary>
        public string TrackingMode { get; set; }

        public string TechnicalCode { get; set; }
        public string Material { get; set; }
        public string Standard { get; set; }
        public string Specification { get; set; }
        public decimal? Weight { get; set; }
        public decimal? MinStock { get; set; }
        public string SepidarCode { get; set; }

        /// <summary>Old code (RayDana / Excel); not used as the item code.</summary>
        public string LegacyCode { get; set; }

        public string MescCode { get; set; }
    }

    public class ImportItemsResultDto
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int GroupsCreated { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>Imported, but the code does not follow WI-WH-002.</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class SaveWarehouseDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public WarehouseKind Kind { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; } = true;
        public string SepidarCode { get; set; }
    }

    public class WarehouseDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public WarehouseKind Kind { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public string SepidarCode { get; set; }
        public List<LocationDto> Locations { get; set; } = new List<LocationDto>();
    }

    public class SaveLocationDto
    {
        public int? Id { get; set; }
        public int WarehouseId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class LocationDto
    {
        public int Id { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseCode { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public bool IsActive { get; set; }
    }

    // ------------------------------------------------------------------------------------------
    // Documents
    // ------------------------------------------------------------------------------------------

    public class SaveDocumentDto
    {
        public int? Id { get; set; }
        public InventoryDocumentType Type { get; set; }
        public DateTime DocumentDate { get; set; }
        public int WarehouseId { get; set; }
        public int? DestinationWarehouseId { get; set; }
        public int? ProviderId { get; set; }
        public int? BuyOrderId { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public string ReferenceNumber { get; set; }
        public string Description { get; set; }
        public int? MaterialRequestId { get; set; }
        public string DriverName { get; set; }
        public string VehiclePlate { get; set; }
        public string ReceiverName { get; set; }
        public string Destination { get; set; }
        public string BuyerName { get; set; }
        public string MinutesNumber { get; set; }
        public Guid? WeighbridgeDocumentId { get; set; }
        public string DepositReference { get; set; }
        public List<SaveDocumentLineDto> Lines { get; set; } = new List<SaveDocumentLineDto>();
    }

    public class ApproveDocumentDto
    {
        public ApprovalKind Kind { get; set; }
    }

    public class ExitCheckDto
    {
        public string Note { get; set; }
    }

    public class SaveDocumentLineDto
    {
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public string CertificateNumber { get; set; }
        public int? LocationId { get; set; }
        public int? DestinationLocationId { get; set; }
        public decimal? UnitPrice { get; set; }
        public string Description { get; set; }
        public DocumentLineDirection Direction { get; set; }
        public int? SourceRowNumber { get; set; }
        public List<string> SerialNumbers { get; set; } = new List<string>();
    }

    public class DocumentQuery : PaginationParams
    {
        public InventoryDocumentType? Type { get; set; }
        public InventoryDocumentStatus? Status { get; set; }
        public int? WarehouseId { get; set; }
        public int? WorkOrderId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string Contains { get; set; }
    }

    public class CancelDocumentDto
    {
        public string Reason { get; set; }
    }

    public class DocumentListItemDto
    {
        public int Id { get; set; }
        public InventoryDocumentType Type { get; set; }
        public string Number { get; set; }
        public DateTime DocumentDate { get; set; }
        public InventoryDocumentStatus Status { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseTitle { get; set; }
        public string DestinationWarehouseTitle { get; set; }
        public string ProviderName { get; set; }
        public string WorkOrderCode { get; set; }
        public string ContractCode { get; set; }
        public string ReferenceNumber { get; set; }
        public string Description { get; set; }
        public int LinesCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public SepidarSyncStatus SepidarStatus { get; set; }
        public DateTimeOffset? SepidarExportedAt { get; set; }
    }

    public class DocumentDto
    {
        public int Id { get; set; }
        public InventoryDocumentType Type { get; set; }
        public string Number { get; set; }
        public DateTime DocumentDate { get; set; }
        public InventoryDocumentStatus Status { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseTitle { get; set; }
        public int? DestinationWarehouseId { get; set; }
        public string DestinationWarehouseTitle { get; set; }
        public int? ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int? BuyOrderId { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public string ReferenceNumber { get; set; }
        public string Description { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public string ConfirmedByName { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }
        public string CancelledByName { get; set; }
        public string CancelReason { get; set; }
        public SepidarSyncStatus SepidarStatus { get; set; }
        public string SepidarReference { get; set; }
        public WarehouseKind WarehouseKind { get; set; }
        public WarehouseKind? DestinationWarehouseKind { get; set; }
        public int? MaterialRequestId { get; set; }
        public string MaterialRequestNumber { get; set; }
        public int? NcrId { get; set; }
        public string NcrNumber { get; set; }
        public int? InspectionId { get; set; }
        public int? StockCountId { get; set; }
        public string DriverName { get; set; }
        public string VehiclePlate { get; set; }
        public string ReceiverName { get; set; }
        public string Destination { get; set; }
        public DateTimeOffset? ExitCheckedAt { get; set; }
        public string ExitCheckedByName { get; set; }
        public string ExitCheckNote { get; set; }
        public DateTimeOffset? QcLoadingApprovedAt { get; set; }
        public string QcLoadingApprovedByName { get; set; }
        public string BuyerName { get; set; }
        public string MinutesNumber { get; set; }
        public Guid? WeighbridgeDocumentId { get; set; }
        public string DepositReference { get; set; }
        public DateTimeOffset? WarehouseApprovedAt { get; set; }
        public string WarehouseApprovedByName { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public string PlanningApprovedByName { get; set; }
        public List<DocumentLineDto> Lines { get; set; } = new List<DocumentLineDto>();
    }

    public class DocumentLineDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public ItemTrackingMode TrackingMode { get; set; }
        public decimal Quantity { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public string CertificateNumber { get; set; }
        public int? LocationId { get; set; }
        public string LocationCode { get; set; }
        public int? DestinationLocationId { get; set; }
        public string DestinationLocationCode { get; set; }
        public decimal? UnitPrice { get; set; }
        public string Description { get; set; }
        public DocumentLineDirection Direction { get; set; }
        public int? SourceRowNumber { get; set; }
        public List<string> SerialNumbers { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------------------------------
    // Stock
    // ------------------------------------------------------------------------------------------

    public class StockQuery : PaginationParams
    {
        public string Contains { get; set; }
        public int? ItemId { get; set; }
        public int? WarehouseId { get; set; }
        public int? ItemGroupId { get; set; }
        public bool LowStockOnly { get; set; }
    }

    public class StockBalanceDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseTitle { get; set; }
        public int? LocationId { get; set; }
        public string LocationCode { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public decimal Quantity { get; set; }
    }

    public class ItemStockDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public string ItemGroupTitle { get; set; }
        public ItemTrackingMode TrackingMode { get; set; }
        public decimal OnHand { get; set; }
        public decimal MinStock { get; set; }
        public bool IsLow { get; set; }
    }

    public class KardexQuery
    {
        public int ItemId { get; set; }
        public int? WarehouseId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public class KardexRowDto
    {
        public DateTime Date { get; set; }
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; }
        public InventoryDocumentType DocumentType { get; set; }
        public string WarehouseTitle { get; set; }
        public string LocationCode { get; set; }
        public string LotNumber { get; set; }
        public string SerialNumber { get; set; }
        public decimal In { get; set; }
        public decimal Out { get; set; }
        public decimal Balance { get; set; }
        public decimal? UnitPrice { get; set; }
        public bool IsReversal { get; set; }
    }

    public class KardexDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal ClosingBalance { get; set; }
        public List<KardexRowDto> Rows { get; set; } = new List<KardexRowDto>();
    }

    // ------------------------------------------------------------------------------------------
    // Lots and serials
    // ------------------------------------------------------------------------------------------

    public class LotQuery : PaginationParams
    {
        public string Contains { get; set; }
        public int? ItemId { get; set; }
    }

    public class SaveLotDto
    {
        public int? Id { get; set; }
        public int ItemId { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public string CertificateNumber { get; set; }
        public string CertificateType { get; set; }
        public Guid? CertificateDocumentId { get; set; }
        public int? ProviderId { get; set; }
        public DateTime? ProductionDate { get; set; }
        public string Description { get; set; }
    }

    public class LotDto
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public string CertificateNumber { get; set; }
        public string CertificateType { get; set; }
        public Guid? CertificateDocumentId { get; set; }
        public int? ProviderId { get; set; }
        public string ProviderName { get; set; }
        public DateTime? ProductionDate { get; set; }
        public string Description { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public int? ParentLotId { get; set; }
        public string ParentLotNumber { get; set; }
        public string ParentItemCode { get; set; }
        public bool IsCustomerProperty { get; set; }
        public decimal OnHand { get; set; }
    }

    public class SerialQuery : PaginationParams
    {
        public string Contains { get; set; }
        public int? ItemId { get; set; }
        public SerialUnitStatus? Status { get; set; }
        public int? WarehouseId { get; set; }
        public int? WorkOrderId { get; set; }
    }

    public class CreateSerialDto
    {
        public int ItemId { get; set; }

        /// <summary>Empty: a number is generated (SN-1405-000001).</summary>
        public string SerialNumber { get; set; }

        public int? LotId { get; set; }
        public int? WorkOrderId { get; set; }
        public string Description { get; set; }

        /// <summary>Create the manufacturing stages defined for the item at once.</summary>
        public bool ApplyRouting { get; set; } = true;
    }

    public class UpdateSerialDto
    {
        public int Id { get; set; }
        public int? WorkOrderId { get; set; }
        public string Description { get; set; }
    }

    public class SerialUnitDto
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string SerialNumber { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public SerialUnitStatus Status { get; set; }
        public int? WarehouseId { get; set; }
        public string WarehouseTitle { get; set; }
        public string LocationCode { get; set; }
        public int? ParentSerialUnitId { get; set; }
        public string ParentSerialNumber { get; set; }
        public string ParentItemTitle { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public string Description { get; set; }
        public DateTimeOffset CreateDate { get; set; }

        /// <summary>Previous stage of the part (old label) it was made from.</summary>
        public int? SourceSerialUnitId { get; set; }
        public string SourceSerialNumber { get; set; }
        public string SourceItemCode { get; set; }
        public int? SourceLotId { get; set; }
        public string SourceLotNumber { get; set; }
    }

    // ------------------------------------------------------------------------------------------
    // Traceability
    // ------------------------------------------------------------------------------------------

    public class SaveOperationTemplateDto
    {
        public int? Id { get; set; }
        public int? ItemGroupId { get; set; }
        public int? ItemId { get; set; }
        public int Sequence { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool RequiresQc { get; set; }
        public int? MachineId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class OperationTemplateDto
    {
        public int Id { get; set; }
        public int? ItemGroupId { get; set; }
        public string ItemGroupTitle { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public int Sequence { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool RequiresQc { get; set; }
        public int? MachineId { get; set; }
        public string MachineTitle { get; set; }
        public bool IsActive { get; set; }
    }

    public class OperationTemplateQuery
    {
        public int? ItemId { get; set; }
        public int? ItemGroupId { get; set; }
    }

    public class ApplyRoutingDto
    {
        public int? SerialUnitId { get; set; }
        public int? LotId { get; set; }
        public int? WorkOrderId { get; set; }
    }

    public class AddPartOperationDto
    {
        public int? SerialUnitId { get; set; }
        public int? LotId { get; set; }
        public string Title { get; set; }
        public int Sequence { get; set; }
        public bool RequiresQc { get; set; }
        public int? MachineId { get; set; }
        public int? WorkOrderId { get; set; }
        public string Notes { get; set; }
    }

    public class UpdatePartOperationDto
    {
        public int Id { get; set; }

        /// <summary>"start", "finish", "reopen" or "edit".</summary>
        public string Action { get; set; }

        public string OperatorName { get; set; }
        public int? MachineId { get; set; }
        public string Result { get; set; }
        public string Notes { get; set; }
    }

    public class QcPartOperationDto
    {
        public int Id { get; set; }
        public QcStatus Status { get; set; }
        public string Note { get; set; }
    }

    public class AddOperationFileDto
    {
        public int PartOperationId { get; set; }
        public Guid DocumentId { get; set; }
        public string Title { get; set; }
    }

    public class PartOperationFileDto
    {
        public int Id { get; set; }
        public Guid DocumentId { get; set; }
        public string Title { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public class PartOperationDto
    {
        public int Id { get; set; }
        public int? SerialUnitId { get; set; }
        public string SerialNumber { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public int? OperationTemplateId { get; set; }
        public int Sequence { get; set; }
        public string Title { get; set; }
        public PartOperationStatus Status { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string OperatorName { get; set; }
        public int? MachineId { get; set; }
        public string MachineTitle { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public string Result { get; set; }
        public QcStatus QcStatus { get; set; }
        public string QcByName { get; set; }
        public DateTimeOffset? QcAt { get; set; }
        public string QcNote { get; set; }
        public string Notes { get; set; }
        public List<PartOperationFileDto> Files { get; set; } = new List<PartOperationFileDto>();
    }

    public class PartOperationQuery : PaginationParams
    {
        public QcStatus? QcStatus { get; set; }
        public PartOperationStatus? Status { get; set; }
        public int? WorkOrderId { get; set; }
        public string Contains { get; set; }
    }

    public class InstallPartDto
    {
        public int ParentSerialUnitId { get; set; }
        public int? ChildSerialUnitId { get; set; }
        public int? ChildLotId { get; set; }
        public decimal? Quantity { get; set; }
        public int? WorkOrderId { get; set; }
        public string Notes { get; set; }
    }

    public class RemovePartDto
    {
        public int AssemblyLinkId { get; set; }
        public string Notes { get; set; }
    }

    public class AssemblyLinkDto
    {
        public int Id { get; set; }
        public int ParentSerialUnitId { get; set; }
        public string ParentSerialNumber { get; set; }
        public string ParentItemCode { get; set; }
        public string ParentItemTitle { get; set; }
        public int? ChildSerialUnitId { get; set; }
        public string ChildSerialNumber { get; set; }
        public int? ChildLotId { get; set; }
        public string ChildLotNumber { get; set; }
        public string ChildHeatNumber { get; set; }
        public string ChildItemCode { get; set; }
        public string ChildItemTitle { get; set; }
        public decimal Quantity { get; set; }
        public DateTimeOffset InstalledAt { get; set; }
        public string InstalledByName { get; set; }
        public DateTimeOffset? RemovedAt { get; set; }
        public string RemovedByName { get; set; }
        public string WorkOrderCode { get; set; }
        public string Notes { get; set; }
    }

    public class MovementDto
    {
        public DateTime Date { get; set; }
        public int DocumentId { get; set; }
        public string DocumentNumber { get; set; }
        public InventoryDocumentType DocumentType { get; set; }
        public string WarehouseTitle { get; set; }
        public string LocationCode { get; set; }
        public string SerialNumber { get; set; }
        public decimal Quantity { get; set; }
        public bool IsReversal { get; set; }
        public string ProviderName { get; set; }
        public string WorkOrderCode { get; set; }
    }

    public class SerialTraceDto
    {
        public SerialUnitDto Unit { get; set; }
        public LotDto Lot { get; set; }

        /// <summary>From the direct parent up to the top assembly (for example the turbine).</summary>
        public List<SerialUnitDto> Ancestors { get; set; } = new List<SerialUnitDto>();

        public List<SerialUnitDto> Children { get; set; } = new List<SerialUnitDto>();
        public List<AssemblyLinkDto> LotComponents { get; set; } = new List<AssemblyLinkDto>();
        public List<AssemblyLinkDto> AssemblyHistory { get; set; } = new List<AssemblyLinkDto>();
        public List<PartOperationDto> Operations { get; set; } = new List<PartOperationDto>();
        public List<MovementDto> Movements { get; set; } = new List<MovementDto>();
    }

    public class LotTraceDto
    {
        public LotDto Lot { get; set; }
        public List<StockBalanceDto> Balances { get; set; } = new List<StockBalanceDto>();
        public List<MovementDto> Movements { get; set; } = new List<MovementDto>();
        public List<SerialUnitDto> Serials { get; set; } = new List<SerialUnitDto>();
        public List<AssemblyLinkDto> UsedIn { get; set; } = new List<AssemblyLinkDto>();

        /// <summary>The top assemblies (turbines) that contain a piece of this lot.</summary>
        public List<SerialUnitDto> TopAssemblies { get; set; } = new List<SerialUnitDto>();

        public List<PartOperationDto> Operations { get; set; } = new List<PartOperationDto>();
    }

    public class TraceSearchResultDto
    {
        public List<InventoryItemDto> Items { get; set; } = new List<InventoryItemDto>();
        public List<SerialUnitDto> Serials { get; set; } = new List<SerialUnitDto>();
        public List<LotDto> Lots { get; set; } = new List<LotDto>();
        public List<LocationDto> Locations { get; set; } = new List<LocationDto>();
    }

    // ------------------------------------------------------------------------------------------
    // Sepidar
    // ------------------------------------------------------------------------------------------

    public class SaveSepidarMappingDto
    {
        /// <summary>"Provider", "WorkOrder" or "Contract".</summary>
        public string EntityType { get; set; }

        public int EntityId { get; set; }
        public string SepidarCode { get; set; }
    }

    public class SepidarMappingDto
    {
        public string EntityType { get; set; }
        public int EntityId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string SepidarCode { get; set; }
    }

    public class SepidarOutboxQuery : PaginationParams
    {
        public SepidarSyncStatus? Status { get; set; }
    }

    public class SepidarExportDto
    {
        /// <summary>Empty: all documents waiting for Sepidar.</summary>
        public List<int> DocumentIds { get; set; } = new List<int>();

        public bool MarkExported { get; set; } = true;
    }

    public class SepidarCheckDto
    {
        public int PendingDocuments { get; set; }
        public List<string> MissingCodes { get; set; } = new List<string>();
    }
}
