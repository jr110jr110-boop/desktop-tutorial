using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderAssemblerAssembled
    {
        [Required]
        public int Id { get; set; }
        public Guid TimeSheet { get; set; }
    }
}
