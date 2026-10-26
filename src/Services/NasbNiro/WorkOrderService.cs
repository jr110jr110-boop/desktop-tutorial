using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.Extentions;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Contracts;
using Entities.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.DateTimeService;
using Services.NasbNiro.WorkOrders;
using Services.Resources;
using ViewModels.NasbNiro.AssemblyDtos;
using ViewModels.NasbNiro.BomDtos;
using ViewModels.NasbNiro.ContractEquipmentDtos;
using ViewModels.NasbNiro.GeneratedAssembliesDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcessDtos;
using ViewModels.NasbNiro.WorkOrderDtos;
// item1 is bomId || item2 is assemblyId \\
using BomAndAssemblyId = System.ValueTuple<int, int>;
using BomEndDate = System.DateTimeOffset;
using WorkOrderAndChildren = System.Threading.Tasks.ValueTask
    <Common.Results.ResponseDto
        <ViewModels.NasbNiro.WorkOrderDtos.ResponseWorkOrderAndChildrenDto>>;
using WorkOrderResult = System.Threading.Tasks.ValueTask
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.WorkOrderDtos.ResponseWorkOrderDto>>;

namespace Services.NasbNiro
{
    public class WorkOrderService : IWorkOrderService
    {
        #region Fields
        private readonly IUnitOfWork _unitOfWork;
        private readonly IContractService _contractService;
        private readonly IMapper _mapper;
        private readonly ILogger<WorkOrderService> _logger;
        private readonly IEquipmentService _equipmentService;
        private readonly DbSet<WorkOrder> _workOrders;
        private readonly DbSet<WorkOrderHistory> _workOrderHistories;
        private readonly DbSet<HolidaysDayOfWeek> _holidays;
        private readonly DbSet<HolidaysByDate> _holidates;
        private readonly DbSet<AssemblyEntity> _assemblies;
        private readonly DbSet<Bom> _boms;
        private readonly DbSet<ProcessEntity> _processes;
        private readonly IContractEquipmentService _contractEquipmentService;
        private readonly IDateTimeService _dateTimeService;
        #endregion

        private IQueryable<WorkOrder> _workOrdersQueryable
            => _workOrders.AsNoTracking().AsQueryable();

        #region ctor
        public WorkOrderService(
            IUnitOfWork unitOfWork,
            IContractService contractService,
            IMapper mapper,
            IContractEquipmentService contractEquipmentService,
            ILogger<WorkOrderService> logger,
            IEquipmentService equipmentService,
            IDateTimeService dateTimeService)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _contractService = contractService.ThrowIfNull(nameof(contractService));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _logger = logger.ThrowIfNull(nameof(logger));
            _equipmentService = equipmentService.ThrowIfNull(nameof(equipmentService));
            _workOrders = _unitOfWork.Set<WorkOrder>().ThrowIfNull(nameof(_workOrders));
            _workOrderHistories = _unitOfWork.Set<WorkOrderHistory>().ThrowIfNull(nameof(_workOrderHistories));
            _holidays = _unitOfWork.Set<HolidaysDayOfWeek>().ThrowIfNull(nameof(_holidays));
            _holidates = _unitOfWork.Set<HolidaysByDate>().ThrowIfNull(nameof(_holidates));
            _assemblies = _unitOfWork.Set<AssemblyEntity>().ThrowIfNull(nameof(_assemblies));
            _boms = _unitOfWork.Set<Bom>().ThrowIfNull(nameof(_boms));
            _processes = _unitOfWork.Set<ProcessEntity>().ThrowIfNull(nameof(_processes));
            _contractEquipmentService = contractEquipmentService.ThrowIfNull(nameof(contractEquipmentService));
            _dateTimeService = dateTimeService.ThrowIfNull(nameof(dateTimeService));
        }
        #endregion

