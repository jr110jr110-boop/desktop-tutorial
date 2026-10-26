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
using Services.Resources;
using ViewModels.NasbNiro.BomDtos;
using ViewModels.NasbNiro.Pagination;

using BomResult = System.Threading.Tasks.ValueTask
    <Common.Results.ResponseDto
        <ViewModels.NasbNiro.BomDtos.ResponseBomDto>>;


namespace Services.NasbNiro
{
    public class BomService : IBomService
    {
        #region Fields
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAssemblyService _assemblyService;
        private readonly ILogger<BomService> _logger;
        private readonly IMapper _mapper;
        private readonly DbSet<Bom> _boms;

        private IQueryable<Bom> _bomsQueryable
            => _boms.AsNoTracking().AsQueryable();
        #endregion

        #region Ctor
        public BomService(IUnitOfWork unitOfWork,
                                        IAssemblyService assemblyService,
                                        ILogger<BomService> logger,
                                        IMapper mapper)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _assemblyService = assemblyService.ThrowIfNull(nameof(assemblyService));
            _boms = _unitOfWork.Set<Bom>().ThrowIfNull(nameof(_boms));
            _logger = logger.ThrowIfNull(nameof(logger));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
        }
        #endregion

        #region AddBomAsync
        public async BomResult AddBomAsync(AddBomDto addBomDto, CancellationToken cancellationToken)
        {
            bool assemblyExists = await _assemblyService
                .AssemblyExistsAsync(addBomDto.AssemblyId, false, cancellationToken);

            if (!assemblyExists)
            {
                return ResponseDtoHelper.NotFound<ResponseBomDto>(AssemblyMessages.NotFound);
            }

            Bom BomToAdd =
                _mapper.Map<Bom>(addBomDto);

            EntityEntry<Bom> entityEntry = await _boms.AddAsync(BomToAdd, cancellationToken);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseBomDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseBomDto>(NasbNiroMessages.BomNotAdded);
        }
        #endregion

        #region UpdateBomAsync
        public async BomResult UpdateBomAsync(UpdateBomDto updateBomDto, CancellationToken cancellationToken)
        {
            Bom bom = await _boms.SingleOrDefaultAsync(x =>
                !x.Deleted &&
                x.Id == updateBomDto.Id &&
                x.AssemblyId == updateBomDto.AssemblyId, cancellationToken);

            if (bom is null)
            {
                return ResponseDtoHelper.NotFound<ResponseBomDto>(NasbNiroMessages.BomNotFound);
            }

            if (bom.AssemblyId != updateBomDto.AssemblyId)
            {
                return ResponseDtoHelper.BadRequest<ResponseBomDto>(NasbNiroMessages.AssemblyBomNotMatched);
            }

            _mapper.Map(updateBomDto, bom);

            EntityEntry<Bom> entityEntry = _boms.Update(bom);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseBomDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseBomDto>(NasbNiroMessages.BomNotUpdated);
        }
        #endregion

