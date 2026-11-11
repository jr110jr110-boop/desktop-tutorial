using System.Collections.Generic;
using System.Text;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.GeneratedAssembliesDtos
{
    public class ResponseGeneratedAssembliesDto
    {
        public bool EquipmentExists { get; set; }
        public StringBuilder Errors { get; set; }

        /// <summary>Persian refusal text when the BOM or assembly predecessors of the product are invalid (no assemblies are produced then).</summary>
        public string Problem { get; set; }
        public IEnumerable<AssemblyEntity> Assemblies { get; set; }
    }
}
