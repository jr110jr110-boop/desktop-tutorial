using System.ComponentModel.DataAnnotations;
using ViewModels.NasbNiro.DocumentDtos;

namespace ViewModels.NasbNiro.ContractDtos
{
    public class ResponseContractDto : ContractDtoBase
    {
        [Display(Name = "شناسه")]
        public int Id { get; set; }

        [Display(Name = "شناسه فایل های قرارداد")]
        public ResponseFileDto[] Files { get; set; }
        public ResponseContractGuarantyDto[] Guaranties { get; set; }

        public string Master { get; set; }

        [Display(Name = "حذف شده")]
        public bool Deleted { get; set; }
        public ResponseFileDto ProjectCharterFile { get; set; }
        public bool ForProvider { get; set; }
    }
}
