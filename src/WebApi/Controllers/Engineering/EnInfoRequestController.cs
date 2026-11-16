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
    // ENG-E1: the questions of the units to the PMO (only the asking unit, the PMO, the CEO and the auditor read them; the roles are checked in the service).

    [ApiController]
    [Authorize]
    [Route("api/v1/engineering/info-requests")]
    [RolesCheckedInService]
    public class EnInfoRequestController : ControllerBase
    {
        private readonly IEnInfoRequestService _service;

        public EnInfoRequestController(IEnInfoRequestService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> ListAsync([FromQuery] EnInfoQuery query, CancellationToken ct) => Ok((await _service.GetRequestsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _service.GetRequestAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> AskAsync([FromBody] EnInfoAskDto dto, CancellationToken ct) => Ok(await _service.AskAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] EnInfoActionDto dto, CancellationToken ct) => Ok(await _service.ActionAsync(dto, ct));
    }
}
