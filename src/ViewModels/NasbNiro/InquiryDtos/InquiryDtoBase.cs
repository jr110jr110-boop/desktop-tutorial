using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.InquiryDtos
{
    public abstract class InquiryDtoBase
    {
        [Required]
        public int Count { get; set; }

        [Required]
        public decimal Price { get; set; }
        public Guid TechnicalDoc { get; set; }
        public Guid FinancialDoc { get; set; }
    }
}
