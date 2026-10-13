using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Identity.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Identity.AccountServices;
using ViewModels.Identity.UserDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Controllers;
using WebApi.Extensions;

namespace Controllers
{
    [Authorize]
    public class AccountsController : BaseApiController
    {
        private readonly IAccountService _accountService;

        public AccountsController(IAccountService accountService)
        {
            _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        }

        [HttpPost("register-user")]
        [ProducesResponseType(typeof(ResponseDto<ResponseLoginUserDto>), 200)]
        public async ValueTask<ActionResult<ResponseDto<ResponseUserDto>>> RegisterAsync(
            [FromBody] RegisterUserDto registerUserDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.RegisterAsync(registerUserDto, cancellationToken));
        }

        [HttpPost("login")]
        [ProducesResponseType(typeof(ResponseDto<ResponseLoginUserDto>), 200)]
        [AllowAnonymous]
        public async ValueTask<ActionResult<ResponseDto<ResponseLoginUserDto>>> LoginAsync(
            [FromBody] LoginUserDto loginUserDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.LoginAsync(loginUserDto, cancellationToken));
        }

        [HttpPost("refresh-token")]
        [ProducesResponseType(typeof(ResponseDto<ResponseLoginUserDto>), 200)]
        public async ValueTask<ActionResult<ResponseDto<ResponseLoginUserDto>>> RefreshTokenAsync(
            [FromBody] RefreshTokenDto refreshTokenDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.RefreshTokenAsync(refreshTokenDto, cancellationToken));
        }

        [AllowAnonymous]
        [HttpGet("logout/{refreshToken}")]
        [ProducesResponseType(typeof(ResponseDto<bool>), 200)]
        public async Task<ActionResult<ResponseDto<bool>>> LogoutAsync(string refreshToken, CancellationToken cancellationToken)
        {
            var claimsIdentity = this.User.Identity as ClaimsIdentity;
            var userIdValue = claimsIdentity.FindFirst(ClaimTypes.UserData)?.Value;

            ResponseDto<bool> responseContract =
                await _accountService.LogoutAsync(userIdValue, refreshToken, cancellationToken);

            return Ok(responseContract);
        }

        [HttpGet("get-current-user-info")]
        [ProducesResponseType(typeof(ResponseDto<ResponseUserDto>), 200)]
        public async Task<ActionResult<ResponseDto<ResponseUserDto>>> GetUserInfoAsync(CancellationToken cancellationToken)
        {
            return Ok(await _accountService.GetUserInfoAsync(cancellationToken));
        }

        [HttpGet("get-user-info")]
        [ProducesResponseType(typeof(ResponseDto<ResponseUserDto>), 200)]
        public async Task<ActionResult<ResponseDto<ResponseUserDto>>> GetUserInfoAsync(
            [FromQuery] GetUserDto getUserDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.GetResponseUserDtoByIdAsync(getUserDto, cancellationToken));
        }

        [HttpPost("change-password")]
        [ProducesResponseType(typeof(ResponseDto<ResponseLoginUserDto>), 200)]
        public async ValueTask<ActionResult<ResponseDto<ResponseLoginUserDto>>> ChangePasswordAsync(
            [FromBody] ChangePasswordDto changePasswordDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.ChangePasswordAsync(changePasswordDto, cancellationToken));
        }

        [HttpPost("edit-user-roles")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<string>>> EditRolesAsync(
            ChangeRoleDto changeRoleDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.ChangeRoleAsync(changeRoleDto, cancellationToken));
        }

        [HttpPost("get-all-roles")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<ResponseRole>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<IEnumerable<ResponseRole>>>> GetAllRolesAsync(CancellationToken cancellationToken)
        {
            return Ok(await _accountService.GetAllRolesAsync(cancellationToken));
        }

        [HttpGet("get-all-users")]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseUserDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<PaginatedResult<ResponseUserDto>>>> GetAllUsersAsync(
            [FromQuery] UserPaginationParams userPaginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _accountService.GetAllUsersAsync(userPaginationParams, cancellationToken)).ToPaginatedResult());
        }

        [HttpGet("get-engineer-users")]
        [ProducesResponseType(typeof(ResponseDto<ResponseUserShortListDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseUserShortListDto>>> GetEngineerUsersAsync(CancellationToken cancellationToken)
        {
            return Ok(await _accountService.GetEngineerUsersAsync(cancellationToken));
        }

        [HttpPut("update-user")]
        [ProducesResponseType(typeof(ResponseDto<ResponseUserDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseUserDto>>> UpdateUserAsync(
            [FromBody] UpdateUserDto updateUserDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.UpdateUserAsync(updateUserDto, cancellationToken));
        }

        [HttpDelete("delete-user/{userId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseUserDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<bool>>> DeleteUserAsync(
            int userId, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.DeleteUserAsync(userId, cancellationToken));
        }

        [HttpDelete("delete-user-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteUserResultDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<IEnumerable<DeleteUserResultDto>>>> DeleteUserCollectionAsync(
            int[] userIds, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.DeleteUserCollectionAsync(userIds, cancellationToken));
        }

        [HttpPost("attach-profile-photo")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<string>>> AttachProfilePhotoAsync(
            [FromQuery] AttachProfilePhotoDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.AttachProfilePhotoAsync(dto, cancellationToken));
        }

        [HttpDelete("detach-profile-photo/{userId:int}")]
        [ProducesResponseType(typeof(ResponseDto<bool>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<bool>>> DetachProfilePhotoAsync(
            int userId, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.DetachUserProfilePhotoAsync(userId, cancellationToken));
        }
    }
}