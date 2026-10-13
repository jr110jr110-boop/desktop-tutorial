using System.Net;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro;

namespace WebApi.Controllers
{
    [Authorize]
    public class BuyOrdersOutgoingController : BaseApiController
    {
        private readonly IBuyOrderOutgoingService _buyOrderOutgoingService;

        public BuyOrdersOutgoingController(IBuyOrderOutgoingService buyOrderOutgoingService)
        {
            _buyOrderOutgoingService = buyOrderOutgoingService;
        }

        [HttpPut("apply-temp-receipt")]
        [ProducesResponseType(typeof(ResponseDto<int>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<int>>> UpdateBuyOrderApplyTempReceiptAsync([FromBody] ApplyTempReceiptDto dto)
        {
            return Ok(await _buyOrderOutgoingService.ApplyTempReceiptAsync(dto));
        }

        [HttpPut("apply-final-receipt")]
        [ProducesResponseType(typeof(ResponseDto<int>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<int>>> UpdateBuyOrderApplyFinalReceiptAsync([FromBody] ApplyFinalReceiptDto dto)
        {
            return Ok(await _buyOrderOutgoingService.ApplyFinalReceiptAsync(dto));
        }
    }
}
