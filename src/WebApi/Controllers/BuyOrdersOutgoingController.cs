using System.Net;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   bulk temporary / final receipt: PMO, COMMERCIAL (no caller in the web app; the bulk update is keyed by commodity code and count only)
    [Authorize]
    public class BuyOrdersOutgoingController : BaseApiController
    {
        private readonly IBuyOrderOutgoingService _buyOrderOutgoingService;

        public BuyOrdersOutgoingController(IBuyOrderOutgoingService buyOrderOutgoingService)
        {
            _buyOrderOutgoingService = buyOrderOutgoingService;
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpPut("apply-temp-receipt")]
        [ProducesResponseType(typeof(ResponseDto<int>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<int>>> UpdateBuyOrderApplyTempReceiptAsync([FromBody] ApplyTempReceiptDto dto)
        {
            return Ok(await _buyOrderOutgoingService.ApplyTempReceiptAsync(dto));
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpPut("apply-final-receipt")]
        [ProducesResponseType(typeof(ResponseDto<int>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<int>>> UpdateBuyOrderApplyFinalReceiptAsync([FromBody] ApplyFinalReceiptDto dto)
        {
            return Ok(await _buyOrderOutgoingService.ApplyFinalReceiptAsync(dto));
        }
    }
}
