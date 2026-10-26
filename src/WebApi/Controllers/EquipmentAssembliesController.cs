using System.Collections.Generic;
using System.Net;
using System.Threading;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.EquipmentAssemblyDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Extensions;
using DeleteEquipmentAssembliesResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <System.Collections.Generic.IEnumerable
                <ViewModels.NasbNiro.EquipmentAssemblyDtos.DeleteEquipmentAssemblyResultDto>>>>;
using EquipmentAssemblyResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.EquipmentAssemblyDtos.ResponseEquipmentAssemblyDto>>>;
using PaginatedEqupmentAssemlies = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.Pagination.PaginatedResult
                <ViewModels.NasbNiro.EquipmentAssemblyDtos.ResponseEquipmentAssemblyDto>>>>;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   every write (engineering definitions): PMO, ENGINEERING, ENGINEER
    [Authorize]
    public class EquipmentAssembliesController : BaseApiController
    {
        private readonly IEquipmentAssemblyService _equipmentAssemblyService;

        public EquipmentAssembliesController(IEquipmentAssemblyService EquipmentAssemblyService)
        {
            _equipmentAssemblyService = EquipmentAssemblyService
                ?? throw new System.ArgumentNullException(nameof(EquipmentAssemblyService));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentAssemblyDto>), (int)HttpStatusCode.OK)]
        public async EquipmentAssemblyResult AddEquipmentAssemblyAsync([FromBody] AddEquipmentAssemblyDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.AddEquipmentAssemblyAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentAssemblyDto>), (int)HttpStatusCode.OK)]
        public async EquipmentAssemblyResult UpdateEquipmentAssemblyAsync([FromBody] UpdateEquipmentAssemblyDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.UpdateEquipmentAssemblyAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPut("set-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentAssemblyDto>), (int)HttpStatusCode.OK)]
        public async EquipmentAssemblyResult SetPredecessorAsync([FromBody] UpdateEquipmentAssemblySetPredecessordDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.SetPredecessorAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPut("delete-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentAssemblyDto>), (int)HttpStatusCode.OK)]
        public async EquipmentAssemblyResult DeletePredecessorAsync(int assemblyId, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.DeletePredecessorAsync(assemblyId, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpDelete("{equipmentAssemblyId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentAssemblyDto>), (int)HttpStatusCode.OK)]
        public async EquipmentAssemblyResult DeleteEquipmentAssemblyAsync(int equipmentAssemblyId, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.DeleteEquipmentAssemblyAsync(equipmentAssemblyId, cancellationToken));
        }

        [HttpGet("get-equipmentAssembly")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentAssemblyDto>), (int)HttpStatusCode.OK)]
        public async EquipmentAssemblyResult GetEquipmentByIdAsync([FromQuery] GetEquipmentAssemblyDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.GetResponseEquipmentAssemblyDtoAsync(dto, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseEquipmentAssemblyDto>>), (int)HttpStatusCode.OK)]
        public async PaginatedEqupmentAssemlies GetAllEquipmentsAsync([FromQuery] EquipmentAssemblyPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _equipmentAssemblyService.GetAllResponseEquipmentAssemblyDtoAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpDelete("delete-equipmentAssembly-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteEquipmentAssemblyResultDto>>), (int)HttpStatusCode.OK)]
        public async DeleteEquipmentAssembliesResult DeleteEquipmentAssemblyCollectionAsync(int[] equipmentAssemblyIds, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.DeleteEquipmentAssemblyCollectionAsync(equipmentAssemblyIds, cancellationToken));
        }
    }
}
