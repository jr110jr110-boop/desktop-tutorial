using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using ViewModels.NasbNiro.PredecessorCheckDtos;

namespace Services.NasbNiro.Contracts
{
    /// <summary>Read-only check of the products in the bank: invalid predecessors and BOM lines without processes. It changes nothing.</summary>
    public interface IEquipmentPredecessorCheckService
    {
        Task<ResponseDto<PredecessorCheckReportDto>> CheckAsync(CancellationToken cancellationToken);
    }
}
