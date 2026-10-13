using System.ComponentModel.DataAnnotations;

namespace ViewModels.Identity
{
    public class ResetPasswordWithUserNameViewModel
    {
        [Required(ErrorMessage = "(*)")]
        [Display(Name = "نام کاربری")]
        public string UserName { get; set; }

        [Required(ErrorMessage = "(*)")]
        [StringLength(100, ErrorMessage = "{0} باید حداقل {2} و حداکثر {1} حرف باشند.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "کلمه‌ی عبور")]
        public string Password { get; set; }

        [Required(ErrorMessage = "(*)")]
        [DataType(DataType.Password)]
        [Display(Name = "تکرار کلمه‌ی عبور")]
        [Compare(nameof(Password), ErrorMessage = "کلمات عبور وارد شده با هم تطابق ندارند")]
        public string ConfirmPassword { get; set; }

        public string Code { get; set; }
        public long CanfirmCode { get; set; }
    }
}