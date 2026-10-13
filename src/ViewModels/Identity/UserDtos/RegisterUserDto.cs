using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.Identity.UserDtos
{
    public class RegisterUserDto
    {
        [Required]
        public string UserName { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string LastName { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        public int[] RoleIds { get; set; }

        [Required]
        [MinLength(8)]
        public string Password { get; set; }

        [Required]
        [Phone]
        public string PhoneNumber { get; set; }

        [Required]
        public DateTimeOffset BirthDate { get; set; }

        [Required]
        [MinLength(10)]
        public string NationalCode { get; set; }
    }
}
