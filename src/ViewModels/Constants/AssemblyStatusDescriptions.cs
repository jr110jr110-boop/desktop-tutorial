using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.Constants
{
    public class AssemblyStatusDescriptions
    {
        public const string NewAssemblyDescription = "در جریان،مونتاژ شروع نشده است.";
        public const string ReadyToAssembleDescription = "آماده مونتاژ";
        public const string AssemblerAssembledDescription = "مونتاژ شده،در انتظار تایید واحد کنترل کیفیت";
        public const string QcAcceptToFinalAssembleDescription = "تایید توسط کنترل کیفی ، در انتظار مونتاژ نهایی";
        public const string QcRejectToAssembleDescription = "بازگشت از کنترل کیفی برای مونتاژ";
        public const string ReadyToFinalAssembleDescription = "آماده مونتاژ نهایی";
        public const string AssemblerFinalAssembledDescription = "مونتاژ نهایی انجام شده.";
        public const string QcRejectToFinalAssembleDescription = "بازگشت از کنترل کیفی برای مونتاژ نهایی";
        public const string PendingFinalAcceptanceDescription = "در انتظار تایید نهایی مدیر";
        public const string IsCompletedDescription = "مونتاژ تکمیل شده است.";
        public const string UnSupportedDescription = "وضعیت مونتاژ پشتیبانی نمیشود.با پشتیبانی تماس بگیرید.";


        internal static string GetProperDescription(AssemblyStatus value)
        {
            switch (value)
            {
                case AssemblyStatus.NewAssembly: return NewAssemblyDescription;
                case AssemblyStatus.ReadyToAssemble: return ReadyToAssembleDescription;
                case AssemblyStatus.AssemblerAssembled: return AssemblerAssembledDescription;
                case AssemblyStatus.QcAcceptToFinalAssemble: return QcAcceptToFinalAssembleDescription;
                case AssemblyStatus.QcRejectToAssemble: return QcRejectToAssembleDescription;
                case AssemblyStatus.ReadyToFinalAssemble: return ReadyToFinalAssembleDescription;
                case AssemblyStatus.AssemblerFinalAssembled: return AssemblerFinalAssembledDescription;
                case AssemblyStatus.QcRejectToFinalAssemble: return QcRejectToFinalAssembleDescription;
                case AssemblyStatus.PendingFinalAcceptance: return PendingFinalAcceptanceDescription;
                case AssemblyStatus.IsCompleted: return IsCompletedDescription;
                default: return UnSupportedDescription;
            }
        }
    }
}
