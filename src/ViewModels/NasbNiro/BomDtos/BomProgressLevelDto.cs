using System.Collections.Generic;
using ViewModels.NasbNiro.ProcessDtos;

namespace ViewModels.NasbNiro.BomDtos
{
    public class BomProgressLevelDto
    {
        public int BomId { get; set; }
        public string Title { get; set; }
        public decimal ProgressLevel { get; set; }
        public IEnumerable<ProcessProgressLevelDto> ProcessProgressLevels { get; set; }
    }
}
