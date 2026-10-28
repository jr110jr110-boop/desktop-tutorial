using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.ProductionDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // Production / shop floor (MES), release P1 part B. Role checks are in the services; rule violations answer 200 with success=false.

    /// <summary>Daily cost posting of the time bookings and the month-end run of the labour pools (finance, finance manager and the production manager).</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/production")]
    public class ProductionCostController : ControllerBase
    {
        private readonly IMfCostService _cost;
        private readonly IMfLabourService _labour;

        public ProductionCostController(IMfCostService cost, IMfLabourService labour)
        {
            _cost = cost;
            _labour = labour;
        }

        [HttpPost("cost/run")]
        public async Task<IActionResult> RunAsync([FromBody] RunCostDto dto, CancellationToken ct) => Ok(await _cost.RunAsync(dto, ct));

        [HttpGet("cost/runs")]
        public async Task<IActionResult> RunsAsync([FromQuery] CostRunQuery query, CancellationToken ct) => Ok((await _cost.GetRunsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("cost/postings")]
        public async Task<IActionResult> PostingsAsync([FromQuery] CostPostingQuery query, CancellationToken ct) => Ok((await _cost.GetPostingsAsync(query, ct)).ToPaginatedResultSafe());

        /// <summary>R1p D1: the month-end run of the labour pools (finance manager, finance, production manager): closes every ended month, stores its pools (the rates of the following days) and posts the true-up (actual pool cost less what the days absorbed) as one company-level line per pool; idempotent.</summary>
        [HttpPost("cost/labour-month")]
        public async Task<IActionResult> LabourMonthAsync([FromBody] MfLabourMonthRunDto dto, CancellationToken ct) => Ok(await _labour.RunAsync(dto, ct));

        /// <summary>R1p D1: the months already closed with their pools (people, hours, pooled rate, absorbed, true-up); the amounts of a protected pool are hidden from readers outside PAYROLL, FINANCEMANAGER and CEO.</summary>
        [HttpGet("cost/labour-months")]
        public async Task<IActionResult> LabourMonthsAsync(CancellationToken ct) => Ok(await _labour.HistoryAsync(ct));
    }

    /// <summary>The finite-capacity scheduler, the load report and the dispatch list of a work centre.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/production")]
    public class ProductionScheduleController : ControllerBase
    {
        private readonly IMfScheduleService _schedule;

        public ProductionScheduleController(IMfScheduleService schedule)
        {
            _schedule = schedule;
        }

        [HttpPost("schedule/run")]
        public async Task<IActionResult> RunAsync([FromBody] RunScheduleDto dto, CancellationToken ct) => Ok(await _schedule.RunAsync(dto, ct));

        [HttpGet("load")]
        public async Task<IActionResult> LoadAsync([FromQuery] LoadQuery query, CancellationToken ct) => Ok(await _schedule.GetLoadAsync(query, ct));

        [HttpGet("dispatch")]
        public async Task<IActionResult> DispatchAsync([FromQuery] DispatchQuery query, CancellationToken ct) => Ok((await _schedule.GetDispatchAsync(query, ct)).ToPaginatedResultSafe());
    }

    /// <summary>Maintenance plans and orders, spare parts, labour, and the maintenance KPIs.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/production")]
    public class ProductionMaintenanceController : ControllerBase
    {
        private readonly IMfMaintenanceService _maintenance;

        public ProductionMaintenanceController(IMfMaintenanceService maintenance)
        {
            _maintenance = maintenance;
        }

        [HttpGet("maintenance/plans")]
        public async Task<IActionResult> PlansAsync([FromQuery] MaintenancePlanQuery query, CancellationToken ct) => Ok((await _maintenance.GetPlansAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("maintenance/plans")]
        public async Task<IActionResult> SavePlanAsync([FromBody] SaveMaintenancePlanDto dto, CancellationToken ct) => Ok(await _maintenance.SavePlanAsync(dto, ct));

        [HttpPost("maintenance/plans/draft-due")]
        public async Task<IActionResult> DraftDueAsync([FromBody] DraftDueDto dto, CancellationToken ct) => Ok(await _maintenance.DraftDueAsync(dto, ct));

        [HttpGet("maintenance/orders")]
        public async Task<IActionResult> OrdersAsync([FromQuery] MaintenanceOrderQuery query, CancellationToken ct) => Ok((await _maintenance.GetOrdersAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("maintenance/orders/{id:int}")]
        public async Task<IActionResult> OrderAsync(int id, CancellationToken ct) => Ok(await _maintenance.GetOrderAsync(id, ct));

        [HttpPost("maintenance/orders")]
        public async Task<IActionResult> CreateOrderAsync([FromBody] CreateMaintenanceOrderDto dto, CancellationToken ct) => Ok(await _maintenance.CreateOrderAsync(dto, ct));

        [HttpPost("maintenance/orders/save")]
        public async Task<IActionResult> SaveOrderAsync([FromBody] SaveMaintenanceOrderDto dto, CancellationToken ct) => Ok(await _maintenance.SaveOrderAsync(dto, ct));

        [HttpPost("maintenance/orders/action")]
        public async Task<IActionResult> ActionAsync([FromBody] MaintenanceOrderActionDto dto, CancellationToken ct) => Ok(await _maintenance.ActionAsync(dto, ct));

        [HttpPost("maintenance/labour")]
        public async Task<IActionResult> AddLabourAsync([FromBody] AddMaintenanceLabourDto dto, CancellationToken ct) => Ok(await _maintenance.AddLabourAsync(dto, ct));

        [HttpGet("maintenance/kpis")]
        public async Task<IActionResult> KpisAsync([FromQuery] KpiQuery query, CancellationToken ct) => Ok((await _maintenance.GetMachineKpisAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("oee")]
        public async Task<IActionResult> OeeAsync([FromQuery] KpiQuery query, CancellationToken ct) => Ok(await _maintenance.GetOeeAsync(query, ct));
    }

    /// <summary>The production manager's dashboard.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/production")]
    public class ProductionDashboardController : ControllerBase
    {
        private readonly IMfDashboardService _dashboard;

        public ProductionDashboardController(IMfDashboardService dashboard)
        {
            _dashboard = dashboard;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> DashboardAsync([FromQuery] DashboardQuery query, CancellationToken ct) => Ok(await _dashboard.GetAsync(query, ct));
    }
}
