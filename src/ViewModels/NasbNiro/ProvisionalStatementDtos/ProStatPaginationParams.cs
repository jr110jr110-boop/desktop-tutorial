using System.ComponentModel.DataAnnotations;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProvisionalStatementDtos
{
    public class ProStatPaginationParams : PaginationParams
    {
        [Required]
        public int ContractId { get; set; }
    }
}
