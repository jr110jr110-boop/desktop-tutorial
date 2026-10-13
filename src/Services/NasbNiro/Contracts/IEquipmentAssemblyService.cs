using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Equipments;
using ViewModels.NasbNiro.EquipmentAssemblyDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Contracts
{
    public interface IEquipmentAssemblyService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseEquipmentAssemblyDto>> AddEquipmentAssemblyAsync(
            AddEquipmentAssemblyDto addEquipmentAssemblyDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentAssemblyDto>> UpdateEquipmentAssemblyAsync(
            UpdateEquipmentAssemblyDto updateEquipmentAssemblyDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentAssemblyDto>> DeleteEquipmentAssemblyAsync(
            int equipmentAssemblyId, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentAssemblyDto>> GetResponseEquipmentAssemblyDtoAsync(
            GetEquipmentAssemblyDto getEquipmentAssemblyDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<PagedList<ResponseEquipmentAssemblyDto>>> GetAllResponseEquipmentAssemblyDtoAsync(
            EquipmentAssemblyPaginationParams equipmentAssemblyPaginationParams, CancellationToken cancellationToken);

        ValueTask<ResponseDto<IEnumerable<DeleteEquipmentAssemblyResultDto>>> DeleteEquipmentAssemblyCollectionAsync(
            IEnumerable<int> equipmentAssemblyIds, CancellationToken cancellationToken);

        ValueTask<EquipmentAssembly> GetEquipmentAssemblyByIdAsync(
            int equipmentAssemblyId, CancellationToken cancellationToken, bool IncludeAllChildren = false);

        ValueTask<bool> EquipmentAssemblyExistsAsync(
            int equipmentAssemblyId, CancellationToken cancellationToken);

        ValueTask<bool> EquipmentAssemblyExistsAsync(
            int equipmentAssemblyId, bool isDeleted, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentAssemblyDto>> SetPredecessorAsync(
            UpdateEquipmentAssemblySetPredecessordDto dto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentAssemblyDto>> DeletePredecessorAsync(
            int assemblyId, CancellationToken cancellationToken);
    }
}
