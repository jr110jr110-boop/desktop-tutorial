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
    // ENG-E1: standards deviations (decided by the head of engineering and the CEO).

    [ApiController]
    [Authorize]
    [Route("api/v1/engineering/deviations")]
    [RolesCheckedInService]
    public class EnDeviationController : ControllerBase
    {
        private readonly IEnDeviationService _service;

        public EnDeviationController(IEnDeviationService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> ListAsync([FromQuery] EnDeviationQuery query, CancellationToken ct) => Ok((await _service.GetDeviationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _service.GetDeviationAsync(id, ct));

        [HttpPost("decide")]
        public async Task<IActionResult> DecideAsync([FromBody] EnDeviationDecisionDto dto, CancellationToken ct) => Ok(await _service.DecideAsync(dto, ct));
    }
}
