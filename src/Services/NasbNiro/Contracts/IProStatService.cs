using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProvisionalStatementDtos;

namespace Services.NasbNiro.Contracts
{
    public interface IProStatService:ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseProStatDto>> AddProStatAsync(AddProStatDto addProStatDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProStatDto>> UpdateProStatAsync(UpdateProStatDto updateProStatDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProStatDto>> GetProStatAsync(GetProStatDto getProStatDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<string>> DeleteProStatAsync(DeleteProStatDto deleteProStatDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseProStatDto>>> GetProStatsAsync(ProStatPaginationParams @params, CancellationToken cancellationToken);
    }
}
