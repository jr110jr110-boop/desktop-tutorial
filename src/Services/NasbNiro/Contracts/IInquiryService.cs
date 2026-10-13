using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.InquiryDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Contracts
{
    public interface IInquiryService : ISaveChangesAsync
    {
        ValueTask<ResponseDto<ResponseInquiryDto>> AddInquiryAsync(
            AddInquiryDto addInquiryDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseInquiryDto>> UpdateInquiryAsync(
            UpdateInquiryDto updateInquiryDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseInquiryDto>> UpdateInquirySetTechnicalScoreAsync(
            UpdateInquirySetTechnicalScoreDto updateInquirySetTechnicalScoreDto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseInquiryDto>> GetResponseInquiryDtoAsync(
            GetInquiryDto getInquiryDto, CancellationToken cancellationToken, Expression<Func<Inquiry, bool>> where = null);

        ValueTask<ResponseDto<ResponseInquiryDto>> DeleteInquiryAsync(
            int inquiryId, CancellationToken cancellationToken);

        ValueTask<ResponseDto<IEnumerable<DeleteInquiryResultDto>>> DeleteInquiryCollectionAsync(
            IEnumerable<int> inquiryIds, CancellationToken cancellationToken);

        ValueTask<ResponseDto<PagedList<ResponseInquiryDto>>> GetAllResponseInquiryDtosAsync(
            InquiryPaginationParams inquiryPaginationParams, CancellationToken cancellationToken);

        ValueTask<Inquiry> GetInquiryByIdAsync(
            int inquiryId, CancellationToken cancellationToken, Expression<Func<Inquiry, bool>> where = null);
    }
}
