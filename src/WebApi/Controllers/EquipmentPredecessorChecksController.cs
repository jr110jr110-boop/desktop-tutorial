using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.PredecessorCheckDtos;

namespace WebApi.Controllers
{
    /// <summary>
    /// FX1: read-only check of the products in the bank for the PMO and planning: invalid predecessors (deleted, other product, missing, loop)
    /// and BOM lines without processes. Nothing is changed here; every correction is a proposal that a second person approves.
    /// </summary>
    [Authorize(Roles = "PMO,PLANNING")]
    public class EquipmentPredecessorChecksController : BaseApiController
    {
        private readonly IEquipmentPredecessorCheckService _service;

        public EquipmentPredecessorChecksController(IEquipmentPredecessorCheckService service)
        {
            _service = service ?? throw new System.ArgumentNullException(nameof(service));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PredecessorCheckReportDto>), (int)HttpStatusCode.OK)]
        public async Task<ActionResult<ResponseDto<PredecessorCheckReportDto>>> GetAsync(CancellationToken cancellationToken)
        {
            return Ok(await _service.CheckAsync(cancellationToken));
        }
    }
}
