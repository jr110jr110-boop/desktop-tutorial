using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.Pagination
{
    public class AssemblySimplePaginationParams : BaseEntityPaginationParams
    {
        public string Title { get; set; }

        [Required]
        public int WorkOrderId { get; set; }
    }
}
