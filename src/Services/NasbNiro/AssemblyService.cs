using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
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
using Services.NasbNiro.WorkOrders;
using Services.Resources;
using ViewModels.NasbNiro.AssemblyDtos;
using ViewModels.NasbNiro.Pagination;
using AssemblyDeleteCollectionResult = System.Threading.Tasks.ValueTask<
    Common.Results.ResponseDto<
        System.Collections.Generic.IEnumerable<
            ViewModels.NasbNiro.AssemblyDtos.DeleteAssemblyResultDto>>>;
using AssemblyPagedResult = System.Threading.Tasks.ValueTask<
    Common.Results.ResponseDto<
        ViewModels.NasbNiro.Pagination.PagedList<
            ViewModels.NasbNiro.AssemblyDtos.ResponseAssemblyDto>>>;
using AssemblyResult = System.Threading.Tasks.ValueTask<Common.Results.ResponseDto<ViewModels.NasbNiro.AssemblyDtos.ResponseAssemblyDto>>;

namespace Services.NasbNiro
{
    public class AssemblyService : IAssemblyService
    {
        #region Fields
        private readonly IUnitOfWork _unitOfWork;
        private readonly IWorkOrderService _workOrderService;
        private readonly ILogger<AssemblyService> _logger;
        private readonly IMapper _mapper;
        private readonly DbSet<AssemblyEntity> _assemblies;

        private IQueryable<AssemblyEntity> _assembliesQueryable =>
            _assemblies.AsNoTracking().AsQueryable();
        #endregion

        #region Ctor
        public AssemblyService(IUnitOfWork unitOfWork,
                                        IWorkOrderService workOrderService,
                                        ILogger<AssemblyService> logger,
                                        IMapper mapper)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _workOrderService = workOrderService.ThrowIfNull(nameof(workOrderService));
            _logger = logger.ThrowIfNull(nameof(logger));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _assemblies = _unitOfWork.Set<AssemblyEntity>().ThrowIfNull(nameof(_assemblies));
        }
        #endregion

