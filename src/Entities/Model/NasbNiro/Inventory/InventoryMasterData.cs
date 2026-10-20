using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Quality;

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

        /// <summary>First two digits of the code (WI-WH-002): 01 material, 02 part, 03 product, 04 model, 05 consumable, 06 asset, 07 tool, 08 standard part, 12 instrument.</summary>
        public string GroupCode { get; set; }

        /// <summary>Part status (the last two digits of a part code), for example 10 raw casting, 01 rough machined.</summary>
        public string StageCode { get; set; }

        /// <summary>The code without the part status: all stages of one part share it.</summary>
        public string BaseCode { get; set; }

        /// <summary>MESC code of a standard part (group 08); for standard parts it is the item code itself.</summary>
        public string MescCode { get; set; }

        /// <summary>Old code (RayDana or the Excel database), kept for searching only.</summary>
        public string LegacyCode { get; set; }

        /// <summary>Receipts go to quarantine and wait for incoming inspection (true for every group except office consumables).</summary>
        public bool RequiresIncomingInspection { get; set; } = true;

        /// <summary>Lots expire (electrodes, chemicals, paint): issued first-expired-first-out.</summary>
        public int? ShelfLifeDays { get; set; }

        /// <summary>EN 10204 certificate type the incoming inspection needs before the lot is accepted (empty = none).</summary>
        public CertificateType? RequiredCertificateType { get; set; }
    }

    /// <summary>
    /// One value of a segment of the company coding (WI-WH-002): item groups, assembly ranges per turbine model,
    /// part statuses, material shapes and grades, product types, tool categories.
    /// </summary>
    public class CodingValue : BaseEntity
    {
        /// <summary>Which segment: see <c>CodingSchemes</c>.</summary>
        public string Scheme { get; set; }

        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>Turbine model for assembly ranges (FY, 2FY, SY ...); empty = all models.</summary>
        public string Model { get; set; }

        /// <summary>For assembly ranges: the first and last 3-digit assembly number of the range.</summary>
        public int? RangeFrom { get; set; }
        public int? RangeTo { get; set; }

        /// <summary>
        /// For part statuses: the stage gets its own item code and is held in stock.
        /// Other statuses are routing operations on the serial or lot. ★ provisional company decision.
        /// </summary>
        public bool IsStockable { get; set; }

        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public string Description { get; set; }
    }

    /// <summary>Request for a new item code: engineering asks, planning (PMO) checks for duplicates and approves.</summary>
    public class ItemRequest : BaseEntity
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public string GroupCode { get; set; }

        /// <summary>Code built by the requester with the coding builder; planning may change it.</summary>
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
        public int? ReviewedByUserId { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string ReviewNote { get; set; }

        public int? ItemId { get; set; }
        public InventoryItem Item { get; set; }
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
