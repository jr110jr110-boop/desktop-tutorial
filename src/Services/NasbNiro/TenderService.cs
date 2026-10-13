using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Tenders;
using Entities.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.Extensions;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.TenderDtos;

namespace Services.NasbNiro
{
    public class TenderService : ITenderService
    {
        private readonly ILogger<TenderService> _logger;
        private readonly IDocumentService _documentService;
        private readonly IUnitOfWork _uow;
        private readonly IMapper _mapper;
        private readonly DbSet<Tender> _tenders;
        private readonly DbSet<TenderFile> _tendersFiles;
        private readonly DbSet<TenderGuaranty> _tendersGuaranties;

        private IQueryable<Tender> _tendersQueryable => _tenders.AsNoTracking().AsQueryable();
        private IQueryable<TenderGuaranty> _tenderGuarantiesQueryable => _tendersGuaranties.AsNoTracking().AsQueryable();

        public TenderService(
            ILogger<TenderService> logger,
            IDocumentService documentService,
            IUnitOfWork uow,
            IMapper mapper)
        {
            _logger = logger;

            _documentService = documentService ??
                throw new ArgumentNullException(nameof(documentService));
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _tenders = _uow.Set<Tender>();
            _tendersFiles = _uow.Set<TenderFile>();
            _tendersGuaranties = _uow.Set<TenderGuaranty>();
        }

