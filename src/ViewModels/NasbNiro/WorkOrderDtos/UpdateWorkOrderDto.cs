using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.WorkOrderDtos
{
    public class UpdateWorkOrderDto : WorkOrderDtoBase
    {
        [Required]
        public int Id { get; set; }
    }
}
