using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.BomDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Contracts
{
    public interface IBomService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseBomDto>> AddBomAsync(AddBomDto addBomDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBomDto>> UpdateBomAsync(UpdateBomDto updateBomDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBomDto>> DeleteBomAsync(int bomId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBomDto>> GetResponseBomDtoAsync(GetBomDto getBomDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseBomDto>>> GetBomsAsync(BomPaginationParams bomPaginationParams, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<DeleteBomResultDto>>> DeleteBomCollectionAsync(IEnumerable<int> bomIds, CancellationToken cancellationToken);
        ValueTask<Bom> GetBomByIdAsync(int bomId, CancellationToken cancellationToken, bool IncludeAllChildren = false);
        ValueTask<bool> BomExistsAsync(int bomId, CancellationToken cancellationToken, bool? isDeleted = null);
        ValueTask<ResponseDto<ResponseBomDto>> SetPredecessorAsync(UpdateBomSetPredecessorDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseBomDto>> DeletePredecessorAsync(int bomId, CancellationToken cancellationToken);
    }
}
