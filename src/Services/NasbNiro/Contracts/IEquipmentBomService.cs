using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Equipments;
using ViewModels.NasbNiro.EquipmentBomDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Contracts
{
    public interface IEquipmentBomService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseEquipmentBomDto>> AddEquipmentBomAsync(
            AddEquipmentBomWithEqueipmentProccessDto addEquipmentBomDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentBomDto>> UpdateEquipmentBomAsync(
            UpdateEquipmentBomDto updateEquipmentBomDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentBomDto>> DeleteEquipmentBomAsync(
            int equipmentBomId, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentBomDto>> GetResponseEquipmentBomDtoByIdAsync(
            GetEquipmentBomDto getEquipmentBomDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentBomAllDto>>
            GetAllResponseEquipmentBomDtoAsync(GetEquipmentBomAllDto getEquipmentBomAllDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<PagedList<ResponseEquipmentBomDto>>> GetAllResponseEquipmentBomDtoAsync(
            EquipmentBomPaginationParams equipmentBomPaginationParams, CancellationToken cancellationToken);

        ValueTask<ResponseDto<IEnumerable<DeleteEquipmentBomResultDto>>> DeleteEquipmentBomCollectionAsync(
            IEnumerable<int> equipmentBomIds, CancellationToken cancellationToken);

        ValueTask<EquipmentBom> GetEquipmentBomByIdAsync(
            int equipmentBomId, CancellationToken cancellationToken, bool IncludeAllChildren = false);

        ValueTask<bool> EquipmentBomExistsAsync(int equipmentBomId, CancellationToken cancellationToken);

        ValueTask<bool> EquipmentBomExistsAsync(int equipmentBomId, bool isDeleted, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentBomDto>> SetPredecessorAsync(
            UpdateEquipmentBomSetPredecessorDto dto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseEquipmentBomDto>> DeletePredecessorAsync(
            int equipmentBomId, CancellationToken cancellationToken);
    }
}
