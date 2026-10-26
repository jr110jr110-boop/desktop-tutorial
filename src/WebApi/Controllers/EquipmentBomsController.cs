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
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   every write (engineering definitions): PMO, ENGINEERING, ENGINEER
    [Authorize]
    public class EquipmentBomsController : BaseApiController
    {
        private readonly IEquipmentBomService _equipmentBomService;

        public EquipmentBomsController(IEquipmentBomService EquipmentBomService)
        {
            _equipmentBomService = EquipmentBomService
                ?? throw new System.ArgumentNullException(nameof(EquipmentBomService));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentBomDto>), (int)HttpStatusCode.OK)]
        public async EquipmentBomResult AddEquipmentBomAsync([FromBody] AddEquipmentBomWithEqueipmentProccessDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.AddEquipmentBomAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentBomDto>), (int)HttpStatusCode.OK)]
        public async EquipmentBomResult UpdateEquipmentBomAsync([FromBody] UpdateEquipmentBomDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.UpdateEquipmentBomAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPut("set-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentBomDto>), (int)HttpStatusCode.OK)]
        public async EquipmentBomResult SetPredecessorAsync([FromBody] UpdateEquipmentBomSetPredecessorDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.SetPredecessorAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPut("delete-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentBomDto>), (int)HttpStatusCode.OK)]
        public async EquipmentBomResult DeletePredecessorAsync(int equipmentBomId, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.DeletePredecessorAsync(equipmentBomId, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
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

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpDelete("delete-equipmentBom-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteEquipmentBomResultDto>>), (int)HttpStatusCode.OK)]
        public async DeleteEquipmentBomsResult DeleteEquipmentBomCollectionAsync(
            int[] equipmentBomIds, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentBomService.DeleteEquipmentBomCollectionAsync(equipmentBomIds, cancellationToken));
        }
    }
}
