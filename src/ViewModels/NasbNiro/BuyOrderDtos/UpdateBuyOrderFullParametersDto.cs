using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderFullParametersDto
    {

        [Required]
        public int Id { get; set; }

        public string ExitCommodityCode { get; set; }
        [Required]
        public string EnterCommodityCode { get; set; }
        [Required]
        public int Count { get; set; }
        [Required]
        public DateTimeOffset InquiryDate { get; set; }
        [Required]
        public DateTimeOffset InquiryAcceptDate { get; set; }
        [Required]
        public DateTimeOffset FinanceDocDate { get; set; }
        [Required]
        public DateTimeOffset FinanceAcceptDate { get; set; }
        [Required]
        public DateTimeOffset TempReceiveDate { get; set; }

        [Required]
        public DateTimeOffset ReceiveDate { get; set; }

        //[Required]
        //public BuyOrderStatus Status { get; set; }

        [Required]
        public string FinanceType { get; set; }
        [Required]
        public string FinanceNumber { get; set; }
        [Required]
        public decimal FinancePrice { get; set; }
        [Required]
        public int BuyCount { get; set; }
        [Required]
        public int ReserveCount { get; set; }
        [Required]
        public int RealBuyCount { get; set; }
    }
}
