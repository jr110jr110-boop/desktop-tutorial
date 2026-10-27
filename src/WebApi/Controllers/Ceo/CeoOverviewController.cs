using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Filters;
using Services.NasbNiro.Ceo;

namespace WebApi.Controllers
{
    // Release D1 part A1: the executive dashboard of the CEO. A reader who may not open it (any role but CEO and PMO) gets 200 with success=false and a Persian message,
    // like the other modules; the service decides.

    /// <summary>The overview (headline, nine sections, alerts, pulse), one fresh section, the snapshot of the day and the spoken summaries (voice mode). CEO and PMO.</summary>
    [ApiController]
    [Authorize]
    [RolesCheckedInService] // CEO and PMO: the services check the reader roles on every call (D1); the snapshot and the analysis are writes only in the sense of a cache row and an audited conversation
    [Route("api/v1/ceo")]
    public class CeoOverviewController : ControllerBase
    {
        private readonly ICeoOverviewService _overview;
        private readonly ICeoSnapshotService _snapshots;
        private readonly ICeoSpeechService _speech;

        public CeoOverviewController(ICeoOverviewService overview, ICeoSnapshotService snapshots, ICeoSpeechService speech)
        {
            _overview = overview;
            _snapshots = snapshots;
            _speech = speech;
        }

        /// <summary>
        /// The whole screen: eight headline figures, nine sections with their charts, the alerts of the company and one pulse light per section.
        /// The answer is kept for 120 seconds (10 seconds when a section could not be computed; one copy per kind of reader); refresh=true computes it again. "cached" says which one you got.
        /// </summary>
        [HttpGet("overview")]
        public async Task<IActionResult> OverviewAsync([FromQuery] bool refresh, CancellationToken ct) => Ok(await _overview.GetOverviewAsync(refresh, ct));

        /// <summary>
        /// One section (finance, sales, projects, production, quality, inventory, procurement, service or office). It is the section of the cached overview when there is one
        /// (the numbers the screen shows), and is computed now otherwise; refresh=true always computes it now. A section is never cached on its own.
        /// </summary>
        [HttpGet("section/{key}")]
        public async Task<IActionResult> SectionAsync(string key, [FromQuery] bool refresh, CancellationToken ct) => Ok(await _overview.GetSectionAsync(key, refresh, ct));

        /// <summary>Stores today's figures for the trends now (the job stores the day that ended every night at 00:30); running it again on the same day replaces the values.</summary>
        [HttpPost("snapshot")]
        public async Task<IActionResult> SnapshotAsync(CancellationToken ct) => Ok(await _snapshots.TakeNowAsync(ct));

        /// <summary>
        /// A spoken summary for the voice mode: what = headline (the eight figures and the sections to look at), section (one section; sectionKey is finance ... office), alerts,
        /// or brief (the latest daily report of the CEO; the CEO only). The text of the first three is a fixed sentence list built from the same figures as the overview, with the numbers in words
        /// (rial values and changes are rounded to three significant digits; it works while the language model is down); the brief is read as it was written, numbers in words.
        /// audioBase64 is the WAV audio from the text-to-speech endpoint, or null with degraded = true when no such endpoint answers in time (Ceo:SpeechTimeoutSeconds, default 20).
        /// </summary>
        [HttpGet("speech")]
        public async Task<IActionResult> SpeechAsync([FromQuery] string what, [FromQuery] string sectionKey, CancellationToken ct) => Ok(await _speech.GetAsync(what, sectionKey, ct));
    }
}
