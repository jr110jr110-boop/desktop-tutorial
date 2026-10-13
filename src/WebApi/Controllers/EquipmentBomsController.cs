using System.Collections.Generic;
using System.Net;
using System.Threading;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.EquipmentBomDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Extensions;
using DeleteEquipmentBomsResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <System.Collections.Generic.IEnumerable
                <ViewModels.NasbNiro.EquipmentBomDtos.DeleteEquipmentBomResultDto>>>>;
using EquipmentBomResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.EquipmentBomDtos.ResponseEquipmentBomDto>>>;
using PaginatedEquipmentBomResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.Pagination.PaginatedResult
                <ViewModels.NasbNiro.EquipmentBomDtos.ResponseEquipmentBomDto>>>>;

namespace WebApi.Controllers
{
    [Authorize]
    public class EquipmentBomsController : BaseApiController
    {
        private readonly IEquipmentBomService _equipmentBomService;

        public EquipmentBomsController(IEquipmentBomService EquipmentBomService)
        {
            _equipmentBomService = EquipmentBomService
                ?? throw new System.ArgumentNullException(nameof(EquipmentBomService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentBomDto>), (int)HttpStatusCode.OK)]
        public async EquipmentBomResult AddEquipmentBomAsync([FromBody] AddEquipmentBomWithEqueipmentProccessDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.AddEquipmentBomAsync(dto, cancellationToken));
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentBomDto>), (int)HttpStatusCode.OK)]
        public async EquipmentBomResult UpdateEquipmentBomAsync([FromBody] UpdateEquipmentBomDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.UpdateEquipmentBomAsync(dto, cancellationToken));
        }

        [HttpPut("set-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentBomDto>), (int)HttpStatusCode.OK)]
        public async EquipmentBomResult SetPredecessorAsync([FromBody] UpdateEquipmentBomSetPredecessorDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.SetPredecessorAsync(dto, cancellationToken));
        }

        [HttpPut("delete-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentBomDto>), (int)HttpStatusCode.OK)]
        public async EquipmentBomResult DeletePredecessorAsync(int equipmentBomId, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.DeletePredecessorAsync(equipmentBomId, cancellationToken));
        }

        [HttpDelete("{equipmentBomId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentBomDto>), (int)HttpStatusCode.OK)]
        public async EquipmentBomResult DeleteEquipmentBomAsync(int equipmentBomId, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.DeleteEquipmentBomAsync(equipmentBomId, cancellationToken));
        }

        [HttpGet("get-equipmentBom")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentBomDto>), (int)HttpStatusCode.OK)]
        public async EquipmentBomResult GetEquipmentAsync([FromQuery] GetEquipmentBomDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.GetResponseEquipmentBomDtoByIdAsync(dto, cancellationToken));
        }

        [HttpGet("get-equipmentBom-all")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentBomAllDto>), (int)HttpStatusCode.OK)]
        public async EquipmentBomResult GetEquipmentAllAsync([FromQuery] GetEquipmentBomAllDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.GetAllResponseEquipmentBomDtoAsync(dto, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseEquipmentBomDto>>), (int)HttpStatusCode.OK)]
        public async PaginatedEquipmentBomResult GetAllEquipmentsAsync(
            [FromQuery] EquipmentBomPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _equipmentBomService.GetAllResponseEquipmentBomDtoAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }

        [HttpDelete("delete-equipmentBom-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteEquipmentBomResultDto>>), (int)HttpStatusCode.OK)]
        public async DeleteEquipmentBomsResult DeleteEquipmentBomCollectionAsync(
            int[] equipmentBomIds, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.DeleteEquipmentBomCollectionAsync(equipmentBomIds, cancellationToken));
        }
    }
}
