using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.HolidayDtos
{
    public class AddDayOfWeekDto
    {
        [Required]
        public DayOfWeek DayOfWeek { get; set; }
    }
}
