using System.Collections.Generic;
using System.Threading.Tasks;
using Entities.Identity;

namespace Services.Contracts.Identity
{
    public interface ISiteStatService
    {
        Task<List<AppLogItem>> GetLastLoginsAsync(string userId);
    }
}