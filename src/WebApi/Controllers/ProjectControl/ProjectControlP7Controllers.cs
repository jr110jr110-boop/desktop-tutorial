using System;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProjectControlDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers.ProjectControl
{
    // Package P-B7a. Roles are checked inside the services (segregation of duties); the progress cartable is open to every signed-in user because a resource reports his own work.

    /// <summary>The progress-approval cartable: chain per project, reports, decisions and the two follow-up reports.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/projectcontrol/progress-approval")]
    [RolesCheckedInService]
    public class ProgressApprovalController : ControllerBase
    {
        private readonly IPcProgressApprovalService _service;

        public ProgressApprovalController(IPcProgressApprovalService service) { _service = service; }

        [HttpGet("chain/{contractId:int}")]
        public async Task<IActionResult> ChainAsync(int contractId, CancellationToken ct) => Ok(await _service.GetChainAsync(contractId, ct));

        [HttpPost("chain")]
        public async Task<IActionResult> SaveChainAsync([FromBody] PcChainSaveDto dto, CancellationToken ct) => Ok(await _service.SaveChainAsync(dto, ct));

        [HttpGet("submissions")]
        public async Task<IActionResult> ListAsync([FromQuery] PcSubmissionQuery query, CancellationToken ct) => Ok((await _service.GetSubmissionsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("submissions/{id:int}")]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _service.GetSubmissionAsync(id, ct));

        [HttpPost("submissions")]
        public async Task<IActionResult> SaveAsync([FromBody] PcSubmissionSaveDto dto, CancellationToken ct) => Ok(await _service.SaveSubmissionAsync(dto, ct));

        [HttpPost("submissions/decide")]
        public async Task<IActionResult> DecideAsync([FromBody] PcSubmissionDecisionDto dto, CancellationToken ct) => Ok(await _service.DecideAsync(dto, ct));

        /// <summary>Assigned activities and tasks with no progress report for the limit of days.</summary>
        [HttpGet("reports/not-updated")]
        public async Task<IActionResult> NotUpdatedAsync([FromQuery] PcNotUpdatedQuery query, CancellationToken ct) => Ok(await _service.GetNotUpdatedAsync(query, ct));

        /// <summary>Reports that wait at one approval level for longer than the limit.</summary>
        [HttpGet("reports/overdue-updates")]
        public async Task<IActionResult> OverdueAsync([FromQuery] int? contractId, CancellationToken ct) => Ok(await _service.GetOverdueUpdatesAsync(contractId, ct));
    }

    /// <summary>PMBOK registers of the project site and the one-page dashboard.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/projectcontrol/registers")]
    [RolesCheckedInService]
    public class ProjectRegistersController : ControllerBase
    {
        private readonly IPcRegistersService _service;

        public ProjectRegistersController(IPcRegistersService service) { _service = service; }

        [HttpGet("one-page/{contractId:int}")]
        public async Task<IActionResult> OnePageAsync(int contractId, CancellationToken ct) => Ok(await _service.GetOnePageAsync(contractId, ct));

        // risks
        [HttpGet("risks")]
        public async Task<IActionResult> RisksAsync([FromQuery] PcRegisterQuery query, CancellationToken ct) => Ok((await _service.GetRisksAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("risks")]
        public async Task<IActionResult> SaveRiskAsync([FromBody] PcRiskSaveDto dto, CancellationToken ct) => Ok(await _service.SaveRiskAsync(dto, ct));

        [HttpPost("risks/status")]
        public async Task<IActionResult> RiskStatusAsync([FromBody] PcRiskStatusDto dto, CancellationToken ct) => Ok(await _service.SetRiskStatusAsync(dto, ct));

        [HttpGet("risks/heat-map/{contractId:int}")]
        public async Task<IActionResult> HeatMapAsync(int contractId, CancellationToken ct) => Ok(await _service.GetRiskHeatMapAsync(contractId, ct));

        // issues
        [HttpGet("issues")]
        public async Task<IActionResult> IssuesAsync([FromQuery] PcRegisterQuery query, CancellationToken ct) => Ok((await _service.GetIssuesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("issues")]
        public async Task<IActionResult> SaveIssueAsync([FromBody] PcIssueSaveDto dto, CancellationToken ct) => Ok(await _service.SaveIssueAsync(dto, ct));

        [HttpPost("issues/action")]
        public async Task<IActionResult> IssueActionAsync([FromBody] PcIssueActionDto dto, CancellationToken ct) => Ok(await _service.IssueActionAsync(dto, ct));

        // stakeholders
        [HttpGet("stakeholders/{contractId:int}")]
        public async Task<IActionResult> StakeholdersAsync(int contractId, CancellationToken ct) => Ok(await _service.GetStakeholdersAsync(contractId, ct));

        [HttpPost("stakeholders")]
        public async Task<IActionResult> SaveStakeholderAsync([FromBody] PcStakeholderSaveDto dto, CancellationToken ct) => Ok(await _service.SaveStakeholderAsync(dto, ct));

        [HttpGet("stakeholders/matrix/{contractId:int}")]
        public async Task<IActionResult> StakeholderMatrixAsync(int contractId, CancellationToken ct) => Ok(await _service.GetStakeholderMatrixAsync(contractId, ct));

        // lessons learned
        [HttpGet("lessons")]
        public async Task<IActionResult> LessonsAsync([FromQuery] PcRegisterQuery query, CancellationToken ct) => Ok((await _service.GetLessonsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("lessons")]
        public async Task<IActionResult> SaveLessonAsync([FromBody] PcLessonSaveDto dto, CancellationToken ct) => Ok(await _service.SaveLessonAsync(dto, ct));

        [HttpPost("lessons/decide")]
        public async Task<IActionResult> DecideLessonAsync([FromBody] PcRegisterDecisionDto dto, CancellationToken ct) => Ok(await _service.DecideLessonAsync(dto, ct));

        // deliverables
        [HttpGet("deliverables")]
        public async Task<IActionResult> DeliverablesAsync([FromQuery] PcRegisterQuery query, CancellationToken ct) => Ok((await _service.GetDeliverablesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("deliverables")]
        public async Task<IActionResult> SaveDeliverableAsync([FromBody] PcProjDeliverableSaveDto dto, CancellationToken ct) => Ok(await _service.SaveDeliverableAsync(dto, ct));

        [HttpPost("deliverables/action")]
        public async Task<IActionResult> DeliverableActionAsync([FromBody] PcProjDeliverableActionDto dto, CancellationToken ct) => Ok(await _service.DeliverableActionAsync(dto, ct));

        // photos
        [HttpGet("photos")]
        public async Task<IActionResult> PhotosAsync([FromQuery] PcRegisterQuery query, CancellationToken ct) => Ok((await _service.GetPhotosAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("photos")]
        public async Task<IActionResult> AddPhotoAsync([FromBody] PcPhotoSaveDto dto, CancellationToken ct) => Ok(await _service.AddPhotoAsync(dto, ct));

        [HttpPost("photos/void")]
        public async Task<IActionResult> VoidPhotoAsync([FromBody] PcPhotoVoidDto dto, CancellationToken ct) => Ok(await _service.VoidPhotoAsync(dto, ct));

        // contractor / consultant evaluation
        [HttpGet("evaluations/template")]
        public IActionResult EvaluationTemplate([FromQuery] PcEvaluationParty kind) => Ok(_service.GetEvaluationTemplate(kind));

        [HttpGet("evaluations")]
        public async Task<IActionResult> EvaluationsAsync([FromQuery] PcRegisterQuery query, CancellationToken ct) => Ok((await _service.GetEvaluationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("evaluations")]
        public async Task<IActionResult> SaveEvaluationAsync([FromBody] PcEvaluationSaveDto dto, CancellationToken ct) => Ok(await _service.SaveEvaluationAsync(dto, ct));

        [HttpPost("evaluations/decide")]
        public async Task<IActionResult> DecideEvaluationAsync([FromBody] PcRegisterDecisionDto dto, CancellationToken ct) => Ok(await _service.DecideEvaluationAsync(dto, ct));

        // weekly report
        [HttpGet("weekly")]
        public async Task<IActionResult> WeeklyAsync([FromQuery] PcRegisterQuery query, CancellationToken ct) => Ok((await _service.GetWeeklyReportsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("weekly/generate")]
        public async Task<IActionResult> GenerateWeeklyAsync([FromBody] PcWeeklyGenerateDto dto, CancellationToken ct) => Ok(await _service.GenerateWeeklyAsync(dto, ct));

        [HttpPost("weekly")]
        public async Task<IActionResult> SaveWeeklyAsync([FromBody] PcWeeklySaveDto dto, CancellationToken ct) => Ok(await _service.SaveWeeklyAsync(dto, ct));

        [HttpPost("weekly/{id:int}/release")]
        public async Task<IActionResult> ReleaseWeeklyAsync(int id, CancellationToken ct) => Ok(await _service.ReleaseWeeklyAsync(id, ct));

        // phase evaluation
        [HttpGet("phase-evaluations")]
        public async Task<IActionResult> PhaseEvaluationsAsync([FromQuery] PcRegisterQuery query, CancellationToken ct) => Ok((await _service.GetPhaseEvaluationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("phase-evaluations")]
        public async Task<IActionResult> SavePhaseEvaluationAsync([FromBody] PcPhaseEvaluationSaveDto dto, CancellationToken ct) => Ok(await _service.SavePhaseEvaluationAsync(dto, ct));

        [HttpPost("phase-evaluations/decide")]
        public async Task<IActionResult> DecidePhaseEvaluationAsync([FromBody] PcRegisterDecisionDto dto, CancellationToken ct) => Ok(await _service.DecidePhaseEvaluationAsync(dto, ct));
    }
}
