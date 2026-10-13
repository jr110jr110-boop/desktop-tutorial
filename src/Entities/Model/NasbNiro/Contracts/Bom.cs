using System.Collections.Generic;
using Entities.Model.NasbNiro.Equipments;
using Newtonsoft.Json;

namespace Entities.Model.NasbNiro.Contracts
{
    public class Bom : BaseEntity
    {
        public int PositionNumber { get; set; }
        public string Title { get; set; }
        public int Count { get; set; }
        public string Unit { get; set; }
        public string Material { get; set; }
        public string Standard { get; set; }
        public string Diagonal { get; set; }
        public string Length { get; set; }
        public string LengthUnit { get; set; }
        public string Width { get; set; }
        public string WidthUnit { get; set; }
        public string Height { get; set; }
        public string HeightUnit { get; set; }
        public decimal NotWetWeight { get; set; }
        public decimal NotWetWeightUnit { get; set; }
        public decimal WetWeight { get; set; }
        public decimal WetWeightUnit { get; set; }
        public string Description { get; set; }
        public int Sort { get; set; }

        public int? EquipmentBomId { get; set; }
        public EquipmentBom EquipmentBom { get; set; }

        public int? PredecessorId { get; set; }
        public Bom Predecessor { get; set; }

        [JsonIgnore]
        public virtual AssemblyEntity Assembly { get; set; }
        public int AssemblyId { get; set; }

        [JsonIgnore]
        public virtual ICollection<ProcessEntity> Processes { get; set; }

        public void RemovePredecessor()
        {
            PredecessorId = null;
        }

        public void UpdatePredecessor(Bom predecessor)
        {
            Predecessor = predecessor;
        }
    }
}
