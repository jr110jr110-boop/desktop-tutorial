using System.Threading;
using System.Threading.Tasks;

namespace Services.NasbNiro.Contracts
{
    public interface ISaveChangesAsync
    {
        ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken);
    }
}
