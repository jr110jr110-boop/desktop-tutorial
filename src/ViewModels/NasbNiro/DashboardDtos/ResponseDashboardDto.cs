using System.Collections.Generic;
using ViewModels.NasbNiro.AssemblyDtos;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.ProcessDtos;

namespace ViewModels.NasbNiro
{
    public class ResponseDashboardTitleValue
    {
        public string Type { get; set; }
        public string Title { get; set; }
        public dynamic Value { get; set; }
    }

    public class ResponseDashboardDto
    {
        public IEnumerable<ResponseDashboardTitleValue> TitleValues { get; set; }
        public IEnumerable<ResponseProcessDashboardDto> UserProcesses { get; set; }
        public IEnumerable<ResponseAssemblyDashboardDto> UserAssemblies { get; set; }
        public IEnumerable<ResponseBuyOrderDto> UserBuyOrders { get; set; }
    }
}
