using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Holidays;
using ViewModels.NasbNiro.HolidayDtos;
using ViewModels.NasbNiro.Pagination;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   every write: PMO, HR, PLANNING
    [Authorize]
    public class HolidaysController : BaseApiController
    {
        private readonly IHolidaysService _holidaysService;

        public HolidaysController(IHolidaysService holidaysService)
        {
            _holidaysService = holidaysService ?? throw new System.ArgumentNullException(nameof(holidaysService));
        }

        [Authorize(Roles = LegacyRoles.HolidayWriters)]
        [HttpPost("add-day-of-week-to-holidays")]
        [ProducesResponseType(typeof(ResponseDto<ResponseHolidayDayDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseHolidayDayDto>>> AddDayOfWeekToHolidaysAsync(
            AddDayOfWeekDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _holidaysService.AddHolidayAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.HolidayWriters)]
        [HttpPost("add-date-to-holidays")]
        [ProducesResponseType(typeof(ResponseDto<ResponseHolidayDayDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseHolidayDateDto>>> AddDateToHolidaysAsync(
            AddHolidayDateDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _holidaysService.AddHolidayAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.HolidayWriters)]
        [HttpDelete("delete-day-of-week-from-holidays")]
        [ProducesResponseType(typeof(ResponseDto<ResponseHolidayDayDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseHolidayDayDto>>> DeleteDayOfWeekFromHolidaysAsync(
            DeleteDayOfWeekDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _holidaysService.DeleteHolidayAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.HolidayWriters)]
        [HttpDelete("delete-date-from-holidays")]
        [ProducesResponseType(typeof(ResponseDto<ResponseHolidayDateDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseHolidayDateDto>>> DeleteDateFromHolidaysAsync(
            DeleteHolidayDateDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _holidaysService.DeleteHolidayAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.HolidayWriters)]
        [HttpDelete("delete-holidates")]
        [ProducesResponseType(typeof(ResponseDto<ResponseHolidayDateDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseHolidayDateDto>>> DeleteHolidatesAsync(
            int[] holidatesIds, CancellationToken cancellationToken)
        {
            return Ok(await _holidaysService.DeleteHolidatesAsync(holidatesIds, cancellationToken));
        }

        [HttpGet("get-all-holiday-dates")]
        [ProducesResponseType(typeof(ResponseDto<ResponseHolidayDateDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseHolidayDateDto>>> GetAllHolidayDatesAsync(
            [FromQuery] HolidayDatePaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok(await _holidaysService.GetAllHolidaysAsync(paginationParams, cancellationToken));
        }

        [HttpGet("get-all-holiday-days")]
        [ProducesResponseType(typeof(ResponseDto<ResponseHolidayDateDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseHolidayDateDto>>> GetAllHolidayDaysAsync(
            [FromQuery] HolidayDayOfWeekPaginationParams pp, CancellationToken cancellationToken)
        {
            return Ok(await _holidaysService.GetAllHolidaysAsync(pp, cancellationToken));
        }
    }
}
