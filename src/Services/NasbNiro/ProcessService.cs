using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Text;
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
using Services.Contracts;
using Services.Contracts.Identity;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.DateTimeService;
using Services.NasbNiro.Extensions;
using Services.NasbNiro.WorkOrders;
using Services.Resources;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcessDtos;
using ViewModels.NasbNiro.Wrokflow;

namespace Services.NasbNiro
{
    public class ProcessService : IProcessService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IBomService _bomService;
        private readonly ILogger<ProcessService> _logger;
        private readonly IMapper _mapper;
        private readonly IDocumentService _documentService;
        private readonly IApplicationUserManager _userManager;
        private readonly IWorkOrderService _workOrderService;
        private readonly IWorkflowStationService _workflowStationService;
        private readonly DbSet<ProcessEntity> _processs;
        private readonly DbSet<ProcessFile> _processFiles;
        private readonly IDateTimeService _dateTimeService;
        private readonly global::Services.NasbNiro.Production.IMfDraftingService _productionDrafting;

        private IQueryable<ProcessEntity> _processsQueryable
            => _processs.AsNoTracking().AsQueryable();

        public ProcessService(IUnitOfWork unitOfWork,
                            IBomService bomService,
                            ILogger<ProcessService> logger,
                            IMapper mapper,
                            IDocumentService documentService,
                            IApplicationUserManager userManager,
                            IWorkOrderService workOrderService,
                            IWorkflowStationService workflowStationService,
                            IDateTimeService dateTimeService,
                            global::Services.NasbNiro.Production.IMfDraftingService productionDrafting = null)
        {
            _productionDrafting = productionDrafting;
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _bomService = bomService.ThrowIfNull(nameof(bomService));
            _processs = _unitOfWork.Set<ProcessEntity>().ThrowIfNull(nameof(_processs));
            _processFiles = _unitOfWork.Set<ProcessFile>().ThrowIfNull(nameof(_processFiles));
            _logger = logger.ThrowIfNull(nameof(logger));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _documentService = documentService.ThrowIfNull(nameof(documentService));
            _userManager = userManager.ThrowIfNull(nameof(userManager));
            _workOrderService = workOrderService.ThrowIfNull(nameof(workOrderService));
            _workflowStationService = workflowStationService.ThrowIfNull(nameof(workOrderService));
            _dateTimeService = dateTimeService;
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>>
            AddProcessAsync(AddProcessDto addProcessDto, CancellationToken cancellationToken)
        {
            if (addProcessDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.InvalidData);
            }


            if (!ValidateInputDto(addProcessDto, out string message))
            {
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(message);
            }

            bool bomExists = await _bomService.BomExistsAsync(addProcessDto.BomId, cancellationToken, false);

            if (!bomExists)
            {
                return ResponseDtoHelper.NotFound<ResponseProcessDto>(NasbNiroMessages.BomNotFound);
            }

            ProcessEntity processToAdd = _mapper.Map<ProcessEntity>(addProcessDto);

            EntityEntry<ProcessEntity> entityEntry = await _processs.AddAsync(processToAdd, cancellationToken);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                entityEntry.Entity.Sort = entityEntry.Entity.Id;
                await this.SaveChangesAsync(cancellationToken);

                return await this.GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>>
            UpdateProcessAsync(UpdateProcessDto updateProcessDto, CancellationToken cancellationToken)
        {
            if (updateProcessDto is null)
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.InvalidData);

            if (!ValidateInputDto(updateProcessDto, out string message))
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(message);

            ProcessEntity process =
                await this.GetProcessByIdAsync(updateProcessDto.Id, cancellationToken);

            if (process is null || process.Deleted)
                return ResponseDtoHelper
                    .NotFound<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);

            if (process.BomId != updateProcessDto.BomId)
                return ResponseDtoHelper
                    .BadRequest<ResponseProcessDto>
                        (NasbNiroMessages.BomProcessNotMatched);

            _mapper.Map(updateProcessDto, process);

