namespace Entities.Resources
{
    public static partial class NasbNiroMessages
    {
        public const string SuccessMessage = "عملیات موفق";
        public const string InvalidData = "داده نامعتبر";
        public const string FileNotAdded = "فایل اضافه نشد";
        public const string DocumentNotDeleted = "فایل سند حذف نشد.";
        public const string UpdateFailed = "درخواست بروزرسانی شکت خورد";
        public const string AlreadyExists = "موجود از قبل.";
        public const string WorkflowNotSelected = "قبل شروع روند کار طرح دستگردان مدارک را برای فرآیند انتخاب کنید.";

        public const string ProcessAlreadyHasBuyOrder = "یک یا چند فرآیند هم اکنون دستور خرید دارند.";

        #region BuyOrderProcesses
        public const string BuyOrderProcessesNotFound = "دستور خرید فرآیند یافت نشد.";
        public const string BuyOrderProcessesDeletingException = "حذف فرآیند و دستور خرید با خطا مواجه شد.";
        public const string BuyOrderProcessesNotDeleted = "فرآیند و دستور خرید حذف نشد.";
        public const string BuyOrderProcessesNotAdded = "فرآیند و دستور خرید اضافه نشد.";
        public const string BuyOrderQCNotAddedFiles = "اسناد کنترل کیفی اضافه نشده است.";
        #endregion

        #region Inquiry 
        public const string InquiryDeletingException = "استعلام به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string InquiryAlreadyIsDeleted = "استعلام قبلا حذف شده است";
        public const string InquiryDeleted = "استعلام حذف شد";
        public const string InquiryNotDeleted = "استعلام حذف نشد";
        public const string InquiryNotFound = "استعلام یافت نشد";
        public const string InquiryNotAdded = "استعلام ثبت نشد";
        public const string InquiryNotUpdated = "استعلام به روز نشد";
        #endregion

        #region Provider
        public const string ProviderDeletingException = "تامین کننده به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string ProviderAlreadyIsDeleted = "تامین کننده قبلا حذف شده است";
        public const string ProviderDeleted = "تامین کننده حذف شد";
        public const string ProviderNotDeleted = "تامین کننده حذف نشد";
        public const string ProviderNotFound = "تامین کننده یافت نشد";
        public const string ProviderNotAdded = "تامین کننده ثبت نشد";
        public const string ProviderNotUpdated = "تامین کننده به روز نشد";

        public const string ProviderProviderContactNotMatched = "عدم تطابق اطلاعات تماس و تامین کننده.";
        #endregion

        #region ProviderContact
        public const string ProviderContactDeletingException = "اطلاعات تماس به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string ProviderContactAlreadyIsDeleted = "اطلاعات تماس قبلا حذف شده است";
        public const string ProviderContactDeleted = "اطلاعات تماس حذف شد";
        public const string ProviderContactNotDeleted = "اطلاعات تماس حذف نشد";
        public const string ProviderContactNotFound = "اطلاعات تماس یافت نشد";
        public const string ProviderContactNotAdded = "اطلاعات تماس ثبت نشد";
        public const string ProviderContactNotUpdated = "اطلاعات تماس به روز نشد";
        #endregion

        #region ProviderAccount
        public const string ProviderAccountDeletingException = "اطلاعات حساب به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string ProviderAccountAlreadyIsDeleted = "اطلاعات حساب قبلا حذف شده است";
        public const string ProviderAccountDeleted = "اطلاعات حساب حذف شد";
        public const string ProviderAccountNotDeleted = "اطلاعات حساب حذف نشد";
        public const string ProviderAccountNotFound = "اطلاعات حساب یافت نشد";
        public const string ProviderAccountNotAdded = "اطلاعات حساب ثبت نشد";
        public const string ProviderAccountNotUpdated = "اطلاعات حساب به روز نشد";
        #endregion

        #region ContractWorkOrder
        public const string ContractWorkOrderNotMatched = "عدم تطابق قرارداد با دستور کار";
        #endregion

