using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.ContractDtos;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   create / update / files: PMO, PLANNING, COMMERCIAL, PROJECTMANAGER
    //   delete (one or many): PMO only
    [Authorize]
    public class ContractsController : BaseApiController
    {
        private readonly IContractService _contractService;

        public ContractsController(IContractService contractService)
        {
            _contractService = contractService ?? throw new System.ArgumentNullException(nameof(contractService));
        }

        [Authorize(Roles = LegacyRoles.ContractWriters)]
        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractDto>>> AddContractAsync([FromBody] AddContractDto addContractDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.AddContractAsync(addContractDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.ContractWriters)]
        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractDto>>> UpdateContractAsync([FromBody] UpdateContractDto updateContractDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.UpdateContractAsync(updateContractDto, cancellationToken));
        }

        [HttpGet("contracts")]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractDto>>> GetContractAsync([FromQuery] GetContractDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.GetResponseContractDtoAsync(dto, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseContractDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<PaginatedResult<ResponseContractDto>>> GetAllContractsAsync([FromQuery] ContractPaginationParams @params, CancellationToken cancellationToken)
        {
            @params.ForProvider = false;
            return (await _contractService.GetAllResponseContractDtosAsync(@params, cancellationToken)).ToPaginatedResult();
        }

        [Authorize(Roles = LegacyRoles.ContractWriters)]
        [HttpPost("attach-file-to-contract")]
        [ProducesResponseType(typeof(ResponseDto<Guid>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<string>>> AttachFileToContractAsync([FromQuery] AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.AttachFileToContractAsync(attachFileDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.ContractWriters)]
        [HttpDelete("detach-file-from-contract")]
        [ProducesResponseType(typeof(ResponseDto<bool>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<bool>>> DetachFileFromContractAsync(int contractId, Guid fileId, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.DetachFileFromContractAsync(contractId, documentId: fileId, cancellationToken: cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpDelete("{contractId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractDto>>> DeleteContractAsync(int contractId, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.DeleteContractAsync(contractId, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpDelete("delete-contract-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteContractResultDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<IEnumerable<DeleteContractResultDto>>>> DeleteContractCollectionAsync(
            int[] contractIds,
            CancellationToken cancellationToken)
        {
            return Ok(await _contractService.DeleteContractCollectionAsync(contractIds, cancellationToken));
        }
    }
}
