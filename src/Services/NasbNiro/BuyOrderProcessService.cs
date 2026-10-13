using System;
using System.Collections.Generic;
using System.Linq;
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
using Services.NasbNiro.Extensions;
using ViewModels.NasbNiro.BuyOrderProcesses;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcessDtos;

namespace Services.NasbNiro
{
    public class BuyOrderProcessService : IBuyOrderProcessService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<BuyOrderProcessService> _logger;
        private readonly IMapper _mapper;
        private readonly IProcessService _processService;
        private readonly DbSet<BuyOrderProcesses> _buyOrderProcesses;
        private readonly DbSet<BuyOrder> _buyOrders;
        private readonly DbSet<ProcessEntity> _processes;
        private IQueryable<BuyOrderProcesses> _buyOrderProcessesQueryable =>
            _buyOrderProcesses.AsNoTracking().AsQueryable();

        public BuyOrderProcessService(IUnitOfWork unitOfWork,
                                        ILogger<BuyOrderProcessService> logger,
                                        IMapper mapper,
                                        IProcessService processService)
        {
            _unitOfWork = unitOfWork ??
                throw new ArgumentNullException(nameof(unitOfWork));


            _logger = logger.ThrowIfNull(nameof(logger));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _processService = processService.ThrowIfNull(nameof(processService));
            _buyOrderProcesses = _unitOfWork.Set<BuyOrderProcesses>().ThrowIfNull(nameof(_buyOrderProcesses));
            _buyOrders = _unitOfWork.Set<BuyOrder>().ThrowIfNull(nameof(_buyOrders));
            _processes = _unitOfWork.Set<ProcessEntity>().ThrowIfNull(nameof(_processes));
        }


        public async ValueTask<ResponseDto<ResponseBuyOrderProcessesDto>> AddBuyOrderProcessesAsync(
            AddBuyOrderProcessesDto addBuyOrderProcessesDto, CancellationToken cancellationToken)
        {
            if (addBuyOrderProcessesDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderProcessesDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await _buyOrders.SingleOrDefaultAsync(x =>
                x.Id == addBuyOrderProcessesDto.BuyOrderId && !x.Deleted,
                cancellationToken);

            if (buyOrder is null)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderProcessesDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            ProcessEntity process = await _processes.SingleOrDefaultAsync(x =>
                x.Id == addBuyOrderProcessesDto.ProcessId && !x.Deleted,
                cancellationToken);

            if (process is null)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderProcessesDto>(NasbNiroMessages.ProcessNotFound);
            }

            int processWorkOrderId = await _processes
                 .Where(x => x.Id == addBuyOrderProcessesDto.ProcessId)
                 .Select(x => x.Bom.Assembly.WorkOrderId)
                 .SingleOrDefaultAsync(cancellationToken);

            int buyOrderWorkOrderId = await _buyOrders
                .Where(x => x.Id == addBuyOrderProcessesDto.BuyOrderId)
                .Select(x => x.WorkOrderId)
                .SingleOrDefaultAsync(cancellationToken);

            if (buyOrderWorkOrderId != processWorkOrderId)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderProcessesDto>("عدم تطابق دستور کار!");
            }

