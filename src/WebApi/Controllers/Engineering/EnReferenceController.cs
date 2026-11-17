using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // ENG-E1: the reference document list. Role checks per action are in the services; rule violations answer 200 with success=false.

    [ApiController]
    [Authorize]
    [Route("api/v1/engineering/reference")]
    [RolesCheckedInService]
    public class EnReferenceController : ControllerBase
    {
        private readonly IEnReferenceService _service;

        public EnReferenceController(IEnReferenceService service)
        {
            _service = service;
        }

        [HttpGet("lists")]
        public async Task<IActionResult> ListsAsync([FromQuery] EnReferenceQuery query, CancellationToken ct) => Ok((await _service.GetListsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("approved-codes")]
        public async Task<IActionResult> ApprovedCodesAsync(CancellationToken ct) => Ok(await _service.GetApprovedCodesAsync(ct));

        [HttpGet("lists/{id:int}")]
        public async Task<IActionResult> ListAsync(int id, CancellationToken ct) => Ok(await _service.GetListAsync(id, ct));

        [HttpGet("current")]
        public async Task<IActionResult> CurrentAsync([FromQuery] string code, CancellationToken ct) => Ok(await _service.GetCurrentAsync(code, ct));

        [HttpPost("lists")]
        public async Task<IActionResult> SaveAsync([FromBody] EnSaveReferenceDto dto, CancellationToken ct) => Ok(await _service.SaveListAsync(dto, ct));

        /// <summary>Replaces the lines of a draft list with a CSV file (multipart: file, rowVersion).</summary>
        [HttpPost("lists/{id:int}/upload-lines")]
        [RequestSizeLimit(1_048_576)]
        public async Task<IActionResult> UploadLinesAsync(int id, [FromForm] string rowVersion, IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return Ok(Common.Results.ResponseDtoHelper.BadRequest<EnReferenceListDto>("فایل را انتخاب کنید."));
            if (file.Length > 512 * 1024) return Ok(Common.Results.ResponseDtoHelper.BadRequest<EnReferenceListDto>("فایل بیش از ۵۰۰ کیلوبایت است."));
            using System.IO.MemoryStream ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms, ct);
            return Ok(await _service.UploadLinesAsync(id, rowVersion, ms.ToArray(), ct));
        }

        /// <summary>A new draft list from a CSV file (multipart: code, title, standardSource, note, file).</summary>
        [HttpPost("lists/upload-new")]
        [RequestSizeLimit(1_048_576)]
        public async Task<IActionResult> CreateFromFileAsync([FromForm] string code, [FromForm] string title, [FromForm] string standardSource, [FromForm] string note, IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return Ok(Common.Results.ResponseDtoHelper.BadRequest<EnReferenceListDto>("فایل را انتخاب کنید."));
            if (file.Length > 512 * 1024) return Ok(Common.Results.ResponseDtoHelper.BadRequest<EnReferenceListDto>("فایل بیش از ۵۰۰ کیلوبایت است."));
            using System.IO.MemoryStream ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms, ct);
            return Ok(await _service.CreateFromFileAsync(new EnSaveReferenceDto { Code = code, Title = title, StandardSource = standardSource, Note = note }, ms.ToArray(), ct));
        }

        [HttpPost("lists/action")]
        public async Task<IActionResult> ActionAsync([FromBody] EnReferenceActionDto dto, CancellationToken ct) => Ok(await _service.ListActionAsync(dto, ct));
    }
}