        public async ValueTask<ResponseDto<ResponseTenderDto>> AddTenderAsync(AddTenderDto addTenderDto, CancellationToken cancellationToken)
        {
            if (addTenderDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseTenderDto>(NasbNiroMessages.InvalidData);

            Tender tenderToAdd = _mapper.Map<Tender>(addTenderDto);

            EntityEntry<Tender> entityEntry = await _tenders.AddAsync(tenderToAdd, cancellationToken);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseTenderDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                 .BadRequest<ResponseTenderDto>(NasbNiroMessages.TenderNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseTenderDto>> UpdateTenderAsync(UpdateTenderDto updateTenderDto, CancellationToken cancellationToken)
        {
            if (updateTenderDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseTenderDto>(NasbNiroMessages.InvalidData);

            Tender tender = await this.GetTenderByIdAsync(updateTenderDto.Id, cancellationToken);

            if (tender is null)
                return ResponseDtoHelper
                    .NotFound<ResponseTenderDto>(NasbNiroMessages.TenderNotFound);

            _mapper.Map(updateTenderDto, tender);

            EntityEntry<Tender> entityEntry = _tenders.Update(tender);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseTenderDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                            .BadRequest<ResponseTenderDto>(NasbNiroMessages.TenderNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseTenderDto>> DeleteTenderAsync(int tenderId, CancellationToken cancellationToken)
        {
            Tender tender = await this.GetTenderByIdAsync(tenderId, cancellationToken);

            if (tender is null)
                return ResponseDtoHelper
                     .NotFound<ResponseTenderDto>(NasbNiroMessages.TenderNotFound);

            if (tender.Deleted == true)
                return ResponseDtoHelper
                     .BadRequest<ResponseTenderDto>(NasbNiroMessages.TenderAlreadyIsDeleted);

            tender.Delete();

            EntityEntry<Tender> entityEntry = _tenders.Update(tender);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseTenderDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                   .BadRequest<ResponseTenderDto>(NasbNiroMessages.TenderNotDeleted);
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteTenderResultDto>>>
            DeleteTenderCollectionAsync(IEnumerable<int> tenderIds, CancellationToken cancellationToken)
        {
            if (tenderIds is null)
                return ResponseDtoHelper
                    .BadRequest<IEnumerable<DeleteTenderResultDto>>(NasbNiroMessages.InvalidData);

            List<DeleteTenderResultDto> deleteTenderResultDtos = new List<DeleteTenderResultDto>();

            foreach (var tenderId in tenderIds)
            {
                bool exception = false;
                ResponseDto<ResponseTenderDto> result = null;

                try
                {
                    result = await this.DeleteTenderAsync(tenderId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteTenderResultDtos.Add(new DeleteTenderResultDto()
                    {
                        Id = tenderId,
                        Success = false,
                        Message = NasbNiroMessages.TenderDeletingException
                    });

                    continue;
                }

                DeleteTenderResultDto deleteTenderResultDto
                    = new DeleteTenderResultDto() { Id = tenderId, Success = result.Success };

                if (!result.Success)
                {
                    deleteTenderResultDto.Message = result.Message;
                }

                deleteTenderResultDtos.Add(deleteTenderResultDto);
            }

            return ResponseDtoHelper
                  .Ok(deleteTenderResultDtos.AsEnumerable());
        }

        public async ValueTask<ResponseDto<string>> AttachFileToTenderAsync
            (AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken)
        {
            attachFileDto.Validate();

            if (!await TenderExistsAsync(attachFileDto.EntityId, cancellationToken))
            {
                _logger.LogError($"Tender with id {attachFileDto.EntityId} not found.");

                return ResponseDtoHelper
                       .NotFound<string>(NasbNiroMessages.TenderNotFound);
            }

            TenderFile tenderFile = new TenderFile();

            tenderFile.TenderId = attachFileDto.EntityId;

            Guid documentId = await _documentService.UploadDocumentAsync(uploadFileDto: attachFileDto);

            tenderFile.DocumentId = documentId;

            await _tendersFiles.AddAsync(tenderFile);
            if (await this.SaveChangesAsync(cancellationToken))
                return ResponseDtoHelper.Ok(documentId.ToString());

            return ResponseDtoHelper.BadRequest<string>(NasbNiroMessages.FileNotAdded);
        }

        public async ValueTask<bool> UpdateFileToTenderAsync
            (UpdateDocumentDto uploadFileDto, CancellationToken cancellationToken)
        {
            return await _documentService.UpdateDocumentAsync(uploadFileDto);
        }

        public async ValueTask<ResponseDto<bool>> DetachFileFromTenderAsync
            (int tenderId, Guid documentId, CancellationToken cancellationToken)
        {
            TenderFile result = await _tendersFiles.SingleOrDefaultAsync(
                tenderFile => tenderFile.DocumentId == documentId && tenderFile.TenderId == tenderId,
                cancellationToken);

            if (result is null)
                return ResponseDtoHelper
                         .NotFound<bool>(NasbNiroMessages.TenderFileNotFound);

            EntityEntry<TenderFile> entityEntry = _tendersFiles.Remove(result);

            if (!await this.SaveChangesAsync(cancellationToken))
                return ResponseDtoHelper
                     .BadRequest<bool>(NasbNiroMessages.TenderFileNotDeleted);

            bool deleteDocumentResult = await _documentService.DeleteDocumentAsync(documentId);

            if (!deleteDocumentResult)
                return ResponseDtoHelper
                         .BadRequest<bool>(NasbNiroMessages.DocumentNotDeleted);

            return ResponseDtoHelper
                         .Ok(true, message: NasbNiroMessages.TenderFileDeleted);
        }

        public async ValueTask<ResponseDto<PagedList<ResponseTenderDto>>>
            GetAllResponseTenderDtosAsync(TenderPaginationParams tenderPaginationParams, CancellationToken cancellationToken)
        {
            if (tenderPaginationParams is null)
                return ResponseDtoHelper.BadRequest<PagedList<ResponseTenderDto>>(NasbNiroMessages.InvalidData);

            IQueryable<Tender> query = _tendersQueryable;

            if (tenderPaginationParams.IsDeleted.HasValue)
                query = query.Where(tender => tender.Deleted == tenderPaginationParams.IsDeleted);

            if (!string.IsNullOrEmpty(tenderPaginationParams.Contains))
            {
                string[] strArray = tenderPaginationParams.Contains.Split(' ');

                query = query.Where(tender =>
                    tender.Code != null && strArray.Any(str => tender.Code.Contains(str)) ||
                    tender.Company != null && strArray.Any(str => tender.Company.Contains(str)) ||
                    tender.Result != null && strArray.Any(str => tender.Result.Contains(str)));
            }

            if (tenderPaginationParams.FromDate.HasValue)
            {
                query = query.Where(tender => tender.TenderDate >= tenderPaginationParams.FromDate);
            }

            if (tenderPaginationParams.ToDate.HasValue)
            {
                query = query.Where(tender => tender.TenderDate <= tenderPaginationParams.ToDate);
            }

            IQueryable<ResponseTenderDto> resultQuery
                = query.ProjectTo<ResponseTenderDto>(_mapper.ConfigurationProvider);

            Task<PagedList<ResponseTenderDto>> data = PagedList<ResponseTenderDto>
                            .CreateAsync(resultQuery,
                                         tenderPaginationParams.PageNumber,
                                         tenderPaginationParams.PageSize,
                                         cancellationToken);

            return ResponseDtoHelper.Ok(await data);
        }

        public async ValueTask<ResponseDto<ResponseTenderDto>> GetResponseTenderDtoByIdAsync(int tenderId, CancellationToken cancellationToken)
        {
            ResponseTenderDto result = await _tendersQueryable
                .Include(tender => tender.TenderFiles)
                .ProjectTo<ResponseTenderDto>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(tender => tender.Id == tenderId, cancellationToken);

            if (result != null)
                return ResponseDtoHelper
                    .Ok(result);

            return ResponseDtoHelper
                    .NotFound<ResponseTenderDto>(NasbNiroMessages.TenderNotFound);
        }

        public async ValueTask<ResponseDto<ResponseTenderGuarantyDto>> GetResponseTenderGuarantyDtoByIdAsync(int tenderGuarantyId, CancellationToken cancellationToken)
        {
            ResponseTenderGuarantyDto result = await _tenderGuarantiesQueryable
                .ProjectTo<ResponseTenderGuarantyDto>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(tender => tender.Id == tenderGuarantyId, cancellationToken);

            if (result != null)
                return ResponseDtoHelper
                    .Ok(result);

            return ResponseDtoHelper
                    .NotFound<ResponseTenderGuarantyDto>(NasbNiroMessages.TenderNotFound);
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _uow.SaveChangesAsync(cancellationToken) > 0;
        }

        private async ValueTask<bool> TenderExistsAsync(int tenderId, CancellationToken cancellationToken)
        {
            bool result = await _tenders.AnyAsync(t => t.Id == tenderId);

            return result;
        }

        public async ValueTask<Tender> GetTenderByIdAsync(int tenderId, CancellationToken cancellationToken)
        {
            Tender tender = await _tendersQueryable.SingleOrDefaultAsync(t => t.Id == tenderId, cancellationToken);

            return tender;
        }

        public async ValueTask<TenderGuaranty> GetTenderGuararntyByIdAsync(int tenderGuarantyId, CancellationToken cancellationToken)
        {
            TenderGuaranty tenderGuaranty = await _tendersGuaranties.SingleOrDefaultAsync(t => t.Id == tenderGuarantyId, cancellationToken);

            return tenderGuaranty;
        }

        public async ValueTask<ResponseDto<ResponseTenderGuarantyDto>> AddGuarantyToTenderAsync
            (GuarantyTenderDtoBase guarantyTenderDtoBase, CancellationToken cancellationToken)
        {

            if (guarantyTenderDtoBase is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseTenderGuarantyDto>(NasbNiroMessages.InvalidData);

            TenderGuaranty tenderGuaranty = _mapper.Map<TenderGuaranty>(guarantyTenderDtoBase);

            EntityEntry<TenderGuaranty> entityEntry = await _tendersGuaranties.AddAsync(tenderGuaranty, cancellationToken);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseTenderGuarantyDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                 .BadRequest<ResponseTenderGuarantyDto>(NasbNiroMessages.TenderNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseTenderGuarantyDto>> UpdateTenderGuarantyAsync
            (ResponseTenderGuarantyDto responseTenderGuarantyDto, CancellationToken cancellationToken)
        {
            if (responseTenderGuarantyDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseTenderGuarantyDto>(NasbNiroMessages.InvalidData);

            TenderGuaranty tenderGuaranty = await this.GetTenderGuararntyByIdAsync(responseTenderGuarantyDto.Id, cancellationToken);

            if (tenderGuaranty is null)
                return ResponseDtoHelper
                    .NotFound<ResponseTenderGuarantyDto>(NasbNiroMessages.TenderNotFound);

            _mapper.Map(responseTenderGuarantyDto, tenderGuaranty);

            EntityEntry<TenderGuaranty> entityEntry = _tendersGuaranties.Update(tenderGuaranty);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseTenderGuarantyDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                            .BadRequest<ResponseTenderGuarantyDto>(NasbNiroMessages.TenderNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseTenderGuarantyDto>> DeleteTenderGuarantyAsync
            (int tenderGuarantyId, CancellationToken cancellationToken)
        {

            TenderGuaranty tenderGuaranty = await this.GetTenderGuararntyByIdAsync(tenderGuarantyId, cancellationToken);

            if (tenderGuaranty is null)
                return ResponseDtoHelper
                     .NotFound<ResponseTenderGuarantyDto>(NasbNiroMessages.TenderNotFound);

            if (tenderGuaranty.Deleted == true)
                return ResponseDtoHelper
                     .BadRequest<ResponseTenderGuarantyDto>(NasbNiroMessages.TenderAlreadyIsDeleted);

            tenderGuaranty.Delete();

            EntityEntry<TenderGuaranty> entityEntry = _tendersGuaranties.Update(tenderGuaranty);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseTenderGuarantyDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                   .BadRequest<ResponseTenderGuarantyDto>(NasbNiroMessages.TenderNotDeleted);
        }
    }
}
