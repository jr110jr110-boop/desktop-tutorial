using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.Constants
{
    public static class ProcessStatusDescriptions
    {
        public const string NewProcessDescription = "فرآیند اخیرا افزوده شده است.";
        public const string PmoAcceptedDescription = "فرآیند توسط مدیر تایید شده است.";
        public const string DocumentsAddedDescription = "اسناد مهندسی به فرآیند افزوده شده اند.";
        public const string EngineerAcceptedDocumentsDescription = "اسناد مهندسی پذیرش شده اند.";
        public const string MasterShouldAcceptDescription = "این فرآیند باید توسط مشتری تایید شود.";
        public const string ReadyToGoDescription = "فرآیند نهایی شده است.";
        public const string NotSupportedDescription = "وضعیت فرآیند توسط سیستم قابل تشخیص نیست.";
        public const string BuyOrderHasBeenAddedDescription = "دستور خرید صادر شده است.";
        public const string ProcessIsCompletedDescription = "فرآیند تکمیل شده است.";
        public const string EngineeringWorkflowStartDescription = "فرآیند دستگردان مدارک شروع شده است.";

        public static string GetStatusDescription(ProcessStatus value)
        {
            switch (value)
            {
                case ProcessStatus.NewProcess: return NewProcessDescription;
                case ProcessStatus.PmoAccepted: return PmoAcceptedDescription;
                case ProcessStatus.DocumentsAdded: return DocumentsAddedDescription;
                case ProcessStatus.MasterShouldAccept: return MasterShouldAcceptDescription;
                case ProcessStatus.ReadyToGo: return ReadyToGoDescription;
                case ProcessStatus.BuyOrderHasBeenAdded: return BuyOrderHasBeenAddedDescription;
                case ProcessStatus.ProcessIsCompleted: return ProcessIsCompletedDescription;
                case ProcessStatus.EngineeringWorkflowStart: return EngineeringWorkflowStartDescription;
                case ProcessStatus.EngineerAcceptedDocuments: return EngineerAcceptedDocumentsDescription;
                default: return NotSupportedDescription;
            }
        }
    }
}
