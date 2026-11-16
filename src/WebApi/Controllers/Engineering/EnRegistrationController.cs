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
    // ENG-E1: the registration of a New design as a platform (the head of engineering proposes, the CEO decides; the roles are checked in the service).

    [ApiController]
    [Authorize]
    [Route("api/v1/engineering/platform-registrations")]
    [RolesCheckedInService]
    public class EnRegistrationController : ControllerBase
    {
        private readonly IEnRegistrationService _service;

        public EnRegistrationController(IEnRegistrationService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> ListAsync([FromQuery] EnRegistrationQuery query, CancellationToken ct) => Ok((await _service.GetRegistrationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _service.GetRegistrationAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> ProposeAsync([FromBody] EnRegistrationProposeDto dto, CancellationToken ct) => Ok(await _service.ProposeAsync(dto, ct));

        [HttpPost("decide")]
        public async Task<IActionResult> DecideAsync([FromBody] EnRegistrationDecisionDto dto, CancellationToken ct) => Ok(await _service.DecideAsync(dto, ct));
    }
}
