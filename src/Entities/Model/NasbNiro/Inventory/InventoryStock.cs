using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Providers;

namespace Entities.Model.NasbNiro.Inventory
{
    /// <summary>A lot / batch of an item: heat number and material certificate (بچ و شماره ذوب).</summary>
    public class InventoryLot : BaseEntity
    {
        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public string CertificateNumber { get; set; }

        /// <summary>For example "EN 10204 3.1".</summary>
        public string CertificateType { get; set; }

        /// <summary>Scanned certificate, uploaded through api/v1/Documents.</summary>
        public Guid? CertificateDocumentId { get; set; }

        public int? ProviderId { get; set; }
        public Provider Provider { get; set; }

        public DateTime? ProductionDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Description { get; set; }

        /// <summary>The lot this one was made from (previous stage of the part, see StageConversion).</summary>
        public int? ParentLotId { get; set; }
        public InventoryLot ParentLot { get; set; }

        /// <summary>Customer or subcontractor property: not company stock (ISO 9001 8.5.3).</summary>
        public bool IsCustomerProperty { get; set; }
    }

    /// <summary>One physical piece with its own serial number.</summary>
    public class SerialUnit : BaseEntity
    {
        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public string SerialNumber { get; set; }

        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        public SerialUnitStatus Status { get; set; }

        /// <summary>Where the piece is while <see cref="Status"/> is InStock.</summary>
        public int? WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public int? LocationId { get; set; }
        public WarehouseLocation Location { get; set; }

