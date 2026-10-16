using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // Cost accounting and the Sepidar link: accounting events, account mapping, costs of work orders, valuation.
    [ApiController]
    [Authorize]
    [Route("api/v1/finance")]
    public class FinanceLinkController : ControllerBase
    {
        private const string Readers = "PMO,CEO,FINANCIAL,PLANNING";
        private readonly IFinanceLinkService _service;
        public FinanceLinkController(IFinanceLinkService service) => _service = service;

        [HttpGet("events")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> EventsAsync([FromQuery] AccountingEventQuery query, CancellationToken ct) =>
            Ok((await _service.GetEventsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("check")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> CheckAsync(CancellationToken ct) => Ok(await _service.CheckAsync(ct));

        /// <summary>Voucher file of the events waiting for Sepidar (UTF-8 CSV, opens in Excel).</summary>
        [HttpPost("events/export")]
        [Authorize(Roles = InventoryRoles.Finance)]
        public async Task<IActionResult> ExportAsync([FromBody] ExportEventsDto dto, CancellationToken ct)
        {
            var (content, fileName, error) = await _service.ExportEventsAsync(dto, ct);
            if (error != null) return Ok(Common.Results.ResponseDtoHelper.BadRequest<bool>(error));
            return File(content, "text/csv; charset=utf-8", fileName);
        }

        [HttpPost("events/mark")]
        [Authorize(Roles = InventoryRoles.Finance)]
        public async Task<IActionResult> MarkAsync([FromBody] MarkEventsDto dto, CancellationToken ct) => Ok(await _service.MarkEventsAsync(dto, ct));

        [HttpGet("accounts")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> AccountsAsync(CancellationToken ct) => Ok(await _service.GetAccountMappingsAsync(ct));

        [HttpPost("accounts")]
        [Authorize(Roles = InventoryRoles.Finance)]
        public async Task<IActionResult> SaveAccountAsync([FromBody] AccountMappingDto dto, CancellationToken ct) => Ok(await _service.SaveAccountMappingAsync(dto, ct));

        [HttpGet("cost-centers")]
        public async Task<IActionResult> CostCentersAsync(CancellationToken ct) => Ok(await _service.GetCostCentersAsync(ct));

        [HttpPost("cost-centers")]
        [Authorize(Roles = InventoryRoles.Finance)]
        public async Task<IActionResult> SaveCostCenterAsync([FromBody] CostCenterDto dto, CancellationToken ct) => Ok(await _service.SaveCostCenterAsync(dto, ct));

        [HttpGet("costs")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> CostsAsync([FromQuery] int? contractId, CancellationToken ct) => Ok(await _service.GetWorkOrderCostsAsync(contractId, ct));

        [HttpGet("valuation")]
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> ValuationAsync([FromQuery] string contains, CancellationToken ct) => Ok(await _service.GetValuationAsync(contains, ct));

        /// <summary>CEO dashboard: stock, projects (price, cost, commitments), procurement, payables, quality, Sepidar link.</summary>
        [HttpGet("ceo-dashboard")]
        [Authorize(Roles = "CEO,PMO")]
        public async Task<IActionResult> CeoDashboardAsync([FromServices] ICeoDashboardService dashboard, CancellationToken ct) => Ok(await dashboard.GetAsync(ct));
    }
}
