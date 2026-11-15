using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProjectControlDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers.ProjectControl
{
    /// <summary>
    /// Project control priority 4: the resource pool, the standard rates, the resource plan with its assignments and the budget view. Every write checks its role (and the maker/checker rules) inside the service;
    /// the readers are the module readers and the roles that handle rates.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/projectcontrol")]
    [RolesCheckedInService]
    public class ProjectControlResourceController : ControllerBase
    {
        private const string Readers = ProjectControlRoles.Readers + ",FINANCEMANAGER,PAYROLL,AUDITOR";

        private readonly IPcResourceService _resources;

        public ProjectControlResourceController(IPcResourceService resources) { _resources = resources; }

        [HttpGet("resources")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> ResourcesAsync([FromQuery] PcResourceQuery query, CancellationToken ct) => Ok((await _resources.GetResourcesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("resources/{id:int}")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> ResourceAsync(int id, CancellationToken ct) => Ok(await _resources.GetResourceAsync(id, ct));

        [HttpPost("resources")]
        public async Task<IActionResult> SaveResourceAsync([FromBody] PcResourceDto dto, CancellationToken ct) => Ok(await _resources.SaveResourceAsync(dto, ct));

        [HttpPost("resources/{id:int}/activate")]
        public async Task<IActionResult> ActivateAsync(int id, CancellationToken ct) => Ok(await _resources.SetResourceActiveAsync(id, true, ct));

        [HttpPost("resources/{id:int}/deactivate")]
        public async Task<IActionResult> DeactivateAsync(int id, CancellationToken ct) => Ok(await _resources.SetResourceActiveAsync(id, false, ct));

        [HttpDelete("resources/{id:int}")]
        public async Task<IActionResult> DeleteResourceAsync(int id, CancellationToken ct) => Ok(await _resources.DeleteResourceAsync(id, ct));

        [HttpGet("resource-rates")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> RatesAsync([FromQuery] PcResourceRateQuery query, CancellationToken ct) => Ok((await _resources.GetRatesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("resource-rates")]
        public async Task<IActionResult> SaveRateAsync([FromBody] PcResourceRateSaveDto dto, CancellationToken ct) => Ok(await _resources.SaveRateAsync(dto, ct));

        [HttpPost("resource-rates/action")]
        public async Task<IActionResult> RateActionAsync([FromBody] PcResourceRateActionDto dto, CancellationToken ct) => Ok(await _resources.RateActionAsync(dto, ct));

        [HttpGet("resource-plans/contract/{contractId:int}")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> PlanAsync(int contractId, [FromQuery] int? version, CancellationToken ct) => Ok(await _resources.GetPlanAsync(contractId, version, ct));

        [HttpGet("resource-plans/contract/{contractId:int}/versions")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> PlanVersionsAsync(int contractId, CancellationToken ct) => Ok(await _resources.GetPlanVersionsAsync(contractId, ct));

        [HttpPost("resource-plans/open")]
        public async Task<IActionResult> OpenPlanAsync([FromBody] PcResourcePlanOpenDto dto, CancellationToken ct) => Ok(await _resources.OpenPlanAsync(dto, ct));

        [HttpPost("resource-plans/assignments")]
        public async Task<IActionResult> SaveAssignmentsAsync([FromBody] PcActivityAssignmentsDto dto, CancellationToken ct) => Ok(await _resources.SaveAssignmentsAsync(dto, ct));

        [HttpDelete("resource-plans/assignments/{id:int}")]
        public async Task<IActionResult> DeleteAssignmentAsync(int id, CancellationToken ct) => Ok(await _resources.DeleteAssignmentAsync(id, ct));

        [HttpPost("resource-plans/mark-unassigned")]
        public async Task<IActionResult> MarkUnassignedAsync([FromBody] PcMarkUnassignedDto dto, CancellationToken ct) => Ok(await _resources.MarkUnassignedAsync(dto, ct));

        [HttpPost("resource-plans/action")]
        public async Task<IActionResult> PlanActionAsync([FromBody] PcResourcePlanActionDto dto, CancellationToken ct) => Ok(await _resources.PlanActionAsync(dto, ct));

        [HttpPost("resource-plans/discard")]
        public async Task<IActionResult> DiscardPlanAsync([FromBody] PcResourcePlanDiscardDto dto, CancellationToken ct) => Ok(await _resources.DiscardPlanAsync(dto, ct));

        [HttpGet("resource-plans/{id:int}/budget")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> BudgetAsync(int id, [FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _resources.GetBudgetAsync(id, asOf, ct));

        [HttpGet("baselines/{id:int}/resources")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> BaselineResourcesAsync(int id, CancellationToken ct) => Ok(await _resources.GetBaselineResourcesAsync(id, ct));
    }
}
