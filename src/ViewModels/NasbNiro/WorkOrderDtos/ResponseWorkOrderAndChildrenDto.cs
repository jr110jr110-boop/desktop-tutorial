using System.Collections.Generic;
using ViewModels.NasbNiro.AssemblyDtos;

namespace ViewModels.NasbNiro.WorkOrderDtos
{
    public class ResponseWorkOrderAndChildrenDto : ResponseWorkOrderDto
    {
        public IEnumerable<ResponseAssemblyAndChildrenDto> Assemblies { get; set; }
    }
}
