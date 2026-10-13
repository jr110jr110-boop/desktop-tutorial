using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentAssemblyDtos
{
    public class UpdateEquipmentAssemblyDto : EquipmentAssemblyDtoBase
    {
        [Required]
        public int Id { get; set; }
    }
}
