using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderInquiriesAddedDto
    {
        [Required]
        public int Id { get; set; }
        public string FinanceType { get; set; }

        //public DateTimeOffset? OptionalInquiryDate { get; set; }
    }
}
