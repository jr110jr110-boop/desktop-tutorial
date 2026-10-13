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
    [Authorize]
    public class EquipmentAssembliesController : BaseApiController
    {
        private readonly IEquipmentAssemblyService _equipmentAssemblyService;

        public EquipmentAssembliesController(IEquipmentAssemblyService EquipmentAssemblyService)
        {
            _equipmentAssemblyService = EquipmentAssemblyService
                ?? throw new System.ArgumentNullException(nameof(EquipmentAssemblyService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentAssemblyDto>), (int)HttpStatusCode.OK)]
        public async EquipmentAssemblyResult AddEquipmentAssemblyAsync([FromBody] AddEquipmentAssemblyDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.AddEquipmentAssemblyAsync(dto, cancellationToken));
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentAssemblyDto>), (int)HttpStatusCode.OK)]
        public async EquipmentAssemblyResult UpdateEquipmentAssemblyAsync([FromBody] UpdateEquipmentAssemblyDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.UpdateEquipmentAssemblyAsync(dto, cancellationToken));
        }

        [HttpPut("set-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentAssemblyDto>), (int)HttpStatusCode.OK)]
        public async EquipmentAssemblyResult SetPredecessorAsync([FromBody] UpdateEquipmentAssemblySetPredecessordDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.SetPredecessorAsync(dto, cancellationToken));
        }

        [HttpPut("delete-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentAssemblyDto>), (int)HttpStatusCode.OK)]
        public async EquipmentAssemblyResult DeletePredecessorAsync(int assemblyId, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.DeletePredecessorAsync(assemblyId, cancellationToken));
        }

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

        [HttpDelete("delete-equipmentAssembly-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteEquipmentAssemblyResultDto>>), (int)HttpStatusCode.OK)]
        public async DeleteEquipmentAssembliesResult DeleteEquipmentAssemblyCollectionAsync(int[] equipmentAssemblyIds, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentAssemblyService.DeleteEquipmentAssemblyCollectionAsync(equipmentAssemblyIds, cancellationToken));
        }
    }
}
