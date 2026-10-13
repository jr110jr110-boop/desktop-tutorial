using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Equipments;

namespace ViewModels.NasbNiro.EquipmentAssemblyDtos
{
    public class GetEquipmentAssemblyDto : GetEntityDto<int>
    {
        [Required]
        public int EquipmentId { get; set; }

        public static implicit operator GetEquipmentAssemblyDto(EquipmentAssembly assembly)
        {
            return new GetEquipmentAssemblyDto()
            {
                Id = assembly.Id,
                EquipmentId = assembly.EquipmentId,
                IncludeTypes = false
            };
        }
    }
}
