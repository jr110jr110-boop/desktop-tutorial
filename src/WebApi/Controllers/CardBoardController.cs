using System.Net;
using System.Threading;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels;
using ViewModels.NasbNiro.AssemblyDtos;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcessDtos;
using WebApi.Extensions;
using PaginatedAssembliesResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.Pagination.PaginatedResult
                <ViewModels.NasbNiro.AssemblyDtos.ResponseAssemblyDto>>>>;
using PaginatedBuyOrdersResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.Pagination.PaginatedResult
                <ViewModels.NasbNiro.BuyOrderDtos.ResponseBuyOrderCardBoardDto>>>>;
using PaginatedProcessesResult = System.Threading.Tasks.ValueTask
    <Microsoft.AspNetCore.Mvc.ActionResult
        <Common.Results.ResponseDto
            <ViewModels.NasbNiro.Pagination.PaginatedResult
                <ViewModels.NasbNiro.ProcessDtos.ResponseProcessCardBoardDto>>>>;

namespace WebApi.Controllers
{
    [Authorize]
    public class CardBoardController : BaseApiController
    {
        private readonly ICardBoardService _cardBoardService;

        public CardBoardController(ICardBoardService cardBoardService)
        {
            _cardBoardService = cardBoardService ?? throw new System.ArgumentNullException(nameof(cardBoardService));
        }

        [HttpGet("get-all-user-buyorders")]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseBuyOrderCardBoardDto>>), (int)HttpStatusCode.OK)]
        public async PaginatedBuyOrdersResult GetAllUserBuyOrdersAsync(
            [FromQuery] UserBuyOrdersPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _cardBoardService.GetAllUserBuyOrdersAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }

        [HttpGet("get-all-user-processes")]
        [ProducesResponseType(typeof(ResponseDto<ResponseObjects>), (int)HttpStatusCode.OK)]
        public async PaginatedProcessesResult GetAllUserProcesssAsync(
            [FromQuery] UserProcessesPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _cardBoardService.GetAllUserProcessesAsync(paginationParams, cancellationToken)));
        }

        [HttpGet("get-all-user-assemblies")]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseAssemblyDto>>), (int)HttpStatusCode.OK)]
        public async PaginatedAssembliesResult GetAllUserAssembliesAsync(
            [FromQuery] UserAssembliesPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _cardBoardService.GetAllUserAssembliesAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }
    }
}
