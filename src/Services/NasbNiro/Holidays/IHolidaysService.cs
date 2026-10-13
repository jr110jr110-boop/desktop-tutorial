using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using ViewModels.NasbNiro.HolidayDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Holidays
{
    public interface IHolidaysService
    {
        ValueTask<ResponseDto<ResponseHolidayDayDto>> AddHolidayAsync(AddDayOfWeekDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseHolidayDayDto>> DeleteHolidayAsync(DeleteDayOfWeekDto dto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<ResponseHolidayDateDto>> AddHolidayAsync(AddHolidayDateDto dto, CancellationToken cancellationToken);
        ValueTask<ResponseDto<ResponseHolidayDateDto>> DeleteHolidayAsync(DeleteHolidayDateDto dto, CancellationToken cancellationToken);

        ValueTask<ResponseDto<PagedList<ResponseHolidayDayDto>>>
            GetAllHolidaysAsync(HolidayDayOfWeekPaginationParams paginationParams, CancellationToken cancellationToken);

        ValueTask<ResponseDto<PagedList<ResponseHolidayDateDto>>>
            GetAllHolidaysAsync(HolidayDatePaginationParams paginationParams, CancellationToken cancellationToken);
        ValueTask<ResponseDto<object>> DeleteHolidatesAsync(int[] holidatesId, CancellationToken cancellationToken);
    }
}
