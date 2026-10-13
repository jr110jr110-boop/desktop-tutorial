using System.ComponentModel.DataAnnotations;
using ViewModels.NasbNiro.DocumentDtos;

namespace ViewModels.NasbNiro.ContractDtos
{
    public class AttachProjectCharterFileDto : UploadFileDto
    {
        [Required]
        public int ContractId { get; set; }
    }
}
