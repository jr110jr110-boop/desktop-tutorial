using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.EquipmentProcessDtos
{
    public class FinalizeEquipmentProcessFileDto
    {
        [Required]
        public int EquipmentProcessId { get; set; }

        [Required]
        public Guid DocumentId { get; set; }
    }
}
