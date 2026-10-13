using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentProcessDtos
{
    public class GetEquipmentProcessDto : GetEntityDto<int>
    {
        [Required]
        public int EquipmentBomId { get; set; }
    }
}
