using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ContractEquipmentDtos
{
    public abstract class ContractEquipmentDtoBase : ContractEquipmentDtoKey
    {
        [Required]
        public int Count { get; set; }
    }
}
