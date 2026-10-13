using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class FinalizeProcessFileDto
    {
        [Required]
        public int ProcessId { get; set; }

        [Required]
        public Guid DocumentId { get; set; }
    }
}
