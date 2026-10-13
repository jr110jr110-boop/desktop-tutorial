using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentProcessDtos
{
    public class UpdateEquipmentProcessSortDto
    {
        [Required]
        public int Id1 { get; set; }

        [Required]
        public int Id2 { get; set; }
    }
}
