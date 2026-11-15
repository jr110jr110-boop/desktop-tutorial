using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Entities.Model.NasbNiro.ProjectControl;
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
        private readonly IPcResourceLoadService _load;
        private readonly IPcResourcePerformanceService _performance;

        public ProjectControlResourceController(IPcResourceService resources, IPcResourceLoadService load, IPcResourcePerformanceService performance) { _resources = resources; _load = load; _performance = performance; }

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

        // ------------------------------------------------------------------------------------------ phase 4c: load, over-allocations, levelling proposals

        [HttpGet("resource-load")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> LoadAsync([FromQuery] PcResourceLoadQuery query, CancellationToken ct) => Ok(await _load.GetLoadAsync(query, ct));

        [HttpGet("resource-overloads")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> OverloadsAsync([FromQuery] PcResourceOverloadQuery query, CancellationToken ct) => Ok((await _load.GetOverloadsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("resource-overloads/{id:int}")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> OverloadAsync(int id, CancellationToken ct) => Ok(await _load.GetOverloadAsync(id, ct));

        [HttpPost("resource-overloads/accept")]
        public async Task<IActionResult> AcceptOverloadAsync([FromBody] PcOverloadAcceptDto dto, CancellationToken ct) => Ok(await _load.AcceptOverloadAsync(dto, ct));

        [HttpGet("leveling-proposals")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> ProposalsAsync([FromQuery] PcLevelingProposalQuery query, CancellationToken ct) => Ok((await _load.GetProposalsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("leveling-proposals/{id:int}")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> ProposalAsync(int id, CancellationToken ct) => Ok(await _load.GetProposalAsync(id, ct));

        [HttpPost("leveling-proposals/action")]
        public async Task<IActionResult> ProposalActionAsync([FromBody] PcLevelingActionDto dto, CancellationToken ct) => Ok(await _load.ProposalActionAsync(dto, ct));

        // ------------------------------------------------------------------------------------------ phase 4d: MS Project import, budget and performance of the resources, cost proposals

        [HttpPost("resource-plans/import/preview")]
        public async Task<IActionResult> ImportPreviewAsync([FromBody] PcMspImportRequestDto dto, CancellationToken ct) => Ok(await _resources.PreviewImportAsync(dto, ct));

        [HttpPost("resource-plans/import")]
        public async Task<IActionResult> ImportAsync([FromBody] PcMspImportRequestDto dto, CancellationToken ct) => Ok(await _resources.ImportAsync(dto, ct));

        [HttpGet("resource-performance/contract/{contractId:int}")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> PerformanceAsync(int contractId, [FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _performance.GetPerformanceAsync(contractId, asOf, ct));

        [HttpGet("resource-cost-proposals")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> CostProposalsAsync([FromQuery] int? contractId, [FromQuery] PcCostProposalStatus? status, [FromQuery] int pageNumber, [FromQuery] int pageSize, CancellationToken ct) =>
            Ok((await _performance.GetProposalsAsync(contractId, status, pageNumber, pageSize, ct)).ToPaginatedResultSafe());

        [HttpPost("resource-cost-proposals/decide")]
        public async Task<IActionResult> DecideCostProposalAsync([FromBody] PcCostProposalDecideDto dto, CancellationToken ct) => Ok(await _performance.DecideProposalAsync(dto, ct));
    }
}
