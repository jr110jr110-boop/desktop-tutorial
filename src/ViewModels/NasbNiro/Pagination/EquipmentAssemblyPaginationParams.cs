using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.Pagination
{
    public class EquipmentAssemblyPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }

        public int? ParentId { get; set; }

        [Required]
        public int EquipmentId { get; set; }

        public bool IncludeTypes { get; set; } = false;
    }
}