            EntityEntry<ProcessEntity> entityEntry =
                _processs.Update(process);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>>
            RevertStatusAsync(RevertProcessDto revertProcessDto, CancellationToken cancellationToken)
        {
            ProcessEntity process = await this.GetProcessByIdAsync(revertProcessDto.ProcessId, cancellationToken);

            if (process is null || process.Deleted)
                return ResponseDtoHelper
                    .NotFound<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);

            process.UpdateStatus(revertProcessDto.Status);

            EntityEntry<ProcessEntity> entityEntry =
                _processs.Update(process);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>>
           ChangeUserAsync(UpdateProcessSelectUserDto updateProcessSelectUserDto, CancellationToken cancellationToken)
        {
            if (updateProcessSelectUserDto is null)
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.InvalidData);

            //ProcessEntity process = await _processs.SingleOrDefaultAsync(
            //    x => x.Id == updateProcessSelectUserDto.Id && !x.Deleted,
            //    cancellationToken);
            ProcessEntity process = await _processs.Where(x => x.Id == updateProcessSelectUserDto.Id).FirstOrDefaultAsync();

            if (process is null)
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);

            //if (!await _userManager.Users
            //    .AnyAsync(x => x.Id == updateProcessSelectUserDto.UserId && !x.IsDeleted, cancellationToken))
            //    return ResponseDtoHelper.BadRequest<ResponseProcessDto>(AccountServiceMessages.UserNotFound);

            //if (!process.CanChangeUser())
            //    return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessCanNotChangeUser);

            List<WorkflowStationViewModel> workflowStations = await _workflowStationService.GetByWorkflowGroupId(process.WorkflowId);

            process.SetWorkflowPositionId(workflowStations[0].Id, workflowStations[0].UserId);

            process.EngineerWorkflowStart();

            EntityEntry<ProcessEntity> entityEntry =
                _processs.Update(process);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>>
           UpdateProcessFullParametersAsync(UpdateProcessFullParametersDto updateProcessFullParametersDto, CancellationToken cancellationToken)
        {
            if (updateProcessFullParametersDto is null)
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.InvalidData);

            if (!ValidateInputDto(updateProcessFullParametersDto, out string message))
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(message);

            ProcessEntity process =
                await this.GetProcessByIdAsync(updateProcessFullParametersDto.Id, cancellationToken);

            if (process is null || process.Deleted)
                return ResponseDtoHelper
                    .NotFound<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);

            if (process.BomId != updateProcessFullParametersDto.BomId)
                return ResponseDtoHelper
                    .BadRequest<ResponseProcessDto>
                        (NasbNiroMessages.BomProcessNotMatched);

            _mapper.Map(updateProcessFullParametersDto, process);

            EntityEntry<ProcessEntity> entityEntry =
                _processs.Update(process);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>> PmoAcceptProcessAsync(
            UpdateProcessPmoAcceptDto updateProcessPmoAcceptDto,
            CancellationToken cancellationToken)
        {
            if (updateProcessPmoAcceptDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.InvalidData);
            }

            ProcessEntity processToUpdate = await GetProcessByIdAsync(
                updateProcessPmoAcceptDto.Id, cancellationToken);
            if (processToUpdate.WorkflowId == null || processToUpdate.WorkflowId == 0)
            {
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.WorkflowNotSelected);
            }

            if (processToUpdate is null || processToUpdate.Deleted)
            {
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);
            }

            if (!processToUpdate.CanPmoAccept())
            {
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessPmoCanNotAccept);
            }

            if (!processToUpdate.ProgramStartDate.HasValue)
            {
                await SetProcessProgramStartDateEndDateAsync(bomId: processToUpdate.BomId, cancellationToken: cancellationToken);
            }

            processToUpdate.PmoAccepted();

            EntityEntry<ProcessEntity> entityEntry = _processs.Update(processToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotUpdated);
            //return new ResponseDto<ResponseProcessDto>(true, "", new ResponseProcessDto());
        }

        private async Task SetProcessProgramStartDateEndDateAsync(int bomId, CancellationToken cancellationToken)
        {
            var processes = await _processs.Where(x => !x.Deleted && x.BomId == bomId).ToArrayAsync();

            for (int i = 0; i < processes.Length; i++)
            {
                if (!processes[i].ProgramStartDate.HasValue)
                {
                    processes[i].ProgramStartDate = DateTimeOffset.UtcNow;
                }

                if (!processes[i].ProgramEndDate.HasValue)
                {
                    int totalDuration = processes[i].MasterDuration + processes[i].MapDuration + processes[i].ProcessDuration;
                    DateTimeOffset programEndDate = DateTimeOffset.UtcNow;

                    for (int j = 0; j < totalDuration; j++)
                    {
                        programEndDate = await _dateTimeService.GetNextValidDateAsync(programEndDate);
                    }

                    processes[i].ProgramEndDate = programEndDate;
                }

                _processs.Update(processes[i]);
            }

            await SaveChangesAsync(cancellationToken);
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>> AssignedToUserAsync(
            UpdateProcessAssignedToUserDto updateProcessAssignedToUserDto, CancellationToken cancellationToken)
        {
            if (updateProcessAssignedToUserDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProcessDto>(NasbNiroMessages.InvalidData);

            ProcessEntity processToUpdate =
                await GetProcessByIdAsync(updateProcessAssignedToUserDto.Id, cancellationToken);

            if (processToUpdate is null || processToUpdate.Deleted)
                return ResponseDtoHelper
                    .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);


            if (!processToUpdate.CanBeAssignedToUser())
                return ResponseDtoHelper
                   .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessPmoCanNotAccept);

            processToUpdate.WorkflowStart();

            EntityEntry<ProcessEntity> entityEntry =
                _processs.Update(processToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>>
            EngineerAcceptDocumentsAsync(UpdateProcessEngineerAcceptDocumentsDto updateProcessEngineerAcceptDocumentsDto, CancellationToken cancellationToken)
        {
            if (updateProcessEngineerAcceptDocumentsDto is null)
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.InvalidData);

            ProcessEntity processToUpdate = await GetProcessByIdAsync(
                updateProcessEngineerAcceptDocumentsDto.Id, cancellationToken);

            if (processToUpdate is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);

            if (!processToUpdate.CanEngineerAcceptDocument())
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessAcceptDocumentsImpossible);

            processToUpdate.EngineerAcceptDouments();

            EntityEntry<ProcessEntity> entityEntry =
                _processs.Update(processToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>>
            EngineeringAcceptDocumentsAsync(UpdateProcessEngineeringAcceptDocumentsDto updateProcessAcceptDocumentsDto, CancellationToken cancellationToken)
        {
            if (updateProcessAcceptDocumentsDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProcessDto>(NasbNiroMessages.InvalidData);

            ProcessEntity processToUpdate =
                await GetProcessByIdAsync(updateProcessAcceptDocumentsDto.Id, cancellationToken);

            if (processToUpdate is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);

            if (!processToUpdate.CanEngineeringAcceptDocument())
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessAcceptDocumentsImpossible);

            processToUpdate.EngineeringAcceptDouments();

            EntityEntry<ProcessEntity> entityEntry =
                _processs.Update(processToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                await DraftProductionOrderAsync(entityEntry.Entity, cancellationToken);
                return await GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);

            }

            return ResponseDtoHelper
                .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotUpdated);
            //return new ResponseDto<ResponseProcessDto>(true, "", new ResponseProcessDto());
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>>
            EngineeringNotAcceptDocumentsAsync(UpdateProcessEngineeringAcceptDocumentsDto updateProcessAcceptDocumentsDto, CancellationToken cancellationToken)
        {
            if (updateProcessAcceptDocumentsDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProcessDto>(NasbNiroMessages.InvalidData);

            ProcessEntity processToUpdate =
                await GetProcessByIdAsync(updateProcessAcceptDocumentsDto.Id, cancellationToken);

            if (processToUpdate is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);

            if (!processToUpdate.CanEngineeringAcceptDocument())
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessAcceptDocumentsImpossible);

            processToUpdate.EngineeringNotAcceptDouments();

            EntityEntry<ProcessEntity> entityEntry =
                _processs.Update(processToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);

            }

            return ResponseDtoHelper
                .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotUpdated);
            //return new ResponseDto<ResponseProcessDto>(true, "", new ResponseProcessDto());
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>> MasterAcceptProcessAsync(
            UpdateProcessMasterAcceptDto updateProcessMasterAcceptDto,
            CancellationToken cancellationToken)
        {
            if (updateProcessMasterAcceptDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseProcessDto>(NasbNiroMessages.InvalidData);

            ProcessEntity processToUpdate =
                await GetProcessByIdAsync(updateProcessMasterAcceptDto.Id, cancellationToken);

            if (processToUpdate is null || processToUpdate.Deleted)
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);

            if (!processToUpdate.CanMasterAccept())
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>("امکان تایید توسط کارفرما وجود ندارد.");

            processToUpdate.UpdateMasterAcceptDate();

            EntityEntry<ProcessEntity> entityEntry = _processs.Update(processToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                await DraftProductionOrderAsync(entityEntry.Entity, cancellationToken);
                return await GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper
                .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>> DeleteProcessAsync
            (int processId, CancellationToken cancellationToken)
        {
            ProcessEntity process = await GetProcessByIdAsync(processId, cancellationToken);

            if (process is null)
                return ResponseDtoHelper
                    .NotFound<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);

            if (process.Deleted == true)
                return ResponseDtoHelper
                    .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessAlreadyIsDeleted);

            // R1a2 (M3): a process that a production order or the schedule (a WBS node) refers to is not deleted; the collection delete comes through here too
            string blocked = await LegacyDeleteGuard.ProcessAsync(_unitOfWork, processId, cancellationToken);
            if (blocked != null)
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(blocked);

            process.Delete();

            EntityEntry<ProcessEntity> entityEntry = _processs.Update(process);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseProcessDtoAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseProcessDto>(NasbNiroMessages.ProcessNotDeleted);
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteProcessResultDto>>>
            DeleteProcessCollectionAsync(IEnumerable<int> processIds, CancellationToken cancellationToken)
        {
            if (processIds is null)
                return ResponseDtoHelper.BadRequest<IEnumerable<DeleteProcessResultDto>>(NasbNiroMessages.InvalidData);

            List<DeleteProcessResultDto> deleteProcessResultDtos = new List<DeleteProcessResultDto>();

            foreach (int processId in processIds)
            {
                bool exception = false;
                ResponseDto<ResponseProcessDto> result = null;

                try
                {
                    result = await this.DeleteProcessAsync(processId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteProcessResultDtos.Add(new DeleteProcessResultDto()
                    {
                        Id = processId,
                        Success = false,
                        Message = NasbNiroMessages.ProcessDeletingException
                    });

                    continue;
                }

                DeleteProcessResultDto deleteProcessResultDto
                    = new DeleteProcessResultDto() { Id = processId, Success = result.Success };

                if (!result.Success)
                {
                    deleteProcessResultDto.Message = result.Message;
                }

                deleteProcessResultDtos.Add(deleteProcessResultDto);
            }

            return ResponseDtoHelper
                .Ok(deleteProcessResultDtos.AsEnumerable());
        }

        public async ValueTask<ResponseDto<PagedList<ResponseProcessDto>>>
            GetAllResponseProcessDtoAsync(ProcessPaginationParams processPaginationParams, CancellationToken cancellationToken)
        {

            if (processPaginationParams is null)
                return ResponseDtoHelper
                    .BadRequest<PagedList<ResponseProcessDto>>(NasbNiroMessages.InvalidData);

            IQueryable<ProcessEntity> query = _processsQueryable;

            if (processPaginationParams.IsDeleted.HasValue)
                query = query.Where(process =>
                        process.Deleted == processPaginationParams.IsDeleted.Value);

            if (!string.IsNullOrEmpty(processPaginationParams.Contains))
            {
                string[] strArray = processPaginationParams.Contains.Split(' ');

                query = query.Where(x =>
                    x.EnterCommodityCode != null && strArray.Any(str => x.EnterCommodityCode.Contains(str)) ||
                    x.ExitCommodityCode != null && strArray.Any(str => x.ExitCommodityCode.Contains(str)));
            }

            if (processPaginationParams.WithStatuses != null &&
                processPaginationParams.WithStatuses.Count() > 0)
            {
                query = query.Where(x =>
                    processPaginationParams.WithStatuses.Any(s => s == (int)x.ProcessStatus));
            }

            query = query.Where(equipmentProcess => equipmentProcess.BomId ==
                processPaginationParams.BomId);

            query = query.OrderBy(p => p.Sort);

            IQueryable<ResponseProcessDto> resultQuery;

            resultQuery = query.ProjectTo<ResponseProcessDto>(_mapper.ConfigurationProvider);
            Console.WriteLine("1");
            Task<PagedList<ResponseProcessDto>> data =
                PagedList<ResponseProcessDto>
                            .CreateAsync(resultQuery,
                                         processPaginationParams.PageNumber,
                                         processPaginationParams.PageSize,
                                         CancellationToken.None);
            Console.WriteLine("2");
            var resolvedData = await data;
            Console.WriteLine("3");
            return ResponseDtoHelper.Ok(resolvedData);
        }

        public async ValueTask<ResponseDto<UpdateProcessSortResponseDto>> UpdateSortAsync(
            UpdateProcessSortDto updateProcessSortDto, CancellationToken cancellationToken)
        {
            if (updateProcessSortDto is null)
                return ResponseDtoHelper.BadRequest<UpdateProcessSortResponseDto>
                        (NasbNiroMessages.InvalidData);

            ProcessEntity p1 =
                await this.GetProcessByIdAsync(updateProcessSortDto.Id1, cancellationToken);

            ProcessEntity p2 =
                await this.GetProcessByIdAsync(updateProcessSortDto.Id2, cancellationToken);

            if (p1 is null)
                return ResponseDtoHelper.NotFound<UpdateProcessSortResponseDto>
                    (NasbNiroMessages.ProcessNotFound + $" ID: {updateProcessSortDto.Id1}");

            if (p2 is null)
                return ResponseDtoHelper.NotFound<UpdateProcessSortResponseDto>
                    (NasbNiroMessages.ProcessNotFound + $" ID: {updateProcessSortDto.Id2}");

            int temp = p1.Sort;
            p1.Sort = p2.Sort;
            p2.Sort = temp;

            EntityEntry<ProcessEntity> ee1 = _processs.Update(p1);
            EntityEntry<ProcessEntity> ee2 = _processs.Update(p2);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(
                        new UpdateProcessSortResponseDto()
                        {
                            Sort1 = ee1.Entity.Sort,
                            Sort2 = ee2.Entity.Sort
                        });
            }

            return ResponseDtoHelper.BadRequest<UpdateProcessSortResponseDto>
                (NasbNiroMessages.UpdateFailed);
        }

        public async ValueTask<ProcessEntity> GetProcessByIdAsync(int ProcessId, CancellationToken cancellationToken)
        {
            ProcessEntity process = await _processsQueryable
               .SingleOrDefaultAsync(ea => ea.Id == ProcessId, cancellationToken);

            return process;
        }

        public async ValueTask<ResponseDto<ResponseProcessDto>> GetResponseProcessDtoAsync(GetProcessDto getProcessDto, CancellationToken cancellationToken)
        {
            if (getProcessDto is null)
                return ResponseDtoHelper.BadRequest<ResponseProcessDto>(NasbNiroMessages.InvalidData);

            IQueryable<ResponseProcessDto> query;

            query = _processsQueryable.ProjectTo<ResponseProcessDto>(_mapper.ConfigurationProvider);

            ResponseProcessDto result = await query
                .SingleOrDefaultAsync(
                    p => p.Id == getProcessDto.Id && p.BomId == getProcessDto.BomId,
                    cancellationToken);

            if (result != null)
                return ResponseDtoHelper.Ok(result);

            return ResponseDtoHelper
                .NotFound<ResponseProcessDto>(NasbNiroMessages.ProcessNotFound);
        }

        public bool ValidateInputDto(ProcessDtoBase processDtoBase, out string message)
        {
            if (processDtoBase is null)
            {
                throw new ArgumentNullException(nameof(processDtoBase));
            }

            StringBuilder @string = new StringBuilder();
            bool result;

            switch (processDtoBase.Type)
            {
                case ProcessType.Buy:
                    if (string.IsNullOrWhiteSpace(processDtoBase.EnterCommodityCode))
                    {
                        @string.AppendLine("کد ورودی نامعتبر است");
                    }
                    break;
                case ProcessType.Service:
                    if (string.IsNullOrWhiteSpace(processDtoBase.ExitCommodityCode))
                    {
                        @string.AppendLine("کد خروجی نامعتبر است");
                    }
                    if (string.IsNullOrWhiteSpace(processDtoBase.EnterCommodityCode))
                    {
                        @string.AppendLine("کد ورودی نامعتبر است");
                    }
                    break;
                case ProcessType.Assembly:
                    break;
                case ProcessType.FinalAssembly:
                    break;
                case ProcessType.Construction:
                    break;
                default:
                    @string.AppendLine("مقدار type پشتیبانی نمیشود");
                    break;
            }

            message = @string.ToString();
            result = string.IsNullOrWhiteSpace(message);

            return result;
        }

        public async ValueTask<ResponseDto<string>> AttachFileToProcessAsync(AttachfileToProcessDto attachFileDto, CancellationToken cancellationToken)
        {
            attachFileDto.Validate();

            ProcessEntity processToUpdate = await _processs.Where(x => x.Id == attachFileDto.ProcessId).FirstOrDefaultAsync();


            if (processToUpdate is null)
                return ResponseDtoHelper
                       .NotFound<string>(NasbNiroMessages.ProcessNotFound);

            if (!processToUpdate.CanAttachOrDetachDocuments())
                return ResponseDtoHelper
                      .NotFound<string>("امکان افزودن فایل به این فرآیند وجود ندارد.");

            ProcessFile lastProccessFile = await _processFiles.Where(x => x.ProcessId == attachFileDto.ProcessId).OrderByDescending(x => x.RegDate).FirstOrDefaultAsync();

            switch (attachFileDto.UploadCondition)
            {
                case ProcessFileType.Forward:
                    if (lastProccessFile == null)
                    {
                        attachFileDto.Major = 0;
                        attachFileDto.Minor = 0;
                    }
                    else
                    {
                        attachFileDto.Major = lastProccessFile.Major;
                        attachFileDto.Minor = lastProccessFile.Minor + 1;
                    }
                    break;
                case ProcessFileType.Undo:
                    if (lastProccessFile == null)
                    {
                        attachFileDto.Major = 0;
                        attachFileDto.Minor = 0;
                    }
                    else
                    {
                        attachFileDto.Major = lastProccessFile.Major + 1;
                        attachFileDto.Minor = 0;
                    }
                    break;
            }

            ProcessFile processFile = new ProcessFile()
            {
                DocumentId = attachFileDto.DocumentId,
                Description = attachFileDto.Description,
                ProcessId = attachFileDto.ProcessId,
                Major = attachFileDto.Major,
                Minor = attachFileDto.Minor,
                RegDate = DateTime.Now,
                UserId = _userManager.GetCurrentUser().Id,
                Issue = 0
            };


            await _processFiles.AddAsync(processFile, cancellationToken);

            await _unitOfWork.SaveChangesAsync();

            List<WorkflowStationViewModel> workflowStations = await _workflowStationService.GetByWorkflowGroupId(processToUpdate.WorkflowId);
            WorkflowStationViewModel workflowStation = workflowStations.Where(x => x.Id == processToUpdate.WorkflowPositionId).FirstOrDefault();
            int index = workflowStations.IndexOf(workflowStation);
            switch (attachFileDto.UploadCondition)
            {
                case ProcessFileType.Forward:
                    if (index == workflowStations.Count - 1)
                    {
                        processToUpdate.WorkflowPositionId = 0;
                        processToUpdate.WorkflowUserId = 0;
                        processToUpdate.EngineerAcceptDouments();
                    }
                    else
                    {
                        processToUpdate.WorkflowPositionId = workflowStations[index + 1].Id;
                        processToUpdate.WorkflowUserId = workflowStations[index + 1].UserId;
                        processFile.Minor++;
                    }
                    break;
                case ProcessFileType.Undo:
                    if (index > 0)
                    {
                        processToUpdate.WorkflowPositionId = workflowStations[index - 1].Id;
                        processToUpdate.WorkflowUserId = workflowStations[index - 1].UserId;
                        processFile.Major++;
                        processFile.Minor = 0;
                    }
                    break;
            }
            await _unitOfWork.SaveChangesAsync();
            return ResponseDtoHelper.Ok(processFile.Id.ToString());
        }

        public async ValueTask<ResponseDto<bool>> DetachFileFromProcessAsync(int processId, Guid documentId, CancellationToken cancellationToken)
        {
            ProcessEntity process = await GetProcessByIdAsync(processId, cancellationToken);

            if (process is null || process.Deleted)
                return ResponseDtoHelper
                     .NotFound<bool>(NasbNiroMessages.ProcessNotFound);

            if (!process.CanAttachOrDetachDocuments())
                return ResponseDtoHelper
                      .BadRequest<bool>("امکان حذف فایل به دلیل وضعیت فرآیند فراهم نمیباشد.");

            ProcessFile result =
                await _processFiles.SingleOrDefaultAsync(
                    processFile => processFile.DocumentId == documentId && processFile.ProcessId == processId,
                    cancellationToken);

            if (result is null)
            {
                return ResponseDtoHelper.NotFound<bool>(NasbNiroMessages.ProcessFileNotFound);
            }

            EntityEntry<ProcessFile> entityEntry = _processFiles.Remove(result);

            if (!await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.BadRequest<bool>(NasbNiroMessages.ProcessFileNotDeleted);
            }

            bool deleteDocumentResult = await _documentService.DeleteDocumentAsync(documentId);

            if (!deleteDocumentResult)
                return ResponseDtoHelper.BadRequest<bool>(NasbNiroMessages.DocumentNotDeleted);

            return ResponseDtoHelper.Ok(true, message: NasbNiroMessages.ProcessFileDeleted);
        }

        private async ValueTask<bool> ProcessExistsAsync(int processId, CancellationToken cancellationToken)
        {
            bool result = await _processs.AnyAsync(e => e.Id == processId, cancellationToken);

            return result;
        }

        /// <summary>A make process that is ready to go gets its production order drafted (people release it). Never fails the caller.</summary>
        private async Task DraftProductionOrderAsync(ProcessEntity process, CancellationToken cancellationToken)
        {
            if (_productionDrafting == null || process == null || process.ProcessStatus != ProcessStatus.ReadyToGo) return;
            await _productionDrafting.DraftForProcessAsync(process.Id, cancellationToken);
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }

        public async ValueTask<bool> ProcessExistsAsync(int processId, CancellationToken cancellationToken, Expression<Func<ProcessEntity, bool>> where = null)
        {
            IQueryable<ProcessEntity> query = _processs;

            if (where != null)
            {
                query = query.Where(where);
            }

            bool result = await query.AnyAsync(x => x.Id == processId, cancellationToken);

            return result;
        }

        public async Task<ResponseDto<string>> FinalizeDocumentAsync(FinalizeProcessFileDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<string>("ورودی نامعتبر");
            }

            ProcessFile processFile = await _processFiles.SingleOrDefaultAsync(
                x => x.ProcessId == dto.ProcessId && x.DocumentId == dto.DocumentId,
                cancellationToken);

            if (processFile is null)
            {
                return ResponseDtoHelper.NotFound<string>("فایل یافت نشد");
            }

            //processFile.IsFinal = true;
            _processFiles.Update(processFile);

            await SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper.Ok("فایل نهایی شد.");
        }

        public async Task<ResponseDto<IEnumerable<ResponseProcessFileStatusDto>>> GetProcessesFileStatusAsync(int bomId, CancellationToken cancellationToken)
        {
            IEnumerable<ResponseProcessFileStatusDto> result = await _processFiles
                .Where(x => !x.Process.Deleted && x.Process.BomId == bomId)
                .ProjectTo<ResponseProcessFileStatusDto>(_mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);

            if (result is null)
            {
                return ResponseDtoHelper.BadRequest<IEnumerable<ResponseProcessFileStatusDto>>(
                    Messages.Notfound);
            }

            return ResponseDtoHelper.Ok(result);
        }
    }
}
