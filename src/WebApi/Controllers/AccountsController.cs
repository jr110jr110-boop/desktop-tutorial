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
using Microsoft.AspNetCore.RateLimiting;
using Services.Identity.AccountServices;
using ViewModels.Identity.UserDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Controllers;
using WebApi.Extensions;

namespace Controllers
{
    /// <summary>
    /// Sign-in, passwords and user administration.
    /// ★ R1a: user administration (register, update, delete, roles, the full user list, password reset, approval of sensitive roles) is for the PMO only;
    /// the service checks the role again. A password is changed only by its owner with the current password.
    /// </summary>
    [Authorize]
    public class AccountsController : BaseApiController
    {
        private readonly IAccountService _accountService;

        public AccountsController(IAccountService accountService)
        {
            _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        }

        // ★ PMO only. A role the sensitive list names (FINANCEMANAGER, PAYROLL, CEO, AUDITOR, HR, SECRETARIAT) is not given until a second PMO user approves it.
        [HttpPost("register-user")]
        [Authorize(Roles = RoleConstants.PMO)]
        [ProducesResponseType(typeof(ResponseDto<ResponseLoginUserDto>), 200)]
        public async ValueTask<ActionResult<ResponseDto<ResponseUserDto>>> RegisterAsync(
            [FromBody] RegisterUserDto registerUserDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.RegisterAsync(registerUserDto, cancellationToken));
        }

