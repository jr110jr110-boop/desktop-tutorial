using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.InquiryDtos
{
    public class AddInquiryDto : InquiryDtoBase
    {
        [Required]
        public int ProviderId { get; set; }

        [Required]
        public int BuyOrderId { get; set; }
    }
}
