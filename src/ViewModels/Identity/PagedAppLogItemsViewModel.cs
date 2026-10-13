using System.Collections.Generic;
using Entities.Identity;
using ViewModels.Dto;

namespace ViewModels.Identity
{
    public class PagedAppLogItemsViewModel
    {
        public PagedAppLogItemsViewModel()
        {
            Paging = new PaginationSettings();
        }

        public string LogLevel { get; set; } = string.Empty;

        public List<AppLogItem> AppLogItems { get; set; }

        public PaginationSettings Paging { get; set; }
    }

    public class LogChartViewModel
    {
        public string Title { get; set; }

        public int Count { get; set; }
    }

    public class LogListViewModel
    {
        public List<LogViewModel> LogsViewModel { get; set; }

        public int LogCount { get; set; }
    }
}