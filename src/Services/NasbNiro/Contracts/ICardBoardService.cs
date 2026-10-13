using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using ViewModels;
using ViewModels.NasbNiro.AssemblyDtos;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.DashboardDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcessDtos;

namespace Services.NasbNiro.Contracts
{
    public interface ICardBoardService
    {
        ValueTask<ResponseDto<PagedList<ResponseBuyOrderCardBoardDto>>> GetAllUserBuyOrdersAsync(
            UserBuyOrdersPaginationParams userBuyOrdersPaginationParams, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseDashboardEntityDto<ResponseBuyOrderDto>>> GetDashboardBuyOrdersAsync(
            CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseObjects>> GetAllUserProcessesAsync(
            UserProcessesPaginationParams userProcessesPaginationParams, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseDashboardEntityDto<ResponseProcessDashboardDto>>> GetUserDashboardProcessesAsync(
            CancellationToken cancellationToken);
        Task<ResponseDto<ResponseDashboardEntityDto<ResponseAssemblyDashboardDto>>> GetUserDashboardAssembliesAsync(CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseAssemblyDto>>> GetAllUserAssembliesAsync(
            UserAssembliesPaginationParams userAssembliesPaginationParams, CancellationToken cancellationToken);
    }
}
