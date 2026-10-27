using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Filters;
using Services.NasbNiro.Ceo.Drills;
using ViewModels.NasbNiro.CeoDtos;

namespace WebApi.Controllers
{
    // Release D1 part A2: the details behind the numbers of the CEO dashboard and the analysis with verified numbers.
    // Rule violations (also a role that may not read) answer 200 with success=false and a Persian message, like the other modules; the service decides.

    /// <summary>Drill catalogue, one drill, and the narrative analysis of a section. CEO and PMO.</summary>
    [ApiController]
    [Authorize]
    [RolesCheckedInService] // CEO and PMO: the services check the reader roles on every call (D1); the snapshot and the analysis are writes only in the sense of a cache row and an audited conversation
    [Route("api/v1/ceo")]
    public class CeoDrillController : ControllerBase
    {
        private static readonly string[] Reserved = { "key", "top", "page" };

        private readonly ICeoDrillService _drills;
        private readonly ICeoAnalysisService _analysis;

        public CeoDrillController(ICeoDrillService drills, ICeoAnalysisService analysis)
        {
            _drills = drills;
            _analysis = analysis;
        }

        /// <summary>The catalogue: key, Persian title and description, module and typed parameters of every drill.</summary>
        [HttpGet("drills")]
        public async Task<IActionResult> DrillsAsync(CancellationToken ct) => Ok(await _drills.GetCatalogueAsync(ct));

        /// <summary>
        /// One drill: ?key=qc.ncr_open&amp;top=50&amp;page=1 and the parameters of the drill (for example from, to, days). top is the page size (default 50, at most 200).
        /// </summary>
        [HttpGet("drill")]
        public async Task<IActionResult> DrillAsync([FromQuery] string key, [FromQuery] string top, [FromQuery] string page, CancellationToken ct)
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> q in Request.Query)
                if (!Reserved.Contains(q.Key, StringComparer.OrdinalIgnoreCase)) parameters[q.Key] = q.Value.ToString();
            return Ok(await _drills.DrillAsync(key, top, page, parameters, ct));
        }

        /// <summary>
        /// The analysis of a section (sectionKey finance, sales, projects, production, quality, inventory, procurement, service or office; empty = the company) by the assistant:
        /// the numbers of the section are given to the model, every number of the answer is checked against them.
        /// </summary>
        [HttpPost("analyze")]
        public async Task<IActionResult> AnalyzeAsync([FromBody] CeoAnalyzeDto dto, CancellationToken ct) => Ok(await _analysis.AnalyzeAsync(dto, ct));
    }
}
