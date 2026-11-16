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
    // ENG-E1: the platform dossiers (the engineering documents of every product of the bank).

    [ApiController]
    [Authorize]
    [Route("api/v1/engineering/platforms")]
    [RolesCheckedInService]
    public class EnPlatformController : ControllerBase
    {
        private readonly IEnPlatformService _service;

        public EnPlatformController(IEnPlatformService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> PlatformsAsync([FromQuery] EnPlatformQuery query, CancellationToken ct) => Ok((await _service.GetPlatformsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("summary")]
        public async Task<IActionResult> SummaryAsync(CancellationToken ct) => Ok(await _service.GetSummaryAsync(ct));

        [HttpGet("{productId:int}")]
        public async Task<IActionResult> DossierAsync(int productId, CancellationToken ct) => Ok(await _service.GetDossierAsync(productId, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] EnDossierActionDto dto, CancellationToken ct) => Ok(await _service.DossierActionAsync(dto, ct));

        [HttpPost("supply")]
        public async Task<IActionResult> SupplyAsync([FromBody] EnSupplyLineDto dto, CancellationToken ct) => Ok(await _service.SupplyLineAsync(dto, ct));
    }
}
