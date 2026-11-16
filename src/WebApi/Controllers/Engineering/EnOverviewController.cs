using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Engineering;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // ENG-E1: the overview of the engineering control (counts only). The role check is in the service.

    [ApiController]
    [Authorize]
    [Route("api/v1/engineering")]
    [RolesCheckedInService]
    public class EnOverviewController : ControllerBase
    {
        private readonly IEnOverviewService _overview;

        public EnOverviewController(IEnOverviewService overview) => _overview = overview;

        [HttpGet("overview")]
        public async Task<IActionResult> OverviewAsync(CancellationToken ct) => Ok(await _overview.GetOverviewAsync(ct));
    }
}
