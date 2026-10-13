using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderDto : BuyOrderDtoBase
    {
        [Required]
        public int Id { get; set; }
    }
}
