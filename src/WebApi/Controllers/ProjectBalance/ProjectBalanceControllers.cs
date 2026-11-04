using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.ProjectBalance;
using ViewModels.NasbNiro.ProjectBalanceDtos;

namespace WebApi.Controllers
{
    // PB2: the automatic material balance, project reservations, substitutes and the draft purchase requests.
    // Reading needs one of BalanceRoles.Readers (project, planning, engineering, warehouse, procurement, QC, management); changes need the roles of BalanceRoles, which the services check again (an AI seat never approves).
    // All answers are ResponseDto { success, message, data } with HTTP 200, like the rest of the API.

    [ApiController]
    [Authorize(Roles = BalanceRoles.Readers)]
    [Route("api/v1/project-balance")]
    public class ProjectBalanceController : ControllerBase
    {
        private readonly IProjectBalanceService _service;
        private readonly IKitService _kits;
        public ProjectBalanceController(IProjectBalanceService service, IKitService kits)
        {
            _service = service;
            _kits = kits;
        }

        [HttpGet("projects")]
        public async Task<IActionResult> GetProjectsAsync(CancellationToken ct) => Ok(await _service.GetProjectsAsync(ct));

        [HttpPost("run")]
        [Authorize(Roles = BalanceRoles.Planning)]
        public async Task<IActionResult> RunAsync([FromBody] BalanceRunRequestDto dto, CancellationToken ct) => Ok(await _service.RunAsync(dto, ct));

        [HttpGet("work-order/{workOrderId:int}/runs")]
        public async Task<IActionResult> GetRunsAsync(int workOrderId, CancellationToken ct) => Ok(await _service.GetRunsAsync(workOrderId, ct));

        [HttpGet("work-order/{workOrderId:int}/latest")]
        public async Task<IActionResult> GetLatestAsync(int workOrderId, CancellationToken ct) => Ok(await _service.GetLatestAsync(workOrderId, ct));

        [HttpGet("runs/{id:int}")]
        public async Task<IActionResult> GetRunAsync(int id, CancellationToken ct) => Ok(await _service.GetRunAsync(id, ct));

        [HttpGet("runs/{id:int}/diff")]
        public async Task<IActionResult> GetDiffAsync(int id, [FromQuery] int? against, CancellationToken ct) => Ok(await _service.GetDiffAsync(id, against, ct));

        [HttpPost("runs/{id:int}/drafts")]
        [Authorize(Roles = BalanceRoles.Planning)]
        public async Task<IActionResult> CreateDraftsAsync(int id, CancellationToken ct) => Ok(await _service.CreateDraftsAsync(id, ct));

        [HttpGet("wbs/{nodeId:int}/material")]
        public async Task<IActionResult> GetNodeMaterialAsync(int nodeId, CancellationToken ct) => Ok(await _service.GetNodeMaterialAsync(nodeId, ct));

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettingsAsync(CancellationToken ct) => Ok(await _service.GetSettingsAsync(ct));

        [HttpPost("settings")]
        [Authorize(Roles = BalanceRoles.Planning)]
        public async Task<IActionResult> SaveSettingAsync([FromBody] SaveBalanceSettingDto dto, CancellationToken ct) => Ok(await _service.SaveSettingAsync(dto, ct));

        [HttpPost("kits/run")]
        [Authorize(Roles = BalanceRoles.Planning)]
        public async Task<IActionResult> RunKitsAsync(CancellationToken ct) => Ok(await _kits.DraftKitsAsync(false, ct));
    }

    [ApiController]
    [Authorize(Roles = BalanceRoles.Readers)]
    [Route("api/v1/project-balance/reservations")]
    public class ProjectReservationController : ControllerBase
    {
        private readonly IProjectReservationService _service;
        public ProjectReservationController(IProjectReservationService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAsync([FromQuery] ReservationQuery query, CancellationToken ct) => Ok(await _service.GetAsync(query, ct));

        [HttpPost("update")]
        [Authorize(Roles = BalanceRoles.Planning)]
        public async Task<IActionResult> UpdateAsync([FromBody] UpdateReservationDto dto, CancellationToken ct) => Ok(await _service.UpdateAsync(dto, ct));

        [HttpPost("release-request")]
        [Authorize(Roles = BalanceRoles.Planning)]
        public async Task<IActionResult> RequestReleaseAsync([FromBody] ReservationActionDto dto, CancellationToken ct) => Ok(await _service.RequestReleaseAsync(dto, ct));

        [HttpPost("release-decision")]
        [Authorize(Roles = BalanceRoles.Planning)]
        public async Task<IActionResult> DecideReleaseAsync([FromBody] ReservationActionDto dto, CancellationToken ct) => Ok(await _service.DecideReleaseAsync(dto, ct));

        [HttpPost("re-enable")]
        [Authorize(Roles = BalanceRoles.Planning)]
        public async Task<IActionResult> ReEnableAsync([FromBody] ReservationActionDto dto, CancellationToken ct) => Ok(await _service.ReEnableAsync(dto, ct));
    }

    [ApiController]
    [Authorize(Roles = BalanceRoles.Readers)]
    [Route("api/v1/project-balance/substitutes")]
    public class ItemSubstituteController : ControllerBase
    {
        private readonly IItemSubstituteService _service;
        public ItemSubstituteController(IItemSubstituteService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAsync([FromQuery] SubstituteQuery query, CancellationToken ct) => Ok(await _service.GetAsync(query, ct));

        [HttpPost("save")]
        [Authorize(Roles = BalanceRoles.SubstituteProposers)]
        public async Task<IActionResult> SaveAsync([FromBody] SaveSubstituteDto dto, CancellationToken ct) => Ok(await _service.SaveAsync(dto, ct));

        [HttpPost("decide")]
        [Authorize(Roles = BalanceRoles.SubstituteApprovers)]
        public async Task<IActionResult> DecideAsync([FromBody] SubstituteDecisionDto dto, CancellationToken ct) => Ok(await _service.DecideAsync(dto, ct));
    }
}
