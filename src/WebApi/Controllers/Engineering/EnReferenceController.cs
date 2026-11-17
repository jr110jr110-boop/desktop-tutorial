using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // ENG-E1: the reference document list. Role checks per action are in the services; rule violations answer 200 with success=false.

    [ApiController]
    [Authorize]
    [Route("api/v1/engineering/reference")]
    [RolesCheckedInService]
    public class EnReferenceController : ControllerBase
    {
        private readonly IEnReferenceService _service;

        public EnReferenceController(IEnReferenceService service)
        {
            _service = service;
        }

        [HttpGet("lists")]
        public async Task<IActionResult> ListsAsync([FromQuery] EnReferenceQuery query, CancellationToken ct) => Ok((await _service.GetListsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("approved-codes")]
        public async Task<IActionResult> ApprovedCodesAsync(CancellationToken ct) => Ok(await _service.GetApprovedCodesAsync(ct));

        [HttpGet("lists/{id:int}")]
        public async Task<IActionResult> ListAsync(int id, CancellationToken ct) => Ok(await _service.GetListAsync(id, ct));

        [HttpGet("current")]
        public async Task<IActionResult> CurrentAsync([FromQuery] string code, CancellationToken ct) => Ok(await _service.GetCurrentAsync(code, ct));

        [HttpPost("lists")]
        public async Task<IActionResult> SaveAsync([FromBody] EnSaveReferenceDto dto, CancellationToken ct) => Ok(await _service.SaveListAsync(dto, ct));

        [HttpPost("lists/action")]
        public async Task<IActionResult> ActionAsync([FromBody] EnReferenceActionDto dto, CancellationToken ct) => Ok(await _service.ListActionAsync(dto, ct));
    }
}
