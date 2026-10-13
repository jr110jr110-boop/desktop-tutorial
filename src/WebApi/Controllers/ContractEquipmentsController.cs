using System.Collections.Generic;
using System.Net;
using System.Threading;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.ContractEquipmentDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Extensions;
using ContractEquipmentResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.ContractEquipmentDtos.ResponseContractEquipmentDto>>>;
using ContractEquipmentsResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto<System.Collections.Generic.IEnumerable
            <ViewModels.NasbNiro.ContractEquipmentDtos.ResponseContractEquipmentDto>>>>;
using ContractEquipmentsToDelete = System.Collections.Generic.IEnumerable
    <ViewModels.NasbNiro.ContractEquipmentDtos.DeleteContractEquipmentDto>;
using DeleteContractEquipmentsResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult<Common.Results.ResponseDto
        <System.Collections.Generic.IEnumerable
            <ViewModels.NasbNiro.ContractEquipmentDtos.DeleteContractEquipmentResultDto>>>>;
using PaginatedContractEquipments = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult<Common.Results.ResponseDto
        <ViewModels.NasbNiro.Pagination.PaginatedResult
            <ViewModels.NasbNiro.ContractEquipmentDtos.ResponseContractEquipmentDto>>>>;

namespace WebApi.Controllers
{
    [Authorize]
    public class ContractEquipmentsController : BaseApiController
    {
        private readonly IContractEquipmentService _contractEquipmentService;

        public ContractEquipmentsController(IContractEquipmentService contractEquipmentService)
        {
            _contractEquipmentService = contractEquipmentService ??
                throw new System.ArgumentNullException(nameof(contractEquipmentService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractEquipmentDto>), (int)HttpStatusCode.OK)]
        public async ContractEquipmentResult AddContractEquipmentAsync([FromBody] AddContractEquipmentDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _contractEquipmentService.AddContractEquipmentAsync(dto, cancellationToken));
        }

        [HttpGet("get-by-key")]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractEquipmentDto>), (int)HttpStatusCode.OK)]
        public async ContractEquipmentResult GetContractEquipmentByIdAsync(int contractId, int equipmentId, CancellationToken cancellationToken)
        {
            return Ok(await _contractEquipmentService.GetResponseContractEquipmentDtoByIdAsync(
            new GetContractEquipmentDto()
            {
                ContractId = contractId,
                EquipmentId = equipmentId
            }, cancellationToken));
        }

        [HttpGet("get-remaining-by-key")]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractEquipmentDto>), (int)HttpStatusCode.OK)]
        public async ContractEquipmentResult GetRemainingCountByIdAsync([FromQuery] GetContractEquipmentDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _contractEquipmentService.GetRemainingCountByIdAsync(dto, cancellationToken));
        }

        [HttpGet("get-all-remaining-of-contract/{contractId:int}")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<ResponseContractEquipmentDto>>), (int)HttpStatusCode.OK)]
        public async ContractEquipmentsResult GetRemainingCountByIdAsync(int contractId, CancellationToken cancellationToken)
        {
            return Ok(await _contractEquipmentService.GetAllRemainingCountOfContractByIdAsync(contractId, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseContractEquipmentDto>>), (int)HttpStatusCode.OK)]
        public async PaginatedContractEquipments GetAllContractEquipmentsAsync([FromQuery] ContractEquipmentPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _contractEquipmentService.GetAllResponseContractEquipmentDtosAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractEquipmentDto>), (int)HttpStatusCode.OK)]
        public async ContractEquipmentResult UpdateContractEquipmentAsync([FromBody] UpdateContractEquipmentDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _contractEquipmentService.UpdateContractEquipmentAsync(dto, cancellationToken));
        }

        [HttpDelete]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractEquipmentDto>), (int)HttpStatusCode.OK)]
        public async ContractEquipmentResult DeleteContractEquipmentAsync(DeleteContractEquipmentDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _contractEquipmentService.DeleteContractEquipmentAsync(dto, cancellationToken));
        }

        [HttpDelete("delete-contract-equipment-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteContractEquipmentResultDto>>), (int)HttpStatusCode.OK)]
        public async DeleteContractEquipmentsResult DeleteContractEquipmentCollectionAsync(ContractEquipmentsToDelete dtos, CancellationToken cancellationToken)
        {
            return Ok(await _contractEquipmentService.DeleteContractEquipmentCollectionAsync(dtos, cancellationToken));
        }
    }
}
