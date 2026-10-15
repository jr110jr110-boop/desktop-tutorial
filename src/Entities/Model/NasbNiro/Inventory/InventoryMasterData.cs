using System.Collections.Generic;

namespace Entities.Model.NasbNiro.Inventory
{
    /// <summary>A node of the part coding tree (گروه کالا). Codes come from the company's existing coding.</summary>
    public class ItemGroup : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }

        public int? ParentId { get; set; }
        public ItemGroup Parent { get; set; }
        public ICollection<ItemGroup> Children { get; set; }

        public ICollection<InventoryItem> Items { get; set; }
    }

    /// <summary>A coded part, material or product (کالا / قطعه).</summary>
    public class InventoryItem : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }

        public int? ItemGroupId { get; set; }
        public ItemGroup ItemGroup { get; set; }

        /// <summary>Unit of measure, for example "عدد" or "کیلوگرم".</summary>
        public string Unit { get; set; }

        public ItemTrackingMode TrackingMode { get; set; }

        /// <summary>Drawing number or manufacturer part number.</summary>
        public string TechnicalCode { get; set; }

        public string Material { get; set; }
        public string Standard { get; set; }
        public string Specification { get; set; }
        public decimal? Weight { get; set; }

        /// <summary>Reorder point: below this quantity the item is reported as low stock.</summary>
        public decimal MinStock { get; set; }

        public decimal? MaxStock { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>Item code in Sepidar, when it differs from <see cref="Code"/>.</summary>
        public string SepidarCode { get; set; }
    }

    /// <summary>A warehouse (انبار).</summary>
    public class Warehouse : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public WarehouseKind Kind { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>Stock (انبار) code in Sepidar.</summary>
        public string SepidarCode { get; set; }

        public ICollection<WarehouseLocation> Locations { get; set; }
    }

    /// <summary>A place inside a warehouse: shelf, row, bin (محل).</summary>
    public class WarehouseLocation : BaseEntity
    {
        public int WarehouseId { get; set; }
        public Warehouse Warehouse { get; set; }

        public string Code { get; set; }
        public string Title { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Code of a PMIS record in Sepidar (supplier = تفصیلی, work order or contract = project / cost centre).
    /// Items and warehouses keep their Sepidar code on their own row.
    /// </summary>
    public class SepidarMapping
    {
        public int Id { get; set; }

        /// <summary>"Provider", "WorkOrder" or "Contract".</summary>
        public string EntityType { get; set; }

        public int EntityId { get; set; }
        public string SepidarCode { get; set; }
    }
}
