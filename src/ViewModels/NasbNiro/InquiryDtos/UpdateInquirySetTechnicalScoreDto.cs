using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.InquiryDtos
{
    public class UpdateInquirySetTechnicalScoreDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public int TechnicalScore { get; set; }
    }
}
