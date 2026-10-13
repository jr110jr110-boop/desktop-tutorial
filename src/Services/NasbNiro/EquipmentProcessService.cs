using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Equipments;
using Entities.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.NasbNiro.Contracts;
using Services.NasbNiro.Extensions;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.EquipmentProcessDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro
{
    public class EquipmentProcessService : IEquipmentProcessService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEquipmentBomService _equipmentBomService;
        private readonly ILogger<EquipmentProcessService> _logger;
        private readonly IMapper _mapper;
        private readonly IDocumentService _documentService;
        private readonly DbSet<EquipmentProcess> _equipmentProcesss;
        private readonly DbSet<EquipmentProcessFile> _equipmentProcessFiles;

        private IQueryable<EquipmentProcess> _equipmentProcesssQueryable
            => _equipmentProcesss.AsNoTracking().AsQueryable();

        public EquipmentProcessService(IUnitOfWork unitOfWork,
                                        IEquipmentBomService equipmentBomService,
                                        ILogger<EquipmentProcessService> logger,
                                        IMapper mapper,
                                        IDocumentService documentService)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _equipmentBomService = equipmentBomService.ThrowIfNull(nameof(equipmentBomService));

            _equipmentProcesss = _unitOfWork.Set<EquipmentProcess>().ThrowIfNull(nameof(_equipmentProcesss));
            _equipmentProcessFiles = _unitOfWork.Set<EquipmentProcessFile>().ThrowIfNull(nameof(_equipmentProcessFiles));

            _logger = logger.ThrowIfNull(nameof(logger));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _documentService = documentService.ThrowIfNull(nameof(documentService));
        }

        public async ValueTask<ResponseDto<ResponseEquipmentProcessDto>> AddEquipmentProcessAsync
            (AddEquipmentProcessDto addEquipmentProcessDto, CancellationToken cancellationToken)
        {
            if (addEquipmentProcessDto is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseEquipmentProcessDto>(NasbNiroMessages.InvalidData);
            }

            if (!ValidateInputDto(addEquipmentProcessDto, out string message, cancellationToken))
            {
                return ResponseDtoHelper.BadRequest<ResponseEquipmentProcessDto>(message);
            }

            bool equipmentBomExists = await _equipmentBomService.EquipmentBomExistsAsync(
                addEquipmentProcessDto.EquipmentBomId, false, cancellationToken);

            if (!equipmentBomExists)
            {
                return ResponseDtoHelper.NotFound<ResponseEquipmentProcessDto>(NasbNiroMessages.EquipmentBomNotFound);
            }

            EquipmentProcess EquipmentProcessToAdd =
                _mapper.Map<EquipmentProcess>(addEquipmentProcessDto);

            EntityEntry<EquipmentProcess> entityEntry =
                await _equipmentProcesss.AddAsync(EquipmentProcessToAdd);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                entityEntry.Entity.Sort = entityEntry.Entity.Id;
                await this.SaveChangesAsync(cancellationToken);

                return await this.GetResponseEquipmentProcessDtoAsync(getEquipmentProcessDto:
                    new GetEquipmentProcessDto()
                    {
                        Id = entityEntry.Entity.Id,
                        EquipmentBomId = entityEntry.Entity.EquipmentBomId,
                        IncludeTypes = false
                    }, cancellationToken: cancellationToken);
            }

            return ResponseDtoHelper.BadRequest<ResponseEquipmentProcessDto>
                (NasbNiroMessages.EquipmentProcessNotAdded);
        }

        public async ValueTask<ResponseDto<EquipmentProcessUpdateResponseDto>> UpdateEquipmentProcessAsync
            (UpdateEquipmentProcessDto updateEquipmentProcessDto, CancellationToken cancellationToken)
        {
            if (updateEquipmentProcessDto is null)
                return ResponseDtoHelper
                    .BadRequest<EquipmentProcessUpdateResponseDto>(NasbNiroMessages.InvalidData);

            if (!ValidateInputDto(updateEquipmentProcessDto, out string message, cancellationToken))
                return ResponseDtoHelper
                    .BadRequest<EquipmentProcessUpdateResponseDto>(message);

            EquipmentProcess equipmentProcess =
                await this.GetEquipmentProcessByIdAsync(updateEquipmentProcessDto.Id, cancellationToken);

            if (equipmentProcess is null || equipmentProcess.Deleted)
                return ResponseDtoHelper
                    .NotFound<EquipmentProcessUpdateResponseDto>(NasbNiroMessages.EquipmentProcessNotFound);

            if (equipmentProcess.EquipmentBomId != updateEquipmentProcessDto.EquipmentBomId)
                return ResponseDtoHelper
                    .BadRequest<EquipmentProcessUpdateResponseDto>
                        (NasbNiroMessages.EquipmentBomEquipmentProcessNotMatched);

            _mapper.Map(updateEquipmentProcessDto, equipmentProcess);

            EntityEntry<EquipmentProcess> entityEntry =
                _equipmentProcesss.Update(equipmentProcess);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(await _equipmentProcesss
                    .Where(x => !x.Deleted)
                    .Where(x => x.Id == entityEntry.Entity.Id)
                    .Where(x => x.EquipmentBomId == entityEntry.Entity.EquipmentBomId)
                    .ProjectTo<EquipmentProcessUpdateResponseDto>(_mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(cancellationToken));
            }

            return ResponseDtoHelper
                .BadRequest<EquipmentProcessUpdateResponseDto>(NasbNiroMessages.EquipmentProcessNotUpdated);
        }

        public async ValueTask<ResponseDto<ResponseEquipmentProcessDto>> DeleteEquipmentProcessAsync
            (int equipmentProcessId, CancellationToken cancellationToken)
        {
            EquipmentProcess equipmentProcess = await GetEquipmentProcessByIdAsync(equipmentProcessId, cancellationToken);

            if (equipmentProcess is null)
                return ResponseDtoHelper
                    .NotFound<ResponseEquipmentProcessDto>(NasbNiroMessages.EquipmentProcessNotFound);

            if (equipmentProcess.Deleted == true)
                return ResponseDtoHelper
                    .BadRequest<ResponseEquipmentProcessDto>(NasbNiroMessages.EquipmentProcessAlreadyIsDeleted);

            equipmentProcess.Delete();

            EntityEntry<EquipmentProcess> entityEntry = _equipmentProcesss.Update(equipmentProcess);

            if (await this.SaveChangesAsync(cancellationToken))
                return await this.GetResponseEquipmentProcessDtoAsync(getEquipmentProcessDto:
                    new GetEquipmentProcessDto()
                    {
                        Id = entityEntry.Entity.Id,
                        EquipmentBomId = entityEntry.Entity.EquipmentBomId,
                        IncludeTypes = false
                    }, cancellationToken: cancellationToken);

            return ResponseDtoHelper
                .BadRequest<ResponseEquipmentProcessDto>(NasbNiroMessages.EquipmentProcessNotDeleted);
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteEquipmentProcessResultDto>>> DeleteEquipmentProcessCollectionAsync(
            IEnumerable<int> equipmentProcessIds, CancellationToken cancellationToken)
        {
            if (equipmentProcessIds is null)
                return ResponseDtoHelper
                    .BadRequest<IEnumerable<DeleteEquipmentProcessResultDto>>(NasbNiroMessages.InvalidData);

            List<DeleteEquipmentProcessResultDto> deleteEquipmentProcessResultDtos = new List<DeleteEquipmentProcessResultDto>();

            foreach (int equipmentProcessId in equipmentProcessIds)
            {
                bool exception = false;
                ResponseDto<ResponseEquipmentProcessDto> result = null;

                try
                {
                    result = await this.DeleteEquipmentProcessAsync(equipmentProcessId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteEquipmentProcessResultDtos.Add(new DeleteEquipmentProcessResultDto()
                    {
                        Id = equipmentProcessId,
                        Success = false,
                        Message = NasbNiroMessages.EquipmentProcessDeletingException
                    });

                    continue;
                }

                DeleteEquipmentProcessResultDto deleteEquipmentProcessResultDto =
                    new DeleteEquipmentProcessResultDto() { Id = equipmentProcessId, Success = result.Success };

                if (!result.Success)
                {
                    deleteEquipmentProcessResultDto.Message = result.Message;
                }

                deleteEquipmentProcessResultDtos.Add(deleteEquipmentProcessResultDto);
            }

            return ResponseDtoHelper
                .Ok(deleteEquipmentProcessResultDtos.AsEnumerable());
        }

        public async ValueTask<ResponseDto<UpdateEquipmentProcessSortResponseDto>> UpdateSortAsync(
            UpdateEquipmentProcessSortDto updateEquipmentProcessSortDto, CancellationToken cancellationToken)
        {
            if (updateEquipmentProcessSortDto is null)
                return ResponseDtoHelper.BadRequest<UpdateEquipmentProcessSortResponseDto>
                    (NasbNiroMessages.InvalidData);

            EquipmentProcess ep_1 =
                await this.GetEquipmentProcessByIdAsync(updateEquipmentProcessSortDto.Id1, cancellationToken);

            EquipmentProcess ep_2 =
                await this.GetEquipmentProcessByIdAsync(updateEquipmentProcessSortDto.Id2, cancellationToken);

            if (ep_1 is null)
                return ResponseDtoHelper.NotFound<UpdateEquipmentProcessSortResponseDto>
                    (NasbNiroMessages.EquipmentProcessNotFound + $" ID: {updateEquipmentProcessSortDto.Id1}");

            if (ep_2 is null)
                return ResponseDtoHelper.NotFound<UpdateEquipmentProcessSortResponseDto>
                    (NasbNiroMessages.EquipmentProcessNotFound + $" ID: {updateEquipmentProcessSortDto.Id2}");

            (ep_1.Sort, ep_2.Sort) = (ep_2.Sort, ep_1.Sort);

            EntityEntry<EquipmentProcess> ee1 = _equipmentProcesss.Update(ep_1);
            EntityEntry<EquipmentProcess> ee2 = _equipmentProcesss.Update(ep_2);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(
                        new UpdateEquipmentProcessSortResponseDto()
                        {
                            Sort1 = ee1.Entity.Sort,
                            Sort2 = ee2.Entity.Sort
                        });
            }

            return ResponseDtoHelper.BadRequest<UpdateEquipmentProcessSortResponseDto>
                (NasbNiroMessages.UpdateFailed);
        }

        public async ValueTask<ResponseDto<PagedList<ResponseEquipmentProcessDto>>> GetAllResponseEquipmentProcessDtoAsync(
            EquipmentProcessPaginationParams equipmentProcessPaginationParams, CancellationToken cancellationToken)
        {
            if (equipmentProcessPaginationParams is null)
            {
                return ResponseDtoHelper.BadRequest<PagedList<ResponseEquipmentProcessDto>>(NasbNiroMessages.InvalidData);
            }

            IQueryable<EquipmentProcess> query = _equipmentProcesssQueryable;

            if (equipmentProcessPaginationParams.IsDeleted.HasValue)
                query = query.Where(equipmentProcess =>
                        equipmentProcess.Deleted == equipmentProcessPaginationParams.IsDeleted);

            #region stringParams
            if (!string.IsNullOrEmpty(equipmentProcessPaginationParams.Contains))
            {
                string[] strArray = equipmentProcessPaginationParams
                    .Contains.Split(' ');

                query = query.Where(x =>
                    x.EnterCommodityCode != null && strArray.Any(str => x.EnterCommodityCode.Contains(str)) ||
                    x.ExitCommodityCode != null && strArray.Any(str => x.ExitCommodityCode.Contains(str)));
            }
            #endregion

            #region numericalParams
            query = query.Where(equipmentProcess => equipmentProcess.EquipmentBomId ==
                equipmentProcessPaginationParams.EquipmentBomId);

            if (equipmentProcessPaginationParams.MapDurationMoreThan.HasValue)
            {
                query = query.Where(equipmentProcess => equipmentProcess.MapDuration >=
                    equipmentProcessPaginationParams.MapDurationMoreThan);
            }

            if (equipmentProcessPaginationParams.MapDurationLessThan.HasValue)
            {
                query = query.Where(equipmentProcess => equipmentProcess.MapDuration <=
                    equipmentProcessPaginationParams.MapDurationLessThan);
            }

            if (equipmentProcessPaginationParams.MasterDurationMoreThan.HasValue)
            {
                query = query.Where(equipmentProcess => equipmentProcess.MasterDuration >=
                    equipmentProcessPaginationParams.MasterDurationMoreThan);
            }

            if (equipmentProcessPaginationParams.MasterDurationLessThan.HasValue)
            {
                query = query.Where(equipmentProcess => equipmentProcess.MasterDuration <=
                    equipmentProcessPaginationParams.MasterDurationLessThan);
            }

            if (equipmentProcessPaginationParams.ProcessDurationLessThan.HasValue)
            {
                query = query.Where(equipmentProcess => equipmentProcess.ProcessDuration <=
                    equipmentProcessPaginationParams.ProcessDurationLessThan);
            }

            if (equipmentProcessPaginationParams.ProcessDurationMoreThan.HasValue)
            {
                query = query.Where(equipmentProcess => equipmentProcess.ProcessDuration >=
                    equipmentProcessPaginationParams.ProcessDurationMoreThan);
            }

            if (equipmentProcessPaginationParams.ProgramPriceLessThan.HasValue)
            {
                query = query.Where(equipmentProcess => equipmentProcess.ProgramPrice <=
                    equipmentProcessPaginationParams.ProgramPriceLessThan);
            }

            if (equipmentProcessPaginationParams.ProgramPriceMoreThan.HasValue)
            {
                query = query.Where(equipmentProcess => equipmentProcess.ProgramPrice >=
                    equipmentProcessPaginationParams.ProgramPriceMoreThan);
            }

            #endregion

            if (equipmentProcessPaginationParams.WithTypes != null)
            {
                query.Where(equipmentProcess
                    => equipmentProcessPaginationParams
                            .WithTypes.Any(type => equipmentProcess.Type == type));
            }

            query = query.OrderBy(ep => ep.Sort);

            IQueryable<ResponseEquipmentProcessDto> resultQuery;

            if (equipmentProcessPaginationParams.IncludeTypes)
            {
                resultQuery = query.ProjectTo<ResponseEquipmentProcessDto>
                    (_mapper.ConfigurationProvider);
            }
            else
            {
                resultQuery = query.ProjectTo<ResponseEquipmentProcessWithoutTypesDto>
                    (_mapper.ConfigurationProvider);
            }


            Task<PagedList<ResponseEquipmentProcessDto>> data =
                PagedList<ResponseEquipmentProcessDto>
                            .CreateAsync(resultQuery,
                                         equipmentProcessPaginationParams.PageNumber,
                                         equipmentProcessPaginationParams.PageSize, cancellationToken);

            return ResponseDtoHelper.Ok(await data);
        }

        public async ValueTask<EquipmentProcess> GetEquipmentProcessByIdAsync(int EquipmentProcessId, CancellationToken cancellationToken)
        {
            EquipmentProcess equipmentProcess =
               await _equipmentProcesssQueryable.SingleOrDefaultAsync(ea => ea.Id == EquipmentProcessId, cancellationToken);

            return equipmentProcess;
        }

        public async ValueTask<ResponseDto<ResponseEquipmentProcessDto>>
            GetResponseEquipmentProcessDtoAsync(GetEquipmentProcessDto getEquipmentProcessDto, CancellationToken cancellationToken)
        {
            if (getEquipmentProcessDto is null)
                return ResponseDtoHelper.BadRequest<ResponseEquipmentProcessDto>
                    (NasbNiroMessages.InvalidData);

            IQueryable<ResponseEquipmentProcessDto> query;

            if (getEquipmentProcessDto.IncludeTypes)
            {
                query = _equipmentProcesssQueryable
                    .ProjectTo<ResponseEquipmentProcessDto>(_mapper.ConfigurationProvider);
            }
            else
            {
                query = _equipmentProcesssQueryable
                    .ProjectTo<ResponseEquipmentProcessWithoutTypesDto>(_mapper.ConfigurationProvider);
            }

            ResponseEquipmentProcessDto result = await query
                .SingleOrDefaultAsync(ep =>
                    ep.Id == getEquipmentProcessDto.Id &&
                    ep.EquipmentBomId == getEquipmentProcessDto.EquipmentBomId,
                    cancellationToken);

            if (result != null)
                return ResponseDtoHelper.Ok(result);

            return ResponseDtoHelper
                .NotFound<ResponseEquipmentProcessDto>(NasbNiroMessages.EquipmentProcessNotFound);
        }

        public bool ValidateInputDto(EquipmentProcessDtoBase equipmentProcessDtoBase, out string message, CancellationToken cancellationToken)
        {
            if (equipmentProcessDtoBase is null)
            {
                throw new ArgumentNullException(nameof(equipmentProcessDtoBase));
            }

            StringBuilder @string = new StringBuilder();
            bool result;

            switch (equipmentProcessDtoBase.Type)
            {
                case EquipmentProcessType.Buy:
                    if (string.IsNullOrWhiteSpace(equipmentProcessDtoBase.EnterCommodityCode))
                    {
                        @string.AppendLine("کد ورودی نامعتبر است");
                    }
                    break;
                case EquipmentProcessType.Service:
                    if (string.IsNullOrWhiteSpace(equipmentProcessDtoBase.EnterCommodityCode))
                    {
                        @string.AppendLine("کد ورودی نامعتبر است");
                    }
                    if (string.IsNullOrWhiteSpace(equipmentProcessDtoBase.ExitCommodityCode))
                    {
                        @string.AppendLine("کد خروجی نامعتبر است");
                    }
                    break;
                case EquipmentProcessType.Assembly:
                    break;
                case EquipmentProcessType.FinalAssembly:
                    break;
                case EquipmentProcessType.Construction:
                    break;
                default:
                    @string.AppendLine("مقدار type پشتیبانی نمیشود");
                    break;
            }

            message = @string.ToString();
            result = string.IsNullOrWhiteSpace(message);

            return result;
        }

        public async ValueTask<ResponseDto<string>> AttachFileAsync(AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken)
        {
            attachFileDto.Validate();

            EquipmentProcessFile equipmentProcessFile = new EquipmentProcessFile();

            equipmentProcessFile.EquipmentProcessId = attachFileDto.EntityId;

            Guid documentId = await _documentService.UploadDocumentAsync(uploadFileDto: attachFileDto);

            equipmentProcessFile.DocumentId = documentId;

            await _equipmentProcessFiles.AddAsync(equipmentProcessFile);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(documentId.ToString());
            }

            return ResponseDtoHelper.BadRequest<string>(NasbNiroMessages.FileNotAdded);
        }

        public async ValueTask<ResponseDto<bool>> DetachFileAsync(int equipmentProcessId, Guid documentId, CancellationToken cancellationToken)
        {
            EquipmentProcessFile result = await _equipmentProcessFiles.SingleOrDefaultAsync
                (equipmentProcessFile =>
                    equipmentProcessFile.DocumentId == documentId &&
                    equipmentProcessFile.EquipmentProcessId == equipmentProcessId,
                    cancellationToken);

            if (result is null)
                return ResponseDtoHelper.NotFound<bool>(NasbNiroMessages.EquipmentProcessFileNotFound);

            EntityEntry<EquipmentProcessFile> entityEntry = _equipmentProcessFiles.Remove(result);

            if (!await this.SaveChangesAsync(cancellationToken))
                return ResponseDtoHelper.BadRequest<bool>(NasbNiroMessages.EquipmentProcessFileNotDeleted);

            bool deleteDocumentResult = await _documentService.DeleteDocumentAsync(documentId);

            if (!deleteDocumentResult)
                return ResponseDtoHelper
                         .BadRequest<bool>(NasbNiroMessages.DocumentNotDeleted);

            return ResponseDtoHelper
                         .Ok(true, message: NasbNiroMessages.EquipmentProcessFileDeleted);
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }

        public async Task<ResponseDto<string>> FinalizeDocumentAsync(FinalizeEquipmentProcessFileDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
            {
                return ResponseDtoHelper.BadRequest<string>("ورودی نامعتبر");
            }

            EquipmentProcessFile processFile = await _equipmentProcessFiles.SingleOrDefaultAsync(
                x => x.EquipmentProcessId == dto.EquipmentProcessId && x.DocumentId == dto.DocumentId,
                cancellationToken);

            if (processFile is null)
            {
                return ResponseDtoHelper.NotFound<string>("فایل یافت نشد");
            }

            //processFile.IsFinal = true;
            _equipmentProcessFiles.Update(processFile);

            await SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper.Ok<string>("فایل نهایی شد.");
        }

        //        public async Task<ResponseDto<ResponseEquipmentProcessFileStatusDto>> GetProcessFileStatusAsync(int processId)
        //{
        //            ResponseEquipmentProcessFileStatusDto result = await _equipmentProcesss
        //                .Where(x => !x.Deleted && x.Id == processId)
        //                .ProjectTo<ResponseEquipmentProcessFileStatusDto>(_mapper.ConfigurationProvider)
        //                .SingleOrDefaultAsync();

        //            if (result is null)
        //            {
        //                return ResponseDtoHelper.BadRequest<ResponseEquipmentProcessFileStatusDto>(
        //                    Messages.Notfound);
        //            }

        //            return ResponseDtoHelper.Ok(result);
        //        }

        public async Task<ResponseDto<IEnumerable<ResponseEquipmentProcessFileStatusDto>>> GetProcessesFileStatusByEquipmentIdAsync(
            int equipmentId, CancellationToken cancellationToken)
        {
            IEnumerable<ResponseEquipmentProcessFileStatusDto> result = await _equipmentProcessFiles
                  .Where(x => x.EquipmentProcess.EquipmentBom.EquipmentAssembly.EquipmentId == equipmentId)
                  .Where(x => !x.EquipmentProcess.Deleted)
                  .ProjectTo<ResponseEquipmentProcessFileStatusDto>(_mapper.ConfigurationProvider)
                  .ToListAsync(cancellationToken);

            return ResponseDtoHelper.Ok(result);
        }
    }
}
