using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class RevertProcessDto
    {
        [Required]
        public int ProcessId { get; set; }

        [Required]
        public ProcessStatus Status { get; set; }
    }
}
