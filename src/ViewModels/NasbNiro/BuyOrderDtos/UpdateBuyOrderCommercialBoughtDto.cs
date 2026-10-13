using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderCommercialBoughtDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public int RealBuyCount { get; set; }
    }
}
