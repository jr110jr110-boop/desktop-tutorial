using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.BuyOrderProcesses
{
    public abstract class BuyOrderProcessesDtoBase : BuyOrderProcessesDtoKey
    {
        [Required]
        public string Attribute { get; set; }
    }
}
