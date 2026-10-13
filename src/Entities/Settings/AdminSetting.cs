using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Entities.Identity;
using Entities.Model;

namespace Entities.Settings
{
    public class AdminSetting
    {
        public AdminSetting(string brokerageName, string brokerageLogo, LoginType loginType, bool isActiveRegistrationSMS,
            string registrationSMSText, int eRegistrationScore, int inPersonRegistrationScore, int profileCompletionScore,
            int scoreOfIntroducingNewCustomer, DateTime modifyDate, int modifierUserId, long brokerageCode,
            long brokerageBranchCode, int transactionPercentage, long scorePrice
            , int transactionPercentageGroup2, long transactionPercentageGroupSell1,
            int transactionPercentageGroup3, long transactionPercentageGroupSell2, long transactionPercentageGroupSell3)
        {
            Update(brokerageName, brokerageLogo, loginType, isActiveRegistrationSMS,
             registrationSMSText, eRegistrationScore, inPersonRegistrationScore, profileCompletionScore,
             scoreOfIntroducingNewCustomer, modifyDate, modifierUserId, brokerageCode, brokerageBranchCode, transactionPercentage, scorePrice, transactionPercentageGroup2, transactionPercentageGroupSell1,
            transactionPercentageGroup3, transactionPercentageGroupSell2, transactionPercentageGroupSell3);
        }

        public void Update(string brokerageName, string brokerageLogo, LoginType loginType, bool isActiveRegistrationSMS,
            string registrationSMSText, int eRegistrationScore, int inPersonRegistrationScore, int profileCompletionScore,
            int scoreOfIntroducingNewCustomer, DateTime modifyDate, int modifierUserId, long brokerageCode,
            long brokerageBranchCode, int transactionPercentage, long scorePrice, int transactionPercentageGroup2, long transactionPercentageGroupSell1,
            int transactionPercentageGroup3, long transactionPercentageGroupSell2, long transactionPercentageGroupSell3)
        {
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

        [Key]
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

        public long BrokerageCode { get; set; }

        public long BrokerageBranchCode { get; set; }

        public int TransactionPercentage { get; set; }

        public long ScorePrice { get; set; }
        public long TransactionPercentageGroupSell1 { get; set; }


        public int TransactionPercentageGroup2 { get; set; }

        public long TransactionPercentageGroupSell2 { get; set; }



        public int TransactionPercentageGroup3 { get; set; }

        public long TransactionPercentageGroupSell3 { get; set; }

        public DateTime ModifyDate { get; set; }

        public int ModifierUserId { get; set; }

        [ForeignKey("ModifierUserId")]
        public virtual User ModifierUser { get; set; }

    }
}
