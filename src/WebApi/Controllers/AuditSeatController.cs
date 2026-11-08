using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.AuditSeat;
using ViewModels.NasbNiro.AuditSeatDtos;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // Package 9: the AI internal auditor (ISO 19011). The seat itself has the read-only AUDITOR role and writes only through its own job; the actions here are for people.
    // Every signed-in user passes [Authorize]; the services check the role of each action again (the closure chain: QA, PMO, CEO, three distinct persons, never the owner and never an AI seat).
    // Answers are ResponseDto { success, message, data } with HTTP 200; lists are plain arrays.

    [ApiController]
    [Authorize]
    [RolesCheckedInService]
    [Route("api/v1/audit")]
    public class AuditSeatController : ControllerBase
    {
        private readonly IAusRunService _runs;
        private readonly IAusFindingService _findings;
        private readonly IAusReportService _reports;
        private readonly IAusProgramService _programs;
        private readonly IAusAuditService _audits;

        public AuditSeatController(IAusRunService runs, IAusFindingService findings, IAusReportService reports, IAusProgramService programs, IAusAuditService audits)
        {
            _runs = runs; _findings = findings; _reports = reports; _programs = programs; _audits = audits;
        }

        [HttpGet("status")]
        public async Task<IActionResult> StatusAsync(CancellationToken ct) => Ok(await _findings.GetStatusAsync(ct));

        // ---- rules and runs
        [HttpGet("rules")]
        public async Task<IActionResult> RulesAsync(CancellationToken ct) => Ok(await _runs.GetRulesAsync(ct));

        [HttpGet("runs")]
        public async Task<IActionResult> RunsAsync([FromQuery] int? limit, CancellationToken ct) => Ok(await _runs.GetRunsAsync(limit, ct));

        [HttpGet("runs/{id:int}")]
        public async Task<IActionResult> RunAsync(int id, CancellationToken ct) => Ok(await _runs.GetRunAsync(id, ct));

        [HttpPost("runs")]
        public async Task<IActionResult> RunNowAsync([FromBody] AusRunRequestDto dto, CancellationToken ct) => Ok(await _runs.RunNowAsync(ct));

        // ---- findings
        [HttpGet("findings")]
        public async Task<IActionResult> FindingsAsync([FromQuery] AusFindingQuery query, CancellationToken ct) => Ok(await _findings.GetAsync(query, ct));

        [HttpGet("findings/{id:int}")]
        public async Task<IActionResult> FindingAsync(int id, CancellationToken ct) => Ok(await _findings.GetOneAsync(id, ct));

        [HttpPost("findings/action")]
        public async Task<IActionResult> ActionAsync([FromBody] AusActionDto dto, CancellationToken ct) => Ok(await _findings.ProposeActionAsync(dto, ct));

        [HttpPost("findings/step")]
        public async Task<IActionResult> StepAsync([FromBody] AusStepDto dto, CancellationToken ct) => Ok(await _findings.StepAsync(dto, ct));

        [HttpPost("findings/manual")]
        public async Task<IActionResult> ManualAsync([FromBody] AusManualFindingDto dto, CancellationToken ct) => Ok(await _findings.CreateManualAsync(dto, ct));

        [HttpPost("findings/manual-recheck")]
        public async Task<IActionResult> ManualRecheckAsync([FromBody] AusManualRecheckDto dto, CancellationToken ct) => Ok(await _findings.ManualRecheckAsync(dto, ct));

        // ---- reports
        [HttpGet("reports")]
        public async Task<IActionResult> ReportsAsync([FromQuery] int? kind, [FromQuery] int? limit, CancellationToken ct) => Ok(await _reports.GetAsync(kind, limit, ct));

        [HttpGet("reports/{id:int}")]
        public async Task<IActionResult> ReportAsync(int id, CancellationToken ct) => Ok(await _reports.GetOneAsync(id, ct));

        [HttpPost("reports")]
        public async Task<IActionResult> GenerateReportAsync([FromBody] AusReportGenerateDto dto, CancellationToken ct) => Ok(await _reports.GenerateAsync(dto, ct));

        // ---- programme
        [HttpGet("programs")]
        public async Task<IActionResult> ProgramsAsync(CancellationToken ct) => Ok(await _programs.GetAsync(ct));

        [HttpGet("programs/{id:int}")]
        public async Task<IActionResult> ProgramAsync(int id, CancellationToken ct) => Ok(await _programs.GetOneAsync(id, ct));

        [HttpPost("programs")]
        public async Task<IActionResult> SaveProgramAsync([FromBody] AusProgramSaveDto dto, CancellationToken ct) => Ok(await _programs.SaveAsync(dto, ct));

        [HttpPost("programs/default/{year:int}")]
        public async Task<IActionResult> DefaultProgramAsync(int year, CancellationToken ct) => Ok(await _programs.DefaultDraftAsync(year, ct));

        [HttpPost("programs/action")]
        public async Task<IActionResult> ProgramActionAsync([FromBody] AusProgramActionDto dto, CancellationToken ct) => Ok(await _programs.ActionAsync(dto, ct));

        // ---- process audits
        [HttpGet("process-audits")]
        public async Task<IActionResult> AuditsAsync([FromQuery] int? limit, CancellationToken ct) => Ok(await _audits.GetAsync(limit, ct));

        [HttpGet("process-audits/{id:int}")]
        public async Task<IActionResult> AuditAsync(int id, CancellationToken ct) => Ok(await _audits.GetOneAsync(id, ct));

        [HttpPost("process-audits")]
        public async Task<IActionResult> CreateAuditAsync([FromBody] AusAuditSaveDto dto, CancellationToken ct) => Ok(await _audits.CreateAsync(dto, ct));

        [HttpPost("process-audits/action")]
        public async Task<IActionResult> AuditActionAsync([FromBody] AusAuditActionDto dto, CancellationToken ct) => Ok(await _audits.ActionAsync(dto, ct));

        [HttpPost("process-audits/checks")]
        public async Task<IActionResult> AuditCheckAsync([FromBody] AusCheckSaveDto dto, CancellationToken ct) => Ok(await _audits.SaveCheckAsync(dto, ct));
    }
}
