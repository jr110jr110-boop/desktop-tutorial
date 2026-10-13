using System;
using System.Threading.Tasks;

namespace Services.NasbNiro.DateTimeService
{
    public interface IDateTimeService
    {
        ValueTask<DateTimeOffset> GetNextValidDateAsync(DateTimeOffset date, int numberOfDaysAfterDate);
        ValueTask<DateTimeOffset> GetNextValidDateAsync(DateTimeOffset date, bool renewGenerator);
        ValueTask<DateTimeOffset> GetNextValidDateAsync(DateTimeOffset date);
        ValueTask<DateTimeOffset> GetNextValidDateAsync(DateTimeOffset date, Func<DateTimeOffset, DateTimeOffset> increaser);
        ValueTask<DateTimeOffset> GetNextValidDateAsync(DateTimeOffset date, Func<DateTimeOffset, DateTimeOffset> increaser, bool renewGenerator);
        void ResetMinDateForHolidates();
        void SetMinDateForHolidates(DateTimeOffset? date);
    }
}