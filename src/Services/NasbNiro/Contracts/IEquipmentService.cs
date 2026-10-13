using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Equipments;
using ViewModels.NasbNiro.EquipmentDtos;
using ViewModels.NasbNiro.GeneratedAssembliesDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Contracts
{
    public interface IEquipmentService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseEquipmentDto>> AddEquipmentAsync(
            AddEquipmentDto addEquipmentDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentDto>> UpdateEquipmentAsync(
            UpdateEquipmentDto updateEquipmentDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentDto>> DeleteEquipmentAsync(
            int equipmentId, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentDto>> GetResponseEquipmentDtoByIdAsync(
            int equipmentId, CancellationToken cancellationToken);

        ValueTask<ResponseGeneratedAssembliesDto> GenerateAssembliesFromEquipmentAsync(
            int equipmentId, CancellationToken cancellationToken);

        ValueTask<ResponseDto<IEnumerable<DeleteEquipmentResultDto>>> DeleteEquipmentCollectionAsync(
            IEnumerable<int> equipmentIds, CancellationToken cancellationToken);

        ValueTask<ResponseDto<PagedList<ResponseEquipmentDto>>> GetAllResponseEquipmentDtoAsync(
            EquipmentPaginationParams equipmentPaginationParams, CancellationToken cancellationToken);

        ValueTask<Equipment> GetEquipmentByIdAsync(
            int equipmentId, CancellationToken cancellationToken, bool IncludeAllChildren = false);

        ValueTask<bool> EquipmentExistsAsync(int equipmentId, CancellationToken cancellationToken);

        ValueTask<bool> EquipmentExistsAsync(int equipmentId, bool isDeleted, CancellationToken cancellationToken);
    }
}