        #region SetPredecessorAsync
        public async AssemblyResult SetPredecessorAsync(UpdateAssemblySetPredecessorDto dto, CancellationToken cancellationToken)
        {
            if (dto.Id == dto.PredecessorId)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.PredecessorCanNotBeTheSame);
            }

            AssemblyEntity assemblyToUpdate = await _assemblies
                .SingleOrDefaultAsync(x =>
                    !x.Deleted &&
                    x.Id == dto.Id, cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.NotFound<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            AssemblyEntity predecessorAssembly = await _assemblies
                .SingleOrDefaultAsync(x =>
                    !x.Deleted &&
                    x.Id == dto.PredecessorId &&
                    x.WorkOrderId == assemblyToUpdate.WorkOrderId, cancellationToken);

            if (predecessorAssembly is null)
            {
                return ResponseDtoHelper.NotFound<ResponseAssemblyDto>(AssemblyMessages.PredecessorNotFound);
            }

            if (assemblyToUpdate.PredecessorId.HasValue && assemblyToUpdate.PredecessorId.Value == predecessorAssembly.Id)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(BomMessages.PredecessorIsAlreadySetted);
            }

            (bool isValid, string messsage) predecessorValidationResult = await IsValidPredecessorAsync(
                assemblyId: assemblyToUpdate.Id,
                predecessorId: predecessorAssembly.Id,
                cancellationToken: cancellationToken);

            if (!predecessorValidationResult.isValid)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(predecessorValidationResult.messsage);
            }

            assemblyToUpdate.UpdatePredecessor(predecessorAssembly);

            EntityEntry<AssemblyEntity> entityEntry = _assemblies.Update(assemblyToUpdate);

            if (await SaveChangesAsync(cancellationToken))
            {
                return await GetResponseAssemblyDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.PredecessorNotUpdated);
        }

        private async Task<(bool, string)> IsValidPredecessorAsync(int assemblyId, int predecessorId, CancellationToken cancellationToken)
        {
            // cycle guard: a link to itself is a loop, and a visited set stops the walk if the stored data already contains a loop
            if (predecessorId == assemblyId) return (false, AssemblyMessages.PredecessorCausesLoop);
            var visited = new System.Collections.Generic.HashSet<int>();
            while (visited.Add(predecessorId))
            {
                int? predecessorOfPredecessor =
                    await _assemblies
                   .Where(x => !x.Deleted && x.Id == predecessorId)
                   .Select(x => x.PredecessorId)
                   .SingleOrDefaultAsync(cancellationToken);

                if (!predecessorOfPredecessor.HasValue) return (true, null);
                if (predecessorOfPredecessor.Value == assemblyId) return (false, AssemblyMessages.PredecessorCausesLoop);

                predecessorId = predecessorOfPredecessor.Value;
            }
            return (false, AssemblyMessages.PredecessorCausesLoop);
        }
        #endregion

        #region DeletePredecessorAsync
        public async AssemblyResult DeletePredecessorAsync(int assemblyId, CancellationToken cancellationToken)
        {
            AssemblyEntity assemblyToUpdate = await _assemblies
                .SingleOrDefaultAsync(x => !x.Deleted && x.Id == assemblyId, cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.NotFound<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            if (!assemblyToUpdate.PredecessorId.HasValue)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.HasNoPredecessor);
            }

            assemblyToUpdate.RemovePredecessor();

            EntityEntry<AssemblyEntity> entityEntry = _assemblies.Update(assemblyToUpdate);

            if (await SaveChangesAsync(cancellationToken))
            {
                return await GetResponseAssemblyDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.PredecessorNotUpdated);
        }
        #endregion

        #region AddAssemblyAsync
        public async AssemblyResult AddAssemblyAsync(AddAssemblyDto addAssemblyDto, CancellationToken cancellationToken)
        {
            if (addAssemblyDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.InvalidData);
            }

            bool workOrderExists = await _workOrderService
                .WorkOrderExistsAsync(addAssemblyDto.WorkOrderId, false, cancellationToken);

            if (!workOrderExists)
            {
                return ResponseDtoHelper.NotFound<ResponseAssemblyDto>(NasbNiroMessages.WorkOrderNotFound);
            }

            AssemblyEntity assemblyToAdd = _mapper.Map<AssemblyEntity>(addAssemblyDto);

            EntityEntry<AssemblyEntity> entityEntry = await _assemblies.AddAsync(assemblyToAdd);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotAdded);
        }
        #endregion

        #region UpdateAssemblyAsync
        public async AssemblyResult UpdateAssemblyAsync(UpdateAssemblyDto updateAssemblyDto, CancellationToken cancellationToken)
        {
            AssemblyEntity assembly = await _assemblies
                .Where(x => !x.Deleted)
                .Where(x => x.Id == updateAssemblyDto.Id && x.WorkOrderId == updateAssemblyDto.WorkOrderId)
                .SingleOrDefaultAsync(cancellationToken);

            if (assembly is null)
            {
                return ResponseDtoHelper.NotFound<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            _mapper.Map(updateAssemblyDto, assembly);

            EntityEntry<AssemblyEntity> entityEntry = _assemblies.Update(assembly);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotUpdated);
        }
        #endregion

        #region ChangeStatusAsync
        public async AssemblyResult ChangeStatusAsync(UpdateAssemblyChangeStatusDto dto, CancellationToken cancellationToken)
        {
            AssemblyEntity assembly = await _assemblies
                .Where(x => !x.Deleted)
                .SingleOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);

            if (assembly is null)
            {
                return ResponseDtoHelper.NotFound<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            assembly.UpdateStatus(dto.Status);
            _assemblies.Update(assembly);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(assembly, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotUpdated);
        }
        #endregion

        #region DeleteAssemblyAsync
        public async AssemblyResult DeleteAssemblyAsync(int assemblyId, CancellationToken cancellationToken)
        {
            AssemblyEntity assemblyToDelete = await _assemblies
                .Include(x => x.Boms)
                    .ThenInclude(x => x.Processes)
                .Where(x => !x.Deleted && x.Id == assemblyId)
                .SingleOrDefaultAsync(cancellationToken);

            if (assemblyToDelete is null)
            {
                return ResponseDtoHelper.NotFound<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            if (assemblyToDelete.Deleted == true)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyAlreadyIsDeleted);
            }

            // R1a: an assembly whose BOMs a production order or a project control WBS node refers to is not deleted
            string blocked = await LegacyDeleteGuard.AssemblyAsync(_unitOfWork, assemblyId, cancellationToken);
            if (blocked != null)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(blocked);
            }

            assemblyToDelete.Delete();

            if (assemblyToDelete.Boms != null)
            {
                foreach (Bom bom in assemblyToDelete.Boms)
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

            // Assemblies that have deleting one as predecessor
            IEnumerable<AssemblyEntity> assembliesToRemovePredecessor = await _assemblies
                .Where(x => !x.Deleted && x.PredecessorId == assemblyToDelete.Id)
                .ToArrayAsync();

            foreach (AssemblyEntity item in assembliesToRemovePredecessor)
            {
                item.RemovePredecessor();
            }
            _assemblies.UpdateRange(assembliesToRemovePredecessor);


            EntityEntry<AssemblyEntity> entityEntry = _assemblies.Update(assemblyToDelete);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotDeleted);
        }
        #endregion

        #region DeleteAssemblyCollectionAsync
        public async AssemblyDeleteCollectionResult DeleteAssemblyCollectionAsync(IEnumerable<int> assemblyIds, CancellationToken cancellationToken)
        {
            if (assemblyIds is null)
            {
                return ResponseDtoHelper.BadRequest<IEnumerable<DeleteAssemblyResultDto>>(NasbNiroMessages.InvalidData);
            }

            List<DeleteAssemblyResultDto> deleteAssemblyResultDtos = new List<DeleteAssemblyResultDto>();

            foreach (int assemblyId in assemblyIds)
            {
                bool exception = false;
                ResponseDto<ResponseAssemblyDto> result = null;

                try
                {
                    result = await this.DeleteAssemblyAsync(assemblyId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteAssemblyResultDtos.Add(new DeleteAssemblyResultDto()
                    {
                        Id = assemblyId,
                        Success = false,
                        Message = NasbNiroMessages.AssemblyDeletingException
                    });

                    continue;
                }

                DeleteAssemblyResultDto deleteAssemblyResultDto = new DeleteAssemblyResultDto()
                {
                    Id = assemblyId,
                    Success = result.Success
                };

                if (!result.Success)
                {
                    deleteAssemblyResultDto.Message = result.Message;
                }

                deleteAssemblyResultDtos.Add(deleteAssemblyResultDto);
            }

            return ResponseDtoHelper.Ok(deleteAssemblyResultDtos.AsEnumerable());
        }
        #endregion

        #region GetAllResponseAssemblyDtoAsync
        public async AssemblyPagedResult GetAllResponseAssemblyDtoAsync(AssemblyPaginationParams assemblyPaginationParams, CancellationToken cancellationToken)
        {
            if (assemblyPaginationParams is null)
            {
                return ResponseDtoHelper.BadRequest<PagedList<ResponseAssemblyDto>>(NasbNiroMessages.InvalidData);
            }

            IQueryable<AssemblyEntity> query = _assembliesQueryable;

            if (assemblyPaginationParams.IsDeleted.HasValue)
            {
                query = query.Where(assembly => assembly.Deleted == assemblyPaginationParams.IsDeleted);
            }

            if (!string.IsNullOrEmpty(assemblyPaginationParams.Contains))
            {
                string[] strArray = assemblyPaginationParams.Contains.Split(' ');
                query = query.Where(assembly =>
                   assembly.Code != null && strArray.Any(str => assembly.Code.Contains(str)) ||
                    assembly.Title != null && strArray.Any(str => assembly.Title.Contains(str)));
            }

            int? parentId = assemblyPaginationParams.ParentId.HasValue
                 ? assemblyPaginationParams.ParentId.Value
                 : (int?)null;

            query = query.Where(x => x.PredecessorId == parentId);
            query = query.Where(assembly => assembly.WorkOrderId == assemblyPaginationParams.WorkOrderId);

            IQueryable<ResponseAssemblyDto> resultQuery;

            resultQuery = query.ProjectTo<ResponseAssemblyDto>(_mapper.ConfigurationProvider);

            PagedList<ResponseAssemblyDto> data = await PagedList<ResponseAssemblyDto>
                .CreateAsync(resultQuery,
                             assemblyPaginationParams.PageNumber,
                             assemblyPaginationParams.PageSize, cancellationToken);

            IEnumerable<Task> tasks = data.Select(x => IncludeChildrenAsync(x, cancellationToken));
            await Task.Run(() => Task.WhenAll(tasks), cancellationToken);

            return ResponseDtoHelper.Ok(data);
        }
        #endregion

        #region GetAssemblyByIdAsync
        public async ValueTask<AssemblyEntity> GetAssemblyByIdAsync(int AssemblyId, CancellationToken cancellationToken, bool IncludeAllChildren = false)
        {
            //Better not to use AsNoTracking in this scope.
            IQueryable<AssemblyEntity> query = _assemblies.AsQueryable();

            if (IncludeAllChildren)
            {
                query = query.Include(x => x.Boms).ThenInclude(x => x.Processes);
            }

            AssemblyEntity assembly = await query.SingleOrDefaultAsync(ea => ea.Id == AssemblyId, cancellationToken);

            return assembly;
        }
        #endregion

        #region GetResponseAssemblyDtoAsync
        public async AssemblyResult GetResponseAssemblyDtoAsync(GetAssemblyDto getAssemblyDto, CancellationToken cancellationToken)
        {
            if (getAssemblyDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.InvalidData);
            }

            IQueryable<ResponseAssemblyDto> resultQuery;

            resultQuery = _assembliesQueryable.ProjectTo<ResponseAssemblyDto>(_mapper.ConfigurationProvider);

            ResponseAssemblyDto data = await resultQuery.SingleOrDefaultAsync(a =>
                    a.Id == getAssemblyDto.Id &&
                    a.WorkOrderId == getAssemblyDto.WorkOrderId, cancellationToken);

            if (data != null)
            {
                await IncludeChildrenAsync(data, cancellationToken);

                return ResponseDtoHelper.Ok(data);
            }

            return ResponseDtoHelper.NotFound<ResponseAssemblyDto>(AssemblyMessages.NotFound);
        }
        #endregion

        #region AssemblyExistsAsync
        public async ValueTask<bool> AssemblyExistsAsync(int assemblyId, bool isDeleted, CancellationToken cancellationToken)
        {
            bool result = await _assembliesQueryable.AnyAsync(ea => ea.Id == assemblyId && ea.Deleted == isDeleted, cancellationToken);

            return result;
        }
        #endregion

        public async AssemblyPagedResult GetReadyToAssembleAssembliesAsync(ReadyToAssembleAssembliesPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            var query = _assemblies
                .AsNoTracking()
                .Where(x => !x.Deleted)
                .Where(x => x.Status == AssemblyStatus.NewAssembly)
                .Where(x => ApplicationDbContext.CalculateAssemblyProgressLevel(x.Id) == Constants.CompletedProgressLevel)
                .ProjectTo<ResponseAssemblyDto>(_mapper.ConfigurationProvider);

            Task<PagedList<ResponseAssemblyDto>> data = PagedList<ResponseAssemblyDto>
                        .CreateAsync(query,
                                    paginationParams.PageNumber,
                                    paginationParams.PageSize, cancellationToken);

            return ResponseDtoHelper.Ok(await data);
        }

        public async AssemblyResult StartAssemblingAsync(UpdateAssemblyStartAssemblingDto updateDto, CancellationToken cancellationToken)
        {
            AssemblyEntity assemblyToUpdate = await _assemblies
                .Where(x => x.Id == updateDto.Id)
                .Where(x => !x.Deleted)
                .SingleOrDefaultAsync(cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            if (assemblyToUpdate.IsAssembligStarted())
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyCanNotStartAssembligDueToStatus);
            }

            (decimal? pl, bool exists) progressLevel = await GetProgressLevelAsync(assemblyToUpdate.Id, cancellationToken);

            if (progressLevel.pl != Constants.CompletedProgressLevel)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyCanNotStartAssembligDueToProgressLevel);
            }

            assemblyToUpdate.StartAssembling();
            _assemblies.Update(assemblyToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(assemblyToUpdate, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotUpdated);
        }

        public async AssemblyResult AssemblerAssembledAsync(UpdateAssemblyAssemblerAssembledDto updateDto, CancellationToken cancellationToken)
        {
            AssemblyEntity assemblyToUpdate = await _assembliesQueryable
                .Where(x => !x.Deleted)
                .SingleOrDefaultAsync(x => x.Id == updateDto.Id, cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            if (!assemblyToUpdate.CanAssemblerAssemble())
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyCanNotUpdateDueToStatus);
            }

            assemblyToUpdate.AssemblerAssembled();
            _assemblies.Update(assemblyToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(assemblyToUpdate, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotUpdated);
        }

        public async AssemblyResult QcAcceptToFinalAssembleAsync(UpdateAssemblyQcAcceptToFinalAssembleDto updateDto, CancellationToken cancellationToken)
        {
            AssemblyEntity assemblyToUpdate = await _assembliesQueryable
                .Where(x => !x.Deleted)
                .SingleOrDefaultAsync(x => x.Id == updateDto.Id, cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            if (!assemblyToUpdate.CanQcRejectToAssembleOrAcceptToFinalAssemble())
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyCanNotUpdateDueToStatus);
            }

            assemblyToUpdate.QcAcceptToFinalAssemble();
            _assemblies.Update(assemblyToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(assemblyToUpdate, cancellationToken);
            }


            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotUpdated);
        }

        public async AssemblyResult QcRejectToAssembleAsync(UpdateAssemblyQcRejectToAssembleDto updateDto, CancellationToken cancellationToken)
        {
            AssemblyEntity assemblyToUpdate = await _assembliesQueryable
                .Where(x => !x.Deleted)
                .SingleOrDefaultAsync(x => x.Id == updateDto.Id, cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            if (!assemblyToUpdate.CanQcRejectToAssembleOrAcceptToFinalAssemble())
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyCanNotUpdateDueToStatus);
            }

            assemblyToUpdate.QcRejectToAssemble();
            _assemblies.Update(assemblyToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(assemblyToUpdate, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotUpdated);
        }

        public async AssemblyResult AssemblerFinalAssembledAsync(UpdateAssemblyAssemblerFinalAssembledDto updateDto, CancellationToken cancellationToken)
        {
            AssemblyEntity assemblyToUpdate = await _assembliesQueryable
                .Where(x => !x.Deleted)
                .SingleOrDefaultAsync(x => x.Id == updateDto.Id, cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            if (!assemblyToUpdate.CanAssemblerFinalAssemble())
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyCanNotUpdateDueToStatus);
            }

            assemblyToUpdate.AssemblerFinalAssembled();
            _assemblies.Update(assemblyToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(assemblyToUpdate, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotUpdated);
        }

        public async AssemblyResult QcRejectToFinalAssembleAsync(UpdateAssemblyQcRejectToFinalAssembleDto updateDto, CancellationToken cancellationToken)
        {
            AssemblyEntity assemblyToUpdate = await _assembliesQueryable
                .Where(x => !x.Deleted)
                .SingleOrDefaultAsync(x => x.Id == updateDto.Id, cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            if (!assemblyToUpdate.CanQcRejectToFinalAssembleOrAcceptToFinalAcceptance())
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyCanNotUpdateDueToStatus);
            }

            assemblyToUpdate.QcRejectToFinalAssemble();
            _assemblies.Update(assemblyToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(assemblyToUpdate, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotUpdated);
        }

        public async AssemblyResult QcAcceptToFinalAcceptanceAsync(UpdateAssemblyQcAcceptToFinalAcceptanceDto updateDto, CancellationToken cancellationToken)
        {
            AssemblyEntity assemblyToUpdate = await _assembliesQueryable
                .Where(x => !x.Deleted)
                .SingleOrDefaultAsync(x => x.Id == updateDto.Id, cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            if (!assemblyToUpdate.CanQcRejectToFinalAssembleOrAcceptToFinalAcceptance())
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyCanNotUpdateDueToStatus);
            }

            assemblyToUpdate.QcAcceptToFinalAcceptance();
            _assemblies.Update(assemblyToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(assemblyToUpdate, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotUpdated);
        }

        public async AssemblyResult PmoAcceptToCompletedAsync(UpdateAssemblyPmoAcceptToCompletedDto updateDto, CancellationToken cancellationToken)
        {
            AssemblyEntity assemblyToUpdate = await _assembliesQueryable
                .Where(x => !x.Deleted)
                .SingleOrDefaultAsync(x => x.Id == updateDto.Id, cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(AssemblyMessages.NotFound);
            }

            if (!assemblyToUpdate.CanPmoAcceptToCompleted())
            {
                return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyCanNotUpdateDueToStatus);
            }

            assemblyToUpdate.PmoAcceptToCompleted();
            _assemblies.Update(assemblyToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseAssemblyDtoAsync(assemblyToUpdate, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseAssemblyDto>(NasbNiroMessages.AssemblyNotUpdated);
        }

        private async Task<(decimal? pl, bool exists)> GetProgressLevelAsync(int id, CancellationToken cancellationToken)
        {
            if (!await _assemblies.AnyAsync(x => x.Id == id, cancellationToken))
            {
                return (null, false);
            }

            decimal? pl = await _assemblies.Where(x => x.Id == id)
                .Select(x => ApplicationDbContext.CalculateAssemblyProgressLevel(x.Id))
                .SingleOrDefaultAsync(cancellationToken);

            return (pl, true);
        }

        private async Task IncludeChildrenAsync(ResponseAssemblyDto data, CancellationToken cancellationToken)
        {
            if (data != null)
            {
                data.Children = await _assembliesQueryable
                    .Where(x => !x.Deleted && x.PredecessorId == data.Id)
                    .ProjectTo<ResponseAssemblyDto>(_mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);

                IEnumerable<Task> tasks = data.Children.Select(x => IncludeChildrenAsync(x, cancellationToken));

                await Task.Run(async () => await Task.WhenAll(tasks), cancellationToken);
            }
        }

        #region SaveChangesAsync
        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }
        #endregion
    }
}
