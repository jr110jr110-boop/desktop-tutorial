using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderWinnerSelectedDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public int WinnerInquiryId { get; set; }
    }
}
