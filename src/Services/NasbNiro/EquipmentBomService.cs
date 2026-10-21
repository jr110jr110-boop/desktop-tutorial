using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Equipments;
using Entities.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Contracts;
using Services.Resources;
using ViewModels.NasbNiro.EquipmentBomDtos;
using ViewModels.NasbNiro.EquipmentProcessDtos;
using ViewModels.NasbNiro.Pagination;
using DeleteEquipmentBomsResult = System.Threading.Tasks.ValueTask
    <Common.Results.ResponseDto
        <System.Collections.Generic.IEnumerable
            <ViewModels.NasbNiro.EquipmentBomDtos.DeleteEquipmentBomResultDto>>>;
using EquipmentBomResult = System.Threading.Tasks.ValueTask
    <Common.Results.ResponseDto
        <ViewModels.NasbNiro.EquipmentBomDtos.ResponseEquipmentBomDto>>;
using EquipmentBomAllResult = System.Threading.Tasks.ValueTask
    <Common.Results.ResponseDto
        <ViewModels.NasbNiro.EquipmentBomDtos.ResponseEquipmentBomAllDto>>;
using PaginatedEquipmentBom = System.Threading.Tasks.ValueTask
    <Common.Results.ResponseDto
        <ViewModels.NasbNiro.Pagination.PagedList
            <ViewModels.NasbNiro.EquipmentBomDtos.ResponseEquipmentBomDto>>>;

namespace Services.NasbNiro
{
    public class EquipmentBomService : IEquipmentBomService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEquipmentAssemblyService _equipmentAssemblyService;
        private readonly DbSet<EquipmentProcess> _equipmentProcess;
        private readonly ILogger<EquipmentBomService> _logger;
        private readonly IMapper _mapper;
        private readonly DbSet<EquipmentBom> _equipmentBoms;

        private IQueryable<EquipmentBom> _equipmentBomsQueryable
            => _equipmentBoms.AsNoTracking().AsQueryable();

