using System.Collections.Generic;

namespace Entities.Model.NasbNiro.Equipments
{
    public class EquipmentProcess : BaseEntity
    {
        public string ExitCommodityCode { get; set; }
        public string EnterCommodityCode { get; set; }
        public string DocumentCode { get; set; }
        public int WorkflowId { get; set; }
        public int MachineId { get; set; }
        public int MapDuration { get; set; }
        public int MasterDuration { get; set; }
        public int ProcessDuration { get; set; }
        public EquipmentProcessType Type { get; set; }
        public int Sort { get; set; }
        public decimal ProgramPrice { get; set; }
        public decimal WeightPercent { get; set; }
        public string Title { get; set; }
        public virtual ICollection<EquipmentProcessFile> EquipmentProcessFiles { get; set; }
        public virtual EquipmentBom EquipmentBom { get; set; }
        public int EquipmentBomId { get; set; }
    }
}
