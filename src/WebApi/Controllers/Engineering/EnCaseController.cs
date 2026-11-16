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
    // ENG-E1: the engineering cases (classification gate, tender package, charter review) and the clarifications through commercial. Role checks per action are in the services.

    [ApiController]
    [Authorize]
    [Route("api/v1/engineering")]
    [RolesCheckedInService]
    public class EnCaseController : ControllerBase
    {
        private readonly IEnCaseService _cases;
        private readonly IEnClarificationService _clarifications;

        public EnCaseController(IEnCaseService cases, IEnClarificationService clarifications)
        {
            _cases = cases;
            _clarifications = clarifications;
        }

        [HttpGet("cases")]
        public async Task<IActionResult> CasesAsync([FromQuery] EnCaseQuery query, CancellationToken ct) => Ok((await _cases.GetCasesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("cases/{id:int}")]
        public async Task<IActionResult> CaseAsync(int id, CancellationToken ct) => Ok(await _cases.GetCaseAsync(id, ct));

        [HttpGet("cases/{id:int}/charter-view")]
        public async Task<IActionResult> CharterViewAsync(int id, CancellationToken ct) => Ok(await _cases.GetCharterViewAsync(id, ct));

        [HttpGet("cases/gate")]
        public async Task<IActionResult> GateAsync([FromQuery] int? inquiryId, [FromQuery] int? contractId, CancellationToken ct) => Ok(await _cases.GetGateStateAsync(inquiryId, contractId, ct));

        [HttpPost("cases/action")]
        public async Task<IActionResult> CaseActionAsync([FromBody] EnCaseActionDto dto, CancellationToken ct) => Ok(await _cases.CaseActionAsync(dto, ct));

        [HttpGet("clarifications")]
        public async Task<IActionResult> ClarificationsAsync([FromQuery] EnClarificationQuery query, CancellationToken ct) => Ok((await _clarifications.GetClarificationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("clarifications/{id:int}")]
        public async Task<IActionResult> ClarificationAsync(int id, CancellationToken ct) => Ok(await _clarifications.GetClarificationAsync(id, ct));

        [HttpPost("clarifications")]
        public async Task<IActionResult> SaveClarificationAsync([FromBody] EnSaveClarificationDto dto, CancellationToken ct) => Ok(await _clarifications.SaveClarificationAsync(dto, ct));

        [HttpPost("clarifications/action")]
        public async Task<IActionResult> ClarificationActionAsync([FromBody] EnClarificationActionDto dto, CancellationToken ct) => Ok(await _clarifications.ClarificationActionAsync(dto, ct));
    }
}
