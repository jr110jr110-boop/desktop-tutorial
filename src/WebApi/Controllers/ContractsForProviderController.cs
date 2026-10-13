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
    [Authorize]
    public class ContractsForProviderController : BaseApiController
    {
        private readonly IContractService _contractService;

        public ContractsForProviderController(IContractService contractService)
        {
            _contractService = contractService ?? throw new System.ArgumentNullException(nameof(contractService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractDto>>> AddContractAsync([FromBody] AddContractDto addContractDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.AddContractForProviderAsync(addContractDto, cancellationToken));
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractDto>>> UpdateContractAsync([FromBody] UpdateContractDto updateContractDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.UpdateContractAsync(updateContractDto, cancellationToken));
        }

        [HttpGet("contracts")]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractDto>>> GetContractAsync([FromQuery]GetContractDto dto, CancellationToken cancellationToken)
        {
            dto.IsForProvider();
            return Ok(await _contractService.GetResponseContractDtoAsync(dto, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseContractDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<PaginatedResult<ResponseContractDto>>>> GetAllContractsAsync([FromQuery] ContractPaginationParams @params, CancellationToken cancellationToken)
        {
            @params.ForProvider = true;
            return Ok((await _contractService.GetAllResponseContractDtosAsync(@params, cancellationToken)).ToPaginatedResult());
        }

        [HttpPost("attach-file-to-contract")]
        [ProducesResponseType(typeof(ResponseDto<Guid>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<string>>> AttachFileToContractAsync([FromQuery] AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.AttachFileToContractAsync(attachFileDto, cancellationToken));
        }

        [HttpDelete("detach-file-from-contract")]
        [ProducesResponseType(typeof(ResponseDto<bool>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<bool>>> DetachFileFromContractAsync(int contractId, Guid fileId, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.DetachFileFromContractAsync(contractId, documentId: fileId, cancellationToken: cancellationToken));
        }

        [HttpDelete("{contractId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractDto>>> DeleteContractAsync(int contractId, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.DeleteContractAsync(contractId, cancellationToken));
        }

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
