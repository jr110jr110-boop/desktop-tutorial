using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ContractEquipmentDtos
{
    public class ContractEquipmentDtoKey
    {
        [Required]
        public int ContractId { get; set; }

        [Required]
        public int EquipmentId { get; set; }
    }
}
