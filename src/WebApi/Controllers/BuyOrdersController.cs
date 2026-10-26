using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts.Identity;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Extensions;
using BuyOrderAndProcessesResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <System.Collections.Generic.IEnumerable
                <ViewModels.NasbNiro.BuyOrderDtos.ResponseBuyOrderAndProcesses>>>>;
using DeleteBuyOrdersResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <System.Collections.Generic.IEnumerable
                <ViewModels.NasbNiro.BuyOrderDtos.DeleteBuyOrderResultDto>>>>;
using PaginatedBuyOrderResponse = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.Pagination.PaginatedResult
                <ViewModels.NasbNiro.BuyOrderDtos.ResponseBuyOrderDto>>>>;
using ResponseBuyOrderDtoResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.BuyOrderDtos.ResponseBuyOrderDto>>>;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   The legacy buy order is a role-per-stage workflow (CardBoardService): each stage action belongs to the role that sees the stage, PMO can do all of them.
    //   create / update / delete / inquiries-added / commercial-bought: PMO, COMMERCIAL
    //   technical-score-added, engineering-accepted, engineering-reject-give-to-salvage, engineering-says-needs-fixes: PMO, ENGINEERING
    //   winner-selected, pmo-accept-reject, pmo-accepted-given-to-warehouse, revert-buyOrder-status, update-full-parameters: PMO only
    //   finance-document-setted: PMO, FINANCIAL;  add-temp-receipt, warehouse-accept-reject, warehouse-finally-accepted: PMO, WAREHOUSE;  qc-accepted / qc-rejected: PMO, QC
    //   assembler-assembled: PMO, ASSEMBLER;  constructor-constructed: PMO, CONSTRUCTION
    //   attach / detach file: every stage role (PMO, COMMERCIAL, FINANCIAL, ENGINEERING, QC, WAREHOUSE, ASSEMBLER, CONSTRUCTION) because each stage attaches its own documents
    [Authorize]
    public class BuyOrdersController : BaseApiController
    {
        private readonly IBuyOrderService _buyOrderService;
        private readonly IApplicationUserManager _applicationUserManager;

        public BuyOrdersController(
            IBuyOrderService BuyOrderService,
            IApplicationUserManager applicationUserManager)
        {
            _buyOrderService = BuyOrderService ?? throw new ArgumentNullException(nameof(BuyOrderService));
            _applicationUserManager = applicationUserManager ?? throw new ArgumentNullException(nameof(applicationUserManager));
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult AddBuyOrderAsync([FromBody] AddBuyOrderDto addBuyOrderDto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.AddBuyOrderAsync(addBuyOrderDto, cancellationToken));
        }

        [HttpGet("GetBuyOrderChangedCountsAsync")]
        [ProducesResponseType(typeof(ResponseDto<ResponseUpdateBuyOrderChangeCountDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult GetBuyOrderChangedCountsAsync(
            [FromQuery] UpdateBuyOrderChangeCountDto dto,
            CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.GetBuyOrderChangedCountsAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderAsync([FromBody] UpdateBuyOrderDto updateBuyOrderDto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderAsync(updateBuyOrderDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.BuyOrderAssembler)]
        [HttpPut("assembler-assembled")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderAssemblerAssembledAsync([FromBody] UpdateBuyOrderAssemblerAssembled dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderAssemblerAssembledAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.BuyOrderConstruction)]
        [HttpPut("constructor-constructed")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderConstructorConstructedAsync([FromBody] UpdateBuyOrderConstructorConstructed dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderConstructorConstructedAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpPut("revert-buyOrder-status")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderRevertStatusAsync([FromBody] RevertBuyOrderDto revertBuyOrderDto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderRevertStatusAsync(revertBuyOrderDto, cancellationToken));
        }


        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpPut("update-full-parameters")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderFullParametersAsync([FromBody] UpdateBuyOrderFullParametersDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderFullParametersAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpPut("inquiries-added")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult InquiriesAddedAsync([FromBody] UpdateBuyOrderInquiriesAddedDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderInquiriesAddedAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TechnicalScoring)]
        [HttpPut("technical-score-added")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderTechnicalScoreAddedAsync
           ([FromBody] UpdateBuyOrderTechnicalScoreAddedDto updateBuyOrderTechnicalScoreAddedDto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderTechnicalScoreAddedAsync(updateBuyOrderTechnicalScoreAddedDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpPut("winner-selected")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderWinnerSelectedAsync([FromBody] UpdateBuyOrderWinnerSelectedDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderWinnerSelectedAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.BuyOrderFinance)]
        [HttpPut("finance-document-setted")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderFinanceDocumentSettedAsync(
            [FromBody] UpdateBuyOrderFinanceDocumentSettedDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderFinanceDocSettedAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpPut("commercial-bought")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderCommercialBoughtAsync(
            [FromBody] UpdateBuyOrderCommercialBoughtDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.CommercialBoughtAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.BuyOrderWarehouse)]
        [HttpPut("add-temp-receipt")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderTempReceiptAddedAsync([FromBody] UpdateBuyOrderTempReceiptAddedDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderTempReceiptAddedAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.BuyOrderQuality)]
        [HttpPut("qc-accepted")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult QcAcceptedAsync([FromBody] UpdateBuyOrderQcAcceptDto updateBuyOrderQcAcceptDto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderQcAcceptAsync(updateBuyOrderQcAcceptDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.BuyOrderQuality)]
        [HttpPut("qc-rejected")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult QcRejectedAsync([FromBody] UpdateBuyOrderQcRejectDto updateBuyOrderQcRejectDto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderQcRejectAsync(updateBuyOrderQcRejectDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TechnicalScoring)]
        [HttpPut("engineering-accepted")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult EngineeringAcceptedAsync([FromBody] UpdateBuyOrderEngineeringAcceptedDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderEngineeringAcceptedAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TechnicalScoring)]
        [HttpPut("engineering-reject-give-to-salvage")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult EngineeringRejectedAsync([FromBody] UpdateBuyOrderEngineeringRejectDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderEngineeringRejectAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.TechnicalScoring)]
        [HttpPut("engineering-says-needs-fixes")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult EngineeringSaysNeedsFixesAsync([FromBody] UpdateBuyOrderEngineeringSaysNeedsFixedsDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderEngineeringSaysNeedsFixesAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpPut("pmo-accept-reject")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult PmoAcceptRejectAsync([FromBody] UpdateBuyOrderEngineeringRejectDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderPmoAcceptRejectAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.BuyOrderWarehouse)]
        [HttpPut("warehouse-accept-reject")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult WareHouseAcceptRejectAsync([FromBody] UpdateBuyOrderEngineeringRejectDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderRejectedAsync(dto, cancellationToken));
        }

        //[HttpPut("finance-accepted-processes-completed")]
        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpPut("pmo-accepted-given-to-warehouse")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderFinanceAcceptedAsync([FromBody] UpdateBuyOrderFinanceAcceptedDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderFinanceAcceptedAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.BuyOrderWarehouse)]
        [HttpPut("warehouse-finally-accepted")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult UpdateBuyOrderFinanceAcceptedAsync([FromBody] UpdateBuyOrderWarehouseFinallyAccepted dto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.UpdateBuyOrderWarehouseFinallyAcceptedAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpDelete("{buyOrderId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult DeleteBuyOrderAsync([FromQuery] int buyOrderId, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.DeleteBuyOrderAsync(buyOrderId, cancellationToken));
        }

        [HttpGet("get-buyorder")]
        [ProducesResponseType(typeof(ResponseDto<ResponseBuyOrderDto>), (int)HttpStatusCode.OK)]
        public async ResponseBuyOrderDtoResult GetBuyOrderAsync([FromQuery] GetBuyOrderDto getBuyOrderDto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.GetResponseBuyOrderDtoAsync(getBuyOrderDto, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseBuyOrderDto>>), (int)HttpStatusCode.OK)]
        public async PaginatedBuyOrderResponse GetAllBuyOrdersAsync([FromQuery] BuyOrderPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _buyOrderService.GetAllResponseBuyOrderDtosAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }

        [HttpGet("get-buyorders-and-processes/{workOrderId:int}")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<ResponseBuyOrderAndProcesses>>), (int)HttpStatusCode.OK)]
        public async BuyOrderAndProcessesResult GetAllBuyOrdersAsync(int workOrderId, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.GetBuyOrdersAndProcessesAsync(workOrderId, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Purchasing)]
        [HttpDelete("delete-buyOrder-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteBuyOrderResultDto>>), (int)HttpStatusCode.OK)]
        public async DeleteBuyOrdersResult DeleteBuyOrderCollectionAsync([FromBody] int[] buyOrderIds, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.DeleteBuyOrderCollectionAsync(buyOrderIds, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.BuyOrderStageActors)]
        [HttpPost("attach-file-to-buyOrder")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<string>>> AttachFileToBuyOrderAsync(
            [FromQuery] AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.AttachFileToBuyOrderAsync(attachFileDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.BuyOrderStageActors)]
        [HttpDelete("detach-file-from-buyOrder")]
        [ProducesResponseType(typeof(ResponseDto<bool>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<bool>>> DetachFileFromBuyOrderAsync(
            int buyOrderId, Guid fileId, CancellationToken cancellationToken)
        {
            return Ok(await _buyOrderService.DetachFileFromBuyOrderAsync(buyOrderId, documentId: fileId, cancellationToken: cancellationToken));
        }
    }
}
