using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.Extentions;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Identity.Constants;
using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Services.Contracts.Identity;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.WorkOrders;
using Services.Resources;
using ViewModels;
using ViewModels.Identity.UserDtos;
using ViewModels.NasbNiro.AssemblyDtos;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.CardBoard;
using ViewModels.NasbNiro.DashboardDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcessDtos;

namespace Services.NasbNiro
{
    public class CardBoardService : ICardBoardService
    {
        private Expression<Func<AssemblyEntity, bool>> WhereWorkOrderOfAssemblyIsActive =>
            x => x.WorkOrder.Condition != WorkOrderCondition.Canceled && x.WorkOrder.Condition != WorkOrderCondition.Stopped;

        private Expression<Func<ProcessEntity, bool>> WhereWorkOrderOfProcessIsActive =>
           x => x.Bom.Assembly.WorkOrder.Condition != WorkOrderCondition.Canceled && x.Bom.Assembly.WorkOrder.Condition != WorkOrderCondition.Stopped;

        private Expression<Func<BuyOrder, bool>> WhereWorkOrderOfBuyOrderIsActive =>
            x => x.WorkOrder.Condition != WorkOrderCondition.Canceled && x.WorkOrder.Condition != WorkOrderCondition.Stopped;

        private readonly IMapper _mapper;
        private readonly IApplicationUserManager _userManager;


        private readonly DbSet<BuyOrder> _buyOrders;
        private readonly DbSet<ProcessEntity> _processes;
        private readonly DbSet<Bom> _bom;
        private readonly DbSet<ProcessFile> _processeFiles;
        private readonly DbSet<AssemblyEntity> _assemblies;
        private readonly DbSet<WorkOrder> _workOrder;
        private readonly DbSet<UserRole> _userRole;
        private readonly IUnitOfWork _uow;
        private readonly IWorkOrderService _workOrderService;
        private readonly IWorkflowStationService _workflowStationService;

        public CardBoardService(
            IMapper mapper,
            IApplicationUserManager userManager,
            IUnitOfWork uow,
            IWorkOrderService workOrderService,
            IWorkflowStationService workflowStationService)
        {
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _userManager = userManager.ThrowIfNull(nameof(userManager));
            _uow = uow.ThrowIfNull(nameof(userManager));
            _workOrderService = workOrderService.ThrowIfNull(nameof(workOrderService));
            _workflowStationService = workflowStationService.ThrowIfNull(nameof(workOrderService));

            _buyOrders = _uow.Set<BuyOrder>().ThrowIfNull(nameof(_buyOrders));
            _workOrder = _uow.Set<WorkOrder>().ThrowIfNull(nameof(_buyOrders));
            _userRole = _uow.Set<UserRole>().ThrowIfNull(nameof(_buyOrders));
            _processes = _uow.Set<ProcessEntity>().ThrowIfNull(nameof(_processes));
            _processeFiles = _uow.Set<ProcessFile>().ThrowIfNull(nameof(_processeFiles));
            _bom = _uow.Set<Bom>().ThrowIfNull(nameof(_processeFiles));
            _assemblies = _uow.Set<AssemblyEntity>().ThrowIfNull(nameof(_assemblies));
        }

        public async ValueTask<ResponseDto<ResponseDashboardEntityDto<ResponseBuyOrderDto>>>
            GetDashboardBuyOrdersAsync(CancellationToken cancellationToken)
        {
            ResponseDashboardEntityDto<ResponseBuyOrderDto> result = new ResponseDashboardEntityDto<ResponseBuyOrderDto>();

            IQueryable<BuyOrder> query = await GetCurrentUserBuyOrdersQueryAsync(cancellationToken);

            result.TotalCount = await query.CountAsync(cancellationToken);
            result.Entities = await query.OrderByDescending(x => x.Id).ProjectTo<ResponseBuyOrderDto>(_mapper.ConfigurationProvider).Take(10).ToListAsync(cancellationToken);

            return ResponseDtoHelper.Ok(result);
        }

