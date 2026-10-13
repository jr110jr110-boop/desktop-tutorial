using System.Collections.Generic;
using System.Threading.Tasks;
using Common.GuardToolkit;
using DataLayer.Context;
using Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Services.Contracts.Identity;


namespace Services.Identity
{
    public class SiteStatService : ISiteStatService
    {
        private readonly IUnitOfWork _uow;
        private readonly IApplicationUserManager _userManager;
        private readonly IAppLogItemsService _appLogItemsService;
        private readonly DbSet<User> _users;

        public SiteStatService(
            IApplicationUserManager userManager,
            IUnitOfWork uow,
            IAppLogItemsService appLogItemsService)
        {
            _userManager = userManager;
            _userManager.ThrowIfNull(nameof(_userManager));

            _uow = uow;
            _uow.ThrowIfNull(nameof(_uow));

            _appLogItemsService = appLogItemsService;
            _appLogItemsService.ThrowIfNull(nameof(_appLogItemsService));

            _users = uow.Set<User>();
        }


        public async Task<List<AppLogItem>> GetLastLoginsAsync(string userId)
        {
            return await _appLogItemsService.GetLogItemsAsync("Services.Identity.ApplicationUserManager", userId, LogLevel.Warning);

        }

    }
}