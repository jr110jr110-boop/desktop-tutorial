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
        /// <summary>قرنطینه: received, waiting for incoming inspection; nothing is issued from it.</summary>
        Quarantine = 5,

        /// <summary>ضایعات: only a scrap sale takes stock out of it (WI-WH-004).</summary>
        Scrap = 6,

        /// <summary>
        /// Virtual warehouse of one subcontractor (پیمانکار): company property sent out for work.
        /// The stock still belongs to the company and is shown as "at the contractor".
        /// </summary>
        Subcontractor = 7,

        /// <summary>اموال مشتری (ISO 9001 8.5.3): customer property and returned goods, kept apart from company stock.</summary>
        CustomerProperty = 8,

        /// <summary>انبار مدل‌های ریخته‌گری</summary>
        Patterns = 9,
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

        /// <summary>تعدیل کاهشی</summary>
        AdjustmentDecrease = 8,

        /// <summary>ارسال به پیمانکار: transfer to the contractor's virtual warehouse; leaves the premises.</summary>
        SubcontractIssue = 9,

        /// <summary>برگشت از پیمانکار: transfer back from the contractor's warehouse.</summary>
        SubcontractReturn = 10,

        /// <summary>تحویل به مشتری: leaves the premises after the QC loading approval.</summary>
        CustomerDelivery = 11,

        /// <summary>
        /// تبدیل مرحله: the part leaves one stockable stage (for example raw casting) and comes back as the next one
        /// (for example rough machined) with a new label linked to the old one.
        /// </summary>
        StageConversion = 12,

        /// <summary>رسید ضایعات (FO-WH-001): scrap and chips collected from the workshops.</summary>
        ScrapReceipt = 13,

        /// <summary>فروش ضایعات (FO-WH-002): scrap sold; the money goes to the company account.</summary>
        ScrapSale = 14,

        /// <summary>موجودی اول دوره</summary>
        OpeningBalance = 15,

        /// <summary>ترخیص از قرنطینه: made by the system from an accepted inspection or an approved NCR disposition.</summary>
        QcRelease = 16,

        /// <summary>رسید اموال مشتری</summary>
        CustomerPropertyReceipt = 17,
    }

    /// <summary>Direction of a line on a stage conversion; other documents take it from their type.</summary>
    public enum DocumentLineDirection
    {
        ByType = 0,
        Out = 1,
        In = 2,
    }

    /// <summary>Approval state of a document that needs approvals before it is confirmed (adjustments).</summary>
    public enum ApprovalKind
    {
        /// <summary>سرپرست انبار</summary>
        Warehouse = 1,

        /// <summary>مدیر برنامه‌ریزی و کنترل پروژه</summary>
        Planning = 2,

        /// <summary>مدیر تولید</summary>
        Manufacturing = 3,

        /// <summary>مدیر کنترل کیفیت</summary>
        Quality = 4,

        /// <summary>مدیر مهندسی / معاونت طراحی و ساخت</summary>
        Engineering = 5,
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

        /// <summary>Became the next stage of the part under a new label (see SerialUnit.SourceSerialUnitId).</summary>
        Converted = 6,

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

    public enum InspectionStatus
    {
        Pending = 0,
        Done = 1,
    }

    public enum NcrSource
    {
        /// <summary>بازرسی ورودی</summary>
        Receiving = 1,

        /// <summary>حین ساخت</summary>
        InProcess = 2,

        /// <summary>بازرسی نهایی</summary>
        Final = 3,

        /// <summary>مشتری</summary>
        Customer = 4,

        /// <summary>پیمانکار</summary>
        Subcontractor = 5,
    }

    /// <summary>PR-QC-002: minor is closed by the QC, engineering and manufacturing managers; major goes to the technical committee.</summary>
    public enum NcrSeverity
    {
        NotClassified = 0,
        Minor = 1,
        Major = 2,
    }

    /// <summary>Root cause categories of FO-QC-013.</summary>
    public enum NcrCauseCategory
    {
        None = 0,
        Manpower = 1,
        Material = 2,
        Method = 3,
        Equipment = 4,
        Drawing = 5,
        Environment = 6,
    }

    /// <summary>Disposition of non-conforming product (ISO 9001 8.7).</summary>
    public enum NcrDisposition
    {
        None = 0,

        /// <summary>استفاده با مجوز ارفاقی (concession / use as is)</summary>
        Concession = 1,

        /// <summary>دوباره‌کاری: brought back to the drawing</summary>
        Rework = 2,

        /// <summary>تعمیر: usable but not to the drawing; needs a concession too</summary>
        Repair = 3,

        /// <summary>ضایعات (company-owned)</summary>
        Scrap = 4,

        /// <summary>مرجوعی به تامین‌کننده یا پیمانکار</summary>
        ReturnToSupplier = 5,
    }

    public enum NcrStatus
    {
        Open = 0,

        /// <summary>Disposition approved; the stock document is not made yet.</summary>
        Approved = 1,

        /// <summary>Stock document made; corrective action is followed up.</summary>
        Executed = 2,

        Closed = 3,
        Cancelled = 4,
    }

    public enum MaterialRequestStatus
    {
        Draft = 0,
        Submitted = 1,
        Approved = 2,
        Rejected = 3,
        Closed = 4,
    }

    public enum StockCountKind
    {
        /// <summary>شمارش ادواری</summary>
        Cycle = 1,

        /// <summary>انبارگردانی سالانه</summary>
        Annual = 2,
    }

    public enum StockCountStatus
    {
        Counting = 0,
        Submitted = 1,
        Approved = 2,
        Posted = 3,
        Cancelled = 4,
    }

    public enum ItemRequestStatus
    {
        Submitted = 0,
        Approved = 1,
        Rejected = 2,
    }
}