        public async ValueTask<ResponseDto<PagedList<ResponseBuyOrderCardBoardDto>>>
            GetAllUserBuyOrdersAsync(UserBuyOrdersPaginationParams userBuyOrdersPaginationParams, CancellationToken cancellationToken)
        {
            IQueryable<BuyOrder> query = await GetCurrentUserBuyOrdersQueryAsync(cancellationToken);

            var resultQuery = query.ProjectTo<ResponseBuyOrderCardBoardDto>(_mapper.ConfigurationProvider);

            var result = await PagedList<ResponseBuyOrderCardBoardDto>
                .CreateAsync(resultQuery, userBuyOrdersPaginationParams.PageNumber, userBuyOrdersPaginationParams.PageSize, cancellationToken);

            return ResponseDtoHelper.Ok(result);
        }

        public async ValueTask<ResponseDto<ResponseObjects>>
            GetAllUserProcessesAsync(UserProcessesPaginationParams userProcessesPaginationParams, CancellationToken cancellationToken)
        {
            List<ProccessCardBoardDto> cardBoardDtos = await _workOrder.Include(x => x.Contract).Where(x => x.Condition == 0
                                                                                                        && !x.Deleted
                                                                                                        && !x.Contract.Deleted)
                                                                        .Select(x => new ProccessCardBoardDto()
                                                                        {
                                                                            Id = x.Id,
                                                                            Title = x.Contract.Title + " - " + x.Title
                                                                        })
                                                                        .ToListAsync();
            for (int i = 0; i < cardBoardDtos.Count; i++)
            {
                List<int> subAssembly = await _assemblies.Where(x => x.WorkOrderId == cardBoardDtos[i].Id && !x.Deleted && (x.PredecessorId == 0 || x.PredecessorId == null)).Select(x => x.Id).ToListAsync();
                for (int j = 0; j < subAssembly.Count; j++)
                {
                    ProccessCardBoardAssemblyDto cardBoardAssemblyDto = await GetAssemblyToCardBoard(subAssembly[j]);
                    if (cardBoardAssemblyDto.Boms.Count > 0 || cardBoardAssemblyDto.assemblies != null)
                    {
                        cardBoardDtos[i].Assemblies.Add(cardBoardAssemblyDto);
                    }
                }
            }

            //IEnumerable<ProcessStatus> HasProcessStatuses = await GetCurrentUserProcessStatusesAsync(cancellationToken);

            //int userId = int.Parse(_userManager.GetCurrentUserId());

            //List<ResponseProcessCardBoardDto> response = await _processes
            //    .Include(x => x.Bom)
            //    .Include(x => x.Bom.Assembly)
            //    .Where(WhereWorkOrderOfProcessIsActive)
            //    .Where(x => !x.Deleted)
            //    .Where(x => HasProcessStatuses.Any(ps =>
            //        ((ps != ProcessStatus.EngineeringWorkflowStart && ps != ProcessStatus.DocumentsAdded) && ps == x.ProcessStatus) ||
            //        (ps == ProcessStatus.EngineeringWorkflowStart && x.WorkflowUserId == userId)))
            //    .Select(x => new ResponseProcessCardBoardDto()
            //    {
            //        Id = x.Id,
            //        Title = x.Title,
            //        Type = x.Type,
            //        AssemblyId = x.Bom.AssemblyId,
            //        AssemblyTitle = x.Bom.Assembly.Title,
            //        BomId = x.BomId,
            //        BomTitle = x.Bom.Title,
            //        Deleted = x.Deleted,
            //        EnterCommodityCode = x.EnterCommodityCode,
            //        ExitCommodityCode = x.ExitCommodityCode,
            //        MapDuration = x.MapDuration,
            //        MasterDuration = x.MasterDuration,
            //        ProcessDuration = x.ProcessDuration,
            //        ProcessStatus = x.ProcessStatus,
            //        ProgramPrice = x.ProgramPrice,
            //        Sort = x.Sort,
            //        UserId = userId,
            //        UserName = "",
            //        WorkOrderId = x.Bom.Assembly.WorkOrderId,
            //        WorkOrderTitle = ""
            //    }).ToListAsync();

            //for (int i = 0; i < response.Count; i++)
            //{
            //    List<ProcessFile> processFiles = await _processeFiles.Include(x => x.User).Where(x => x.ProcessId == response[i].Id).ToListAsync();
            //    for (int j = 0; j < processFiles.Count; j++)
            //    {
            //        response[i].Files.Add(new ResponseProcessFileDto
            //        {
            //            Id = processFiles[j].Id,
            //            ProcessId = processFiles[j].ProcessId,
            //            DocumentId = processFiles[j].DocumentId,
            //            RegDate = processFiles[j].RegDate,
            //            Major = processFiles[j].Major,
            //            Minor = processFiles[j].Minor,
            //            UserId = processFiles[j].UserId,
            //            UserDispalyName = processFiles[j].User.DisplayName,
            //            Description = processFiles[j].Description
            //        });
            //    }
            //}




            ////IQueryable<ProcessEntity> query = await GetCurrentUserProcessesQueryAsync(cancellationToken);


            ////var resultQuery = query.ProjectTo<ResponseProcessCardBoardDto>(_mapper.ConfigurationProvider);

            ////var result = await PagedList<ResponseProcessCardBoardDto>.CreateAsync(
            ////        resultQuery,
            ////        userProcessesPaginationParams.PageNumber,
            ////        userProcessesPaginationParams.PageSize,
            ////        cancellationToken);

            //var result = await PagedList<ResponseProcessCardBoardDto>.CreateAsync(
            //        response,
            //        userProcessesPaginationParams.PageNumber,
            //        userProcessesPaginationParams.PageSize,
            //        cancellationToken);
            for (int i = 0; i < cardBoardDtos.Count; i++)
            {
                if (cardBoardDtos[i].Assemblies.Count == 0)
                {
                    cardBoardDtos.RemoveAt(i);
                    i--;
                }
            }
            return ResponseDtoHelper.Ok(new ResponseObjects() { Success = true, Data = cardBoardDtos });
        }

