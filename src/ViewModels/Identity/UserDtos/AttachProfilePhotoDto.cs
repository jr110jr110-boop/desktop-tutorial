using System.ComponentModel.DataAnnotations;
using ViewModels.NasbNiro.DocumentDtos;

namespace ViewModels.Identity.UserDtos
{
    public class AttachProfilePhotoDto : UploadFileDto
    {
        [Required]
        public int UserId { get; set; }
    }
}
