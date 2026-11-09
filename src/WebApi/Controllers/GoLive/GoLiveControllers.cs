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
        public GoLiveController(IGoLiveOverviewService overview, IPoCitationService po) { _overview = overview; _po = po; }

        [HttpGet("overview")]
        public async Task<IActionResult> OverviewAsync(CancellationToken ct) => Ok(await _overview.OverviewAsync(ct));

        [HttpGet("edms-revision-precheck")]
        public async Task<IActionResult> RevisionPreCheckAsync(CancellationToken ct) => Ok(await _overview.RevisionPreCheckAsync(ct));

        [HttpGet("ceo-inbox")]
        public async Task<IActionResult> CeoInboxAsync(CancellationToken ct) => Ok(await _overview.CeoInboxAsync(ct));

        [HttpGet("po-stale-lines")]
        public async Task<IActionResult> StaleLinesAsync(CancellationToken ct) => Ok(await _po.StaleLinesAsync(ct));

        [HttpPost("po-confirm")]
        public async Task<IActionResult> ConfirmAsync([FromBody] GlPoConfirmDto dto, CancellationToken ct) => Ok(await _po.ConfirmAsync(dto, ct));

        [HttpPost("po-sync")]
        public async Task<IActionResult> SyncAsync(CancellationToken ct) => Ok(await _po.SyncAsync(ct));

        /// <summary>Stamps a PDF with the header and footer images rendered by the browser (raw RGBA, row by row). The answer is the stamped PDF; an unsupported structure answers 422 with a Persian message (the page falls back to the HTML stamp).</summary>
        [HttpPost("stamp-pdf")]
        [RequestSizeLimit(60_000_000)]
        public async Task<IActionResult> StampPdfAsync([FromForm] IFormFile file, [FromForm] IFormFile headerRgba, [FromForm] IFormFile footerRgba,
            [FromForm] int headerWidth, [FromForm] int headerHeight, [FromForm] int footerWidth, [FromForm] int footerHeight, CancellationToken ct)
        {
            if (file == null || headerRgba == null || footerRgba == null) return BadRequest(new { success = false, message = GlText.InvalidData });
            byte[] pdf = await Read(file, ct), h = await Read(headerRgba, ct), f = await Read(footerRgba, ct);
            try
            {
                byte[] stamped = PdfStamper.Stamp(pdf, new PdfStampImage { Width = headerWidth, Height = headerHeight, Rgba = h }, new PdfStampImage { Width = footerWidth, Height = footerHeight, Rgba = f });
                return File(stamped, "application/pdf", "controlled-copy.pdf");
            }
            catch (PdfStampException ex) { return StatusCode(422, new { success = false, message = ex.Message }); }
        }

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
        public async Task<IActionResult> CandidatesAsync(CancellationToken ct) => Ok(await _service.CandidatesAsync(ct));

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
        public async Task<IActionResult> CandidatesAsync(CancellationToken ct) => Ok(await _service.CandidatesAsync(ct));

        [HttpGet]
        public async Task<IActionResult> ListAsync(CancellationToken ct) => Ok(await _service.GetListAsync(ct));

        [HttpPost]
        public async Task<IActionResult> RequestAsync([FromBody] GlSaveConfigAdoptionDto dto, CancellationToken ct) => Ok(await _service.RequestAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] GlAdoptionActionDto dto, CancellationToken ct) => Ok(await _service.ActionAsync(dto, ct));
    }
}
