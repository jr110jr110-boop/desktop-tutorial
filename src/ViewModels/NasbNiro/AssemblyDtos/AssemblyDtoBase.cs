using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.AssemblyDtos
{
    public abstract class AssemblyDtoBase
    {
        [Required]
        public string Title { get; set; }

        [Required]
        public string Code { get; set; }

        [Required]
        public int WorkOrderId { get; set; }

        [Required]
        public int AssemblerDuration { get; set; }
    }
}
