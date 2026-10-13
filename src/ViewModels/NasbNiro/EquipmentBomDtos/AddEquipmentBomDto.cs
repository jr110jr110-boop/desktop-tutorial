using System.Collections.Generic;
using ViewModels.NasbNiro.EquipmentProcessDtos;

namespace ViewModels.NasbNiro.EquipmentBomDtos
{
    public class AddEquipmentBomDto : EquipmentBomDtoBase
    {
    }

    public class AddEquipmentBomWithEqueipmentProccessDto : EquipmentBomDtoBase
    {
        public List<ResponseEquipmentProcessDto> equipmentProcesses { get; set; }
        public AddEquipmentBomWithEqueipmentProccessDto()
        {
            equipmentProcesses = new List<ResponseEquipmentProcessDto>();
        }
    }
}
