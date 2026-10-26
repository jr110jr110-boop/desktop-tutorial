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
using ViewModels.NasbNiro.TenderDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   every write (tender, its files and its guarantees): PMO, COMMERCIAL, SALES
    [Authorize]
    public class TendersController : BaseApiController
    {
        private readonly ITenderService _tenderService;

        public TendersController(ITenderService tenderService)
        {
            _tenderService = tenderService ?? throw new System.ArgumentNullException(nameof(tenderService));
        }

        [Authorize(Roles = LegacyRoles.TenderWriters)]
        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseTenderDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseTenderDto>>> RegisterTenderAsync(
            [FromBody] AddTenderDto addTenderDto, CancellationToken cancellationToken)
        {
            return Ok(await _tenderService.AddTenderAsync(addTenderDto, cancellationToken));
        }

        [HttpGet("{tenderId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseTenderDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseTenderDto>>> GetTenderByIdAsync(
            int tenderId, CancellationToken cancellationToken)
        {
            return Ok(await _tenderService.GetResponseTenderDtoByIdAsync(tenderId, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseTenderDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<PaginatedResult<ResponseTenderDto>>>> GetAllTendersAsync(
            [FromQuery] TenderPaginationParams tenderPaginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _tenderService.GetAllResponseTenderDtosAsync(tenderPaginationParams, cancellationToken)).ToPaginatedResult());
        }

        [Authorize(Roles = LegacyRoles.TenderWriters)]
        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseTenderDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseTenderDto>>> UpdateTenderAsync(
            [FromBody] UpdateTenderDto updateTenderDto, CancellationToken cancellationToken)
        {
            return Ok(await _tenderService.UpdateTenderAsync(updateTenderDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TenderWriters)]
        [HttpPost("attach-file-to-tender")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<string>>> AttachFileToTenderAsync(
            [FromQuery] AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken)
        {
            return Ok(await _tenderService.AttachFileToTenderAsync(attachFileDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TenderWriters)]
        [HttpPut("edit-file-to-tender")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<string>>> UpdateFileToTenderAsync(
            [FromQuery] UpdateDocumentDto updateFileDto, CancellationToken cancellationToken)
        {
            return Ok(await _tenderService.UpdateFileToTenderAsync(updateFileDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TenderWriters)]
        [HttpDelete("detach-file-from-tender")]
        [ProducesResponseType(typeof(ResponseDto<bool>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<bool>>> DetachFileFromTenderAsync(
            int tenderId, Guid fileId, CancellationToken cancellationToken)
        {
            return Ok(await _tenderService.DetachFileFromTenderAsync(tenderId, documentId: fileId, cancellationToken: cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TenderWriters)]
        [HttpDelete("{tenderId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseTenderDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseTenderDto>>> DeleteTenderAsync(
            int tenderId, CancellationToken cancellationToken)
        {
            return Ok(await _tenderService.DeleteTenderAsync(tenderId, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TenderWriters)]
        [HttpDelete("delete-tender-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteTenderResultDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<IEnumerable<DeleteTenderResultDto>>>> DeleteTenderCollectionAsync(
            int[] tenderIds, CancellationToken cancellationToken)
        {
            return Ok(await _tenderService.DeleteTenderCollectionAsync(tenderIds, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TenderWriters)]
        [HttpPost("add-guaranty-to-tender")]
        [ProducesResponseType(typeof(ResponseDto<ResponseTenderGuarantyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseTenderGuarantyDto>>> AddTenderAsync(
            [FromBody] GuarantyTenderDtoBase guarantyTenderDto, CancellationToken cancellationToken)
        {
            return Ok(await _tenderService.AddGuarantyToTenderAsync(guarantyTenderDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TenderWriters)]
        [HttpPut("edit-guaranty-in-tender")]
        [ProducesResponseType(typeof(ResponseDto<ResponseTenderGuarantyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseTenderGuarantyDto>>> UpdateTenderGuarantyAsync(
            [FromBody] ResponseTenderGuarantyDto responseTenderGuarantyDto, CancellationToken cancellationToken)
        {
            return Ok(await _tenderService.UpdateTenderGuarantyAsync(responseTenderGuarantyDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TenderWriters)]
        [HttpDelete("delete-guaranty-from-tender")]
        [ProducesResponseType(typeof(ResponseDto<ResponseTenderGuarantyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseTenderGuarantyDto>>> UpdateTenderGuarantyAsync(
            int tenderGuarantyId, CancellationToken cancellationToken)
        {
            return Ok(await _tenderService.DeleteTenderGuarantyAsync(tenderGuarantyId, cancellationToken));
        }
    }
}
