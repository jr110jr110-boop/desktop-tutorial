using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Equipments;

namespace ViewModels.NasbNiro.EquipmentBomDtos
{
    public class GetEquipmentBomDto : GetEntityDto<int>
    {
        [Required]
        public int EquipmentAssemblyId { get; set; }

        public static implicit operator GetEquipmentBomDto(EquipmentBom entity)
            => new GetEquipmentBomDto()
            {
                Id = entity.Id,
                EquipmentAssemblyId = entity.EquipmentAssemblyId,
                IncludeTypes = false
            };
    }
}
