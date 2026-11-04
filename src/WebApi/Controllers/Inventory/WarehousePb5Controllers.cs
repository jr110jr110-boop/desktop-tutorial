using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.InventoryDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // PB5: freight / landed cost, alerts, reorder list, reports with Excel, Excel templates and import, assets, unit conversions and consumption ceilings.

    public static class WarehouseFiles
    {
        public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/inventory/freight")]
    public class InventoryFreightController : ControllerBase
    {
        private readonly IInventoryFreightService _service;
        public InventoryFreightController(IInventoryFreightService service) => _service = service;

        [HttpGet]
        [Authorize(Roles = FreightRoles.Readers)]
        public async Task<IActionResult> GetAsync([FromQuery] FreightQuery query, CancellationToken ct) => Ok(await _service.GetAsync(query, ct));

        [HttpGet("{id:int}")]
        [Authorize(Roles = FreightRoles.Readers)]
        public async Task<IActionResult> GetByIdAsync(int id, CancellationToken ct) => Ok(await _service.GetByIdAsync(id, ct));

        [HttpPost]
        [Authorize(Roles = FreightRoles.Writers + ",FINANCIAL,FINANCEMANAGER")]
        public async Task<IActionResult> SaveAsync([FromBody] SaveFreightDto dto, CancellationToken ct) => Ok(await _service.SaveAsync(dto, ct));

        /// <summary>The accountant approves (never the person who prepared it, never an AI seat, never in a closed period).</summary>
        [HttpPost("{id:int}/approve")]
        [Authorize(Roles = FreightRoles.Approvers)]
        public async Task<IActionResult> ApproveAsync(int id, CancellationToken ct) => Ok(await _service.ApproveAsync(id, ct));

        [HttpPost("{id:int}/void")]
        [Authorize(Roles = FreightRoles.Writers + "," + FreightRoles.Approvers)]
        public async Task<IActionResult> VoidAsync(int id, [FromBody] VoidFreightDto dto, CancellationToken ct) => Ok(await _service.VoidAsync(id, dto, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/inventory/alerts")]
    public class InventoryAlertsController : ControllerBase
    {
        private readonly IWarehouseAlertService _alerts;
        private readonly IWarehouseReorderService _reorder;
        public InventoryAlertsController(IWarehouseAlertService alerts, IWarehouseReorderService reorder)
        {
            _alerts = alerts;
            _reorder = reorder;
        }

        /// <summary>Runs the consignment and incoming-inspection alerts now (the daily job does the same).</summary>
        [HttpPost("run")]
        [Authorize(Roles = InventoryRoles.Documents + ",PLANNING,QC,QA")]
        public async Task<IActionResult> RunAsync(CancellationToken ct) => Ok(await _alerts.RunAsync(null, ct));

        [HttpGet("reorder")]
        [Authorize(Roles = WarehouseReportRoles.Readers)]
        public async Task<IActionResult> ReorderAsync([FromQuery] ReorderQuery query, CancellationToken ct) => Ok(await _reorder.GetAsync(query, ct));

        /// <summary>A draft purchase request from the reorder list; a person reviews and submits it.</summary>
        [HttpPost("reorder/draft")]
        [Authorize(Roles = InventoryRoles.Planning + "," + InventoryRoles.Documents)]
        public async Task<IActionResult> DraftReorderAsync([FromBody] DraftReorderRequestDto dto, CancellationToken ct) => Ok(await _reorder.DraftAsync(dto, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/inventory/reports")]
    public class InventoryReportsController : ControllerBase
    {
        private readonly IWarehouseReportService _reports;
        public InventoryReportsController(IWarehouseReportService reports) => _reports = reports;

        [HttpGet("catalog")]
        [Authorize(Roles = WarehouseReportRoles.Readers)]
        public IActionResult Catalog() => Ok(_reports.GetCatalog());

        [HttpGet]
        [Authorize(Roles = WarehouseReportRoles.Readers)]
        public async Task<IActionResult> GetAsync([FromQuery] WarehouseReportQuery query, CancellationToken ct) => Ok(await _reports.GetAsync(query, ct));

        [HttpGet("export")]
        [Authorize(Roles = WarehouseReportRoles.Readers)]
        public async Task<IActionResult> ExportAsync([FromQuery] WarehouseReportQuery query, CancellationToken ct)
        {
            var result = await _reports.ExportAsync(query, ct);
            if (!result.Success || result.Data == null) return BadRequest(result);
            return File(result.Data, WarehouseFiles.Xlsx, "warehouse-" + (query?.Report ?? "report") + ".xlsx");
        }
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/inventory/excel")]
    public class InventoryExcelController : ControllerBase
    {
        private readonly IWarehouseExcelService _excel;
        public InventoryExcelController(IWarehouseExcelService excel) => _excel = excel;

        [HttpGet("template/{kind}")]
        [Authorize(Roles = InventoryRoles.Counts + ",ENGINEERING,FINANCEMANAGER")]
        public IActionResult Template(string kind)
        {
            var result = _excel.Template(kind);
            if (!result.Success || result.Data == null) return BadRequest(result);
            return File(result.Data, WarehouseFiles.Xlsx, "template-" + kind + ".xlsx");
        }

        /// <summary>Preview (Preview=true) or one draft document / stock count (Preview=false). Never confirmed by the import.</summary>
        [HttpPost("import")]
        [Authorize(Roles = InventoryRoles.Counts)]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> ImportAsync([FromForm] ExcelImportRequestDto request, IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return Ok(Common.Results.ResponseDtoHelper.BadRequest<ExcelImportResultDto>("فایل را انتخاب کنید."));
            using var ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms, ct);
            return Ok(await _excel.ImportAsync(request, ms.ToArray(), ct));
        }
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/inventory/register")]
    public class InventoryRegisterController : ControllerBase
    {
        private readonly IWarehouseRegisterService _service;
        public InventoryRegisterController(IWarehouseRegisterService service) => _service = service;

        [HttpGet("assets")]
        [Authorize(Roles = WarehouseReportRoles.Readers)]
        public async Task<IActionResult> AssetsAsync([FromQuery] InvAssetQuery query, CancellationToken ct) => Ok((await _service.GetAssetsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("assets")]
        [Authorize(Roles = WarehouseRegisterService.AssetRoles)]
        public async Task<IActionResult> SaveAssetAsync([FromBody] SaveInvAssetDto dto, CancellationToken ct) => Ok(await _service.SaveAssetAsync(dto, ct));

        [HttpGet("conversions")]
        [Authorize(Roles = WarehouseReportRoles.Readers)]
        public async Task<IActionResult> ConversionsAsync([FromQuery] int? itemId, CancellationToken ct) => Ok(await _service.GetConversionsAsync(itemId, ct));

        [HttpPost("conversions")]
        [Authorize(Roles = WarehouseRegisterService.RulesRoles)]
        public async Task<IActionResult> SaveConversionAsync([FromBody] SaveUnitConversionDto dto, CancellationToken ct) => Ok(await _service.SaveConversionAsync(dto, ct));

        [HttpGet("ceilings")]
        [Authorize(Roles = WarehouseReportRoles.Readers)]
        public async Task<IActionResult> CeilingsAsync([FromQuery] int? itemId, [FromQuery] int? workOrderId, CancellationToken ct) => Ok(await _service.GetCeilingsAsync(itemId, workOrderId, ct));

        [HttpPost("ceilings")]
        [Authorize(Roles = WarehouseRegisterService.RulesRoles)]
        public async Task<IActionResult> SaveCeilingAsync([FromBody] SaveCeilingDto dto, CancellationToken ct) => Ok(await _service.SaveCeilingAsync(dto, ct));

        [HttpDelete("ceilings/{id:int}")]
        [Authorize(Roles = WarehouseRegisterService.RulesRoles)]
        public async Task<IActionResult> DeleteCeilingAsync(int id, CancellationToken ct) => Ok(await _service.DeleteCeilingAsync(id, ct));
    }
}
