using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.ContractEquipments;
using ViewModels.NasbNiro.ContractEquipmentDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Contracts
{
    public interface IContractEquipmentService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseContractEquipmentDto>> AddContractEquipmentAsync(
            AddContractEquipmentDto addContractEquipmentDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseContractEquipmentDto>> UpdateContractEquipmentAsync(
            UpdateContractEquipmentDto updateContractEquipmentDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseContractEquipmentDto>> DeleteContractEquipmentAsync(
            DeleteContractEquipmentDto deleteContractEquipmentDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<IEnumerable<DeleteContractEquipmentResultDto>>> DeleteContractEquipmentCollectionAsync(
            IEnumerable<DeleteContractEquipmentDto> deleteContractEquipmentDtos, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseContractEquipmentDto>> GetResponseContractEquipmentDtoByIdAsync(
            GetContractEquipmentDto getContractEquipmentDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseContractEquipmentDto>> GetRemainingCountByIdAsync(
            GetContractEquipmentDto getContractEquipmentDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<IEnumerable<ResponseContractEquipmentDto>>> GetAllRemainingCountOfContractByIdAsync(
            int contractId, CancellationToken cancellationToken);

        ValueTask<ContractEquipment> GetContractEquipmenttAsync(
            GetContractEquipmentDto getContractEquipmentDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<PagedList<ResponseContractEquipmentDto>>> GetAllResponseContractEquipmentDtosAsync(
            ContractEquipmentPaginationParams contractEquipmentPaginationParams, CancellationToken cancellationToken);
    }
}
