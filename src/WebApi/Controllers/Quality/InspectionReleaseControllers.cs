using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.QualityDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // Package B (inspection and release integrity). Role checks per step (segregation of duties) are in the services; reads are open to the quality readers.

    /// <summary>Characteristic lists of the drawing revisions (the list the inspections pick from).</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality/characteristics")]
    [RolesCheckedInService]
    public class QualityCharacteristicController : ControllerBase
    {
        private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private readonly ICharacteristicService _service;

        public QualityCharacteristicController(ICharacteristicService service) => _service = service;

        [HttpGet("lists")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ListsAsync([FromQuery] CharListQuery query, CancellationToken ct) => Ok((await _service.GetListsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("lists/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ListAsync(int id, CancellationToken ct) => Ok(await _service.GetListAsync(id, ct));

        [HttpGet("for-revision/{revisionId:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ForRevisionAsync(int revisionId, CancellationToken ct) => Ok(await _service.ForRevisionAsync(revisionId, ct));

        [HttpGet("for-item/{itemId:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ForItemAsync(int itemId, CancellationToken ct) => Ok(await _service.ForItemAsync(itemId, ct));

        [HttpPost("lists")]
        public async Task<IActionResult> SaveAsync([FromBody] SaveCharListDto dto, CancellationToken ct) => Ok(await _service.SaveAsync(dto, ct));

        [HttpPost("lists/action")]
        public async Task<IActionResult> ActionAsync([FromBody] CharListActionDto dto, CancellationToken ct) => Ok(await _service.ActionAsync(dto, ct));

        [HttpGet("template")]
        [Authorize(Roles = QualityRoles.Planners)]
        public IActionResult Template()
        {
            var result = _service.Template();
            if (!result.Success || result.Data == null) return BadRequest(result);
            return File(result.Data, Xlsx, "characteristics-template.xlsx");
        }

        [HttpGet("lists/{id:int}/export")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ExportAsync(int id, CancellationToken ct)
        {
            var result = await _service.ExportAsync(id, ct);
            if (!result.Success || result.Data == null) return BadRequest(result);
            return File(result.Data, Xlsx, "characteristics-" + id + ".xlsx");
        }

        /// <summary>Preview (Preview=true) or one draft list (Preview=false). Never approved or released by the import.</summary>
        [HttpPost("import")]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> ImportAsync([FromForm] CharExcelRequestDto request, IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return Ok(Common.Results.ResponseDtoHelper.BadRequest<CharExcelResultDto>("فایل را انتخاب کنید."));
            using var ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms, ct);
            return Ok(await _service.ImportAsync(request, ms.ToArray(), ct));
        }
    }

    /// <summary>The «بدون نقشه» flag of a part.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality/no-drawing")]
    [RolesCheckedInService]
    public class QualityNoDrawingController : ControllerBase
    {
        private readonly INoDrawingService _service;

        public QualityNoDrawingController(INoDrawingService service) => _service = service;

        [HttpGet]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ListAsync([FromQuery] NoDrawingQuery query, CancellationToken ct) => Ok((await _service.GetAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("request")]
        public async Task<IActionResult> RequestAsync([FromBody] RequestNoDrawingDto dto, CancellationToken ct) => Ok(await _service.RequestAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] NoDrawingActionDto dto, CancellationToken ct) => Ok(await _service.ActionAsync(dto, ct));
    }

    /// <summary>First article inspection (FAI).</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality/first-articles")]
    [RolesCheckedInService]
    public class QualityFirstArticleController : ControllerBase
    {
        private readonly IFirstArticleService _service;

        public QualityFirstArticleController(IFirstArticleService service) => _service = service;

        [HttpGet]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ListAsync([FromQuery] FaiQuery query, CancellationToken ct) => Ok((await _service.GetAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> OneAsync(int id, CancellationToken ct) => Ok(await _service.GetByIdAsync(id, ct));

        [HttpGet("by-order/{productionOrderId:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ByOrderAsync(int productionOrderId, CancellationToken ct) => Ok(await _service.ForOrderAsync(productionOrderId, ct));

        [HttpPost("start")]
        public async Task<IActionResult> StartAsync([FromBody] StartFaiDto dto, CancellationToken ct) => Ok(await _service.StartAsync(dto, ct));

        [HttpPost("measure")]
        public async Task<IActionResult> MeasureAsync([FromBody] MeasureFaiDto dto, CancellationToken ct) => Ok(await _service.MeasureAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] FaiActionDto dto, CancellationToken ct) => Ok(await _service.ActionAsync(dto, ct));
    }

    /// <summary>The as-built record («پیکربندی ساخته‌شده») of released units.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality/as-built")]
    [RolesCheckedInService]
    public class QualityAsBuiltController : ControllerBase
    {
        private readonly IAsBuiltService _service;

        public QualityAsBuiltController(IAsBuiltService service) => _service = service;

        [HttpGet]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ListAsync([FromQuery] AsBuiltQuery query, CancellationToken ct) => Ok((await _service.GetAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> OneAsync(int id, CancellationToken ct) => Ok(await _service.GetByIdAsync(id, ct));

        [HttpGet("by-serial")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> BySerialAsync([FromQuery] string serial, [FromQuery] int? itemId, CancellationToken ct) => Ok(await _service.GetBySerialAsync(serial, itemId, ct));

        [HttpPost("reissue")]
        public async Task<IActionResult> ReissueAsync([FromBody] ReissueAsBuiltDto dto, CancellationToken ct) => Ok(await _service.ReissueAsync(dto, ct));

        [HttpPost("reissue/decide")]
        public async Task<IActionResult> DecideReissueAsync([FromBody] DecideAsBuiltDto dto, CancellationToken ct) => Ok(await _service.DecideReissueAsync(dto, ct));
    }
}
