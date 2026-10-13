using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderChangeCountDto
    {
        [Required]
        public int Id { get; set; }
    }
}
