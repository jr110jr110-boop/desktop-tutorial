using System.Collections.Generic;
using ViewModels.NasbNiro.BomDtos;

namespace ViewModels.NasbNiro.AssemblyDtos
{
    public class AssemblyProgressLevelDto
    {
        public int AssemblyId { get; set; }
        public string Title { get; set; }
        public decimal ProgressLevel { get; set; }
        public IEnumerable<BomProgressLevelDto> BomProgressLevels { get; set; }
    }
}
