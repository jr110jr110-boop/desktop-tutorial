using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Identity.Constants;
using Entities.Model;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Entities.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Contracts.Identity;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.EquipmentDtos;
using ViewModels.NasbNiro.GeneratedAssembliesDtos;
using ViewModels.NasbNiro.Pagination;

//item1 is bomId || item2 is assemblyId \\
using EquipmentBomAndAssemblyId = System.ValueTuple<int, int>;

namespace Services.NasbNiro
{
    public class EquipmentService : IEquipmentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<EquipmentService> _logger;
        private readonly IMapper _mapper;
        private readonly IDocumentService _documentService;
        private readonly IApplicationUserManager _userManager;
        private readonly IApplicationRoleManager _roleManager;

        private readonly DbSet<Equipment> _equipments;
        private readonly DbSet<EquipmentAssembly> _equipmentAssemblies;
        private readonly DbSet<EquipmentBom> _equipmentBoms;

        private IQueryable<Equipment> _equipmentsQueryable => _equipments.AsNoTracking().AsQueryable();

        public EquipmentService
            (IUnitOfWork unitOfWork,
             ILogger<EquipmentService> logger,
             IMapper mapper,
             IDocumentService documentService,
             IApplicationUserManager userManager,
             IApplicationRoleManager roleManager)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _logger = logger.ThrowIfNull(nameof(logger));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _documentService = documentService.ThrowIfNull(nameof(documentService));
            _userManager = userManager.ThrowIfNull(nameof(userManager));
            _roleManager = roleManager.ThrowIfNull(nameof(roleManager));

