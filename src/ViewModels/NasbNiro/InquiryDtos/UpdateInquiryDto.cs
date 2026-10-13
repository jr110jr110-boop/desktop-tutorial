using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.InquiryDtos
{
    public class UpdateInquiryDto : InquiryDtoBase
    {
        [Required]
        public int Id { get; set; }
    }
}
