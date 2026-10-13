using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.Pagination
{
    public class WorkOrderPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }
        public int? Count { get; set; }

        [Required]
        public int ContractId { get; set; }
    }
}
