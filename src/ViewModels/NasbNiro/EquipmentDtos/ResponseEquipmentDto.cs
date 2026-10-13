using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ViewModels.NasbNiro.EquipmentAssemblyDtos;

namespace ViewModels.NasbNiro.EquipmentDtos
{
    public class ResponseEquipmentDto : EquipmentBaseDto
    {
        [Display(Name = "شناسه تجهیزات")]
        public int Id { get; set; }

        [Display(Name = "حذف شده")]
        public bool Deleted { get; set; }

        [Display(Name = "قطعات تجهیزات")]
        public IEnumerable<ResponseEquipmentAssemblyDto> EquipmentAssemblies { get; set; }
    }
}
