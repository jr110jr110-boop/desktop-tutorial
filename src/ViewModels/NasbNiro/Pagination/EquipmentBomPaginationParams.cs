using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.Pagination
{
    public class EquipmentBomPaginationParams : BaseEntityPaginationParams
    {
        [Required]
        public int EquipmentAssemblyId { get; set; }

        public int? HasPositionNumber { get; set; }
        public string Contains { get; set; }
        public int? Count { get; set; }
        public decimal? NotWetWeightLessThan { get; set; }
        public decimal? NotWetWeightMoreThan { get; set; }
        public decimal? WetWeightLessThan { get; set; }
        public decimal? WetWeightMoreThan { get; set; }
        public int? Sort { get; set; }
        public bool IncludeTypes { get; set; } = false;
    }
}
