using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.Pagination
{
    public class ReadyProcessesForBuyOrderPaginationaParams : BaseEntityPaginationParams
    {
        [Required]
        public int WorkOrderId { get; set; }

        [Required]
        public string EnterCommodityCode { get; set; }
    }
}
