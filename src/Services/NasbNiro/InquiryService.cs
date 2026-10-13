using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.InquiryDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro
{
    public class InquiryService : IInquiryService
    {

        private readonly ILogger<InquiryService> _logger;
        private readonly IUnitOfWork _uow;
        private readonly IMapper _mapper;
        private readonly DbSet<Inquiry> _inquirys;
        private readonly IBuyOrderService _buyOrderService;
        private readonly IProviderService _providerService;

        private IQueryable<Inquiry> _inquirysQueryable => _inquirys.AsNoTracking().AsQueryable();

        public InquiryService(
            ILogger<InquiryService> logger,
            IUnitOfWork uow,
            IMapper mapper,
            IBuyOrderService buyOrderService,
            IProviderService providerService)
        {
            _logger = logger.ThrowIfNull(nameof(logger));
            _uow = uow.ThrowIfNull(nameof(uow));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _inquirys = _uow.Set<Inquiry>().ThrowIfNull(nameof(_inquirys));
            _buyOrderService = buyOrderService.ThrowIfNull(nameof(buyOrderService));
            _providerService = providerService.ThrowIfNull(nameof(providerService));
        }

        public async ValueTask<ResponseDto<ResponseInquiryDto>> AddInquiryAsync(AddInquiryDto addInquiryDto, CancellationToken cancellationToken)
        {
            if (addInquiryDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseInquiryDto>(NasbNiroMessages.InvalidData);
            }

            if (!await _buyOrderService.BuyOrderExistsAsync(addInquiryDto.BuyOrderId, cancellationToken, x => x.Deleted == false))
                return ResponseDtoHelper
                    .BadRequest<ResponseInquiryDto>(NasbNiroMessages.BuyOrderNotFound);

            if (!await _providerService.ProviderExistsAsync(addInquiryDto.ProviderId, cancellationToken, x => x.Deleted == false))
                return ResponseDtoHelper
                    .BadRequest<ResponseInquiryDto>(NasbNiroMessages.ProviderNotFound);

            Inquiry inquiryToAdd = _mapper.Map<Inquiry>(addInquiryDto);

            EntityEntry<Inquiry> entityEntry = await _inquirys.AddAsync(inquiryToAdd);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseInquiryDtoAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper
                 .BadRequest<ResponseInquiryDto>(NasbNiroMessages.InquiryNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseInquiryDto>> UpdateInquiryAsync(UpdateInquiryDto updateInquiryDto, CancellationToken cancellationToken)
        {
            if (updateInquiryDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseInquiryDto>(NasbNiroMessages.InvalidData);

            Inquiry inquiry = await this.GetInquiryByIdAsync(updateInquiryDto.Id, cancellationToken);

            if (inquiry is null || inquiry.Deleted)
                return ResponseDtoHelper
                    .NotFound<ResponseInquiryDto>(NasbNiroMessages.InquiryNotFound);

            _mapper.Map(updateInquiryDto, inquiry);

            EntityEntry<Inquiry> entityEntry = _inquirys.Update(inquiry);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseInquiryDtoAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseInquiryDto>(NasbNiroMessages.InquiryNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseInquiryDto>> UpdateInquirySetTechnicalScoreAsync(
            UpdateInquirySetTechnicalScoreDto updateInquirySetTechnicalScoreDto, CancellationToken cancellationToken)
        {
            if (updateInquirySetTechnicalScoreDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseInquiryDto>(NasbNiroMessages.InvalidData);
            }

            Inquiry inquiry = await this.GetInquiryByIdAsync(updateInquirySetTechnicalScoreDto.Id, cancellationToken);

            if (inquiry is null || inquiry.Deleted)
                return ResponseDtoHelper
                    .NotFound<ResponseInquiryDto>(NasbNiroMessages.InquiryNotFound);

            inquiry.TechnicalScore = updateInquirySetTechnicalScoreDto.TechnicalScore;

            EntityEntry<Inquiry> entityEntry = _inquirys.Update(inquiry);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseInquiryDtoAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseInquiryDto>(NasbNiroMessages.InquiryNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseInquiryDto>> DeleteInquiryAsync(int inquiryId, CancellationToken cancellationToken)
        {
            Inquiry inquiry = await this.GetInquiryByIdAsync(inquiryId, cancellationToken);

            if (inquiry is null)
                return ResponseDtoHelper
                     .NotFound<ResponseInquiryDto>(NasbNiroMessages.InquiryNotFound);

            if (inquiry.Deleted == true)
                return ResponseDtoHelper
                     .BadRequest<ResponseInquiryDto>(NasbNiroMessages.InquiryAlreadyIsDeleted);

            inquiry.Delete();

            EntityEntry<Inquiry> entityEntry = _inquirys.Update(inquiry);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseInquiryDtoAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper
                   .BadRequest<ResponseInquiryDto>(NasbNiroMessages.InquiryNotDeleted);
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteInquiryResultDto>>> DeleteInquiryCollectionAsync(
            IEnumerable<int> inquiryIds, CancellationToken cancellationToken)
        {
            if (inquiryIds is null)
            {
                return ResponseDtoHelper.BadRequest<IEnumerable<DeleteInquiryResultDto>>(NasbNiroMessages.InvalidData);
            }

            List<DeleteInquiryResultDto> deleteInquiryResultDtos = new List<DeleteInquiryResultDto>();

            foreach (var inquiryId in inquiryIds)
            {
                bool exception = false;
                ResponseDto<ResponseInquiryDto> result = null;

                try
                {
                    result = await this.DeleteInquiryAsync(inquiryId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteInquiryResultDtos.Add(new DeleteInquiryResultDto()
                    {
                        Id = inquiryId,
                        Success = false,
                        Message = NasbNiroMessages.InquiryDeletingException
                    });

                    continue;
                }

                DeleteInquiryResultDto deleteInquiryResultDto
                    = new DeleteInquiryResultDto() { Id = inquiryId, Success = result.Success };

                if (!result.Success)
                {
                    deleteInquiryResultDto.Message = result.Message;
                }

                deleteInquiryResultDtos.Add(deleteInquiryResultDto);
            }

            return ResponseDtoHelper.Ok(deleteInquiryResultDtos.AsEnumerable());
        }

        public async ValueTask<ResponseDto<PagedList<ResponseInquiryDto>>> GetAllResponseInquiryDtosAsync(
            InquiryPaginationParams inquiryPaginationParams, CancellationToken cancellationToken)
        {
            if (inquiryPaginationParams is null)
                return ResponseDtoHelper
                    .BadRequest<PagedList<ResponseInquiryDto>>(NasbNiroMessages.InvalidData);

            IQueryable<Inquiry> query = _inquirysQueryable;

            if (inquiryPaginationParams.IsDeleted.HasValue)
                query = query.Where(inquiry => inquiry.Deleted == inquiryPaginationParams.IsDeleted.Value);

            if (inquiryPaginationParams.ProviderId.HasValue)
                query = query.Where(inquiry => inquiry.ProviderId == inquiryPaginationParams.ProviderId.Value);

            query = query.Where(inquiry => inquiry.BuyOrderId == inquiryPaginationParams.BuyOrderId);

            IQueryable<ResponseInquiryDto> resultQuery
                = query.ProjectTo<ResponseInquiryDto>(_mapper.ConfigurationProvider);

            Task<PagedList<ResponseInquiryDto>> data = PagedList<ResponseInquiryDto>
                            .CreateAsync(resultQuery,
                                         inquiryPaginationParams.PageNumber,
                                         inquiryPaginationParams.PageSize,
                                         cancellationToken);

            return ResponseDtoHelper
                    .Ok(await data);
        }

        public async ValueTask<ResponseDto<ResponseInquiryDto>> GetResponseInquiryDtoAsync(
            GetInquiryDto getInquiryDto, CancellationToken cancellationToken, Expression<Func<Inquiry, bool>> where = null)
        {
            IQueryable<Inquiry> query = _inquirysQueryable;

            if (where != null)
            {
                query = query.Where(where);
            }

            ResponseInquiryDto result = await query
                .ProjectTo<ResponseInquiryDto>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(
                    x => x.Id == getInquiryDto.Id && x.BuyOrderId == getInquiryDto.BuyOrderId,
                    cancellationToken);

            if (result != null)
            {
                return ResponseDtoHelper.Ok(result);
            }

            return ResponseDtoHelper
                    .NotFound<ResponseInquiryDto>(NasbNiroMessages.InquiryNotFound);
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _uow.SaveChangesAsync(cancellationToken) > 0;
        }

        private async ValueTask<bool> InquiryExistsAsync(int inquiryId, CancellationToken cancellationToken, Expression<Func<Inquiry, bool>> where = null)
        {
            IQueryable<Inquiry> query = _inquirys;

            if (where != null)
            {
                query = query.Where(where);
            }


            bool result = await query.AnyAsync(t => t.Id == inquiryId, cancellationToken);

            return result;
        }

        public async ValueTask<Inquiry> GetInquiryByIdAsync(int inquiryId, CancellationToken cancellationToken, Expression<Func<Inquiry, bool>> where = null)
        {
            Inquiry inquiry = await _inquirysQueryable
                .SingleOrDefaultAsync(t => t.Id == inquiryId, cancellationToken);

            return inquiry;
        }
    }
}
