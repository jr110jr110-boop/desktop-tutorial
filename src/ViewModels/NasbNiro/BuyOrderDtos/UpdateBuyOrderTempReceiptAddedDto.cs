using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderTempReceiptAddedDto
    {
        [Required]
        public int Id { get; set; }
        public string TempReceiptCode { get; set; }
        public Guid LogShitId { get; set; }

        //public DateTimeOffset? OptionalTempReceiveDate { get; set; }
    }
}
