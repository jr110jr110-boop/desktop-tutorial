using System.Collections.Generic;
using ViewModels.NasbNiro.EquipmentBomDtos;
using ViewModels.NasbNiro.EquipmentDtos;

namespace ViewModels.NasbNiro.EquipmentAssemblyDtos
{
    public class ResponseEquipmentAssemblyDto : EquipmentAssemblyDtoBase
    {
        public int Id { get; set; }
        public bool Deleted { get; set; }
        public decimal TotalWeightPercent { get; set; }
        public int? ParentId { get; set; }
        public IEnumerable<ResponseEquipmentAssemblyDto> Children { get; set; }
        public ResponseEquipmentWithoutTypesDto Equipment { get; set; }
        public IEnumerable<ResponseEquipmentBomDto> EquipmentBoms { get; set; }
    }
}
