using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentBomDtos
{
    public class UpdateEquipmentBomDto : EquipmentBomDtoBase
    {
        [Required]
        public int Id { get; set; }
    }
}
