using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderFinanceAcceptedDto
    {
        [Required]
        public int Id { get; set; }

        //public DateTimeOffset? OptionalFinanceAcceptDate { get; set; }
    }
}
