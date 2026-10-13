using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProviderDtos;
using WebApi.Extensions;
using DeleteProvidersResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <System.Collections.Generic.IEnumerable
                <ViewModels.NasbNiro.ProviderDtos.DeleteProviderResultDto>>>>;
using PaginatedProvidersResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.Pagination.PaginatedResult
                <ViewModels.NasbNiro.ProviderDtos.ResponseProviderDto>>>>;
using ProviderResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.ProviderDtos.ResponseProviderDto>>>;

namespace WebApi.Controllers
{
    [Authorize]
    public class ProviderController : BaseApiController
    {
        private readonly IProviderService _providerService;

        public ProviderController(IProviderService providerService)
        {
            _providerService = providerService ?? throw new System.ArgumentNullException(nameof(providerService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseProviderDto>), (int)HttpStatusCode.OK)]
        public async ProviderResult AddProviderAsync([FromBody] AddProviderDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _providerService.AddProviderAsync(dto, cancellationToken));
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseProviderDto>), (int)HttpStatusCode.OK)]
        public async ProviderResult UpdateProviderAsync([FromBody] UpdateProviderDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _providerService.UpdateProviderAsync(dto, cancellationToken));
        }

        [HttpPut("update-contact")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProviderDto>), (int)HttpStatusCode.OK)]
        public async ProviderResult UpdateProviderContactAsync([FromBody] UpdateProviderContactDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _providerService.UpdateProviderContractAsync(dto, cancellationToken));
        }

        [HttpPut("update-account")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProviderDto>), (int)HttpStatusCode.OK)]
        public async ProviderResult UpdateProviderAccountAsync([FromBody] UpdateProviderAccountDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _providerService.UpdateProviderAccountAsync(dto, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseProviderDto>>), (int)HttpStatusCode.OK)]
        public async PaginatedProvidersResult GetAllProvidersAsync([FromQuery] ProviderPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _providerService.GetAllResponseProviderDtosAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }

        [HttpGet("providers")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProviderDto>), (int)HttpStatusCode.OK)]
        public async ProviderResult GetProviderAsync([FromQuery] GetProviderDto getProviderDto, CancellationToken cancellationToken)
        {
            return Ok(await _providerService.GetResponseProviderDtoAsync(getProviderDto, cancellationToken));
        }

        [HttpPost("add-contact")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProviderDto>), (int)HttpStatusCode.OK)]
        public async ProviderResult AddProviderContactAsync([FromBody] AddProviderContactDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _providerService.AddProviderContactAsync(dto, cancellationToken));
        }

        [HttpPost("add-account")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProviderDto>), (int)HttpStatusCode.OK)]
        public async ProviderResult AddProviderAccountAsync([FromBody] AddProviderAccountDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _providerService.AddProviderAccountAsync(dto, cancellationToken));
        }

        [HttpDelete("{providerId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProviderDto>), (int)HttpStatusCode.OK)]
        public async ProviderResult DeleteProviderAsync([FromBody] int providerId, CancellationToken cancellationToken)
        {
            return Ok(await _providerService.DeleteProviderAsync(providerId, cancellationToken));
        }

        [HttpDelete("delete-contact/{providerContactId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProviderDto>), (int)HttpStatusCode.OK)]
        public async ProviderResult DeleteProviderContactAsync([FromBody] int providerContactId, CancellationToken cancellationToken)
        {
            return Ok(await _providerService.DeleteProviderContactAsync(providerContactId, cancellationToken));
        }

        [HttpDelete("delete-account/{providerAccountId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProviderDto>), (int)HttpStatusCode.OK)]
        public async ProviderResult DeleteProviderAccountAsync([FromBody] int providerAccountId, CancellationToken cancellationToken)
        {
            return Ok(await _providerService.DeleteProviderAccountAsync(providerAccountId, cancellationToken));
        }

        [HttpDelete("delete-provider-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteProviderResultDto>>), (int)HttpStatusCode.OK)]
        public async DeleteProvidersResult DeleteProviderCollectionAsync([FromBody] int[] providerIds, CancellationToken cancellationToken)
        {
            return Ok(await _providerService.DeleteProviderCollectionAsync(providerIds, cancellationToken));
        }

        [HttpGet("vendor-list")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<ResponseProviderDto>>), (int)HttpStatusCode.OK)]
        public async Task<ResponseDto<IEnumerable<ResponseProviderDto>>> GetVendorListAsync()
        {
            ResponseDto<IEnumerable<ResponseProviderDto>> result = await _providerService.GetVendorListAsync();

            return result;
        }
    }
}
