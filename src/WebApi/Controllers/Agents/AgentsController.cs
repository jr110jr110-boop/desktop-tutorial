using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Agents;
using ViewModels.NasbNiro.AgentDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // Agent layer: the business processes are made by AI agents; people decide at the gates and do the physical work. The gate inbox shows each person what waits for his roles (it never shows a seat);
    // the decision itself is made in the existing page of the document. PMO, CEO and the auditor read the tasks and the master board; the PMO retries a failed task and switches steps.
    // Every service checks the caller again; rule violations answer 200 with success=false like the other modules.
    [ApiController]
    [Authorize]
    [Route("api/v1/agents")]
    [RolesCheckedInService]
    public class AgentsController : ControllerBase
    {
        private readonly IAgentInboxService _inbox;
        private readonly IAgentBoardService _board;

        public AgentsController(IAgentInboxService inbox, IAgentBoardService board)
        {
            _inbox = inbox;
            _board = board;
        }

        // ---- the gate inbox (every signed-in person; the service shows only his own)
        [HttpGet("inbox")]
        public async Task<IActionResult> InboxAsync([FromQuery] AgentInboxQuery query, CancellationToken ct) => Ok((await _inbox.GetInboxAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("inbox/counts")]
        public async Task<IActionResult> InboxCountsAsync(CancellationToken ct) => Ok(await _inbox.GetCountsAsync(ct));

        [HttpPost("inbox/return")]
        public async Task<IActionResult> ReturnAsync([FromBody] AgentReturnDto dto, CancellationToken ct) => Ok(await _inbox.ReturnAsync(dto, ct));

        // ---- tasks and the master board (PMO, CEO, AUDITOR)
        [HttpGet("tasks")]
        public async Task<IActionResult> TasksAsync([FromQuery] AgentTaskQuery query, CancellationToken ct) => Ok((await _board.GetTasksAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("tasks/{id:int}")]
        public async Task<IActionResult> TaskAsync(int id, CancellationToken ct) => Ok(await _board.GetTaskAsync(id, ct));

        [HttpPost("tasks/retry")]
        public async Task<IActionResult> RetryAsync([FromBody] AgentRetryDto dto, CancellationToken ct) => Ok(await _board.RetryAsync(dto, ct));

        [HttpGet("board")]
        public async Task<IActionResult> BoardAsync([FromQuery] int? contractId, CancellationToken ct) => Ok(await _board.GetBoardAsync(contractId, ct));

        // ---- the steps (PMO and CEO read, PMO switches)
        [HttpGet("steps")]
        public async Task<IActionResult> StepsAsync(CancellationToken ct) => Ok(await _board.GetStepsAsync(ct));

        [HttpPost("steps/enable")]
        public async Task<IActionResult> EnableStepAsync([FromBody] AgentStepEnableDto dto, CancellationToken ct) => Ok(await _board.EnableStepAsync(dto, ct));
    }
}
