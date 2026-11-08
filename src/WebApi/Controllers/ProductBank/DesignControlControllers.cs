using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.ProductBankDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // Package A1 (design and development control, ISO 9001 8.3). Every signed-in user can read; the role and the segregation of duties of each step are checked in the services (an AI seat never approves, closes or judges).

    [ApiController]
    [Authorize]
    [Route("api/v1/product-bank/design/plans")]
    [RolesCheckedInService]
    public class DesignPlanController : ControllerBase
    {
        private readonly IDesignPlanService _service;
        public DesignPlanController(IDesignPlanService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> ListAsync([FromQuery] DesignPlanQuery query, CancellationToken ct) => Ok((await _service.GetListAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _service.GetAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> SaveAsync([FromBody] SaveDesignPlanDto dto, CancellationToken ct) => Ok(await _service.SaveAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] DesignPlanActionDto dto, CancellationToken ct) => Ok(await _service.ActionAsync(dto, ct));

        [HttpPost("stage-action")]
        public async Task<IActionResult> StageActionAsync([FromBody] DesignStageActionDto dto, CancellationToken ct) => Ok(await _service.StageActionAsync(dto, ct));

        [HttpGet("dossier/{productId:int}")]
        public async Task<IActionResult> DossierAsync(int productId, CancellationToken ct) => Ok(await _service.DossierAsync(productId, ct));

        /// <summary>kind = bom | drawing; id = BOM header id or drawing revision id.</summary>
        [HttpGet("gate/{kind}/{id:int}")]
        public async Task<IActionResult> GateAsync(string kind, int id, CancellationToken ct) => Ok(await _service.GateStatusAsync(kind, id, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/product-bank/design/inputs")]
    [RolesCheckedInService]
    public class DesignInputController : ControllerBase
    {
        private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private readonly IDesignInputService _service;
        public DesignInputController(IDesignInputService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> ListAsync([FromQuery] DesignInputQuery query, CancellationToken ct) => Ok(await _service.GetListAsync(query, ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _service.GetAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> SaveAsync([FromBody] SaveDesignInputDto dto, CancellationToken ct) => Ok(await _service.SaveAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] DesignInputActionDto dto, CancellationToken ct) => Ok(await _service.ActionAsync(dto, ct));

        [HttpPost("copy")]
        public async Task<IActionResult> CopyAsync([FromQuery] int productId, [FromQuery] int fromRevision, [FromQuery] int toRevision, CancellationToken ct) => Ok(await _service.CopyFromRevisionAsync(productId, fromRevision, toRevision, ct));

        [HttpGet("template")]
        public IActionResult Template()
        {
            var result = _service.Template();
            if (!result.Success || result.Data == null) return BadRequest(result);
            return File(result.Data, Xlsx, "design-inputs-template.xlsx");
        }

        [HttpGet("export")]
        public async Task<IActionResult> ExportAsync([FromQuery] int productId, [FromQuery] int revision, CancellationToken ct)
        {
            var result = await _service.ExportAsync(productId, revision, ct);
            if (!result.Success || result.Data == null) return BadRequest(result);
            return File(result.Data, Xlsx, "design-inputs-" + productId + "-" + revision + ".xlsx");
        }

        [HttpPost("import")]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> ImportAsync([FromForm] DesignInputExcelRequestDto request, IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return Ok(Common.Results.ResponseDtoHelper.BadRequest<DesignInputExcelResultDto>("فایل را انتخاب کنید."));
            using var ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms, ct);
            return Ok(await _service.ImportAsync(request, ms.ToArray(), ct));
        }
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/product-bank/design/reviews")]
    [RolesCheckedInService]
    public class DesignReviewController : ControllerBase
    {
        private readonly IDesignReviewService _service;
        public DesignReviewController(IDesignReviewService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> ListAsync([FromQuery] DesignReviewQuery query, CancellationToken ct) => Ok((await _service.GetListAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAsync(int id, CancellationToken ct) => Ok(await _service.GetAsync(id, ct));

        [HttpGet("{id:int}/matrix")]
        public async Task<IActionResult> MatrixAsync(int id, CancellationToken ct) => Ok(await _service.MatrixAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> SaveAsync([FromBody] SaveDesignReviewDto dto, CancellationToken ct) => Ok(await _service.SaveAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] DesignReviewActionDto dto, CancellationToken ct) => Ok(await _service.ActionAsync(dto, ct));

        [HttpPost("validation")]
        public async Task<IActionResult> ValidationAsync([FromBody] SaveDesignValidationDto dto, CancellationToken ct) => Ok(await _service.SaveValidationAsync(dto, ct));

        [HttpPost("findings")]
        public async Task<IActionResult> FindingAsync([FromBody] SaveDesignFindingDto dto, CancellationToken ct) => Ok(await _service.SaveFindingAsync(dto, ct));

        [HttpPost("findings/action")]
        public async Task<IActionResult> FindingActionAsync([FromBody] DesignFindingActionDto dto, CancellationToken ct) => Ok(await _service.FindingActionAsync(dto, ct));

        [HttpPost("traces")]
        public async Task<IActionResult> TraceAsync([FromBody] SaveDesignTraceDto dto, CancellationToken ct) => Ok(await _service.SaveTraceAsync(dto, ct));
    }
}
