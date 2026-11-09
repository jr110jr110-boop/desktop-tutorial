using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.GoLive;
using ViewModels.NasbNiro.GoLiveDtos;
using WebApi.Filters;

namespace WebApi.Controllers.GoLive
{
    // Package G (go-live cut-over). Every signed-in user can read; the role and the segregation of duties of each step are checked in the services (an AI seat never approves).

    [ApiController]
    [Authorize]
    [Route("api/v1/go-live")]
    [RolesCheckedInService]
    public class GoLiveController : ControllerBase
    {
        private readonly IGoLiveOverviewService _overview;
        private readonly IPoCitationService _po;
        private readonly IGlPrintService _print;
        public GoLiveController(IGoLiveOverviewService overview, IPoCitationService po, IGlPrintService print) { _overview = overview; _po = po; _print = print; }

        [HttpGet("overview")]
        public async Task<IActionResult> OverviewAsync(CancellationToken ct) => Ok(await _overview.OverviewAsync(ct));

        [HttpGet("edms-revision-precheck")]
        public async Task<IActionResult> RevisionPreCheckAsync(CancellationToken ct) => Ok(await _overview.RevisionPreCheckAsync(ct));

        [HttpGet("ceo-inbox")]
        public async Task<IActionResult> CeoInboxAsync(CancellationToken ct) => Ok(await _overview.CeoInboxAsync(ct));

        [HttpGet("po-stale-lines")]
        public async Task<IActionResult> StaleLinesAsync(CancellationToken ct) => Ok(await _po.StaleLinesAsync(ct));

        [HttpGet("po-uncited-lines")]
        public async Task<IActionResult> UncitedLinesAsync([FromQuery] GlCandidateQuery q, CancellationToken ct) => Ok(await _po.UncitedLinesAsync(q, ct));

        [HttpPost("po-confirm")]
        public async Task<IActionResult> ConfirmAsync([FromBody] GlPoConfirmDto dto, CancellationToken ct) => Ok(await _po.ConfirmAsync(dto, ct));

        [HttpPost("po-sync")]
        public async Task<IActionResult> SyncAsync(CancellationToken ct) => Ok(await _po.SyncAsync(ct));

        /// <summary>Step 1 of a printed copy: the server checks the revision, numbers the copy, records the print and returns the stamp text; the browser draws the text in the programme's font.</summary>
        [HttpPost("print/prepare")]
        public async Task<IActionResult> PrintPrepareAsync([FromBody] GlPrintPrepareDto dto, CancellationToken ct) => Ok(await _print.PrepareAsync(dto, ct));

        /// <summary>Step 2: the server loads the file of the revision from the store itself and adds the two bands. No PDF is accepted from the user. 422 = the structure cannot be stamped / too big (fall back to the HTML print); 429 = one stamp at a time.</summary>
        [HttpPost("print/stamp")]
        [RequestSizeLimit(8_000_000)]
        public async Task<IActionResult> PrintStampAsync([FromForm] int printId, [FromForm] IFormFile headerRgba, [FromForm] IFormFile footerRgba,
            [FromForm] int headerWidth, [FromForm] int headerHeight, [FromForm] int footerWidth, [FromForm] int footerHeight, CancellationToken ct)
        {
            if (headerRgba == null || footerRgba == null) return BadRequest(new { success = false, message = GlText.InvalidData });
            byte[] h = await Read(headerRgba, ct), f = await Read(footerRgba, ct);
            GlStampOutcome o = await _print.StampAsync(printId, new PdfStampImage { Width = headerWidth, Height = headerHeight, Rgba = h }, new PdfStampImage { Width = footerWidth, Height = footerHeight, Rgba = f }, ct);
            if (o.Pdf != null) return File(o.Pdf, "application/pdf", o.FileName);
            if (o.Status == 400) return Ok(new { success = false, message = o.Error });
            return StatusCode(o.Status, new { success = false, message = o.Error });
        }

        /// <summary>The dry run of the record-integrity baseline (nothing is written): how many records of the newly covered types differ from their trail now. For the rehearsal on a copy of the server database.</summary>
        [HttpGet("drift-rehearsal")]
        public async Task<IActionResult> DriftRehearsalAsync(CancellationToken ct) => Ok(await _overview.DriftRehearsalAsync(ct));

        private static async Task<byte[]> Read(IFormFile file, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/go-live/char-adoptions")]
    [RolesCheckedInService]
    public class GlCharAdoptionController : ControllerBase
    {
        private readonly IGlCharAdoptionService _service;
        public GlCharAdoptionController(IGlCharAdoptionService service) => _service = service;

        [HttpGet("candidates")]
        public async Task<IActionResult> CandidatesAsync([FromQuery] GlCandidateQuery q, CancellationToken ct) => Ok(await _service.CandidatesAsync(q, ct));

        [HttpGet]
        public async Task<IActionResult> ListAsync(CancellationToken ct) => Ok(await _service.GetListAsync(ct));

        [HttpPost]
        public async Task<IActionResult> RequestAsync([FromBody] GlSaveCharAdoptionDto dto, CancellationToken ct) => Ok(await _service.RequestAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] GlAdoptionActionDto dto, CancellationToken ct) => Ok(await _service.ActionAsync(dto, ct));
    }

    [ApiController]
    [Authorize]
    [Route("api/v1/go-live/config-adoptions")]
    [RolesCheckedInService]
    public class GlConfigAdoptionController : ControllerBase
    {
        private readonly IGlConfigAdoptionService _service;
        public GlConfigAdoptionController(IGlConfigAdoptionService service) => _service = service;

        [HttpGet("candidates")]
        public async Task<IActionResult> CandidatesAsync([FromQuery] GlCandidateQuery q, CancellationToken ct) => Ok(await _service.CandidatesAsync(q, ct));

        [HttpGet]
        public async Task<IActionResult> ListAsync(CancellationToken ct) => Ok(await _service.GetListAsync(ct));

        [HttpPost]
        public async Task<IActionResult> RequestAsync([FromBody] GlSaveConfigAdoptionDto dto, CancellationToken ct) => Ok(await _service.RequestAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] GlAdoptionActionDto dto, CancellationToken ct) => Ok(await _service.ActionAsync(dto, ct));
    }
}
