using System.Collections.Generic;
using System.Net;
using System.Threading;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.InquiryDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Extensions;
using DeleteInquiresResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <System.Collections.Generic.IEnumerable
                <ViewModels.NasbNiro.InquiryDtos.DeleteInquiryResultDto>>>>;
using InquiryResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.InquiryDtos.ResponseInquiryDto>>>;
using PaginatedInquiresResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.Pagination.PaginatedResult
                <ViewModels.NasbNiro.InquiryDtos.ResponseInquiryDto>>>>;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   create / update / delete: PMO, COMMERCIAL
    //   set-technical-score: PMO, ENGINEERING (the card board shows the scoring stage to engineering; commercial must not score its own inquiries)
    [Authorize]
    public class InquiriesController : BaseApiController
    {
        private readonly IInquiryService _inquiryService;

        public InquiriesController(IInquiryService inquiryService)
        {
            _inquiryService = inquiryService ?? throw new System.ArgumentNullException(nameof(inquiryService));
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseInquiryDto>), (int)HttpStatusCode.OK)]
        public async InquiryResult RegisterInquiryAsync([FromBody] AddInquiryDto addInquiryDto, CancellationToken cancellationToken)
        {
            return Ok(await _inquiryService.AddInquiryAsync(addInquiryDto, cancellationToken));
        }

        [HttpGet("get-inquiry")]
        [ProducesResponseType(typeof(ResponseDto<ResponseInquiryDto>), (int)HttpStatusCode.OK)]
        public async InquiryResult GetInquiryByIdAsync(GetInquiryDto getInquiryDto, CancellationToken cancellationToken)
        {
            return Ok(await _inquiryService.GetResponseInquiryDtoAsync(getInquiryDto, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseInquiryDto>>), (int)HttpStatusCode.OK)]
        public async PaginatedInquiresResult GetAllInquirysAsync(
            [FromQuery] InquiryPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _inquiryService.GetAllResponseInquiryDtosAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseInquiryDto>), (int)HttpStatusCode.OK)]
        public async InquiryResult UpdateInquiryAsync([FromBody] UpdateInquiryDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _inquiryService.UpdateInquiryAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TechnicalScoring)]
        [HttpPut("set-technical-score")]
        [ProducesResponseType(typeof(ResponseDto<ResponseInquiryDto>), (int)HttpStatusCode.OK)]
        public async InquiryResult UpdateInquirySetTechnicalScoreAsync(
            [FromBody] UpdateInquirySetTechnicalScoreDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _inquiryService.UpdateInquirySetTechnicalScoreAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpDelete("{inquiryId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseInquiryDto>), (int)HttpStatusCode.OK)]
        public async InquiryResult DeleteInquiryAsync(int inquiryId, CancellationToken cancellationToken)
        {
            return Ok(await _inquiryService.DeleteInquiryAsync(inquiryId, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpDelete("delete-inquiry-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteInquiryResultDto>>), (int)HttpStatusCode.OK)]
        public async DeleteInquiresResult DeleteInquiryCollectionAsync(int[] inquiryIds, CancellationToken cancellationToken)
        {
            return Ok(await _inquiryService.DeleteInquiryCollectionAsync(inquiryIds, cancellationToken));
        }
    }
}
