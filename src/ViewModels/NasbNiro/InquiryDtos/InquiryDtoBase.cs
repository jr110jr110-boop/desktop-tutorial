using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.InquiryDtos
{
    public abstract class InquiryDtoBase
    {
        [Required]
        public int Count { get; set; }

        // R1a2 (M8): nullable so that the answer can leave the price out for a signed-in user who is not allowed to see it (a missing value in a request is saved as 0, as before)
        public decimal? Price { get; set; }
        public Guid TechnicalDoc { get; set; }
        public Guid FinancialDoc { get; set; }
    }
}
