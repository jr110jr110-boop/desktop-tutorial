using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.DashboardDtos;
using WebApi.Filters;

namespace WebApi.Controllers.ProjectControl
{
    /// <summary>Read-only management dashboards. The role of each dashboard is checked in the service; prices are masked there.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/dashboards")]
    [RolesCheckedInService]
    public class EpmDashboardController : ControllerBase
    {
        private readonly IEpmDashboardService _dash;

        public EpmDashboardController(IEpmDashboardService dash) { _dash = dash; }

        [HttpGet]
        public async Task<IActionResult> IndexAsync(CancellationToken ct) => Ok(await _dash.IndexAsync(ct));

        [HttpGet("{key}")]
        public async Task<IActionResult> GetAsync(string key, [FromQuery] int? contractId, CancellationToken ct) => Ok(await _dash.GetAsync(key, contractId, ct));

        [HttpGet("portfolio-evm")]
        public async Task<IActionResult> PortfolioEvmAsync([FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _dash.PortfolioEvmAsync(asOf, ct));

        [HttpGet("commitments")]
        public async Task<IActionResult> CommitmentsAsync([FromQuery] CommitmentsQuery query, CancellationToken ct) => Ok(await _dash.CommitmentsAsync(query, ct));

        [HttpGet("delay-causes")]
        public async Task<IActionResult> DelayCausesAsync([FromQuery] DelayCauseQuery query, CancellationToken ct) => Ok(await _dash.DelayCausesAsync(query, ct));
    }
}
