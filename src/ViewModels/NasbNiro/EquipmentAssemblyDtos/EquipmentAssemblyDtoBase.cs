using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentAssemblyDtos
{
    public abstract class EquipmentAssemblyDtoBase
    {
        [Required]
        public int EquipmentId { get; set; }

        [Required]
        public int AssemblerDuration { get; set; }

        [Required]
        public string Code { get; set; }

        [Required]
        public string Title { get; set; }
    }
}
