using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.WorkOrderDtos
{
    public class AddWorkOrderDto : WorkOrderDtoBase
    {

        [Required]
        public int EquipmentId { get; set; }
    }
}
