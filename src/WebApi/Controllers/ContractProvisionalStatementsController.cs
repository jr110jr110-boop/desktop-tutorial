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
using ViewModels.NasbNiro.ProvisionalStatementDtos;
using ViewModels.NasbNiro.TenderDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   every write: PMO, FINANCIAL, FINANCEMANAGER, PROJECTMANAGER
    [Authorize]
    public class ContractProvisionalStatementsController : BaseApiController
    {
        private readonly IProStatService _proStatService;

        public ContractProvisionalStatementsController(IProStatService proStatService)
        {
            _proStatService = proStatService ?? throw new ArgumentNullException(nameof(proStatService));
        }

        [Authorize(Roles = LegacyRoles.ProvisionalStatementWriters)]
        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseProStatDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<ResponseProStatDto>> AddProStatAsync(
            [FromBody] AddProStatDto addProStatDto,
            CancellationToken cancellationToken)
        {
            var result = await _proStatService.AddProStatAsync(addProStatDto, cancellationToken);

            return result;
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<ResponseProStatDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<ResponseProStatDto>> GetProStatAsync(
            [FromQuery] GetProStatDto getProStatDto,
            CancellationToken cancellationToken)
        {
            var result = await _proStatService.GetProStatAsync(getProStatDto, cancellationToken);

            return result;
        }

        [HttpGet("provisional-statements")]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseProStatDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<PaginatedResult<ResponseProStatDto>>> GetAllProStatsAsync(
            [FromQuery] ProStatPaginationParams @params, 
            CancellationToken cancellationToken)
        {
            var result = await _proStatService.GetProStatsAsync(@params, cancellationToken);

            return result.ToPaginatedResult();
        }

        [Authorize(Roles = LegacyRoles.ProvisionalStatementWriters)]
        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseProStatDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<ResponseProStatDto>> UpdateProStatAsync(
            [FromBody] UpdateProStatDto updateProStatDto , 
            CancellationToken cancellationToken)
        {
            var result = await _proStatService.UpdateProStatAsync(updateProStatDto, cancellationToken);

            return result;
        }

        [Authorize(Roles = LegacyRoles.ProvisionalStatementWriters)]
        [HttpDelete]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async ValueTask<ResponseDto<string>> DeleteProStatAsync(
           [FromBody]DeleteProStatDto deleteProStatDto, 
            CancellationToken cancellationToken)
        {
            var result = await _proStatService.DeleteProStatAsync(deleteProStatDto , cancellationToken);

            return result;
        }
    }
}
