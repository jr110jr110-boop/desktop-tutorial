using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Finance;

namespace WebApi.Controllers
{
    /// <summary>R1t D20: the check of the hash chain of the audit trail. It shares the route of the audit list (<c>FinanceAuditController</c>) and is read-only.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/finance/audit")]
    public class FinanceAuditChainController : ControllerBase
    {
        private readonly IAuditChainService _chain;
        public FinanceAuditChainController(IAuditChainService chain) { _chain = chain; }

        /// <summary>
        /// Walks the chain of the audit trail and reports the first break (AUDITOR, FINANCEMANAGER). <c>from</c> and <c>to</c> are places in the chain (default: the whole chain);
        /// <c>anchorSeq</c> and <c>anchorHash</c> are a head that was written down earlier and must still be in the chain.
        /// </summary>
        [HttpGet("verify")]
        [Authorize(Roles = AuditChainService.Roles)]
        public async Task<IActionResult> VerifyAsync([FromQuery] long? from, [FromQuery] long? to, [FromQuery] long? anchorSeq, [FromQuery] string anchorHash, CancellationToken ct) =>
            Ok(await _chain.VerifyAsync(from, to, anchorSeq, anchorHash, ct));
    }
}
