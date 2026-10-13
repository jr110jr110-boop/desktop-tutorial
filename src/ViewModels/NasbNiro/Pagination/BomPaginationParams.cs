using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.Pagination
{
    public class BomPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }

        [Required]
        public int AssemblyId { get; set; }
        public int? PositionNumber { get; set; }
        public int? Count { get; set; }
        public decimal? NotWetWeightLessThan { get; set; }
        public decimal? NotWetWeightMoreThan { get; set; }
        public decimal? WetWeightLessThan { get; set; }
        public decimal? WetWeightMoreThan { get; set; }
    }
}