        private async Task<ProccessCardBoardAssemblyDto> GetAssemblyToCardBoard(int id)
        {
            ProccessCardBoardAssemblyDto proccessCardBoardAssemblyDto = new ProccessCardBoardAssemblyDto();
            AssemblyEntity assembly = await _assemblies.Where(x => x.Id == id).FirstOrDefaultAsync();
            proccessCardBoardAssemblyDto.Id = assembly.Id;
            proccessCardBoardAssemblyDto.Title = assembly.Title;
            proccessCardBoardAssemblyDto.Boms = await GetBomToCardBoard(id);
            List<int> subAssemblies = await _assemblies.Where(x => x.PredecessorId == id && !x.Deleted && (x.PredecessorId == 0 || x.PredecessorId == null)).Select(x => x.Id).ToListAsync();
            for (int i = 0; i < subAssemblies.Count; i++)
            {
                ProccessCardBoardAssemblyDto subAssemblyInfo = await GetAssemblyToCardBoard(subAssemblies[i]);
                if (subAssemblyInfo.Boms.Count > 0 || subAssemblyInfo.assemblies != null)
                    proccessCardBoardAssemblyDto.assemblies.Add(subAssemblyInfo);
            }
            for (int i = 0; i < proccessCardBoardAssemblyDto.Boms.Count; i++)
            {
                if (proccessCardBoardAssemblyDto.Boms[i].proccesses.Count == 0)
                {
                    proccessCardBoardAssemblyDto.Boms.RemoveAt(i);
                    i = 0;
                }

            }
            return proccessCardBoardAssemblyDto;
        }

