using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.WorkOrders;
using ViewModels.NasbNiro.AssemblyDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.WorkOrderDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    [Authorize]
    public class WorkOrdersController : BaseApiController
    {
        private readonly IWorkOrderService _workOrderService;

        public WorkOrdersController(IWorkOrderService WorkOrderService)
        {
            _workOrderService = WorkOrderService
                ?? throw new System.ArgumentNullException(nameof(WorkOrderService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseWorkOrderDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseWorkOrderDto>>> AddWorkOrderAsync(
            [FromBody] AddWorkOrderDto addWorkOrderDto, CancellationToken cancellationToken)
        {
            return Ok(await _workOrderService.AddWorkOrderAsync(addWorkOrderDto, cancellationToken));
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseWorkOrderDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseWorkOrderDto>>> UpdateWorkOrderAsync(
            [FromBody] UpdateWorkOrderDto updateWorkOrderDto, CancellationToken cancellationToken)
        {
            return Ok(await _workOrderService.UpdateWorkOrderAsync(updateWorkOrderDto, cancellationToken));
        }

        [HttpPut("set-start-date")]
        [ProducesResponseType(typeof(ResponseDto<ResponseWorkOrderDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseWorkOrderDto>>> SetStartDateAsync(
            [FromBody] UpdateWorkOrderSetStartDateDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _workOrderService.SetStartDateAsync(dto, cancellationToken));
        }

        [HttpPut("change-condition")]
        [ProducesResponseType(typeof(ResponseDto<ResponseWorkOrderDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseWorkOrderDto>>> ChangeConditionAsync(
            [FromBody] UpdateWorkOrderChangeConditionDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _workOrderService.ChangeConditionAsync(dto, cancellationToken));
        }

        [HttpDelete("{workOrderId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseWorkOrderDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseWorkOrderDto>>> DeleteWorkOrderAsync(
            [FromBody] int workOrderId, CancellationToken cancellationToken)
        {
            return Ok(await _workOrderService.DeleteWorkOrderAsync(workOrderId, cancellationToken));
        }

        [HttpGet("get-workorder")]
        [ProducesResponseType(typeof(ResponseDto<ResponseWorkOrderDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseWorkOrderDto>>> GetWorkOrderAsync(
            [FromQuery] GetWorkOrderDto getWorkOrderDto, CancellationToken cancellationToken)
        {
            return Ok(await _workOrderService.GetResponseWorkOrderDtoAsync(getWorkOrderDto, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseWorkOrderDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<PaginatedResult<ResponseWorkOrderDto>>>> GetAllWorkOrdersAsync(
            [FromQuery] WorkOrderPaginationParams WorkOrderPaginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _workOrderService.GetAllResponseWorkOrderDtosAsync(WorkOrderPaginationParams, cancellationToken)).ToPaginatedResult());
        }

        [HttpDelete("delete-workOrder-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteWorkOrderResultDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<IEnumerable<DeleteWorkOrderResultDto>>>> DeleteWorkOrderCollectionAsync(
            int[] deleteWorkOrderIds, CancellationToken cancellationToken)
        {
            return Ok(await _workOrderService.DeleteWorkOrderCollectionAsync(deleteWorkOrderIds, cancellationToken));
        }

        [HttpGet("calculate-progress-levels")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<AssemblyProgressLevelDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<IEnumerable<AssemblyProgressLevelDto>>>> CalculateWorkOrderProgressLevelAsync(
            int workOrderId, CancellationToken CancellationToken)
        {
            return Ok(await _workOrderService.CalculateProgressLevelAsync(workOrderId, CancellationToken));
        }
    }
}
