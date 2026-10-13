using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.AssemblyDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Contracts
{
    public interface IAssemblyService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseAssemblyDto>> AddAssemblyAsync(AddAssemblyDto addAssemblyDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> UpdateAssemblyAsync(UpdateAssemblyDto updateAssemblyDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> DeleteAssemblyAsync(int assemblyId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<DeleteAssemblyResultDto>>> DeleteAssemblyCollectionAsync(IEnumerable<int> assemblyIds, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> GetResponseAssemblyDtoAsync(GetAssemblyDto getAssemblyDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseAssemblyDto>>> GetAllResponseAssemblyDtoAsync(AssemblyPaginationParams assemblyPaginationParams, CancellationToken cancellationToken);
        ValueTask<AssemblyEntity> GetAssemblyByIdAsync(int assemblyId, CancellationToken cancellationToken, bool IncludeAllChildren = false);
        ValueTask<bool> AssemblyExistsAsync(int assemblyId, bool isDeleted, CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseAssemblyDto>>> GetReadyToAssembleAssembliesAsync(ReadyToAssembleAssembliesPaginationParams paginationParams, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> StartAssemblingAsync(UpdateAssemblyStartAssemblingDto updateDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> AssemblerAssembledAsync(UpdateAssemblyAssemblerAssembledDto updateDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> ChangeStatusAsync(UpdateAssemblyChangeStatusDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> QcAcceptToFinalAssembleAsync(UpdateAssemblyQcAcceptToFinalAssembleDto updateDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> QcRejectToAssembleAsync(UpdateAssemblyQcRejectToAssembleDto updateDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> AssemblerFinalAssembledAsync(UpdateAssemblyAssemblerFinalAssembledDto updateDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> QcRejectToFinalAssembleAsync(UpdateAssemblyQcRejectToFinalAssembleDto updateDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> QcAcceptToFinalAcceptanceAsync(UpdateAssemblyQcAcceptToFinalAcceptanceDto updateDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> PmoAcceptToCompletedAsync(UpdateAssemblyPmoAcceptToCompletedDto updateDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> SetPredecessorAsync(UpdateAssemblySetPredecessorDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseAssemblyDto>> DeletePredecessorAsync(int assemblyId, CancellationToken cancellationToken);
    }
}