            if (!process.CanAddToBuyOrder())
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderProcessesDto>(NasbNiroMessages.ProcessCanNotAddBuyOrderDueToCurrentStatus);
            }

            bool exists = await _buyOrderProcesses.AnyAsync(x =>
                x.ProcessId == addBuyOrderProcessesDto.ProcessId &&
                x.BuyOrderId == addBuyOrderProcessesDto.BuyOrderId,
                cancellationToken);

            if (exists) return ResponseDtoHelper
                    .NotFound<ResponseBuyOrderProcessesDto>(NasbNiroMessages.AlreadyExists);

            BuyOrderProcesses buyOrderProcesses = _mapper.Map<BuyOrderProcesses>(addBuyOrderProcessesDto);

            process.AddedToBuyOrder();

            _processes.Update(process);

            EntityEntry<BuyOrderProcesses> entityEntry = await _buyOrderProcesses.AddAsync(
                buyOrderProcesses, cancellationToken);

            bool isAssemblerProcess = process.Type == ProcessType.Assembly
                || process.Type == ProcessType.FinalAssembly;

            if (isAssemblerProcess && buyOrder.Status == BuyOrderStatus.NewBuyOrder)
            {
                buyOrder.NewForAssembler();
                _buyOrders.Update(buyOrder);
            }

            bool isConstructorProcess = process.Type == ProcessType.Construction;

            if (isConstructorProcess && buyOrder.Status == BuyOrderStatus.NewBuyOrder)
            {
                buyOrder.NewForConstructor();
                _buyOrders.Update(buyOrder);
            }

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseBuyOrderProcessesDtoByIdAsync(addBuyOrderProcessesDto.AsGetBuyOrderProcessesDto(), cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderProcessesDto>(NasbNiroMessages.BuyOrderProcessesNotAdded);
        }

        public async ValueTask<ResponseDto<PagedList<ResponseProcessSimpleDto>>> GetReadyProcessesForBuyOrderAsync(
            ReadyProcessesForBuyOrderPaginationaParams processesByBuyOrderIdPaginationaParams, CancellationToken cancellationToken)
        {
            IQueryable<ResponseProcessSimpleDto> processDtos = _processes
                .Where(x => x.Bom.Assembly.WorkOrderId == processesByBuyOrderIdPaginationaParams.WorkOrderId)
                .Where(x =>
                        !x.Deleted &&
                        x.EnterCommodityCode == processesByBuyOrderIdPaginationaParams.EnterCommodityCode &&
                        x.ProcessStatus == ProcessStatus.ReadyToGo)
                .ProjectTo<ResponseProcessSimpleDto>(_mapper.ConfigurationProvider);

            Task<PagedList<ResponseProcessSimpleDto>> data =
                PagedList<ResponseProcessSimpleDto>
                            .CreateAsync(processDtos,
                                         processesByBuyOrderIdPaginationaParams.PageNumber,
                                         processesByBuyOrderIdPaginationaParams.PageSize, cancellationToken);

            return ResponseDtoHelper.Ok(await data);
        }

        public async Task<List<ResponseProcessSimpleDto>> GetProcessesByBuyOrderIdAsync(
            int buyOrderId, CancellationToken cancellationToken)
        {
            List<ResponseProcessSimpleDto> list = await _buyOrderProcesses.Include(x => x.Process)
                                                                          .Where(x => x.BuyOrderId == buyOrderId)
                                                                          .Select(x => new ResponseProcessSimpleDto()
                                                                          {
                                                                              BomId = x.Process.BomId,
                                                                              Id = x.Process.Id,
                                                                              Title = x.Process.Title
                                                                          })
                                                                          .ToListAsync();

            return list;
        }

        public async ValueTask<ResponseDto<ResponseBuyOrderProcessesDto>> UpdateBuyOrderProcessesAsync(
            UpdateBuyOrderProcessesDto updateBuyOrderProcessesDto, CancellationToken cancellationToken)
        {
            if (updateBuyOrderProcessesDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderProcessesDto>(NasbNiroMessages.InvalidData);
            }

            if (!await _buyOrders.AnyAsync(x =>
                x.Id == updateBuyOrderProcessesDto.BuyOrderId && !x.Deleted,
                cancellationToken))
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderProcessesDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!await _processService.ProcessExistsAsync(updateBuyOrderProcessesDto.ProcessId, cancellationToken, x => !x.Deleted))
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderProcessesDto>(NasbNiroMessages.ProcessNotFound);
            }

            BuyOrderProcesses buyOrderProcesses = await GetBuyOrderProcessAsync(
                updateBuyOrderProcessesDto.AsGetBuyOrderProcessesDto(), cancellationToken);

            if (buyOrderProcesses is null)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderProcessesDto>(NasbNiroMessages.BuyOrderProcessesNotFound);
            }

            _mapper.Map(updateBuyOrderProcessesDto, buyOrderProcesses);

            EntityEntry<BuyOrderProcesses> entityEntry = _buyOrderProcesses.Update(buyOrderProcesses);

            if (await SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseBuyOrderProcessesDtoByIdAsync(
                    updateBuyOrderProcessesDto.AsGetBuyOrderProcessesDto(), cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderProcessesDto>(NasbNiroMessages.UpdateFailed);
        }

        public async ValueTask<ResponseDto<ResponseBuyOrderProcessesDto>> DeleteBuyOrderProcessesAsync(
            DeleteBuyOrderProcessesDto deleteBuyOrderProcessesDto, CancellationToken cancellationToken)
        {
            if (deleteBuyOrderProcessesDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderProcessesDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrderProcesses buyOrderProcesses = await GetBuyOrderProcessAsync(
                deleteBuyOrderProcessesDto.AsGetBuyOrderProcessesDto(),
                cancellationToken);

            if (buyOrderProcesses is null)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderProcessesDto>(NasbNiroMessages.BuyOrderProcessesNotFound);
            }

            EntityEntry<BuyOrderProcesses> entityEntry = _buyOrderProcesses.Remove(buyOrderProcesses);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseBuyOrderProcessesDtoByIdAsync(
                    deleteBuyOrderProcessesDto.AsGetBuyOrderProcessesDto(), cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderProcessesDto>(NasbNiroMessages.BuyOrderProcessesNotDeleted);
        }

        public async ValueTask<BuyOrderProcesses> GetBuyOrderProcessAsync(
            GetBuyOrderProcessesDto getBuyOrderProcessesDto,
            CancellationToken cancellationToken)
        {
            BuyOrderProcesses buyOrderProcesses =
                await _buyOrderProcessesQueryable.SingleOrDefaultAsync
                    (ce => ce.BuyOrderId == getBuyOrderProcessesDto.BuyOrderId &&
                        ce.ProcessId == getBuyOrderProcessesDto.ProcessId,
                        cancellationToken);

            return buyOrderProcesses;
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteBuyOrderProcessesResultDto>>> DeleteBuyOrderProcessesCollectionAsync(
            IEnumerable<DeleteBuyOrderProcessesDto> deleteBuyOrderProcessesDtos, CancellationToken cancellationToken)
        {
            if (deleteBuyOrderProcessesDtos is null)
            {
                return ResponseDtoHelper.BadRequest<IEnumerable<DeleteBuyOrderProcessesResultDto>>(NasbNiroMessages.InvalidData);
            }

            List<DeleteBuyOrderProcessesResultDto> deleteBuyOrderProcessesResultDtos =
                new List<DeleteBuyOrderProcessesResultDto>();

            foreach (DeleteBuyOrderProcessesDto deleteBuyOrderProcessesDto in deleteBuyOrderProcessesDtos.Where(dtd => dtd != null))
            {
                bool exception = false;
                ResponseDto<ResponseBuyOrderProcessesDto> result = null;

                try
                {
                    result = await this.DeleteBuyOrderProcessesAsync(deleteBuyOrderProcessesDto, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteBuyOrderProcessesResultDtos.Add(new DeleteBuyOrderProcessesResultDto()
                    {
                        Id = deleteBuyOrderProcessesDto,
                        Success = false,
                        Message = NasbNiroMessages.BuyOrderProcessesDeletingException
                    });

                    continue;
                }

                DeleteBuyOrderProcessesResultDto deleteBuyOrderProcessesResultDto
                    = new DeleteBuyOrderProcessesResultDto() { Id = deleteBuyOrderProcessesDto, Success = result.Success };

                if (!result.Success)
                {
                    deleteBuyOrderProcessesResultDto.Message = result.Message;
                }

                deleteBuyOrderProcessesResultDtos.Add(deleteBuyOrderProcessesResultDto);
            }

            return ResponseDtoHelper.Ok(deleteBuyOrderProcessesResultDtos.AsEnumerable());
        }

        public async ValueTask<ResponseDto<PagedList<ResponseBuyOrderProcessesDto>>> GetAllResponseBuyOrderProcessesDtosAsync(
                BuyOrderProcessPaginationParams buyOrderProcessPaginationParams,
                CancellationToken cancellationToken)
        {
            if (buyOrderProcessPaginationParams is null)
            {
                return ResponseDtoHelper.BadRequest<PagedList<ResponseBuyOrderProcessesDto>>(NasbNiroMessages.InvalidData);
            }

            IQueryable<BuyOrderProcesses> query = _buyOrderProcessesQueryable;

            if (!string.IsNullOrEmpty(buyOrderProcessPaginationParams.Contains))
            {
                query = query.Where(x =>
                    buyOrderProcessPaginationParams
                    .Contains.Split()
                    .Any(s => x.Attribute != null && x.Attribute.Contains(s)));
            }

            IQueryable<ResponseBuyOrderProcessesDto> resultQuery =
                query.ProjectTo<ResponseBuyOrderProcessesDto>(_mapper.ConfigurationProvider);

            Task<PagedList<ResponseBuyOrderProcessesDto>> dataTask =
                PagedList<ResponseBuyOrderProcessesDto>.CreateAsync(resultQuery,
                    buyOrderProcessPaginationParams.PageNumber,
                         buyOrderProcessPaginationParams.PageSize, cancellationToken);

            return ResponseDtoHelper.Ok(await dataTask);
        }

        public async ValueTask<ResponseDto<ResponseBuyOrderProcessesDto>> GetResponseBuyOrderProcessesDtoByIdAsync(
            GetBuyOrderProcessesDto getBuyOrderProcessesDto,
            CancellationToken cancellationToken)
        {
            ResponseBuyOrderProcessesDto responseBuyOrderProcessesDto =
                await _buyOrderProcessesQueryable
                        .Include(x => x.Process)
                        .Include(x => x.BuyOrder)
                        .ProjectTo<ResponseBuyOrderProcessesDto>(_mapper.ConfigurationProvider)
                        .SingleOrDefaultAsync(ce =>
                            ce.BuyOrderId == getBuyOrderProcessesDto.BuyOrderId &&
                            ce.ProcessId == getBuyOrderProcessesDto.ProcessId
                            , cancellationToken);

            if (responseBuyOrderProcessesDto is null)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderProcessesDto>(NasbNiroMessages.BuyOrderProcessesNotFound);
            }

            return ResponseDtoHelper.Ok(responseBuyOrderProcessesDto);
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }
    }
}
