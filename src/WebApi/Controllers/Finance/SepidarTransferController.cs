using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Finance.Sp;
using ViewModels.NasbNiro.FinanceDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // Sepidar transfer per document: the kinds that can be transferred (all off until the owner enables them), the queue of prepared items, the decisions of the finance manager and the status of the sender.
    // Read: the finance manager, the CEO and the auditor (read only). Every write names its roles here and the service checks them again (it also refuses an AI seat and the maker of the source document).
    [ApiController]
    [Authorize(Roles = SpRoles.Readers)]
    [Route("api/v1/finance/sepidar/transfer")]
    public class SepidarTransferController : ControllerBase
    {
        private readonly ISpOutboxService _outbox;

        public SepidarTransferController(ISpOutboxService outbox) { _outbox = outbox; }

        [HttpGet("types")]
        public async Task<IActionResult> TypesAsync(CancellationToken ct) => Ok(await _outbox.GetTypesAsync(ct));

        [HttpPost("types/switch")]
        [Authorize(Roles = SpRoles.TypeSwitchers)]
        public async Task<IActionResult> SwitchTypeAsync([FromBody] SpTypeSwitchDto dto, CancellationToken ct) => Ok(await _outbox.SwitchTypeAsync(dto, ct));

        [HttpGet("items")]
        public async Task<IActionResult> ItemsAsync([FromQuery] SpOutboxQuery query, CancellationToken ct) => Ok((await _outbox.GetItemsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("items/{id:int}")]
        public async Task<IActionResult> ItemAsync(int id, CancellationToken ct) => Ok(await _outbox.GetItemAsync(id, ct));

        /// <summary>Approve or reject one item (action "approve" or "reject"; a reason is required to reject).</summary>
        [HttpPost("decide")]
        [Authorize(Roles = SpRoles.Deciders)]
        public async Task<IActionResult> DecideAsync([FromBody] SpDecideDto dto, CancellationToken ct) => Ok(await _outbox.DecideAsync(dto, ct));

        /// <summary>Approve a selected set; every item is checked and saved on its own and the answer counts them exactly.</summary>
        [HttpPost("bulk-approve")]
        [Authorize(Roles = SpRoles.Deciders)]
        public async Task<IActionResult> BulkApproveAsync([FromBody] SpBulkApproveDto dto, CancellationToken ct) => Ok(await _outbox.BulkApproveAsync(dto, ct));

        /// <summary>Retry a failed item (it goes back to approved) or cancel it (a reason is required).</summary>
        [HttpPost("fix")]
        [Authorize(Roles = SpRoles.Deciders)]
        public async Task<IActionResult> FixAsync([FromBody] SpFixDto dto, CancellationToken ct) => Ok(await _outbox.FixAsync(dto, ct));

        [HttpGet("summary")]
        public async Task<IActionResult> SummaryAsync(CancellationToken ct) => Ok(await _outbox.GetSummaryAsync(ct));
    }
}
