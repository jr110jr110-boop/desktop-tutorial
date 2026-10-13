using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Entities.Identity;
using Microsoft.Extensions.Logging;
using ViewModels;
using ViewModels.Identity;

namespace Services.Contracts.Identity
{
    public interface IAppLogItemsService
    {
        Task DeleteAllAsync(string logLevel = "");
        Task DeleteAsync(int logItemId);
        Task DeleteOlderThanAsync(DateTimeOffset cutoffDateUtc, string logLevel = "");
        Task<int> GetCountAsync(string logLevel = "");
        Task<PagedAppLogItemsViewModel> GetPagedAppLogItemsAsync(int pageNumber, int pageSize, SortOrder sortOrder, string logLevel = "");
        Task<LogListViewModel> GetAllLogItemAsync(DataTableAjaxPostModel dataTable);
        Task<List<AppLogItem>> GetLogItemsAsync(string loger, string userId, LogLevel logLevel = LogLevel.Warning);
        Task<List<LogChartViewModel>> GetChartDataAsync(DateTime? FromDate, DateTime? ToDate);
    }
}