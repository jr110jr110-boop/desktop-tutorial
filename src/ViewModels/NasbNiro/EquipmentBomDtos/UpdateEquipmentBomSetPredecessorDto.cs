using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentBomDtos
{
    public class UpdateEquipmentBomSetPredecessorDto
    {
        [Required]
        public int EquipmentBomId { get; set; }

        [Required]
        public int PredecessorId { get; set; }
    }
}
