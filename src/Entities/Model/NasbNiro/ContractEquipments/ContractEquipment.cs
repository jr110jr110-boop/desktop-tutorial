using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Newtonsoft.Json;

namespace Entities.Model.NasbNiro.ContractEquipments
{
    public class ContractEquipment
    {
        public int Count { get; set; }

        public int ContractId { get; set; }
        [JsonIgnore]
        public virtual ContractEntity Contract { get; set; }

        public virtual int EquipmentId { get; set; }
        [JsonIgnore]
        public Equipment Equipment { get; set; }
    }
}
