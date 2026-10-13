using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderProcesses
{
    public class BuyOrderProcessesDtoKey
    {
        [Required]
        public int BuyOrderId { get; set; }

        [Required]
        public int ProcessId { get; set; }
    }
}
