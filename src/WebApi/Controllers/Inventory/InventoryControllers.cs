using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.InventoryDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // Warehouse (انبارداری), part traceability (ردیابی قطعات) and the Sepidar link.
    // Every signed-in user can read; changes need the roles listed in InventoryRoles.
    // All answers are ResponseDto { success, message, data } with HTTP 200, like the rest of the API.

    [ApiController]
    [Authorize]
    [Route("api/v1/inventory/groups")]
    public class InventoryGroupsController : ControllerBase
    {
        private readonly IInventoryMasterService _service;
        public InventoryGroupsController(IInventoryMasterService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAsync(CancellationToken ct) => Ok(await _service.GetGroupsAsync(ct));

        [HttpPost]
        [Authorize(Roles = InventoryRoles.MasterData)]
        public async Task<IActionResult> SaveAsync([FromBody] SaveItemGroupDto dto, CancellationToken ct) => Ok(await _service.SaveGroupAsync(dto, ct));

        [HttpDelete("{id:int}")]
        [Authorize(Roles = InventoryRoles.MasterData)]
        public async Task<IActionResult> DeleteAsync(int id, CancellationToken ct) => Ok(await _service.DeleteGroupAsync(id, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/inventory/items")]
    public class InventoryItemsController : ControllerBase
    {
        private readonly IInventoryMasterService _service;
        public InventoryItemsController(IInventoryMasterService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAsync([FromQuery] InventoryItemQuery query, CancellationToken ct) =>
            Ok((await _service.GetItemsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetByIdAsync(int id, CancellationToken ct) => Ok(await _service.GetItemAsync(id, ct));

        [HttpGet("by-code")]
        public async Task<IActionResult> GetByCodeAsync([FromQuery] string code, CancellationToken ct) => Ok(await _service.GetItemByCodeAsync(code, ct));

        [HttpPost]
        [Authorize(Roles = InventoryRoles.MasterData)]
        public async Task<IActionResult> SaveAsync([FromBody] SaveInventoryItemDto dto, CancellationToken ct) => Ok(await _service.SaveItemAsync(dto, ct));

        [HttpPost("import")]
        [Authorize(Roles = InventoryRoles.MasterData)]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> ImportAsync([FromBody] List<ImportItemRowDto> rows, CancellationToken ct) => Ok(await _service.ImportItemsAsync(rows, ct));

        [HttpDelete("{id:int}")]
        [Authorize(Roles = InventoryRoles.MasterData)]
        public async Task<IActionResult> DeleteAsync(int id, CancellationToken ct) => Ok(await _service.DeleteItemAsync(id, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/inventory/warehouses")]
    public class InventoryWarehousesController : ControllerBase
    {
        private readonly IInventoryMasterService _service;
        public InventoryWarehousesController(IInventoryMasterService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAsync(CancellationToken ct) => Ok(await _service.GetWarehousesAsync(ct));

        [HttpPost]
        [Authorize(Roles = InventoryRoles.MasterData)]
        public async Task<IActionResult> SaveAsync([FromBody] SaveWarehouseDto dto, CancellationToken ct) => Ok(await _service.SaveWarehouseAsync(dto, ct));

        [HttpPost("locations")]
        [Authorize(Roles = InventoryRoles.MasterData)]
        public async Task<IActionResult> SaveLocationAsync([FromBody] SaveLocationDto dto, CancellationToken ct) => Ok(await _service.SaveLocationAsync(dto, ct));

        [HttpDelete("locations/{id:int}")]
        [Authorize(Roles = InventoryRoles.MasterData)]
        public async Task<IActionResult> DeleteLocationAsync(int id, CancellationToken ct) => Ok(await _service.DeleteLocationAsync(id, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/inventory/documents")]
    public class InventoryDocumentsController : ControllerBase
    {
        private readonly IInventoryDocumentService _service;
        public InventoryDocumentsController(IInventoryDocumentService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAsync([FromQuery] DocumentQuery query, CancellationToken ct) =>
            Ok((await _service.GetDocumentsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetByIdAsync(int id, CancellationToken ct) => Ok(await _service.GetDocumentAsync(id, ct));

        [HttpPost]
        [Authorize(Roles = InventoryRoles.Documents)]
        public async Task<IActionResult> SaveAsync([FromBody] SaveDocumentDto dto, CancellationToken ct) => Ok(await _service.SaveDraftAsync(dto, ct));

        [HttpDelete("{id:int}")]
        [Authorize(Roles = InventoryRoles.Documents)]
        public async Task<IActionResult> DeleteAsync(int id, CancellationToken ct) => Ok(await _service.DeleteDraftAsync(id, ct));

        [HttpPost("{id:int}/confirm")]
        [Authorize(Roles = InventoryRoles.Documents)]
        public async Task<IActionResult> ConfirmAsync(int id, CancellationToken ct) => Ok(await _service.ConfirmAsync(id, ct));

        [HttpPost("{id:int}/cancel")]
        [Authorize(Roles = InventoryRoles.Documents)]
        public async Task<IActionResult> CancelAsync(int id, [FromBody] CancelDocumentDto dto, CancellationToken ct) => Ok(await _service.CancelAsync(id, dto, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/inventory/stock")]
    public class InventoryStockController : ControllerBase
    {
        private readonly IInventoryStockService _service;
        public InventoryStockController(IInventoryStockService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetBalancesAsync([FromQuery] StockQuery query, CancellationToken ct) =>
            Ok((await _service.GetBalancesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("items")]
        public async Task<IActionResult> GetItemStocksAsync([FromQuery] StockQuery query, CancellationToken ct) =>
            Ok((await _service.GetItemStocksAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("kardex")]
        public async Task<IActionResult> GetKardexAsync([FromQuery] KardexQuery query, CancellationToken ct) => Ok(await _service.GetKardexAsync(query, ct));

        [HttpGet("lots")]
        public async Task<IActionResult> GetLotsAsync([FromQuery] LotQuery query, CancellationToken ct) =>
            Ok((await _service.GetLotsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("lots")]
        [Authorize(Roles = InventoryRoles.Production)]
        public async Task<IActionResult> SaveLotAsync([FromBody] SaveLotDto dto, CancellationToken ct) => Ok(await _service.SaveLotAsync(dto, ct));

        [HttpGet("serials")]
        public async Task<IActionResult> GetSerialsAsync([FromQuery] SerialQuery query, CancellationToken ct) =>
            Ok((await _service.GetSerialsAsync(query, ct)).ToPaginatedResultSafe());
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/inventory/trace")]
    public class TraceabilityController : ControllerBase
    {
        private readonly ITraceabilityService _service;
        public TraceabilityController(ITraceabilityService service) => _service = service;

        [HttpGet("search")]
        public async Task<IActionResult> SearchAsync([FromQuery] string q, CancellationToken ct) => Ok(await _service.SearchAsync(q, ct));

        [HttpGet("serial/{id:int}")]
        public async Task<IActionResult> SerialAsync(int id, CancellationToken ct) => Ok(await _service.GetSerialTraceAsync(id, ct));

        [HttpGet("lot/{id:int}")]
        public async Task<IActionResult> LotAsync(int id, CancellationToken ct) => Ok(await _service.GetLotTraceAsync(id, ct));

        [HttpPost("serials")]
        [Authorize(Roles = InventoryRoles.Production)]
        public async Task<IActionResult> CreateSerialAsync([FromBody] CreateSerialDto dto, CancellationToken ct) => Ok(await _service.CreateSerialAsync(dto, ct));

        [HttpPut("serials")]
        [Authorize(Roles = InventoryRoles.Production)]
        public async Task<IActionResult> UpdateSerialAsync([FromBody] UpdateSerialDto dto, CancellationToken ct) => Ok(await _service.UpdateSerialAsync(dto, ct));

        [HttpGet("templates")]
        public async Task<IActionResult> TemplatesAsync([FromQuery] OperationTemplateQuery query, CancellationToken ct) => Ok(await _service.GetTemplatesAsync(query, ct));

        [HttpPost("templates")]
        [Authorize(Roles = InventoryRoles.Routing)]
        public async Task<IActionResult> SaveTemplateAsync([FromBody] SaveOperationTemplateDto dto, CancellationToken ct) => Ok(await _service.SaveTemplateAsync(dto, ct));

        [HttpDelete("templates/{id:int}")]
        [Authorize(Roles = InventoryRoles.Routing)]
        public async Task<IActionResult> DeleteTemplateAsync(int id, CancellationToken ct) => Ok(await _service.DeleteTemplateAsync(id, ct));

        [HttpGet("operations")]
        public async Task<IActionResult> OperationsAsync([FromQuery] PartOperationQuery query, CancellationToken ct) =>
            Ok((await _service.GetOperationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("operations/apply-routing")]
        [Authorize(Roles = InventoryRoles.Production)]
        public async Task<IActionResult> ApplyRoutingAsync([FromBody] ApplyRoutingDto dto, CancellationToken ct) => Ok(await _service.ApplyRoutingAsync(dto, ct));

        [HttpPost("operations")]
        [Authorize(Roles = InventoryRoles.Production)]
        public async Task<IActionResult> AddOperationAsync([FromBody] AddPartOperationDto dto, CancellationToken ct) => Ok(await _service.AddOperationAsync(dto, ct));

        [HttpPut("operations")]
        [Authorize(Roles = InventoryRoles.Production)]
        public async Task<IActionResult> UpdateOperationAsync([FromBody] UpdatePartOperationDto dto, CancellationToken ct) => Ok(await _service.UpdateOperationAsync(dto, ct));

        [HttpPost("operations/qc")]
        [Authorize(Roles = InventoryRoles.Qc)]
        public async Task<IActionResult> QcAsync([FromBody] QcPartOperationDto dto, CancellationToken ct) => Ok(await _service.QcOperationAsync(dto, ct));

        [HttpPost("operations/files")]
        [Authorize(Roles = InventoryRoles.Production)]
        public async Task<IActionResult> AddFileAsync([FromBody] AddOperationFileDto dto, CancellationToken ct) => Ok(await _service.AddOperationFileAsync(dto, ct));

        [HttpPost("assembly/install")]
        [Authorize(Roles = InventoryRoles.Production)]
        public async Task<IActionResult> InstallAsync([FromBody] InstallPartDto dto, CancellationToken ct) => Ok(await _service.InstallAsync(dto, ct));

        [HttpPost("assembly/remove")]
        [Authorize(Roles = InventoryRoles.Production)]
        public async Task<IActionResult> RemoveAsync([FromBody] RemovePartDto dto, CancellationToken ct) => Ok(await _service.RemoveAsync(dto, ct));
    }

    [ApiController]
    [Authorize(Roles = InventoryRoles.Finance)]
    [Route("api/v1/inventory/sepidar")]
    public class SepidarLinkController : ControllerBase
    {
        private readonly ISepidarLinkService _service;
        public SepidarLinkController(ISepidarLinkService service) => _service = service;

        [HttpGet("outbox")]
        public async Task<IActionResult> OutboxAsync([FromQuery] SepidarOutboxQuery query, CancellationToken ct) =>
            Ok((await _service.GetOutboxAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("check")]
        public async Task<IActionResult> CheckAsync(CancellationToken ct) => Ok(await _service.CheckAsync(ct));

        /// <summary>CSV file of the documents waiting for Sepidar (UTF-8, opens in Excel).</summary>
        [HttpPost("export")]
        public async Task<IActionResult> ExportAsync([FromBody] SepidarExportDto dto, CancellationToken ct)
        {
            var (content, fileName, error) = await _service.ExportCsvAsync(dto, ct);
            if (error != null) return Ok(Common.Results.ResponseDtoHelper.BadRequest<object>(error));
            return File(content, "text/csv; charset=utf-8", fileName);
        }

        [HttpGet("mappings")]
        public async Task<IActionResult> MappingsAsync([FromQuery] string entityType, CancellationToken ct) => Ok(await _service.GetMappingsAsync(entityType, ct));

        [HttpPost("mappings")]
        public async Task<IActionResult> SaveMappingAsync([FromBody] SaveSepidarMappingDto dto, CancellationToken ct) => Ok(await _service.SaveMappingAsync(dto, ct));
    }
}
