using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.WorkOrderDtos
{
    public class UpdateWorkOrderChangeConditionDto
    {
        [Required]
        public int WorkOrderId { get; set; }

        [Required]
        public WorkOrderCondition NewCondition { get; set; }
    }
}
