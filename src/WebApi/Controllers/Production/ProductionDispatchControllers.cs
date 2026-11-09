using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Production.Dispatch;
using Services.NasbNiro.Remote;
using ViewModels.NasbNiro.ProductionDtos;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // Production release P2 part D: the production AI seat's plan and the shop-floor overview (read-only), and the remote-access status of the caller.

    [ApiController]
    [Authorize]
    [Route("api/v1/production/dispatch")]
    [RolesCheckedInService]
    public class ProductionDispatchController : ControllerBase
    {
        private readonly IMfDispatchService _dispatch;

        public ProductionDispatchController(IMfDispatchService dispatch)
        {
            _dispatch = dispatch;
        }

        /// <summary>The ranked queue of every work centre, the load and the proposals of the planning seat.</summary>
        [HttpGet("plan")]
        public async Task<IActionResult> PlanAsync([FromQuery] AiDispatchQuery query, CancellationToken ct) => Ok(await _dispatch.GetPlanAsync(query, ct));

        /// <summary>The shop-floor overview (the home page of production).</summary>
        [HttpGet("floor")]
        public async Task<IActionResult> FloorAsync(CancellationToken ct) => Ok(await _dispatch.GetFloorAsync(HttpContext.Items.ContainsKey(RemoteAccessPolicy.ItemKey), ct));
    }

    /// <summary>Is the caller working from outside the company (VPN), and is it read-only? Every signed-in user may ask about himself.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/remote")]
    public class RemoteAccessController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public RemoteAccessController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet("status")]
        public IActionResult Status()
        {
            RemoteAccessPolicy policy = RemoteAccessPolicy.From(_configuration);
            bool remote = HttpContext.Items.ContainsKey(RemoteAccessPolicy.ItemKey);
            string[] roles = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray();
            bool allowed = !remote || roles.Any(r => policy.Roles.Contains(r.ToUpperInvariant()));
            var dto = new RemoteStatusDto
            {
                Enabled = policy.Enabled, Remote = remote, ReadOnly = remote && policy.ReadOnly, Allowed = allowed,
                Text = remote ? (policy.ReadOnly ? "اتصال از بیرون شرکت (VPN): فقط مشاهده" : "اتصال از بیرون شرکت (VPN)") : null,
            };
            return Ok(Common.Results.ResponseDtoHelper.Ok(dto));
        }
    }
}
