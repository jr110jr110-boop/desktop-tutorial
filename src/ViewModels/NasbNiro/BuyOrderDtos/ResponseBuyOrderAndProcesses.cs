using System.Collections.Generic;
using ViewModels.NasbNiro.ProcessDtos;

namespace ViewModels.NasbNiro.BuyOrderDtos
{
    public class ResponseBuyOrderAndProcesses : ResponseBuyOrderDto
    {
        public IEnumerable<ResponseProcessDto> Processes { get; set; }
    }
}
