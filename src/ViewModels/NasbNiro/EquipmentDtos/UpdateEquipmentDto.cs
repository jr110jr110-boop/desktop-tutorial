using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentDtos
{
    public class UpdateEquipmentDto : EquipmentBaseDto
    {
        [Display(Name = "شناسه تجهیزات")]
        [Required]
        public int Id { get; set; }
    }
}
