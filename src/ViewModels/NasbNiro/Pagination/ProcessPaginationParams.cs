using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.Pagination
{
    public class ProcessPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }
        public int[] WithStatuses { get; set; }

        [Required]
        public int BomId { get; set; }
    }
}
