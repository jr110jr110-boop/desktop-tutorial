using System.Collections.Generic;
using System.Net;
using System.Threading;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.EquipmentDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Extensions;
using DeleteEquipmentsResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <System.Collections.Generic.IEnumerable
                <ViewModels.NasbNiro.EquipmentDtos.DeleteEquipmentResultDto>>>>;
using EquipmentResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.EquipmentDtos.ResponseEquipmentDto>>>;
using PaginatedEquipmentsResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.Pagination.PaginatedResult
                <ViewModels.NasbNiro.EquipmentDtos.ResponseEquipmentDto>>>>;

namespace WebApi.Controllers
{
    [Authorize]
    public class EquipmentsController : BaseApiController
    {
        private readonly IEquipmentService _equipmentService;

        public EquipmentsController(IEquipmentService equipmentService)
        {
            _equipmentService = equipmentService ?? throw new System.ArgumentNullException(nameof(equipmentService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentDto>), (int)HttpStatusCode.OK)]
        public async EquipmentResult AddEquipmentAsync(
            [FromBody] AddEquipmentDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentService.AddEquipmentAsync(dto, cancellationToken));
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentDto>), (int)HttpStatusCode.OK)]
        public async EquipmentResult UpdateEquipmentAsync(
            [FromBody] UpdateEquipmentDto updateEquipmentDto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentService.UpdateEquipmentAsync(updateEquipmentDto, cancellationToken));
        }

        [HttpDelete("{equipmentId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentDto>), (int)HttpStatusCode.OK)]
        public async EquipmentResult DeleteEquipmentAsync(int equipmentId, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentService.DeleteEquipmentAsync(equipmentId, cancellationToken));
        }

        [HttpGet("{equipmentId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentDto>), (int)HttpStatusCode.OK)]
        public async EquipmentResult GetEquipmentByIdAsync(
            int equipmentId, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentService.GetResponseEquipmentDtoByIdAsync(equipmentId, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseEquipmentDto>>), (int)HttpStatusCode.OK)]
        public async PaginatedEquipmentsResult GetAllEquipmentsAsync(
            [FromQuery] EquipmentPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _equipmentService.GetAllResponseEquipmentDtoAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }

        [HttpDelete("delete-equipment-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteEquipmentResultDto>>), (int)HttpStatusCode.OK)]
        public async DeleteEquipmentsResult DeleteEquipmentCollectionAsync(
            int[] equipmentIds, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentService.DeleteEquipmentCollectionAsync(equipmentIds, cancellationToken));
        }
    }
}
