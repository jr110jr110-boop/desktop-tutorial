using System;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Equipments;

namespace ViewModels.NasbNiro.EquipmentProcessDtos
{
    public abstract class EquipmentProcessDtoBase
    {
        public string ExitCommodityCode { get; set; }

        [Required]
        public string EnterCommodityCode { get; set; }
        public string DocumentCode { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public int MapDuration { get; set; }

        [Required]
        public int MasterDuration { get; set; }

        [Required]
        public int ProcessDuration { get; set; }

        public int WorkflowId { get; set; }
        public int MachineId { get; set; }
        public Guid TimeSheet { get; set; }

        [Required]
        public virtual EquipmentProcessType Type { get; set; }

        [Required]
        public decimal ProgramPrice { get; set; }

        [Required]
        public decimal WeightPercent { get; set; }

        [Required]
        public int EquipmentBomId { get; set; }

        public int TotalDuration { get => this.MapDuration + this.MasterDuration + this.ProcessDuration; }
    }
}