        /// <summary>The assembly the piece is installed in, while <see cref="Status"/> is Installed.</summary>
        public int? ParentSerialUnitId { get; set; }
        public SerialUnit ParentSerialUnit { get; set; }
        public ICollection<SerialUnit> Children { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public string Description { get; set; }

        /// <summary>The piece this one was made from (previous stage, old label), see StageConversion.</summary>
        public int? SourceSerialUnitId { get; set; }
        public SerialUnit SourceSerialUnit { get; set; }

        /// <summary>The lot of the previous stage, when the piece was made from a lot (for example a casting batch).</summary>
        public int? SourceLotId { get; set; }
        public InventoryLot SourceLot { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A warehouse document: receipt, issue, transfer, return or adjustment.</summary>
    public class InventoryDocument : BaseEntity
    {
        public InventoryDocumentType Type { get; set; }

        /// <summary>Given when the document is confirmed, for example "RC-1405-00012".</summary>
        public string Number { get; set; }

        public DateTime DocumentDate { get; set; }
        public InventoryDocumentStatus Status { get; set; }

        /// <summary>The warehouse the document is for (the source warehouse of a transfer).</summary>
        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public int? DestinationWarehouseId { get; set; }
        public Warehouse DestinationWarehouse { get; set; }

        public int? ProviderId { get; set; }
        public Provider Provider { get; set; }

        public int? BuyOrderId { get; set; }
        public BuyOrder BuyOrder { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public int? ContractId { get; set; }
        public ContractEntity Contract { get; set; }

        /// <summary>Supplier invoice, delivery note or request number.</summary>
        public string ReferenceNumber { get; set; }

        public string Description { get; set; }

        public DateTimeOffset? ConfirmedAt { get; set; }
        public int? ConfirmedByUserId { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }
        public int? CancelledByUserId { get; set; }
        public string CancelReason { get; set; }

        public SepidarSyncStatus SepidarStatus { get; set; }
        public string SepidarReference { get; set; }
        public DateTimeOffset? SepidarExportedAt { get; set; }

        // ----- links
        /// <summary>Approved material request an issue is made against (no issue without one).</summary>
        public int? MaterialRequestId { get; set; }
        public MaterialRequest MaterialRequest { get; set; }

        public int? NcrId { get; set; }
        public NonConformance Ncr { get; set; }

        public int? InspectionId { get; set; }
        public int? StockCountId { get; set; }

        // ----- exit from the premises (برگه خروج): returns, subcontract, delivery, scrap sale
        public string DriverName { get; set; }
        public string VehiclePlate { get; set; }
        public string ReceiverName { get; set; }
        public string Destination { get; set; }
        public DateTimeOffset? ExitCheckedAt { get; set; }
        public int? ExitCheckedByUserId { get; set; }
        public string ExitCheckNote { get; set; }

        /// <summary>QC approval of the loading before a customer delivery.</summary>
        public DateTimeOffset? QcLoadingApprovedAt { get; set; }
        public int? QcLoadingApprovedByUserId { get; set; }

        // ----- scrap sale (WI-WH-004, FO-WH-020)
        public string BuyerName { get; set; }
        public string MinutesNumber { get; set; }

        /// <summary>Weighbridge ticket (photo), uploaded through api/v1/Documents.</summary>
        public Guid? WeighbridgeDocumentId { get; set; }

        /// <summary>Reference of the deposit to the company bank account (no personal accounts).</summary>
        public string DepositReference { get; set; }

        // ----- approvals of adjustments: the warehouse supervisor (not the creator) and the planning manager
        public DateTimeOffset? WarehouseApprovedAt { get; set; }
        public int? WarehouseApprovedByUserId { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public int? PlanningApprovedByUserId { get; set; }

        public ICollection<InventoryDocumentLine> Lines { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class InventoryDocumentLine
    {
        public int Id { get; set; }

        public int DocumentId { get; set; }
        public InventoryDocument Document { get; set; }

        public int RowNumber { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public decimal Quantity { get; set; }

        /// <summary>Existing lot (issues, transfers) or the lot created by a receipt.</summary>
        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        // Lot data typed on a receipt; the lot is created or found when the document is confirmed.
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public string CertificateNumber { get; set; }

        public int? LocationId { get; set; }
        public WarehouseLocation Location { get; set; }

        public int? DestinationLocationId { get; set; }
        public WarehouseLocation DestinationLocation { get; set; }

        public decimal? UnitPrice { get; set; }
        public string Description { get; set; }

        /// <summary>Only for a stage conversion: the line takes the part out (old stage) or brings it in (new stage).</summary>
        public DocumentLineDirection Direction { get; set; }

        /// <summary>Stage conversion "in" line: row number of the "out" line it was made from (label genealogy).</summary>
        public int? SourceRowNumber { get; set; }

        public ICollection<InventoryDocumentLineSerial> Serials { get; set; }
    }

    public class InventoryDocumentLineSerial
    {
        public int Id { get; set; }

        public int LineId { get; set; }
        public InventoryDocumentLine Line { get; set; }

        public string SerialNumber { get; set; }

        public int? SerialUnitId { get; set; }
        public SerialUnit SerialUnit { get; set; }
    }

    /// <summary>One stock movement (کاردکس). Rows are never changed: a cancelled document adds reversing rows.</summary>
    public class StockTransaction
    {
        public int Id { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public int? LocationId { get; set; }
        public WarehouseLocation Location { get; set; }

        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        public int? SerialUnitId { get; set; }
        public SerialUnit SerialUnit { get; set; }

        /// <summary>Positive = into the warehouse, negative = out.</summary>
        public decimal Quantity { get; set; }

        public decimal? UnitPrice { get; set; }
        public DateTime TransactionDate { get; set; }

        public int DocumentId { get; set; }
        public InventoryDocument Document { get; set; }

        public int? DocumentLineId { get; set; }

        /// <summary>True for the rows written when a confirmed document is cancelled.</summary>
        public bool IsReversal { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
    }

    /// <summary>Current quantity per item, warehouse, location and lot. Kept in step with <see cref="StockTransaction"/>.</summary>
    public class StockBalance
    {
        public int Id { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public int? LocationId { get; set; }
        public WarehouseLocation Location { get; set; }

        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        public decimal Quantity { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Last number used per document type and Persian year.</summary>
    public class DocumentNumberSequence
    {
        public int Id { get; set; }
        public int Type { get; set; }
        public int Year { get; set; }
        public int LastNumber { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
