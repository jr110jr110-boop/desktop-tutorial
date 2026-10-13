using System;
using DNTPersianUtils.Core;
using Entities.Model;

namespace ViewModels.NasbNiro.HolidayDtos
{
    public class ResponseHolidayDayDto
    {
        public int Id { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public string Description { get => DayOfWeek.GetPersianWeekDayName(); }

        public static implicit operator ResponseHolidayDayDto(HolidaysDayOfWeek entity)
        => new ResponseHolidayDayDto()
        {
            Id = entity.Id,
            DayOfWeek = entity.DayOfWeek,
        };
    }
}
