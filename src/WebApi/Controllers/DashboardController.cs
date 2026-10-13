using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Identity.AccountServices;
using Services.NasbNiro.Contracts;
using ViewModels.Identity.UserDtos;
using ViewModels.NasbNiro;
using ViewModels.NasbNiro.AssemblyDtos;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.DashboardDtos;
using ViewModels.NasbNiro.ProcessDtos;

namespace WebApi.Controllers
{
    [Authorize]
    public class DashboardController : BaseApiController
    {
        private readonly ICardBoardService _cardBoardService;
        private readonly IAccountService _accountService;

        public DashboardController(ICardBoardService cardBoardService, IAccountService accountService)
        {
            _cardBoardService = cardBoardService ?? throw new System.ArgumentNullException(nameof(cardBoardService));
            _accountService = accountService ?? throw new System.ArgumentNullException(nameof(accountService));
        }

        [HttpGet("get-dashboard-info")]
        [ProducesResponseType(typeof(ResponseDto<ResponseUserDto>), 200)]
        public async Task<ActionResult<ResponseDto<ResponseDashboardDto>>> GetDashboardInfoAsync(
            CancellationToken cancellationToken)
        {
            List<ResponseDashboardTitleValue> titleValues = new List<ResponseDashboardTitleValue>();

            ResponseDto<ResponseDashboardUserDto> userInfo =
                await _accountService.GetUserDashboardInfoAsync(cancellationToken);

            titleValues.Add(new ResponseDashboardTitleValue()
            {
                Type = "userinfo",
                Title = userInfo.Data.FullName,
                Value = userInfo.Data.Roles.FirstOrDefault()?.Name
            });

            ResponseDto<ResponseDashboardEntityDto<ResponseBuyOrderDto>> userBuyOrders =
                await _cardBoardService.GetDashboardBuyOrdersAsync(cancellationToken);

            titleValues.Add(new ResponseDashboardTitleValue()
            {
                Type = "buyorder",
                Title = "دستور خرید",
                Value = userBuyOrders.Data?.TotalCount ?? 0
            });

            ResponseDto<ResponseDashboardEntityDto<ResponseProcessDashboardDto>> userProcesses =
                await _cardBoardService.GetUserDashboardProcessesAsync(cancellationToken);

            titleValues.Add(new ResponseDashboardTitleValue()
            {
                Type = "process",
                Title = "فرآیند",
                Value = userProcesses.Data?.TotalCount ?? 0
            });

            ResponseDto<ResponseDashboardEntityDto<ResponseAssemblyDashboardDto>> userAssemblies =
              await _cardBoardService.GetUserDashboardAssembliesAsync(cancellationToken);

            titleValues.Add(new ResponseDashboardTitleValue()
            {
                Type = "assembly",
                Title = "مونتاژ",
                Value = userAssemblies.Data?.TotalCount ?? 0
            });

            ResponseDashboardDto data = new ResponseDashboardDto()
            {
                TitleValues = titleValues,
                UserBuyOrders = userBuyOrders.Data?.Entities,
                UserProcesses = userProcesses.Data?.Entities,
                UserAssemblies = userAssemblies.Data?.Entities
            };

            return Ok(ResponseDtoHelper.Ok(data));
        }
    }
}
