using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.Pagination
{
    public class InquiryPaginationParams : BaseEntityPaginationParams
    {
        [Required]
        public int BuyOrderId { get; set; }
        public int? ProviderId { get; set; }
    }
}
