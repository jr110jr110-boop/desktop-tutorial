using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Equipments;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.EquipmentProcessDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Contracts
{
    public interface IEquipmentProcessService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseEquipmentProcessDto>> AddEquipmentProcessAsync(
            AddEquipmentProcessDto addEquipmentProcessDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<EquipmentProcessUpdateResponseDto>> UpdateEquipmentProcessAsync(
            UpdateEquipmentProcessDto updateEquipmentProcessDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentProcessDto>> DeleteEquipmentProcessAsync(
            int equipmentProcessId, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentProcessDto>> GetResponseEquipmentProcessDtoAsync(
            GetEquipmentProcessDto getEquipmentProcessDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<PagedList<ResponseEquipmentProcessDto>>> GetAllResponseEquipmentProcessDtoAsync(
            EquipmentProcessPaginationParams equipmentProcessPaginationParams, CancellationToken cancellationToken);

        ValueTask<ResponseDto<IEnumerable<DeleteEquipmentProcessResultDto>>> DeleteEquipmentProcessCollectionAsync(
            IEnumerable<int> equipmentProcessIds, CancellationToken cancellationToken);

        ValueTask<ResponseDto<UpdateEquipmentProcessSortResponseDto>> UpdateSortAsync(
            UpdateEquipmentProcessSortDto updateEquipmentProcessSortDto, CancellationToken cancellationToken);

        ValueTask<EquipmentProcess> GetEquipmentProcessByIdAsync(
            int equipmentProcessId, CancellationToken cancellationToken);

        bool ValidateInputDto(
            EquipmentProcessDtoBase equipmentProcessDtoBase, out string message, CancellationToken cancellationToken);

        ValueTask<ResponseDto<string>> AttachFileAsync(
            AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<bool>> DetachFileAsync(
            int equipmentProcessId, Guid documentId, CancellationToken cancellationToken);

        Task<ResponseDto<string>> FinalizeDocumentAsync(
            FinalizeEquipmentProcessFileDto dto, CancellationToken cancellationToken);

        //Task<ResponseDto<ResponseEquipmentProcessFileStatusDto>> GetProcessFileStatusAsync(int processId);
        Task<ResponseDto<IEnumerable<ResponseEquipmentProcessFileStatusDto>>> GetProcessesFileStatusByEquipmentIdAsync(
            int equipmentId, CancellationToken cancellationToken);
    }
}
