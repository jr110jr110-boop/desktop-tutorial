using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.Pagination
{
    public class BuyOrderPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }

        [Required]
        public int WorkOrderId { get; set; }
    }
}
