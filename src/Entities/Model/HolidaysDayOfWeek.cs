using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model
{
    public class HolidaysDayOfWeek
    {
        [Key]
        public int Id { get; protected set; }

        public DayOfWeek DayOfWeek { get; protected set; }

        public static HolidaysDayOfWeek From(DayOfWeek dayOfWeek)
            => new HolidaysDayOfWeek()
            {
                DayOfWeek = dayOfWeek
            };
    }
}
