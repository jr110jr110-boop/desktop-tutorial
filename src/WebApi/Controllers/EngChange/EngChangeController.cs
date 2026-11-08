using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.EngChange;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.EngChange;
using ViewModels.NasbNiro.EngChangeDtos;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // Package A2: engineering change requests (ECR) and orders (ECO), the computed impact, dispositions, tasks, change history and the gate check.
    // Every signed-in user passes [Authorize]; the service checks the role of every step again (the approvers are ENGINEERING and QA, two distinct persons, never an AI seat).
    // All answers are ResponseDto { success, message, data } with HTTP 200, like the rest of the API.

    [ApiController]
    [Authorize]
    [RolesCheckedInService]
    [Route("api/v1/eng-change")]
    public class EngChangeController : ControllerBase
    {
        private readonly IEngChangeService _service;
        public EngChangeController(IEngChangeService service) => _service = service;

        [HttpGet("requests")]
        public async Task<IActionResult> GetRequestsAsync([FromQuery] EcRequestQuery query, CancellationToken ct) => Ok(await _service.GetRequestsAsync(query, ct));

        [HttpGet("requests/{id:int}")]
        public async Task<IActionResult> GetRequestAsync(int id, CancellationToken ct) => Ok(await _service.GetRequestAsync(id, ct));

        [HttpPost("requests")]
        public async Task<IActionResult> SaveRequestAsync([FromBody] SaveEcRequestDto dto, CancellationToken ct) => Ok(await _service.SaveRequestAsync(dto, ct));

        [HttpPost("requests/action")]
        public async Task<IActionResult> RequestActionAsync([FromBody] EcRequestActionDto dto, CancellationToken ct) => Ok(await _service.RequestActionAsync(dto, ct));

        [HttpGet("orders")]
        public async Task<IActionResult> GetOrdersAsync([FromQuery] EcOrderQuery query, CancellationToken ct) => Ok(await _service.GetOrdersAsync(query, ct));

        [HttpGet("orders/{id:int}")]
        public async Task<IActionResult> GetOrderAsync(int id, CancellationToken ct) => Ok(await _service.GetOrderAsync(id, ct));

        [HttpPost("orders")]
        public async Task<IActionResult> SaveOrderAsync([FromBody] SaveEcOrderDto dto, CancellationToken ct) => Ok(await _service.SaveOrderAsync(dto, ct));

        [HttpPost("orders/changes")]
        public async Task<IActionResult> AddChangeAsync([FromBody] EcOrderChangeSaveDto dto, CancellationToken ct) => Ok(await _service.AddChangeAsync(dto, ct));

        [HttpPost("orders/changes/remove")]
        public async Task<IActionResult> RemoveChangeAsync([FromBody] EcOrderChangeRemoveDto dto, CancellationToken ct) => Ok(await _service.RemoveChangeAsync(dto, ct));

        [HttpPost("orders/new-revision")]
        public async Task<IActionResult> NewRevisionAsync([FromBody] EcNewRevisionDto dto, CancellationToken ct) => Ok(await _service.NewRevisionAsync(dto, ct));

        [HttpPost("orders/dispositions")]
        public async Task<IActionResult> SaveDispositionAsync([FromBody] EcDispositionSaveDto dto, CancellationToken ct) => Ok(await _service.SaveDispositionAsync(dto, ct));

        [HttpPost("orders/dispositions/bulk")]
        public async Task<IActionResult> BulkDispositionAsync([FromBody] EcBulkDispositionDto dto, CancellationToken ct) => Ok(await _service.BulkDispositionAsync(dto, ct));

        [HttpPost("orders/action")]
        public async Task<IActionResult> OrderActionAsync([FromBody] EcOrderActionDto dto, CancellationToken ct) => Ok(await _service.OrderActionAsync(dto, ct));

        [HttpPost("tasks/action")]
        public async Task<IActionResult> TaskActionAsync([FromBody] EcTaskActionDto dto, CancellationToken ct) => Ok(await _service.TaskActionAsync(dto, ct));

        [HttpGet("tasks/mine")]
        public async Task<IActionResult> GetMyTasksAsync(CancellationToken ct) => Ok(await _service.GetMyTasksAsync(ct));

        [HttpGet("history")]
        public async Task<IActionResult> GetHistoryAsync([FromQuery] string partNumber, CancellationToken ct) => Ok(await _service.GetHistoryAsync(partNumber, ct));

        [HttpGet("gate-check")]
        public async Task<IActionResult> CheckAsync([FromQuery] EcChangeKind kind, [FromQuery] int targetId, [FromQuery] EcChangeAction action, CancellationToken ct) => Ok(await _service.CheckAsync(kind, targetId, action == 0 ? EcChangeAction.NewRevision : action, ct));
    }
}
