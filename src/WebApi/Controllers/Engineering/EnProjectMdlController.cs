using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // ENG-E1g: the MDL of a project in the tender (employer rule 20) and the CEO permit for a tender without it. Role checks per action are in the service; rule violations answer 200 with success=false.

    [ApiController]
    [Authorize]
    [Route("api/v1/engineering/project-mdl")]
    [RolesCheckedInService]
    public class EnProjectMdlController : ControllerBase
    {
        private readonly IEnProjectMdlService _service;

        public EnProjectMdlController(IEnProjectMdlService service)
        {
            _service = service;
        }

        /// <summary>The live project MDL of a case with its history, the permit and the flags.</summary>
        [HttpGet("by-case/{caseId:int}")]
        public async Task<IActionResult> ByCaseAsync(int caseId, CancellationToken ct) => Ok(await _service.GetByCaseAsync(caseId, ct));

        /// <summary>The CSV of the current not-removed lines (the format of the reference upload).</summary>
        [HttpGet("{id:int}/csv")]
        public async Task<IActionResult> CsvAsync(int id, CancellationToken ct)
        {
            Common.Results.ResponseDto<EnProjectMdlCsvDto> r = await _service.GetCsvAsync(id, ct);
            if (!r.Success) return Ok(r);
            return File(r.Data.Content, "text/csv; charset=utf-8", r.Data.FileName);
        }

        private async Task<IActionResult> RunAsync(string action, EnProjectMdlActionDto dto, CancellationToken ct)
        {
            dto ??= new EnProjectMdlActionDto();
            dto.Action = action;
            return Ok(await _service.ActionAsync(dto, ct));
        }

        /// <summary>The engineering agent (or an engineer) drafts the project MDL of a case: body {caseId}.</summary>
        [HttpPost("draft")]
        public Task<IActionResult> DraftAsync([FromBody] EnProjectMdlActionDto dto, CancellationToken ct) => RunAsync("draft", dto, ct);

        /// <summary>Replaces the lines from a CSV file (multipart: file, note of at least 15 characters, rowVersion).</summary>
        [HttpPost("{id:int}/upload-lines")]
        [RequestSizeLimit(1_048_576)]
        public async Task<IActionResult> UploadLinesAsync(int id, [FromForm] string rowVersion, [FromForm] string note, IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return Ok(Common.Results.ResponseDtoHelper.BadRequest<EnProjectMdlCaseDto>("فایل را انتخاب کنید."));
            string extension = System.IO.Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
            if (extension != ".csv" && extension != ".txt") return Ok(Common.Results.ResponseDtoHelper.BadRequest<EnProjectMdlCaseDto>("فقط فایل CSV پذیرفته می‌شود (در اکسل: Save As ← CSV UTF-8)؛ فایل «" + (file.FileName ?? string.Empty) + "» از نوع دیگری است."));
            if (file.Length > EnReferenceCsv.MaxBytes) return Ok(Common.Results.ResponseDtoHelper.BadRequest<EnProjectMdlCaseDto>("فایل بیش از ۵۰۰ کیلوبایت است."));
            using System.IO.MemoryStream ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms, ct);
            return Ok(await _service.UploadLinesAsync(id, rowVersion, note, ms.ToArray(), file.FileName, ct));
        }

        [HttpPost("{id:int}/check")]
        public Task<IActionResult> CheckAsync(int id, [FromBody] EnProjectMdlActionDto dto, CancellationToken ct) { dto ??= new EnProjectMdlActionDto(); dto.Id = id; return RunAsync("check", dto, ct); }

        [HttpPost("{id:int}/return")]
        public Task<IActionResult> ReturnAsync(int id, [FromBody] EnProjectMdlActionDto dto, CancellationToken ct) { dto ??= new EnProjectMdlActionDto(); dto.Id = id; return RunAsync("return", dto, ct); }

        [HttpPost("{id:int}/approve")]
        public Task<IActionResult> ApproveAsync(int id, [FromBody] EnProjectMdlActionDto dto, CancellationToken ct) { dto ??= new EnProjectMdlActionDto(); dto.Id = id; return RunAsync("approve", dto, ct); }

        [HttpPost("{id:int}/void")]
        public Task<IActionResult> VoidAsync(int id, [FromBody] EnProjectMdlActionDto dto, CancellationToken ct) { dto ??= new EnProjectMdlActionDto(); dto.Id = id; return RunAsync("void", dto, ct); }

        /// <summary>Commercial records the client's approval: body {letterRef, documentId, note (on a correction)}.</summary>
        [HttpPost("{id:int}/record-client-approval")]
        public Task<IActionResult> RecordClientApprovalAsync(int id, [FromBody] EnProjectMdlActionDto dto, CancellationToken ct) { dto ??= new EnProjectMdlActionDto(); dto.Id = id; return RunAsync("record-client-approval", dto, ct); }

        /// <summary>The engineering manager asks for a tender without an MDL: body {caseId, reason of at least 50 characters}.</summary>
        [HttpPost("request-permit")]
        public Task<IActionResult> RequestPermitAsync([FromBody] EnProjectMdlActionDto dto, CancellationToken ct) => RunAsync("request-permit", dto, ct);

        /// <summary>The CEO decides: body {permitId, approve, note (required for a rejection)}.</summary>
        [HttpPost("decide-permit")]
        public Task<IActionResult> DecidePermitAsync([FromBody] EnProjectMdlActionDto dto, CancellationToken ct) => RunAsync("decide-permit", dto, ct);

        /// <summary>The requester or the manager withdraws a requested permit: body {permitId, note}.</summary>
        [HttpPost("withdraw-permit")]
        public Task<IActionResult> WithdrawPermitAsync([FromBody] EnProjectMdlActionDto dto, CancellationToken ct) => RunAsync("withdraw-permit", dto, ct);
    }
}
