using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Providers;

namespace Entities.Model.NasbNiro.Inventory
{
    /// <summary>
    /// PB5 (F1, IAS 2.11): freight / landed cost of one receipt. The amount is spread over the receipt lines (by weight for metal groups, by value for the others, decision D10) when the
    /// accountant approves it; approval adds the amounts to the weighted-average value of the items. A document is never approved in a closed period; it is voided (reversed) when the receipt is cancelled.
    /// </summary>
    public class InventoryFreight : BaseEntity
    {
        public string Number { get; set; }

        public int ReceiptDocumentId { get; set; }
        public InventoryDocument ReceiptDocument { get; set; }

        public DateTime FreightDate { get; set; }
        public string VehiclePlate { get; set; }
        public string GoodsType { get; set; }
        public string BillOfLadingNumber { get; set; }

        /// <summary>The carrier (a provider) the amount is payable to.</summary>
        public int? CarrierProviderId { get; set; }
        public Provider CarrierProvider { get; set; }

        /// <summary>Rial.</summary>
        public decimal Amount { get; set; }

        public string Description { get; set; }
        public FreightStatus Status { get; set; }

        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public int? VoidedByUserId { get; set; }
        public string VoidReason { get; set; }

        public ICollection<InventoryFreightAllocation> Allocations { get; set; }
    }

    /// <summary>PB5: the share of a freight document that one receipt line carries (kept as history, rows are never edited after approval).</summary>
    public class InventoryFreightAllocation
    {
        public int Id { get; set; }

        public int FreightId { get; set; }
        public InventoryFreight Freight { get; set; }

        public int DocumentLineId { get; set; }
        public InventoryDocumentLine DocumentLine { get; set; }

        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public FreightBasis Basis { get; set; }

        /// <summary>Weight in kg (Basis = Weight) or the value of the line in rial (Basis = Value) the share was computed from.</summary>
        public decimal BasisAmount { get; set; }

        /// <summary>Rial, whole numbers; the shares of one document add up to its amount exactly.</summary>
        public decimal Amount { get; set; }
    }

    /// <summary>PB5 (F16): a row of the packing list of an exit slip, per bill of lading.</summary>
    public class InventoryPackingLine
    {
        public int Id { get; set; }

        public int DocumentId { get; set; }
        public InventoryDocument Document { get; set; }

        public int RowNumber { get; set; }
        public string BillOfLadingNumber { get; set; }
        public string PackageNumber { get; set; }
        public string Description { get; set; }

        public int? ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public decimal? Quantity { get; set; }
        public int? PackageCount { get; set; }
        public decimal? GrossWeight { get; set; }
    }

    /// <summary>PB5 (item 13): conversion between two units of measure. ItemId empty = valid for every item. 1 FromUnit = Factor ToUnit.</summary>
    public class ItemUnitConversion : BaseEntity
    {
        public int? ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public string FromUnit { get; set; }
        public string ToUnit { get; set; }
        public decimal Factor { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>PB5 (item 13): fixed-asset / tool register (light): the tag is the barcode / QR text of the asset label.</summary>
    public class InventoryAsset : BaseEntity
    {
        public string Tag { get; set; }
        public string Title { get; set; }

        public int? ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int? SerialUnitId { get; set; }
        public SerialUnit SerialUnit { get; set; }

        public int? WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public int? LocationId { get; set; }
        public WarehouseLocation Location { get; set; }

        public AssetStatus Status { get; set; } = AssetStatus.InService;
        public DateTime? PurchaseDate { get; set; }
        public decimal? Cost { get; set; }

        /// <summary>The unit or role that holds the asset (not a person's name).</summary>
        public string CustodianUnit { get; set; }

        public string Description { get; set; }
    }

    /// <summary>PB5 (F8): the most an item may consume, for one work order (project) or in total (WorkOrderId empty).</summary>
    public class InventoryConsumptionCeiling : BaseEntity
    {
        public int ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public decimal MaxQuantity { get; set; }
        public string Note { get; set; }
    }
}
