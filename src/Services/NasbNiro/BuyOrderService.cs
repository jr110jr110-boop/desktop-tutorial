using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Reports;
using Entities.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Contracts.Identity;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.Extensions;
using Services.NasbNiro.WorkOrders;
using Services.Resources;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;

using BuyOrderResult = System.Threading.Tasks.ValueTask<Common.Results.ResponseDto<ViewModels.NasbNiro.BuyOrderDtos.ResponseBuyOrderDto>>;

namespace Services.NasbNiro
{
    public class BuyOrderService : IBuyOrderService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<BuyOrderService> _logger;
        private readonly DbSet<BuyOrder> _buyOrders;
        private readonly DbSet<BuyOrderFile> _buyOrderFiles;
        private readonly DbSet<BuyOrderProcesses> _buyOrderProcesses;
        private readonly DbSet<ProcessEntity> _processes;
        private readonly DbSet<ProcessFile> _processesFile;
        private readonly IWorkOrderService _workOrderService;
        private readonly IApplicationUserManager _userManager;
        private readonly DbSet<Bom> _boms;
        private readonly IDocumentService _documentService;

        private IQueryable<BuyOrder> _buyOrdersQueryable
            => _buyOrders.AsNoTracking().AsQueryable();

        public BuyOrderService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            ILogger<BuyOrderService> logger,
            IWorkOrderService workOrderService,
            IApplicationUserManager userManager,
            IDocumentService documentService)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _logger = logger.ThrowIfNull(nameof(logger));
            _workOrderService = workOrderService.ThrowIfNull(nameof(workOrderService)); ;
            _userManager = userManager.ThrowIfNull(nameof(userManager));
            _boms = _unitOfWork.Set<Bom>().ThrowIfNull(nameof(_boms));
            _buyOrders = _unitOfWork.Set<BuyOrder>().ThrowIfNull(nameof(_buyOrders));
            _buyOrderFiles = _unitOfWork.Set<BuyOrderFile>().ThrowIfNull(nameof(_buyOrderFiles));
            _buyOrderProcesses = _unitOfWork.Set<BuyOrderProcesses>().ThrowIfNull(nameof(_buyOrderProcesses));
            _processes = _unitOfWork.Set<ProcessEntity>().ThrowIfNull(nameof(_processes));
            _processesFile = _unitOfWork.Set<ProcessFile>().ThrowIfNull(nameof(_processes));
            _documentService = documentService.ThrowIfNull(nameof(documentService));
        }

        public async BuyOrderResult AddBuyOrderAsync(AddBuyOrderDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            bool workOrderExists = await _workOrderService.WorkOrderExistsAsync(
                dto.WorkOrderId, false, cancellationToken);

            if (!workOrderExists)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.WorkOrderNotFound);
            }

            BuyOrder buyOrderToAdd = _mapper.Map<BuyOrder>(dto);

            if (buyOrderToAdd.ReserveCount != 0)
            {
                await CorrectBuyOrderCountsAsync(buyOrderToAdd, cancellationToken);
            }

            EntityEntry<BuyOrder> entityEntry = await _buyOrders.AddAsync(buyOrderToAdd, cancellationToken);

            if (await SaveChangesAsync(cancellationToken))//await this.SaveChangesAsync()
            {
                return await this.GetResponseBuyOrderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotAdded);
        }

        private async Task CorrectBuyOrderCountsAsync(BuyOrder buyOrder, CancellationToken cancellationToken)
        {
            _logger.LogWarning($"CorrectBuyOrderCountsAsync is not implemented yet");
            // call api // get existing count by EntryCode
            // if existing not 0 , then call api // reserve existings

            //b.BuyCount whole needed
            //b.ReserveCount reserved in api
            //b.RealBuyCount we should buy , BuyCount - existing

            var x = await Task.FromResult<int>(0);
        }

        public async ValueTask<ResponseDto<ResponseUpdateBuyOrderChangeCountDto>> GetBuyOrderChangedCountsAsync(
            UpdateBuyOrderChangeCountDto dto,
            CancellationToken cancellationToken)
        {
            Expression<Func<BuyOrder, bool>> condition = currentBo => !currentBo.Deleted && currentBo.Id == dto.Id;

            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseUpdateBuyOrderChangeCountDto>(NasbNiroMessages.InvalidData);
            }

            bool buyOrderExists = await _buyOrders.Where(condition).AnyAsync();

            if (buyOrderExists is false)
            {
                return ResponseDtoHelper.NotFound<ResponseUpdateBuyOrderChangeCountDto>(NasbNiroMessages.BuyOrderNotFound);
            }
            else
            {
                ResponseUpdateBuyOrderChangeCountDto result = await _buyOrders
                    .Where(condition)
                    .Select(bo => new ResponseUpdateBuyOrderChangeCountDto()
                    {
                        Id = bo.Id,
                        BuyCount = bo.BuyCount,
                        RealBuyCount = bo.RealBuyCount,
                        ReserveCount = bo.ReserveCount
                    }).SingleOrDefaultAsync();

                return ResponseDtoHelper.Ok(result);
            }
        }

        public async BuyOrderResult UpdateBuyOrderAsync(UpdateBuyOrderDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            _mapper.Map(dto, buyOrder);

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                GetBuyOrderDto getBuyOrderDto = entityEntry.Entity;

                return await this.GetResponseBuyOrderDtoAsync(getBuyOrderDto, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderRevertStatusAsync(RevertBuyOrderDto dto, CancellationToken cancellationToken)
        {
            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.BuyOrderId, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            buyOrder.RevertStatus(dto.Status);

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                //GetBuyOrderDto getBuyOrderDto = entityEntry.Entity;

                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));// await this.GetResponseBuyOrderDtoAsync(getBuyOrderDto);
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderFullParametersAsync(UpdateBuyOrderFullParametersDto dto, CancellationToken cancellationToken)
        {

            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            _mapper.Map(dto, buyOrder);

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderInquiriesAddedAsync(UpdateBuyOrderInquiriesAddedDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanAddInquiries())
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanNotAddNewInquires);
            }

            //buyOrder.InquiriesAdded(updateBuyOrderInquiriesAddedDto.OptionalInquiryDate);
            buyOrder.InquiriesAdded(dto.FinanceType);

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderTechnicalScoreAddedAsync(UpdateBuyOrderTechnicalScoreAddedDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanAddTechnicalScore())
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanNotAddTechnicalScore);
            }

            buyOrder.TechnicalScoreAdded();

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper
                .BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderWinnerSelectedAsync(
            UpdateBuyOrderWinnerSelectedDto dto,
            CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken, true);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanSelectWinner())
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanNotAddTechnicalScore);
            }

            if (buyOrder.Inquiries == null ||
                buyOrder.Inquiries.Count() == 0 ||
                !buyOrder.Inquiries.Any(x => x.Id == dto.WinnerInquiryId && x.Deleted == false))
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InquiryNotFound);
            }

            foreach (Inquiry item in buyOrder.Inquiries)
            {
                item.IsAccept = false;
            }

            Inquiry inquiry = buyOrder
                .Inquiries
                .Where(x => x.Id == dto.WinnerInquiryId)
                .First();

            inquiry.IsAccept = true;

            buyOrder.WinnerSelected();


            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderFinanceDocSettedAsync(UpdateBuyOrderFinanceDocumentSettedDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanSetFinanceDocuments())
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanNotSetFinanceDoc);
            }

            buyOrder.FinanceDocumentSetted(dto.OptionalFinanceDocDate);
            _mapper.Map(dto, buyOrder);

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async Task<ResponseDto<ResponseBuyOrderDto>> CommercialBoughtAsync(UpdateBuyOrderCommercialBoughtDto dto, CancellationToken cancellationToken)
        {
            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanCommercialBuy())
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(BuyOrderMessages.CanNotCommercialBuy);
            }

            buyOrder.CommercialBought(dto.RealBuyCount);

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderTempReceiptAddedAsync(UpdateBuyOrderTempReceiptAddedDto dto, CancellationToken cancellationToken)
        {
            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanAddTempReceipt())
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(BuyOrderMessages.CanNotAddTempReceipt);
            }

            buyOrder.AddTempReciept(dto.LogShitId);

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderQcAcceptAsync(UpdateBuyOrderQcAcceptDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            List<BuyOrderFile> buyOrderFiles = await _buyOrderFiles.Where(x=>x.BuyOrderId== dto.Id).ToListAsync();

            if (buyOrderFiles.Count==0)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderQCNotAddedFiles);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanQcAcceptOrReject())
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanNotQcAcceptOrReject);
            }

            buyOrder.QcAccepted();

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderQcRejectAsync(UpdateBuyOrderQcRejectDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanQcAcceptOrReject())
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanNotQcAcceptOrReject);
            }

            buyOrder.QcReject();

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderEngineeringAcceptedAsync(UpdateBuyOrderEngineeringAcceptedDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanEnginneringAcceptOrRejectOrSayNeedsFixes())
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanNotEngineeringAcceptOrReject);
            }

            buyOrder.EngineeringAccept();

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper
                .BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderEngineeringRejectAsync(UpdateBuyOrderEngineeringRejectDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanEnginneringAcceptOrRejectOrSayNeedsFixes())
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanNotEngineeringAcceptOrReject);
            }

            var processesToRevertToReadyToGo =
                await _buyOrderProcesses
                .AsNoTracking()
                .Include(x => x.Process)
                .Select(x => x.Process)
                .Where(x => !x.Deleted)
                .ToListAsync(cancellationToken);

            foreach (ProcessEntity process in processesToRevertToReadyToGo)
            {
                process.UpdateStatus(ProcessStatus.ReadyToGo);
                _processes.Update(process);
            }

            buyOrder.EngineeringReject();

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderPmoAcceptRejectAsync(UpdateBuyOrderEngineeringRejectDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanAcceptReject())
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanPmoAcceptReject);
            }

            buyOrder.PmoAcceptReject();

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderRejectedAsync(UpdateBuyOrderEngineeringRejectDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanReject())
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanPmoAcceptedReject);
            }

            var processesToRevertToReadyToGo =
                await _buyOrderProcesses
                .AsNoTracking()
                .Include(x => x.Process)
                .Select(x => x.Process)
                .Where(x => !x.Deleted)
                .ToListAsync(cancellationToken);

            foreach (ProcessEntity process in processesToRevertToReadyToGo)
            {
                process.UpdateStatus(ProcessStatus.ReadyToGo);
                _processes.Update(process);
            }

            buyOrder.Rejected();

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderEngineeringSaysNeedsFixesAsync(UpdateBuyOrderEngineeringSaysNeedsFixedsDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanEnginneringAcceptOrRejectOrSayNeedsFixes())
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanNotEngineeringAcceptOrReject);
            }
            List<BuyOrderProcesses> buyOrderProcess = buyOrder.BuyOrderProcesses.ToList();
            if (buyOrderProcess.Count > 0)
            {
                int proccessId = buyOrderProcess[0].ProcessId;
                ProcessEntity process = await _processes.Where(x => x.Id == proccessId).FirstOrDefaultAsync();
                if (process.Type == ProcessType.Buy || process.Type == ProcessType.Service)
                    buyOrder.EngineeringSaysNeedsFixes();
                else if (process.Type == ProcessType.Assembly || process.Type == ProcessType.FinalAssembly)
                    buyOrder.EngineeringSaysNeedsAssemblerFixes();
                else if (process.Type == ProcessType.Construction)
                    buyOrder.EngineeringSaysNeedsContractorFixes();
            }

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper
                .BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async BuyOrderResult UpdateBuyOrderFinanceAcceptedAsync(UpdateBuyOrderFinanceAcceptedDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanFinanceAccept())
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderCanNotAcceptFinanceDoc);
            }

            //buyOrder.FinanceAccepted(updateBuyOrderFinanceAcceptedDto.OptionalFinanceAcceptDate);
            buyOrder.FinanceAccepted();

            IEnumerable<ProcessEntity> processes = await _buyOrderProcesses
                .Include(x => x.Process)
                .Where(x => x.BuyOrderId == dto.Id)
                .Select(x => x.Process)
                .Where(x => !x.Deleted)
                .OrderBy(x => x.Sort)
                .ToListAsync(cancellationToken);

            foreach (ProcessEntity process in processes)
            {
                int remainingProcessesCount = await _boms
                    .Where(x => x.Id == process.BomId)
                    .SelectMany(x => x.Processes)
                    .Where(x => !x.Deleted && x.ProcessStatus != ProcessStatus.ProcessIsCompleted)
                    .CountAsync();

                process.Completed();
                _processes.Update(process);

                if (remainingProcessesCount == 1)
                {
                    await this.SaveChangesAsync(cancellationToken);

                    await AddProgressReportAsync(processId: process.Id, cancellationToken: cancellationToken);
                }
            }

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            await this.SaveChangesAsync(cancellationToken);

            GetBuyOrderDto getBuyOrderDto = entityEntry.Entity;
            return await this.GetResponseBuyOrderDtoAsync(getBuyOrderDto, cancellationToken);
        }

        private async Task AddProgressReportAsync(int processId, CancellationToken cancellationToken)
        {
            var _contracts = _unitOfWork.Set<ContractEntity>();
            var progressReports = _unitOfWork.Set<ProgressReport>();

            var data = await (
                    from process in _processes.Where(x => !x.Deleted && x.Id == processId)
                    let workOrder = process.Bom.Assembly.WorkOrder
                    let contract = process.Bom.Assembly.WorkOrder.Contract
                    let processesOfCompany = _contracts
                         .Where(c => !c.Deleted)
                         .SelectMany(c => c.WorkOrders)
                         .Where(wo => !wo.Deleted)
                         .SelectMany(x => x.Assemblies)
                         .Where(asm => !asm.Deleted)
                         .SelectMany(asm => asm.Boms)
                         .Where(bom => !bom.Deleted)
                         .SelectMany(bom => bom.Processes)
                         .Where(p => !p.Deleted)
                    let processesOfContract = workOrder.Contract.WorkOrders
                         .Where(wo => !wo.Deleted)
                         .SelectMany(x => x.Assemblies)
                         .Where(asm => !asm.Deleted)
                         .SelectMany(asm => asm.Boms)
                         .Where(bom => !bom.Deleted)
                         .SelectMany(bom => bom.Processes)
                         .Where(p => !p.Deleted)
                    let processesOfBom = process.Bom.Processes.Where(p => !p.Deleted)
                    let contractId = workOrder.ContractId
                    let workOrderId = workOrder.Id
                    let workOrderTitle = workOrder.Title
                    let actualPrice = workOrder.BuyOrders
                        .Where(x => !x.Deleted && x.Status == BuyOrderStatus.CompletedWarehouseFinallyAccepted)
                        .SelectMany(x => x.Inquiries)
                        .Where(x => x.IsAccept)
                        .Select(x => x.Price)
                        .Sum()
                    let plannedPrice = process.Bom.Assembly.Boms
                        .Where(x => x.Processes.All(p => p.ProgramEndDate <= DateTimeOffset.UtcNow))
                        .SelectMany(x => x.Processes)
                        .Select(x => x.ProgramPrice)
                        .Sum()
                    let workOrderAllBomCount = workOrder.Assemblies
                        .Where(asm => !asm.Deleted)
                        .SelectMany(asm => asm.Boms)
                        .Where(bom => !bom.Deleted)
                        .Count()
                    let workOrderLevelPlannedProgress = workOrderAllBomCount == 0 ? 0 : workOrder.Assemblies
                        .Where(asm => !asm.Deleted)
                        .SelectMany(asm => asm.Boms)
                        .Where(bom => !bom.Deleted && bom.Processes.Where(p => !p.Deleted)
                                .All(p => p.ProgramEndDate != null && p.ProgramEndDate <= DateTimeOffset.UtcNow))
                        .Count() * 100.0D / workOrderAllBomCount
                    let workOrderLevelActualProgress = workOrderAllBomCount == 0 ? 0 : workOrder.Assemblies
                        .Where(asm => !asm.Deleted)
                        .SelectMany(asm => asm.Boms)
                        .Where(bom => !bom.Deleted && bom.Processes.Where(p => !p.Deleted)
                             .All(p => p.ProgramEndDate != null && p.ActualEndDate <= DateTimeOffset.UtcNow))
                        .Count() * 100.0D / workOrderAllBomCount
                    let validProcessesCount = processesOfContract.Count()
                    let contractLevelPlannedProgress = validProcessesCount == 0 ? 0 : processesOfContract
                        .Where(p => p.ProgramEndDate != null && p.ProgramEndDate < DateTimeOffset.UtcNow)
                        .Count() * 100.0D / validProcessesCount
                    let contractLevelActualProgress = validProcessesCount == 0 ? 0 : processesOfContract
                        .Where(p => p.ActualEndDate != null && p.ActualEndDate <= DateTimeOffset.UtcNow)
                        .Count() * 100.0D / validProcessesCount
                    let validCompanyProcessesCount = processesOfCompany.Count()
                    let actualWeightPercentBuyOrder = processesOfBom.Where(p => p.ProcessStatus == ProcessStatus.ProcessIsCompleted)
                        .Select(p => p.WeightPercent).Sum()
                    let plannedWeightPercentBuyOrder = process.Bom.Assembly.Boms
                        .Where(x => x.Processes.All(p => p.ProgramEndDate <= DateTimeOffset.UtcNow))
                        .SelectMany(x => x.Processes)
                        .Select(x => x.WeightPercent)
                        .Sum()
                    let programEndDateOfProcessesOfWorkOrder = workOrder.Assemblies
                        .Where(asm => !asm.Deleted)
                        .SelectMany(asm => asm.Boms)
                        .Where(bom => !bom.Deleted)
                        .SelectMany(bom => bom.Processes)
                        .Where(p => !p.Deleted)
                        .Select(p => p.ProgramEndDate)
                    let contractRemainingDays_helper = (DateTimeOffset.UtcNow - contract.EndDate).TotalDays
                    let contractRemainingDays = contractRemainingDays_helper >= 0
                        ? (int)contractRemainingDays_helper
                        : 0
                    let contractPlannedStartDate = contract.WorkOrders.OrderBy(x => x.Id).Select(x => x.StartDate).First()
                    let contractActualStartDate = contract.WorkOrders.OrderBy(x => x.Id).Select(x => x.StartDate).First()
                    let contractPlannedEndDate = contract.EndDate
                    let contractActualEndtDate = contract.WorkOrders
                        .SelectMany(x => x.Assemblies)
                        .Where(x => !x.Deleted)
                        .Select(x => x.ActualEndDate)
                        .Max()
                    let contractActualPrice = progressReports
                        .Where(x => x.ContractId == contract.Id)
                        .GroupBy(x => x.WorkOrderId)
                        .Select(x => x.OrderByDescending(xx => xx.ReportDate).Select(xx => xx.ActualPrice).FirstOrDefault())
                        .Sum()
                    let contractPlannedPrice = progressReports
                        .Where(x => x.ContractId == contract.Id)
                        .GroupBy(x => x.WorkOrderId)
                        .Select(x => x.OrderByDescending(xx => xx.ReportDate).Select(xx => xx.PlannedPrice).FirstOrDefault())
                        .Sum()
                    let contractActualWeightPercent = progressReports
                        .Where(x => x.ContractId == contract.Id)
                        .GroupBy(x => x.WorkOrderId)
                        .Select(x => x
                            .OrderByDescending(xx => xx.ReportDate)
                            .Select(xx => xx.ActualWeightPercent * xx.WorkOrder.WeightPercent / 100.0M)
                            .FirstOrDefault())
                        .Sum()
                    let contractPlannedWeightPercent = progressReports
                    .Where(x => x.ContractId == contract.Id)
                    .GroupBy(x => x.WorkOrderId)
                    .Select(x => x
                        .OrderByDescending(xx => xx.ReportDate)
                        .Select(xx => xx.PlannedWeightPercent * xx.WorkOrder.WeightPercent / 100.0M)
                        .FirstOrDefault())
                    .Sum()
                    select new
                    {
                        ProcessId = processId,
                        ProcessTitle = process.Title,
                        ContractId = contractId,
                        WorkOrderId = workOrderId,
                        WorkOrderTitle = workOrderTitle,
                        PlannedPrice = plannedPrice,
                        ActualPrice = actualPrice,
                        PlannedStartDate = process.ProgramStartDate,
                        ActualStartDate = process.ActualStartDate,
                        PlannedEndDate = process.ProgramEndDate,
                        ActualEndDate = process.ActualEndDate,
                        ActualWeightPercent = actualWeightPercentBuyOrder,
                        PlannedWeightPercent = plannedWeightPercentBuyOrder,
                        WorkOrderLevelActualProgress = workOrderLevelActualProgress,
                        WorkOrderLevelPlannedProgress = workOrderLevelPlannedProgress,
                        ContractLevelPlannedProgress = contractLevelPlannedProgress,
                        ContractLevelActualProgress = contractLevelActualProgress,
                        ProgramEndDateOfProcessesOfWorkOrderCount = programEndDateOfProcessesOfWorkOrder.Count(),
                        ProgramEndDateOfProcessesOfWorkOrderMax = programEndDateOfProcessesOfWorkOrder.Max(),
                        ContractRemainingDays = contractRemainingDays,
                        ContractPlannedStartDate = contractPlannedStartDate,
                        ContractActualStartDate = contractActualStartDate,
                        ContractPlannedEndDate = contractPlannedEndDate,
                        ContractActualEndtDate = contractActualEndtDate,
                        ContractActualPrice = contractActualPrice,
                        ContractPlannedPrice = contractPlannedPrice,
                        ContractActualWeightPercent = contractActualWeightPercent,
                        ContractPlannedWeightPercent = contractPlannedWeightPercent
                        //WorkOrderRemainingDays = workOrderRemainingDays
                    }).SingleOrDefaultAsync(cancellationToken);

            if (data == null)
            {
                throw new Exception("فرآیندی برای گزارش یافت نشد.");
            }

            int workOrderRemainingDays = data.ProgramEndDateOfProcessesOfWorkOrderCount == 0
                ? 0
                : data.ProgramEndDateOfProcessesOfWorkOrderMax == null
                    ? 0
                    : (int)(DateTimeOffset.UtcNow - data.ProgramEndDateOfProcessesOfWorkOrderMax.Value).TotalDays;

            var report = new ProgressReport()
            {
                ProcessId = data.ProcessId,
                ProcessTitle = data.ProcessTitle,
                ContractId = data.ContractId,
                WorkOrderId = data.WorkOrderId,
                WorkOrderTitle = data.WorkOrderTitle,
                PlannedStartDate = data.PlannedStartDate,
                ActualStartDate = data.ActualStartDate,
                PlannedEndDate = data.PlannedEndDate,
                ActualEndDate = data.ActualEndDate,
                ReportDate = DateTimeOffset.UtcNow,
                PlannedPrice = data.PlannedPrice,
                ActualPrice = data.ActualPrice,
                ContractLevelPlannedProgress = (decimal)data.ContractLevelPlannedProgress,
                ContractLevelActualProgress = (decimal)data.ContractLevelActualProgress,
                WorkOrderLevelPlannedProgress = (decimal)data.WorkOrderLevelPlannedProgress,
                WorkOrderLevelActualProgress = (decimal)data.WorkOrderLevelActualProgress,
                PlannedWeightPercent = data.PlannedWeightPercent,
                ActualWeightPercent = data.ActualWeightPercent,
                WorkOrderRemainingDays = workOrderRemainingDays,
                ContractPlannedStartDate = data.ContractPlannedStartDate,
                ContractActualStartDate = data.ContractActualStartDate,
                ContractPlannedEndDate = data.ContractPlannedEndDate,
                ContractActualEndtDate = data.ContractActualEndtDate,
                ContractActualWeightPercent = data.ContractActualWeightPercent,
                ContractPlannedPrice = data.ContractPlannedPrice,
                ContractActualPrice = data.ContractActualPrice,
                ContractPlannedWeightPercent = data.ContractPlannedWeightPercent,
                ContractRemainingDays = data.ContractRemainingDays
            };

            await progressReports.AddAsync(report, cancellationToken);
            await this.SaveChangesAsync(cancellationToken);
        }

        public async BuyOrderResult DeleteBuyOrderAsync(int buyOrderId, CancellationToken cancellationToken)
        {

            GetBuyOrderDto getBuyOrderDto = buyOrderId;

            BuyOrder buyOrder = await GetBuyOrderAsync(getBuyOrderDto, cancellationToken, true);

            if (buyOrder is null)
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);

            if (buyOrder.Deleted == true)
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderAlreadyIsDeleted);

            buyOrder.Delete();

            if (buyOrder.Inquiries != null)
            {
                foreach (Inquiry inquiry in buyOrder.Inquiries)
                {
                    inquiry.Delete();
                }
            }

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseBuyOrderDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotDeleted);
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteBuyOrderResultDto>>> DeleteBuyOrderCollectionAsync(IEnumerable<int> buyOrderIds, CancellationToken cancellationToken)
        {
            if (buyOrderIds is null)
            {
                return ResponseDtoHelper.BadRequest<IEnumerable<DeleteBuyOrderResultDto>>(NasbNiroMessages.InvalidData);
            }

            List<DeleteBuyOrderResultDto> deleteBuyOrderResultDtos = new List<DeleteBuyOrderResultDto>();

            foreach (int buyOrderId in buyOrderIds)
            {
                bool exception = false;
                ResponseDto<ResponseBuyOrderDto> result = null;

                try
                {
                    result = await this.DeleteBuyOrderAsync(buyOrderId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteBuyOrderResultDtos.Add(new DeleteBuyOrderResultDto()
                    {
                        Id = buyOrderId,
                        Success = false,
                        Message = NasbNiroMessages.BuyOrderDeletingException
                    });

                    continue;
                }

                DeleteBuyOrderResultDto deleteBuyOrderResultDto
                    = new DeleteBuyOrderResultDto() { Id = buyOrderId, Success = result.Success };

                if (!result.Success)
                {
                    deleteBuyOrderResultDto.Message = result.Message;
                }

                deleteBuyOrderResultDtos.Add(deleteBuyOrderResultDto);
            }

            return ResponseDtoHelper
                .Ok(deleteBuyOrderResultDtos.AsEnumerable());
        }

        public async ValueTask<ResponseDto<PagedList<ResponseBuyOrderDto>>> GetAllResponseBuyOrderDtosAsync(
            BuyOrderPaginationParams buyOrderPaginationParams, CancellationToken cancellationToken)
        {
            if (buyOrderPaginationParams is null)
            {
                return ResponseDtoHelper.BadRequest<PagedList<ResponseBuyOrderDto>>(NasbNiroMessages.InvalidData);
            }

            IQueryable<BuyOrder> query = _buyOrdersQueryable;

            if (buyOrderPaginationParams.IsDeleted.HasValue)
            {
                query = query.Where(buyOrder => buyOrder.Deleted == buyOrderPaginationParams.IsDeleted);
            }

            if (!string.IsNullOrEmpty(buyOrderPaginationParams.Contains))
            {
                string[] strArray = buyOrderPaginationParams
                    .Contains.Split(' ');

                query = query.Where(buyOrder =>
                       buyOrder.EnterCommodityCode != null && strArray.Any(str => buyOrder.EnterCommodityCode.Contains(str)) ||
                       buyOrder.ExitCommodityCode != null && strArray.Any(str => buyOrder.ExitCommodityCode.Contains(str)) ||
                       buyOrder.FinanceNumber != null && strArray.Any(str => buyOrder.FinanceNumber.Contains(str)) ||
                       buyOrder.FinanceType != null && strArray.Any(str => buyOrder.FinanceType.Contains(str)));
            }

            query = query.Where(buyOrder =>
                buyOrder.WorkOrderId == buyOrderPaginationParams.WorkOrderId);

            IQueryable<ResponseBuyOrderDto> resultQuery;

            resultQuery = query.ProjectTo<ResponseBuyOrderDto>
                (_mapper.ConfigurationProvider);

            Task<PagedList<ResponseBuyOrderDto>> data =
                PagedList<ResponseBuyOrderDto>
                            .CreateAsync(resultQuery,
                                         buyOrderPaginationParams.PageNumber,
                                         buyOrderPaginationParams.PageSize, cancellationToken);

            return ResponseDtoHelper.Ok(await data);
        }

        public async ValueTask<ResponseDto<IEnumerable<ResponseBuyOrderAndProcesses>>> GetBuyOrdersAndProcessesAsync(int workOrderId, CancellationToken cancellationToken)
        {
            var result = await _buyOrdersQueryable
                .Where(x => x.WorkOrderId == workOrderId)
                .ProjectTo<ResponseBuyOrderAndProcesses>(_mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);
            //List<ResponseBuyOrderAndProcesses> list = result.ToList();
            //for (int i = 0; i < result.Count; i++)
            //{
            //    if (result[i].BuyOrderProcesses.ToList().Count > 0)
            //    {
            //        int proccessId = result[i].BuyOrderProcesses.ToList()[0].ProcessId;
            //        result[i].BuyOrderProcesses.ToList()[0].processFile = await _processesFile.Where(x => x.ProcessId == proccessId).OrderByDescending(x => x.RegDate).FirstOrDefaultAsync();
            //    }
            //}

            return ResponseDtoHelper.Ok(result.AsEnumerable());
        }

        public async BuyOrderResult GetResponseBuyOrderDtoAsync(GetBuyOrderDto getBuyOrderDto, CancellationToken cancellationToken)
        {
            if (getBuyOrderDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            IQueryable<BuyOrder> query = _buyOrdersQueryable.Include(x => x.Inquiries);

            IQueryable<ResponseBuyOrderDto> resultQuery;

            resultQuery = query.ProjectTo<ResponseBuyOrderDto>
                (_mapper.ConfigurationProvider);

            if (getBuyOrderDto.WorkOrderId.HasValue)
            {
                resultQuery = resultQuery.Where(x => x.WorkOrderId == getBuyOrderDto.WorkOrderId.Value);
            }

            ResponseBuyOrderDto result = await resultQuery.SingleOrDefaultAsync(wo =>
                wo.Id == getBuyOrderDto.Id,
                cancellationToken);

            if (result != null)
            {
                return ResponseDtoHelper.Ok(result);
            }

            return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
        }

        public async ValueTask<BuyOrder> GetBuyOrderAsync(
            GetBuyOrderDto getBuyOrderDto,
            CancellationToken cancellationToken,
            bool includeInquiries = false)
        {
            IQueryable<BuyOrder> buyOrderQuery = _buyOrders.Include(x => x.BuyOrderProcesses).AsQueryable();

            if (includeInquiries)
            {
                buyOrderQuery = buyOrderQuery.Include(x => x.Inquiries);
            }

            if (getBuyOrderDto.WorkOrderId.HasValue)
            {
                buyOrderQuery = buyOrderQuery.Where(x => x.WorkOrderId == getBuyOrderDto.WorkOrderId.Value);
            }

            BuyOrder buyOrder = await buyOrderQuery.SingleOrDefaultAsync(x =>
                x.Id == getBuyOrderDto.Id,
                cancellationToken);

            return buyOrder;
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) != 0;
        }

        public async ValueTask<bool> BuyOrderExistsAsync(int buyOrderId, CancellationToken cancellationToken, Expression<Func<BuyOrder, bool>> where = null)
        {
            IQueryable<BuyOrder> query = _buyOrders;

            if (where != null)
            {
                query = query.Where(where);
            }

            bool result = await query.AnyAsync(e => e.Id == buyOrderId, cancellationToken);

            return result;
        }

        public async ValueTask<ResponseDto<string>> AttachFileToBuyOrderAsync(AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken)
        {
            attachFileDto.Validate();

            bool buyOrderExists = await _buyOrders.Where(x => !x.Deleted && x.Id == attachFileDto.EntityId).AnyAsync(cancellationToken);

            if (buyOrderExists is false)
            {
                return ResponseDtoHelper.NotFound<string>(NasbNiroMessages.BuyOrderNotFound);
            }

            //if (!buyOrderToUpdate.CanAttachOrDetachDocuments())
            //{
            //    return ResponseDtoHelper.NotFound<string>("امکان افزودن فایل به این دستور خرید وجود ندارد.");
            //}

            Guid documentId = await _documentService.UploadDocumentAsync(uploadFileDto: attachFileDto);
            BuyOrderFile buyOrderFile = new BuyOrderFile(documentId, attachFileDto.EntityId);


            await _buyOrderFiles.AddAsync(buyOrderFile);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(documentId.ToString());
            }

            return ResponseDtoHelper.BadRequest<string>(NasbNiroMessages.FileNotAdded);
        }

        public async ValueTask<ResponseDto<bool>> DetachFileFromBuyOrderAsync(int buyOrderId, Guid documentId, CancellationToken cancellationToken)
        {

            BuyOrderFile result = await _buyOrderFiles.SingleOrDefaultAsync(x =>
                    x.DocumentId == documentId && x.BuyOrderId == buyOrderId);

            if (result is null)
            {
                return ResponseDtoHelper.NotFound<bool>("فایل دستورخرید یافت نشد.");
            }

            EntityEntry<BuyOrderFile> entityEntry = _buyOrderFiles.Remove(result);

            if (!await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.BadRequest<bool>(NasbNiroMessages.ProcessFileNotDeleted);
            }

            bool deleteDocumentResult = await _documentService.DeleteDocumentAsync(documentId);

            if (!deleteDocumentResult)
            {
                return ResponseDtoHelper.BadRequest<bool>(NasbNiroMessages.DocumentNotDeleted);
            }

            return ResponseDtoHelper.Ok(true, message: "فایل دستور خرید حذف شد.");
        }

        public async Task<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderAssemblerAssembledAsync(
            UpdateBuyOrderAssemblerAssembled dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanAssemblerAssemble())
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>("امکان مونتاژ با توجه به وضعیت دستور خرید وجود ندارد.");
            }

            buyOrder.TimeSheet = dto.TimeSheet;
            buyOrder.AssemblerAssembled();

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async Task<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderConstructorConstructedAsync(
            UpdateBuyOrderConstructorConstructed dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanConstructorConstructe())
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>("امکان تولید با توجه به وضعیت دستور خرید وجود ندارد.");
            }

            buyOrder.ConstructorConstructed();
            buyOrder.MachineId = dto.MachineId;
            buyOrder.TimeSheet = dto.TimeSheet;
            buyOrder.ConstractorDescription = dto.ConstractorDescription;

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }

        public async Task<ResponseDto<ResponseBuyOrderDto>> UpdateBuyOrderWarehouseFinallyAcceptedAsync(
            UpdateBuyOrderWarehouseFinallyAccepted dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.InvalidData);
            }

            BuyOrder buyOrder = await this.GetBuyOrderAsync(dto.Id, cancellationToken);

            if (buyOrder is null || buyOrder.Deleted)
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotFound);
            }

            if (!buyOrder.CanWarehouseFinallyAccept())
            {
                return ResponseDtoHelper.NotFound<ResponseBuyOrderDto>("امکان صدور رسید قطعی وجود ندارد.");
            }

            buyOrder.WarehouseFinallyAccepted();

            EntityEntry<BuyOrder> entityEntry = _buyOrders.Update(buyOrder);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseBuyOrderDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseBuyOrderDto>(NasbNiroMessages.BuyOrderNotUpdated);
        }
    }
}
