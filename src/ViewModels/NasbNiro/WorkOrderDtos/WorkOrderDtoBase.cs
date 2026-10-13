using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Newtonsoft.Json;

namespace ViewModels.NasbNiro.WorkOrderDtos
{
    public abstract class WorkOrderDtoBase
    {

        [Required]
        public int ContractId { get; set; }

        [Required]
        public string Code { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public string Description { get; set; }

        [Required]
        public int Count { get; set; }

        public decimal WeightPercent { get; set; }

        public WorkOrderCondition Condition { get; set; }
    }
}
