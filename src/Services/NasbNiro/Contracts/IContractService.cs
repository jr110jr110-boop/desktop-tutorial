using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.ContractDtos;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Contracts
{
    public interface IContractService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseContractDto>> AddContractAsync(AddContractDto addContractDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseContractDto>> AddContractForProviderAsync(AddContractDto addContractDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseContractDto>> UpdateContractAsync(UpdateContractDto updateContractDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseContractDto>> DeleteContractAsync(int contractId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseContractDto>> UpdateContractMasterAsync(UpdateMasterDto updateMasterDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<string>> AttachProjectCharterFileAsync(AttachProjectCharterFileDto attachProjectCharterFileDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<DeleteContractResultDto>>> DeleteContractCollectionAsync(IEnumerable<int> contractIds, CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseContractDto>>> GetAllResponseContractDtosAsync(ContractPaginationParams contractPaginationParams, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseContractDto>> GetResponseContractDtoAsync(GetContractDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<string>> AttachFileToContractAsync(AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<bool>> DetachFileFromContractAsync(int contractId, Guid documentId, CancellationToken cancellationToken);
        ValueTask<ContractEntity> GetContractByIdAsync(int contractId, CancellationToken cancellationToken, bool IncludeAllChildren = false);
        ValueTask<bool> ContractExistsAsync(int contractId, CancellationToken cancellationToken);
        ValueTask<bool> ContractExistsAsync(int contractId, bool isDeleted, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseContractGuarantyDto>> AddGuarantyToContractAsync(GuarantyContractDtoBase guarantyTenderDtoBase, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseContractGuarantyDto>> UpdateContractGuarantyAsync(ResponseContractGuarantyDto responseContractGuarantyDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseContractGuarantyDto>> DeleteContractGuarantyAsync(int contractGuarantyId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseContractGuarantyDto>> GetResponseContractGuarantyDtoByIdAsync(int tenderGuarantyId, CancellationToken cancellationToken);
        ValueTask<ContractGuaranty> GetContractGuararntyByIdAsync(int tenderGuarantyId, CancellationToken cancellationToken);
    }
}
