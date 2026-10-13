using System;
using System.ComponentModel.DataAnnotations;
using Common.GuardToolkit;

namespace Entities.Model
{
    public class HolidaysByDate
    {
        [Key]
        public int Id { get; set; }

        private DateTimeOffset _date { get; set; }
        public DateTimeOffset Date
        {
            get => _date;
            protected set
            {
                _date = value.OnlyDateNotHoursUtc();
            }
        }

        public static HolidaysByDate From(DateTimeOffset holidayDate)
            => new HolidaysByDate()
            {
                Date = holidayDate
            };
    }
}
