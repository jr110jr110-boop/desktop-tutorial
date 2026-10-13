using System.Collections.Generic;
using ViewModels.NasbNiro.ProcessDtos;

namespace ViewModels.NasbNiro.BomDtos
{
    public class ResponseBomAndChildrenDto : ResponseBomDto
    {
        public IEnumerable<ResponseProcessDto> Processes { get; set; }
    }
}
