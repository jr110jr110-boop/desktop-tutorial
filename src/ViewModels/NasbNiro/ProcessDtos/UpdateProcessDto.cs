using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class UpdateProcessDto : ProcessDtoBase
    {
        [Required]
        public int Id { get; set; }
    }
}
