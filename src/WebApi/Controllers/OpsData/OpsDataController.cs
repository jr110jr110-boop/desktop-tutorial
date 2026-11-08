using System;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.OpsData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.OpsData;
using ViewModels.NasbNiro.OpsDataDtos;

namespace WebApi.Controllers
{
    // PB8: operational data control (the data-quality page, the exceptions the supervisors decide, the engineering readiness checklist).

    [ApiController]
    [Authorize]
    [Route("api/v1/opsdata")]
    public class OpsDataController : ControllerBase
    {
        private const string AllMakers = OpsRoles.WarehouseMakers + "," + OpsRoles.ProductionMakers + "," + OpsRoles.QualityMakers + "," + OpsRoles.FinanceMakers;
        private const string AllSupervisors = OpsRoles.WarehouseSupervisors + "," + OpsRoles.ProductionSupervisors + "," + OpsRoles.QualitySupervisors + "," + OpsRoles.FinanceSupervisors;

        private readonly IOpsDataQualityService _quality;
        private readonly IOpsExceptionService _exceptions;
        private readonly IOpsReadinessService _readiness;

        public OpsDataController(IOpsDataQualityService quality, IOpsExceptionService exceptions, IOpsReadinessService readiness)
        {
            _quality = quality; _exceptions = exceptions; _readiness = readiness;
        }

        [HttpGet("findings")]
        [Authorize(Roles = OpsRoles.Readers)]
        public async Task<IActionResult> FindingsAsync([FromQuery] OpsFindingQuery query, CancellationToken ct) => Ok(await _quality.GetFindingsAsync(query, ct));

        [HttpGet("summary")]
        [Authorize(Roles = OpsRoles.Readers)]
        public async Task<IActionResult> SummaryAsync([FromQuery] OpsUnit? unit, CancellationToken ct) => Ok(await _quality.SummaryAsync(unit, ct));

        [HttpGet("rules")]
        [Authorize(Roles = OpsRoles.Readers)]
        public async Task<IActionResult> RulesAsync(CancellationToken ct) => Ok(await _quality.RulesAsync(ct));

        /// <summary>The findings of one document as they would be now (nothing is written).</summary>
        [HttpGet("evaluate")]
        [Authorize(Roles = OpsRoles.Readers)]
        public async Task<IActionResult> EvaluateAsync([FromQuery] OpsEvaluateQuery query, CancellationToken ct) => Ok(await _quality.EvaluateAsync(query, ct));

        /// <summary>Resolved with a corrective document, or verified as right by a supervisor who did not enter the data (the service checks).</summary>
        [HttpPost("findings/resolve")]
        [Authorize(Roles = AllMakers + "," + AllSupervisors)]
        public async Task<IActionResult> ResolveAsync([FromBody] ResolveOpsFindingDto dto, CancellationToken ct) => Ok(await _quality.ResolveAsync(dto, ct));

        [HttpGet("exceptions")]
        [Authorize(Roles = OpsRoles.Readers)]
        public async Task<IActionResult> ExceptionsAsync([FromQuery] OpsExceptionQuery query, CancellationToken ct) => Ok(await _exceptions.GetAsync(query, ct));

        [HttpPost("exceptions")]
        [Authorize(Roles = AllMakers + "," + AllSupervisors)]
        public async Task<IActionResult> RequestExceptionAsync([FromBody] RequestOpsExceptionDto dto, CancellationToken ct) => Ok(await _exceptions.RequestAsync(dto, ct));

        /// <summary>The supervisor of the unit decides; never the person who asked or entered the data, never an AI seat (the service checks).</summary>
        [HttpPost("exceptions/decide")]
        [Authorize(Roles = AllSupervisors)]
        public async Task<IActionResult> DecideExceptionAsync([FromBody] DecideOpsExceptionDto dto, CancellationToken ct) => Ok(await _exceptions.DecideAsync(dto, ct));

        // ------------------------------------------------------------------------------------------ engineering readiness

        [HttpGet("readiness")]
        [Authorize(Roles = OpsReadinessService.Readers)]
        public async Task<IActionResult> ReadinessAsync([FromQuery] int? workOrderId, [FromQuery] int? productId, CancellationToken ct) => Ok(await _readiness.GetAsync(workOrderId, productId, ct));

        [HttpGet("readiness/summary")]
        [Authorize(Roles = OpsReadinessService.Readers)]
        public async Task<IActionResult> ReadinessSummaryAsync(CancellationToken ct) => Ok(await _readiness.SummaryAsync(ct));

        [HttpGet("readiness/obligations")]
        [Authorize(Roles = OpsReadinessService.Readers)]
        public async Task<IActionResult> ObligationsAsync([FromQuery] int? workOrderId, [FromQuery] bool openOnly, CancellationToken ct) => Ok(await _readiness.ObligationsAsync(workOrderId, openOnly, ct));

        [HttpGet("readiness/exceptions")]
        [Authorize(Roles = OpsReadinessService.Readers)]
        public async Task<IActionResult> ReadinessExceptionsAsync([FromQuery] int? workOrderId, CancellationToken ct) => Ok(await _readiness.ExceptionsAsync(workOrderId, ct));

        [HttpPost("readiness/exceptions")]
        [Authorize(Roles = OpsReadinessService.Requesters)]
        public async Task<IActionResult> RequestReadinessExceptionAsync([FromBody] RequestReadinessExceptionDto dto, CancellationToken ct) => Ok(await _readiness.RequestExceptionAsync(dto, ct));

        /// <summary>The PMO manager and the CEO each approve once (two different persons, neither the requester, never an AI seat).</summary>
        [HttpPost("readiness/exceptions/decide")]
        [Authorize(Roles = "PMO,CEO")]
        public async Task<IActionResult> DecideReadinessExceptionAsync([FromBody] DecideReadinessExceptionDto dto, CancellationToken ct) => Ok(await _readiness.DecideExceptionAsync(dto, ct));
    }
}
