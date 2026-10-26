using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.InventoryDtos
{
    // ------------------------------------------------------------------------------------------
    // Coding (WI-WH-002) and new item requests
    // ------------------------------------------------------------------------------------------

    public class CodingValueDto
    {
        public int Id { get; set; }
        public string Scheme { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Model { get; set; }
        public int? RangeFrom { get; set; }
        public int? RangeTo { get; set; }
        public bool IsStockable { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public string Description { get; set; }
    }

    public class SaveCodingValueDto
    {
        public int? Id { get; set; }
        public string Scheme { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Model { get; set; }
        public int? RangeFrom { get; set; }
        public int? RangeTo { get; set; }
        public bool IsStockable { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public string Description { get; set; }
    }

    public class CodeSegmentDto
    {
        public string Title { get; set; }
        public string Value { get; set; }
        public string Meaning { get; set; }
    }

    public class CodeCheckDto
    {
        public string Code { get; set; }
        public bool IsValid { get; set; }
        public string GroupCode { get; set; }
        public string StageCode { get; set; }
        public string BaseCode { get; set; }
        public string MescCode { get; set; }
        public bool StageNotStockable { get; set; }
        public List<CodeSegmentDto> Segments { get; set; } = new List<CodeSegmentDto>();
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Other stages of the same part (same code without the part status).</summary>
        public List<string> OtherStages { get; set; } = new List<string>();
    }

    public class SaveItemRequestDto
    {
        public int? Id { get; set; }
        public string Title { get; set; }
        public string GroupCode { get; set; }
        public string ProposedCode { get; set; }
        public string TechnicalCode { get; set; }
        public string Material { get; set; }
        public string Standard { get; set; }
        public string MescCode { get; set; }
        public string Unit { get; set; }
        public ItemTrackingMode TrackingMode { get; set; }
        public string Specification { get; set; }
        public string Reason { get; set; }
    }

    public class ReviewItemRequestDto
    {
        public int Id { get; set; }
        public bool Approve { get; set; }

        /// <summary>Final code given by planning (defaults to the proposed code).</summary>
        public string Code { get; set; }

        public string Note { get; set; }
    }

    public class ItemRequestDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string GroupCode { get; set; }
        public string ProposedCode { get; set; }
        public string TechnicalCode { get; set; }
        public string Material { get; set; }
        public string Standard { get; set; }
        public string MescCode { get; set; }
        public string Unit { get; set; }
        public ItemTrackingMode TrackingMode { get; set; }
        public string Specification { get; set; }
        public string Reason { get; set; }
        public ItemRequestStatus Status { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public int CreatorUserId { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public int? ReviewedByUserId { get; set; }
        public string ReviewedByName { get; set; }
        public string ReviewNote { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
    }

    // ------------------------------------------------------------------------------------------
    // Incoming inspection and NCR
    // ------------------------------------------------------------------------------------------

    public class InspectionQuery : PaginationParams
    {
        public InspectionStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class InspectionDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ReceiptDocumentId { get; set; }
        public string ReceiptNumber { get; set; }
        public InventoryDocumentType ReceiptType { get; set; }
        public DateTime ReceiptDate { get; set; }
        public string ProviderName { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public ItemTrackingMode TrackingMode { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseTitle { get; set; }
        public decimal Quantity { get; set; }
        public List<string> SerialNumbers { get; set; } = new List<string>();
        public int? TargetWarehouseId { get; set; }
        public string TargetWarehouseTitle { get; set; }
        public InspectionStatus Status { get; set; }
        public decimal AcceptedQuantity { get; set; }
        public decimal RejectedQuantity { get; set; }
        public List<string> RejectedSerialNumbers { get; set; } = new List<string>();
        public string ReportNumber { get; set; }
        public Guid? ReportDocumentId { get; set; }
        public string Note { get; set; }
        public DateTimeOffset? InspectedAt { get; set; }
        public int? InspectedByUserId { get; set; }
        public string InspectedByName { get; set; }
        public int? ReleaseDocumentId { get; set; }
        public string ReleaseDocumentNumber { get; set; }
        public int? NcrId { get; set; }
        public string NcrNumber { get; set; }

        /// <summary>Instruments used for the measurements (calibration checked on the day of the inspection).</summary>
        public List<int> EquipmentIds { get; set; } = new List<int>();

        /// <summary>The inspection of goods that came back from a rework decision of this NCR (the NCR closes only after this inspection accepts them).</summary>
        public int? ReinspectionOfNcrId { get; set; }
        public string ReinspectionOfNcrNumber { get; set; }
    }

    public class RecordInspectionDto
    {
        public int Id { get; set; }
        public decimal AcceptedQuantity { get; set; }
        public decimal RejectedQuantity { get; set; }

        /// <summary>For serial items: the rejected pieces; the others are accepted.</summary>
        public List<string> RejectedSerialNumbers { get; set; } = new List<string>();

        public int? TargetWarehouseId { get; set; }
        public int? TargetLocationId { get; set; }
        public string ReportNumber { get; set; }
        public Guid? ReportDocumentId { get; set; }
        public string Note { get; set; }

        /// <summary>Description of the non-conformity, copied to the NCR of the rejected quantity.</summary>
        public string RejectReason { get; set; }

        /// <summary>True when values were measured (dimensions, thickness, hardness...): the instruments are then mandatory. A visual or document check leaves it false.</summary>
        public bool Measured { get; set; }

        public List<int> EquipmentIds { get; set; } = new List<int>();
    }

    public class NcrQuery : PaginationParams
    {
        public NcrStatus? Status { get; set; }
        public string Contains { get; set; }
        public int? ItemId { get; set; }
    }

    public class SaveNcrDto
    {
        public int? Id { get; set; }
        public DateTime ReportDate { get; set; }
        public NcrSource Source { get; set; }
        public NcrSeverity Severity { get; set; }
        public int ItemId { get; set; }
        public int? LotId { get; set; }
        public decimal Quantity { get; set; }
        public List<string> SerialNumbers { get; set; } = new List<string>();
        public int? WarehouseId { get; set; }
        public int? ProviderId { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public bool OwnedByContractor { get; set; }
        public string Requirement { get; set; }
        public string Description { get; set; }
        public NcrCauseCategory CauseCategory { get; set; }
        public string RootCause { get; set; }
        public NcrDisposition Disposition { get; set; }
        public string DispositionNote { get; set; }
        public string CommitteeMinutesNumber { get; set; }
        public string CommitteeMembers { get; set; }
        public string ConcessionNumber { get; set; }
        public bool CustomerApprovalRequired { get; set; }
        public string CustomerApprovalReference { get; set; }
        public int? TargetWarehouseId { get; set; }
        public string CorrectiveAction { get; set; }
        public string CorrectiveActionOwner { get; set; }
        public DateTime? CorrectiveActionDueDate { get; set; }

        // quality module additions (spec 5.5)
        public DateTime? DueDate { get; set; }
        public Entities.Model.NasbNiro.Quality.RootCauseMethod RootCauseMethod { get; set; }
        public decimal? CostImpact { get; set; }
        public int? TimeImpactDays { get; set; }
        public string CustomerReturnRef { get; set; }
        public int? AffectedEquipmentId { get; set; }
    }

    public class ApproveNcrDto
    {
        public int Id { get; set; }

        /// <summary>Quality, Engineering, Manufacturing, or Engineering for the concession (Concession = true).</summary>
        public ApprovalKind Kind { get; set; }

        public bool Concession { get; set; }
    }

    public class ExecuteNcrDto
    {
        public int Id { get; set; }
        public DateTime? DocumentDate { get; set; }
        public int? TargetWarehouseId { get; set; }
        public int? TargetLocationId { get; set; }

        // evidence for a product that is not held in a warehouse (found in the workshop); R1q D5

        /// <summary>Rework or repair: the finished rework operation of the production order (it is also found by itself when the production module linked it to the NCR).</summary>
        public int? ReworkOperationId { get; set; }

        /// <summary>Rework or repair done outside a production order: QcRecord or TestReport, and the accepted re-inspection record.</summary>
        public string ReinspectionKind { get; set; }

        public int? ReinspectionRecordId { get; set; }

        /// <summary>Scrap: the confirmed scrap receipt document of the product.</summary>
        public int? ScrapDocumentId { get; set; }

        /// <summary>Scrap of a serial item: the serial unit whose status is now Scrapped.</summary>
        public int? ScrappedSerialUnitId { get; set; }

        /// <summary>Return to the supplier of a product that is not in a warehouse: the confirmed supplier-return document.</summary>
        public int? ReturnDocumentId { get; set; }
    }

    public class CloseNcrDto
    {
        public int Id { get; set; }
        public string EffectivenessCheck { get; set; }
        public bool Cancel { get; set; }
        public string Reason { get; set; }

        /// <summary>Rework and repair close only with an accepted re-inspection; when it is not linked yet: QcInspection, QcRecord or TestReport and its id.</summary>
        public string ReinspectionKind { get; set; }

        public int? ReinspectionRecordId { get; set; }
    }

    public class NcrListItemDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime ReportDate { get; set; }
        public NcrSource Source { get; set; }
        public NcrStatus Status { get; set; }
        public NcrSeverity Severity { get; set; }
        public NcrDisposition Disposition { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public decimal Quantity { get; set; }
        public string ProviderName { get; set; }
        public string WorkOrderCode { get; set; }
    }

    public class NcrDto : NcrListItemDto
    {
        public int ItemId { get; set; }
        public string Unit { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public List<string> SerialNumbers { get; set; } = new List<string>();
        public int? WarehouseId { get; set; }
        public string WarehouseTitle { get; set; }
        public int? ProviderId { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public int? InspectionId { get; set; }
        public int? ReceiptDocumentId { get; set; }
        public string ReceiptNumber { get; set; }
        public bool OwnedByContractor { get; set; }
        public string Requirement { get; set; }
        public string Description { get; set; }
        public NcrCauseCategory CauseCategory { get; set; }
        public string RootCause { get; set; }
        public string DispositionNote { get; set; }
        public string CommitteeMinutesNumber { get; set; }
        public string CommitteeMembers { get; set; }
        public DateTimeOffset? QualityApprovedAt { get; set; }
        public string QualityApprovedByName { get; set; }
        public DateTimeOffset? EngineeringApprovedAt { get; set; }
        public string EngineeringApprovedByName { get; set; }
        public DateTimeOffset? ManufacturingApprovedAt { get; set; }
        public string ManufacturingApprovedByName { get; set; }
        public string ConcessionNumber { get; set; }
        public DateTimeOffset? ConcessionApprovedAt { get; set; }
        public string ConcessionApprovedByName { get; set; }
        public bool CustomerApprovalRequired { get; set; }
        public string CustomerApprovalReference { get; set; }
        public int? TargetWarehouseId { get; set; }
        public int? ResultDocumentId { get; set; }
        public string ResultDocumentNumber { get; set; }
        public DateTimeOffset? ExecutedAt { get; set; }
        public string CorrectiveAction { get; set; }
        public string CorrectiveActionOwner { get; set; }
        public DateTime? CorrectiveActionDueDate { get; set; }
        public string EffectivenessCheck { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string ClosedByName { get; set; }
        public string CreatorName { get; set; }

        // quality module additions (spec 5.5)
        public DateTime? DueDate { get; set; }
        public bool Overdue { get; set; }
        public Entities.Model.NasbNiro.Quality.RootCauseMethod RootCauseMethod { get; set; }
        public int? CapaId { get; set; }
        public string CapaNumber { get; set; }
        public decimal? CostImpact { get; set; }
        public int? TimeImpactDays { get; set; }
        public string CustomerReturnRef { get; set; }
        public int? AffectedEquipmentId { get; set; }
        public string SourceRecordNumber { get; set; }

        // R1q D4, D5, D3
        public string ReinspectionKind { get; set; }
        public int? ReinspectionRecordId { get; set; }
        public int? ScrapDocumentId { get; set; }
        public int? ScrappedSerialUnitId { get; set; }

        /// <summary>The finished rework operation of the production order; filled when the NCR is executed.</summary>
        public int? ReworkOperationId { get; set; }

        public string CancelReason { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }
        public string CancelledByName { get; set; }
    }

    /// <summary>Everything the commercial unit needs to follow a return to the supplier.</summary>
    public class SupplierReturnReportDto
    {
        public string NcrNumber { get; set; }
        public string ProviderName { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? Amount { get; set; }
        public string ReceiptNumber { get; set; }
        public DateTime? ReceiptDate { get; set; }
        public string InvoiceNumber { get; set; }
        public int? BuyOrderId { get; set; }
        public string BuyOrderTitle { get; set; }
        public string FinanceNumber { get; set; }
        public decimal? FinancePrice { get; set; }
        public string PaymentState { get; set; }
        public string WorkOrderCode { get; set; }
        public string ContractCode { get; set; }
        public string Description { get; set; }
        public string ReturnDocumentNumber { get; set; }
    }

    // ------------------------------------------------------------------------------------------
    // Material requests
    // ------------------------------------------------------------------------------------------

    public class MaterialRequestQuery : PaginationParams
    {
        public MaterialRequestStatus? Status { get; set; }
        public int? WorkOrderId { get; set; }
        public string Contains { get; set; }
    }

    public class SaveMaterialRequestDto
    {
        public int? Id { get; set; }
        public DateTime RequestDate { get; set; }
        public int? WorkOrderId { get; set; }
        public int? WarehouseId { get; set; }
        public string Purpose { get; set; }
        public List<SaveMaterialRequestLineDto> Lines { get; set; } = new List<SaveMaterialRequestLineDto>();
    }

    public class SaveMaterialRequestLineDto
    {
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public string Description { get; set; }
    }

    public class MaterialRequestActionDto
    {
        public int Id { get; set; }

        /// <summary>submit, approve-planning, approve-manufacturing, reject, close</summary>
        public string Action { get; set; }

        public string Reason { get; set; }
    }

    public class MaterialRequestListItemDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public DateTime RequestDate { get; set; }
        public MaterialRequestStatus Status { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public string WarehouseTitle { get; set; }
        public string Purpose { get; set; }
        public int LinesCount { get; set; }
        public bool PlanningApproved { get; set; }
        public bool ManufacturingApproved { get; set; }
    }

    public class MaterialRequestDto : MaterialRequestListItemDto
    {
        public int? WarehouseId { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public string PlanningApprovedByName { get; set; }
        public DateTimeOffset? ManufacturingApprovedAt { get; set; }
        public string ManufacturingApprovedByName { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectedByName { get; set; }
        public string RejectReason { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public List<MaterialRequestLineDto> Lines { get; set; } = new List<MaterialRequestLineDto>();
        public List<string> Issues { get; set; } = new List<string>();
    }

    public class MaterialRequestLineDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public ItemTrackingMode TrackingMode { get; set; }
        public decimal Quantity { get; set; }
        public decimal IssuedQuantity { get; set; }
        public decimal Remaining { get; set; }

        /// <summary>Usable stock (not in quarantine, scrap, at contractors or customer property).</summary>
        public decimal Available { get; set; }

        public decimal Shortage { get; set; }
        public string Description { get; set; }
    }

    // ------------------------------------------------------------------------------------------
    // Stock counts
    // ------------------------------------------------------------------------------------------

    public class StartStockCountDto
    {
        public StockCountKind Kind { get; set; }
        public DateTime CountDate { get; set; }
        public int WarehouseId { get; set; }
        public int? LocationId { get; set; }
        public string GroupCode { get; set; }
        public string Description { get; set; }
    }

    public class SaveCountLinesDto
    {
        public int Id { get; set; }
        public List<CountLineEntryDto> Lines { get; set; } = new List<CountLineEntryDto>();
    }

    public class CountLineEntryDto
    {
        /// <summary>Existing line; empty for an item found that was not in the snapshot.</summary>
        public int? LineId { get; set; }

        public int? ItemId { get; set; }
        public int? LocationId { get; set; }
        public int? LotId { get; set; }
        public decimal? CountedQuantity { get; set; }
        public decimal? RecountedQuantity { get; set; }
        public string Note { get; set; }
    }

    public class StockCountActionDto
    {
        public int Id { get; set; }

        /// <summary>submit, approve-warehouse, approve-planning, post, cancel</summary>
        public string Action { get; set; }
    }

    public class StockCountListItemDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public StockCountKind Kind { get; set; }
        public StockCountStatus Status { get; set; }
        public DateTime CountDate { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseTitle { get; set; }
        public string GroupCode { get; set; }
        public int LinesCount { get; set; }
        public int DifferencesCount { get; set; }
    }

    public class StockCountDto : StockCountListItemDto
    {
        public int? LocationId { get; set; }
        public string Description { get; set; }
        public DateTimeOffset SnapshotAt { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset? WarehouseApprovedAt { get; set; }
        public string WarehouseApprovedByName { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public string PlanningApprovedByName { get; set; }
        public DateTimeOffset? PostedAt { get; set; }
        public int? IncreaseDocumentId { get; set; }
        public string IncreaseDocumentNumber { get; set; }
        public int? DecreaseDocumentId { get; set; }
        public string DecreaseDocumentNumber { get; set; }
        public List<StockCountLineDto> Lines { get; set; } = new List<StockCountLineDto>();
    }

    public class StockCountLineDto
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public int? LocationId { get; set; }
        public string LocationCode { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
        public decimal SystemQuantity { get; set; }
        public decimal? CountedQuantity { get; set; }
        public decimal? RecountedQuantity { get; set; }

        /// <summary>Recount if given, else the first count, minus the system quantity.</summary>
        public decimal? Difference { get; set; }

        public string Note { get; set; }
    }

    // ------------------------------------------------------------------------------------------
    // Opening stock from the old warehouse data
    // ------------------------------------------------------------------------------------------

    public class OpeningImportDto
    {
        public DateTime DocumentDate { get; set; }
        public string Reference { get; set; }

        /// <summary>Locations that do not exist yet are created.</summary>
        public bool CreateLocations { get; set; } = true;

        /// <summary>When some rows have errors: still bring in the rows without errors.</summary>
        public bool ImportValidRows { get; set; }

        /// <summary>
        /// R1q D35: rows without a unit price are refused (stock without a price is valued at zero). The finance manager can allow them knowingly; the import is then audited
        /// and the result lists the zero-priced lines.
        /// </summary>
        public bool AllowZeroPrice { get; set; }

        public List<OpeningImportRowDto> Rows { get; set; } = new List<OpeningImportRowDto>();
    }

    public class OpeningImportRowDto
    {
        /// <summary>New code, or the old (legacy) code of the item.</summary>
        public string ItemCode { get; set; }
        public string WarehouseCode { get; set; }
        public string LocationCode { get; set; }
        public decimal Quantity { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public string CertificateNumber { get; set; }

        /// <summary>Company label number (YYDDDNNNN) of the piece or package.</summary>
        public string LabelNumber { get; set; }
        public List<string> SerialNumbers { get; set; } = new List<string>();
        public decimal? UnitPrice { get; set; }
        public string Description { get; set; }
    }

    public class OpeningImportResultDto
    {
        public List<int> DocumentIds { get; set; } = new List<int>();
        public int Lines { get; set; }
        public int LocationsCreated { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>Lines that came in without a price (value zero); only filled when the finance manager allowed it.</summary>
        public List<string> ZeroPricedLines { get; set; } = new List<string>();
    }
}
