using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class UpdateProcessPmoAcceptDto
    {
        [Required]
        public int Id { get; set; }
    }
}
