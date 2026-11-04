using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.InventoryDtos
{
    public class RejectDocumentDto
    {
        public string Reason { get; set; }
    }

    // ------------------------------------------------------------------------------ freight (landed cost)

    public class SaveFreightDto
    {
        public int? Id { get; set; }
        public int ReceiptDocumentId { get; set; }
        public DateTime FreightDate { get; set; }
        public string VehiclePlate { get; set; }
        public string GoodsType { get; set; }
        public string BillOfLadingNumber { get; set; }
        public int? CarrierProviderId { get; set; }

        /// <summary>Rial.</summary>
        public decimal Amount { get; set; }
        public string Description { get; set; }
    }

    public class VoidFreightDto
    {
        public string Reason { get; set; }
    }

    public class FreightQuery
    {
        public int? ReceiptDocumentId { get; set; }
        public FreightStatus? Status { get; set; }
    }

    public class FreightAllocationDto
    {
        public int DocumentLineId { get; set; }
        public int RowNumber { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public FreightBasis Basis { get; set; }
        public decimal BasisAmount { get; set; }
        public decimal Amount { get; set; }
    }

    public class FreightDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ReceiptDocumentId { get; set; }
        public string ReceiptNumber { get; set; }
        public DateTime FreightDate { get; set; }
        public string VehiclePlate { get; set; }
        public string GoodsType { get; set; }
        public string BillOfLadingNumber { get; set; }
        public int? CarrierProviderId { get; set; }
        public string CarrierName { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; }
        public FreightStatus Status { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public string VoidReason { get; set; }
        public List<FreightAllocationDto> Allocations { get; set; } = new List<FreightAllocationDto>();
    }

    // ------------------------------------------------------------------------------ reorder point

    public class ReorderQuery
    {
        public int? ItemGroupId { get; set; }
        public string Platform { get; set; }
        public string Contains { get; set; }
    }

    public class ReorderRowDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public string GroupTitle { get; set; }
        public decimal OnHand { get; set; }
        public decimal OnOrder { get; set; }
        public bool OpenRequest { get; set; }
        public decimal SafetyStock { get; set; }
        public decimal DailyUse { get; set; }
        public int LeadDays { get; set; }
        public decimal ReorderPoint { get; set; }
        public decimal SuggestedQuantity { get; set; }
        public bool FromGroupDefault { get; set; }
    }

    public class DraftReorderRequestDto
    {
        public List<int> ItemIds { get; set; } = new List<int>();
    }

    public class DraftReorderResultDto
    {
        public int RequestId { get; set; }
        public string RequestNumber { get; set; }
        public int Lines { get; set; }
    }

    // ------------------------------------------------------------------------------ alerts

    public class AlertRunResultDto
    {
        public DateTime AsOf { get; set; }
        public int ConsignmentOpen { get; set; }
        public int ConsignmentAlerts { get; set; }
        public int QcPending { get; set; }
        public int QcAlerts { get; set; }
        public int QcEscalations { get; set; }
    }

    // ------------------------------------------------------------------------------ assets, units, ceilings

    public class SaveAssetDto
    {
        public int? Id { get; set; }
        public string Tag { get; set; }
        public string Title { get; set; }
        public int? ItemId { get; set; }
        public int? SerialUnitId { get; set; }
        public int? WarehouseId { get; set; }
        public int? LocationId { get; set; }
        public AssetStatus Status { get; set; } = AssetStatus.InService;
        public DateTime? PurchaseDate { get; set; }
        public decimal? Cost { get; set; }
        public string CustodianUnit { get; set; }
        public string Description { get; set; }
    }

    public class AssetDto
    {
        public int Id { get; set; }
        public string Tag { get; set; }
        public string Title { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public int? SerialUnitId { get; set; }
        public string SerialNumber { get; set; }
        public int? WarehouseId { get; set; }
        public string WarehouseTitle { get; set; }
        public int? LocationId { get; set; }
        public string LocationCode { get; set; }
        public AssetStatus Status { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public decimal? Cost { get; set; }
        public string CustodianUnit { get; set; }
        public string Description { get; set; }

        /// <summary>The text of the barcode / QR label ("A/" and the tag).</summary>
        public string LabelCode { get; set; }
    }

    public class AssetQuery : PaginationParams
    {
        public string Contains { get; set; }
        public AssetStatus? Status { get; set; }
        public int? WarehouseId { get; set; }
    }

    public class SaveUnitConversionDto
    {
        public int? Id { get; set; }
        public int? ItemId { get; set; }
        public string FromUnit { get; set; }
        public string ToUnit { get; set; }
        public decimal Factor { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class UnitConversionDto
    {
        public int Id { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string FromUnit { get; set; }
        public string ToUnit { get; set; }
        public decimal Factor { get; set; }
        public bool IsActive { get; set; }
    }

    public class SaveCeilingDto
    {
        public int? Id { get; set; }
        public int ItemId { get; set; }
        public int? WorkOrderId { get; set; }
        public decimal MaxQuantity { get; set; }
        public string Note { get; set; }
    }

    public class CeilingDto
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public decimal MaxQuantity { get; set; }
        public string Note { get; set; }
    }


    // ------------------------------------------------------------------------------ reports and Excel

    public class WarehouseReportQuery
    {
        public string Report { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int? WarehouseId { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ItemId { get; set; }
        public int? ItemGroupId { get; set; }
        public int? ProviderId { get; set; }
        public int? ContractId { get; set; }
        public string Platform { get; set; }
        public string EquipmentType { get; set; }
        public string ValveModel { get; set; }
        public string Contains { get; set; }

        /// <summary>Request number (PR-... or MR-...) of the request trace.</summary>
        public string RequestNumber { get; set; }
        public WarehouseKind? Kind { get; set; }

        /// <summary>Stagnant stock: only this bucket (1 slow, 2 stagnant, 3 dead).</summary>
        public int? Bucket { get; set; }
    }

    public class ExcelRowIssueDto
    {
        public int Row { get; set; }
        public string Message { get; set; }
    }

    public class ExcelPreviewRowDto
    {
        public int Row { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public string Extra { get; set; }
        public bool Ok { get; set; }
        public string Message { get; set; }
    }

    public class ExcelImportResultDto
    {
        public string Kind { get; set; }
        public bool Preview { get; set; }
        public int RowCount { get; set; }
        public int ValidRows { get; set; }
        public int ErrorRows { get; set; }
        public List<ExcelPreviewRowDto> Rows { get; set; } = new List<ExcelPreviewRowDto>();
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>The draft document (or the stock count) the rows were written to; it is never confirmed by the import.</summary>
        public int? DraftId { get; set; }
        public string DraftKind { get; set; }
    }

    /// <summary>One report table with the filters that produced it (printed on the page and written on the info sheet of the Excel file).</summary>
    public class WarehouseReportDto : ViewModels.NasbNiro.ProcurementDtos.ReportTableDto
    {
        public string Filters { get; set; }
        public DateTimeOffset GeneratedAt { get; set; }

        /// <summary>True when the reader's role does not see money: the money columns are removed.</summary>
        public bool PricesMasked { get; set; }
        public string Note { get; set; }
    }

    public class WarehouseReportCatalogDto
    {
        public string Key { get; set; }
        public string Title { get; set; }

        /// <summary>Filters the report uses: date, warehouse, project, item, provider, group, platform, bucket, request.</summary>
        public List<string> Filters { get; set; } = new List<string>();
    }

    /// <summary>The header fields of an Excel import (sent with the file as form fields).</summary>
    public class ExcelImportRequestDto
    {
        /// <summary>receipt, issue or count.</summary>
        public string Kind { get; set; }
        public int WarehouseId { get; set; }
        public DateTime? DocumentDate { get; set; }
        public int? ProviderId { get; set; }

        /// <summary>An issue is made against an approved material request (no issue without one).</summary>
        public int? MaterialRequestId { get; set; }
        public string Description { get; set; }
        public bool Preview { get; set; } = true;
    }
}
