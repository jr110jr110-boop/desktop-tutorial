using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ContractDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    [Authorize]
    public class ContractGuarantyController : BaseApiController
    {
        private readonly IContractService _contractService;

        public ContractGuarantyController(IContractService contractService)
        {
            _contractService = contractService ?? throw new System.ArgumentNullException(nameof(contractService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractGuarantyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractGuarantyDto>>> AddTenderAsync(
            [FromBody] GuarantyContractDtoBase guarantyContractDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.AddGuarantyToContractAsync(guarantyContractDto, cancellationToken));
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractGuarantyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractGuarantyDto>>> UpdateTenderGuarantyAsync(
            [FromBody] ResponseContractGuarantyDto responseContractGuarantyDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.UpdateContractGuarantyAsync(responseContractGuarantyDto, cancellationToken));
        }

        [HttpDelete]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractGuarantyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractGuarantyDto>>> UpdateTenderGuarantyAsync(
            int contractGuarantyId, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.DeleteContractGuarantyAsync(contractGuarantyId, cancellationToken));
        }
    }
}