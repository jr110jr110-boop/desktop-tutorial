using System.Collections.Generic;

namespace Entities.Model.NasbNiro.Equipments
{
    public class EquipmentAssembly : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public int AssemblerDuration { get; set; }

        public int? PredecessorId { get; protected set; }
        public EquipmentAssembly Predecessor { get; protected set; }
        public ICollection<EquipmentAssembly> Children { get; private set; }


        public virtual Equipment Equipment { get; set; }
        public int EquipmentId { get; set; }

        public virtual ICollection<EquipmentBom> EquipmentBoms { get; set; }

        public void UpdatePredecessor(EquipmentAssembly predecessorAssembly)
        {
            Predecessor = predecessorAssembly;
        }

        public void RemovePredecessor()
        {
            if (PredecessorId.HasValue)
            {
                PredecessorId = null;
            }

        }
    }
}
