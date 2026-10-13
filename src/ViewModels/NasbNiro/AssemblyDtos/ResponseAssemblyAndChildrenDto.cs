using System.Collections.Generic;
using ViewModels.NasbNiro.BomDtos;

namespace ViewModels.NasbNiro.AssemblyDtos
{
    public class ResponseAssemblyAndChildrenDto : ResponseAssemblyDto
    {
        public IEnumerable<ResponseBomAndChildrenDto> Boms { get; set; }
    }
}
