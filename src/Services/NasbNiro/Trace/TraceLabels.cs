using System.Collections.Generic;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;

namespace Services.NasbNiro.Trace
{
    /// <summary>Persian words for the statuses shown on the trace page, and the colour tone of each.</summary>
    internal static class TraceLabels
    {
        public static string Order(MfOrderStatus s) => s switch
        {
            MfOrderStatus.Draft => "پیش‌نویس", MfOrderStatus.Released => "آزادشده", MfOrderStatus.InProgress => "در حال ساخت", MfOrderStatus.Completed => "تکمیل‌شده",
            MfOrderStatus.Closed => "بسته", MfOrderStatus.Cancelled => "باطل", _ => s.ToString(),
        };

        public static string OrderTone(MfOrderStatus s) => s == MfOrderStatus.Completed || s == MfOrderStatus.Closed ? "ok" : s == MfOrderStatus.Cancelled ? "bad" : "info";

        public static string Operation(MfOperationStatus s) => s switch
        {
            MfOperationStatus.Waiting => "در انتظار", MfOperationStatus.Ready => "آماده", MfOperationStatus.InProgress => "در حال انجام", MfOperationStatus.Paused => "متوقف",
            MfOperationStatus.Done => "انجام‌شده", MfOperationStatus.QcHold => "در انتظار QC", MfOperationStatus.Rejected => "ردشده", MfOperationStatus.Skipped => "حذف‌شده از مسیر", _ => s.ToString(),
        };

        public static string OperationTone(MfOperationStatus s) => s == MfOperationStatus.Done ? "ok" : s == MfOperationStatus.Rejected ? "bad" : s == MfOperationStatus.QcHold ? "warn" : "info";

        public static string PartOperation(PartOperationStatus s) => s switch
        {
            PartOperationStatus.Pending => "در انتظار", PartOperationStatus.InProgress => "در حال انجام", PartOperationStatus.Done => "انجام‌شده", PartOperationStatus.Rejected => "ردشده", _ => s.ToString(),
        };

        public static string Qc(QcStatus s) => s switch
        {
            QcStatus.NotRequired => "بدون QC", QcStatus.Pending => "در انتظار QC", QcStatus.Accepted => "تایید QC", QcStatus.Rejected => "رد QC", QcStatus.AcceptedWithConditions => "تایید مشروط QC", _ => s.ToString(),
        };

        public static string QcTone(QcStatus s) => s == QcStatus.Accepted ? "ok" : s == QcStatus.Rejected ? "bad" : s == QcStatus.Pending || s == QcStatus.AcceptedWithConditions ? "warn" : "";

        public static string QcRecord(QcResult s) => s switch { QcResult.Accept => "تایید", QcResult.Reject => "رد", QcResult.Rework => "اصلاح", QcResult.Hold => "توقف", _ => s.ToString() };

        public static string Inspection(InspectionStatus s) => s == InspectionStatus.Done ? "بازرسی‌شده" : "در انتظار بازرسی";

        public static string Ncr(NcrStatus s) => s switch
        {
            NcrStatus.Open => "باز", NcrStatus.Approved => "تصمیم تاییدشده", NcrStatus.Executed => "اجراشده", NcrStatus.Closed => "بسته", NcrStatus.Cancelled => "باطل", _ => s.ToString(),
        };

        public static string Severity(NcrSeverity s) => s switch { NcrSeverity.Minor => "جزئی", NcrSeverity.Major => "عمده", _ => "طبقه‌بندی‌نشده" };

        public static string PurchaseRequest(PurchaseRequestStatus s) => s switch
        {
            PurchaseRequestStatus.Draft => "پیش‌نویس", PurchaseRequestStatus.Submitted => "ارسال‌شده", PurchaseRequestStatus.Approved => "تاییدشده", PurchaseRequestStatus.Ordered => "سفارش‌شده",
            PurchaseRequestStatus.Closed => "بسته", PurchaseRequestStatus.Rejected => "ردشده", _ => s.ToString(),
        };

        public static string PurchaseOrder(PurchaseOrderStatus s) => s switch
        {
            PurchaseOrderStatus.Draft => "پیش‌نویس", PurchaseOrderStatus.Approved => "تاییدشده", PurchaseOrderStatus.Released => "ارسال‌شده به تامین‌کننده", PurchaseOrderStatus.PartlyReceived => "بخشی دریافت‌شده",
            PurchaseOrderStatus.Received => "دریافت‌شده", PurchaseOrderStatus.Closed => "بسته", PurchaseOrderStatus.Cancelled => "باطل", _ => s.ToString(),
        };

        public static string Inquiry(InquiryStatus s) => s switch { InquiryStatus.Open => "باز", InquiryStatus.Selected => "انتخاب‌شده", InquiryStatus.Approved => "تاییدشده", InquiryStatus.Cancelled => "باطل", _ => s.ToString() };

        public static string Serial(SerialUnitStatus s) => s switch
        {
            SerialUnitStatus.InProduction => "در ساخت", SerialUnitStatus.InStock => "در انبار", SerialUnitStatus.Installed => "نصب‌شده", SerialUnitStatus.Delivered => "تحویل‌شده",
            SerialUnitStatus.ReturnedToSupplier => "برگشت به تامین‌کننده", SerialUnitStatus.Converted => "تبدیل به مرحله بعد", SerialUnitStatus.Scrapped => "ضایعات", _ => s.ToString(),
        };

        public static string MakeOrBuy(PbMakeOrBuy m) => m switch
        {
            PbMakeOrBuy.Make => "ساخت", PbMakeOrBuy.Purchase => "خرید", PbMakeOrBuy.CustomerSupplied => "تامین توسط کارفرما", PbMakeOrBuy.Subcontract => "پیمانکاری", _ => m.ToString(),
        };

        public static readonly Dictionary<string, string> EvidenceFa = new Dictionary<string, string>
        {
            ["DIM"] = "ابعادی", ["VT"] = "چشمی", ["UT"] = "اولتراسونیک", ["MT"] = "ذرات مغناطیسی", ["PT"] = "مایع نافذ", ["NDT"] = "NDT", ["TENSILE"] = "کشش",
            ["HARDNESS"] = "سختی", ["IMPACT"] = "ضربه", ["CHEM"] = "آنالیز شیمیایی", ["HYDRO"] = "هیدرواستاتیک", ["BALANCE"] = "بالانس", ["RUNOUT"] = "ران‌اوت",
        };
    }
}
