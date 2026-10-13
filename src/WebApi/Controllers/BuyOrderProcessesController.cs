using System.Collections.Generic;
using System.Net;
using System.Threading;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels;
using ViewModels.NasbNiro.BuyOrderProcesses;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcessDtos;
using WebApi.Extensions;
using BuyOrderProcessesResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.BuyOrderProcesses.ResponseBuyOrderProcessesDto>>>;
using DeleteBuyOrderProcessesResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <System.Collections.Generic.IEnumerable
                <ViewModels.NasbNiro.BuyOrderProcesses.DeleteBuyOrderProcessesResultDto>>>>;
using PaginatedBuyOrderProcesses = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.Pagination.PaginatedResult
                <ViewModels.NasbNiro.BuyOrderProcesses.ResponseBuyOrderProcessesDto>>>>;
using PaginatedSimpleProcesses = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.Pagination.PaginatedResult
                <ViewModels.NasbNiro.ProcessDtos.ResponseProcessSimpleDto>>>>;

namespace WebApi.Controllers
{
    [Authorize]
    public class BuyOrderProcessesController : BaseApiController
    {
        private readonly IBuyOrderProcessService _buyOrderProcessesService;

        public BuyOrderProcessesController
            (IBuyOrderProcessService buyOrderProcessService)
        {
            _buyOrderProcessesService = buyOrderProcessService ??
                throw new System.ArgumentNullException(nameof(buyOrderProcessService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderProcessesDto>), (int)HttpStatusCode.OK)]
        public async BuyOrderProcessesResult AddBuyOrderProcessesAsync([FromBody] AddBuyOrderProcessesDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderProcessesService.AddBuyOrderProcessesAsync(dto, cancellationToken));
        }

        [HttpGet("get-by-key")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderProcessesDto>), (int)HttpStatusCode.OK)]
        public async BuyOrderProcessesResult GetBuyOrderProcessesByIdAsync(int buyOrderId, int processId, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderProcessesService.GetResponseBuyOrderProcessesDtoByIdAsync(
                new GetBuyOrderProcessesDto()
                {
                    BuyOrderId = buyOrderId,
                    ProcessId = processId
                }, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseBuyOrderProcessesDto>>), (int)HttpStatusCode.OK)]
        public async PaginatedBuyOrderProcesses GetAllBuyOrderProcessessAsync(
            [FromQuery] BuyOrderProcessPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _buyOrderProcessesService.GetAllResponseBuyOrderProcessesDtosAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderProcessesDto>), (int)HttpStatusCode.OK)]
        public async BuyOrderProcessesResult UpdateBuyOrderProcessesAsync(
            [FromBody] UpdateBuyOrderProcessesDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderProcessesService.UpdateBuyOrderProcessesAsync(dto, cancellationToken));
        }

        [HttpGet("get-ready-processes-for-buyOrder")]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseProcessSimpleDto>>), (int)HttpStatusCode.OK)]
        public async PaginatedSimpleProcesses GetAllProcessesOfBuyOrderAsync(
            [FromQuery] ReadyProcessesForBuyOrderPaginationaParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _buyOrderProcessesService.GetReadyProcessesForBuyOrderAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }

        [HttpGet("get-processes-for-buyOrder")]
        [ProducesResponseType(typeof(List<ResponseProcessSimpleDto>), (int)HttpStatusCode.OK)]
        public async PaginatedSimpleProcesses GetProcessesOfBuyOrderIdAsync(
            [FromQuery] int buyOrderId, CancellationToken cancellationToken)
        {
            return Ok(new ResponseObjects()
            {
                Success = true,
                Message = "",
                Data = await _buyOrderProcessesService.GetProcessesByBuyOrderIdAsync(buyOrderId, cancellationToken)
            });
        }

        [HttpDelete]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderProcessesDto>), (int)HttpStatusCode.OK)]
        public async BuyOrderProcessesResult DeleteBuyOrderProcessesAsync(
            DeleteBuyOrderProcessesDto deleteBuyOrderProcessesDto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderProcessesService.DeleteBuyOrderProcessesAsync(deleteBuyOrderProcessesDto, cancellationToken));
        }

        [HttpDelete("delete-buyOrder-process-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteBuyOrderProcessesResultDto>>), (int)HttpStatusCode.OK)]
        public async DeleteBuyOrderProcessesResult DeleteBuyOrderProcessesCollectionAsync(
            IEnumerable<DeleteBuyOrderProcessesDto> dtos, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderProcessesService.DeleteBuyOrderProcessesCollectionAsync(dtos, cancellationToken));
        }
    }
}