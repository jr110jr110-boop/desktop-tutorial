using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.BomDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Extensions;
using BomPagedResult = System.Threading.Tasks.ValueTask<
    Microsoft.AspNetCore.Mvc.ActionResult<
        Common.Results.ResponseDto<
            ViewModels.NasbNiro.Pagination.PaginatedResult<
                ViewModels.NasbNiro.BomDtos.ResponseBomDto>>>>;
using BomResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto<ViewModels.NasbNiro.BomDtos.ResponseBomDto>>>;

namespace WebApi.Controllers
{
    [Authorize]
    public class BomsController : BaseApiController
    {
        private readonly IBomService _bomService;

        public BomsController(IBomService BomService)
        {
            _bomService = BomService
                ?? throw new System.ArgumentNullException(nameof(BomService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseBomDto>), (int)HttpStatusCode.OK)]
        public async BomResult AddBomAsync([FromBody] AddBomDto addBomDto, CancellationToken cancellationToken)
        {
            return Ok(await _bomService.AddBomAsync(addBomDto, cancellationToken));
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseBomDto>), (int)HttpStatusCode.OK)]
        public async BomResult UpdateBomAsync([FromBody] UpdateBomDto updateBomDto, CancellationToken cancellationToken)
        {
            return Ok(await _bomService.UpdateBomAsync(updateBomDto, cancellationToken));
        }

        [HttpPut("set-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBomDto>), (int)HttpStatusCode.OK)]
        public async BomResult SetPredecessorAsync([FromBody] UpdateBomSetPredecessorDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _bomService.SetPredecessorAsync(dto, cancellationToken));
        }

        [HttpPut("delete-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBomDto>), (int)HttpStatusCode.OK)]
        public async BomResult DeletePredecessorAsync(int bomId, CancellationToken cancellationToken)
        {
            return Ok(await _bomService.DeletePredecessorAsync(bomId, cancellationToken));
        }

        [HttpDelete("{bomId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBomDto>), (int)HttpStatusCode.OK)]
        public async BomResult DeleteBomAsync(int bomId, CancellationToken cancellationToken)
            => Ok(await _bomService.DeleteBomAsync(bomId, cancellationToken));

        [HttpGet("get-bom")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBomDto>), (int)HttpStatusCode.OK)]
        public async BomResult GetBomAsync([FromQuery] GetBomDto getBomDto, CancellationToken cancellationToken)
            => Ok(await _bomService.GetResponseBomDtoAsync(getBomDto, cancellationToken));

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseBomDto>>), (int)HttpStatusCode.OK)]
        public async BomPagedResult GetAllBomsAsync([FromQuery] BomPaginationParams BomPaginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _bomService.GetBomsAsync(BomPaginationParams, cancellationToken)).ToPaginatedResult());
        }


        [HttpDelete("delete-bom-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteBomResultDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<IEnumerable<DeleteBomResultDto>>>> DeleteBomCollectionAsync(int[] bomIds, CancellationToken cancellationToken)
        {
            return Ok(await _bomService.DeleteBomCollectionAsync(bomIds, cancellationToken));
        }
    }
}
