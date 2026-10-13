using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderWarehouseFinallyAccepted
    {
        [Required]
        public int Id { get; set; }
        public string ReceiptCode { get; set; }
    }
}
