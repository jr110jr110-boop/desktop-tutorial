using System.ComponentModel.DataAnnotations;

namespace ViewModels.Dto
{
    public class UserEditDto
    {
        [Required(ErrorMessage = "(*)")]
        [Display(Name = "نام کاربری")]
        public string Username { get; set; }

        public string Email { get; set; }


        public string PhoneNumber { get; set; }

        public bool IsActive { get; set; } = true;


        [Required(ErrorMessage = "(*)")]
        public string FirstName { get; set; }

        [Required(ErrorMessage = "(*)")]
        public string LastName { get; set; }

        public bool IsDeleted { get; set; }

        public string InviteCode { get; set; }

        public int UserId { get; set; }

    }

}
