using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Providers;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProviderDtos;

namespace Services.NasbNiro.Contracts
{
    public interface IProviderService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseProviderDto>> AddProviderAsync(AddProviderDto addProviderDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProviderDto>> UpdateProviderAsync(UpdateProviderDto updateProviderDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProviderDto>> GetResponseProviderDtoAsync(GetProviderDto getProviderDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProviderDto>> UpdateProviderContractAsync(UpdateProviderContactDto updateProviderContactDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProviderDto>> UpdateProviderAccountAsync(UpdateProviderAccountDto updateProviderAccountDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProviderDto>> AddProviderContactAsync(AddProviderContactDto addProviderContactDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProviderDto>> AddProviderAccountAsync(AddProviderAccountDto addProviderAccountDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProviderDto>> DeleteProviderContactAsync(int providerContactId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProviderDto>> DeleteProviderAccountAsync(int providerAccountId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProviderDto>> DeleteProviderAsync(int providerId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<DeleteProviderResultDto>>> DeleteProviderCollectionAsync(IEnumerable<int> providerIds, CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseProviderDto>>> GetAllResponseProviderDtosAsync(ProviderPaginationParams providerPaginationParams, CancellationToken cancellationToken);
        ValueTask<bool> ProviderExistsAsync(int providerId, CancellationToken cancellationToken, Expression<Func<Provider, bool>> where = null);
        ValueTask<ResponseDto<IEnumerable<ResponseProviderDto>>> GetVendorListAsync();
    }
}
