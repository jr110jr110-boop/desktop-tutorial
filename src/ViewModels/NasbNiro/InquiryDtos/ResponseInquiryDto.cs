using System;
using ViewModels.NasbNiro.ProviderDtos;

namespace ViewModels.NasbNiro.InquiryDtos
{
    public class ResponseInquiryDto : InquiryDtoBase
    {
        public int Id { get; set; }
        public bool Deleted { get; set; }
        public bool IsAccept { get; set; }
        public int TechnicalScore { get; set; }
        public int ProviderId { get; set; }
        public int BuyOrderId { get; set; }

        public virtual ResponseProviderDto Provider { get; set; }
    }
}
