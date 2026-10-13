using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.TenderDtos
{
    public class UpdateTenderDto : TenderDtoBase
    {
        [Display(Name = "شناسه مناقصه")]
        [Required]
        public int Id { get; set; }
    }
}
