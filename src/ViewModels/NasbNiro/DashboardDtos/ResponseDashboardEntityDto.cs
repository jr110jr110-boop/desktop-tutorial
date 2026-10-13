using System.Collections.Generic;

namespace ViewModels.NasbNiro.DashboardDtos
{
    public class ResponseDashboardEntityDto<T>
        where T : class
    {
        public IEnumerable<T> Entities { get; set; }
        public int TotalCount { get; set; }
    }
}
