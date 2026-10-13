using System.Collections.Generic;
using System.Text;
using Entities.Model.NasbNiro.Contracts;

namespace ViewModels.NasbNiro.GeneratedAssembliesDtos
{
    public class ResponseGeneratedAssembliesDto
    {
        public bool EquipmentExists { get; set; }
        public StringBuilder Errors { get; set; }
        public IEnumerable<AssemblyEntity> Assemblies { get; set; }
    }
}
