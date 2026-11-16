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
    // ENG-E1: the handover of the engineering documents to the PMO, the CEO emergency waiver, the document debts and the distribution. Role checks per action are in the service.

    [ApiController]
    [Authorize]
    [Route("api/v1/engineering")]
    [RolesCheckedInService]
    public class EnHandoverController : ControllerBase
    {
        private readonly IEnHandoverService _handovers;

        public EnHandoverController(IEnHandoverService handovers) => _handovers = handovers;

        [HttpGet("handovers")]
        public async Task<IActionResult> HandoversAsync([FromQuery] EnHandoverQuery query, CancellationToken ct) => Ok((await _handovers.GetHandoversAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("handovers/{id:int}")]
        public async Task<IActionResult> HandoverAsync(int id, CancellationToken ct) => Ok(await _handovers.GetHandoverAsync(id, ct));

        [HttpPost("handovers/action")]
        public async Task<IActionResult> HandoverActionAsync([FromBody] EnHandoverActionDto dto, CancellationToken ct) => Ok(await _handovers.HandoverActionAsync(dto, ct));

        [HttpGet("waivers")]
        public async Task<IActionResult> WaiversAsync([FromQuery] EnWaiverQuery query, CancellationToken ct) => Ok((await _handovers.GetWaiversAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("waivers/{id:int}")]
        public async Task<IActionResult> WaiverAsync(int id, CancellationToken ct) => Ok(await _handovers.GetWaiverAsync(id, ct));

        [HttpPost("waivers/request")]
        public async Task<IActionResult> RequestWaiverAsync([FromBody] EnWaiverRequestDto dto, CancellationToken ct) => Ok(await _handovers.RequestWaiverAsync(dto, ct));

        [HttpPost("waivers/decide")]
        public async Task<IActionResult> DecideWaiverAsync([FromBody] EnWaiverDecisionDto dto, CancellationToken ct) => Ok(await _handovers.DecideWaiverAsync(dto, ct));

        [HttpGet("debts")]
        public async Task<IActionResult> DebtsAsync([FromQuery] EnDebtQuery query, CancellationToken ct) => Ok((await _handovers.GetDebtsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("debts/settle")]
        public async Task<IActionResult> SettleDebtsAsync(CancellationToken ct) => Ok(await _handovers.SettleDebtsAsync(ct));

        [HttpGet("distributions")]
        public async Task<IActionResult> DistributionsAsync([FromQuery] int? handoverId, [FromQuery] bool onlyOpen, CancellationToken ct) => Ok(await _handovers.GetDistributionsAsync(handoverId, onlyOpen, ct));
    }
}
