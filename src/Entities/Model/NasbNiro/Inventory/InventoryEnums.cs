namespace Entities.Model.NasbNiro.Inventory
{
    /// <summary>How the stock of an item is tracked.</summary>
    public enum ItemTrackingMode
    {
        /// <summary>Quantity only.</summary>
        None = 0,

        /// <summary>By lot / batch (for example a heat number of steel or a supplier batch).</summary>
        Lot = 1,

        /// <summary>Every piece has its own serial number (critical turbine parts).</summary>
        Serial = 2,
    }

    public enum WarehouseKind
    {
        General = 0,
        RawMaterial = 1,
        Parts = 2,
        WorkInProgress = 3,
        FinishedGoods = 4,
        Quarantine = 5,
        Scrap = 6,
    }

    /// <summary>Standard warehouse document types.</summary>
    public enum InventoryDocumentType
    {
        /// <summary>رسید خرید</summary>
        PurchaseReceipt = 1,

        /// <summary>رسید ساخت: a part or product made in-house enters the warehouse.</summary>
        ProductionReceipt = 2,

        /// <summary>برگشت از تولید</summary>
        ProductionReturn = 3,

        /// <summary>حواله: issue to production / a work order.</summary>
        Issue = 4,

        /// <summary>برگشت به تامین‌کننده</summary>
        SupplierReturn = 5,

        /// <summary>انتقال بین انبارها یا محل‌ها</summary>
        Transfer = 6,

        /// <summary>تعدیل افزایشی (for example after a stock count)</summary>
        AdjustmentIncrease = 7,

        /// <summary>تعدیل کاهشی / ضایعات</summary>
        AdjustmentDecrease = 8,
    }

    public enum InventoryDocumentStatus
    {
        Draft = 0,
        Confirmed = 1,
        Cancelled = 2,
    }

    /// <summary>State of a document towards the Sepidar accounting system.</summary>
    public enum SepidarSyncStatus
    {
        NotRequired = 0,
        Pending = 1,
        Exported = 2,
        Sent = 3,
        Failed = 4,
    }

    public enum SerialUnitStatus
    {
        /// <summary>Being made, or issued to production.</summary>
        InProduction = 1,

        InStock = 2,

        /// <summary>Installed in a parent assembly (see AssemblyLink).</summary>
        Installed = 3,

        /// <summary>Delivered to the customer.</summary>
        Delivered = 4,

        ReturnedToSupplier = 5,

        Scrapped = 9,
    }

    public enum PartOperationStatus
    {
        Pending = 0,
        InProgress = 1,
        Done = 2,
        Rejected = 3,
    }

    public enum QcStatus
    {
        NotRequired = 0,
        Pending = 1,
        Accepted = 2,
        Rejected = 3,
        AcceptedWithConditions = 4,
    }
}
