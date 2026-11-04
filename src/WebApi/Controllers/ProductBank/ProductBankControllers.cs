using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.ProductBankDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // PB1: part-number item coding, BOM master with revisions, OPC routings, product bank and project deviations.
    // Every signed-in user can read. Changes need the roles of PbRoles, which the services check again (an AI seat never signs or approves).
    // All answers are ResponseDto { success, message, data } with HTTP 200, like the rest of the API.

    [ApiController]
    [Authorize]
    [Route("api/v1/product-bank/coding")]
    public class PbCodingController : ControllerBase
    {
        private readonly IPbCodingService _service;
        public PbCodingController(IPbCodingService service) => _service = service;

        [HttpGet("stages")]
        public async Task<IActionResult> GetStagesAsync([FromQuery] bool includeInactive, CancellationToken ct) => Ok(await _service.GetStagesAsync(includeInactive, ct));

        [HttpPost("stages")]
        [Authorize(Roles = PbRoles.Planning)]
        public async Task<IActionResult> SaveStageAsync([FromBody] SavePbStageDto dto, CancellationToken ct) => Ok(await _service.SaveStageAsync(dto, ct));

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettingsAsync(CancellationToken ct) => Ok(await _service.GetSettingsAsync(ct));

        [HttpPost("settings")]
        [Authorize(Roles = PbRoles.Planning)]
        public async Task<IActionResult> SaveSettingAsync([FromBody] SavePbSettingDto dto, CancellationToken ct) => Ok(await _service.SaveSettingAsync(dto, ct));

        [HttpGet("preview")]
        public async Task<IActionResult> PreviewAsync([FromQuery] string partNumber, [FromQuery] string stageCode2, [FromQuery] string title, CancellationToken ct) =>
            Ok(await _service.PreviewAsync(partNumber, stageCode2, title, ct));

        [HttpPost("items")]
        [Authorize(Roles = PbRoles.Coding)]
        public async Task<IActionResult> CreateItemAsync([FromBody] PbCreateItemDto dto, CancellationToken ct) => Ok(await _service.CreateItemAsync(dto, ct));

        /// <summary>One-time upgrade step (PMO): existing items whose code or MESC code is a part number get that PartNumber.</summary>
        [HttpPost("adopt-part-numbers")]
        [Authorize(Roles = PbRoles.Admin)]
        public async Task<IActionResult> AdoptPartNumbersAsync(CancellationToken ct) => Ok(await _service.AdoptPartNumbersAsync(ct));

        [HttpGet("mappings")]
        public async Task<IActionResult> GetMappingsAsync([FromQuery] PbMappingQuery query, CancellationToken ct) =>
            Ok((await _service.GetMappingsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("mappings/import")]
        [Authorize(Roles = PbRoles.Coding)]
        public async Task<IActionResult> ImportMappingsAsync([FromBody] List<PbImportMappingRowDto> rows, CancellationToken ct) => Ok(await _service.ImportMappingsAsync(rows, ct));

        [HttpPost("mappings/suggest")]
        [Authorize(Roles = PbRoles.Coding)]
        public async Task<IActionResult> SuggestMappingsAsync(CancellationToken ct) => Ok(await _service.SuggestMappingsAsync(ct));

        [HttpPost("mappings")]
        [Authorize(Roles = PbRoles.Coding)]
        public async Task<IActionResult> SaveMappingAsync([FromBody] SavePbMappingDto dto, CancellationToken ct) => Ok(await _service.SaveMappingAsync(dto, ct));

        /// <summary>Approve or reject a mapping: ENGINEERING, never the person who proposed it.</summary>
        [HttpPost("mappings/decide")]
        [Authorize(Roles = PbRoles.Engineering)]
        public async Task<IActionResult> DecideMappingAsync([FromBody] PbDecideDto dto, CancellationToken ct) => Ok(await _service.DecideMappingAsync(dto, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/product-bank/boms")]
    public class PbBomsController : ControllerBase
    {
        private readonly IPbBomService _service;
        public PbBomsController(IPbBomService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAsync([FromQuery] PbBomQuery query, CancellationToken ct) => Ok((await _service.GetListAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetByIdAsync(int id, CancellationToken ct) => Ok(await _service.GetAsync(id, ct));

        [HttpGet("diff")]
        public async Task<IActionResult> DiffAsync([FromQuery] int from, [FromQuery] int to, CancellationToken ct) => Ok(await _service.DiffAsync(from, to, ct));

        [HttpGet("where-used")]
        public async Task<IActionResult> WhereUsedAsync([FromQuery] string partNumber, CancellationToken ct) => Ok(await _service.WhereUsedAsync(partNumber, ct));

        [HttpPost]
        [Authorize(Roles = PbRoles.Writers)]
        public async Task<IActionResult> SaveAsync([FromBody] SavePbBomDto dto, CancellationToken ct) => Ok(await _service.SaveAsync(dto, ct));

        [HttpPost("submit")]
        [Authorize(Roles = PbRoles.Writers)]
        public async Task<IActionResult> SubmitAsync([FromBody] PbBomActionDto dto, CancellationToken ct) => Ok(await _service.SubmitAsync(dto, ct));

        [HttpPost("return")]
        [Authorize(Roles = PbRoles.Reviewers)]
        public async Task<IActionResult> ReturnAsync([FromBody] PbBomActionDto dto, CancellationToken ct) => Ok(await _service.ReturnAsync(dto, ct));

        /// <summary>One signature of the block: checked, QC checked, manufacturing checked, approved. The service checks the role of the step and that nobody signs twice.</summary>
        [HttpPost("sign")]
        [Authorize(Roles = PbRoles.Reviewers)]
        public async Task<IActionResult> SignAsync([FromBody] PbSignDto dto, CancellationToken ct) => Ok(await _service.SignAsync(dto, ct));

        [HttpPost("release")]
        [Authorize(Roles = PbRoles.ReleaseRoles)]
        public async Task<IActionResult> ReleaseAsync([FromBody] PbBomActionDto dto, CancellationToken ct) => Ok(await _service.ReleaseAsync(dto, ct));

        [HttpPost("new-revision")]
        [Authorize(Roles = PbRoles.Writers)]
        public async Task<IActionResult> NewRevisionAsync([FromBody] PbBomActionDto dto, CancellationToken ct) => Ok(await _service.NewRevisionAsync(dto, ct));

        [HttpPost("obsolete")]
        [Authorize(Roles = PbRoles.ReleaseRoles)]
        public async Task<IActionResult> ObsoleteAsync([FromBody] PbBomActionDto dto, CancellationToken ct) => Ok(await _service.ObsoleteAsync(dto, ct));

        [HttpDelete("{id:int}")]
        [Authorize(Roles = PbRoles.Writers)]
        public async Task<IActionResult> DeleteDraftAsync(int id, CancellationToken ct) => Ok(await _service.DeleteDraftAsync(id, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/product-bank/routings")]
    public class PbRoutingsController : ControllerBase
    {
        private readonly IPbRoutingService _service;
        public PbRoutingsController(IPbRoutingService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> GetAsync([FromQuery] int? productId, [FromQuery] string partNumber, CancellationToken ct) => Ok(await _service.GetListAsync(productId, partNumber, ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetByIdAsync(int id, CancellationToken ct) => Ok(await _service.GetAsync(id, ct));

        [HttpPost]
        [Authorize(Roles = PbRoles.Writers)]
        public async Task<IActionResult> SaveAsync([FromBody] SavePbOpcDto dto, CancellationToken ct) => Ok(await _service.SaveAsync(dto, ct));

        [HttpPost("submit")]
        [Authorize(Roles = PbRoles.Writers)]
        public async Task<IActionResult> SubmitAsync([FromBody] PbOpcActionDto dto, CancellationToken ct) => Ok(await _service.SubmitAsync(dto, ct));

        [HttpPost("return")]
        [Authorize(Roles = PbRoles.Reviewers)]
        public async Task<IActionResult> ReturnAsync([FromBody] PbOpcActionDto dto, CancellationToken ct) => Ok(await _service.ReturnAsync(dto, ct));

        [HttpPost("check")]
        [Authorize(Roles = PbRoles.CheckRoles)]
        public async Task<IActionResult> CheckAsync([FromBody] PbOpcActionDto dto, CancellationToken ct) => Ok(await _service.CheckAsync(dto, ct));

        [HttpPost("approve")]
        [Authorize(Roles = PbRoles.ApproveRoles)]
        public async Task<IActionResult> ApproveAsync([FromBody] PbOpcActionDto dto, CancellationToken ct) => Ok(await _service.ApproveAsync(dto, ct));

        [HttpPost("new-revision")]
        [Authorize(Roles = PbRoles.Writers)]
        public async Task<IActionResult> NewRevisionAsync([FromBody] PbOpcActionDto dto, CancellationToken ct) => Ok(await _service.NewRevisionAsync(dto, ct));

        /// <summary>Writes the approved steps into the MES routing (operation templates and a routing revision that still needs the two MES approvals).</summary>
        [HttpPost("publish")]
        [Authorize(Roles = PbRoutingService.PublishRoles)]
        public async Task<IActionResult> PublishAsync([FromBody] PbOpcActionDto dto, CancellationToken ct) => Ok(await _service.PublishToMesAsync(dto, ct));

        [HttpDelete("{id:int}")]
        [Authorize(Roles = PbRoles.Writers)]
        public async Task<IActionResult> DeleteDraftAsync(int id, CancellationToken ct) => Ok(await _service.DeleteDraftAsync(id, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/product-bank")]
    public class PbProductsController : ControllerBase
    {
        private readonly IPbProductService _service;
        private readonly IPbSeedService _seed;
        public PbProductsController(IPbProductService service, IPbSeedService seed) { _service = service; _seed = seed; }

        [HttpGet("products")]
        public async Task<IActionResult> GetProductsAsync(CancellationToken ct) => Ok(await _service.GetProductsAsync(ct));

        [HttpPost("products")]
        [Authorize(Roles = PbRoles.Writers)]
        public async Task<IActionResult> SaveProductAsync([FromBody] SavePbProductDto dto, CancellationToken ct) => Ok(await _service.SaveProductAsync(dto, ct));

        [HttpGet("products/{id:int}")]
        public async Task<IActionResult> GetBankAsync(int id, CancellationToken ct) => Ok(await _service.GetBankAsync(id, ct));

        [HttpPost("templates/import-mspdi")]
        [Authorize(Roles = PbRoles.Writers)]
        public async Task<IActionResult> ImportMspdiAsync([FromBody] PbImportMspdiDto dto, CancellationToken ct) => Ok(await _service.ImportMspdiAsync(dto, ct));

        [HttpGet("templates/{id:int}")]
        public async Task<IActionResult> GetTemplateAsync(int id, CancellationToken ct) => Ok(await _service.GetTemplateAsync(id, ct));

        /// <summary>First ENGINEERING, then a different PLANNING person; the importer approves neither.</summary>
        [HttpPost("templates/{id:int}/approve")]
        [Authorize(Roles = PbRoles.Engineering + "," + PbRoles.Planning)]
        public async Task<IActionResult> ApproveTemplateAsync(int id, CancellationToken ct) => Ok(await _service.ApproveTemplateAsync(id, ct));

        [HttpPost("seed-fy")]
        [Authorize(Roles = PbRoles.Admin)]
        public async Task<IActionResult> SeedAsync(CancellationToken ct) => Ok(await _seed.InstallFyAsync(ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/product-bank/projects")]
    public class PbProjectsController : ControllerBase
    {
        private readonly IPbProductService _service;
        public PbProjectsController(IPbProductService service) => _service = service;

        [HttpPost("instantiate")]
        [Authorize(Roles = PbRoles.Requesters)]
        public async Task<IActionResult> InstantiateAsync([FromBody] PbInstantiateDto dto, CancellationToken ct) => Ok(await _service.InstantiateAsync(dto, ct));

        [HttpPost("generate-wbs")]
        [Authorize(Roles = PbRoles.Requesters)]
        public async Task<IActionResult> GenerateWbsAsync([FromBody] PbGenerateWbsDto dto, CancellationToken ct) => Ok(await _service.GenerateWbsAsync(dto, ct));

        [HttpGet("work-order/{workOrderId:int}")]
        public async Task<IActionResult> GetBaselineAsync(int workOrderId, CancellationToken ct) => Ok(await _service.GetBaselineAsync(workOrderId, ct));

        [HttpPost("changes")]
        [Authorize(Roles = PbRoles.Requesters)]
        public async Task<IActionResult> SaveChangeAsync([FromBody] SavePbChangeDto dto, CancellationToken ct) => Ok(await _service.SaveChangeAsync(dto, ct));

        [HttpPost("changes/submit")]
        [Authorize(Roles = PbRoles.Requesters)]
        public async Task<IActionResult> SubmitChangeAsync([FromBody] PbChangeActionDto dto, CancellationToken ct) => Ok(await _service.SubmitChangeAsync(dto, ct));

        [HttpPost("changes/approve-engineering")]
        [Authorize(Roles = PbRoles.Engineering)]
        public async Task<IActionResult> ApproveEngineeringAsync([FromBody] PbChangeActionDto dto, CancellationToken ct) => Ok(await _service.ApproveChangeEngineeringAsync(dto, ct));

        [HttpPost("changes/approve-planning")]
        [Authorize(Roles = PbRoles.Planning)]
        public async Task<IActionResult> ApprovePlanningAsync([FromBody] PbChangeActionDto dto, CancellationToken ct) => Ok(await _service.ApproveChangePlanningAsync(dto, ct));

        [HttpPost("changes/reject")]
        [Authorize(Roles = PbRoles.Engineering + "," + PbRoles.Planning)]
        public async Task<IActionResult> RejectChangeAsync([FromBody] PbChangeActionDto dto, CancellationToken ct) => Ok(await _service.RejectChangeAsync(dto, ct));

        [HttpPost("changes/apply")]
        [Authorize(Roles = PbRoles.Engineering + "," + PbRoles.Planning)]
        public async Task<IActionResult> ApplyChangeAsync([FromBody] PbChangeActionDto dto, CancellationToken ct) => Ok(await _service.ApplyChangeAsync(dto, ct));

        [HttpPost("changes/cancel")]
        [Authorize(Roles = PbRoles.Requesters)]
        public async Task<IActionResult> CancelChangeAsync([FromBody] PbChangeActionDto dto, CancellationToken ct) => Ok(await _service.CancelChangeAsync(dto, ct));
    }
}
