using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentProcessDtos
{
    public class UpdateEquipmentProcessDto : EquipmentProcessDtoBase
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public virtual int Sort { get; set; }
    }
}
