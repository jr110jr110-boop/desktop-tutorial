using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class ResponseUpdateBuyOrderChangeCountDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public int BuyCount { get; set; }

        [Required]
        public int ReserveCount { get; set; }

        [Required]
        public int RealBuyCount { get; set; }
    }
}