        #region WorkOrderAssembly
        public const string WorkOrderAssemblyNotMatched = "عدم تطابق دستور کار با تسک مونتاژ";
        #endregion

        #region BomProcess
        public const string BomProcessNotMatched = "عدم تطابق بوم با فرآیند ";
        #endregion

        #region Process
        public const string ProcessFileNotFound = "فایل فرآیند یافت نشد";
        public const string ProcessFileDeleted = "فایل فرآیند حذف شد";
        public const string ProcessFileNotDeleted = "فایل فرآیند حذف نشد";

        public const string ProcessDeletingException = "فرآیند به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string ProcessAlreadyIsDeleted = "فرآیند قبلا حذف شده است";
        public const string ProcessDeleted = "فرآیند حذف شد";

        public const string ProcessPmoShouldAccept = "فرآیند ابتدا باید توسط مدیر تایید شود.";
        public const string ProcessAcceptDocumentsImpossible = "امکان تایید اسناد به دلیل وضعیت فرآیند فراهم نمیباشد.";
        public const string ProcessAcceptDocumentsFirst = "ابتدا اسناد مهندسی باید تایید شوند.";
        public const string ProcessAcceptFinalizeDocuments = "ابتدا وضعیت اسناد مهندسی را مشخص کنید.";
        public const string ProcessAcceptIsAlreadyFinalized = "فرآیند قبلا نهایی شده است.";
        public const string ProcessCanNotAddBuyOrderDueToCurrentStatus = "وضعیت فعلی فرآیند امکان افزودن آن به دستور خرید را فراهم نمیکند.";

        public const string ProcessPmoCanNotAccept = "امکان تایید فرایند توسط مدیریت فراهم نیست.";

        public const string ProcessNotDeleted = "فرآیند حذف نشد";
        public const string ProcessNotFound = "فرآیند یافت نشد";
        public const string ProcessNotAdded = "فرآیند ثبت نشد";
        public const string ProcessNotUpdated = "فرآیند به روز نشد";
        #endregion

        #region AssemblyBom
        public const string AssemblyBomNotMatched = "عدم تطابق تسک مونتاژ با بوم ";
        #endregion

        #region Bom
        public const string BomDeletingException = "بوم به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string BomAlreadyIsDeleted = "بوم قبلا حذف شده است";
        public const string BomDeleted = "بوم حذف شد";

        public const string BomNotDeleted = "بوم حذف نشد";
        public const string BomNotFound = "بوم یافت نشد";
        public const string BomNotAdded = "بوم ثبت نشد";
        public const string BomNotUpdated = "بوم به روز نشد";
        #endregion

        #region Assembly
        public const string AssemblyCanNotUpdateDueToStatus =
            "با توجه به وضعیت مونتاژ،عملیات ممکن نیست.";

        public const string AssemblyCanNotStartAssembligDueToStatus =
            "این مونتاژ قبلا شروع شده است.";

        public const string AssemblyCanNotStartAssembligDueToProgressLevel =
            "امکان شروع مونتاژ به دلیل کامل نشدن این تسک مونتاژ،فراهم نیست.";

        public const string AssemblyDeletingException = "تسک مونتاژ به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string AssemblyAlreadyIsDeleted = "تسک مونتاژ قبلا حذف شده است";
        public const string AssemblyDeleted = "تسک مونتاژ حذف شد";

        public const string AssemblyNotDeleted = "تسک مونتاژ حذف نشد";
        public const string AssemblyNotAdded = "تسک مونتاژ ثبت نشد";
        public const string AssemblyNotUpdated = "تسک مونتاژ به روز نشد";
        #endregion

        #region WorkOrder
        public const string WorkOrderDeletingException = "دستور کار به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string WorkOrderAlreadyIsDeleted = "دستور کار قبلا حذف شده است";
        public const string WorkOrderDeleted = "دستور کار حذف شد";