            _equipments = _unitOfWork.Set<Equipment>().ThrowIfNull(nameof(_equipments));
            _equipmentAssemblies = _unitOfWork.Set<EquipmentAssembly>().ThrowIfNull(nameof(_equipmentAssemblies));
            _equipmentBoms = _unitOfWork.Set<EquipmentBom>().ThrowIfNull(nameof(_equipmentBoms));
        }

        public async ValueTask<ResponseDto<ResponseEquipmentDto>> AddEquipmentAsync(
            AddEquipmentDto addEquipmentDto, CancellationToken cancellationToken)
        {
            if (addEquipmentDto is null)
                return ResponseDtoHelper.BadRequest<ResponseEquipmentDto>(NasbNiroMessages.InvalidData);

            Equipment equipmentToAdd = _mapper.Map<Equipment>(addEquipmentDto);

            EntityEntry<Equipment> entityEntry = await _equipments.AddAsync(equipmentToAdd, cancellationToken);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseEquipmentDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseEquipmentDto>(NasbNiroMessages.EquipmentNotAdded);
        }

        public async ValueTask<ResponseDto<ResponseEquipmentDto>> UpdateEquipmentAsync(UpdateEquipmentDto updateEquipmentDto, CancellationToken cancellationToken)
        {
            if (updateEquipmentDto is null)
                return ResponseDtoHelper.BadRequest<ResponseEquipmentDto>(NasbNiroMessages.InvalidData);

            Equipment equipment = await this.GetEquipmentByIdAsync(updateEquipmentDto.Id, cancellationToken);

            if (equipment is null)
                return ResponseDtoHelper
                    .NotFound<ResponseEquipmentDto>(NasbNiroMessages.EquipmentNotFound);

            _mapper.Map(updateEquipmentDto, equipment);

            EntityEntry<Equipment> entityEntry = _equipments.Update(equipment);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseEquipmentDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseEquipmentDto>(NasbNiroMessages.EquipmentNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseEquipmentDto>> DeleteEquipmentAsync(int equipmentId, CancellationToken cancellationToken)
        {
            Equipment equipment = await this.GetEquipmentByIdAsync
                (equipmentId, cancellationToken, IncludeAllChildren: true);

            if (equipment is null)
                return ResponseDtoHelper
                    .NotFound<ResponseEquipmentDto>(NasbNiroMessages.EquipmentNotFound);

            if (equipment.Deleted == true)
                return ResponseDtoHelper
                    .BadRequest<ResponseEquipmentDto>(NasbNiroMessages.EquipmentAlreadyIsDeleted);

            // R1a: an equipment used by a contract, a work order or a sales estimate (or whose BOM lines are used) is not deleted
            string blocked = await LegacyDeleteGuard.EquipmentAsync(_unitOfWork, equipmentId, cancellationToken);
            if (blocked != null)
                return ResponseDtoHelper.BadRequest<ResponseEquipmentDto>(blocked);

            equipment.Delete();

            //delete children
            if (equipment.EquipmentAssemblies != null)
                foreach (EquipmentAssembly equipmentAssembly in equipment.EquipmentAssemblies)
                {
                    equipmentAssembly.Delete();

                    if (equipmentAssembly.EquipmentBoms != null)
                        foreach (EquipmentBom equipmentBom in equipmentAssembly.EquipmentBoms)
                        {
                            equipmentBom.Delete();

                            if (equipmentBom.EquipmentProcesses != null)
                                foreach (EquipmentProcess equipmentProcess in equipmentBom.EquipmentProcesses)
                                {
                                    equipmentProcess.Delete();
                                }
                        }
                }

            EntityEntry<Equipment> entityEntry = _equipments.Update(equipment);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseEquipmentDtoByIdAsync(entityEntry.Entity.Id, cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseEquipmentDto>(NasbNiroMessages.EquipmentNotDeleted);
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteEquipmentResultDto>>>
            DeleteEquipmentCollectionAsync(IEnumerable<int> equipmentIds, CancellationToken cancellationToken)
        {
            if (equipmentIds is null)
                return ResponseDtoHelper
                    .BadRequest<IEnumerable<DeleteEquipmentResultDto>>(NasbNiroMessages.InvalidData);

            List<DeleteEquipmentResultDto> deleteEquipmentResultDtos = new List<DeleteEquipmentResultDto>();

            foreach (int equipmentId in equipmentIds)
            {
                bool exception = false;
                ResponseDto<ResponseEquipmentDto> result = null;

                try
                {
                    result = await this.DeleteEquipmentAsync(equipmentId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteEquipmentResultDtos.Add(new DeleteEquipmentResultDto()
                    {
                        Id = equipmentId,
                        Success = false,
                        Message = NasbNiroMessages.EquipmentDeletingException
                    });

                    continue;
                }

                DeleteEquipmentResultDto deleteEquipmentResultDto
                    = new DeleteEquipmentResultDto() { Id = equipmentId, Success = result.Success };

                if (!result.Success)
                {
                    deleteEquipmentResultDto.Message = result.Message;
                }

                deleteEquipmentResultDtos.Add(deleteEquipmentResultDto);
            }

            return ResponseDtoHelper
                .Ok(deleteEquipmentResultDtos.AsEnumerable());
        }

        public async ValueTask<ResponseDto<ResponseEquipmentDto>>
            GetResponseEquipmentDtoByIdAsync(int equipmentId, CancellationToken cancellationToken)
        {
            ResponseEquipmentDto result = await _equipmentsQueryable
                .ProjectTo<ResponseEquipmentDto>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(
                    equipment => equipment.Id == equipmentId,
                    cancellationToken);

            if (result != null)
                return ResponseDtoHelper
                    .Ok(result);

            return ResponseDtoHelper.NotFound<ResponseEquipmentDto>(NasbNiroMessages.EquipmentNotFound);
        }

        public async ValueTask<ResponseDto<PagedList<ResponseEquipmentDto>>> GetAllResponseEquipmentDtoAsync(
            EquipmentPaginationParams equipmentPaginationParams, CancellationToken cancellationToken)
        {
            if (equipmentPaginationParams is null)
                return ResponseDtoHelper
                    .BadRequest<PagedList<ResponseEquipmentDto>>(NasbNiroMessages.InvalidData);

            IQueryable<Equipment> query = _equipmentsQueryable;

            if (equipmentPaginationParams.IsDeleted.HasValue)
                query = query.Where(equipment => equipment.Deleted == equipmentPaginationParams.IsDeleted);

            if (!string.IsNullOrEmpty(equipmentPaginationParams.Contains))
            {
                string[] strArray = equipmentPaginationParams.Contains.Split(' ');

                query = query.Where(equipment =>
                    equipment.Code != null && strArray.Any(str => equipment.Code.Contains(str)) ||
                    equipment.Title != null && strArray.Any(str => equipment.Title.Contains(str)) ||
                    equipment.Description != null && strArray.Any(str => equipment.Description.Contains(str)));
            }

            IQueryable<ResponseEquipmentDto> resultQuery;

            if (equipmentPaginationParams.IncludeTypes)
            {
                resultQuery = query.ProjectTo<ResponseEquipmentDto>
                    (_mapper.ConfigurationProvider);
            }
            else
            {
                resultQuery = query.ProjectTo<ResponseEquipmentWithoutTypesDto>
                    (_mapper.ConfigurationProvider);
            }


            Task<PagedList<ResponseEquipmentDto>> data =
                PagedList<ResponseEquipmentDto>
                            .CreateAsync(resultQuery,
                                         equipmentPaginationParams.PageNumber,
                                         equipmentPaginationParams.PageSize, cancellationToken);

            return ResponseDtoHelper.Ok(await data);
        }

        public async ValueTask<Equipment> GetEquipmentByIdAsync(int equipmentId, CancellationToken cancellationToken, bool IncludeAllChildren = false)
        {
            //Better not to use AsNoTracking in this scope.

            IQueryable<Equipment> query = _equipments.AsQueryable();

            if (IncludeAllChildren)
            {
                query = query
                    .Include(equipment => equipment.EquipmentAssemblies)
                        .ThenInclude(equipmentAssembly => equipmentAssembly.EquipmentBoms)
                            .ThenInclude(equipmentBom => equipmentBom.EquipmentProcesses);
            }

            Equipment result = await query
                .SingleOrDefaultAsync(equipment => equipment.Id == equipmentId, cancellationToken);

            return result;
        }


        public async ValueTask<bool> EquipmentExistsAsync(int equipmentId, bool isDeleted, CancellationToken cancellationToken)
        {
            bool result = await _equipments
                .AnyAsync(e => e.Id == equipmentId && e.Deleted == isDeleted, cancellationToken);

            return result;
        }

        public async ValueTask<bool> EquipmentExistsAsync(int equipmentId, CancellationToken cancellationToken)
        {
            bool result = await _equipments.AnyAsync(
                e => e.Id == equipmentId, cancellationToken);

            return result;
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }

        public async ValueTask<ResponseGeneratedAssembliesDto> GenerateAssembliesFromEquipmentAsync(int equipmentId, CancellationToken cancellationToken)
        {
            #region Result
            StringBuilder errors = new StringBuilder();
            ResponseGeneratedAssembliesDto result = new ResponseGeneratedAssembliesDto();
            result.Errors = errors;

            result.EquipmentExists = await _equipments.AnyAsync(x => x.Id == equipmentId, cancellationToken);

            if (!result.EquipmentExists)
                return result;
            #endregion

            #region Object Cache Definition
            Dictionary<EquipmentBomAndAssemblyId, Bom> bomCache = new Dictionary<EquipmentBomAndAssemblyId, Bom>();
            #endregion

            int userId = await GetDefaultProcessUserIdAsync(cancellationToken);

            #region Get Processes From Equipment Bom
            async ValueTask<ICollection<ProcessEntity>> GenerateProcessesFromEquipmentProcessesAsync(int bomId)
            {

                List<ProcessEntity> processes = new List<ProcessEntity>();

                IEnumerable<EquipmentProcess> equipmentProcesses = await _unitOfWork
                    .Set<EquipmentProcess>()
                    .Where(e => e.EquipmentBomId == bomId && !e.Deleted)
                    .ToListAsync(cancellationToken);

                if (equipmentProcesses == null)
                    return null;

                foreach (EquipmentProcess equipmentProcess in equipmentProcesses)
                {
                    ProcessEntity process = _mapper.Map<ProcessEntity>(equipmentProcess);

                    //process.SetUserId(userId);

                    IEnumerable<EquipmentProcessFile> documents =
                        await _unitOfWork
                        .Set<EquipmentProcessFile>()
                        .Where(x => x.EquipmentProcessId == equipmentProcess.Id)
                        .ToListAsync(cancellationToken);

                    if (documents == null)
                    {
                        continue;
                    }

                    //foreach (EquipmentProcessFile document in documents)
                    //{
                    //    try
                    //    {
                    //        ResponseDocumentDto responseDocumentDto =
                    //            await _documentService.DownloadDocumentAsync(document.Id);

                    //        IFormFile formFile = new FormFile
                    //            (responseDocumentDto.FileStream,
                    //            0,
                    //            responseDocumentDto.FileStream.Length,
                    //            null,
                    //            Path.GetFileName(document.Path));

                    //        Guid newFileId = await _documentService.UploadDocumentAsync(new UploadFileDto()
                    //        {
                    //            File = formFile,
                    //            Code = Code,
                    //            Name = document.Name,
                    //            Description = document.Description,
                    //        });

                    //        if (process.ProcessFiles == null)
                    //            process.ProcessFiles = new List<ProcessFile>();

                    //        process.ProcessFiles.Add(new ProcessFile()
                    //        {
                    //            DocumentId = newFileId
                    //        });
                    //    }
                    //    catch (Exception ex)
                    //    {
                    //        errors.AppendLine(ex.Message);
                    //    }
                    //}

                    processes.Add(process);
                }

                return processes;
            }
            #endregion

            #region Map Single Bom
            async ValueTask<Bom> MapSingleBom(EquipmentBom bomToMap)
            {
                Bom resultBom;
                resultBom = _mapper.Map<Bom>(bomToMap);
                resultBom.Processes = await GenerateProcessesFromEquipmentProcessesAsync(bomId: bomToMap.Id);

                return resultBom;
            }
            #endregion

            #region Map Boms First
            // Order is important , Predecessor Matters
            IEnumerable<EquipmentBom> bomsToMap = await _equipmentBoms
                .Where(x => !x.Deleted && x.EquipmentAssembly.EquipmentId == equipmentId)
                .OrderBy(x => x.PredecessorId)
                .ToListAsync();

            Queue<EquipmentBom> bomsPendingPredecessor = new Queue<EquipmentBom>(bomsToMap);

            while (bomsPendingPredecessor.Any())
            {
                EquipmentBom bomToMap = bomsPendingPredecessor.Dequeue();
                int? predecessorId = bomToMap.PredecessorId;

                EquipmentBomAndAssemblyId cacheKey = (bomToMap.Id, bomToMap.EquipmentAssemblyId);

                Bom resultBom;

                if (!predecessorId.HasValue)
                {
                    resultBom = await MapSingleBom(bomToMap);
                }
                else
                {
                    if (!bomCache.Keys.Any(x => x.Item1 == predecessorId.Value))
                    {
                        bomsPendingPredecessor.Enqueue(bomToMap);
                        throw new Exception("پیشنیاز BOM "+bomToMap.Title+" موجود نمی باشد");
                    }

                    resultBom = await MapSingleBom(bomToMap);
                    EquipmentBomAndAssemblyId predecessorKey = bomCache.Keys.Where(x => x.Item1 == predecessorId.Value).First();
                    resultBom.Predecessor = bomCache[predecessorKey];

                }

                bomCache.Add(cacheKey, resultBom);
            }
            #endregion

            List<AssemblyEntity> assemblies = new List<AssemblyEntity>();

            // Order is important , Predecessor Matters
            IEnumerable<EquipmentAssembly> equipmentAssemblies =
                await _unitOfWork
                .Set<EquipmentAssembly>()
                .Where(e => e.EquipmentId == equipmentId && !e.Deleted)
                .OrderBy(x => x.PredecessorId)
                .ToListAsync();

            var assembliesQueue = new Queue<EquipmentAssembly>(equipmentAssemblies);
            var equipmentAssemblyIdAndMappedAssembly = new Dictionary<int, AssemblyEntity>();

            while (assembliesQueue.Any())
            {
                EquipmentAssembly currentEquipmentAssembly = assembliesQueue.Dequeue();

                if (currentEquipmentAssembly.PredecessorId.HasValue &&
                    !equipmentAssemblyIdAndMappedAssembly.ContainsKey(currentEquipmentAssembly.PredecessorId.Value))
                {
                    assembliesQueue.Enqueue(currentEquipmentAssembly);
                    continue;
                }

                AssemblyEntity assembly = _mapper.Map<AssemblyEntity>(currentEquipmentAssembly);

                assembly.Boms = bomCache.Keys
                    .Where(x => x.Item2 == currentEquipmentAssembly.Id)
                    .Select(x => bomCache[x])
                    .ToList();

                if (currentEquipmentAssembly.PredecessorId is int number)
                {
                    //assembly.Predecessor = equipmentAssemblyIdAndMappedAssembly[currentEquipmentAssembly.PredecessorId.Value];
                    assembly.Predecessor = equipmentAssemblyIdAndMappedAssembly[number];
                }

                assemblies.Add(assembly);
                equipmentAssemblyIdAndMappedAssembly.Add(currentEquipmentAssembly.Id, assembly);
            }

            result.Assemblies = assemblies;
            return result;
        }

        private async ValueTask<int> GetDefaultProcessUserIdAsync(CancellationToken cancellationToken)
        {
            int userId;

            var roleAndUsers = await _roleManager.Roles.Include(r => r.Users)
                .FirstOrDefaultAsync(
                    r => r.NormalizedName.ToUpper() == RoleConstants.Engineer.ToUpper(),
                    cancellationToken);

            if (roleAndUsers == null || roleAndUsers.Users == null || roleAndUsers.Users.Count() == 0)
                roleAndUsers = await _roleManager.Roles
                    .Include(r => r.Users)
                    .FirstOrDefaultAsync(
                        r => r.NormalizedName.ToUpper() == RoleConstants.PMO.ToUpper(),
                        cancellationToken);

            userId = roleAndUsers.Users.Select(u => u.UserId).First();

            return userId;
        }
    }
}