        public async WorkOrderResult AddWorkOrderAsync(AddWorkOrderDto addWorkOrderDto, CancellationToken cancellationToken)
        {
            if (addWorkOrderDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseWorkOrderDto>(NasbNiroMessages.InvalidData);
            }

            bool contractExists = await _contractService.ContractExistsAsync(addWorkOrderDto.ContractId, false, cancellationToken);

            if (!contractExists)
            {
                return ResponseDtoHelper.NotFound<ResponseWorkOrderDto>(NasbNiroMessages.ContractNotFound);
            }

            bool isCountValid = await IsCountValidAsync(
                contractId: addWorkOrderDto.ContractId,
                equipmentId: addWorkOrderDto.EquipmentId,
                countToAdd: addWorkOrderDto.Count, cancellationToken: cancellationToken);

            if (!isCountValid)
            {
                return ResponseDtoHelper.BadRequest<ResponseWorkOrderDto>(WorkOrderMessages.CountIsNotValid);
            }

            ResponseGeneratedAssembliesDto responseGeneratedAssembliesDto = await _equipmentService
                .GenerateAssembliesFromEquipmentAsync(addWorkOrderDto.EquipmentId, cancellationToken);

            if (!responseGeneratedAssembliesDto.EquipmentExists)
            {
                return ResponseDtoHelper.NotFound<ResponseWorkOrderDto>(NasbNiroMessages.EquipmentNotFound);
            }

            WorkOrder workOrderToAdd = _mapper.Map<WorkOrder>(addWorkOrderDto);
            IEnumerable<AssemblyEntity> assembliesToAdd = responseGeneratedAssembliesDto.Assemblies;

            EntityEntry<WorkOrder> entityEntry = await _workOrders.AddAsync(workOrderToAdd);
            int workOrderCount = entityEntry.Entity.Count;

            foreach (AssemblyEntity assembly in assembliesToAdd)
            {
                SetWorkOrderToAssembly(assembly, entityEntry.Entity);
                if (assembly.Boms != null)
                {
                    foreach (Bom bomToAdd in assembly.Boms)
                    {
                        bomToAdd.Count *= workOrderCount;
                    }
                }

                await _assemblies.AddAsync(assembly);
            }

            if (await this.SaveChangesAsync(cancellationToken))
            {
                ResponseDto<ResponseWorkOrderDto> responseWorkOrderDto = await GetResponseWorkOrderDtoAsync(entityEntry.Entity, cancellationToken);

                return ResponseDtoHelper.Ok(responseWorkOrderDto.Data, responseGeneratedAssembliesDto.Errors.ToString());
            }


            return ResponseDtoHelper
                .BadRequest<ResponseWorkOrderDto>(NasbNiroMessages.WorkOrderNotAdded);
        }

        private void SetWorkOrderToAssembly(AssemblyEntity assembly, WorkOrder entity)
        {
            assembly.WorkOrder = entity;

            foreach (AssemblyEntity item in assembly.Children)
            {
                SetWorkOrderToAssembly(item, entity);
            }
        }

        private async Task<bool> IsCountValidAsync(int contractId, int equipmentId, int countToAdd, CancellationToken cancellationToken)
        {
            var result = await _contractEquipmentService.GetRemainingCountByIdAsync(new GetContractEquipmentDto()
            {
                ContractId = contractId,
                EquipmentId = equipmentId
            }, cancellationToken);

            if (!result.Success) return false;

            return countToAdd <= result.Data.Count;
        }