        public const string WorkOrderNotDeleted = "دستور کار حذف نشد";
        public const string WorkOrderNotFound = "دستور کار یافت نشد";
        public const string WorkOrderNotAdded = "دستور کار ثبت نشد";
        public const string WorkOrderNotUpdated = "دستور کار به روز نشد";
        #endregion

        #region BuyOrder
        public const string BuyOrderDeletingException = "دستور خرید به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string BuyOrderAlreadyIsDeleted = "دستور خرید قبلا حذف شده است";
        public const string BuyOrderDeleted = "دستور خرید حذف شد";
        public const string BuyOrderNotDeleted = "دستور خرید حذف نشد";
        public const string BuyOrderNotFound = "دستور خرید یافت نشد";
        public const string BuyOrderNotAdded = "دستور خرید ثبت نشد";
        public const string BuyOrderNotUpdated = "دستور خرید به روز نشد";

        public const string BuyOrderCanNotAddNewInquires = " افزودن استعلام جدید به دستور خرید،با توجه به وضعیت آن امکان پذیر نیست.";
        public const string BuyOrderCanNotAcceptInquires = " تایید استعلام ها به دلیل وضعیت دستور خرید امکان پذیر نیست.";
        public const string BuyOrderCanNotAddTechnicalScore = " ثبت امتیاز فنی با توجه به وضعیت دستور خرید،امکان پذیر نیست.";
        public const string BuyOrderCanNotSetFinanceDoc = " ثبت سند مالی با توجه به وضعیت دستور خرید،امکان پذیر نیست.";
        public const string BuyOrderCanNotAcceptFinanceDoc = " تایید سند مالی با توجه به وضعیت دستور خرید،امکان پذیر نیست.";
        public const string BuyOrderCanNotAddTempReceiptDoc = " افزودن رسید موقت با توجه به وضعیت دستور خرید،امکان پذیر نیست.";
        public const string BuyOrderCanNotQcAcceptOrReject = " تایید یا رد توسط واحد کنترل کیفیت با توجه به وضعیت دستور خرید،امکان پذیر نیست.";
        public const string BuyOrderCanNotEngineeringAcceptOrReject = " تایید یا رد توسط واحد مهندسی با توجه به وضعیت دستور خرید،امکان پذیر نیست.";
        public const string BuyOrderCanPmoAcceptReject = " تایید ارسال به انبار ضایعات توسط برنامه ریزی وجود ندارد.";
        public const string BuyOrderCanPmoAcceptedReject = " تایید ارسال به انبار ضایعات توسط کاربر انبار وجود ندارد.";

        #endregion

        #region ContractEquipment
        public const string ContractEquipmentNotAdded = "محصولات به قرارداد اضافه نشد";
        public const string ContractEquipmentNotFound = "قرارداد شامل این محصولات نیست";
        public const string ContractEquipmentNotDeleted = "محصولات از قرارداد حذف نشد";
        public const string ContractEquipmentDeletingException = "حذف محصولات از قرارداد با خطا مواجه شد";
        #endregion 

        #region EquipmentEquipmentAssembly
        public const string EquipmentEquipmentAssemblyNotMatched = "عدم تطابق محصولات با قطعات";
        #endregion

        #region EquipmentAssembly
        public const string EquipmentAssemblyDeletingException = "قطعات محصولات به دلیل رخداد خطا وضعیت حذف نامشخص است";

        public const string EquipmentAssemblyDeleted = "قطعات محصولات حذف شد";
        public const string EquipmentAssemblyNotDeleted = "قطعات محصولات حذف نشد";
        public const string EquipmentAssemblyNotFound = "قطعات محصولات یافت نشد";
        public const string EquipmentAssemblyNotAdded = "قطعات محصولات ثبت نشد";
        public const string EquipmentAssemblyNotUpdated = "قطعات محصولات به روز نشد";
        #endregion

        #region EquipmentAssemblyEquipmentBom
        public const string EquipmentAssemblyEquipmentBomNotMatched = "عدم تطابق بوم با قطعات مونتاژ";
        #endregion

