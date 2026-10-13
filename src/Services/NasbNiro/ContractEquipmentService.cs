using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.ContractEquipments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.Extensions;
using ViewModels.NasbNiro.ContractEquipmentDtos;
using ViewModels.NasbNiro.Pagination;

using ContractEquipmentResult = System.Threading.Tasks.ValueTask
    <Common.Results.ResponseDto
        <ViewModels.NasbNiro.ContractEquipmentDtos.ResponseContractEquipmentDto>>;

using DeleteContractEquipmentsResult = System.Threading.Tasks.ValueTask
    <Common.Results.ResponseDto
        <System.Collections.Generic.IEnumerable
            <ViewModels.NasbNiro.ContractEquipmentDtos.DeleteContractEquipmentResultDto>>>;

using PaginatedContractEquipments = System.Threading.Tasks.ValueTask<Common.Results.ResponseDto
    <ViewModels.NasbNiro.Pagination.PagedList
        <ViewModels.NasbNiro.ContractEquipmentDtos.ResponseContractEquipmentDto>>>;

namespace Services.NasbNiro
{
    public class ContractEquipmentService : IContractEquipmentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEquipmentService _equipmentService;
        private readonly IContractService _contractService;
        private readonly ILogger<ContractEquipmentService> _logger;
        private readonly IMapper _mapper;
        private readonly DbSet<ContractEquipment> _contractEquipment;
        private readonly DbSet<WorkOrder> _workOrders;
        private IQueryable<ContractEquipment> _contractEquipmentQueryable =>
            _contractEquipment.AsNoTracking().AsQueryable();

