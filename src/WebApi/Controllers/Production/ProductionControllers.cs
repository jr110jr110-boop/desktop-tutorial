using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.ProductionDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // Production / shop floor (MES), release P1 part A. Role checks per action (segregation of duties) are in the services; rule violations answer 200 with success=false.

    /// <summary>Work centres, machines, operation standards, routing revisions and reason codes.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/production")]
    [RolesCheckedInService]
    public class ProductionMasterController : ControllerBase
    {
        private readonly IMfMasterService _master;

        public ProductionMasterController(IMfMasterService master)
        {
            _master = master;
        }

        [HttpGet("work-centers")]
        public async Task<IActionResult> WorkCentersAsync([FromQuery] WorkCenterQuery query, CancellationToken ct) => Ok((await _master.GetWorkCentersAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("work-centers/{id:int}")]
        public async Task<IActionResult> WorkCenterAsync(int id, CancellationToken ct) => Ok(await _master.GetWorkCenterAsync(id, ct));

        [HttpPost("work-centers")]
        public async Task<IActionResult> SaveWorkCenterAsync([FromBody] SaveWorkCenterDto dto, CancellationToken ct) => Ok(await _master.SaveWorkCenterAsync(dto, ct));

        [HttpPost("work-centers/rates")]
        public async Task<IActionResult> SaveRatesAsync([FromBody] SaveWorkCenterRatesDto dto, CancellationToken ct) => Ok(await _master.SaveWorkCenterRatesAsync(dto, ct));

        [HttpGet("machines")]
        public async Task<IActionResult> MachinesAsync([FromQuery] MachineQuery query, CancellationToken ct) => Ok((await _master.GetMachinesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("machines/info")]
        public async Task<IActionResult> SaveMachineInfoAsync([FromBody] SaveMachineInfoDto dto, CancellationToken ct) => Ok(await _master.SaveMachineInfoAsync(dto, ct));

        [HttpGet("operation-standards")]
        public async Task<IActionResult> StandardsAsync([FromQuery] OperationStandardQuery query, CancellationToken ct) => Ok((await _master.GetStandardsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("operation-standards")]
        public async Task<IActionResult> SaveStandardAsync([FromBody] SaveOperationStandardDto dto, CancellationToken ct) => Ok(await _master.SaveStandardAsync(dto, ct));

        [HttpGet("routings")]
        public async Task<IActionResult> RoutingsAsync([FromQuery] RoutingQuery query, CancellationToken ct) => Ok((await _master.GetRoutingsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("routings/{id:int}")]
        public async Task<IActionResult> RoutingAsync(int id, CancellationToken ct) => Ok(await _master.GetRoutingAsync(id, ct));

        [HttpPost("routings")]
        public async Task<IActionResult> SaveRoutingAsync([FromBody] SaveRoutingRevisionDto dto, CancellationToken ct) => Ok(await _master.SaveRoutingAsync(dto, ct));

        [HttpPost("routings/action")]
        public async Task<IActionResult> RoutingActionAsync([FromBody] RoutingActionDto dto, CancellationToken ct) => Ok(await _master.RoutingActionAsync(dto, ct));

        [HttpGet("reason-codes")]
        public async Task<IActionResult> ReasonCodesAsync([FromQuery] ReasonCodeQuery query, CancellationToken ct) => Ok((await _master.GetReasonCodesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("reason-codes")]
        public async Task<IActionResult> SaveReasonCodeAsync([FromBody] SaveReasonCodeDto dto, CancellationToken ct) => Ok(await _master.SaveReasonCodeAsync(dto, ct));
    }

    /// <summary>Production orders, the release gate, operations, QC decisions and rework.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/production")]
    [RolesCheckedInService]
    public class ProductionOrdersController : ControllerBase
    {
        private readonly IMfProductionService _production;
        private readonly IMfDraftingService _drafting;

        public ProductionOrdersController(IMfProductionService production, IMfDraftingService drafting)
        {
            _production = production;
            _drafting = drafting;
        }

        [HttpGet("orders")]
        public async Task<IActionResult> OrdersAsync([FromQuery] OrderQuery query, CancellationToken ct) => Ok((await _production.GetOrdersAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("orders/{id:int}")]
        public async Task<IActionResult> OrderAsync(int id, CancellationToken ct) => Ok(await _production.GetOrderAsync(id, ct));

        [HttpPost("orders/draft")]
        public async Task<IActionResult> DraftAsync([FromBody] DraftOrdersDto dto, CancellationToken ct) => Ok(await _drafting.DraftAsync(dto, ct));

        [HttpPost("orders/components")]
        public async Task<IActionResult> ComponentsAsync([FromBody] SaveComponentsDto dto, CancellationToken ct) => Ok(await _production.SaveComponentsAsync(dto, ct));

        [HttpGet("orders/{id:int}/release-check")]
        public async Task<IActionResult> ReleaseCheckAsync(int id, CancellationToken ct) => Ok(await _production.ReleaseCheckAsync(id, ct));

        [HttpPost("orders/release")]
        public async Task<IActionResult> ReleaseAsync([FromBody] ReleaseOrderDto dto, CancellationToken ct) => Ok(await _production.ReleaseAsync(dto, ct));

        [HttpPost("orders/action")]
        public async Task<IActionResult> OrderActionAsync([FromBody] OrderActionDto dto, CancellationToken ct) => Ok(await _production.OrderActionAsync(dto, ct));

        [HttpGet("operations")]
        public async Task<IActionResult> OperationsAsync([FromQuery] OperationQuery query, CancellationToken ct) => Ok((await _production.GetOperationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("operations/action")]
        public async Task<IActionResult> OperationActionAsync([FromBody] OperationActionDto dto, CancellationToken ct) => Ok(await _production.OperationActionAsync(dto, ct));

        [HttpPost("operations/qc-decision")]
        public async Task<IActionResult> QcDecisionAsync([FromBody] QcDecisionDto dto, CancellationToken ct) => Ok(await _production.QcDecisionAsync(dto, ct));

        [HttpPost("operations/rework")]
        public async Task<IActionResult> ReworkAsync([FromBody] ReworkOperationDto dto, CancellationToken ct) => Ok(await _production.ReworkAsync(dto, ct));
    }

    /// <summary>The shop-floor terminal, time bookings, breakdown reports and the attendance check.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/production")]
    [RolesCheckedInService]
    public class ProductionShopFloorController : ControllerBase
    {
        private readonly IMfShopFloorService _shop;

        public ProductionShopFloorController(IMfShopFloorService shop)
        {
            _shop = shop;
        }

        [HttpGet("terminal/my-operations")]
        public async Task<IActionResult> MyOperationsAsync([FromQuery] TerminalQuery query, CancellationToken ct) => Ok((await _shop.MyOperationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("terminal/scan")]
        public async Task<IActionResult> ScanAsync([FromQuery] TerminalQuery query, CancellationToken ct) => Ok((await _shop.ScanAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("terminal/start")]
        public async Task<IActionResult> StartAsync([FromBody] StartOperationDto dto, CancellationToken ct) => Ok(await _shop.StartAsync(dto, ct));

        [HttpPost("terminal/pause")]
        public async Task<IActionResult> PauseAsync([FromBody] PauseOperationDto dto, CancellationToken ct) => Ok(await _shop.PauseAsync(dto, ct));

        [HttpPost("terminal/report-quantity")]
        public async Task<IActionResult> ReportQuantityAsync([FromBody] ReportQuantityDto dto, CancellationToken ct) => Ok(await _shop.ReportQuantityAsync(dto, ct));

        [HttpPost("terminal/finish")]
        public async Task<IActionResult> FinishAsync([FromBody] FinishOperationDto dto, CancellationToken ct) => Ok(await _shop.FinishAsync(dto, ct));

        [HttpPost("terminal/request-qc")]
        public async Task<IActionResult> RequestQcAsync([FromBody] RequestQcDto dto, CancellationToken ct) => Ok(await _shop.RequestQcAsync(dto, ct));

        [HttpPost("terminal/machine-down")]
        public async Task<IActionResult> MachineDownAsync([FromBody] MachineDownDto dto, CancellationToken ct) => Ok(await _shop.MachineDownAsync(dto, ct));

        [HttpGet("maintenance-requests")]
        public async Task<IActionResult> MaintenanceRequestsAsync([FromQuery] MaintenanceRequestQuery query, CancellationToken ct) => Ok((await _shop.GetMaintenanceRequestsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("bookings")]
        public async Task<IActionResult> BookingsAsync([FromQuery] BookingQuery query, CancellationToken ct) => Ok((await _shop.GetBookingsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("bookings")]
        public async Task<IActionResult> CreateBookingAsync([FromBody] SaveBookingDto dto, CancellationToken ct) => Ok(await _shop.CreateBookingAsync(dto, ct));

        [HttpPost("bookings/edit")]
        public async Task<IActionResult> EditBookingAsync([FromBody] EditBookingDto dto, CancellationToken ct) => Ok(await _shop.EditBookingAsync(dto, ct));

        /// <summary>An extra booking on an operation of a completed order (its own bookings are frozen), by the shop supervisor with a reason; it waits for the approval of another production manager.</summary>
        [HttpPost("bookings/correct")]
        public async Task<IActionResult> CorrectBookingAsync([FromBody] SaveCorrectionBookingDto dto, CancellationToken ct) => Ok(await _shop.CorrectBookingAsync(dto, ct));

        /// <summary>A production manager other than the creator approves (or rejects) a correction booking; before that it counts in no cost run, hour report or OEE.</summary>
        [HttpPost("bookings/approve-correction")]
        public async Task<IActionResult> ApproveCorrectionAsync([FromBody] ApproveCorrectionDto dto, CancellationToken ct) => Ok(await _shop.ApproveCorrectionAsync(dto, ct));

        /// <summary>Booked hours per work centre and day, without any name.</summary>
        [HttpGet("bookings/hours")]
        public async Task<IActionResult> BookingHoursAsync([FromQuery] BookingHoursQuery query, CancellationToken ct) => Ok(await _shop.GetBookingHoursAsync(query, ct));

        [HttpGet("attendance-differences")]
        public async Task<IActionResult> AttendanceDifferencesAsync([FromQuery] AttendanceDifferenceQuery query, CancellationToken ct) => Ok((await _shop.GetAttendanceDifferencesAsync(query, ct)).ToPaginatedResultSafe());
    }
}
