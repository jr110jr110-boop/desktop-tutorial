using System.ComponentModel.DataAnnotations;

namespace ViewModels.Identity.UserDtos
{
    public class ChangePasswordDto
    {
        [Required]
        public int UserId { get; set; }

        [Required]
        [MinLength(8)]
        public string NewPassword { get; set; }
    }
}