        public async WorkOrderResult UpdateWorkOrderAsync(UpdateWorkOrderDto updateWorkOrderDto, CancellationToken cancellationToken)
        {
            if (updateWorkOrderDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseWorkOrderDto>(NasbNiroMessages.InvalidData);
            }

            WorkOrder workOrder = await _workOrders.SingleOrDefaultAsync(x =>
                !x.Deleted && x.Id == updateWorkOrderDto.Id && x.ContractId == updateWorkOrderDto.ContractId,
                cancellationToken);

            if (workOrder is null)
            {
                return ResponseDtoHelper.NotFound<ResponseWorkOrderDto>(NasbNiroMessages.WorkOrderNotFound);
            }

            bool isCountValid = await IsCountValidAsync(
               contractId: workOrder.ContractId,
               equipmentId: workOrder.EquipmentId,
               countToAdd: updateWorkOrderDto.Count - workOrder.Count, cancellationToken: cancellationToken);

            if (!isCountValid)
            {
                return ResponseDtoHelper.BadRequest<ResponseWorkOrderDto>(WorkOrderMessages.CountIsNotValid);
            }

            _mapper.Map(updateWorkOrderDto, workOrder);

            EntityEntry<WorkOrder> entityEntry = _workOrders.Update(workOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseWorkOrderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseWorkOrderDto>(NasbNiroMessages.WorkOrderNotUpdated);
        }

        public async WorkOrderAndChildren ChangeConditionAsync(UpdateWorkOrderChangeConditionDto dto, CancellationToken cancellationToken)
        {
            bool exists = await _workOrders
                .AnyAsync(x => !x.Deleted && x.Id == dto.WorkOrderId, cancellationToken);

            if (exists is false)
            {
                return ResponseDtoHelper.NotFound<ResponseWorkOrderAndChildrenDto>(WorkOrderMessages.NotFound);
            }

            WorkOrderCondition currentCondition = await _workOrders
                .Where(x => x.Id == dto.WorkOrderId)
                .Select(x => x.Condition)
                .SingleOrDefaultAsync(cancellationToken);

            if (currentCondition == dto.NewCondition)
            {
                return ResponseDtoHelper.BadRequest<ResponseWorkOrderAndChildrenDto>(WorkOrderMessages.IsAlreadyInRequestedCondition);
            }

            WorkOrder workOrderToUpdate = await _workOrders
                .Include(x => x.Assemblies)
                .ThenInclude(x => x.Boms)
                .ThenInclude(x => x.Processes)
                .SingleOrDefaultAsync(x => !x.Deleted && x.Id == dto.WorkOrderId, cancellationToken);


            if (dto.NewCondition == WorkOrderCondition.Active)
            {
                IEnumerable<WorkOrderHistory> lastHistories = await _workOrderHistories
                             .OrderByDescending(x => x.Id)
                             .Where(x => x.WorkOrderId == dto.WorkOrderId)
                             .ToListAsync(cancellationToken);

                WorkOrderHistory lastHistory;

                lastHistory = lastHistories
                   .TakeWhile(x => x.Condition != WorkOrderCondition.Active)
                   .LastOrDefault();

                lastHistory = lastHistories
                  .LastOrDefault();

                if (lastHistory != null)
                {
                    _dateTimeService.ResetMinDateForHolidates();
                    _dateTimeService.SetMinDateForHolidates(workOrderToUpdate.StartDate);
                    await ApplyDelayAsync(workOrderToUpdate, lastHistory.Date);
                }
            }

            await _workOrderHistories
                .AddAsync(new WorkOrderHistory()
                {
                    WorkOrderId = workOrderToUpdate.Id,
                    Condition = dto.NewCondition,
                    Date = DateTimeOffset.UtcNow
                });
            workOrderToUpdate.Condition = dto.NewCondition;
            _workOrders.Update(workOrderToUpdate);

            if (await SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(await _workOrders
                    .ProjectTo<ResponseWorkOrderAndChildrenDto>(_mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(x => x.Id == dto.WorkOrderId, cancellationToken));
            }

            return ResponseDtoHelper.BadRequest<ResponseWorkOrderAndChildrenDto>(WorkOrderMessages.ChangingConditionFailed);
        }

        private async Task ApplyDelayAsync(WorkOrder workOrderToUpdate, DateTimeOffset firstInActiveDate)
        {
            firstInActiveDate = firstInActiveDate.OnlyDateNotHoursUtc();
            int daysToAdd = (DateTimeOffset.UtcNow - firstInActiveDate).Days;

            if (daysToAdd == 0) return;

            foreach (AssemblyEntity assembly in workOrderToUpdate.Assemblies.Where(x => !x.Deleted))
            {
                if (assembly.StartDate.HasValue && assembly.StartDate > firstInActiveDate)
                {
                    assembly.StartDate = await _dateTimeService
                        .GetNextValidDateAsync(assembly.StartDate.Value, daysToAdd);
                }

                if (assembly.EndDate.HasValue && assembly.EndDate > firstInActiveDate)
                {
                    assembly.EndDate = await _dateTimeService
                        .GetNextValidDateAsync(assembly.EndDate.Value, daysToAdd);
                }

                foreach (Bom bom in assembly.Boms.Where(x => !x.Deleted))
                {
                    foreach (ProcessEntity process in bom.Processes.Where(x => !x.Deleted))
                    {
                        if (process.ProgramStartDate.HasValue && process.ProgramStartDate > firstInActiveDate)
                        {
                            process.ProgramStartDate = await _dateTimeService
                                .GetNextValidDateAsync(process.ProgramStartDate.Value, daysToAdd);
                        }
                        if (process.ProgramEndDate.HasValue && process.ProgramEndDate > firstInActiveDate)
                        {
                            process.ProgramEndDate = await _dateTimeService
                                .GetNextValidDateAsync(process.ProgramEndDate.Value, daysToAdd);
                        }
                    }
                }
            }
        }

        public async WorkOrderAndChildren SetStartDateAsync(UpdateWorkOrderSetStartDateDto dto, CancellationToken cancellationToken)
        {
            #region Get WorkOrder
            WorkOrder workOrderToUpdate = await _workOrders
                .SingleOrDefaultAsync(x => !x.Deleted && x.Id == dto.WorkOrderId, cancellationToken);
            #endregion

            #region Validate WorkOrder
            if (workOrderToUpdate is null)
                return ResponseDtoHelper.NotFound<ResponseWorkOrderAndChildrenDto>(WorkOrderMessages.NotFound);

            if (workOrderToUpdate.Condition != WorkOrderCondition.Active)
                return ResponseDtoHelper.BadRequest<ResponseWorkOrderAndChildrenDto>(WorkOrderMessages.ShouldBeActiveToSetStartDate);
            #endregion

            int workOrderId = workOrderToUpdate.Id;
            DateTimeOffset startDate = dto.StartDate;

            //here dateTime offset is the end time of bom
            Dictionary<BomAndAssemblyId, DateTimeOffset?> bomEndDateCache = new Dictionary<BomAndAssemblyId, BomEndDate?>();

            #region reset datetimeservice
            _dateTimeService.ResetMinDateForHolidates();
            _dateTimeService.SetMinDateForHolidates(dto.StartDate);
            #endregion

            #region Arrange Assembly
            async ValueTask ArrangeAssemblyAsync(AssemblyEntity assembly, CancellationToken cancellationTokenX)
            {
                bool isDone = await _boms
                    .AnyAsync(x =>
                        x.Id == assembly.Id && !x.Deleted &&
                        ApplicationDbContext.CalculateAssemblyProgressLevel(x.Id) == Constants.CompletedProgressLevel,
                        cancellationToken);

                if (isDone) return;

                assembly.StartDate = bomEndDateCache.Keys.Where(x => x.Item2 == assembly.Id).Select(x => bomEndDateCache[x]).Min();
                assembly.EndDate = bomEndDateCache.Keys.Where(x => x.Item2 == assembly.Id).Select(x => bomEndDateCache[x]).Max();

                if (assembly.EndDate.HasValue)
                    assembly.EndDate = await _dateTimeService.GetNextValidDateAsync(assembly.EndDate.Value, assembly.AssemblerDuration);
            }
            #endregion

            #region Arrange Bom
            async ValueTask ArrangeBomAsync(Bom bom, DateTimeOffset bomStartDate, CancellationToken cancellationTokenX)
            {
                bool isDone = await _boms
                    .AnyAsync(x =>
                        x.Id == bom.Id && !x.Deleted && ApplicationDbContext.CalculateBomProgressLevel(x.Id) == Constants.CompletedProgressLevel,
                        cancellationToken);

                BomAndAssemblyId chacheKey = (bom.Id, bom.AssemblyId);

                if (isDone)
                {
                    DateTimeOffset? endDate = bom.Processes.Select(x => x.ProgramEndDate).Max();

                    bomEndDateCache.Add(chacheKey, endDate);
                    return;
                }


                var arrangeProcessResult = await ArrangeProcessesAsync(bom.Processes, startDate, cancellationTokenX);

                if (!arrangeProcessResult.Success)
                {
                    throw new Exception(arrangeProcessResult.Message);
                }

                bomEndDateCache.Add(chacheKey, arrangeProcessResult.Data);
            }
            #endregion

            #region Arrange Proceeses 
            async Task<ResponseDto<BomEndDate>> ArrangeProcessesAsync(
                IEnumerable<ProcessEntity> processes, BomEndDate bomStartDate, CancellationToken cancellationTokenX)
            {
                IEnumerable<ProcessEntity> validProcesses = processes
                    .Where(x => !x.Deleted &&
                           x.ProcessStatus != ProcessStatus.ProcessIsCompleted)
                    .OrderBy(x => x.Sort)
                    .ToList();

                if (!validProcesses.Any())
                    return ResponseDtoHelper.BadRequest<BomEndDate>(WorkOrderMessages.EmptyBomFound);

                DateTimeOffset lastDate = bomStartDate;
                DateTimeOffset lastDateToReturn = default(DateTimeOffset);

                foreach (ProcessEntity process in validProcesses)
                {
                    process.ProgramStartDate = lastDate;
                    int duration = process.GetTotalDuration();

                    var nexDateResult = await _dateTimeService.GetNextValidDateAsync(lastDate, duration);

                    lastDate = nexDateResult;
                    process.ProgramEndDate = lastDate;
                    lastDateToReturn = lastDate;

                    nexDateResult = await _dateTimeService.GetNextValidDateAsync(lastDate);
                    lastDate = nexDateResult;
                }

                return ResponseDtoHelper.Ok(lastDateToReturn);
            }
            #endregion

            #region Process Boms
            IEnumerable<Bom> bomsToUpdate = await _boms
                .Include(x => x.Processes)
                .Where(x => !x.Deleted)
                .Where(x => x.Assembly.WorkOrderId == workOrderId)
                .OrderBy(x => x.PredecessorId)
                .ToListAsync(cancellationToken);

            Queue<Bom> bomsPendingPredecessor = new Queue<Bom>();

            bomsToUpdate.ForEach(x => bomsPendingPredecessor.Enqueue(x));

            while (bomsPendingPredecessor.Any())
            {
                Bom bomToUpdate = bomsPendingPredecessor.Dequeue();
                int? predecessorId = bomToUpdate.PredecessorId;

                if (!predecessorId.HasValue)
                {
                    await ArrangeBomAsync(bomToUpdate, startDate, cancellationToken);
                }
                else
                {
                    if (!bomEndDateCache.Keys.Any(x => x.Item1 == predecessorId.Value))
                    {
                        bomsPendingPredecessor.Enqueue(bomToUpdate);
                        continue;
                    }
                    DateTimeOffset bomToUpdateStartDate =
                        bomEndDateCache
                            .Keys
                            .Where(x => x.Item1 == predecessorId.Value)
                            .Select(x => bomEndDateCache[x])
                            .First().Value;

                    await ArrangeBomAsync(bomToUpdate, startDate, cancellationToken);
                }
            }
            #endregion

            #region Process Assemblies
            IEnumerable<AssemblyEntity> assembliesToUpdate = await _assemblies
                .Where(x => !x.Deleted)
                .Where(x => x.WorkOrderId == workOrderId)
                .ToListAsync(cancellationToken);



            await assembliesToUpdate.ForEachAsync(async assembly =>
             {
                 await ArrangeAssemblyAsync(assembly, cancellationToken);
             });

            #endregion

            workOrderToUpdate.SetStartDate(dto.StartDate);
            _workOrders.Update(workOrderToUpdate);

            if (await SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(await _workOrders
                    .ProjectTo<ResponseWorkOrderAndChildrenDto>(_mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(x => x.Id == dto.WorkOrderId, cancellationToken));
            }

            return ResponseDtoHelper.BadRequest<ResponseWorkOrderAndChildrenDto>(WorkOrderMessages.FailedToSetStartDate);
        }

        public async WorkOrderResult DeleteWorkOrderAsync(int workOrderId, CancellationToken cancellationToken)
        {
            WorkOrder workOrder = await _workOrders
                .Include(x => x.Assemblies)
                    .ThenInclude(x => x.Boms)
                    .ThenInclude(x => x.Processes)
                .Include(x => x.BuyOrders)
                .Where(x => !x.Deleted)
                .SingleOrDefaultAsync(x => x.Id == workOrderId, cancellationToken);

            if (workOrder is null)
            {
                return ResponseDtoHelper.NotFound<ResponseWorkOrderDto>(NasbNiroMessages.WorkOrderNotFound);
            }

            if (workOrder.Deleted == true)
            {
                return ResponseDtoHelper.BadRequest<ResponseWorkOrderDto>(NasbNiroMessages.WorkOrderAlreadyIsDeleted);
            }

            // R1a: a work order with production orders, inventory documents, cost entries or other downstream records is not deleted
            string blocked = await LegacyDeleteGuard.WorkOrderAsync(_unitOfWork, workOrderId, cancellationToken);
            if (blocked != null)
            {
                return ResponseDtoHelper.BadRequest<ResponseWorkOrderDto>(blocked);
            }

            workOrder.Delete();


            if (workOrder.BuyOrders != null)
            {
                foreach (BuyOrder buyOrder in workOrder.BuyOrders)
                {
                    buyOrder.Delete();
                }
            }

            if (workOrder.Assemblies != null)
            {
                foreach (AssemblyEntity assemblyEntity in workOrder.Assemblies)
                {
                    assemblyEntity.Delete();

                    if (assemblyEntity.Boms != null)
                    {
                        foreach (Bom bom in assemblyEntity.Boms)
                        {
                            bom.Delete();

                            if (bom.Processes != null)
                            {
                                foreach (ProcessEntity process in bom.Processes)
                                {
                                    process.Delete();
                                }
                            }
                        }
                    }
                }
            }

            EntityEntry<WorkOrder> entityEntry = _workOrders.Update(workOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseWorkOrderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseWorkOrderDto>(NasbNiroMessages.WorkOrderNotDeleted);
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteWorkOrderResultDto>>> DeleteWorkOrderCollectionAsync(
            IEnumerable<int> deleteWorkOrderIds, CancellationToken cancellationToken)
        {
            if (deleteWorkOrderIds is null)
            {
                return ResponseDtoHelper.BadRequest<IEnumerable<DeleteWorkOrderResultDto>>(NasbNiroMessages.InvalidData);
            }

            List<DeleteWorkOrderResultDto> deleteWorkOrderResultDtos = new List<DeleteWorkOrderResultDto>();

            foreach (int workOrderId in deleteWorkOrderIds)
            {
                bool exception = false;
                ResponseDto<ResponseWorkOrderDto> result = null;

                try
                {
                    result = await this.DeleteWorkOrderAsync(workOrderId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteWorkOrderResultDtos.Add(new DeleteWorkOrderResultDto()
                    {
                        Id = workOrderId,
                        Success = false,
                        Message = NasbNiroMessages.WorkOrderDeletingException
                    });

                    continue;
                }

                DeleteWorkOrderResultDto deleteWorkOrderResultDto
                    = new DeleteWorkOrderResultDto() { Id = workOrderId, Success = result.Success };

                if (!result.Success)
                {
                    deleteWorkOrderResultDto.Message = result.Message;
                }

                deleteWorkOrderResultDtos.Add(deleteWorkOrderResultDto);
            }

            return ResponseDtoHelper.Ok(deleteWorkOrderResultDtos.AsEnumerable());
        }

        public async ValueTask<ResponseDto<PagedList<ResponseWorkOrderDto>>> GetAllResponseWorkOrderDtosAsync(
            WorkOrderPaginationParams workOrderPaginationParams, CancellationToken cancellationToken)
        {
            if (workOrderPaginationParams is null)
                return ResponseDtoHelper
                    .BadRequest<PagedList<ResponseWorkOrderDto>>(NasbNiroMessages.InvalidData);

            IQueryable<WorkOrder> query = _workOrdersQueryable;

            if (workOrderPaginationParams.IsDeleted.HasValue)
                query = query.Where(workOrder =>
                        workOrder.Deleted == workOrderPaginationParams.IsDeleted);

            if (!string.IsNullOrEmpty(workOrderPaginationParams.Contains))
            {
                string[] strArray = workOrderPaginationParams
                    .Contains.Split(' ');

                query = query.Where(workOrder =>
                    workOrder.Code != null && strArray.Any(str => workOrder.Code.Contains(str)) ||
                    workOrder.Title != null && strArray.Any(str => workOrder.Title.Contains(str)) ||
                    workOrder.Description != null && strArray.Any(str => workOrder.Description.Contains(str)));
            }

            #region numericalParams
            if (workOrderPaginationParams.Count.HasValue)
            {
                query = query.Where(workOrder =>
                    workOrder.Count == workOrderPaginationParams.Count);
            }

            query = query.Where(workOrder =>
                workOrder.ContractId == workOrderPaginationParams.ContractId);
            #endregion

            IQueryable<ResponseWorkOrderDto> resultQuery;

            resultQuery = query.ProjectTo<ResponseWorkOrderDto>
                (_mapper.ConfigurationProvider);


            Task<PagedList<ResponseWorkOrderDto>> data = PagedList<ResponseWorkOrderDto>
                .CreateAsync(resultQuery,
                            workOrderPaginationParams.PageNumber,
                            workOrderPaginationParams.PageSize,
                            cancellationToken);

            return ResponseDtoHelper.Ok(await data);
        }

        public async WorkOrderResult GetResponseWorkOrderDtoAsync(GetWorkOrderDto getWorkOrderDto, CancellationToken cancellationToken)
        {
            if (getWorkOrderDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseWorkOrderDto>(NasbNiroMessages.InvalidData);
            }

            IQueryable<ResponseWorkOrderDto> resultQuery = _workOrdersQueryable
                .ProjectTo<ResponseWorkOrderDto>(_mapper.ConfigurationProvider);

            ResponseWorkOrderDto result = await resultQuery
                .SingleOrDefaultAsync(wo =>
                    wo.Id == getWorkOrderDto.Id && wo.ContractId == getWorkOrderDto.ContractId,
                    cancellationToken);

            if (result != null)
            {
                return ResponseDtoHelper.Ok(result);
            }

            return ResponseDtoHelper.NotFound<ResponseWorkOrderDto>(NasbNiroMessages.WorkOrderNotFound);
        }

        public async ValueTask<WorkOrder> GetWorkOrderByIdAsync(int workOrderId, CancellationToken cancellationToken, bool IncludeAllChildren = false)
        {
            IQueryable<WorkOrder> query = _workOrders.AsQueryable();

            if (IncludeAllChildren)
            {
                query = query
                        .Include(x => x.BuyOrders)
                        .Include(x => x.Assemblies)
                        .ThenInclude(x => x.Boms)
                            .ThenInclude(x => x.Processes);
            }

            WorkOrder workOrder =
               await query.SingleOrDefaultAsync(ea => ea.Id == workOrderId, cancellationToken);

            return workOrder;
        }

        public async ValueTask<ResponseDto<IEnumerable<AssemblyProgressLevelDto>>> CalculateProgressLevelAsync(
            int workOrderId, CancellationToken cancellationToken, Expression<Func<WorkOrder, bool>> where = null)
        {
            IQueryable<WorkOrder> query = _workOrdersQueryable;

            if (where != null)
            {
                query = query.Where(where);
            }

            query = query
                .Include(x => x.Assemblies)
                .ThenInclude(x => x.Boms)
                .ThenInclude(x => x.Processes);

            WorkOrder workOrder = await query.SingleOrDefaultAsync(x => x.Id == workOrderId, cancellationToken);

            if (workOrder is null)
            {
                return ResponseDtoHelper.NotFound<IEnumerable<AssemblyProgressLevelDto>>(NasbNiroMessages.WorkOrderNotFound);
            }

            List<AssemblyProgressLevelDto> assemblyProgressLevels = new List<AssemblyProgressLevelDto>();

            foreach (AssemblyEntity assembly in workOrder.Assemblies)
            {
                AssemblyProgressLevelDto assemblyProgressLevel = CalculateAssemblyProgressLevel(assembly);
                assemblyProgressLevels.Add(assemblyProgressLevel);
            }

            return ResponseDtoHelper.Ok(assemblyProgressLevels.AsEnumerable());
        }

        private AssemblyProgressLevelDto CalculateAssemblyProgressLevel(AssemblyEntity assembly)
        {
            List<BomProgressLevelDto> bomProgressLevels = new List<BomProgressLevelDto>();

            foreach (Bom bom in assembly.Boms)
            {
                BomProgressLevelDto bomProgressLevel = CalculateBomProgressLevel(bom);
                bomProgressLevels.Add(bomProgressLevel);
            }

            return new AssemblyProgressLevelDto()
            {
                AssemblyId = assembly.Id,
                Title = assembly.Title,
                ProgressLevel = bomProgressLevels.Count == 0 ? 0 : bomProgressLevels.Average(x => x.ProgressLevel),
                BomProgressLevels = bomProgressLevels
            };
        }

        private BomProgressLevelDto CalculateBomProgressLevel(Bom bom)
        {
            int count = 0;
            decimal done = 0;

            List<ProcessProgressLevelDto> processProgressLevels = new List<ProcessProgressLevelDto>();

            foreach (ProcessEntity process in bom.Processes.Where(x => !x.Deleted))
            {
                decimal processProgressLevel = process.ProgressLevel;

                processProgressLevels.Add(new ProcessProgressLevelDto()
                {
                    ProcessId = process.Id,
                    Title = process.Title,
                    ProgressLevel = processProgressLevel,
                    ActualEndDate = process.ActualEndDate,
                    ActualStartDate = process.ActualStartDate,
                    ProgramEndDate = process.ProgramEndDate,
                    ProgramStartDate = process.ProgramStartDate,
                    Status = process.ProcessStatus
                });

                count++;
                if (process.ProcessStatus == ProcessStatus.ProcessIsCompleted) done++;
            }

            return new BomProgressLevelDto()
            {
                BomId = bom.Id,
                Title = bom.Title,
                ProgressLevel = count == 0 ? 0 : 100 * done / count,
                ProcessProgressLevels = processProgressLevels
            };
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }

        public async ValueTask<bool> WorkOrderExistsAsync(int workOrderId, CancellationToken cancellationToken)
        {
            bool result = await _workOrders.AnyAsync(e =>
                e.Id == workOrderId,
                cancellationToken);

            return result;
        }

        public async ValueTask<bool> WorkOrderExistsAsync(int workOrderId, bool isDeleted, CancellationToken cancellationToken)
        {
            bool result = await _workOrders.AnyAsync(e =>
                e.Id == workOrderId && e.Deleted == isDeleted,
                cancellationToken);

            return result;
        }
    }
}
