using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Tasks;
using ViewModels.NasbNiro.TaskDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers.Tasks
{
    /// <summary>Task management (TMS). Every signed-in user has a cartable; what he may see and do with a task is decided inside the service (creator, referral holder, watcher, PMO).</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/tasks")]
    [RolesCheckedInService]
    public class TasksController : ControllerBase
    {
        private readonly ITaskService _tasks;

        public TasksController(ITaskService tasks) { _tasks = tasks; }

        [HttpGet]
        public async Task<IActionResult> ListAsync([FromQuery] TmTaskQuery query, CancellationToken ct) => Ok((await _tasks.GetTasksAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _tasks.GetTaskAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> SaveAsync([FromBody] TmTaskSaveDto dto, CancellationToken ct) => Ok(await _tasks.SaveTaskAsync(dto, ct));

        /// <summary>start, complete, approve, return, reject, suspend, resume, reopen.</summary>
        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] TmTaskActionDto dto, CancellationToken ct) => Ok(await _tasks.ActionAsync(dto, ct));

        [HttpPost("assign")]
        public async Task<IActionResult> AssignAsync([FromBody] TmTaskAssignDto dto, CancellationToken ct) => Ok(await _tasks.AssignAsync(dto, ct));

        /// <summary>Check-out of a referral to another cartable, with a reason.</summary>
        [HttpPost("transfer")]
        public async Task<IActionResult> TransferAsync([FromBody] TmTaskTransferDto dto, CancellationToken ct) => Ok(await _tasks.TransferAsync(dto, ct));

        [HttpPost("withdraw")]
        public async Task<IActionResult> WithdrawAsync([FromBody] TmTaskWithdrawDto dto, CancellationToken ct) => Ok(await _tasks.WithdrawAsync(dto, ct));

        [HttpGet("{id:int}/referrals")]
        public async Task<IActionResult> ReferralsAsync(int id, CancellationToken ct) => Ok(await _tasks.GetReferralTreeAsync(id, ct));

        [HttpPost("progress")]
        public async Task<IActionResult> ProgressAsync([FromBody] TmTaskProgressDto dto, CancellationToken ct) => Ok(await _tasks.UpdateProgressAsync(dto, ct));

        [HttpPost("checklist")]
        public async Task<IActionResult> ChecklistAsync([FromBody] TmTaskChecklistDto dto, CancellationToken ct) => Ok(await _tasks.ChecklistAsync(dto, ct));

        [HttpPost("comment")]
        public async Task<IActionResult> CommentAsync([FromBody] TmTaskCommentDto dto, CancellationToken ct) => Ok(await _tasks.CommentAsync(dto, ct));

        [HttpPost("{id:int}/watch")]
        public async Task<IActionResult> WatchAsync(int id, [FromQuery] bool watch, CancellationToken ct) => Ok(await _tasks.WatchAsync(id, watch, ct));

        [HttpGet("board")]
        public async Task<IActionResult> BoardAsync([FromQuery] TmTaskQuery query, CancellationToken ct) => Ok(await _tasks.GetBoardAsync(query, ct));

        [HttpGet("agenda")]
        public async Task<IActionResult> AgendaAsync([FromQuery] TmAgendaQuery query, CancellationToken ct) => Ok(await _tasks.GetAgendaAsync(query, ct));

        [HttpGet("by-activity/{activityId:int}")]
        public async Task<IActionResult> ByActivityAsync(int activityId, CancellationToken ct) => Ok(await _tasks.GetByActivityAsync(activityId, ct));

        [HttpGet("summary")]
        public async Task<IActionResult> SummaryAsync([FromQuery] int? contractId, CancellationToken ct) => Ok(await _tasks.GetSummaryAsync(contractId, ct));

        [HttpGet("counts")]
        public async Task<IActionResult> CountsAsync(CancellationToken ct) => Ok(await _tasks.GetCountsAsync(ct));

        /// <summary>A task from an open decision of an approved meeting minute.</summary>
        [HttpPost("from-decision/{decisionId:int}")]
        public async Task<IActionResult> FromDecisionAsync(int decisionId, CancellationToken ct) => Ok(await _tasks.FromDecisionAsync(decisionId, ct));

        /// <summary>Runs the reminders now (the same job runs every night).</summary>
        [HttpPost("reminders/run")]
        [Authorize(Roles = "PMO,CEO")]
        public async Task<IActionResult> RunRemindersAsync(CancellationToken ct) => Ok(await _tasks.RunRemindersAsync(ct));
    }
}
