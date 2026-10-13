using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.Identity.UserDtos
{
    public class UpdateUserDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        public bool EmailConfirmed { get; set; }

        [Required]
        [Phone]
        public string PhoneNumber { get; set; }

        [Required]
        public bool PhoneNumberConfirmed { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string LastName { get; set; }

        [Required]
        public string NationalCode { get; set; }

        [Required]
        public DateTimeOffset BirthDate { get; set; }

        [Required]
        public int[] RoleIds { get; set; }
    }
}
