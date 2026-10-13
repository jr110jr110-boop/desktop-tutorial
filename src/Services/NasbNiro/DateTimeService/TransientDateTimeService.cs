using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.GuardToolkit;
using DataLayer.Context;
using Entities.Model;
using Microsoft.EntityFrameworkCore;
using NextDateGenerator = System.Func
    <System.DateTimeOffset, System.Func<System.DateTimeOffset, System.DateTimeOffset>,
        System.Threading.Tasks.ValueTask<System.DateTimeOffset>>;

namespace Services.NasbNiro.DateTimeService
{
    public class TransientDateTimeService : IDateTimeService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly DbSet<HolidaysDayOfWeek> _holidays;
        private IQueryable<HolidaysByDate> _holidates;
        private NextDateGenerator _nextDateGenerator;
        public Func<DateTimeOffset, DateTimeOffset> DefaultIncreaser =>
            x => x.AddDays(1);

        public TransientDateTimeService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork.ThrowIfNull(nameof(unitOfWork));
            _holidays = unitOfWork.Set<HolidaysDayOfWeek>().ThrowIfNull(nameof(_holidays));
            _holidates = GetQueryableHolidates();
        }

        public IQueryable<HolidaysByDate> GetQueryableHolidates()
        {
            return _unitOfWork.Set<HolidaysByDate>().ThrowIfNull(nameof(_holidates));
        }

        public void SetMinDateForHolidates(DateTimeOffset? date)
        {
            _holidates = _holidates.Where(x => x.Date >= date);
        }

        public void ResetMinDateForHolidates()
            => _holidates = GetQueryableHolidates();

        public async ValueTask<DateTimeOffset> GetNextValidDateAsync(
            DateTimeOffset date, int numberOfDaysAfterDate)
        {
            for (int i = 0; i < numberOfDaysAfterDate; i++)
            {
                date = await GetNextValidDateAsync(date);
            }

            return date;
        }

        public async ValueTask<DateTimeOffset> GetNextValidDateAsync(
            DateTimeOffset date, bool renewGenerator)
        {
            DateTimeOffset newDate = await GetNextValidDateAsync(date, DefaultIncreaser, renewGenerator);

            return newDate;
        }

        public async ValueTask<DateTimeOffset> GetNextValidDateAsync(
            DateTimeOffset date)
        {
            DateTimeOffset newDate = await GetNextValidDateAsync(date, DefaultIncreaser);

            return newDate;
        }

        public async ValueTask<DateTimeOffset> GetNextValidDateAsync(
            DateTimeOffset date,
            Func<DateTimeOffset, DateTimeOffset> increaser)
        {
            DateTimeOffset result = await GetNextValidDateAsync(date, increaser, renewGenerator: false);

            return result;
        }

        public async ValueTask<DateTimeOffset> GetNextValidDateAsync(
            DateTimeOffset date,
            Func<DateTimeOffset, DateTimeOffset> increaser,
            bool renewGenerator)
        {
            if (_nextDateGenerator is null || renewGenerator)
            {
                _nextDateGenerator = await GetNewGeneratorAsync();
            }

            return await _nextDateGenerator(date, increaser);
        }

        private async ValueTask<DateTimeOffset> GetNextValidDateAsync(
            DateTimeOffset date,
            Func<DateTimeOffset, DateTimeOffset> increaser,
            DayOfWeek[] holidaysOfWeeks)
        {
            date = date.OnlyDateNotHoursUtc();
            while (true)
            {
                date = increaser(date);
                if (holidaysOfWeeks.Contains(date.DayOfWeek)) continue;
                if (await _holidates.AnyAsync(x => x.Date == date)) continue;

                return date;
            }
        }

        private async ValueTask<NextDateGenerator> GetNewGeneratorAsync()
        {
            DayOfWeek[] holidaysOfWeeks = await _holidays
                      .Select(x => x.DayOfWeek)
                      .Distinct()
                      .ToArrayAsync();

            int totalWeekDaysCount = Enum.GetNames(typeof(DayOfWeek)).Length;

            if (totalWeekDaysCount == holidaysOfWeeks.Count())
                throw new Exception("همه روزهای هفته نمیتوانند تعطیل باشند");

            return (date, increaser) => GetNextValidDateAsync(date, increaser, holidaysOfWeeks);
        }
    }
}
