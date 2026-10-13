using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.DocumentDtos
{
    public class AttachFileToEntityDto : UploadFileDto
    {
        [Required]
        public int EntityId { get; set; }
    }
}
