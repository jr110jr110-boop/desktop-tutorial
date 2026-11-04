using System;
using Entities.Model.NasbNiro.Production;

namespace Entities.Model.NasbNiro.Inventory
{
    public enum GenealogySource
    {
        /// <summary>Written when the production receipt (or a late issue) of the order was confirmed.</summary>
        Live = 1,

        /// <summary>Rebuilt from the confirmed issues and the receipt of an order that was finished before the genealogy existed.</summary>
        Backfill = 2,
    }

    /// <summary>
    /// PB4 two-way genealogy: one input (a lot or a serial piece that a production order consumed through its kit issues) tied to one output of the same order (the lot or serial pieces of its production receipt).
    /// "Parent" is the output, "child" the input, like <see cref="AssemblyLink"/>. The row is derived from confirmed documents and can be rebuilt; it is never edited by hand. Rows of a receipt that was
    /// cancelled are marked deleted (the history stays in the audit trail).
    /// </summary>
    public class GenealogyLink : BaseEntity
    {
        public int ProductionOrderId { get; set; }
        public MfProductionOrder ProductionOrder { get; set; }

        /// <summary>The operation the input feeds. The kit is issued for the first operation of the order, so that is the one recorded; empty when the order has no operation.</summary>
        public int? OperationId { get; set; }
        public MfOperation Operation { get; set; }

        // ----- output (what the order made): the item is the part or the stage item (part number + stage suffix)
        public int OutputItemId { get; set; }
        public InventoryItem OutputItem { get; set; }
        public int? OutputLotId { get; set; }
        public InventoryLot OutputLot { get; set; }
        public int? OutputSerialUnitId { get; set; }
        public SerialUnit OutputSerialUnit { get; set; }

        // ----- input (what the order consumed)
        public int InputItemId { get; set; }
        public InventoryItem InputItem { get; set; }
        public int? InputLotId { get; set; }
        public InventoryLot InputLot { get; set; }
        public int? InputSerialUnitId { get; set; }
        public SerialUnit InputSerialUnit { get; set; }

        /// <summary>The quantity of the input that went into this output. When one input fed several serial pieces of the order, it is the equal share and <see cref="IsShared"/> is true.</summary>
        public decimal Quantity { get; set; }

        public bool IsShared { get; set; }

        /// <summary>The issue that took the input out (the first one when several issued the same lot).</summary>
        public int? IssueDocumentId { get; set; }
        public InventoryDocument IssueDocument { get; set; }

        public int ReceiptDocumentId { get; set; }
        public InventoryDocument ReceiptDocument { get; set; }

        /// <summary>The date of the receipt (the moment the output exists).</summary>
        public DateTime LinkDate { get; set; }

        public GenealogySource Source { get; set; }

        public string Note { get; set; }
    }
}