        public EquipmentBomService(IUnitOfWork unitOfWork,
                                        IEquipmentAssemblyService equipmentAssemblyService,
                                        ILogger<EquipmentBomService> logger,
                                        IMapper mapper)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));

            _equipmentProcess = _unitOfWork.Set<EquipmentProcess>() ??
                throw new ArgumentNullException(nameof(_equipmentBoms));

            _equipmentAssemblyService = equipmentAssemblyService ??
                throw new ArgumentNullException(nameof(equipmentAssemblyService));

            _equipmentBoms = _unitOfWork.Set<EquipmentBom>() ??
                throw new ArgumentNullException(nameof(_equipmentBoms));

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public async EquipmentBomResult AddEquipmentBomAsync(AddEquipmentBomWithEqueipmentProccessDto addEquipmentBomDto, CancellationToken cancellationToken)
        {
            if (addEquipmentBomDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseEquipmentBomDto>(NasbNiroMessages.InvalidData);

            bool equipmentAssemblyExists = await _equipmentAssemblyService
                .EquipmentAssemblyExistsAsync(addEquipmentBomDto.EquipmentAssemblyId, false, cancellationToken);

            if (!equipmentAssemblyExists)
                return ResponseDtoHelper.NotFound<ResponseEquipmentBomDto>(NasbNiroMessages.EquipmentAssemblyNotFound);

            EquipmentBom EquipmentBomToAdd = new EquipmentBom()
            {
                EquipmentAssemblyId = addEquipmentBomDto.EquipmentAssemblyId,
                Title = addEquipmentBomDto.Title,
                Sort = addEquipmentBomDto.Sort,
                Count = addEquipmentBomDto.Count,
                Standard = addEquipmentBomDto.Standard,
                Material = addEquipmentBomDto.Material,
                Length = addEquipmentBomDto.Length,
                LengthUnit = addEquipmentBomDto.LengthUnit,
                Height = addEquipmentBomDto.Height,
                HeightUnit = addEquipmentBomDto.HeightUnit,
                Width = addEquipmentBomDto.Width,
                WidthUnit = addEquipmentBomDto.WidthUnit,
                WetWeight = addEquipmentBomDto.WetWeight,
                NotWetWeightUnit = addEquipmentBomDto.WetWeightUnit,
                Diagonal = addEquipmentBomDto.Diagonal,
                PositionNumber = addEquipmentBomDto.PositionNumber,
                PredecessorId = addEquipmentBomDto.PredecessorId
            };

            EntityEntry<EquipmentBom> entityEntry = await _equipmentBoms.AddAsync(EquipmentBomToAdd);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                var bomId = await this.GetResponseEquipmentBomDtoByIdAsync(entityEntry.Entity, cancellationToken);
                for (int i = 0; i < addEquipmentBomDto.equipmentProcesses.Count; i++)
                {
                    EquipmentProcess equipmentProcess = new EquipmentProcess()
                    {
                        EquipmentBomId = bomId.Data.Id,
                        ExitCommodityCode = addEquipmentBomDto.equipmentProcesses[i].ExitCommodityCode,
                        EnterCommodityCode = addEquipmentBomDto.equipmentProcesses[i].EnterCommodityCode,
                        Title = addEquipmentBomDto.equipmentProcesses[i].Title,
                        MapDuration = addEquipmentBomDto.equipmentProcesses[i].MapDuration,
                        MasterDuration = addEquipmentBomDto.equipmentProcesses[i].MasterDuration,
                        ProcessDuration = addEquipmentBomDto.equipmentProcesses[i].ProcessDuration,
                        ProgramPrice = addEquipmentBomDto.equipmentProcesses[i].ProgramPrice,
                        WeightPercent = addEquipmentBomDto.equipmentProcesses[i].WeightPercent,
                        Type = addEquipmentBomDto.equipmentProcesses[i].Type,
                        WorkflowId=addEquipmentBomDto.equipmentProcesses[i].WorkflowId
                        
                    };
                    await _equipmentProcess.AddAsync(equipmentProcess);
                    await _unitOfWork.SaveChangesAsync();
                }
                return bomId;
            }

            return ResponseDtoHelper.BadRequest<ResponseEquipmentBomDto>
                (NasbNiroMessages.EquipmentBomNotAdded);
        }

        public async EquipmentBomResult UpdateEquipmentBomAsync(UpdateEquipmentBomDto updateEquipmentBomDto, CancellationToken cancellationToken)
        {
            EquipmentBom equipmentBom = await this.GetEquipmentBomByIdAsync(
                updateEquipmentBomDto.Id, cancellationToken);

            if (equipmentBom is null)
                return ResponseDtoHelper
                    .NotFound<ResponseEquipmentBomDto>(NasbNiroMessages.EquipmentBomNotFound);

            if (equipmentBom.EquipmentAssemblyId != updateEquipmentBomDto.EquipmentAssemblyId)
                return ResponseDtoHelper
                    .BadRequest<ResponseEquipmentBomDto>
                        (NasbNiroMessages.EquipmentAssemblyEquipmentBomNotMatched);

            _mapper.Map(updateEquipmentBomDto, equipmentBom);

            EntityEntry<EquipmentBom> entityEntry = _equipmentBoms.Update(equipmentBom);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseEquipmentBomDtoByIdAsync(getEquipmentBomDto:
                    new GetEquipmentBomDto()
                    {
                        Id = entityEntry.Entity.Id,
                        EquipmentAssemblyId = entityEntry.Entity.EquipmentAssemblyId,
                        IncludeTypes = true
                    }, cancellationToken: cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseEquipmentBomDto>(NasbNiroMessages.EquipmentBomNotUpdated);
        }

        public async EquipmentBomResult SetPredecessorAsync(UpdateEquipmentBomSetPredecessorDto dto, CancellationToken cancellationToken)
        {
            if (dto.EquipmentBomId == dto.PredecessorId)
                return ResponseDtoHelper.BadRequest<ResponseEquipmentBomDto>(EquipmentBomMessages.PredecessorCanNotBeTheSameEquipmentBom);

            var bomAndEquipment = await _equipmentBoms
                .Include(x => x.EquipmentAssembly)
                .Where(x => !x.Deleted && x.Id == dto.EquipmentBomId)
                .Select(x => new
                {
                    EquipmentBom = x,
                    EquipmentId = x.EquipmentAssembly.EquipmentId
                })
                .SingleOrDefaultAsync(cancellationToken);

            EquipmentBom bomToUpdate = bomAndEquipment.EquipmentBom;

            if (bomToUpdate is null)
                return ResponseDtoHelper
                    .NotFound<ResponseEquipmentBomDto>(EquipmentBomMessages.EquipmentBomNotFound);

            EquipmentBom predecessorBom = await _equipmentBoms
                .Include(x => x.EquipmentAssembly)
                .SingleOrDefaultAsync(x =>
                    !x.Deleted && x.Id == dto.PredecessorId && x.EquipmentAssembly.EquipmentId == bomAndEquipment.EquipmentId,
                    cancellationToken);


            if (predecessorBom is null)
                return ResponseDtoHelper
                    .NotFound<ResponseEquipmentBomDto>(EquipmentBomMessages.PredecessorNotFound);

            if (bomToUpdate.PredecessorId.HasValue && bomToUpdate.PredecessorId.Value == predecessorBom.Id)
                return ResponseDtoHelper
                    .BadRequest<ResponseEquipmentBomDto>(EquipmentBomMessages.PredecessorIsAlreadySetted);

            (bool isValid, string messsage) predecessorValidationResult =
                await IsValidPredecessorAsync(bomId: bomToUpdate.Id, predecessorId: predecessorBom.Id, cancellationToken: cancellationToken);

            if (!predecessorValidationResult.isValid)
                return ResponseDtoHelper
                    .BadRequest<ResponseEquipmentBomDto>(predecessorValidationResult.messsage);

            bomToUpdate.UpdatePredecessor(predecessorBom);

            EntityEntry<EquipmentBom> entityEntry = _equipmentBoms.Update(bomToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseEquipmentBomDtoByIdAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper.BadRequest<ResponseEquipmentBomDto>
               (EquipmentBomMessages.PredecessorNotUpdated);
        }

        public async EquipmentBomResult DeletePredecessorAsync(int equipmentBomId, CancellationToken cancellationToken)
        {
            EquipmentBom bomToUpdate = await _equipmentBoms.SingleOrDefaultAsync(
                x => !x.Deleted && x.Id == equipmentBomId,
                cancellationToken);

            bomToUpdate.RemovePredecessor();

            EntityEntry<EquipmentBom> entityEntry = _equipmentBoms.Update(bomToUpdate);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseEquipmentBomDtoByIdAsync(entityEntry.Entity, cancellationToken);

            return ResponseDtoHelper.BadRequest<ResponseEquipmentBomDto>(EquipmentBomMessages.PredecessorNotUpdated);
        }

        private async Task<(bool, string)> IsValidPredecessorAsync(int bomId, int predecessorId, CancellationToken cancellationToken)
        {
            // cycle guard: a link to itself is a loop, and a visited set stops the walk if the stored data already contains a loop
            if (predecessorId == bomId) return (false, EquipmentBomMessages.PredecessorCausesLoop);
            var visited = new System.Collections.Generic.HashSet<int>();
            while (visited.Add(predecessorId))
            {
                int? predecessorOfPredecessor = await _equipmentBoms
                   .Where(x => !x.Deleted && x.Id == predecessorId)
                   .Select(x => x.PredecessorId)
                   .SingleOrDefaultAsync(cancellationToken);

                if (predecessorOfPredecessor is null) return (true, null);
                if (predecessorOfPredecessor.Value == bomId) return (false, EquipmentBomMessages.PredecessorCausesLoop);

                predecessorId = predecessorOfPredecessor.Value;
            }
            return (false, EquipmentBomMessages.PredecessorCausesLoop);
        }

        public async EquipmentBomResult DeleteEquipmentBomAsync(int equipmentBomId, CancellationToken cancellationToken)
        {
            EquipmentBom equipmentBom = await GetEquipmentBomByIdAsync
                (equipmentBomId, cancellationToken, IncludeAllChildren: true);

            if (equipmentBom is null)
                return ResponseDtoHelper
                    .NotFound<ResponseEquipmentBomDto>(NasbNiroMessages.EquipmentBomNotFound);

            if (equipmentBom.Deleted == true)
                return ResponseDtoHelper
                    .BadRequest<ResponseEquipmentBomDto>(NasbNiroMessages.EquipmentBomAlreadyIsDeleted);

            equipmentBom.Delete();

            if (equipmentBom.EquipmentProcesses != null)
                foreach (EquipmentProcess equipmentProcess in equipmentBom.EquipmentProcesses)
                {
                    equipmentProcess.Delete();
                }

            IEnumerable<EquipmentBom> equipmentBomsToPredecessorless = await _equipmentBoms
                .Where(x => !x.Deleted && x.PredecessorId == equipmentBom.Id)
                .ToListAsync(cancellationToken);

            foreach (EquipmentBom item in equipmentBomsToPredecessorless)
            {
                item.RemovePredecessor();
                _equipmentBoms.Update(item);
            }

            EntityEntry<EquipmentBom> entityEntry =
                _equipmentBoms.Update(equipmentBom);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseEquipmentBomDtoByIdAsync(getEquipmentBomDto:
                    new GetEquipmentBomDto()
                    {
                        Id = entityEntry.Entity.Id,
                        EquipmentAssemblyId = entityEntry.Entity.EquipmentAssemblyId,
                        IncludeTypes = false
                    }, cancellationToken: cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseEquipmentBomDto>(NasbNiroMessages.EquipmentBomNotDeleted);
        }

        public async DeleteEquipmentBomsResult DeleteEquipmentBomCollectionAsync(IEnumerable<int> equipmentBomIds, CancellationToken cancellationToken)
        {
            if (equipmentBomIds is null)
                return ResponseDtoHelper
                    .BadRequest<IEnumerable<DeleteEquipmentBomResultDto>>(NasbNiroMessages.InvalidData);

            List<DeleteEquipmentBomResultDto> deleteEquipmentBomResultDtos =
                new List<DeleteEquipmentBomResultDto>();

            foreach (int equipmentBomId in equipmentBomIds)
            {
                bool exception = false;
                ResponseDto<ResponseEquipmentBomDto> result = null;

                try
                {
                    result = await this.DeleteEquipmentBomAsync(equipmentBomId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteEquipmentBomResultDtos.Add(new DeleteEquipmentBomResultDto()
                    {
                        Id = equipmentBomId,
                        Success = false,
                        Message = NasbNiroMessages.EquipmentBomDeletingException
                    });

                    continue;
                }

                DeleteEquipmentBomResultDto deleteEquipmentBomResultDto
                    = new DeleteEquipmentBomResultDto()
                    {
                        Id = equipmentBomId,
                        Success = result.Success
                    };

                if (!result.Success)
                {
                    deleteEquipmentBomResultDto.Message = result.Message;
                }

                deleteEquipmentBomResultDtos.Add(deleteEquipmentBomResultDto);
            }

            return ResponseDtoHelper.Ok(deleteEquipmentBomResultDtos.AsEnumerable());
        }

        public async PaginatedEquipmentBom GetAllResponseEquipmentBomDtoAsync(EquipmentBomPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            if (paginationParams is null)
            {
                return ResponseDtoHelper.BadRequest<PagedList<ResponseEquipmentBomDto>>(NasbNiroMessages.InvalidData);
            }

            IQueryable<EquipmentBom> query = _equipmentBomsQueryable;

            if (paginationParams.IsDeleted.HasValue)
                query = query.Where(equipmentBom =>
                        equipmentBom.Deleted == paginationParams.IsDeleted);

            if (!string.IsNullOrEmpty(paginationParams.Contains))
            {
                string[] strArray = paginationParams.Contains.Split(' ');
                query = query.Where(equipmentBom =>
                    equipmentBom.Title != null && strArray.Any(str => equipmentBom.Title.Contains(str)) ||
                    equipmentBom.Unit != null && strArray.Any(str => equipmentBom.Unit.Contains(str)) ||
                    equipmentBom.Material != null && strArray.Any(str => equipmentBom.Material.Contains(str)) ||
                    equipmentBom.Standard != null && strArray.Any(str => equipmentBom.Standard.Contains(str)) ||
                    equipmentBom.Description != null && strArray.Any(str => equipmentBom.Description.Contains(str)));
            }

            #region numericalParams
            query = query.Where(equipmentBom => equipmentBom.EquipmentAssemblyId ==
                paginationParams.EquipmentAssemblyId);

            if (paginationParams.HasPositionNumber.HasValue)
            {
                query = query.Where(equipmentBom => equipmentBom.PositionNumber ==
                    paginationParams.HasPositionNumber);
            }

            if (paginationParams.Count.HasValue)
            {
                query = query.Where(equipmentBom => equipmentBom.Count ==
                    paginationParams.Count);
            }

            if (paginationParams.Sort.HasValue)
            {
                query = query.Where(equipmentBom => equipmentBom.Sort ==
                    paginationParams.Sort.Value);
            }

            if (paginationParams.NotWetWeightLessThan.HasValue)
            {
                query = query.Where(equipmentBom => equipmentBom.NotWetWeight <=
                    paginationParams.NotWetWeightLessThan);
            }

            if (paginationParams.NotWetWeightMoreThan.HasValue)
            {
                query = query.Where(equipmentBom => equipmentBom.NotWetWeight >=
                    paginationParams.NotWetWeightMoreThan);
            }

            if (paginationParams.WetWeightLessThan.HasValue)
            {
                query = query.Where(equipmentBom => equipmentBom.WetWeight <=
                    paginationParams.WetWeightLessThan);
            }

            if (paginationParams.WetWeightMoreThan.HasValue)
            {
                query = query.Where(equipmentBom => equipmentBom.WetWeight >=
                    paginationParams.WetWeightMoreThan);
            }
            #endregion

            IQueryable<ResponseEquipmentBomDto> resultQuery;

            if (paginationParams.IncludeTypes)
            {
                resultQuery = query.ProjectTo<ResponseEquipmentBomDto>
                    (_mapper.ConfigurationProvider);
            }
            else
            {
                resultQuery = query.ProjectTo<ResponseEquipmentBomWithoutTypesDto>
                    (_mapper.ConfigurationProvider);
            }

            Task<PagedList<ResponseEquipmentBomDto>> data =
                PagedList<ResponseEquipmentBomDto>
                            .CreateAsync(resultQuery,
                                         paginationParams.PageNumber,
                                         paginationParams.PageSize, cancellationToken);

            return ResponseDtoHelper.Ok(await data);
        }

        public async ValueTask<EquipmentBom> GetEquipmentBomByIdAsync(int EquipmentBomId, CancellationToken cancellationToken, bool IncludeAllChildren = false)
        {
            //Better not to use AsNoTracking in this scope.
            IQueryable<EquipmentBom> query = _equipmentBoms.AsQueryable();

            if (IncludeAllChildren)
            {
                query = query
                    .Include(x => x.EquipmentProcesses);
            }

            EquipmentBom equipmentBom =
               await query.SingleOrDefaultAsync(ea => ea.Id == EquipmentBomId, cancellationToken);

            return equipmentBom;
        }

        public async EquipmentBomResult GetResponseEquipmentBomDtoByIdAsync(GetEquipmentBomDto getEquipmentBomDto, CancellationToken cancellationToken)
        {
            if (getEquipmentBomDto is null)
                return ResponseDtoHelper.BadRequest<ResponseEquipmentBomDto>(NasbNiroMessages.InvalidData);

            IQueryable<ResponseEquipmentBomDto> query;

            if (getEquipmentBomDto.IncludeTypes)
            {
                query = _equipmentBomsQueryable
                    .ProjectTo<ResponseEquipmentBomDto>(_mapper.ConfigurationProvider);
            }
            else
            {
                query = _equipmentBomsQueryable
                   .ProjectTo<ResponseEquipmentBomWithoutTypesDto>(_mapper.ConfigurationProvider);
            }

            ResponseEquipmentBomDto result = await query
                    .SingleOrDefaultAsync(eb =>
                        eb.Id == getEquipmentBomDto.Id && eb.EquipmentAssemblyId == getEquipmentBomDto.EquipmentAssemblyId,
                        cancellationToken);

            if (result != null)
                return ResponseDtoHelper.Ok(result);

            return ResponseDtoHelper
                .NotFound<ResponseEquipmentBomDto>(NasbNiroMessages.EquipmentBomNotFound);
        }

        public async EquipmentBomAllResult GetAllResponseEquipmentBomDtoAsync(GetEquipmentBomAllDto getEquipmentBomAllDto, CancellationToken cancellationToken)
        {
            if (getEquipmentBomAllDto is null)
                return ResponseDtoHelper.BadRequest<ResponseEquipmentBomAllDto>(NasbNiroMessages.InvalidData);

            IQueryable<EquipmentBom> query = _equipmentBomsQueryable;

            query = query
                .Include(equipmentBom => equipmentBom.EquipmentAssembly)
                .Where(equipmentBom => equipmentBom.EquipmentAssembly.EquipmentId == getEquipmentBomAllDto.EquipmentId && !equipmentBom.Deleted);

            List<EquipmentBomAllDto> resultQuery = await query
                .Select(equipment => new EquipmentBomAllDto()
                {
                    Id = equipment.Id,
                    Title = equipment.EquipmentAssembly.Title + " -> " + equipment.Title
                }).ToListAsync();


            return ResponseDtoHelper.Ok(new ResponseEquipmentBomAllDto() { Success = true, Message = "", Data = resultQuery });
        }

        public async ValueTask<bool> EquipmentBomExistsAsync(int equipmentBomId, CancellationToken cancellationToken)
        {
            bool result = await _equipmentBoms.AnyAsync(eb => eb.Id == equipmentBomId, cancellationToken);

            return result;
        }

        public async ValueTask<bool> EquipmentBomExistsAsync(int equipmentBomId, bool isDeleted, CancellationToken cancellationToken)
        {
            bool result = await _equipmentBoms.AnyAsync(eb =>
                eb.Id == equipmentBomId && eb.Deleted == isDeleted,
                cancellationToken);

            return result;
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }
    }
}
