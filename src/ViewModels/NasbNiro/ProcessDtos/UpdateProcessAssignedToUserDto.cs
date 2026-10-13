using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class UpdateProcessAssignedToUserDto
    {
        [Required]
        public int Id { get; set; }
    }
}