        public ContractEquipmentService(IUnitOfWork unitOfWork,
                                        IEquipmentService equipmentService,
                                        IContractService contractService,
                                        ILogger<ContractEquipmentService> logger,
                                        IMapper mapper)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _equipmentService = equipmentService.ThrowIfNull(nameof(equipmentService));
            _contractService = contractService.ThrowIfNull(nameof(contractService));
            _logger = logger.ThrowIfNull(nameof(logger));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _contractEquipment = _unitOfWork.Set<ContractEquipment>().ThrowIfNull(nameof(_contractEquipment));
            _workOrders = _unitOfWork.Set<WorkOrder>().ThrowIfNull(nameof(_workOrders));
        }


        public async ContractEquipmentResult AddContractEquipmentAsync(AddContractEquipmentDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseContractEquipmentDto>(NasbNiroMessages.InvalidData);

            if (!await _equipmentService.EquipmentExistsAsync(dto.EquipmentId, false, cancellationToken))
                return ResponseDtoHelper
                    .NotFound<ResponseContractEquipmentDto>(NasbNiroMessages.EquipmentNotFound);

            if (!await _contractService.ContractExistsAsync(dto.ContractId, false, cancellationToken))
                return ResponseDtoHelper
                   .NotFound<ResponseContractEquipmentDto>(NasbNiroMessages.ContractNotFound);

            bool exitst = await _contractEquipment
                .AnyAsync(x =>
                    x.ContractId == dto.ContractId && x.EquipmentId == dto.EquipmentId,
                    cancellationToken);

            if (exitst)
                return ResponseDtoHelper
                  .NotFound<ResponseContractEquipmentDto>(NasbNiroMessages.AlreadyExists);

            ContractEquipment contractEquipment = _mapper.Map<ContractEquipment>(dto);

            EntityEntry<ContractEquipment> entityEntry = await _contractEquipment.AddAsync(contractEquipment);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseContractEquipmentDtoByIdAsync
                    (dto.AsGetContractEquipmentDto(), cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseContractEquipmentDto>(NasbNiroMessages.ContractEquipmentNotAdded);
        }

        public async ContractEquipmentResult UpdateContractEquipmentAsync(UpdateContractEquipmentDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseContractEquipmentDto>(NasbNiroMessages.InvalidData);

            if (!await _equipmentService.EquipmentExistsAsync(dto.EquipmentId, false, cancellationToken))
                return ResponseDtoHelper
                    .NotFound<ResponseContractEquipmentDto>(NasbNiroMessages.EquipmentNotFound);

            if (!await _contractService.ContractExistsAsync(dto.ContractId, false, cancellationToken))
                return ResponseDtoHelper
                    .NotFound<ResponseContractEquipmentDto>(NasbNiroMessages.ContractNotFound);

            ContractEquipment contractEquipment =
                await GetContractEquipmenttAsync
                    (dto.AsGetContractEquipmentDto(), cancellationToken);

            if (contractEquipment is null)
                return ResponseDtoHelper
                    .NotFound<ResponseContractEquipmentDto>(NasbNiroMessages.ContractEquipmentNotFound);

            _mapper.Map(dto, contractEquipment);

            EntityEntry<ContractEquipment> entityEntry =
                _contractEquipment.Update(contractEquipment);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseContractEquipmentDtoByIdAsync
                    (dto.AsGetContractEquipmentDto(), cancellationToken);
            }

            return ResponseDtoHelper
                        .BadRequest<ResponseContractEquipmentDto>(NasbNiroMessages.UpdateFailed);
        }

        public async ContractEquipmentResult DeleteContractEquipmentAsync(DeleteContractEquipmentDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
                return ResponseDtoHelper
                     .BadRequest<ResponseContractEquipmentDto>(NasbNiroMessages.InvalidData);

            ContractEquipment contractEquipment = await GetContractEquipmenttAsync(
                dto.AsGetContractEquipmentDto(), cancellationToken);

            if (contractEquipment is null)
                return ResponseDtoHelper.NotFound<ResponseContractEquipmentDto>(NasbNiroMessages.ContractEquipmentNotFound);

            EntityEntry<ContractEquipment> entityEntry =
                _contractEquipment.Remove(contractEquipment);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseContractEquipmentDtoByIdAsync(
                    dto.AsGetContractEquipmentDto(), cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseContractEquipmentDto>(NasbNiroMessages.ContractEquipmentNotDeleted);
        }

        public async ValueTask<ContractEquipment> GetContractEquipmenttAsync(GetContractEquipmentDto getContractEquipmentDto, CancellationToken cancellationToken)
        {
            ContractEquipment contractEquipment =
                await _contractEquipmentQueryable.SingleOrDefaultAsync(ce =>
                    ce.ContractId == getContractEquipmentDto.ContractId && ce.EquipmentId == getContractEquipmentDto.EquipmentId,
                    cancellationToken);

            return contractEquipment;
        }

        public async DeleteContractEquipmentsResult DeleteContractEquipmentCollectionAsync(IEnumerable<DeleteContractEquipmentDto> dtos, CancellationToken cancellationToken)
        {
            if (dtos is null)
                return ResponseDtoHelper
                    .BadRequest<IEnumerable<DeleteContractEquipmentResultDto>>(NasbNiroMessages.InvalidData);

            List<DeleteContractEquipmentResultDto> deleteContractEquipmentResultDtos =
                new List<DeleteContractEquipmentResultDto>();

            foreach (DeleteContractEquipmentDto deleteContractEquipmentDto in
                        dtos.Where(dtd => dtd != null))
            {
                bool exception = false;
                ResponseDto<ResponseContractEquipmentDto> result = null;

                try
                {
                    result = await this.DeleteContractEquipmentAsync(deleteContractEquipmentDto, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteContractEquipmentResultDtos.Add(new DeleteContractEquipmentResultDto()
                    {
                        Id = deleteContractEquipmentDto,
                        Success = false,
                        Message = NasbNiroMessages.ContractEquipmentDeletingException
                    });

                    continue;
                }

                DeleteContractEquipmentResultDto deleteContractEquipmentResultDto
                    = new DeleteContractEquipmentResultDto() { Id = deleteContractEquipmentDto, Success = result.Success };

                if (!result.Success)
                {
                    deleteContractEquipmentResultDto.Message = result.Message;
                }

                deleteContractEquipmentResultDtos.Add(deleteContractEquipmentResultDto);
            }

            return ResponseDtoHelper
                 .Ok(deleteContractEquipmentResultDtos.AsEnumerable());
        }

        public async PaginatedContractEquipments GetAllResponseContractEquipmentDtosAsync(
            ContractEquipmentPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            if (paginationParams is null)
                return ResponseDtoHelper
                    .BadRequest<PagedList<ResponseContractEquipmentDto>>(NasbNiroMessages.InvalidData);

            IQueryable<ContractEquipment> query = _contractEquipmentQueryable;

            if (paginationParams.Count.HasValue)
            {
                query = query.Where(contractEquipment =>
                    contractEquipment.Count == paginationParams.Count);
            }

            if (paginationParams.ContractId.HasValue)
            {
                query = query.Where(contractEquipment =>
                    contractEquipment.ContractId == paginationParams.ContractId);
            }

            if (paginationParams.EquipmentId.HasValue)
            {
                query = query.Where(contractEquipment =>
                    contractEquipment.EquipmentId == paginationParams.EquipmentId);
            }

            IQueryable<ResponseContractEquipmentDto> resultQuery =
                query.ProjectTo<ResponseContractEquipmentDto>(_mapper.ConfigurationProvider);

            Task<PagedList<ResponseContractEquipmentDto>> dataTask =
                PagedList<ResponseContractEquipmentDto>.CreateAsync(resultQuery,
                    paginationParams.PageNumber,
                         paginationParams.PageSize, cancellationToken);

            return ResponseDtoHelper
                 .Ok(await dataTask);
        }

        public async ContractEquipmentResult GetResponseContractEquipmentDtoByIdAsync(GetContractEquipmentDto dto, CancellationToken cancellationToken)
        {
            ResponseContractEquipmentDto responseContractEquipmentDto =
                await _contractEquipmentQueryable
                        .ProjectTo<ResponseContractEquipmentDto>(_mapper.ConfigurationProvider)
                        .SingleOrDefaultAsync(ce =>
                            ce.ContractId == dto.ContractId && ce.EquipmentId == dto.EquipmentId,
                            cancellationToken);

            if (responseContractEquipmentDto is null)
                return ResponseDtoHelper
                    .NotFound<ResponseContractEquipmentDto>(NasbNiroMessages.ContractEquipmentNotFound);

            return ResponseDtoHelper
                .Ok(responseContractEquipmentDto);
        }

        public async ContractEquipmentResult GetRemainingCountByIdAsync(GetContractEquipmentDto getContractEquipmentDto, CancellationToken cancellationToken)
        {
            var getCurrent = await GetResponseContractEquipmentDtoByIdAsync(getContractEquipmentDto, cancellationToken);

            if (!getCurrent.Success)
                return ResponseDtoHelper.BadRequest<ResponseContractEquipmentDto>(getCurrent.Message);

            getCurrent.Data.Count = getCurrent.Data.Count - await _workOrders
                     .Where(x => x.Condition != WorkOrderCondition.Canceled &&
                             !x.Deleted &&
                             x.ContractId == getCurrent.Data.ContractId &&
                             x.EquipmentId == getCurrent.Data.EquipmentId)
                     .Select(x => x.Count)
                     .SumAsync(cancellationToken);

            return ResponseDtoHelper.Ok(getCurrent.Data);
        }

        public async ValueTask<ResponseDto<IEnumerable<ResponseContractEquipmentDto>>> GetAllRemainingCountOfContractByIdAsync(
            int contractId, CancellationToken cancellationToken)
        {
            int[] equipmentIds = await _contractEquipmentQueryable
                .Where(x => x.ContractId == contractId)
                .Select(x => x.EquipmentId)
                .ToArrayAsync(cancellationToken);

            List<ResponseContractEquipmentDto> result = new List<ResponseContractEquipmentDto>();
            StringBuilder message = new StringBuilder();

            foreach (int eId in equipmentIds)
            {
                ResponseDto<ResponseContractEquipmentDto> responseContract =
                    await GetRemainingCountByIdAsync(
                        new GetContractEquipmentDto()
                        {
                            ContractId = contractId,
                            EquipmentId = eId
                        }, cancellationToken);

                if (!responseContract.Success)
                {
                    message.AppendLine(responseContract.Message);
                    continue;
                }

                result.Add(responseContract.Data);
            }

            return ResponseDtoHelper.Ok(result.AsEnumerable());
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }
    }
}
