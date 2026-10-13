using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.HolidayDtos
{
    public class DeleteDayOfWeekDto
    {
        [Required]
        public DayOfWeek DayOfWeek { get; set; }
    }
}
