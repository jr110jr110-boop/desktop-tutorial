using System.Collections.Generic;
using Entities.Model.NasbNiro.ContractEquipments;

namespace Entities.Model.NasbNiro.Equipments
{
    public class Equipment : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }

        public virtual ICollection<ContractEquipment> ContractEquipments { get; set; }
        public virtual ICollection<EquipmentAssembly> EquipmentAssemblies { get; set; }
    }
}
