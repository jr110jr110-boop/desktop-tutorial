using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.GuardToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Services.NasbNiro.Contracts;
using Services.Resources;
using ViewModels.NasbNiro.HolidayDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Holidays
{
    public class HolidaysService : IHolidaysService, ISaveChangesAsync
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly DbSet<HolidaysDayOfWeek> _dayOfWeek;
        private readonly DbSet<HolidaysByDate> _dates;

        public HolidaysService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));

            _dayOfWeek = _unitOfWork.Set<HolidaysDayOfWeek>().ThrowIfNull(nameof(_dayOfWeek));
            _dates = _unitOfWork.Set<HolidaysByDate>().ThrowIfNull(nameof(_dates));
        }

        public async ValueTask<ResponseDto<ResponseHolidayDayDto>> AddHolidayAsync(AddDayOfWeekDto dto, CancellationToken cancellationToken)
        {
            DayOfWeek dow = dto.DayOfWeek;

            bool isValid = Enum.IsDefined(typeof(DayOfWeek), dow);
            if (!isValid)
                return ResponseDtoHelper
                    .BadRequest<ResponseHolidayDayDto>(HolidaysMessages.DayOfWeekInvalid);

            HolidaysDayOfWeek dayOfWeek = await _dayOfWeek.SingleOrDefaultAsync(
                x => x.DayOfWeek == dow,
                cancellationToken);

            if (dayOfWeek != null)
                return ResponseDtoHelper.Ok<ResponseHolidayDayDto>(dayOfWeek, HolidaysMessages.DayOfWeekAlreadyExists);

            EntityEntry<HolidaysDayOfWeek> entityEntry = await _dayOfWeek.AddAsync(HolidaysDayOfWeek.From(dow), cancellationToken);

            if (await SaveChangesAsync(cancellationToken))
                return ResponseDtoHelper.Ok<ResponseHolidayDayDto>(entityEntry.Entity);

            return ResponseDtoHelper.BadRequest<ResponseHolidayDayDto>(HolidaysMessages.DayOfWeekNotCreated);
        }

        public async ValueTask<ResponseDto<ResponseHolidayDateDto>> AddHolidayAsync(
            AddHolidayDateDto dto, CancellationToken cancellationToken)
        {
            if (dto.HolidayDate <= DateTimeOffset.UtcNow)
                return ResponseDtoHelper.BadRequest<ResponseHolidayDateDto>(HolidaysMessages.DateCanNotBePassed);

            HolidaysByDate holiday = await _dates.SingleOrDefaultAsync(
                x => x.Date == dto.HolidayDate,
                cancellationToken);

            if (holiday != null)
                return ResponseDtoHelper.Ok<ResponseHolidayDateDto>(holiday, HolidaysMessages.DateAlreadyExists);

            EntityEntry<HolidaysByDate> entityEntry = await _dates.AddAsync(HolidaysByDate.From(dto.HolidayDate));

            if (await SaveChangesAsync(cancellationToken))
                return ResponseDtoHelper.Ok<ResponseHolidayDateDto>(entityEntry.Entity);

            return ResponseDtoHelper.BadRequest<ResponseHolidayDateDto>(HolidaysMessages.DateNotCreated);
        }

        public async ValueTask<ResponseDto<ResponseHolidayDayDto>> DeleteHolidayAsync(
            DeleteDayOfWeekDto dto, CancellationToken cancellationToken)
        {
            HolidaysDayOfWeek holiday = await _dayOfWeek.SingleOrDefaultAsync(
                x => x.DayOfWeek == dto.DayOfWeek,
                cancellationToken);

            if (holiday is null)
                return ResponseDtoHelper.NotFound<ResponseHolidayDayDto>(HolidaysMessages.DayOfWeekNotFound);

            EntityEntry<HolidaysDayOfWeek> entityEntry = _dayOfWeek.Remove(holiday);

            if (await SaveChangesAsync(cancellationToken))
                return ResponseDtoHelper.Ok<ResponseHolidayDayDto>(entityEntry.Entity);

            return ResponseDtoHelper.BadRequest<ResponseHolidayDayDto>(HolidaysMessages.DayOfWeekNotDeleted);
        }

        public async ValueTask<ResponseDto<ResponseHolidayDateDto>> DeleteHolidayAsync(
            DeleteHolidayDateDto dto, CancellationToken cancellationToken)
        {
            HolidaysByDate holiday = await _dates
                .SingleOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);

            if (holiday is null)
                return ResponseDtoHelper.NotFound<ResponseHolidayDateDto>(HolidaysMessages.DateNotFound);

            EntityEntry<HolidaysByDate> entityEntry = _dates.Remove(holiday);

            if (await SaveChangesAsync(cancellationToken))
                return ResponseDtoHelper.Ok<ResponseHolidayDateDto>(entityEntry.Entity);

            return ResponseDtoHelper.BadRequest<ResponseHolidayDateDto>(HolidaysMessages.DateNotDeleted);
        }

        public async ValueTask<ResponseDto<object>> DeleteHolidatesAsync(
            int[] holidatesId, CancellationToken cancellationToken)
        {
            IEnumerable<HolidaysByDate> datesToDelete = await _dates
                .Where(x => holidatesId.Contains(x.Id))
                .ToListAsync(cancellationToken);

            _dates.RemoveRange(datesToDelete);

            if (await SaveChangesAsync(cancellationToken))
                return ResponseDtoHelper.Ok(new object());

            return ResponseDtoHelper.BadRequest<object>(HolidaysMessages.DeletingRangeOfDatesFailed);
        }

        public async ValueTask<ResponseDto<PagedList<ResponseHolidayDateDto>>> GetAllHolidaysAsync(
            HolidayDatePaginationParams paginationParams, CancellationToken cancellationToken)
        {
            IQueryable<ResponseHolidayDateDto> query = _dates
                .Select(x => new ResponseHolidayDateDto()
                {
                    Id = x.Id,
                    HolidayDate = x.Date,
                });

            var result = await PagedList<ResponseHolidayDateDto>.CreateAsync(
                query,
                paginationParams.PageNumber,
                paginationParams.PageSize,
                cancellationToken);

            return ResponseDtoHelper.Ok(result);
        }

        public async ValueTask<ResponseDto<PagedList<ResponseHolidayDayDto>>> GetAllHolidaysAsync(HolidayDayOfWeekPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            IQueryable<ResponseHolidayDayDto> query = _dayOfWeek
               .Select(x => new ResponseHolidayDayDto()
               {
                   Id = x.Id,
                   DayOfWeek = x.DayOfWeek,
               });

            var result = await PagedList<ResponseHolidayDayDto>.CreateAsync(
                query,
                paginationParams.PageNumber,
                paginationParams.PageSize,
                cancellationToken);

            return ResponseDtoHelper.Ok(result);
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken) != 0;
        }

    }
}
