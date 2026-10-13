using System.ComponentModel.DataAnnotations;
using ViewModels.NasbNiro.DocumentDtos;

namespace ViewModels.NasbNiro.TenderDtos
{
    public class ResponseTenderDto : TenderDtoBase
    {
        [Display(Name = "شناسه مناقصه")]
        public int Id { get; set; }

        [Display(Name = "کد فایلهای مناقصه")]
        public ResponseFileDto[] Files { get; set; }

        [Display(Name = "کد ضمانت نامه های مناقصه")]
        public ResponseTenderGuarantyDto[] Guaranties { get; set; }

        [Display(Name = "حذف شده")]
        public bool Deleted { get; set; }
    }
}
