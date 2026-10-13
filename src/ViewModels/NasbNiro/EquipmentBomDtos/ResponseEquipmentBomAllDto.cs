using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels.NasbNiro.EquipmentBomDtos
{
    public class ResponseEquipmentBomAllDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<EquipmentBomAllDto> Data { get; set; }
    }
}
