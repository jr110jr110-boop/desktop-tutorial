using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class UpdateBuyOrderConstructorConstructed
    {
        [Required]
        public int Id { get; set; }
        public int MachineId { get; set; }
        public Guid TimeSheet { get; set; }
        public string ConstractorDescription { get; set; }
    }
}
