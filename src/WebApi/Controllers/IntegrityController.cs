using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Integrity;
using WebApi.Filters;
using ViewModels.NasbNiro.IntegrityDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    /// <summary>
    /// Package D: «سلامت سوابق». The verification of the audit chain (history, on demand) and the result of the monthly restore test. The service checks the roles
    /// (PMO, CEO, QA, AUDITOR, FINANCEMANAGER; the summary light is also read by the heads of production, planning and engineering).
    /// </summary>
    [ApiController]
    [Authorize]
    [RolesCheckedInService]
    [Route("api/v1/integrity")]
    public class IntegrityController : ControllerBase
    {
        private readonly IAuditIntegrityService _integrity;
        private readonly IRestoreTestService _restore;

        public IntegrityController(IAuditIntegrityService integrity, IRestoreTestService restore)
        {
            _integrity = integrity;
            _restore = restore;
        }

        /// <summary>The two lights: the audit chain and the restore test (last successful restore test, red after 35 days).</summary>
        [HttpGet("summary")]
        public async Task<IActionResult> SummaryAsync(CancellationToken ct) => Ok(await _integrity.GetSummaryAsync(ct));

        /// <summary>The history of the chain verifications, newest first.</summary>
        [HttpGet("chain/runs")]
        public async Task<IActionResult> RunsAsync([FromQuery] AuditChainRunQuery query, CancellationToken ct) => Ok((await _integrity.GetRunsAsync(query, ct)).ToPaginatedResultSafe());

        /// <summary>Verifies the chain now: mode "incremental" (from the checkpoint, default) or "full".</summary>
        [HttpPost("chain/run")]
        public async Task<IActionResult> RunAsync([FromBody] AuditChainRunRequestDto dto, CancellationToken ct) => Ok(await _integrity.RunOnDemandAsync(dto, ct));

        /// <summary>The results of the restore tests, newest first.</summary>
        [HttpGet("restore-tests")]
        public async Task<IActionResult> RestoreTestsAsync([FromQuery] PaginationParams query, CancellationToken ct) => Ok((await _restore.GetResultsAsync(query, ct)).ToPaginatedResultSafe());

        /// <summary>Reads the result files of the restore test script now.</summary>
        [HttpPost("restore-tests/import")]
        public async Task<IActionResult> ImportAsync(CancellationToken ct) => Ok(await _restore.ImportOnDemandAsync(ct));
    }
}

namespace WebApi.Controllers
{
    /// <summary>Package D: the strict production profile and the two-person change of security-relevant settings.</summary>
    [ApiController]
    [Authorize]
    [RolesCheckedInService]
    [Route("api/v1/safety")]
    public class SafetySettingsController : ControllerBase
    {
        private readonly Services.NasbNiro.Integrity.ISafetySettingsService _safety;
        public SafetySettingsController(Services.NasbNiro.Integrity.ISafetySettingsService safety) { _safety = safety; }

        /// <summary>The strict profile with the value of every security-relevant setting now; deviations are flagged.</summary>
        [HttpGet("profile")]
        public async Task<IActionResult> ProfileAsync(CancellationToken ct) => Ok(await _safety.GetProfileAsync(ct));

        /// <summary>The settings that differ from the strict profile (also read by the AI internal auditor).</summary>
        [HttpGet("deviations")]
        public async Task<IActionResult> DeviationsAsync(CancellationToken ct) => Ok(await _safety.GetDeviationsAsync(ct));

        [HttpGet("changes")]
        public async Task<IActionResult> ChangesAsync([FromQuery] SafetyChangeQuery query, CancellationToken ct) => Ok((await _safety.GetChangesAsync(query, ct)).ToPaginatedResultSafe());

        /// <summary>A request to change a security-relevant setting, with the reason. Nothing changes until a second person approves.</summary>
        [HttpPost("changes")]
        public async Task<IActionResult> RequestAsync([FromBody] SafetyRequestDto dto, CancellationToken ct) => Ok(await _safety.RequestAsync(dto, ct));

        /// <summary>PMO or CEO, not the requester: approve (the value is applied) or reject (with a note).</summary>
        [HttpPost("changes/decide")]
        public async Task<IActionResult> DecideAsync([FromBody] SafetyDecisionDto dto, CancellationToken ct) => Ok(await _safety.DecideAsync(dto, ct));

        [HttpPost("changes/{id:int}/cancel")]
        public async Task<IActionResult> CancelAsync(int id, CancellationToken ct) => Ok(await _safety.CancelAsync(id, ct));
    }
}
