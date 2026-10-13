using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcessDtos;

namespace Services.NasbNiro.Contracts
{
    public interface IProcessService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseProcessDto>> AddProcessAsync(AddProcessDto addProcessDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> UpdateProcessAsync(UpdateProcessDto updateProcessDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> RevertStatusAsync(RevertProcessDto revertProcessDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> ChangeUserAsync(UpdateProcessSelectUserDto updateProcessSelectUserDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> UpdateProcessFullParametersAsync(UpdateProcessFullParametersDto updateProcessFullParametersDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> DeleteProcessAsync(int processId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> PmoAcceptProcessAsync(UpdateProcessPmoAcceptDto updateProcessPmoAcceptDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> AssignedToUserAsync(UpdateProcessAssignedToUserDto updateProcessAssignedToUserDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> EngineerAcceptDocumentsAsync(UpdateProcessEngineerAcceptDocumentsDto updateProcessEngineerAcceptDocumentsDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> EngineeringAcceptDocumentsAsync(UpdateProcessEngineeringAcceptDocumentsDto updateProcessAcceptDocumentsDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> EngineeringNotAcceptDocumentsAsync(UpdateProcessEngineeringAcceptDocumentsDto updateProcessAcceptDocumentsDto, CancellationToken cancellationToken);
        Task<ResponseDto<string>> FinalizeDocumentAsync(FinalizeProcessFileDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> MasterAcceptProcessAsync(UpdateProcessMasterAcceptDto updateProcessMasterAcceptDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseProcessDto>> GetResponseProcessDtoAsync(GetProcessDto getProcessDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseProcessDto>>> GetAllResponseProcessDtoAsync(ProcessPaginationParams processPaginationParams, CancellationToken cancellationToken);
        ValueTask<ResponseDto<UpdateProcessSortResponseDto>> UpdateSortAsync(UpdateProcessSortDto updateProcessSortDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<DeleteProcessResultDto>>> DeleteProcessCollectionAsync(IEnumerable<int> processIds, CancellationToken cancellationToken);
        ValueTask<ProcessEntity> GetProcessByIdAsync(int processId, CancellationToken cancellationToken);
        bool ValidateInputDto(ProcessDtoBase processDtoBase, out string message);
        ValueTask<ResponseDto<string>> AttachFileToProcessAsync(AttachfileToProcessDto attachFileDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<bool>> DetachFileFromProcessAsync(int processId, Guid documentId, CancellationToken cancellationToken);
        ValueTask<bool> ProcessExistsAsync(int processId, CancellationToken cancellationToken, Expression<Func<ProcessEntity, bool>> where = null);
        Task<ResponseDto<IEnumerable<ResponseProcessFileStatusDto>>> GetProcessesFileStatusAsync(int bomId, CancellationToken cancellationToken);
    }
}
