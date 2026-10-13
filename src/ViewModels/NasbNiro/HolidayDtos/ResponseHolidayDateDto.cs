using System;
using DNTPersianUtils.Core;
using Entities.Model;

namespace ViewModels.NasbNiro.HolidayDtos
{
    public class ResponseHolidayDateDto
    {
        public int Id { get; set; }
        public DateTimeOffset HolidayDate { get; set; }
        public string Description { get => HolidayDate.DayOfWeek.GetPersianWeekDayName(); }

        public static implicit operator ResponseHolidayDateDto(HolidaysByDate entity)
            => new ResponseHolidayDateDto()
            {
                Id = entity.Id,
                HolidayDate = entity.Date,
            };
    }
}
