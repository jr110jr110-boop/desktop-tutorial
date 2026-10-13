using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.WorkOrderDtos
{
    public class GetWorkOrderDto
    {
        [Required]
        public int Id { get; set; }

        [Required]
        public int ContractId { get; set; }

        public static implicit operator GetWorkOrderDto(WorkOrder workOrder)
            => new GetWorkOrderDto() { Id = workOrder.Id, ContractId = workOrder.ContractId };
    }
}
