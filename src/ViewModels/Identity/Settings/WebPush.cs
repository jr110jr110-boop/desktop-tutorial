using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.Identity.Settings
{
    public class WebPush
    {
        [Display(Name = " عنوان  ")]
        [Required(ErrorMessage = "وارد کردن {0}  الزامیست")]
        public string title { set; get; }
        [Display(Name = " شرح ")]
        [Required(ErrorMessage = "وارد کردن {0}  الزامیست")]
        public string message { set; get; }
        [Display(Name = " کاربر ")]
        public int UserId { set; get; }
        [Display(Name = " کد پکیج ")]
        public string coursePackage { set; get; }
        public string icon { set; get; }
        public dynamic Data { get; set; }
        public List<UserDto> Users { get; set; }
    }
    public class UserDto
    {
        public int Id { set; get; }
        public string DisplayName { set; get; }
    }

}