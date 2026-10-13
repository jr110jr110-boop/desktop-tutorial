using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.Pagination
{
    public class AssemblyPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }
        public int? ParentId { get; set; }

        [Required]
        public int WorkOrderId { get; set; }
    }
}
