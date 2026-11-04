using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Trace;
using ViewModels.NasbNiro.TraceDtos;

namespace WebApi.Controllers
{
    // PB4: the one trace page. Readers are the people of the technical chain (purchase, stock, shop floor, quality) and the audit readers; prices are masked inside the service for everyone outside TraceRoles.Prices.
    [ApiController]
    [Authorize(Roles = TraceRoles.Readers)]
    [Route("api/v1/trace")]
    public class TraceCenterController : ControllerBase
    {
        private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private readonly ITraceCenterService _service;
        private readonly IGenealogyService _genealogy;

        public TraceCenterController(ITraceCenterService service, IGenealogyService genealogy)
        {
            _service = service;
            _genealogy = genealogy;
        }

        /// <summary>Product tree: product code, part number, serial, lot or heat number (or a label code).</summary>
        [HttpGet("tree")]
        public async Task<IActionResult> TreeAsync([FromQuery] TraceQuery query, CancellationToken ct) => Ok(await _service.TreeAsync(query, ct));

        [HttpGet("tree/export")]
        public async Task<IActionResult> ExportTreeAsync([FromQuery] TraceQuery query, CancellationToken ct)
        {
            var result = await _service.ExportTreeAsync(query, ct);
            if (!result.Success) return BadRequest(result);
            return File(result.Data, Xlsx, "product-tree.xlsx");
        }

        /// <summary>Where used (recall): heat or lot number, supplier, purchase order, purchase request, material request or serial.</summary>
        [HttpGet("reverse")]
        public async Task<IActionResult> ReverseAsync([FromQuery] ReverseTraceQuery query, CancellationToken ct) => Ok(await _service.ReverseAsync(query, ct));

        [HttpGet("reverse/export")]
        public async Task<IActionResult> ExportReverseAsync([FromQuery] ReverseTraceQuery query, CancellationToken ct)
        {
            var result = await _service.ExportReverseAsync(query, ct);
            if (!result.Success) return BadRequest(result);
            return File(result.Data, Xlsx, "reverse-trace.xlsx");
        }

        [HttpGet("genealogy/order/{orderId:int}")]
        public async Task<IActionResult> GenealogyAsync(int orderId, CancellationToken ct) => Ok(await _genealogy.ForOrderAsync(orderId, ct));

        /// <summary>Rebuilds the links of one order from its confirmed issues and receipt (idempotent).</summary>
        [HttpPost("genealogy/order/{orderId:int}/rebuild")]
        [Authorize(Roles = TraceRoles.Admin)]
        public async Task<IActionResult> RebuildAsync(int orderId, CancellationToken ct) => Ok(await _genealogy.RebuildOrderAsync(orderId, ct));

        /// <summary>Once after the upgrade: links for every order that already has a production receipt. Reports what could not be linked.</summary>
        [HttpPost("genealogy/backfill")]
        [Authorize(Roles = TraceRoles.Admin)]
        public async Task<IActionResult> BackfillAsync(CancellationToken ct) => Ok(await _genealogy.BackfillAsync(ct));
    }
}
