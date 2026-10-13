using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class UpdateProcessSelectUserDto
    {
        [Required]
        public int Id { get; set; }
    }
}
