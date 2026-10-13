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
using ViewModels.NasbNiro.EquipmentAssemblyDtos;
using ViewModels.NasbNiro.EquipmentBomDtos;
using ViewModels.NasbNiro.Pagination;
using EquipmentAssemblyPagedResult = System.Threading.Tasks.ValueTask<
    Common.Results.ResponseDto<ViewModels.NasbNiro.Pagination.PagedList<
        ViewModels.NasbNiro.EquipmentAssemblyDtos.ResponseEquipmentAssemblyDto>>>;
using EquipmentAssemblyResult = System.Threading.Tasks.ValueTask<
    Common.Results.ResponseDto<
        ViewModels.NasbNiro.EquipmentAssemblyDtos.ResponseEquipmentAssemblyDto>>;

namespace Services.NasbNiro
{
    public class EquipmentAssemblyService : IEquipmentAssemblyService
    {
        #region Fields
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEquipmentService _equipmentService;
        private readonly ILogger<EquipmentAssemblyService> _logger;
        private readonly IMapper _mapper;
        private readonly DbSet<EquipmentAssembly> _equipmentAssemblies;
        #endregion

        #region Ctor
        public EquipmentAssemblyService(IUnitOfWork unitOfWork,
                                        IEquipmentService equipmentService,
                                        ILogger<EquipmentAssemblyService> logger,
                                        IMapper mapper)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _equipmentService = equipmentService ?? throw new ArgumentNullException(nameof(equipmentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _equipmentAssemblies = _unitOfWork.Set<EquipmentAssembly>() ?? throw new ArgumentNullException(nameof(_equipmentAssemblies));
        }
        #endregion

        #region SetPredecessorAsync
        public async EquipmentAssemblyResult SetPredecessorAsync(UpdateEquipmentAssemblySetPredecessordDto dto, CancellationToken cancellationToken)
        {
            if (dto.Id == dto.PredecessorId)
            {
                return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(EquipmentAssemblyMessages.PredecessorCanNotBeTheSame);
            }

            EquipmentAssembly assemblyToUpdate = await _equipmentAssemblies
                .SingleOrDefaultAsync(x =>
                    !x.Deleted && x.Id == dto.Id,
                    cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.NotFound<ResponseEquipmentAssemblyDto>(EquipmentAssemblyMessages.NotFound);
            }

            EquipmentAssembly predecessorAssembly = await _equipmentAssemblies
                .SingleOrDefaultAsync(x =>
                    !x.Deleted && x.Id == dto.PredecessorId && x.EquipmentId == assemblyToUpdate.EquipmentId,
                    cancellationToken);

            if (predecessorAssembly is null)
            {
                return ResponseDtoHelper.NotFound<ResponseEquipmentAssemblyDto>(EquipmentAssemblyMessages.PredecessorNotFound);
            }

            if (assemblyToUpdate.PredecessorId.HasValue && assemblyToUpdate.PredecessorId.Value == predecessorAssembly.Id)
            {
                return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(EquipmentAssemblyMessages.PredecessorIsAlreadySetted);
            }

            (bool isValid, string messsage) predecessorValidationResult = await IsValidPredecessorAsync(
                assemblyId: assemblyToUpdate.Id,
                predecessorId: predecessorAssembly.Id,
                cancellationToken: cancellationToken);

            if (!predecessorValidationResult.isValid)
            {
                return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(predecessorValidationResult.messsage);
            }

            assemblyToUpdate.UpdatePredecessor(predecessorAssembly);

            EntityEntry<EquipmentAssembly> entityEntry = _equipmentAssemblies.Update(assemblyToUpdate);

            if (await SaveChangesAsync(cancellationToken))
            {
                return await GetResponseEquipmentAssemblyDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(EquipmentAssemblyMessages.PredecessorNotUpdated);
        }

        private async Task<(bool, string)> IsValidPredecessorAsync(int assemblyId, int predecessorId, CancellationToken cancellationToken)
        {
            while (true)
            {
                int? predecessorOfPredecessor =
                    await _equipmentAssemblies
                   .Where(x => !x.Deleted && x.Id == predecessorId)
                   .Select(x => x.PredecessorId)
                   .SingleOrDefaultAsync(cancellationToken);

                if (!predecessorOfPredecessor.HasValue) return (true, null);
                if (predecessorOfPredecessor.Value == assemblyId) return (false, EquipmentAssemblyMessages.PredecessorCausesLoop);

                predecessorId = predecessorOfPredecessor.Value;
            }
        }
        #endregion

        #region DeletePredecessorAsync
        public async EquipmentAssemblyResult DeletePredecessorAsync(int assemblyId, CancellationToken cancellationToken)
        {
            EquipmentAssembly assemblyToUpdate = await _equipmentAssemblies
                .SingleOrDefaultAsync(x => !x.Deleted && x.Id == assemblyId, cancellationToken);

            if (assemblyToUpdate is null)
            {
                return ResponseDtoHelper.NotFound<ResponseEquipmentAssemblyDto>(EquipmentAssemblyMessages.NotFound);
            }

            if (!assemblyToUpdate.PredecessorId.HasValue)
            {
                return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(EquipmentAssemblyMessages.HasNoPredecessor);
            }

            assemblyToUpdate.RemovePredecessor();

            EntityEntry<EquipmentAssembly> entityEntry = _equipmentAssemblies.Update(assemblyToUpdate);

            if (await SaveChangesAsync(cancellationToken))
            {
                return await GetResponseEquipmentAssemblyDtoAsync(entityEntry.Entity, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(EquipmentAssemblyMessages.PredecessorNotUpdated);
        }
        #endregion

        #region AddEquipmentAssemblyAsync
        public async EquipmentAssemblyResult AddEquipmentAssemblyAsync(AddEquipmentAssemblyDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(NasbNiroMessages.InvalidData);
            }

            bool equipmentExists = await _equipmentService.EquipmentExistsAsync(dto.EquipmentId, false, cancellationToken);

            if (!equipmentExists)
            {
                return ResponseDtoHelper.NotFound<ResponseEquipmentAssemblyDto>(NasbNiroMessages.EquipmentNotFound);
            }

            EquipmentAssembly EquipmentAssemblyToAdd = _mapper.Map<EquipmentAssembly>(dto);

            EntityEntry<EquipmentAssembly> entityEntry = await _equipmentAssemblies.AddAsync(EquipmentAssemblyToAdd);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return await this.GetResponseEquipmentAssemblyDtoAsync(dto: entityEntry.Entity, cancellationToken: cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(NasbNiroMessages.EquipmentAssemblyNotAdded);
        }
        #endregion

        #region UpdateEquipmentAssemblyAsync
        public async EquipmentAssemblyResult UpdateEquipmentAssemblyAsync(UpdateEquipmentAssemblyDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(NasbNiroMessages.InvalidData);
            }

            bool equipmentExists = await _equipmentService.EquipmentExistsAsync(dto.EquipmentId, false, cancellationToken);

            if (!equipmentExists)
            {
                return ResponseDtoHelper.NotFound<ResponseEquipmentAssemblyDto>(NasbNiroMessages.EquipmentNotFound);
            }

            EquipmentAssembly equipmentAssembly = await this.GetEquipmentAssemblyByIdAsync(dto.Id, cancellationToken);

            if (equipmentAssembly is null)
            {
                return ResponseDtoHelper.NotFound<ResponseEquipmentAssemblyDto>(NasbNiroMessages.EquipmentNotFound);
            }

            if (equipmentAssembly.EquipmentId != dto.EquipmentId)
            {
                return ResponseDtoHelper.NotFound<ResponseEquipmentAssemblyDto>(NasbNiroMessages.EquipmentEquipmentAssemblyNotMatched);
            }

            _mapper.Map(dto, equipmentAssembly);
            EntityEntry<EquipmentAssembly> entityEntry = _equipmentAssemblies.Update(equipmentAssembly);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                GetEquipmentAssemblyDto getEquipmentAssemblyDto = entityEntry.Entity;
                getEquipmentAssemblyDto.IncludeTypes = true;

                return await this.GetResponseEquipmentAssemblyDtoAsync(getEquipmentAssemblyDto, cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(NasbNiroMessages.EquipmentAssemblyNotUpdated);
        }
        #endregion 

        #region DeleteEquipmentAssemblyAsync
        public async EquipmentAssemblyResult DeleteEquipmentAssemblyAsync(int equipmentAssemblyId, CancellationToken cancellationToken)
        {
            EquipmentAssembly equipmentAssemblyToRemove = await GetEquipmentAssemblyByIdAsync(
                equipmentAssemblyId,
                cancellationToken,
                includeAllChildren: true);

            if (equipmentAssemblyToRemove is null || equipmentAssemblyToRemove.Deleted == true)
            {
                return ResponseDtoHelper.NotFound<ResponseEquipmentAssemblyDto>(NasbNiroMessages.EquipmentAssemblyNotFound);
            }

            equipmentAssemblyToRemove.Delete();

            if (equipmentAssemblyToRemove.EquipmentBoms != null)
            {
                foreach (EquipmentBom equipmentBom in equipmentAssemblyToRemove.EquipmentBoms)
                {
                    equipmentBom.Delete();

                    if (equipmentBom.EquipmentProcesses != null)
                    {
                        foreach (EquipmentProcess equipmentProcess in equipmentBom.EquipmentProcesses)
                        {
                            equipmentProcess.Delete();
                        }
                    }
                }
            }

            #region Clean From Being Predecessor
            IEnumerable<EquipmentAssembly> equipmentAssembliesToRemovePredecessor = await _equipmentAssemblies
                .Where(x => !x.Deleted && x.PredecessorId == equipmentAssemblyToRemove.Id)
                .ToArrayAsync();

            foreach (EquipmentAssembly item in equipmentAssembliesToRemovePredecessor)
            {
                item.RemovePredecessor();
            }

            _equipmentAssemblies.UpdateRange(equipmentAssembliesToRemovePredecessor);
            #endregion

            EntityEntry<EquipmentAssembly> entityEntry = _equipmentAssemblies.Update(equipmentAssemblyToRemove);

            if (await SaveChangesAsync(cancellationToken))
            {
                return await GetResponseEquipmentAssemblyDtoAsync(dto: entityEntry.Entity, cancellationToken: cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(NasbNiroMessages.EquipmentAssemblyNotDeleted);
        }
        #endregion

        #region DeleteEquipmentAssemblyCollectionAsync
        public async ValueTask<ResponseDto<IEnumerable<DeleteEquipmentAssemblyResultDto>>> DeleteEquipmentAssemblyCollectionAsync(
            IEnumerable<int> equipmentAssemblyIds, CancellationToken cancellationToken)
        {

            if (equipmentAssemblyIds is null)
            {
                return ResponseDtoHelper.BadRequest<IEnumerable<DeleteEquipmentAssemblyResultDto>>(NasbNiroMessages.InvalidData);
            }

            List<DeleteEquipmentAssemblyResultDto> deleteEquipmentAssemblyResultDtos = new List<DeleteEquipmentAssemblyResultDto>();

            foreach (int equipmentAssemblyId in equipmentAssemblyIds)
            {
                bool exception = false;
                ResponseDto<ResponseEquipmentAssemblyDto> result = null;

                try
                {
                    result = await this.DeleteEquipmentAssemblyAsync(equipmentAssemblyId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteEquipmentAssemblyResultDtos.Add(new DeleteEquipmentAssemblyResultDto()
                    {
                        Id = equipmentAssemblyId,
                        Success = false,
                        Message = NasbNiroMessages.EquipmentAssemblyDeletingException
                    });

                    continue;
                }

                DeleteEquipmentAssemblyResultDto deleteEquipmentAssemblyResultDto;
                deleteEquipmentAssemblyResultDto = new DeleteEquipmentAssemblyResultDto()
                {
                    Id = equipmentAssemblyId,
                    Success = result.Success
                };

                if (!result.Success)
                {
                    deleteEquipmentAssemblyResultDto.Message = result.Message;
                }

                deleteEquipmentAssemblyResultDtos.Add(deleteEquipmentAssemblyResultDto);
            }

            return ResponseDtoHelper.Ok(deleteEquipmentAssemblyResultDtos.AsEnumerable());
        }
        #endregion

        #region GetAllResponseEquipmentAssemblyDtoAsync
        public async EquipmentAssemblyPagedResult GetAllResponseEquipmentAssemblyDtoAsync(
            EquipmentAssemblyPaginationParams equipmentAssemblyPaginationParams, CancellationToken cancellationToken)
        {
            bool onlyNotDeleted = false;

            if (equipmentAssemblyPaginationParams is null)
            {
                return ResponseDtoHelper.BadRequest<PagedList<ResponseEquipmentAssemblyDto>>(NasbNiroMessages.InvalidData);
            }

            IQueryable<EquipmentAssembly> query = _equipmentAssemblies;

            if (equipmentAssemblyPaginationParams.IsDeleted.HasValue)
            {
                bool deleted = equipmentAssemblyPaginationParams.IsDeleted.Value;

                query = query.Where(equipmentAssembly =>
                    equipmentAssembly.Deleted == deleted);

                if (!deleted)
                {
                    onlyNotDeleted = true;
                }
            }

            if (!string.IsNullOrEmpty(equipmentAssemblyPaginationParams.Contains))
            {
                string[] strArray = equipmentAssemblyPaginationParams.Contains.Split(' ');

                query = query.Where(equipmentAssembly =>
                    equipmentAssembly.Code != null && strArray.Any(str => equipmentAssembly.Code.Contains(str)) ||
                    equipmentAssembly.Title != null && strArray.Any(str => equipmentAssembly.Title.Contains(str)));
            }

            int? parentId = equipmentAssemblyPaginationParams.ParentId.HasValue
                ? equipmentAssemblyPaginationParams.ParentId.Value
                : (int?)null;

            query = query.Where(x => x.PredecessorId == parentId);
            query = query.Where(equipmentAssembly =>
                equipmentAssembly.EquipmentId == equipmentAssemblyPaginationParams.EquipmentId);

            IQueryable<ResponseEquipmentAssemblyDto> resultQuery;

            if (equipmentAssemblyPaginationParams.IncludeTypes)
            {
                resultQuery = query.ProjectTo<ResponseEquipmentAssemblyDto>
                    (_mapper.ConfigurationProvider);
            }
            else
            {
                resultQuery = query.ProjectTo<ResponseEquipmentAssemblyWithoutTypesDto>
                    (_mapper.ConfigurationProvider);
            }

            PagedList<ResponseEquipmentAssemblyDto> data =
                await PagedList<ResponseEquipmentAssemblyDto>
                    .CreateAsync(resultQuery,
                        equipmentAssemblyPaginationParams.PageNumber,
                        equipmentAssemblyPaginationParams.PageSize, cancellationToken);

            if (onlyNotDeleted)
            {
                foreach (ResponseEquipmentAssemblyDto equipmentAssembly in data)
                {
                    if (equipmentAssembly.EquipmentBoms != null)
                    {
                        equipmentAssembly.EquipmentBoms =
                            equipmentAssembly.EquipmentBoms.Where(eb => eb.Deleted == false);

                        foreach (ResponseEquipmentBomDto equipmentBomDto in equipmentAssembly.EquipmentBoms)
                        {
                            if (equipmentBomDto.EquipmentProcesses != null)
                            {
                                equipmentBomDto.EquipmentProcesses =
                                    equipmentBomDto.EquipmentProcesses.Where(ep => ep.Deleted == false);
                            }
                        }
                    }
                }
            }

            IEnumerable<Task> tasks = data.Select(x => IncludeChildrenAsync(x, cancellationToken));
            await Task.WhenAll(tasks);

            return ResponseDtoHelper.Ok(data);
        }
        #endregion

        #region GetEquipmentAssemblyByIdAsync
        public async ValueTask<EquipmentAssembly> GetEquipmentAssemblyByIdAsync(int equipmentAssemblyId, CancellationToken cancellationToken, bool includeAllChildren = false)
        {
            //Better not to use AsNoTracking in this scope.
            IQueryable<EquipmentAssembly> query = _equipmentAssemblies;

            if (includeAllChildren)
            {
                query = query
                    .Include(x => x.EquipmentBoms)
                    .ThenInclude(x => x.EquipmentProcesses)
                    .ThenInclude(x => x.EquipmentProcessFiles);
            }

            EquipmentAssembly storageEquipmentAssembly = await query
               .SingleOrDefaultAsync(ea => ea.Id == equipmentAssemblyId, cancellationToken);

            return storageEquipmentAssembly;
        }
        #endregion

        #region GetResponseEquipmentAssemblyDtoAsync
        public async EquipmentAssemblyResult GetResponseEquipmentAssemblyDtoAsync(GetEquipmentAssemblyDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseEquipmentAssemblyDto>(NasbNiroMessages.InvalidData);
            }

            IQueryable<ResponseEquipmentAssemblyDto> result;

            if (dto.IncludeTypes)
            {
                result = _equipmentAssemblies
                    .ProjectTo<ResponseEquipmentAssemblyDto>(_mapper.ConfigurationProvider);
            }
            else
            {
                result = _equipmentAssemblies
                    .ProjectTo<ResponseEquipmentAssemblyWithoutTypesDto>(_mapper.ConfigurationProvider);
            }

            ResponseEquipmentAssemblyDto data = await result
                .SingleOrDefaultAsync(ea =>
                    ea.Id == dto.Id && ea.EquipmentId == dto.EquipmentId,
                    cancellationToken);

            await IncludeChildrenAsync(data, cancellationToken);

            if (data != null)
            {
                return ResponseDtoHelper.Ok(data);
            }

            return ResponseDtoHelper.NotFound<ResponseEquipmentAssemblyDto>(NasbNiroMessages.EquipmentAssemblyNotFound);
        }
        #endregion

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }

        public async ValueTask<bool> EquipmentAssemblyExistsAsync(int equipmentAssemblyId, CancellationToken cancellationToken)
        {
            bool result = await _equipmentAssemblies
                .AnyAsync(ea => ea.Id == equipmentAssemblyId, cancellationToken);

            return result;
        }

        public async ValueTask<bool> EquipmentAssemblyExistsAsync(int equipmentAssemblyId, bool isDeleted, CancellationToken cancellationToken)
        {
            bool result = await _equipmentAssemblies
                .AnyAsync(ea => ea.Id == equipmentAssemblyId && ea.Deleted == isDeleted, cancellationToken);

            return result;
        }

        private async Task IncludeChildrenAsync(ResponseEquipmentAssemblyDto data, CancellationToken cancellationToken)
        {
            if (data != null)
            {
                data.Children = await _equipmentAssemblies
                    .Where(x => !x.Deleted && x.PredecessorId == data.Id)
                    .ProjectTo<ResponseEquipmentAssemblyDto>(_mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);

                IEnumerable<Task> tasks = data.Children.Select(x => IncludeChildrenAsync(x, cancellationToken));

                await Task.WhenAll(tasks);
            }
        }
    }
}