        private async Task<List<ProccessCardBoardBomDto>> GetBomToCardBoard(int assemblyId)
        {
            IEnumerable<ProcessStatus> HasProcessStatuses = await GetCurrentUserProcessStatusesAsync(new CancellationToken());
            int userId = int.Parse(_userManager.GetCurrentUserId());
            List<ProccessCardBoardBomDto> proccessCardBoardBomDtos = await _bom.Where(x => x.AssemblyId == assemblyId
                                                                                        && !x.Deleted)
                                                                                .Select(x => new ProccessCardBoardBomDto()
                                                                                {
                                                                                    Id = x.Id,
                                                                                    Title = x.Title,
                                                                                    PredecessorId = x.PredecessorId
                                                                                }).ToListAsync();
            for (int i = 0; i < proccessCardBoardBomDtos.Count; i++)
            {
                if (!(proccessCardBoardBomDtos[i].PredecessorId == null || proccessCardBoardBomDtos[i].PredecessorId == 0))
                {
                    ProcessEntity process = await _processes.Where(x => x.BomId == proccessCardBoardBomDtos[i].PredecessorId && !x.Deleted).OrderByDescending(x => x.Sort).FirstOrDefaultAsync();
                    //if (process.ProcessStatus!=ProcessStatus.ReadyToGo || process.ProcessStatus != ProcessStatus.BuyOrderHasBeenAdded || process.ProcessStatus != ProcessStatus.ProcessIsCompleted)
                    if (process.ProcessStatus != ProcessStatus.ProcessIsCompleted)
                    {
                        proccessCardBoardBomDtos.RemoveAt(i);
                        i--;
                    }

                }
            }

            for (int i = 0; i < proccessCardBoardBomDtos.Count; i++)
            {
                
                List<UserRole> userRoles = await _userRole.Where(x => x.UserId == userId && x.RoleId == 1).ToListAsync();
                if (userRoles.Count > 0)
                {
                    List<ProccessCardBoardProccessDto> list = await _processes.Where(x => x.BomId == proccessCardBoardBomDtos[i].Id
                                                                                  && !x.Deleted
                                                                                  && x.ProcessStatus != ProcessStatus.ProcessIsCompleted)
                                                                         .OrderBy(x => x.Sort)
                                                                         .Select(x => new ProccessCardBoardProccessDto()
                                                                         {
                                                                             Id = x.Id,
                                                                             Title = x.Title,
                                                                             ExitCommodityCode = x.ExitCommodityCode,
                                                                             EnterCommodityCode = x.EnterCommodityCode,
                                                                             MapDuration = x.MapDuration,
                                                                             MasterDuration = x.MasterDuration,
                                                                             ProcessDuration = x.ProcessDuration,
                                                                             Type = x.Type,
                                                                             ProcessStatus = x.ProcessStatus,
                                                                             Sort = x.Sort,
                                                                             ProgramStartDate = x.ProgramStartDate,
                                                                             ProgramEndDate = x.ProgramEndDate,
                                                                             ActualStartDate = x.ActualStartDate,
                                                                             ActualEndDate = x.ActualEndDate,
                                                                             DocAcceptDate = x.DocAcceptDate,
                                                                             MasterAcceptDate = x.MasterAcceptDate,
                                                                             ProgramPrice = x.ProgramPrice,
                                                                             WeightPercent = x.WeightPercent
                                                                         })
                                                                         .ToListAsync();
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (list[j].ProcessStatus == ProcessStatus.PmoAccepted || list[j].ProcessStatus == ProcessStatus.EngineeringWorkflowStart || list[j].ProcessStatus == ProcessStatus.DocumentsAdded || list[j].ProcessStatus == ProcessStatus.EngineerAcceptedDocuments)
                            break;
                        //if (list[j].ProcessStatus == ProcessStatus.ReadyToGo || list[j].ProcessStatus == ProcessStatus.BuyOrderHasBeenAdded || list[j].ProcessStatus == ProcessStatus.ProcessIsCompleted)
                        if (list[j].ProcessStatus == ProcessStatus.ProcessIsCompleted)
                        {
                            proccessCardBoardBomDtos[i].proccesses.Add(list[j]);
                        }
                        else
                        {
                            proccessCardBoardBomDtos[i].proccesses.Add(list[j]);
                            break;
                        }
                    }
                }
                else
                {
                    List<ProccessCardBoardProccessDto> list = await _processes.Where(x => x.BomId == proccessCardBoardBomDtos[i].Id
                                                                                  && !x.Deleted
                                                                                  && x.ProcessStatus != ProcessStatus.ProcessIsCompleted)
                                                                         .OrderBy(x => x.Sort)
                                                                         .Where(x => HasProcessStatuses.Any(ps =>
                                                                            ((ps != ProcessStatus.EngineeringWorkflowStart && ps != ProcessStatus.DocumentsAdded) && ps == x.ProcessStatus) ||
                                                                            (ps == ProcessStatus.EngineeringWorkflowStart && x.WorkflowUserId == userId)))
                                                                         .Select(x => new ProccessCardBoardProccessDto()
                                                                         {
                                                                             Id = x.Id,
                                                                             Title = x.Title,
                                                                             ExitCommodityCode = x.ExitCommodityCode,
                                                                             EnterCommodityCode = x.EnterCommodityCode,
                                                                             MapDuration = x.MapDuration,
                                                                             MasterDuration = x.MasterDuration,
                                                                             ProcessDuration = x.ProcessDuration,
                                                                             Type = x.Type,
                                                                             ProcessStatus = x.ProcessStatus,
                                                                             Sort = x.Sort,
                                                                             ProgramStartDate = x.ProgramStartDate,
                                                                             ProgramEndDate = x.ProgramEndDate,
                                                                             ActualStartDate = x.ActualStartDate,
                                                                             ActualEndDate = x.ActualEndDate,
                                                                             DocAcceptDate = x.DocAcceptDate,
                                                                             MasterAcceptDate = x.MasterAcceptDate,
                                                                             ProgramPrice = x.ProgramPrice,
                                                                             WeightPercent = x.WeightPercent
                                                                         })
                                                                         .ToListAsync();
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (list[j].ProcessStatus == ProcessStatus.ReadyToGo || list[j].ProcessStatus == ProcessStatus.BuyOrderHasBeenAdded || list[j].ProcessStatus == ProcessStatus.ProcessIsCompleted)
                        {
                            proccessCardBoardBomDtos[i].proccesses.Add(list[j]);
                        }
                        else
                        {
                            proccessCardBoardBomDtos[i].proccesses.Add(list[j]);
                            break;
                        }
                    }
                }
            }
            for (int i = 0; i < proccessCardBoardBomDtos.Count; i++)
            {
                for (int j = 0; j < proccessCardBoardBomDtos[i].proccesses.Count; j++)
                {
                    List<ProcessFile> processFiles = await _processeFiles.Include(x => x.User).Where(x => x.ProcessId == proccessCardBoardBomDtos[i].proccesses[j].Id).ToListAsync();
                    for (int k = 0; k < processFiles.Count; k++)
                    {
                        proccessCardBoardBomDtos[i].proccesses[j].Files.Add(new ResponseProcessFileDto
                        {
                            Id = processFiles[k].Id,
                            ProcessId = processFiles[k].ProcessId,
                            DocumentId = processFiles[k].DocumentId,
                            RegDate = processFiles[k].RegDate,
                            Major = processFiles[k].Major,
                            Minor = processFiles[k].Minor,
                            UserId = processFiles[k].UserId,
                            UserDispalyName = processFiles[k].User.DisplayName,
                            Description = processFiles[k].Description
                        });
                    }
                }
            }
            for (int i = 0; i < proccessCardBoardBomDtos.Count; i++)
            {
                if(proccessCardBoardBomDtos[i].proccesses.Count==0)
                {
                    proccessCardBoardBomDtos.RemoveAt(i);
                    i--;
                }
            }
            return proccessCardBoardBomDtos;
        }

        public async ValueTask<ResponseDto<PagedList<ResponseAssemblyDto>>>
            GetAllUserAssembliesAsync(UserAssembliesPaginationParams userAssembliesPaginationParams, CancellationToken cancellationToken)
        {
            IQueryable<AssemblyEntity> query = await GetCurrentUserAssembliesQueryAsync(cancellationToken);

            var resultQuery = query.ProjectTo<ResponseAssemblyDto>(_mapper.ConfigurationProvider);

            var result = await PagedList<ResponseAssemblyDto>
                .CreateAsync(resultQuery,
                             userAssembliesPaginationParams.PageNumber,
                             userAssembliesPaginationParams.PageSize, cancellationToken);

            return ResponseDtoHelper.Ok(result);
        }

        public async ValueTask<ResponseDto<ResponseDashboardEntityDto<ResponseProcessDashboardDto>>>
             GetUserDashboardProcessesAsync(CancellationToken cancellationToken)
        {
            ResponseDashboardEntityDto<ResponseProcessDashboardDto> result = new ResponseDashboardEntityDto<ResponseProcessDashboardDto>();

            IQueryable<ProcessEntity> query = await GetCurrentUserProcessesQueryAsync(cancellationToken);

            result.TotalCount = await query.CountAsync(cancellationToken);
            result.Entities = await query
                .OrderBy(x => x.Sort)
                .Take(10)
                .ProjectTo<ResponseProcessDashboardDto>(_mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);

            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<ResponseDashboardEntityDto<ResponseAssemblyDashboardDto>>>
            GetUserDashboardAssembliesAsync(CancellationToken cancellationToken)
        {
            IQueryable<ProcessEntity> processes = await GetCurrentUserProcessesQueryAsync(cancellationToken);

            IEnumerable<int> userWorkOrderIds = await processes
                .Select(x => x.Bom.Assembly.WorkOrderId)
                .Distinct()
                .ToListAsync(cancellationToken);

            ResponseDashboardEntityDto<ResponseAssemblyDashboardDto> result = new ResponseDashboardEntityDto<ResponseAssemblyDashboardDto>();

            var query = _assemblies
                .Where(WhereWorkOrderOfAssemblyIsActive)
                .Where(x =>
                    !x.Deleted &&
                    userWorkOrderIds.Contains(x.WorkOrderId) &&
                    ApplicationDbContext.CalculateAssemblyProgressLevel(x.Id) == Constants.CompletedProgressLevel);

            result.TotalCount = await query.CountAsync(cancellationToken);
            result.Entities = await query
                .Take(10)
                .ProjectTo<ResponseAssemblyDashboardDto>(_mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);

            return ResponseDtoHelper.Ok(result);
        }

        private async Task<IQueryable<BuyOrder>> GetCurrentUserBuyOrdersQueryAsync(CancellationToken cancellationToken)
        {
            IEnumerable<BuyOrderStatus> buyOrderStatuses =
                await GetCurrentUserBuyOrderStatusesAsync(cancellationToken);

            return _buyOrders
                .AsNoTracking()
                .Where(WhereWorkOrderOfBuyOrderIsActive)
                .Where(buyOrder => !buyOrder.Deleted)
                .Where(x => buyOrderStatuses.Any(s => s == x.Status));
        }

        private async Task<IEnumerable<BuyOrderStatus>> GetCurrentUserBuyOrderStatusesAsync(CancellationToken cancellationToken)
        {
            User user = await _userManager.GetCurrentUserAsync().WithCancellationTokenAsync(cancellationToken);
            IEnumerable<string> roles = await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken);

            bool AreEqual(string a, string b) => string.Compare(a, b, true) == 0;

            List<BuyOrderStatus> buyOrderStatuses = new List<BuyOrderStatus>();

            bool IsPmo() => roles.Any(r => AreEqual(r, RoleConstants.PMO));
            bool IsEngineer() => roles.Any(r => AreEqual(r, RoleConstants.Engineer));
            bool IsEngineering() => roles.Any(r => AreEqual(r, RoleConstants.Engineering));
            bool IsCommercial() => roles.Any(r => AreEqual(r, RoleConstants.Commercial));
            bool IsFinancial() => roles.Any(r => AreEqual(r, RoleConstants.Financial));
            bool IsQc() => roles.Any(r => AreEqual(r, RoleConstants.QC));
            bool IsWarehouse() => roles.Any(r => AreEqual(r, RoleConstants.WareHouse));
            bool IsAssembler() => roles.Any(r => AreEqual(r, RoleConstants.Assembler));
            bool IsConstruction() => roles.Any(r => AreEqual(r, RoleConstants.Construction));

            if (IsConstruction())
            {
                buyOrderStatuses.AddRange(new BuyOrderStatus[]
                    {
                          BuyOrderStatus.NewBuyOrderForConstructor,
                          BuyOrderStatus.NeedsConstractorFixes,
                    });
            }

            if (IsAssembler())
            {
                buyOrderStatuses.AddRange(new BuyOrderStatus[]
                    {
                          BuyOrderStatus.NewBuyOrderForAssembler,
                          BuyOrderStatus.NeedsAssemblerFixes,
                    });
            }

            if (IsCommercial())
                buyOrderStatuses.AddRange(new BuyOrderStatus[]
                    {
                        BuyOrderStatus.NewBuyOrder ,
                        BuyOrderStatus.InquiriesAdded ,
                        BuyOrderStatus.FinanceDocumentSetted,
                        BuyOrderStatus.NeedsCommercialFixes
                    });

            if (IsEngineering())
                buyOrderStatuses.AddRange(new BuyOrderStatus[]
                {
                    BuyOrderStatus.InquiriesAdded ,
                    BuyOrderStatus.QcRejected
                });

            if (IsPmo())
                buyOrderStatuses.AddRange(new BuyOrderStatus[]
                 {
                    BuyOrderStatus.TechnicalScoreAdded,
                    BuyOrderStatus.QcAccepted ,
                    BuyOrderStatus.EngineeringAccepted ,
                    BuyOrderStatus.RejectedGivenToSalvage ,
                 });

            if (IsFinancial())
                buyOrderStatuses.AddRange(new BuyOrderStatus[]
                 {
                     BuyOrderStatus.WinnerIsSelected,
                     BuyOrderStatus.FinanceAcceptedGivenToWarehouse ,
                 });

            if (IsQc())
                buyOrderStatuses.AddRange(new BuyOrderStatus[]
                 {
                     BuyOrderStatus.TempReceiptGenerated,
                 });

            if (IsWarehouse())
                buyOrderStatuses.AddRange(new BuyOrderStatus[]
                 {
                     BuyOrderStatus.QcAccepted,
                     BuyOrderStatus.EngineeringAccepted,
                     BuyOrderStatus.RejectedGivenToSalvage,
                     BuyOrderStatus.CommercialBought,
                     BuyOrderStatus.AssemblerAssembled,
                     BuyOrderStatus.ConstructorConstructed,
                     BuyOrderStatus.FinanceAcceptedGivenToWarehouse,
                     BuyOrderStatus.RejectedAcceptedByPmo
                 });

            return buyOrderStatuses.Distinct();
        }

        private async Task<IQueryable<ProcessEntity>> GetCurrentUserProcessesQueryAsync(CancellationToken cancellationToken)
        {

            IEnumerable<ProcessStatus> HasProcessStatuses = await GetCurrentUserProcessStatusesAsync(cancellationToken);

            int userId = int.Parse(_userManager.GetCurrentUserId());
            
            return _processes
                .AsNoTracking()
                .Where(WhereWorkOrderOfProcessIsActive)
                .Where(x => !x.Deleted)
                .Where(x => HasProcessStatuses.Any(ps =>
                    ((ps != ProcessStatus.EngineeringWorkflowStart && ps != ProcessStatus.DocumentsAdded) && ps == x.ProcessStatus) ||
                    (ps == x.ProcessStatus && x.WorkflowUserId == userId)));
        }

        private async Task<IEnumerable<ProcessStatus>> GetCurrentUserProcessStatusesAsync(CancellationToken cancellationToken)
        {
            List<ProcessStatus> processStatuses = new List<ProcessStatus>();

            User user = await _userManager
                .GetCurrentUserAsync()
                .WithCancellationTokenAsync(cancellationToken);

            IEnumerable<string> roles = await _userManager.GetRolesAsync(user);

            bool AreEqual(string a, string b) => string.Compare(a, b, true) == 0;

            bool IsPmo() => roles.Any(r => AreEqual(r, RoleConstants.PMO));
            bool IsEngineer() => roles.Any(r => AreEqual(r, RoleConstants.Engineer));
            bool IsEngineering() => roles.Any(r => AreEqual(r, RoleConstants.Engineering));

            if (IsPmo())
                processStatuses.AddRange(new ProcessStatus[]
                    {
                        ProcessStatus.NewProcess ,
                        ProcessStatus.MasterShouldAccept ,
                        ProcessStatus.ReadyToGo ,
                    });

            if (IsEngineer())
                processStatuses.AddRange(new ProcessStatus[]
                {
                    ProcessStatus.DocumentsAdded ,
                    ProcessStatus.EngineeringWorkflowStart
                });

            if (IsEngineering())
                processStatuses.AddRange(new ProcessStatus[]
                {
                    ProcessStatus.EngineerAcceptedDocuments,
                    ProcessStatus.PmoAccepted
                });

            return processStatuses.Distinct();
        }

        private async Task<IQueryable<AssemblyEntity>> GetCurrentUserAssembliesQueryAsync(CancellationToken cancellationToken)
        {

            IEnumerable<AssemblyStatus> HasAssemblyStatuses = await GetCurrentUserAssembliesStatusesAsync(cancellationToken);

            int userId = int.Parse(_userManager.GetCurrentUserId());

            return _assemblies
                .AsNoTracking()
                .Where(WhereWorkOrderOfAssemblyIsActive)
                .Where(x => !x.Deleted)
                .Where(x => x.Status != AssemblyStatus.IsCompleted && x.Status != AssemblyStatus.NewAssembly)
                .Where(x => ApplicationDbContext.CalculateAssemblyProgressLevel(x.Id) == Constants.CompletedProgressLevel)
                .Where(x => HasAssemblyStatuses.Any(ps => ps == x.Status));
        }

        private async Task<IEnumerable<AssemblyStatus>> GetCurrentUserAssembliesStatusesAsync(CancellationToken cancellationToken)
        {
            List<AssemblyStatus> assemblyStatuses = new List<AssemblyStatus>();
            User user = await _userManager.GetCurrentUserAsync();
            IEnumerable<string> roles = await _userManager
                .GetRolesAsync(user)
                .WithCancellationTokenAsync(cancellationToken);

            bool AreEqual(string a, string b) => string.Compare(a, b, true) == 0;

            bool IsPmo() => roles.Any(r => AreEqual(r, RoleConstants.PMO));
            bool IsQc() => roles.Any(r => AreEqual(r, RoleConstants.QC));
            bool IsAssembler() => roles.Any(r => AreEqual(r, RoleConstants.Assembler));

            if (IsPmo())
                assemblyStatuses.AddRange(new AssemblyStatus[]
                    {
                        AssemblyStatus.ReadyToAssemble,
                        AssemblyStatus.ReadyToFinalAssemble,
                        AssemblyStatus.QcAcceptToFinalAssemble,
                        AssemblyStatus.QcRejectToAssemble,
                        AssemblyStatus.QcRejectToFinalAssemble,
                        AssemblyStatus.PendingFinalAcceptance,
                    });

            if (IsQc())
                assemblyStatuses.AddRange(new AssemblyStatus[]
                {
                    AssemblyStatus.AssemblerAssembled,
                    AssemblyStatus.AssemblerFinalAssembled
                });

            if (IsAssembler())
                assemblyStatuses.AddRange(new AssemblyStatus[]
                {
                        AssemblyStatus.ReadyToAssemble,
                        AssemblyStatus.ReadyToFinalAssemble,
                        AssemblyStatus.QcAcceptToFinalAssemble,
                        AssemblyStatus.QcRejectToAssemble,
                        AssemblyStatus.QcRejectToFinalAssemble,
                });

            return assemblyStatuses.Distinct();
        }
    }
}
