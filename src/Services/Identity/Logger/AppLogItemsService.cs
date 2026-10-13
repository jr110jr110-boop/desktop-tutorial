using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Base;
using Common.GuardToolkit;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Services.Contracts.Identity;
using ViewModels;
using ViewModels.Dto;
using ViewModels.Identity;

namespace Services.Identity.Logger
{
    public class AppLogItemsService : IAppLogItemsService
    {
        private readonly DbSet<AppLogItem> _appLogItems;
        private readonly DbSet<User> _appUsesr;
        private readonly IUnitOfWork _uow;
        public AppLogItemsService(IUnitOfWork uow)
        {
            _uow = uow;
            _uow.ThrowIfNull(nameof(_uow));

            _appLogItems = _uow.Set<AppLogItem>();
            _appUsesr = _uow.Set<User>();
        }

        public Task DeleteAllAsync(string logLevel = "")
        {
            if (string.IsNullOrWhiteSpace(logLevel))
            {
                _appLogItems.RemoveRange(_appLogItems);
            }
            else
            {
                var query = _appLogItems.Where(l => l.LogLevel == logLevel);
                _appLogItems.RemoveRange(query);
            }

            return _uow.SaveChangesAsync();
        }

        public async Task DeleteAsync(int logItemId)
        {
            var itemToRemove = await _appLogItems.FirstOrDefaultAsync(x => x.Id.Equals(logItemId));
            if (itemToRemove != null)
            {
                _appLogItems.Remove(itemToRemove);
                await _uow.SaveChangesAsync();
            }
        }

        public Task DeleteOlderThanAsync(DateTimeOffset cutoffDateUtc, string logLevel = "")
        {
            if (string.IsNullOrWhiteSpace(logLevel))
            {
                var query = _appLogItems.Where(l => l.CreatedDateTime < cutoffDateUtc);
                _appLogItems.RemoveRange(query);
            }
            else
            {
                var query = _appLogItems.Where(l => l.CreatedDateTime < cutoffDateUtc && l.LogLevel == logLevel);
                _appLogItems.RemoveRange(query);
            }

            return _uow.SaveChangesAsync();
        }

        public async Task<LogListViewModel> GetAllLogItemAsync(DataTableAjaxPostModel dataTable)
        {
            return new LogListViewModel { LogCount = 0, LogsViewModel = new List<LogViewModel>() }; ;

        }

        public Task<int> GetCountAsync(string logLevel = "")
        {
            return string.IsNullOrWhiteSpace(logLevel) ?
                            _appLogItems.CountAsync() :
                            _appLogItems.Where(l => l.LogLevel == logLevel).CountAsync();
        }
        public async Task<List<AppLogItem>> GetLogItemsAsync(string loger, string userId, LogLevel logLevel)
        {
            return await _appLogItems.Where(x => x.LogLevel == LogLevel.Warning.ToString() && x.Logger == loger && Regex.Match(x.Message, @"\d+").Value == userId).OrderByDescending(x => x.Id).Take(10).ToListAsync();

        }
        public async Task<PagedAppLogItemsViewModel> GetPagedAppLogItemsAsync(
            int pageNumber,
            int pageSize,
            SortOrder sortOrder,
            string logLevel = "")
        {
            var offset = (pageSize * pageNumber) - pageSize;

            var query = string.IsNullOrWhiteSpace(logLevel) ?
                             _appLogItems :
                             _appLogItems.Where(l => l.LogLevel == logLevel);

            query = sortOrder == SortOrder.Descending ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id);

            return new PagedAppLogItemsViewModel
            {
                Paging =
                {
                    TotalItems = await query.CountAsync()
                },
                AppLogItems = await query.Skip(offset).Take(pageSize).ToListAsync()
            };
        }
        public async Task<List<LogChartViewModel>> GetChartDataAsync(DateTime? FromDate, DateTime? ToDate)
        {
            var model = await _appLogItems.Where(x => x.LogLevel == LogLevel.None.ToString()
            && x.CreatedDateTime.Value > FromDate && x.CreatedDateTime.Value <= ToDate)
                .GroupBy(x => x.Message)
                .Select(x => new LogChartViewModel
                {
                    Count = x.Count(),
                    Title = ((LogType)Enum.Parse(typeof(LogType), x.Key, true)).GetDisplayName()
                }).ToListAsync();
            return model;

        }
    }
}