        // R1a2 (M1): the calls that need no token (login, refresh-token, change-temporary-password, logout) are limited per client address (Identity:RateLimitPerMinute).
        [HttpPost("login")]
        [ProducesResponseType(typeof(ResponseDto<ResponseLoginUserDto>), 200)]
        [AllowAnonymous]
        [EnableRateLimiting(WebApi.Startup.IdentityRateLimitPolicy)]
        public async ValueTask<ActionResult<ResponseDto<ResponseLoginUserDto>>> LoginAsync(
            [FromBody] LoginUserDto loginUserDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.LoginAsync(loginUserDto, cancellationToken));
        }

        // Anonymous by design: the access token it replaces has usually expired, so the refresh token (signed, stored hashed, one use) is the proof.
        [HttpPost("refresh-token")]
        [AllowAnonymous]
        [EnableRateLimiting(WebApi.Startup.IdentityRateLimitPolicy)]
        [ProducesResponseType(typeof(ResponseDto<ResponseLoginUserDto>), 200)]
        public async ValueTask<ActionResult<ResponseDto<ResponseLoginUserDto>>> RefreshTokenAsync(
            [FromBody] RefreshTokenDto refreshTokenDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.RefreshTokenAsync(refreshTokenDto, cancellationToken));
        }

        // R1a2 (M6): the refresh token travels in the body, not in the address (addresses end up in server and proxy logs).
        // Anonymous by design: the access token has usually expired, so the refresh token is the proof; the service signs out the owner of the token.
        [AllowAnonymous]
        [HttpPost("logout")]
        [EnableRateLimiting(WebApi.Startup.IdentityRateLimitPolicy)]
        [ProducesResponseType(typeof(ResponseDto<bool>), 200)]
        public async Task<ActionResult<ResponseDto<bool>>> LogoutAsync([FromBody] LogoutDto logoutDto, CancellationToken cancellationToken)
        {
            var claimsIdentity = this.User?.Identity as ClaimsIdentity;
            var userIdValue = claimsIdentity?.FindFirst(ClaimTypes.UserData)?.Value;

            ResponseDto<bool> responseContract =
                await _accountService.LogoutAsync(userIdValue, logoutDto?.RefreshToken, cancellationToken);

            return Ok(responseContract);
        }

        // DEPRECATED (R1a2, M6): kept for one release so the front-end that was built before can still sign out. It puts the refresh token in the address, which
        // servers and proxies write to their logs; the front-end must call POST logout with {"refreshToken": ...} instead. This action will be removed in the next release.
        [AllowAnonymous]
        [HttpGet("logout/{refreshToken}")]
        [EnableRateLimiting(WebApi.Startup.IdentityRateLimitPolicy)]
        [ProducesResponseType(typeof(ResponseDto<bool>), 200)]
        public async Task<ActionResult<ResponseDto<bool>>> LogoutByAddressAsync(string refreshToken, CancellationToken cancellationToken)
        {
            var claimsIdentity = this.User?.Identity as ClaimsIdentity;
            var userIdValue = claimsIdentity?.FindFirst(ClaimTypes.UserData)?.Value;

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

        // The own record for everybody; another user's record (national code, birth date, phone) is for the PMO only (the service checks).
        [HttpGet("get-user-info")]
        [ProducesResponseType(typeof(ResponseDto<ResponseUserDto>), 200)]
        public async Task<ActionResult<ResponseDto<ResponseUserDto>>> GetUserInfoAsync(
            [FromQuery] GetUserDto getUserDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.GetResponseUserDtoByIdAsync(getUserDto, cancellationToken));
        }

        // Own password only, with the current password (the service refuses another user id). A PMO user sets a temporary password with admin-reset-password.
        [HttpPost("change-password")]
        [ProducesResponseType(typeof(ResponseDto<ResponseLoginUserDto>), 200)]
        public async ValueTask<ActionResult<ResponseDto<ResponseLoginUserDto>>> ChangePasswordAsync(
            [FromBody] ChangePasswordDto changePasswordDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.ChangePasswordAsync(changePasswordDto, cancellationToken));
        }

        // ★ PMO only. Sets a temporary password (given or made by the system); the user must change it at the next sign-in. No tokens are returned.
        [HttpPost("admin-reset-password")]
        [Authorize(Roles = RoleConstants.PMO)]
        [ProducesResponseType(typeof(ResponseDto<AdminResetPasswordResultDto>), 200)]
        public async ValueTask<ActionResult<ResponseDto<AdminResetPasswordResultDto>>> AdminResetPasswordAsync(
            [FromBody] AdminResetPasswordDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.AdminResetPasswordAsync(dto, cancellationToken));
        }

        // Anonymous by design: the user cannot sign in until the temporary password is changed, so the temporary password itself is the proof
        // (a wrong one counts toward the sign-in lock-out).
        [HttpPost("change-temporary-password")]
        [AllowAnonymous]
        [EnableRateLimiting(WebApi.Startup.IdentityRateLimitPolicy)]
        [ProducesResponseType(typeof(ResponseDto<ResponseLoginUserDto>), 200)]
        public async ValueTask<ActionResult<ResponseDto<ResponseLoginUserDto>>> ChangeTemporaryPasswordAsync(
            [FromBody] ChangeTemporaryPasswordDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.ChangeTemporaryPasswordAsync(dto, cancellationToken));
        }

        // ★ PMO only, and never the own roles (a PMO user may only step down from PMO himself, unless he is the last active one).
        // A sensitive role, and the removal of the PMO role, are only requested here; a second PMO user approves them (role-grants).
        // R1a2 (L-9): data is { roles, pending, grantId, grantIds } (it was the comma-joined roles string).
        [HttpPost("edit-user-roles")]
        [Authorize(Roles = RoleConstants.PMO)]
        [ProducesResponseType(typeof(ResponseDto<ChangeRoleResultDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ChangeRoleResultDto>>> EditRolesAsync(
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

        // ★ PMO only: the full record of every user. Pickers use users/lookup.
        [HttpGet("get-all-users")]
        [Authorize(Roles = RoleConstants.PMO)]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseUserDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<PaginatedResult<ResponseUserDto>>>> GetAllUsersAsync(
            [FromQuery] UserPaginationParams userPaginationParams, CancellationToken cancellationToken)
        {
            ResponseDto<PagedList<ResponseUserDto>> users = await _accountService.GetAllUsersAsync(userPaginationParams, cancellationToken);
            return Ok(users.ToPaginatedResultSafe());
        }

        // Every signed-in user: the slim list for pickers (id, user name, name). Same paging and search as get-all-users (Contains, Role, PageNumber, PageSize).
        [HttpGet("users/lookup")]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseUserLookupDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<PaginatedResult<ResponseUserLookupDto>>>> GetUserLookupAsync(
            [FromQuery] UserPaginationParams userPaginationParams, CancellationToken cancellationToken)
        {
            ResponseDto<PagedList<ResponseUserLookupDto>> users = await _accountService.GetUserLookupAsync(userPaginationParams, cancellationToken);
            return Ok(users.ToPaginatedResultSafe());
        }

        [HttpGet("get-engineer-users")]
        [ProducesResponseType(typeof(ResponseDto<ResponseUserShortListDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseUserShortListDto>>> GetEngineerUsersAsync(CancellationToken cancellationToken)
        {
            return Ok(await _accountService.GetEngineerUsersAsync(cancellationToken));
        }

        // ★ PMO only. A PMO user may correct his or her own details but not the own roles.
        [HttpPut("update-user")]
        [Authorize(Roles = RoleConstants.PMO)]
        [ProducesResponseType(typeof(ResponseDto<ResponseUserDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseUserDto>>> UpdateUserAsync(
            [FromBody] UpdateUserDto updateUserDto, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.UpdateUserAsync(updateUserDto, cancellationToken));
        }

        // ★ PMO only; not the own account.
        [HttpDelete("delete-user/{userId:int}")]
        [Authorize(Roles = RoleConstants.PMO)]
        [ProducesResponseType(typeof(ResponseDto<ResponseUserDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<bool>>> DeleteUserAsync(
            int userId, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.DeleteUserAsync(userId, cancellationToken));
        }

        // ★ PMO only.
        [HttpDelete("delete-user-collection")]
        [Authorize(Roles = RoleConstants.PMO)]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteUserResultDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<IEnumerable<DeleteUserResultDto>>>> DeleteUserCollectionAsync(
            int[] userIds, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.DeleteUserCollectionAsync(userIds, cancellationToken));
        }

        // The own photo for everybody, any photo for the PMO (the service checks).
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

        // ---- two-person approval of sensitive roles (★ PMO only; the approver is neither the one who asked nor the one who receives the role)

        [HttpGet("role-grants")]
        [Authorize(Roles = RoleConstants.PMO)]
        [ProducesResponseType(typeof(ResponseDto<List<RoleGrantDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<List<RoleGrantDto>>>> GetRoleGrantsAsync(CancellationToken cancellationToken)
        {
            return Ok(await _accountService.GetRoleGrantsAsync(cancellationToken));
        }

        [HttpPost("role-grants/{grantId:int}/approve")]
        [Authorize(Roles = RoleConstants.PMO)]
        [ProducesResponseType(typeof(ResponseDto<RoleGrantDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<RoleGrantDto>>> ApproveRoleGrantAsync(int grantId, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.ApproveRoleGrantAsync(grantId, cancellationToken));
        }

        [HttpPost("role-grants/{grantId:int}/reject")]
        [Authorize(Roles = RoleConstants.PMO)]
        [ProducesResponseType(typeof(ResponseDto<bool>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<bool>>> RejectRoleGrantAsync(int grantId, CancellationToken cancellationToken)
        {
            return Ok(await _accountService.RejectRoleGrantAsync(grantId, cancellationToken));
        }
    }
}
