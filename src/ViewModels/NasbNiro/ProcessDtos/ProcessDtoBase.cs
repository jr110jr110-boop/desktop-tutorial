using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Newtonsoft.Json;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public abstract class ProcessDtoBase
    {
        public string ExitCommodityCode { get; set; }

        [Required]
        public string EnterCommodityCode { get; set; }
        public string DocumentCode { get; set; }

        public int WorkflowId { get; set; }
        public int MachineId { get; set; }

        [Required]
        public int MapDuration { get; set; }

        [Required]
        public int MasterDuration { get; set; }

        [Required]
        public int ProcessDuration { get; set; }

        [Required]
        public decimal WeightPercent { get; set; }

        [Required]
        public virtual ProcessType Type { get; set; }

        [Required]
        public int Sort { get; set; }

        [Required]
        public decimal ProgramPrice { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public int BomId { get; set; }

        public int TotalDuration
        {
            get => this.MapDuration + this.MasterDuration + this.ProcessDuration;
        }
    }
}
