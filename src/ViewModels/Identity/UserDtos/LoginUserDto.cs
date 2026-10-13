using System.ComponentModel.DataAnnotations;

namespace ViewModels.Identity.UserDtos
{
    public class LoginUserDto
    {
        [Required]
        public string UserName { get; set; }

        [Required]
        [MinLength(8)]
        public string Password { get; set; }
    }
}
