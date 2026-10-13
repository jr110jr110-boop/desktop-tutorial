using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Equipments;

namespace ViewModels.NasbNiro.Pagination
{
    public class EquipmentProcessPaginationParams : BaseEntityPaginationParams
    {
        public string Contains { get; set; }
        public int? MapDurationMoreThan { get; set; }
        public int? MapDurationLessThan { get; set; }
        public int? MasterDurationMoreThan { get; set; }
        public int? MasterDurationLessThan { get; set; }
        public int? ProcessDurationMoreThan { get; set; }
        public int? ProcessDurationLessThan { get; set; }
        public EquipmentProcessType[] WithTypes { get; set; }
        public decimal? ProgramPriceMoreThan { get; set; }
        public decimal? ProgramPriceLessThan { get; set; }

        [Required]
        public int EquipmentBomId { get; set; }
        public bool IncludeTypes { get; set; } = false;
    }
}