        #region SetPredecessorAsync
        public async BomResult SetPredecessorAsync(UpdateBomSetPredecessorDto dto, CancellationToken cancellationToken)
        {
            if (dto.BomId == dto.PredecessorId)
            {
                return ResponseDtoHelper.BadRequest<ResponseBomDto>(BomMessages.PredecessorCanNotBeTheSameBom);
            }

            var bomAndWorkOrderId = await _boms
                .Include(x => x.Assembly)
                .Where(x => !x.Deleted && x.Id == dto.BomId)
                .Select(x => new
                {
                    Bom = x,
                    WorkOrderId = x.Assembly.WorkOrderId
                })
                .SingleOrDefaultAsync(cancellationToken);

            Bom bomToUpdate = bomAndWorkOrderId.Bom;

            if (bomToUpdate is null)
            {
                return ResponseDtoHelper.NotFound<ResponseBomDto>(BomMessages.BomNotFound);
            }

            Bom predecessorBom = await _boms
                .Include(x => x.Assembly)
                .Where(x =>
                    !x.Deleted &&
                    x.Id == dto.PredecessorId &&
                    x.Assembly.WorkOrderId == bomAndWorkOrderId.WorkOrderId)
                .SingleOrDefaultAsync(cancellationToken);

            if (predecessorBom is null)
                return ResponseDtoHelper.NotFound<ResponseBomDto>(BomMessages.PredecessorNotFound);

            if (bomToUpdate.PredecessorId.HasValue && bomToUpdate.PredecessorId.Value == predecessorBom.Id)
                return ResponseDtoHelper
                    .BadRequest<ResponseBomDto>(BomMessages.PredecessorIsAlreadySetted);

            (bool isValid, string messsage) predecessorValidationResult =
                await IsValidPredecessorAsync(bomId: bomToUpdate.Id, predecessorId: dto.PredecessorId, cancellationToken: cancellationToken);

            if (!predecessorValidationResult.isValid)
                return ResponseDtoHelper.BadRequest<ResponseBomDto>(predecessorValidationResult.messsage);

            bomToUpdate.UpdatePredecessor(predecessorBom);

            EntityEntry<Bom> entityEntry = _boms.Update(bomToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseBomDtoAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper.BadRequest<ResponseBomDto>
               (BomMessages.PredecessorNotUpdated);
        }

        private async Task<(bool, string)> IsValidPredecessorAsync(int bomId, int predecessorId, CancellationToken cancellationToken)
        {
            // cycle guard: a link to itself is a loop, and a visited set stops the walk if the stored data already contains a loop
            if (predecessorId == bomId) return (false, BomMessages.PredecessorCausesLoop);
            var visited = new System.Collections.Generic.HashSet<int>();
            while (visited.Add(predecessorId))
            {
                int? predecessorOfPredecessor = await _boms
                   .Where(x => !x.Deleted && x.Id == predecessorId)
                   .Select(x => x.PredecessorId)
                   .SingleOrDefaultAsync(cancellationToken);

                if (predecessorOfPredecessor is null) return (true, null);
                if (predecessorOfPredecessor.Value == bomId) return (false, BomMessages.PredecessorCausesLoop);

                predecessorId = predecessorOfPredecessor.Value;
            }
            return (false, BomMessages.PredecessorCausesLoop);
        }
        #endregion

        #region DeletePredecessorAsync
        public async BomResult DeletePredecessorAsync(int bomId, CancellationToken cancellationToken)
        {
            Bom bomToUpdate = await _boms
                .SingleOrDefaultAsync(x => !x.Deleted && x.Id == bomId, cancellationToken);

            bomToUpdate.RemovePredecessor();

            EntityEntry<Bom> entityEntry = _boms.Update(bomToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseBomDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseBomDto>(BomMessages.PredecessorNotUpdated);
        }
        #endregion

        #region DeleteBomAsync
        public async BomResult DeleteBomAsync(int bomId, CancellationToken cancellationToken)
        {

            Bom bom = await _boms
                .Include(x => x.Processes)
                .SingleOrDefaultAsync(x => !x.Deleted && x.Id == bomId, cancellationToken);

            if (bom is null)
            {
                return ResponseDtoHelper.NotFound<ResponseBomDto>(NasbNiroMessages.BomNotFound);
            }

            // R1a: a BOM that a production order or a project control WBS node refers to is not deleted
            string blocked = await LegacyDeleteGuard.BomAsync(_unitOfWork, bomId, cancellationToken);
            if (blocked != null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBomDto>(blocked);
            }

            bom.Delete();

            if (bom.Processes != null)
            {
                foreach (ProcessEntity process in bom.Processes)
                {
                    process.Delete();
                }
            }

            IEnumerable<Bom> bomsToRemovePredecessor = await _bomsQueryable
                .Where(x => !x.Deleted && x.PredecessorId == bom.Id)
                .ToListAsync(cancellationToken);

            foreach (Bom b in bomsToRemovePredecessor)
            {
                b.RemovePredecessor();
                _boms.Update(b);
            }

            EntityEntry<Bom> entityEntry = _boms.Update(bom);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseBomDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseBomDto>(NasbNiroMessages.BomNotDeleted);
        }
        #endregion

        #region DeleteBomCollectionAsync
        public async ValueTask<ResponseDto<IEnumerable<DeleteBomResultDto>>> DeleteBomCollectionAsync(IEnumerable<int> bomIds, CancellationToken cancellationToken)
        {
            if (bomIds is null)
            {
                return ResponseDtoHelper.BadRequest<IEnumerable<DeleteBomResultDto>>(NasbNiroMessages.InvalidData);
            }

            List<DeleteBomResultDto> deleteBomResultDtos = new List<DeleteBomResultDto>();

            foreach (int bomId in bomIds)
            {
                bool exception = false;
                ResponseDto<ResponseBomDto> result = null;

                try
                {
                    result = await this.DeleteBomAsync(bomId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteBomResultDtos.Add(new DeleteBomResultDto()
                    {
                        Id = bomId,
                        Success = false,
                        Message = NasbNiroMessages.BomDeletingException
                    });

                    continue;
                }

                DeleteBomResultDto deleteBomResultDto
                    = new DeleteBomResultDto()
                    {
                        Id = bomId,
                        Success = result.Success
                    };

                if (!result.Success)
                {
                    deleteBomResultDto.Message = result.Message;
                }

                deleteBomResultDtos.Add(deleteBomResultDto);
            }

            return ResponseDtoHelper.Ok(deleteBomResultDtos.AsEnumerable());
        }
        #endregion

        #region GetBomsAsync
        public async ValueTask<ResponseDto<PagedList<ResponseBomDto>>> GetBomsAsync(BomPaginationParams bomPaginationParams, CancellationToken cancellationToken)
        {
            if (bomPaginationParams is null)
                return ResponseDtoHelper
                    .BadRequest<PagedList<ResponseBomDto>>(NasbNiroMessages.InvalidData);

            IQueryable<Bom> query = _bomsQueryable;

            if (bomPaginationParams.IsDeleted.HasValue)
                query = query.Where(bom =>
                        bom.Deleted == bomPaginationParams.IsDeleted);

            if (!string.IsNullOrEmpty(bomPaginationParams.Contains))
            {
                string[] strArray = bomPaginationParams.Contains.Split(' ');

                query = query.Where(bom =>
                    bom.Title != null && strArray.Any(str => bom.Title.Contains(str)) ||
                    bom.Unit != null && strArray.Any(str => bom.Unit.Contains(str)) ||
                    bom.Material != null && strArray.Any(str => bom.Material.Contains(str)) ||
                    bom.Standard != null && strArray.Any(str => bom.Standard.Contains(str)) ||
                    bom.Description != null && strArray.Any(str => bom.Description.Contains(str)));
            }

            #region numericalParams
            query = query.Where(bom => bom.AssemblyId ==
                bomPaginationParams.AssemblyId);

            if (bomPaginationParams.PositionNumber.HasValue)
            {
                query = query.Where(bom => bom.PositionNumber ==
                    bomPaginationParams.PositionNumber);
            }

            if (bomPaginationParams.Count.HasValue)
            {
                query = query.Where(bom => bom.Count ==
                    bomPaginationParams.Count);
            }

            if (bomPaginationParams.NotWetWeightLessThan.HasValue)
            {
                query = query.Where(bom => bom.NotWetWeight <=
                    bomPaginationParams.NotWetWeightLessThan);
            }

            if (bomPaginationParams.NotWetWeightMoreThan.HasValue)
            {
                query = query.Where(bom => bom.NotWetWeight >=
                    bomPaginationParams.NotWetWeightMoreThan);
            }

            if (bomPaginationParams.WetWeightLessThan.HasValue)
            {
                query = query.Where(bom => bom.WetWeight <=
                    bomPaginationParams.WetWeightLessThan);
            }

            if (bomPaginationParams.WetWeightMoreThan.HasValue)
            {
                query = query.Where(bom => bom.WetWeight >=
                    bomPaginationParams.WetWeightMoreThan);
            }
            #endregion

            IQueryable<ResponseBomDto> resultQuery;

            resultQuery = query.ProjectTo<ResponseBomDto>(_mapper.ConfigurationProvider);

            Task<PagedList<ResponseBomDto>> data =
                PagedList<ResponseBomDto>
                            .CreateAsync(source: resultQuery,
                                         pageNumber: bomPaginationParams.PageNumber,
                                         pageSize: bomPaginationParams.PageSize, cancellationToken: cancellationToken);

            return ResponseDtoHelper.Ok(await data);
        }
        #endregion

        #region GetBomByIdAsync
        public async ValueTask<Bom> GetBomByIdAsync(int BomId, CancellationToken cancellationToken, bool IncludeAllChildren = false)
        {
            //Better not to use AsNoTracking in this scope.
            IQueryable<Bom> query = _boms.AsQueryable();

            if (IncludeAllChildren)
            {
                query = query.Include(x => x.Processes);
            }

            Bom bom = await query.SingleOrDefaultAsync(ea => ea.Id == BomId, cancellationToken);

            return bom;
        }
        #endregion

        #region GetResponseBomDtoAsync
        public async BomResult GetResponseBomDtoAsync(GetBomDto getBomDto, CancellationToken cancellationToken)
        {
            if (getBomDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseBomDto>(NasbNiroMessages.InvalidData);
            }

            IQueryable<ResponseBomDto> resultQuery;

            resultQuery = _bomsQueryable.ProjectTo<ResponseBomDto>(_mapper.ConfigurationProvider);

            ResponseBomDto data = await resultQuery
                .SingleOrDefaultAsync(b => b.Id == getBomDto.Id && b.AssemblyId == getBomDto.AssemblyId, cancellationToken);

            if (data != null)
            {
                return ResponseDtoHelper.Ok(data);
            }

            return ResponseDtoHelper.NotFound<ResponseBomDto>(NasbNiroMessages.BomNotFound);
        }
        #endregion

        #region BomExistsAsync
        public async ValueTask<bool> BomExistsAsync(int bomId, CancellationToken cancellationToken, bool? isDeleted = null)
        {
            IQueryable<Bom> boms = _boms;

            if (isDeleted.HasValue) boms = boms.Where(x => x.Deleted == isDeleted.Value);

            bool result = await boms.AnyAsync(x => x.Id == bomId, cancellationToken);

            return result;
        }
        #endregion

        #region SaveChangesAsync
        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }
        #endregion
    }
}
