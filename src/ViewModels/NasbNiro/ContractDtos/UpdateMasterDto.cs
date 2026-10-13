using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ContractDtos
{
    public class UpdateMasterDto
    {
        [Required]
        public int ContractId { get; set; }

        [Required]
        public string Master { get; set; }
    }
}
