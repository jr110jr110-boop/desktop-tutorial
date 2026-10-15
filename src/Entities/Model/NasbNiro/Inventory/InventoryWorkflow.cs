using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Providers;

namespace Entities.Model.NasbNiro.Inventory
{
    /// <summary>
    /// Incoming inspection of one receipt line held in quarantine (WI-WH-001, ISO 9001 8.6).
    /// Accepted quantity is released to its warehouse; rejected quantity opens an NCR.
    /// </summary>
    public class QcInspection : BaseEntity
    {
        public string Number { get; set; }

        public int ReceiptDocumentId { get; set; }
        public InventoryDocument ReceiptDocument { get; set; }

        public int ReceiptLineId { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        /// <summary>The quarantine warehouse.</summary>
        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public int? LocationId { get; set; }

        public decimal Quantity { get; set; }

        /// <summary>Serial numbers of the line, one per line of text.</summary>
        public string SerialNumbers { get; set; }

        /// <summary>Warehouse the accepted quantity goes to (chosen at receipt, may be changed by QC).</summary>
        public int? TargetWarehouseId { get; set; }
        public Warehouse TargetWarehouse { get; set; }

        public int? TargetLocationId { get; set; }

        public InspectionStatus Status { get; set; }
        public decimal AcceptedQuantity { get; set; }
        public decimal RejectedQuantity { get; set; }
        public string RejectedSerialNumbers { get; set; }

        /// <summary>Inspection report number (QC form).</summary>
        public string ReportNumber { get; set; }

        public Guid? ReportDocumentId { get; set; }
        public string Note { get; set; }

        public DateTimeOffset? InspectedAt { get; set; }
        public int? InspectedByUserId { get; set; }

        public int? ReleaseDocumentId { get; set; }
        public int? NcrId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Non-conformance report (گزارش عدم انطباق) per PR-QC-002 and form FO-QC-013.</summary>
    public class NonConformance : BaseEntity
    {
        public string Number { get; set; }
        public DateTime ReportDate { get; set; }
        public NcrSource Source { get; set; }
        public NcrStatus Status { get; set; }
        public NcrSeverity Severity { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        public decimal Quantity { get; set; }

        /// <summary>Serial numbers, one per line of text.</summary>
        public string SerialNumbers { get; set; }

        /// <summary>Where the non-conforming product is held (normally the quarantine warehouse).</summary>
        public int? WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public int? LocationId { get; set; }

        public int? ProviderId { get; set; }
        public Provider Provider { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public int? ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        public int? InspectionId { get; set; }
        public int? ReceiptDocumentId { get; set; }

        /// <summary>The product belongs to a subcontractor or supplier: a rejected piece goes back to them, not to scrap.</summary>
        public bool OwnedByContractor { get; set; }

        public string Requirement { get; set; }
        public string Description { get; set; }

        public NcrCauseCategory CauseCategory { get; set; }
        public string RootCause { get; set; }

        public NcrDisposition Disposition { get; set; }
        public string DispositionNote { get; set; }

        /// <summary>Major NCR: minutes of the technical committee and its members.</summary>
        public string CommitteeMinutesNumber { get; set; }
        public string CommitteeMembers { get; set; }

        // approvals of the disposition
        public DateTimeOffset? QualityApprovedAt { get; set; }
        public int? QualityApprovedByUserId { get; set; }
        public DateTimeOffset? EngineeringApprovedAt { get; set; }
        public int? EngineeringApprovedByUserId { get; set; }
        public DateTimeOffset? ManufacturingApprovedAt { get; set; }
        public int? ManufacturingApprovedByUserId { get; set; }

        /// <summary>Concession (مجوز ارفاقی), issued by the deputy for design and manufacturing.</summary>
        public string ConcessionNumber { get; set; }
        public DateTimeOffset? ConcessionApprovedAt { get; set; }
        public int? ConcessionApprovedByUserId { get; set; }

        /// <summary>The contract needs the customer to accept deviations (for example API 611/612): the concession needs the customer's approval.</summary>
        public bool CustomerApprovalRequired { get; set; }
        public string CustomerApprovalReference { get; set; }

        public int? TargetWarehouseId { get; set; }
        public int? ResultDocumentId { get; set; }
        public DateTimeOffset? ExecutedAt { get; set; }
        public int? ExecutedByUserId { get; set; }

        public string CorrectiveAction { get; set; }
        public string CorrectiveActionOwner { get; set; }
        public DateTime? CorrectiveActionDueDate { get; set; }
        public string EffectivenessCheck { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>
    /// Material request (درخواست کالا): production asks, planning (PMO) and the manufacturing manager approve.
    /// The warehouse issues only against an approved request and never more than what is left of it.
    /// </summary>
    public class MaterialRequest : BaseEntity
    {
        public string Number { get; set; }
        public DateTime RequestDate { get; set; }
        public MaterialRequestStatus Status { get; set; }

        /// <summary>Project / work order (cost centre). Empty only for general use (maintenance, overhead) with a written purpose.</summary>
        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        /// <summary>Warehouse the material is wanted from (optional).</summary>
        public int? WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public string Purpose { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public int? PlanningApprovedByUserId { get; set; }
        public DateTimeOffset? ManufacturingApprovedAt { get; set; }
        public int? ManufacturingApprovedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public string RejectReason { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }

        public ICollection<MaterialRequestLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class MaterialRequestLine
    {
        public int Id { get; set; }

        public int MaterialRequestId { get; set; }
        public MaterialRequest MaterialRequest { get; set; }

        public int RowNumber { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public decimal Quantity { get; set; }

        /// <summary>Kept in step with the confirmed issues made against the request.</summary>
        public decimal IssuedQuantity { get; set; }

        public string Description { get; set; }
    }

    /// <summary>Stock count (شمارش موجودی): a snapshot of the balances, the counted quantities and the approved differences.</summary>
    public class StockCount : BaseEntity
    {
        public string Number { get; set; }
        public StockCountKind Kind { get; set; }
        public StockCountStatus Status { get; set; }
        public DateTime CountDate { get; set; }

        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        /// <summary>Only this item group (code prefix), for a cycle count.</summary>
        public string GroupCode { get; set; }

        public int? LocationId { get; set; }

        public string Description { get; set; }

        public DateTimeOffset SnapshotAt { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? WarehouseApprovedAt { get; set; }
        public int? WarehouseApprovedByUserId { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public int? PlanningApprovedByUserId { get; set; }
        public DateTimeOffset? PostedAt { get; set; }
        public int? PostedByUserId { get; set; }

        public int? IncreaseDocumentId { get; set; }
        public int? DecreaseDocumentId { get; set; }

        public ICollection<StockCountLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class StockCountLine
    {
        public int Id { get; set; }

        public int StockCountId { get; set; }
        public StockCount StockCount { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int? LocationId { get; set; }
        public WarehouseLocation Location { get; set; }

        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        public decimal SystemQuantity { get; set; }
        public decimal? CountedQuantity { get; set; }

        /// <summary>Second count of a line with a difference (recount before approval).</summary>
        public decimal? RecountedQuantity { get; set; }

        public string Note { get; set; }
        public int? CountedByUserId { get; set; }
    }
}
