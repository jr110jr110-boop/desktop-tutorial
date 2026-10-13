using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentDtos
{
    public abstract class EquipmentBaseDto
    {
        [Required]
        public string Code { get; set; }

        [Required]
        public string Title { get; set; }

        public string Description { get; set; }
    }
}
