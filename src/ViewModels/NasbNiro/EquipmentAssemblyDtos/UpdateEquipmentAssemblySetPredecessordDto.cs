using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentAssemblyDtos
{
    public class UpdateEquipmentAssemblySetPredecessordDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public int PredecessorId { get; set; }
    }
}
