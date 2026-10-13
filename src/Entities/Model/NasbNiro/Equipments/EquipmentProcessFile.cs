using System;

namespace Entities.Model.NasbNiro.Equipments
{
    public class EquipmentProcessFile
    {
        public Guid DocumentId { get; set; }
        public int EquipmentProcessId { get; set; }
        public string Description { get; set; }
        public int major { get; set; }
        public int minor { get; set; }
        public int UserId { get; set; }
        public DateTime RegDate { get; set; }
        public virtual EquipmentProcess EquipmentProcess { get; set; }
    }
}
