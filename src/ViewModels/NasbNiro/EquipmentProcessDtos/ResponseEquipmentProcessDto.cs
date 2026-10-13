using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Equipments;
using ViewModels.Constants;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.EquipmentBomDtos;

namespace ViewModels.NasbNiro.EquipmentProcessDtos
{
    public class ResponseEquipmentProcessDto : EquipmentProcessDtoBase
    {
        public int Id { get; set; }
        public bool Deleted { get; set; }

        public ResponseEquipmentBomWithoutTypesDto EquipmentBom { get; set; }

        [Required]
        public virtual int Sort { get; set; }

        public string TypeDescription { get; protected set; }

        public IEnumerable<ResponseFileFinalizeableDto> Files { get; set; }

        public override EquipmentProcessType Type
        {
            get => base.Type;
            set
            {
                base.Type = value;
                TypeDescription = ProcessTypeDescriptions.GetProcessTypeDescription(value);
            }
        }
    }
}