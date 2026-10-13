using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.Pagination
{
    public class BomSimplePaginationParams : BaseEntityPaginationParams
    {
        public string Title { get; set; }

        [Required]
        public int AssemblyId { get; set; }
    }
}
