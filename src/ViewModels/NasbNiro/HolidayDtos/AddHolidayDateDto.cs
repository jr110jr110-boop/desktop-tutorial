using System;
using System.ComponentModel.DataAnnotations;
using Common.GuardToolkit;

namespace ViewModels.NasbNiro.HolidayDtos
{
    public class AddHolidayDateDto
    {
        private DateTimeOffset _holidayDate { get; set; }

        [Required]
        public DateTimeOffset HolidayDate
        {
            get => this._holidayDate;
            set
            {
                this._holidayDate = value.OnlyDateNotHoursUtc();
            }
        }
    }
}
