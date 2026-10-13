using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.BuyOrderProcesses;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcessDtos;

namespace Services.NasbNiro.Contracts
{
    public interface IBuyOrderProcessService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseBuyOrderProcessesDto>> AddBuyOrderProcessesAsync(
            AddBuyOrderProcessesDto addBuyOrderProcessesDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseBuyOrderProcessesDto>> UpdateBuyOrderProcessesAsync(
            UpdateBuyOrderProcessesDto updateBuyOrderProcessesDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<PagedList<ResponseProcessSimpleDto>>> GetReadyProcessesForBuyOrderAsync(
            ReadyProcessesForBuyOrderPaginationaParams processesByBuyOrderIdPaginationaParams, CancellationToken cancellationToken);
        Task<List<ResponseProcessSimpleDto>> GetProcessesByBuyOrderIdAsync(
            int buyOrderId, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseBuyOrderProcessesDto>> DeleteBuyOrderProcessesAsync(
            DeleteBuyOrderProcessesDto deleteBuyOrderProcessesDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<IEnumerable<DeleteBuyOrderProcessesResultDto>>> DeleteBuyOrderProcessesCollectionAsync(
            IEnumerable<DeleteBuyOrderProcessesDto> deleteBuyOrderProcessesDtos, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseBuyOrderProcessesDto>> GetResponseBuyOrderProcessesDtoByIdAsync(
            GetBuyOrderProcessesDto getBuyOrderProcessesDto, CancellationToken cancellationToken);

        ValueTask<BuyOrderProcesses> GetBuyOrderProcessAsync(
            GetBuyOrderProcessesDto getBuyOrderProcessesDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<PagedList<ResponseBuyOrderProcessesDto>>> GetAllResponseBuyOrderProcessesDtosAsync(
            BuyOrderProcessPaginationParams buyOrderProcessPaginationParams1, CancellationToken cancellationToken);
    }
}