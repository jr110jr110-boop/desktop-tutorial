using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Tenders;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.TenderDtos;

namespace Services.NasbNiro.Contracts
{
    public interface ITenderService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseTenderDto>> AddTenderAsync(AddTenderDto addTenderDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseTenderDto>> UpdateTenderAsync(UpdateTenderDto updateTenderDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseTenderDto>> GetResponseTenderDtoByIdAsync(int tenderId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseTenderGuarantyDto>> GetResponseTenderGuarantyDtoByIdAsync(int tenderGuarantyId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseTenderDto>> DeleteTenderAsync(int tenderId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<IEnumerable<DeleteTenderResultDto>>> DeleteTenderCollectionAsync(IEnumerable<int> tenderIds, CancellationToken cancellationToken);
        ValueTask<ResponseDto<PagedList<ResponseTenderDto>>> GetAllResponseTenderDtosAsync(TenderPaginationParams tenderPaginationParams, CancellationToken cancellationToken);
        ValueTask<ResponseDto<string>> AttachFileToTenderAsync(AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken);
        ValueTask<bool> UpdateFileToTenderAsync(UpdateDocumentDto uploadFileDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<bool>> DetachFileFromTenderAsync(int tenderId, Guid documentId, CancellationToken cancellationToken);
        ValueTask<Tender> GetTenderByIdAsync(int tenderId, CancellationToken cancellationToken);
        ValueTask<TenderGuaranty> GetTenderGuararntyByIdAsync(int tenderGuarantyId, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseTenderGuarantyDto>> AddGuarantyToTenderAsync(GuarantyTenderDtoBase guarantyTenderDtoBase, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseTenderGuarantyDto>> UpdateTenderGuarantyAsync(ResponseTenderGuarantyDto responseTenderGuarantyDto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseTenderGuarantyDto>> DeleteTenderGuarantyAsync(int tenderGuarantyId, CancellationToken cancellationToken);
    }
}
