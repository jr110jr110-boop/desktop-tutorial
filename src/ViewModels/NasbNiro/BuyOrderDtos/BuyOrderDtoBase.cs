using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public abstract class BuyOrderDtoBase
    {

        [Required]
        public string ExitCommodityCode { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public string EnterCommodityCode { get; set; }

        [Required]
        public int Count { get; set; }

        [Required]
        public DateTimeOffset FinanceAcceptDate { get; set; }

        [Required]
        public DateTimeOffset TempReceiveDate { get; set; }

        [Required]
        public DateTimeOffset ReceiveDate { get; set; }

        public int MachineId { get; set; }
        public Guid TimeSheet { get; set; }
        public string ConstractorDescription { get; set; }

        [Required]
        public int ReserveCount { get; set; }

        [Required]
        public int BuyCount { get; set; }

        [Required]
        public int RealBuyCount { get; set; }
        public Guid LogShitId { get; set; }

        [Required]
        public DateTimeOffset FinanceDocDate { get; set; }

        [Required]
        public int WorkOrderId { get; set; }

        public string TempReceiptCode { get; set; }
        public string ReceiptCode { get; set; }
    }
}
