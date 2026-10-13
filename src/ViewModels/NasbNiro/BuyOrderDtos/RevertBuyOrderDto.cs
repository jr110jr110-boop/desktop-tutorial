using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class RevertBuyOrderDto
    {
        [Required]
        public int BuyOrderId { get; set; }

        [Required]
        public BuyOrderStatus Status { get; set; }
    }
}
