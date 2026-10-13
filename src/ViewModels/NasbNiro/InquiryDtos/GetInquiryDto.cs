using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.InquiryDtos
{
    public class GetInquiryDto
    {
        [Required]
        public int BuyOrderId { get; set; }

        [Required]
        public int Id { get; set; }

        public static implicit operator GetInquiryDto(Inquiry inquiry)
        {
            return new GetInquiryDto()
            {
                Id = inquiry.Id,
                BuyOrderId = inquiry.BuyOrderId,
            };
        }
    }
}
