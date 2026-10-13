using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.ContractEquipments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.Extensions;
using ViewModels.NasbNiro.ContractDtos;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro
{
    public class ContractService : IContractService
    {
        private readonly ILogger<ContractService> _logger;
        private readonly IUnitOfWork _uow;
        private readonly IDocumentService _documentService;
        private readonly IMapper _mapper;
        private readonly DbSet<ContractEntity> _contracts;
        private readonly DbSet<ContractFile> _contractFiles;
        private readonly DbSet<ContractGuaranty> _contractGuaranties;

        private IQueryable<ContractEntity> _contractsQueryable => this._contracts.AsNoTracking().AsQueryable();
        private IQueryable<ContractGuaranty> _contractGuarantiesQueryable => _contractGuaranties.AsNoTracking().AsQueryable();

        public ContractService
            (
                ILogger<ContractService> logger,
                IUnitOfWork uow,
                IDocumentService documentService,
                IMapper mapper
            )
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _contracts = _uow.Set<ContractEntity>() ?? throw new ArgumentNullException(nameof(_contracts));
            _contractFiles = _uow.Set<ContractFile>() ?? throw new ArgumentNullException(nameof(_contractFiles));
            _contractGuaranties = _uow.Set<ContractGuaranty>() ?? throw new ArgumentNullException(nameof(_contractFiles));
        }


        public async ValueTask<ResponseDto<ResponseContractDto>> AddContractAsync(AddContractDto addContractDto, CancellationToken cancellationToken)
        {
            if (addContractDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseContractDto>(NasbNiroMessages.InvalidData);
            }

            ContractEntity contractToAdd = _mapper.Map<ContractEntity>(addContractDto);

            EntityEntry<ContractEntity> entityEntry = await _contracts.AddAsync(contractToAdd);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseContractDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseContractDto>(NasbNiroMessages.ContractNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseContractDto>> AddContractForProviderAsync(AddContractDto addContractDto, CancellationToken cancellationToken)
        {
            if (addContractDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseContractDto>(NasbNiroMessages.InvalidData);
            }

            ContractEntity contractToAdd = _mapper.Map<ContractEntity>(addContractDto);
            contractToAdd.ForProvider = true;
            EntityEntry<ContractEntity> entityEntry = await _contracts.AddAsync(contractToAdd);
            
            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseContractDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseContractDto>(NasbNiroMessages.ContractNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseContractDto>> UpdateContractAsync(UpdateContractDto updateContractDto, CancellationToken cancellationToken)
        {
            if (updateContractDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseContractDto>(NasbNiroMessages.InvalidData);
            }

            ContractEntity contract = await GetContractByIdAsync(updateContractDto.Id, cancellationToken);

            if (contract is null)
                return ResponseDtoHelper
                    .NotFound<ResponseContractDto>(NasbNiroMessages.ContractNotFound);

            _mapper.Map(updateContractDto, contract);

            EntityEntry<ContractEntity> entityEntry = _contracts.Update(contract);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseContractDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                 .BadRequest<ResponseContractDto>(NasbNiroMessages.ContractNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseContractDto>> GetResponseContractDtoAsync(GetContractDto dto, CancellationToken cancellationToken)
        {

            IQueryable<ResponseContractDto> query;

            query = _contractsQueryable.ProjectTo<ResponseContractDto>(_mapper.ConfigurationProvider);

            ResponseContractDto result = await query
                .SingleOrDefaultAsync(c => !c.Deleted && c.Id == dto.Id && c.ForProvider == dto.ForProvider, cancellationToken);

            if (result != null)
                return ResponseDtoHelper.Ok(result);

            return ResponseDtoHelper
                 .NotFound<ResponseContractDto>(NasbNiroMessages.ContractNotFound);
        }

        public async ValueTask<ResponseDto<PagedList<ResponseContractDto>>>
            GetAllResponseContractDtosAsync(ContractPaginationParams contractPaginationParams, CancellationToken cancellationToken)
        {
            if (contractPaginationParams is null)
            {
                return ResponseDtoHelper.BadRequest<PagedList<ResponseContractDto>>(NasbNiroMessages.InvalidData);
            }

            IQueryable<ContractEntity> query = _contractsQueryable.Where(c => c.ForProvider == contractPaginationParams.ForProvider);

            if (contractPaginationParams.IsDeleted.HasValue)
            {
                query = query.Where(contract => contract.Deleted == contractPaginationParams.IsDeleted);
            }

            if (contractPaginationParams.StartedFrom.HasValue)
            {
                query = query.Where(contract => contract.StartDate >= contractPaginationParams.StartedFrom);
            }

            if (contractPaginationParams.EndedAt.HasValue)
            {
                query = query.Where(contract => contract.EndDate <= contractPaginationParams.EndedAt);
            }

            if (!string.IsNullOrEmpty(contractPaginationParams.Contains))
            {
                string[] strArray = contractPaginationParams.Contains.Split(' ');
                query = query.Where(contract =>
                    contract.Code != null && strArray.Any(str => contract.Code.Contains(str)) ||
                    contract.Master != null && strArray.Any(str => contract.Master.Contains(str)));
            }

            IQueryable<ResponseContractDto> resultQuery;

            resultQuery = query.ProjectTo<ResponseContractDto>
                (_mapper.ConfigurationProvider);

            Task<PagedList<ResponseContractDto>> responseContractDtos = PagedList<ResponseContractDto>
                            .CreateAsync(resultQuery,
                                         contractPaginationParams.PageNumber,
                                         contractPaginationParams.PageSize, cancellationToken);

            return ResponseDtoHelper
                .Ok(data: await responseContractDtos);
        }

        public async ValueTask<ResponseDto<ResponseContractDto>> DeleteContractAsync(int contractId, CancellationToken cancellationToken)
        {
            ContractEntity contract = await _contracts
                .Include(x => x.ProvisionalStatements)
                .Include(x => x.WorkOrders)
                .ThenInclude(x => x.Assemblies)
                .ThenInclude(x => x.Boms)
                .ThenInclude(x => x.Processes)
                .Include(x => x.WorkOrders)
                .ThenInclude(x => x.BuyOrders)
                .SingleOrDefaultAsync(x => !x.Deleted && x.Id == contractId, cancellationToken);

            if (contract is null)
            {
                return ResponseDtoHelper.NotFound<ResponseContractDto>(NasbNiroMessages.ContractNotFound);
            }

            contract.Delete();

            if (contract.ContractEquipments != null)
                foreach (ContractEquipment contractEquipment in contract.ContractEquipments)
                {
                    _uow.Set<ContractEquipment>().Remove(contractEquipment);
                }

            if (contract.WorkOrders != null)
                foreach (WorkOrder workOrder in contract.WorkOrders)
                {
                    workOrder.Delete();

                    if (workOrder.Assemblies != null)
                        foreach (AssemblyEntity assemblyEntity in workOrder.Assemblies)
                        {
                            assemblyEntity.Delete();

                            if (assemblyEntity.Boms != null)
                                foreach (Bom bom in assemblyEntity.Boms)
                                {
                                    bom.Delete();

                                    if (bom.Processes != null)
                                        foreach (ProcessEntity process in bom.Processes)
                                        {
                                            process.Delete();
                                        }
                                }
                        }
                }

            EntityEntry<ContractEntity> entityEntry = _contracts.Update(contract);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseContractDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                 .BadRequest<ResponseContractDto>(NasbNiroMessages.ContractNotDeleted);
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteContractResultDto>>>
            DeleteContractCollectionAsync(IEnumerable<int> contractIds, CancellationToken cancellationToken)
        {
            if (contractIds is null)
                return ResponseDtoHelper
                    .BadRequest<IEnumerable<DeleteContractResultDto>>(NasbNiroMessages.InvalidData);

            List<DeleteContractResultDto> deleteContractResultDtos = new List<DeleteContractResultDto>();

            foreach (int contractId in contractIds)
            {
                bool exception = false;
                ResponseDto<ResponseContractDto> result = null;

                try
                {
                    result = await this.DeleteContractAsync(contractId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteContractResultDtos.Add(new DeleteContractResultDto()
                    {
                        Id = contractId,
                        Success = false,
                        Message = NasbNiroMessages.ContractDeletingException
                    });

                    continue;
                }

                DeleteContractResultDto deleteContractResultDto
                    = new DeleteContractResultDto() { Id = contractId, Success = result.Success };

                if (!result.Success)
                {
                    deleteContractResultDto.Message = result.Message;
                }

                deleteContractResultDtos.Add(deleteContractResultDto);
            }

            return ResponseDtoHelper
                .Ok(deleteContractResultDtos.AsEnumerable());
        }

        public async ValueTask<ResponseDto<string>> AttachFileToContractAsync(AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken)
        {
            attachFileDto.Validate();

            if (!await ContractExistsAsync(attachFileDto.EntityId, cancellationToken))
            {
                _logger.LogError($"Contract with id {attachFileDto.EntityId} not found.");

                return ResponseDtoHelper
                    .NotFound<string>(NasbNiroMessages.ContractNotFound);
            }

            ContractFile contractFile = new ContractFile();

            contractFile.ContractId = attachFileDto.EntityId;

            Guid documentId = await _documentService.UploadDocumentAsync(attachFileDto);

            contractFile.DocumentId = documentId;

            await _contractFiles.AddAsync(contractFile, cancellationToken);

            if (await this.SaveChangesAsync(cancellationToken))
                return ResponseDtoHelper
                    .Ok(documentId.ToString());

            return ResponseDtoHelper
                .BadRequest<string>(NasbNiroMessages.FileNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseContractDto>> UpdateContractMasterAsync(UpdateMasterDto updateMasterDto, CancellationToken cancellationToken)
        {
            if (updateMasterDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseContractDto>(NasbNiroMessages.InvalidData);

            ContractEntity contract = await GetContractByIdAsync(updateMasterDto.ContractId, cancellationToken);

            if (contract is null)
                return ResponseDtoHelper
                    .NotFound<ResponseContractDto>(NasbNiroMessages.ContractNotFound);

            contract.Master = updateMasterDto.Master;

            EntityEntry<ContractEntity> entityEntry = _contracts.Update(contract);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseContractDtoAsync(contract, cancellationToken);
            }

            return ResponseDtoHelper
                 .BadRequest<ResponseContractDto>(NasbNiroMessages.ContractNotUpdated);
        }

        public async ValueTask<ResponseDto<string>> AttachProjectCharterFileAsync(AttachProjectCharterFileDto attachProjectCharterFileDto, CancellationToken cancellationToken)
        {
            attachProjectCharterFileDto.Validate();

            ContractEntity contract = await GetContractByIdAsync(attachProjectCharterFileDto.ContractId, cancellationToken);

            if (contract is null || contract.Deleted == true)
            {
                _logger.LogError($"Contract with id {attachProjectCharterFileDto.ContractId} not found.");

                return ResponseDtoHelper.NotFound<string>(NasbNiroMessages.ContractNotFound);
            }

            if (contract.ProjectCharterFileId.HasValue)
            {
                try
                {
                    await _documentService.DeleteDocumentAsync(contract.ProjectCharterFileId.Value);
                }
                catch (Exception ex)
                {

                    _logger.LogError(ex.Message, ex);
                }
            }

            Guid documentId =
                await _documentService.UploadDocumentAsync(attachProjectCharterFileDto);

            contract.ProjectCharterFileId = documentId;

            _contracts.Update(contract);

            if (await this.SaveChangesAsync(cancellationToken))
                return ResponseDtoHelper.Ok(documentId.ToString());

            return ResponseDtoHelper
                .BadRequest<string>(NasbNiroMessages.FileNotAdded);
        }

        public async ValueTask<ResponseDto<bool>> DetachFileFromContractAsync(int contractId, Guid documentId, CancellationToken cancellationToken)
        {
            ContractFile result = await _contractFiles.SingleOrDefaultAsync(contractFile =>
                contractFile.DocumentId == documentId && contractFile.ContractId == contractId,
                cancellationToken);

            if (result is null)
                return ResponseDtoHelper.NotFound<bool>(NasbNiroMessages.ContractFileNotFound);

            EntityEntry<ContractFile> entityEntry = _contractFiles.Remove(result);

            if (!await this.SaveChangesAsync(cancellationToken))
                return ResponseDtoHelper.BadRequest<bool>(NasbNiroMessages.ContractFileNotDeleted);

            bool deleteDocumentResult = await _documentService.DeleteDocumentAsync(documentId);

            if (!deleteDocumentResult)
                return ResponseDtoHelper
                     .BadRequest<bool>(NasbNiroMessages.DocumentNotDeleted);

            return ResponseDtoHelper
                 .Ok<bool>(true);

        }

        public async ValueTask<ContractEntity> GetContractByIdAsync(int contractId, CancellationToken cancellationToken, bool IncludeAllChildren = false)
        {
            IQueryable<ContractEntity> query = _contracts.AsQueryable();

            if (IncludeAllChildren)
            {
                query
                    .Include(x => x.ContractEquipments)
                    .Include(x => x.ContractGuaranties)
                    .Include(x => x.WorkOrders)
                        .ThenInclude(x => x.Assemblies)
                            .ThenInclude(x => x.Boms)
                                .ThenInclude(x => x.Processes);
            }

            ContractEntity result = await query
                .SingleOrDefaultAsync(c => c.Id == contractId, cancellationToken);

            return result;
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _uow.SaveChangesAsync(cancellationToken) > 0;
        }

        public async ValueTask<bool> ContractExistsAsync(int contractId, bool isDeleted, CancellationToken cancellationToken)
        {
            bool result = await _contracts.AnyAsync(contract => contract.Id == contractId && contract.Deleted == isDeleted, cancellationToken);

            return result;
        }

        public async ValueTask<bool> ContractExistsAsync(int contractId, CancellationToken cancellationToken)
        {
            bool result = await _contracts.AnyAsync(contract => contract.Id == contractId, cancellationToken);

            return result;
        }

        public async ValueTask<ResponseDto<ResponseContractGuarantyDto>> AddGuarantyToContractAsync
            (GuarantyContractDtoBase guarantyTenderDtoBase, CancellationToken cancellationToken)
        {

            if (guarantyTenderDtoBase is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseContractGuarantyDto>(NasbNiroMessages.InvalidData);

            ContractGuaranty tenderGuaranty = _mapper.Map<ContractGuaranty>(guarantyTenderDtoBase);

            EntityEntry<ContractGuaranty> entityEntry = await _contractGuaranties.AddAsync(tenderGuaranty, cancellationToken);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseContractGuarantyDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                 .BadRequest<ResponseContractGuarantyDto>(NasbNiroMessages.TenderNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseContractGuarantyDto>> UpdateContractGuarantyAsync
            (ResponseContractGuarantyDto responseContractGuarantyDto, CancellationToken cancellationToken)
        {
            if (responseContractGuarantyDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseContractGuarantyDto>(NasbNiroMessages.InvalidData);

            ContractGuaranty tenderGuaranty = await this.GetContractGuararntyByIdAsync(responseContractGuarantyDto.Id, cancellationToken);

            if (tenderGuaranty is null)
                return ResponseDtoHelper
                    .NotFound<ResponseContractGuarantyDto>(NasbNiroMessages.TenderNotFound);

            _mapper.Map(responseContractGuarantyDto, tenderGuaranty);

            EntityEntry<ContractGuaranty> entityEntry = _contractGuaranties.Update(tenderGuaranty);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseContractGuarantyDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                            .BadRequest<ResponseContractGuarantyDto>(NasbNiroMessages.TenderNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseContractGuarantyDto>> DeleteContractGuarantyAsync
            (int contractGuarantyId, CancellationToken cancellationToken)
        {

            ContractGuaranty contractGuaranty = await this.GetContractGuararntyByIdAsync(contractGuarantyId, cancellationToken);

            if (contractGuaranty is null)
                return ResponseDtoHelper
                     .NotFound<ResponseContractGuarantyDto>(NasbNiroMessages.TenderNotFound);

            if (contractGuaranty.Deleted == true)
                return ResponseDtoHelper
                     .BadRequest<ResponseContractGuarantyDto>(NasbNiroMessages.TenderAlreadyIsDeleted);

            contractGuaranty.Delete();

            EntityEntry<ContractGuaranty> entityEntry = _contractGuaranties.Update(contractGuaranty);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseContractGuarantyDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                   .BadRequest<ResponseContractGuarantyDto>(NasbNiroMessages.TenderNotDeleted);
        }

        public async ValueTask<ResponseDto<ResponseContractGuarantyDto>> GetResponseContractGuarantyDtoByIdAsync(int contractGuarantyId, CancellationToken cancellationToken)
        {
            ResponseContractGuarantyDto result = await _contractGuarantiesQueryable
                .ProjectTo<ResponseContractGuarantyDto>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(contract => contract.Id == contractGuarantyId, cancellationToken);

            if (result != null)
                return ResponseDtoHelper
                    .Ok(result);

            return ResponseDtoHelper
                    .NotFound<ResponseContractGuarantyDto>(NasbNiroMessages.TenderNotFound);
        }

        public async ValueTask<ContractGuaranty> GetContractGuararntyByIdAsync(int tenderGuarantyId, CancellationToken cancellationToken)
        {
            ContractGuaranty contractGuaranty = await _contractGuaranties.SingleOrDefaultAsync(t => t.Id == tenderGuarantyId, cancellationToken);

            return contractGuaranty;
        }
    }
}
