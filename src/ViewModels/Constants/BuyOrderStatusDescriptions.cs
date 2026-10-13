using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.Constants
{
    public static class BuyOrderStatusDescriptions
    {
        public const string NewBuyOrderDescription = "دستور خرید اخیرا اضافه شده.";
        public const string NewBuyOrderForAssemblerDescription = "دستور خرید مونتاژ اخیرا اضافه شده.";
        public const string NewBuyOrderForConstractDescription = "دستور خرید ساخت اخیرا اضافه شده.";
        public const string InquiriesAddedDescription = "استعلام های دستور خرید ثبت شده اند.";
        public const string TechnicalScoreAddedDescription = "امتیاز فنی ثبت شده است.";
        public const string WinnerIsSelectedDescription = "برنده تعیین شده است.";
        public const string FinanceDocRegisteredDescription = "سند مالی اضافه شده است.";
        public const string CommercialBoughtDescription = "خریداری شده توسط واحد بازگانی";
        public const string TempReceiveGeneratedDescription = "رسید موقت صادر شده است.";
        public const string QcAcceptedDescription = "این دستور خرید توسط واحد کنترل کیفیت تایید شده است.";
        public const string QcRejectedDescription = "این دستور خرید توسط واحد کنترل کیفیت رد شده است.";
        public const string EngineeringAcceptedDescription = "این دستور خرید توسط واحد مهندسی تایید شده است.";
        public const string AssemblerAssembledDescription = "مونتاژ شده،در انتظار ثبت رسید موقت.";
        public const string ConstructorConstructedDescription = "ساخته شده،در انتظار ثبت رسید موقت.";
        public const string RejectedGivenToSalvageDescription = "این دستور خرید توسط کنترل کیفیت و مهندسی رد شده است.";
        public const string RejectedAcceptedByPmoDescription = "این دستور خرید توسط کنترل کیفیت و مهندسی رد شده و تایید شده است.";
        public const string RejectedAcceptedDescription = "این دستور خرید به علت عدم تطابق کنترل کیفی در انبار ضایعات است.";
        public const string FinanceAcceptedDescription = "حاصل فرآیندها به انبار تحویل شده است. در انتظار رسید قطعی انبار.";
        public const string WarehouseFinallyAcceptedDescription = "رسید قطعی توسط انبار صادر شد. ";
        public const string NeedsFixesDescription = "در انتظار اصلاح";

        public const string UnSupportedDescription = "وضعیت دستور خرید نامعتبر است.";

        internal static string GetStatusDescription(BuyOrderStatus value)
        {
            switch (value)
            {
                case BuyOrderStatus.NewBuyOrder: return NewBuyOrderDescription;
                case BuyOrderStatus.InquiriesAdded: return InquiriesAddedDescription;
                case BuyOrderStatus.TechnicalScoreAdded: return TechnicalScoreAddedDescription;
                case BuyOrderStatus.WinnerIsSelected: return WinnerIsSelectedDescription;
                case BuyOrderStatus.TempReceiptGenerated: return TempReceiveGeneratedDescription;
                case BuyOrderStatus.FinanceDocumentSetted: return FinanceDocRegisteredDescription;
                case BuyOrderStatus.QcAccepted: return QcAcceptedDescription;
                case BuyOrderStatus.QcRejected: return QcRejectedDescription;
                case BuyOrderStatus.EngineeringAccepted: return EngineeringAcceptedDescription;

                case BuyOrderStatus.RejectedGivenToSalvage: return RejectedGivenToSalvageDescription;
                case BuyOrderStatus.RejectedAcceptedByPmo: return RejectedAcceptedByPmoDescription;
                case BuyOrderStatus.RejectedAccepted: return RejectedAcceptedDescription;
                case BuyOrderStatus.FinanceAcceptedGivenToWarehouse: return FinanceAcceptedDescription;
                case BuyOrderStatus.CommercialBought: return CommercialBoughtDescription;
                case BuyOrderStatus.NewBuyOrderForAssembler: return NewBuyOrderForAssemblerDescription;
                case BuyOrderStatus.AssemblerAssembled: return AssemblerAssembledDescription;
                case BuyOrderStatus.NewBuyOrderForConstructor: return NewBuyOrderForConstractDescription;
                case BuyOrderStatus.ConstructorConstructed: return ConstructorConstructedDescription;
                case BuyOrderStatus.CompletedWarehouseFinallyAccepted: return WarehouseFinallyAcceptedDescription;
                case BuyOrderStatus.NeedsCommercialFixes: return NeedsFixesDescription;
                case BuyOrderStatus.NeedsAssemblerFixes: return NeedsFixesDescription;
                case BuyOrderStatus.NeedsConstractorFixes: return NeedsFixesDescription;
                default: return UnSupportedDescription;
            }
        }
    }
}