        #region EquipmentBom
        public const string EquipmentBomDeletingException = "بوم محصولات به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string EquipmentBomAlreadyIsDeleted = "بوم محصولات قبلا حذف شده است";

        public const string EquipmentBomDeleted = "بوم محصولات حذف شد";
        public const string EquipmentBomNotDeleted = "بوم محصولات حذف نشد";
        public const string EquipmentBomNotFound = "بوم محصولات یافت نشد";
        public const string EquipmentBomNotAdded = "بوم محصولات ثبت نشد";
        public const string EquipmentBomNotUpdated = "بوم محصولات به روز نشد";
        #endregion

        #region EquipmentBomEquipmentProcess
        public const string EquipmentBomEquipmentProcessNotMatched = "عدم تطابق بوم با فرآیند مونتاژ";
        #endregion

        #region EquipmentProcess
        public const string EquipmentProcessFileNotFound = "فایل فرآیند یافت نشد";
        public const string EquipmentProcessFileDeleted = "فایل فرآیند حذف شد";
        public const string EquipmentProcessFileNotDeleted = "فایل فرآیند حذف نشد";

        public const string EquipmentProcessDeletingException = "فرآیند به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string EquipmentProcessAlreadyIsDeleted = "فرآیند قبلا حذف شده است";

        public const string EquipmentProcessDeleted = "فرآیند حذف شد";
        public const string EquipmentProcessNotDeleted = "فرآیند حذف نشد";
        public const string EquipmentProcessNotFound = "فرآیند یافت نشد";
        public const string EquipmentProcessNotAdded = "فرآیند ثبت نشد";
        public const string EquipmentProcessNotUpdated = "فرآیند به روز نشد";
        #endregion

        #region Equipment
        public const string EquipmentDeletingException = "تجهزیات به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string EquipmentAlreadyIsDeleted = "تجهزیات قبلا حذف شده است";

        public const string EquipmentDeleted = "تجهزیات حذف شد";
        public const string EquipmentNotDeleted = "تجهزیات حذف نشد";
        public const string EquipmentNotFound = "تجهزیات یافت نشد";
        public const string EquipmentNotAdded = "تجهزیات ثبت نشد";
        public const string EquipmentNotUpdated = "تجهزیات به روز نشد";
        #endregion

        #region Contract
        public const string ContractFileNotFound = "فایل قرارداد یافت نشد";
        public const string ContractFileDeleted = "فایل قرارداد حذف شد";
        public const string ContractFileNotDeleted = "فایل قرارداد حذف نشد";

        public const string ContractDeletingException = "قرارداد به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string ContractAlreadyIsDeleted = "قرارداد قبلا حذف شده است";

        public const string ContractDeleted = "قرارداد حذف شد";
        public const string ContractNotDeleted = "قرارداد حذف نشد";
        public const string ContractNotFound = "قرارداد یافت نشد";
        public const string ContractNotAdded = "قرارداد ثبت نشد";
        public const string ContractNotUpdated = "قرارداد به روز نشد";
        #endregion

        #region Tender
        public const string TenderFileNotFound = "فایل مناقصه یافت نشد";
        public const string TenderFileDeleted = "فایل مناقصه حذف شد";
        public const string TenderFileNotDeleted = "فایل مناقصه حذف نشد";

        public const string TenderDeletingException = "مناقصه به دلیل رخداد خطا وضعیت حذف نامشخص است";
        public const string TenderAlreadyIsDeleted = "مناقصه قبلا حذف شده است";

        public const string TenderDeleted = "مناقصه حذف شد";
        public const string TenderNotDeleted = "مناقصه حذف نشد";
        public const string TenderNotFound = "مناقصه یافت نشد";
        public const string TenderNotAdded = "مناقصه ثبت نشد";
        public const string TenderNotUpdated = "مناقصه به روز نشد";
        public const string ProcessCanNotChangeUser = "امکان تغییر مهندس برای این فرآیند وجود ندارد.";
        #endregion
    }
}
