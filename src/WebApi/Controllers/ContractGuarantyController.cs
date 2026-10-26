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
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   every write: PMO, COMMERCIAL, FINANCIAL, FINANCEMANAGER, TREASURY
    [Authorize]
    public class ContractGuarantyController : BaseApiController
    {
        private readonly IContractService _contractService;

        public ContractGuarantyController(IContractService contractService)
        {
            _contractService = contractService ?? throw new System.ArgumentNullException(nameof(contractService));
        }

        [Authorize(Roles = LegacyRoles.GuarantyWriters)]
        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractGuarantyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractGuarantyDto>>> AddTenderAsync(
            [FromBody] GuarantyContractDtoBase guarantyContractDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.AddGuarantyToContractAsync(guarantyContractDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.GuarantyWriters)]
        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractGuarantyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractGuarantyDto>>> UpdateTenderGuarantyAsync(
            [FromBody] ResponseContractGuarantyDto responseContractGuarantyDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.UpdateContractGuarantyAsync(responseContractGuarantyDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.GuarantyWriters)]
        [HttpDelete]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractGuarantyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractGuarantyDto>>> UpdateTenderGuarantyAsync(
            int contractGuarantyId, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.DeleteContractGuarantyAsync(contractGuarantyId, cancellationToken));
        }
    }
}