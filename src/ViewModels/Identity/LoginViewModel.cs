using System.ComponentModel.DataAnnotations;

namespace ViewModels.Identity
{
    public class RegisterConfirmViewModel
    {
        [Required(ErrorMessage = "(*)")]
        [Display(Name = "نام کاربری")]
        public string Username { get; set; }

        [Required(ErrorMessage = "(*)")]
        [Display(Name = "کد تایید")]
        public string ConfirmCode { get; set; }

    }
}