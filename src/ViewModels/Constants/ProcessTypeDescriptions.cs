using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;

namespace ViewModels.Constants
{
    public static class ProcessTypeDescriptions
    {
        public const string BuyDescription = "خرید کالا";
        public const string ServiceDescription = "خرید خدمات";
        public const string AssemblyDescription = "فرآیند مونتاژ";
        public const string FinalAssemblyDescription = "فرآیند مونتاژ نهایی";
        public const string ConstructionDescription = "فرآیند تولید";
        public const string NotSupportedDescription = "نوع فرآیند پشتیبانی نمیشود";

        public static string GetProcessTypeDescription(ProcessType value)
        {
            switch (value)
            {
                case ProcessType.Buy: return BuyDescription;
                case ProcessType.Service: return ServiceDescription;
                case ProcessType.Assembly: return AssemblyDescription;
                case ProcessType.FinalAssembly: return FinalAssemblyDescription;
                case ProcessType.Construction: return ConstructionDescription;
                default: return NotSupportedDescription;
            };
        }

        public static string GetProcessTypeDescription(EquipmentProcessType value)
        {
            switch (value)
            {
                case EquipmentProcessType.Buy: return BuyDescription;
                case EquipmentProcessType.Service: return ServiceDescription;
                case EquipmentProcessType.Assembly: return AssemblyDescription;
                case EquipmentProcessType.FinalAssembly: return FinalAssemblyDescription;
                case EquipmentProcessType.Construction: return ConstructionDescription;
                default: return NotSupportedDescription;
            };
        }
    }
}
