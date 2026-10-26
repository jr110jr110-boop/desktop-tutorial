using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Providers;
using Entities.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProviderDtos;

namespace Services.NasbNiro
{
    public class ProviderService : IProviderService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<ProviderService> _logger;
        private readonly DbSet<Provider> _providers;
        private readonly DbSet<ProviderContact> _providerContacts;
        private readonly DbSet<ProviderAccount> _providerAccounts;

        public ProviderService(IUnitOfWork unitOfWork, IMapper mapper, ILogger<ProviderService> logger)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _providers = unitOfWork.Set<Provider>() ??
                throw new ArgumentNullException(nameof(_providers));

            _providerContacts = unitOfWork.Set<ProviderContact>() ??
                throw new ArgumentNullException(nameof(_providerContacts));
            _providerAccounts = unitOfWork.Set<ProviderAccount>() ??
                throw new ArgumentNullException(nameof(_providerAccounts));
        }

        public async ValueTask<ResponseDto<ResponseProviderDto>>
            AddProviderAsync(AddProviderDto addProviderDto, CancellationToken cancellationToken)
        {
            if (addProviderDto is null)
                return ResponseDtoHelper.BadRequest<ResponseProviderDto>(NasbNiroMessages.InvalidData);

            Provider providerToAdd = _mapper.Map<Provider>(addProviderDto);

            EntityEntry<Provider> entityEntry = await _providers.AddAsync(providerToAdd, cancellationToken);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseProviderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseProviderDto>>
            AddProviderContactAsync(AddProviderContactDto addProviderContactDto, CancellationToken cancellationToken)
        {
            if (addProviderContactDto is null)
                return ResponseDtoHelper.BadRequest<ResponseProviderDto>(NasbNiroMessages.InvalidData);

            if (!await _providers.AnyAsync(x => x.Id == addProviderContactDto.ProviderId && !x.Deleted, cancellationToken))
                return ResponseDtoHelper
                    .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotFound);

            ProviderContact providerContact = _mapper.Map<ProviderContact>(addProviderContactDto);

            EntityEntry<ProviderContact> entityEntry =
                await _providerContacts.AddAsync(providerContact);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseProviderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderContactNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseProviderDto>>
            AddProviderAccountAsync(AddProviderAccountDto addProviderAccountDto, CancellationToken cancellationToken)
        {
            if (addProviderAccountDto is null)
                return ResponseDtoHelper.BadRequest<ResponseProviderDto>(NasbNiroMessages.InvalidData);

            if (!await _providers.AnyAsync(x => x.Id == addProviderAccountDto.ProviderId && !x.Deleted, cancellationToken))
                return ResponseDtoHelper
                    .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotFound);

            ProviderAccount providerAccount = _mapper.Map<ProviderAccount>(addProviderAccountDto);

            EntityEntry<ProviderAccount> entityEntry =
                await _providerAccounts.AddAsync(providerAccount);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseProviderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderContactNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseProviderDto>>
            DeleteProviderAsync(int providerId, CancellationToken cancellationToken)
        {
            Provider provider = await _providers
                .Include(x => x.ProviderContacts)
                .SingleOrDefaultAsync(x => !x.Deleted && x.Id == providerId, cancellationToken);

            if (provider is null)
                return ResponseDtoHelper
                .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotFound);

            // R1a: a provider with purchase orders, invoices, payments or stock receipts is not deleted
            string blocked = await LegacyDeleteGuard.ProviderAsync(_unitOfWork, providerId, cancellationToken);
            if (blocked != null)
                return ResponseDtoHelper.BadRequest<ResponseProviderDto>(blocked);

            provider.Delete();

            if (provider.ProviderContacts != null)
            {
                foreach (ProviderContact pc in provider.ProviderContacts)
                {
                    pc.Delete();
                }
            }

            if (provider.ProviderAccounts != null)
            {
                foreach (ProviderAccount pa in provider.ProviderAccounts)
                {
                    pa.Delete();
                }
            }

            EntityEntry<Provider> entityEntry = _providers.Update(provider);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseProviderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotUpdated);
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteProviderResultDto>>>
            DeleteProviderCollectionAsync(IEnumerable<int> providerIds, CancellationToken cancellationToken)
        {
            if (providerIds is null)
                return ResponseDtoHelper
                    .BadRequest<IEnumerable<DeleteProviderResultDto>>(NasbNiroMessages.InvalidData);

            List<DeleteProviderResultDto> deleteProviderResultDtos = new List<DeleteProviderResultDto>();

            foreach (var providerId in providerIds)
            {
                bool exception = false;
                ResponseDto<ResponseProviderDto> result = null;

                try
                {
                    result = await this.DeleteProviderAsync(providerId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteProviderResultDtos.Add(new DeleteProviderResultDto()
                    {
                        Id = providerId,
                        Success = false,
                        Message = NasbNiroMessages.ProviderDeletingException
                    });

                    continue;
                }

                DeleteProviderResultDto deleteProviderResultDto =
                    new DeleteProviderResultDto() { Id = providerId, Success = result.Success };

                if (!result.Success)
                {
                    deleteProviderResultDto.Message = result.Message;
                }

                deleteProviderResultDtos.Add(deleteProviderResultDto);
            }

            return ResponseDtoHelper.Ok(deleteProviderResultDtos.AsEnumerable());
        }

        public async ValueTask<ResponseDto<ResponseProviderDto>> DeleteProviderContactAsync(
            int providerContactId, CancellationToken cancellationToken)
        {
            ProviderContact providerContact = await _providerContacts
                .SingleOrDefaultAsync(x => !x.Deleted && x.Id == providerContactId, cancellationToken);

            if (providerContact is null)
                return ResponseDtoHelper.BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderContactNotFound);

            providerContact.Delete();

            EntityEntry<ProviderContact> entityEntry = _providerContacts.Update(providerContact);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseProviderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseProviderDto>> DeleteProviderAccountAsync(
            int providerAccountId, CancellationToken cancellationToken)
        {
            ProviderAccount providerAccount = await _providerAccounts
                .SingleOrDefaultAsync(x => !x.Deleted && x.Id == providerAccountId, cancellationToken);

            if (providerAccount is null)
                return ResponseDtoHelper.BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderContactNotFound);

            providerAccount.Delete();

            EntityEntry<ProviderAccount> entityEntry = _providerAccounts.Update(providerAccount);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseProviderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotUpdated);
        }

        public async ValueTask<ResponseDto<PagedList<ResponseProviderDto>>> GetAllResponseProviderDtosAsync(
            ProviderPaginationParams providerPaginationParams, CancellationToken cancellationToken)
        {
            if (providerPaginationParams is null)
            {
                return ResponseDtoHelper.BadRequest<PagedList<ResponseProviderDto>>(NasbNiroMessages.InvalidData);
            }

            IQueryable<Provider> query = _providers.Include(x => x.ProviderContacts).AsNoTracking();

            if (!string.IsNullOrEmpty(providerPaginationParams.Contains))
            {
                string[] strArr = providerPaginationParams.Contains.Split();

                query = query.Where(x =>
                   x.Name != null && strArr.Any(str => x.Name.Contains(str)) ||
                   x.Description != null && strArr.Any(str => x.Description.Contains(str)));
            }

            if (providerPaginationParams.CategoryId.HasValue)
            {
                query = query.Where(x => x.CategoryId == providerPaginationParams.CategoryId.Value);
            }

            if (providerPaginationParams.IsDeleted.HasValue)
            {
                bool deleted = providerPaginationParams.IsDeleted.Value;

                query = query
                    .Where(x => x.Deleted == deleted);
            }

            IQueryable<ResponseProviderDto> result;

            result = query
                .ProjectTo<ResponseProviderDto>(_mapper.ConfigurationProvider);

            PagedList<ResponseProviderDto> data = await PagedList<ResponseProviderDto>
                            .CreateAsync(result,
                                         providerPaginationParams.PageNumber,
                                         providerPaginationParams.PageSize,
                                         cancellationToken);

            if (providerPaginationParams.IsDeleted.HasValue)
            {
                bool deleted = providerPaginationParams.IsDeleted.Value;

                foreach (ResponseProviderDto item in data)
                {
                    item.ProviderContacts = item.ProviderContacts.Where(x => x.Deleted == deleted).ToArray();
                    item.ProviderAccounts = item.ProviderAccounts.Where(x => x.Deleted == deleted).ToArray();
                }
            }

            return ResponseDtoHelper.Ok(data);
        }

        public async ValueTask<ResponseDto<ResponseProviderDto>> GetResponseProviderDtoAsync(
            GetProviderDto getProviderDto,
            CancellationToken cancellationToken)
        {
            if (getProviderDto is null)
            {
                return ResponseDtoHelper
                    .NotFound<ResponseProviderDto>("ورودی نامعتبر");
            }

            IQueryable<ResponseProviderDto> query = _providers
                .ProjectTo<ResponseProviderDto>(_mapper.ConfigurationProvider);

            if (getProviderDto.HasCategory())
            {
                query = query.Where(x => x.CategoryId == getProviderDto.CategoryId.Value);
            }

            ResponseProviderDto result = await query
                .SingleOrDefaultAsync(x => !x.Deleted && x.Id == getProviderDto.Id, cancellationToken);

            result.ProviderContacts = result.ProviderContacts.Where(x => !x.Deleted).ToArray();
            result.ProviderAccounts = result.ProviderAccounts.Where(x => !x.Deleted).ToArray();

            if (result != null)
            {
                return ResponseDtoHelper.Ok(result);
            }

            return ResponseDtoHelper
                    .NotFound<ResponseProviderDto>(NasbNiroMessages.ProviderNotFound);
        }

        public async ValueTask<ResponseDto<ResponseProviderDto>> UpdateProviderAsync(
            UpdateProviderDto updateProviderDto,
            CancellationToken cancellationToken)
        {
            if (updateProviderDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProviderDto>(NasbNiroMessages.InvalidData);

            Provider provider = await _providers
                .SingleOrDefaultAsync(
                    x => !x.Deleted && x.Id == updateProviderDto.Id,
                    cancellationToken);

            if (provider is null)
                return ResponseDtoHelper
                .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotFound);

            _mapper.Map(updateProviderDto, provider);

            EntityEntry<Provider> entityEntry = _providers.Update(provider);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseProviderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseProviderDto>> UpdateProviderContractAsync(
            UpdateProviderContactDto updateProviderContactDto, CancellationToken cancellationToken)
        {
            if (updateProviderContactDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProviderDto>(NasbNiroMessages.InvalidData);

            ProviderContact providerContact = await _providerContacts
                .SingleOrDefaultAsync(x =>
                    x.Id == updateProviderContactDto.Id && x.ProviderId == updateProviderContactDto.ProviderId && !x.Deleted,
                    cancellationToken);

            if (providerContact is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotFound);

            _mapper.Map(updateProviderContactDto, providerContact);

            EntityEntry<ProviderContact> entityEntry =
                _providerContacts.Update(providerContact);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseProviderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseProviderDto>> UpdateProviderAccountAsync(
            UpdateProviderAccountDto updateProviderAccountDto, CancellationToken cancellationToken)
        {
            if (updateProviderAccountDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProviderDto>(NasbNiroMessages.InvalidData);

            ProviderAccount providerAccount = await _providerAccounts
                .SingleOrDefaultAsync(x =>
                    x.Id == updateProviderAccountDto.Id && x.ProviderId == updateProviderAccountDto.ProviderId && !x.Deleted,
                    cancellationToken);

            if (providerAccount is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotFound);

            _mapper.Map(updateProviderAccountDto, providerAccount);

            EntityEntry<ProviderAccount> entityEntry =
                _providerAccounts.Update(providerAccount);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseProviderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseProviderDto>(NasbNiroMessages.ProviderNotUpdated);
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }

        public async ValueTask<bool> ProviderExistsAsync(
            int providerId, CancellationToken cancellationToken, Expression<Func<Provider, bool>> where = null)
        {
            IQueryable<Provider> query = _providers;

            if (where != null)
            {
                query = query.Where(where);
            }

            var result = await query.AnyAsync(x => x.Id == providerId, cancellationToken);

            return result;
        }

        public async ValueTask<ResponseDto<IEnumerable<ResponseProviderDto>>> GetVendorListAsync()
        {
            IEnumerable<ResponseProviderDto> result = await _providers
                .Where(x => !x.Deleted && x.EvaluationScore >= 70.0M)
                .ProjectTo<ResponseProviderDto>(_mapper.ConfigurationProvider)
                .ToListAsync();

            return ResponseDtoHelper.Ok(result);
        }
    }
}
