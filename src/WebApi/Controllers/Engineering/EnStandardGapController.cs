using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // ENG-E1: the standard-gap reports (made by the engineering agent, confirmed by the head of engineering).

    [ApiController]
    [Authorize]
    [Route("api/v1/engineering/standard-gaps")]
    [RolesCheckedInService]
    public class EnStandardGapController : ControllerBase
    {
        private readonly IEnStandardGapService _service;

        public EnStandardGapController(IEnStandardGapService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> ListAsync([FromQuery] EnGapQuery query, CancellationToken ct) => Ok((await _service.GetGapsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _service.GetGapAsync(id, ct));

        [HttpPost("confirm")]
        public async Task<IActionResult> ConfirmAsync([FromBody] EnGapConfirmDto dto, CancellationToken ct) => Ok(await _service.ConfirmAsync(dto, ct));
    }
}
