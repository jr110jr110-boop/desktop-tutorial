using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model;

namespace ViewModels.Dto
{
    public class AdminSettingVM
    {
        public AdminSettingVM()
        {

        }

        public AdminSettingVM(int id, string brokerageName, string brokerageLogo, LoginType loginType,
            bool isActiveRegistrationSMS, string registrationSMSText, int eRegistrationScore, int inPersonRegistrationScore,
            int profileCompletionScore, int scoreOfIntroducingNewCustomer, long brokerageCode, long brokerageBranchCode,
            string modifyDate, int modifierUserId, string modifierUserFullName, int transactionPercentage, long scorePrice,
            int transactionPercentageGroup2, long transactionPercentageGroupSell1,
            int transactionPercentageGroup3, long transactionPercentageGroupSell2, long transactionPercentageGroupSell3)
        {
            Id = id;
            BrokerageName = brokerageName;
            BrokerageLogo = brokerageLogo;
            LoginType = loginType;
            IsActiveRegistrationSMS = isActiveRegistrationSMS;
            RegistrationSMSText = registrationSMSText;
            ERegistrationScore = eRegistrationScore;
            InPersonRegistrationScore = inPersonRegistrationScore;
            ProfileCompletionScore = profileCompletionScore;
            ScoreOfIntroducingNewCustomer = scoreOfIntroducingNewCustomer;
            ModifyDate = modifyDate;
            ModifierUserId = modifierUserId;
            ModifierUserFullName = modifierUserFullName;
            BrokerageCode = brokerageCode;
            BrokerageBranchCode = brokerageBranchCode;
            TransactionPercentage = transactionPercentage;
            ScorePrice = scorePrice;
            TransactionPercentageGroup2 = transactionPercentageGroup2;
            TransactionPercentageGroup3 = transactionPercentageGroup3;
            TransactionPercentageGroupSell1 = transactionPercentageGroupSell1;
            TransactionPercentageGroupSell2 = transactionPercentageGroupSell2;
            TransactionPercentageGroupSell3 = transactionPercentageGroupSell3;
        }


        public int Id { get; set; }

        public string BrokerageName { get; set; }

        public string BrokerageLogo { get; set; }

        public LoginType LoginType { get; set; }

        public bool IsActiveRegistrationSMS { get; set; }

        public string RegistrationSMSText { get; set; }

        public int ERegistrationScore { get; set; }

        public int InPersonRegistrationScore { get; set; }

        public int ProfileCompletionScore { get; set; }

        public int ScoreOfIntroducingNewCustomer { get; set; }

        public string ModifyDate { get; set; }

        public int ModifierUserId { get; set; }

        public string ModifierUserFullName { get; set; }

        public long BrokerageCode { get; set; }

        public long BrokerageBranchCode { get; set; }

        public int TransactionPercentage { get; set; }

        public long ScorePrice { get; set; }

        public int TransactionPercentageGroup2 { get; set; }

        public int TransactionPercentageGroup3 { get; set; }
        public long TransactionPercentageGroupSell1 { get; set; }
        public long TransactionPercentageGroupSell2 { get; set; }
        public long TransactionPercentageGroupSell3 { get; set; }
    }

    public class AdminSettingListDto
    {
        public List<AdminSettingVM> List { get; set; }

        public int Count { get; set; }
    }

    public enum SellOrBuy
    {
        [Display(Name = "خرید")]
        buy = 1,
        [Display(Name = "فروش")]
        sell = 2
    }
}
