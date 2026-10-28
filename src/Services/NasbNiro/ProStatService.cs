using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProvisionalStatementDtos;

namespace Services.NasbNiro
{
    public class ProStatService : IProStatService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly DbSet<ContractEntity> _contracts;
        private readonly DbSet<ProvisionalStatement> _proStats;

        public ProStatService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _mapper = mapper.ThrowIfNull(nameof(unitOfWork));
            _contracts = unitOfWork.Set<ContractEntity>().ThrowIfNull(nameof(_contracts));
            _proStats = unitOfWork.Set<ProvisionalStatement>().ThrowIfNull(nameof(_proStats));
        }

        public async ValueTask<ResponseDto<ResponseProStatDto>> AddProStatAsync(
            AddProStatDto addProStatDto,
            CancellationToken cancellationToken)
        {
            bool contractExists = await _contracts.AnyAsync(c => !c.Deleted && c.Id == addProStatDto.ContractId);

            if (contractExists is false)
            {
                return ResponseDtoHelper.BadRequest<ResponseProStatDto>("قرارداد یافت نشد.");   // R1a2: the answer was built but never returned, so a statement for a missing contract was saved
            }

            ProvisionalStatement proStatToAdd = _mapper.Map<ProvisionalStatement>(addProStatDto);
            EntityEntry<ProvisionalStatement> entityEntry = await _proStats.AddAsync(proStatToAdd, cancellationToken);

            if (await this.SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseProStatDto>(entityEntry.Entity));
            }

            return ResponseDtoHelper.BadRequest<ResponseProStatDto>("افزودن صورت وضعیت شکست خورد");
        }

        public async ValueTask<ResponseDto<string>> DeleteProStatAsync(DeleteProStatDto deleteProStatDto, CancellationToken cancellationToken)
        {
            ProvisionalStatement proStatToDelete = await _proStats
                .SingleOrDefaultAsync(ProvideProStatGetExpression(deleteProStatDto.Id, deleteProStatDto.ContractId));

            if (proStatToDelete is null)
            {
                return ResponseDtoHelper.BadRequest<string>("صورت وضعیت یافت نشد.");
            }

            // R1a2 (M3): a statement that a payment point or a sales invoice refers to is not deleted
            string blocked = await LegacyDeleteGuard.ProvisionalStatementAsync(_unitOfWork, proStatToDelete.Id, true, cancellationToken);
            if (blocked != null)
            {
                return ResponseDtoHelper.BadRequest<string>(blocked);
            }

            proStatToDelete.Delete();

            if (await SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok("صورت وضعیت حذف شد.");
            }
            else
            {

            return ResponseDtoHelper.BadRequest<string>("حذف صورت وضعیت شکست خورد.");
            }

        }

        public async ValueTask<ResponseDto<ResponseProStatDto>> GetProStatAsync(GetProStatDto getProStatDto, CancellationToken cancellationToken)
        {
            var result = await _proStats
                .Where(ProvideProStatGetExpression(getProStatDto.Id , getProStatDto.ContractId))
                .ProjectTo<ResponseProStatDto>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(cancellationToken);

            if (result is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseProStatDto>("صورت وضعیت یافت نشد.");
            }
            else
            {
                return ResponseDtoHelper.Ok(result);
            }
        }

        public async ValueTask<ResponseDto<PagedList<ResponseProStatDto>>> GetProStatsAsync(
            ProStatPaginationParams @params,
            CancellationToken cancellationToken)
        {
            var query = _proStats
                .Where(ps => ps.ContractId == @params.ContractId)
                .ProjectTo<ResponseProStatDto>(_mapper.ConfigurationProvider);

            var pagedResult = await PagedList<ResponseProStatDto>.CreateAsync(
                query,
                @params.PageNumber,
                @params.PageSize,
                cancellationToken);

            return ResponseDtoHelper.Ok(pagedResult);
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) > 0;
        }

        public async ValueTask<ResponseDto<ResponseProStatDto>> UpdateProStatAsync(UpdateProStatDto updateProStatDto, CancellationToken cancellationToken)
        {
            ProvisionalStatement proStatToUpdate = await _proStats
                .SingleOrDefaultAsync(ProvideProStatGetExpression(updateProStatDto.Id , updateProStatDto.ContractId));

            if (proStatToUpdate is null)
            {
                return ResponseDtoHelper.BadRequest<ResponseProStatDto>("صورت وضعیت یافت نشد.");
            }

            // R1a2 (M3): a statement that a payment point or a sales invoice refers to is not edited
            string blocked = await LegacyDeleteGuard.ProvisionalStatementAsync(_unitOfWork, proStatToUpdate.Id, false, cancellationToken);
            if (blocked != null)
            {
                return ResponseDtoHelper.BadRequest<ResponseProStatDto>(blocked);
            }

            _mapper.Map(updateProStatDto, proStatToUpdate);
            _proStats.Update(proStatToUpdate);

            if (await SaveChangesAsync(cancellationToken))
            {
                return ResponseDtoHelper.Ok(_mapper.Map<ResponseProStatDto>(proStatToUpdate));
            }

            return ResponseDtoHelper.BadRequest<ResponseProStatDto>("به روزرسانی صورت وضعیت شکست خورد.");
        }

        #region Private Members
        private Expression<Func<ProvisionalStatement, bool>> ProvideProStatGetExpression(
            int id,
            int contractId,
            bool deleted = false)
        {
            return ps => ps.Id == id && ps.ContractId == contractId && ps.Deleted == deleted;
        }
        #endregion
    }
}
