using System.Collections.Generic;
using ViewModels.NasbNiro.EquipmentProcessDtos;

namespace ViewModels.NasbNiro.EquipmentBomDtos
{
    public class ResponseEquipmentBomDto : EquipmentBomDtoBase
    {
        public int Id { get; set; }
        public bool Deleted { get; set; }
        public int? PredecessorId { get; set; }
        public int TotalDuration { get; set; }
        public decimal TotalWeightPercent { get; set; }
        public virtual IEnumerable<ResponseEquipmentProcessDto> EquipmentProcesses { get; set; }
    }
}
