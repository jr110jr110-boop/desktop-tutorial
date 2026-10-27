using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Common.Extentions;
using Common.GuardToolkit;
using Common.IdentityToolkit;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Identity.Constants;
using Entities.Model;
using Entities.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Contracts.Identity;
using Services.NasbNiro.Contracts;
using Services.Resources;
using Services.Services;
using Services.Token;
using ViewModels.Identity.UserDtos;
using ViewModels.NasbNiro;
using ViewModels.NasbNiro.Pagination;

namespace Services.Identity.AccountServices
{
    /// <summary>
    /// Sign-in, passwords and user administration.
    /// R1a (security review): user administration is for the PMO only (the controller says so and this service checks again),
    /// a password is changed only by its owner with the current password, an administrator can only set a temporary password,
    /// nobody changes his or her own roles, and a sensitive role needs the approval of a second PMO user.
    /// Every sign-in, password and role event is written to the append-only audit trail.
    /// </summary>
    public class AccountService : IAccountService, ISaveChangesAsync
    {
        private readonly IApplicationUserManager _userManager;
        private readonly IApplicationRoleManager _roleManager;
        private readonly IApplicationSignInManager _signInManager;
        private readonly ITokenFactoryService _tokenFactoryService;
        private readonly IUnitOfWork _uow;
        private readonly ITokenStoreService _tokenStoreService;
        private readonly ILogger<AccountService> _logger;
        private readonly IMapper _mapper;
        private readonly IDocumentService _documentService;
        private readonly IHttpContextAccessor _http;
        private readonly DbSet<UserRole> _userRole;
        public AccountService(
            IApplicationUserManager userManager,
            IApplicationRoleManager roleManager,
            IApplicationSignInManager signInManager,
            ITokenFactoryService tokenFactoryService,
            IUnitOfWork uow,
            ITokenStoreService tokenStoreService,
            ILogger<AccountService> logger,
            IMapper mapper,
            IDocumentService documentService,
            IHttpContextAccessor httpContextAccessor)
        {
            _userManager = userManager.ThrowIfNull(nameof(userManager));
            _roleManager = roleManager.ThrowIfNull(nameof(roleManager));
            _signInManager = signInManager.ThrowIfNull(nameof(signInManager));
            _tokenFactoryService = tokenFactoryService.ThrowIfNull(nameof(tokenFactoryService));
            _uow = uow.ThrowIfNull(nameof(uow));
            _tokenStoreService = tokenStoreService.ThrowIfNull(nameof(tokenStoreService));
            _logger = logger.ThrowIfNull(nameof(logger));
            _mapper = mapper.ThrowIfNull(nameof(mapper));
            _documentService = documentService.ThrowIfNull(nameof(documentService));
            _http = httpContextAccessor.ThrowIfNull(nameof(httpContextAccessor));
            _userRole = _uow.Set<UserRole>();
        }

        // ------------------------------------------------------------------------------------------ who is calling

        private ClaimsPrincipal Caller => _http.HttpContext?.User;

        /// <summary>Id of the signed-in user, 0 when nobody is signed in.</summary>
        private int Me => int.TryParse(Caller?.Identity?.GetUserId(), out int id) ? id : 0;

        private bool CallerIsPmo => Caller?.IsInRole(IdentityRules.Pmo) == true;

        private static ResponseDto<T> OnlyPmoAnswer<T>() => ResponseDtoHelper.Forbidden<T>(AccountServiceMessages.OnlyPmo);

        private const string SeatUserMessage = "کاربر سرویس صندلی هوش مصنوعی را فقط خود سامانه مدیریت می‌کند.";

        private static string NameOf(User user) => user == null ? "؟" : (string.IsNullOrWhiteSpace(user.UserName) ? "#" + user.Id : user.UserName);

        // ------------------------------------------------------------------------------------------ register

        public async ValueTask<ResponseDto<ResponseUserDto>> RegisterAsync(RegisterUserDto registerUserDto, CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<ResponseUserDto>();

            if (registerUserDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseUserDto>(AccountServiceMessages.NullDataReceived);

            // nobody may register a name that belongs to an AI seat
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(registerUserDto.UserName))
                return ResponseDtoHelper
                    .BadRequest<ResponseUserDto>(AccountServiceMessages.InvalidLoginData);

            var user = await _userManager
                .FindByNameAsync(registerUserDto.UserName)
                .WithCancellationTokenAsync(cancellationToken);

            if (user != null && user.IsDeleted == false)
                return ResponseDtoHelper
                    .BadRequest<ResponseUserDto>(AccountServiceMessages
                        .UserNameIsUsedBefore);

            int[] roleIds = registerUserDto.RoleIds ?? Array.Empty<int>();
            List<Role> roles = await _roleManager
                .Roles.Where(r => roleIds.Contains(r.Id)).ToListAsync(cancellationToken);

            user = new User
            {
                UserName = registerUserDto.UserName,
                Name = registerUserDto.Name,
                LastName = registerUserDto.LastName,
                Email = registerUserDto.Email,
                PhoneNumber = registerUserDto.PhoneNumber,
                BirthDate = registerUserDto.BirthDate,
                NationalCode = registerUserDto.NationalCode,
                EmailConfirmed = true,
                LockoutEnabled = true, // the sign-in lock-out applies to every account
                IsActive = true,
                IsDeleted = false
            };

            var createUserResult = await _userManager
                .CreateAsync(user, registerUserDto.Password)
                .WithCancellationTokenAsync(cancellationToken);

            if (!createUserResult.Succeeded)
            {
                _logger.LogError("Register user failed: {Errors}", createUserResult.DumpErrors());

                return ResponseDtoHelper
                   .BadRequest<ResponseUserDto>(AccountServiceMessages
                       .FailedToRegisterUser + createUserResult.DumpErrors());
            }

            RoleOutcome outcome = await ApplyRolesAsync(user, roles, cancellationToken);
            if (outcome.Error != null)
            {
                await _userManager.DeleteAsync(user).WithCancellationTokenAsync(cancellationToken);

                return ResponseDtoHelper
                    .BadRequest<ResponseUserDto>(outcome.Error);
            }

            IdentityAudit.Add(_uow, _http, Me, IdentityAudit.User, user.Id, IdentityAudit.Registered,
                $"ثبت کاربر «{user.UserName}»" + outcome.AuditText(),
                after: new { userName = user.UserName, roles = outcome.Added, pendingApproval = outcome.Pending });
            await _uow.SaveChangesAsync(cancellationToken);

            ResponseDto<ResponseUserDto> created = await GetResponseUserDtoByIdAsync(new GetUserDto { Id = user.Id }, cancellationToken);
            return outcome.Pending.Count == 0 ? created : ResponseDtoHelper.Ok(created.Data, outcome.PendingMessage());
        }

        // ------------------------------------------------------------------------------------------ sign in

        public async ValueTask<ResponseDto<ResponseLoginUserDto>> LoginAsync(LoginUserDto loginUserDto, CancellationToken cancellationToken)
        {
            if (loginUserDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.NullDataReceived);

            // the service users of the AI seats ("seat.*") are locked out for good; this check keeps them out even if a lockout were ever lifted
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(loginUserDto.UserName))
            {
                await RecordSignInAsync(0, null, IdentityAudit.Blocked, $"تلاش برای ورود با کاربر سرویس «{IdentityRules.Clip(loginUserDto.UserName, 64)}»", cancellationToken);
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.InvalidLoginData);
            }

            var user = await _userManager
                .FindByNameAsync(loginUserDto.UserName)
                .WithCancellationTokenAsync(cancellationToken);

            if (user is null)
            {
                // the same answer as a wrong password, so the sign-in page does not tell which names exist
                await RecordSignInAsync(0, null, IdentityAudit.Failure, $"ورود ناموفق: نام کاربری ناشناخته «{IdentityRules.Clip(loginUserDto.UserName, 64)}»", cancellationToken);
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.InvalidLoginData);
            }

            if (!user.IsActive)
            {
                await RecordSignInAsync(user.Id, user.Id, IdentityAudit.Blocked, $"ورود حساب غیرفعال «{NameOf(user)}»", cancellationToken);
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.AccountIsNotActive);
            }

            if (user.IsDeleted)
            {
                await RecordSignInAsync(user.Id, user.Id, IdentityAudit.Blocked, $"ورود حساب حذف‌شده «{NameOf(user)}»", cancellationToken);
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.AccountIsDeleted);
            }

            // lockoutOnFailure: true for everybody (R1a): after MaxFailedAccessAttempts wrong passwords the account is locked for DefaultLockoutTimeSpan.
            // CheckPasswordSignInAsync does not make the identity cookie that PasswordSignInAsync made; the API signs in with the JWT only.
            SignInResult signInResult = await _signInManager.CheckPasswordSignInAsync(
                  user,
                  loginUserDto.Password,
                  lockoutOnFailure: true)
                .WithCancellationTokenAsync(cancellationToken);

            if (signInResult.IsLockedOut)
            {
                await RecordSignInAsync(user.Id, user.Id, IdentityAudit.LockedOut, $"حساب «{NameOf(user)}» قفل است (ورودهای ناموفق پشت سر هم)", cancellationToken);
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.AccountIsLockedOut);
            }

            if (!signInResult.Succeeded)
            {
                await RecordSignInAsync(user.Id, user.Id, IdentityAudit.Failure, $"ورود ناموفق «{NameOf(user)}» (رمز عبور نادرست)", cancellationToken);
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.InvalidLoginData);
            }

            // the password is right but it is a temporary one set by an administrator: no tokens until the user chooses a new one
            if (await HasMustChangeFlagAsync(user.Id, cancellationToken))
            {
                await RecordSignInAsync(user.Id, user.Id, IdentityAudit.MustChange, $"ورود با رمز موقت «{NameOf(user)}»؛ تغییر رمز لازم است", cancellationToken);
                return MustChangeAnswer();
            }

            ResponseLoginUserDto login = await FinishSignInAsync(user, $"ورود موفق «{NameOf(user)}»", cancellationToken);
            return ResponseDtoHelper
                .Ok(login, AccountServiceMessages.LogedInSuccessfully);
        }

        /// <summary>The answer when the password must be changed first: success is false, the body carries the code the front-end reacts to.</summary>
        private static ResponseDto<ResponseLoginUserDto> MustChangeAnswer() =>
            new ResponseDto<ResponseLoginUserDto>(false, AccountServiceMessages.MustChangePassword,
                new ResponseLoginUserDto { Code = AccountServiceMessages.MustChangePasswordCode, MustChangePassword = true })
            .HasHttpStatusCode(HttpStatusCode.PreconditionRequired);

        /// <summary>Makes the tokens of a user whose password was checked, stores the refresh token and writes the sign-in row of the audit trail.</summary>
        private async Task<ResponseLoginUserDto> FinishSignInAsync(User user, string auditSummary, CancellationToken cancellationToken)
        {
            JwtTokensData token = await _tokenFactoryService
                .CreateJwtTokensAsync(user)
                .WithCancellationTokenAsync(cancellationToken);

            await _tokenStoreService.AddUserTokenAsync(
                user,
                token.RefreshTokenSerial,
                token.AccessToken,
                null)
                .WithCancellationTokenAsync(cancellationToken);

            IdentityAudit.Add(_uow, _http, user.Id, IdentityAudit.SignIn, user.Id, IdentityAudit.Success, auditSummary);
            await _uow.SaveChangesAsync(cancellationToken);

            user.LastLoggedIn = DateTime.Now;
            await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

            return new ResponseLoginUserDto()
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                AccessList = await AccessListAsync(user.Id, cancellationToken),
            };
        }

        /// <summary>
        /// The claims of the roles the user holds (by role id; the old LIKE on the role name did the same and could not be run on the in-memory test database).
        /// The sign-in and the refresh both return it, so a role change shows at the next refresh and not only at the next sign-in.
        /// </summary>
        private async Task<string[]> AccessListAsync(int userId, CancellationToken cancellationToken)
        {
            int[] roleIds = await _uow.Set<UserRole>().AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToArrayAsync(cancellationToken);

            return await _uow.Set<RoleClaim>().AsNoTracking()
                .Where(rc => roleIds.Contains(rc.RoleId))
                .Select(rc => rc.ClaimValue)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        }

        private async Task RecordSignInAsync(int userId, int? entityId, string action, string summary, CancellationToken cancellationToken)
        {
            IdentityAudit.Add(_uow, _http, userId, IdentityAudit.SignIn, entityId, action, summary);
            await _uow.SaveChangesAsync(cancellationToken);
        }

        // ------------------------------------------------------------------------------------------ refresh and sign out

        public async ValueTask<ResponseDto<ResponseLoginUserDto>> RefreshTokenAsync(RefreshTokenDto refreshTokenDto, CancellationToken cancellationToken)
        {
            if (refreshTokenDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.NullDataReceived);

            string refreshToken = refreshTokenDto.RefreshToken;

            if (string.IsNullOrWhiteSpace(refreshToken))
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(
                    AccountServiceMessages.InvalidRefreshToken);

            var storagetoken = await _tokenStoreService.FindTokenAsync(refreshToken);
            if (storagetoken is null)
                return ResponseDtoHelper.Unauthorized<ResponseLoginUserDto>(
                    AccountServiceMessages.InvalidRefreshToken);

            if (storagetoken.User == null || storagetoken.User.IsDeleted == true)
                return ResponseDtoHelper.NotFound<ResponseLoginUserDto>(
                    AccountServiceMessages.UserNotFound);

            // the refresh token is the proof here (the call is anonymous because the access token it replaces has usually expired):
            // a deactivated account, an AI seat or an account that must first choose a new password gets nothing
            if (!storagetoken.User.IsActive || global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(storagetoken.User.UserName))
                return ResponseDtoHelper.Unauthorized<ResponseLoginUserDto>(
                    AccountServiceMessages.AccountRefreshForbidden);

            if (await HasMustChangeFlagAsync(storagetoken.User.Id, cancellationToken))
                return MustChangeAnswer();

            var token = await _tokenFactoryService
                .CreateJwtTokensAsync(storagetoken.User)
                .WithCancellationTokenAsync(cancellationToken);

            await _tokenStoreService.AddUserTokenAsync(
                    storagetoken.User,
                    token.RefreshTokenSerial,
                    token.AccessToken,
                    _tokenFactoryService.GetRefreshTokenSerial(refreshToken))
                .WithCancellationTokenAsync(cancellationToken);

            await _uow.SaveChangesAsync(cancellationToken);

            ResponseLoginUserDto responseLoginUserDto
                = new ResponseLoginUserDto()
                {
                    AccessToken = token.AccessToken,
                    RefreshToken = token.RefreshToken,
                    AccessList = await AccessListAsync(storagetoken.User.Id, cancellationToken),
                };

            return ResponseDtoHelper.Ok(responseLoginUserDto);
        }

        public async ValueTask<ResponseDto<bool>> LogoutAsync(string userId, string refreshToken, CancellationToken cancellationToken)
        {
            await _tokenStoreService.RevokeUserBearerTokensAsync(userId, refreshToken).WithCancellationTokenAsync(cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper
                     .Ok(true, AccountServiceMessages.LogedOutSuccessfully);
        }

        // ------------------------------------------------------------------------------------------ passwords

        private enum PasswordCheck { Ok, Wrong, LockedOut }

        /// <summary>
        /// Checks a password and counts a wrong one toward the lock-out, the same way the sign-in does
        /// (so the change-password calls cannot be used to guess a password without limit).
        /// </summary>
        private async Task<PasswordCheck> CheckPasswordCountingFailuresAsync(User user, string password, CancellationToken cancellationToken)
        {
            if (await _userManager.IsLockedOutAsync(user).WithCancellationTokenAsync(cancellationToken))
                return PasswordCheck.LockedOut;

            if (await _userManager.CheckPasswordAsync(user, password).WithCancellationTokenAsync(cancellationToken))
            {
                await _userManager.ResetAccessFailedCountAsync(user).WithCancellationTokenAsync(cancellationToken);
                return PasswordCheck.Ok;
            }

            await _userManager.AccessFailedAsync(user).WithCancellationTokenAsync(cancellationToken);
            return await _userManager.IsLockedOutAsync(user).WithCancellationTokenAsync(cancellationToken)
                ? PasswordCheck.LockedOut
                : PasswordCheck.Wrong;
        }

        /// <summary>A user changes his or her own password; the current password is required. Other users' passwords are never changed here.</summary>
        public async ValueTask<ResponseDto<ResponseLoginUserDto>> ChangePasswordAsync(ChangePasswordDto changePasswordDto, CancellationToken cancellationToken)
        {
            if (changePasswordDto is null)
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (AccountServiceMessages.NullDataReceived);

            int me = Me;
            if (me <= 0)
                return ResponseDtoHelper.Unauthorized<ResponseLoginUserDto>(AccountServiceMessages.NotSignedIn);

            // the old call took any user id; now it is the caller's own id or nothing
            if (changePasswordDto.UserId.HasValue && changePasswordDto.UserId.Value != me)
            {
                IdentityAudit.Add(_uow, _http, me, IdentityAudit.Password, changePasswordDto.UserId.Value, IdentityAudit.ChangeRefused,
                    $"تلاش برای تغییر رمز عبور کاربر دیگر (شناسه {changePasswordDto.UserId.Value})");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper.Forbidden<ResponseLoginUserDto>(AccountServiceMessages.OnlyOwnPassword);
            }

            if (string.IsNullOrEmpty(changePasswordDto.CurrentPassword))
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.CurrentPasswordRequired);

            string problem = PasswordPolicy.Problem(changePasswordDto.NewPassword);
            if (problem != null)
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(problem);

            User user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == me, cancellationToken);
            if (user == null || user.IsDeleted == true || !user.IsActive)
                return ResponseDtoHelper.NotFound<ResponseLoginUserDto>
                         (AccountServiceMessages.UserNotFound);

            // the service users of the AI seats are managed by the system only
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(SeatUserMessage);

            PasswordCheck check = await CheckPasswordCountingFailuresAsync(user, changePasswordDto.CurrentPassword, cancellationToken);
            if (check != PasswordCheck.Ok)
            {
                IdentityAudit.Add(_uow, _http, me, IdentityAudit.Password, me, check == PasswordCheck.LockedOut ? IdentityAudit.LockedOut : IdentityAudit.ChangeRefused,
                    check == PasswordCheck.LockedOut ? $"تغییر رمز «{NameOf(user)}» رد شد: حساب قفل است" : $"تغییر رمز «{NameOf(user)}» رد شد: رمز فعلی نادرست");
                await _uow.SaveChangesAsync(cancellationToken);
                return check == PasswordCheck.LockedOut
                    ? ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.AccountIsLockedOut)
                    : ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.CurrentPasswordWrong);
            }

            if (changePasswordDto.CurrentPassword == changePasswordDto.NewPassword)
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.NewPasswordSameAsCurrent);

            IdentityResult result = await _userManager
                .ChangePasswordAsync(user, changePasswordDto.CurrentPassword, changePasswordDto.NewPassword)
                .WithCancellationTokenAsync(cancellationToken);

            if (!result.Succeeded)
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>
                        (AccountServiceMessages.ChangePasswordFailed + Environment.NewLine + result.DumpErrors());

            // other sessions of this user end with the old password; the caller gets fresh tokens
            await SetMustChangeFlagAsync(user.Id, false, cancellationToken);
            await _tokenStoreService.InvalidateUserTokensAsync(user.Id).WithCancellationTokenAsync(cancellationToken);
            IdentityAudit.Add(_uow, _http, me, IdentityAudit.Password, me, IdentityAudit.Changed, $"تغییر رمز عبور توسط خود کاربر «{NameOf(user)}»");
            await _uow.SaveChangesAsync(cancellationToken);

            ResponseLoginUserDto login = await FinishSignInAsync(user, $"ورود پس از تغییر رمز عبور «{NameOf(user)}»", cancellationToken);
            return ResponseDtoHelper.Ok(login, AccountServiceMessages.PasswordChangedSuccessfully);
        }

        /// <summary>
        /// A PMO user sets a temporary password for another user (never his or her own). The user cannot sign in with it
        /// until a new password is chosen, the old sessions end and a lock-out is lifted. No tokens are returned.
        /// </summary>
        public async ValueTask<ResponseDto<AdminResetPasswordResultDto>> AdminResetPasswordAsync(AdminResetPasswordDto dto, CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<AdminResetPasswordResultDto>();

            if (dto is null)
                return ResponseDtoHelper.BadRequest<AdminResetPasswordResultDto>(AccountServiceMessages.NullDataReceived);

            int me = Me;
            if (dto.UserId == me)
                return ResponseDtoHelper.BadRequest<AdminResetPasswordResultDto>(AccountServiceMessages.CannotResetOwnPassword);

            User user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == dto.UserId, cancellationToken);
            if (user == null || user.IsDeleted == true)
                return ResponseDtoHelper.NotFound<AdminResetPasswordResultDto>(AccountServiceMessages.UserNotFound);

            // the service users of the AI seats are managed by the system only
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.BadRequest<AdminResetPasswordResultDto>(SeatUserMessage);

            bool generated = string.IsNullOrWhiteSpace(dto.TemporaryPassword);
            string temporary = generated ? null : dto.TemporaryPassword;
            IdentityResult result = null;

            // a made-up password that the validators refuse (for example it holds the user name) is simply made again
            for (int attempt = 0; attempt < (generated ? 5 : 1); attempt++)
            {
                if (generated) temporary = IdentityRules.GenerateTemporaryPassword();
                string resetToken = await _userManager.GeneratePasswordResetTokenAsync(user).WithCancellationTokenAsync(cancellationToken);
                result = await _userManager.ResetPasswordAsync(user, resetToken, temporary).WithCancellationTokenAsync(cancellationToken);
                if (result.Succeeded) break;
            }

            if (result == null || !result.Succeeded)
                return ResponseDtoHelper.BadRequest<AdminResetPasswordResultDto>
                        (AccountServiceMessages.ChangePasswordFailed + Environment.NewLine + (result?.DumpErrors() ?? ""));

            await SetMustChangeFlagAsync(user.Id, true, cancellationToken);
            await _tokenStoreService.InvalidateUserTokensAsync(user.Id).WithCancellationTokenAsync(cancellationToken);
            // the reset also unlocks the account (a user who forgot the password usually locked it)
            await _userManager.SetLockoutEndDateAsync(user, null).WithCancellationTokenAsync(cancellationToken);
            await _userManager.ResetAccessFailedCountAsync(user).WithCancellationTokenAsync(cancellationToken);

            IdentityAudit.Add(_uow, _http, me, IdentityAudit.Password, user.Id, IdentityAudit.AdminReset,
                $"بازنشانی رمز عبور «{NameOf(user)}» توسط مدیر؛ رمز موقت است و در ورود بعدی باید تغییر کند");
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper.Ok(new AdminResetPasswordResultDto
            {
                UserId = user.Id,
                UserName = user.UserName,
                TemporaryPassword = generated ? temporary : null,
                MustChangePassword = true,
            }, AccountServiceMessages.PasswordResetSuccessfully);
        }

        /// <summary>
        /// The last step of a temporary password: the user proves it, chooses a new password and is signed in.
        /// Anonymous call; a wrong temporary password counts toward the lock-out like a wrong sign-in.
        /// </summary>
        public async ValueTask<ResponseDto<ResponseLoginUserDto>> ChangeTemporaryPasswordAsync(ChangeTemporaryPasswordDto dto, CancellationToken cancellationToken)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.UserName) || string.IsNullOrEmpty(dto.TemporaryPassword))
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.NullDataReceived);

            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(dto.UserName))
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.InvalidLoginData);

            User user = await _userManager.FindByNameAsync(dto.UserName).WithCancellationTokenAsync(cancellationToken);
            if (user == null)
            {
                await RecordSignInAsync(0, null, IdentityAudit.Failure, $"تغییر رمز موقت ناموفق: نام کاربری ناشناخته «{IdentityRules.Clip(dto.UserName, 64)}»", cancellationToken);
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.InvalidLoginData);
            }

            if (!user.IsActive)
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.AccountIsNotActive);
            if (user.IsDeleted)
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.AccountIsDeleted);

            PasswordCheck check = await CheckPasswordCountingFailuresAsync(user, dto.TemporaryPassword, cancellationToken);
            if (check == PasswordCheck.LockedOut)
            {
                await RecordSignInAsync(user.Id, user.Id, IdentityAudit.LockedOut, $"حساب «{NameOf(user)}» قفل است (تغییر رمز موقت)", cancellationToken);
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.AccountIsLockedOut);
            }
            if (check == PasswordCheck.Wrong)
            {
                await RecordSignInAsync(user.Id, user.Id, IdentityAudit.Failure, $"تغییر رمز موقت «{NameOf(user)}» ناموفق (رمز موقت نادرست)", cancellationToken);
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.InvalidLoginData);
            }

            if (!await HasMustChangeFlagAsync(user.Id, cancellationToken))
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.NoTemporaryPassword);

            string problem = PasswordPolicy.Problem(dto.NewPassword);
            if (problem != null)
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(problem);
            if (dto.TemporaryPassword == dto.NewPassword)
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.NewPasswordSameAsCurrent);

            IdentityResult result = await _userManager
                .ChangePasswordAsync(user, dto.TemporaryPassword, dto.NewPassword)
                .WithCancellationTokenAsync(cancellationToken);
            if (!result.Succeeded)
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>
                        (AccountServiceMessages.ChangePasswordFailed + Environment.NewLine + result.DumpErrors());

            await SetMustChangeFlagAsync(user.Id, false, cancellationToken);
            await _tokenStoreService.InvalidateUserTokensAsync(user.Id).WithCancellationTokenAsync(cancellationToken);
            IdentityAudit.Add(_uow, _http, user.Id, IdentityAudit.Password, user.Id, IdentityAudit.TempChanged, $"تعیین رمز جدید پس از رمز موقت «{NameOf(user)}»");
            await _uow.SaveChangesAsync(cancellationToken);

            ResponseLoginUserDto login = await FinishSignInAsync(user, $"ورود پس از تعیین رمز جدید «{NameOf(user)}»", cancellationToken);
            return ResponseDtoHelper.Ok(login, AccountServiceMessages.PasswordChangedSuccessfully);
        }

        // ------------------------------------------------------------------------------------------ the temporary-password flag (a claim of the user)

        private Task<bool> HasMustChangeFlagAsync(int userId, CancellationToken cancellationToken) =>
            _uow.Set<UserClaim>().AsNoTracking().AnyAsync(c => c.UserId == userId && c.ClaimType == IdentityRules.MustChangePasswordClaimType, cancellationToken);

        /// <summary>Sets or clears the flag; the caller saves.</summary>
        private async Task SetMustChangeFlagAsync(int userId, bool on, CancellationToken cancellationToken)
        {
            List<UserClaim> rows = await _uow.Set<UserClaim>()
                .Where(c => c.UserId == userId && c.ClaimType == IdentityRules.MustChangePasswordClaimType)
                .ToListAsync(cancellationToken);
            if (on && rows.Count == 0)
                _uow.Set<UserClaim>().Add(new UserClaim { UserId = userId, ClaimType = IdentityRules.MustChangePasswordClaimType, ClaimValue = "1" });
            else if (!on && rows.Count > 0)
                _uow.RemoveRange(rows);
        }

        // ------------------------------------------------------------------------------------------ roles

        private sealed class RoleOutcome
        {
            public List<string> Added { get; } = new List<string>();
            public List<string> Removed { get; } = new List<string>();
            public List<string> Pending { get; } = new List<string>();
            public List<string> AlreadyPending { get; } = new List<string>();
            public string Error { get; set; }

            public string AuditText()
            {
                var parts = new List<string>();
                if (Added.Count > 0) parts.Add("نقش‌های داده‌شده: " + string.Join("، ", Added));
                if (Removed.Count > 0) parts.Add("نقش‌های گرفته‌شده: " + string.Join("، ", Removed));
                if (Pending.Count > 0) parts.Add("در انتظار تأیید مدیر دوم: " + string.Join("، ", Pending));
                return parts.Count == 0 ? "" : " — " + string.Join("؛ ", parts);
            }

            public string PendingMessage() => AccountServiceMessages.SensitiveRolePending + string.Join("، ", Pending);
        }

        /// <summary>The open (not expired) requests of sensitive roles of a user; expired ones are not returned.</summary>
        private async Task<List<(UserClaim Row, PendingRoleGrant Grant)>> OpenGrantsOfAsync(int userId, CancellationToken cancellationToken)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            List<UserClaim> rows = await _uow.Set<UserClaim>()
                .Where(c => c.UserId == userId && c.ClaimType == IdentityRules.PendingRoleGrantClaimType)
                .ToListAsync(cancellationToken);
            var open = new List<(UserClaim, PendingRoleGrant)>();
            foreach (UserClaim row in rows)
                if (PendingRoleGrant.TryParse(row.ClaimValue, out PendingRoleGrant grant) && !grant.IsExpired(now)) open.Add((row, grant));
            return open;
        }

        /// <summary>
        /// Brings the roles of a user to the wanted list: roles are taken away at once, ordinary roles are given at once,
        /// sensitive roles become a request that a second PMO user must approve. Saves.
        /// </summary>
        private async Task<RoleOutcome> ApplyRolesAsync(User user, IList<Role> wanted, CancellationToken cancellationToken)
        {
            var outcome = new RoleOutcome();
            IList<string> currentNames = await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken);
            var open = await OpenGrantsOfAsync(user.Id, cancellationToken);

            IdentityRules.RolePlan plan = IdentityRules.PlanRoles(currentNames, wanted.Select(r => r.Name), open.Select(o => o.Grant.Role));

            if (plan.AddNow.Count > 0)
            {
                IdentityResult added = await _userManager.AddToRolesAsync(user, plan.AddNow).WithCancellationTokenAsync(cancellationToken);
                if (!added.Succeeded)
                {
                    outcome.Error = AccountServiceMessages.FailedToAssignRole + added.DumpErrors();
                    return outcome;
                }
                outcome.Added.AddRange(plan.AddNow);
            }

            if (plan.Remove.Count > 0)
            {
                IdentityResult removed = await _userManager.RemoveFromRolesAsync(user, plan.Remove).WithCancellationTokenAsync(cancellationToken);
                if (!removed.Succeeded)
                {
                    outcome.Error = AccountServiceMessages.FailedToAssignRole + removed.DumpErrors();
                    return outcome;
                }
                outcome.Removed.AddRange(plan.Remove);
            }

            DateTimeOffset now = DateTimeOffset.Now;
            foreach (string role in plan.NeedApproval)
            {
                var grant = new PendingRoleGrant { Role = role, RequestedBy = Me, RequestedAt = now };
                _uow.Set<UserClaim>().Add(new UserClaim { UserId = user.Id, ClaimType = IdentityRules.PendingRoleGrantClaimType, ClaimValue = grant.ToClaimValue() });
                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Roles, user.Id, IdentityAudit.GrantRequested,
                    $"درخواست نقش حساس {role} برای «{NameOf(user)}»؛ تا تأیید مدیر (PMO) دیگر اعمال نمی‌شود",
                    after: new { role, requestedBy = Me, expires = grant.ExpiresAt.ToString("O") });
                outcome.Pending.Add(role);
            }
            outcome.AlreadyPending.AddRange(plan.AlreadyPending);

            if (outcome.Added.Count > 0 || outcome.Removed.Count > 0)
                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Roles, user.Id, IdentityAudit.RolesChanged,
                    $"تغییر نقش‌های «{NameOf(user)}»" + outcome.AuditText(),
                    before: new { roles = currentNames }, after: new { added = outcome.Added, removed = outcome.Removed });

            await _uow.SaveChangesAsync(cancellationToken);
            return outcome;
        }

        public async ValueTask<ResponseDto<string>> ChangeRoleAsync(ChangeRoleDto changeRoleDto, CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<string>();

            if (changeRoleDto is null)
                return ResponseDtoHelper.BadRequest<string>
                    (AccountServiceMessages.NullDataReceived);

            User user = await _userManager
                .FindByIdAsync(changeRoleDto.UserId.ToString())
                .WithCancellationTokenAsync(cancellationToken);

            if (user is null)
                return ResponseDtoHelper.NotFound<string>
                    (AccountServiceMessages.UserNotFound);

            // the service users of the AI seats are managed by the system only
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.BadRequest<string>(SeatUserMessage);

            // nobody changes his or her own roles: another PMO user does it (and a sensitive role needs a third look anyway)
            if (user.Id == Me)
            {
                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Access, user.Id, IdentityAudit.Refused, $"تلاش برای تغییر نقش‌های خود «{NameOf(user)}»");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper.Forbidden<string>(AccountServiceMessages.CannotChangeOwnRoles);
            }

            int[] roleIds = changeRoleDto.RoleIds ?? Array.Empty<int>();
            List<Role> wanted = await _roleManager.Roles.Where(role => roleIds.Contains(role.Id)).ToListAsync(cancellationToken);

            RoleOutcome outcome = await ApplyRolesAsync(user, wanted, cancellationToken);
            if (outcome.Error != null)
                return ResponseDtoHelper.BadRequest<string>(outcome.Error);

            IList<string> userCurrentRoles = await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken);
            string message = outcome.Pending.Count == 0 ? AccountServiceMessages.RolesChangedSuccessfully : outcome.PendingMessage();
            return ResponseDtoHelper.Ok(string.Join(",", userCurrentRoles), message);
        }

        public async ValueTask<ResponseDto<IEnumerable<ResponseRole>>> GetAllRolesAsync(CancellationToken cancellationToken)
        {
            List<Role> roles = await _roleManager.GetAllCustomRolesAsync().WithCancellationTokenAsync(cancellationToken);

            return ResponseDtoHelper.Ok(roles.Select(role => new ResponseRole()
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description
            }));
        }

        // ------------------------------------------------------------------------------------------ two-person approval of sensitive roles

        public async ValueTask<ResponseDto<List<RoleGrantDto>>> GetRoleGrantsAsync(CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<List<RoleGrantDto>>();

            DateTimeOffset now = DateTimeOffset.Now;
            int me = Me;
            List<UserClaim> rows = await _uow.Set<UserClaim>().AsNoTracking()
                .Where(c => c.ClaimType == IdentityRules.PendingRoleGrantClaimType)
                .OrderBy(c => c.Id)
                .ToListAsync(cancellationToken);

            var parsed = new List<(UserClaim Row, PendingRoleGrant Grant)>();
            foreach (UserClaim row in rows)
                if (PendingRoleGrant.TryParse(row.ClaimValue, out PendingRoleGrant grant) && !grant.IsExpired(now)) parsed.Add((row, grant));

            int[] ids = parsed.Select(p => p.Row.UserId).Concat(parsed.Select(p => p.Grant.RequestedBy)).Distinct().ToArray();
            var users = await _userManager.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.UserName, u.Name, u.LastName, u.IsDeleted, u.IsActive })
                .ToListAsync(cancellationToken);
            string Display(int id)
            {
                var u = users.FirstOrDefault(x => x.Id == id);
                if (u == null) return "؟";
                string full = $"{u.Name} {u.LastName}".Trim();
                return full.Length == 0 ? u.UserName : full;
            }

            var list = parsed
                .Where(p => users.Any(u => u.Id == p.Row.UserId && !u.IsDeleted))
                .Select(p => new RoleGrantDto
                {
                    Id = p.Row.Id,
                    UserId = p.Row.UserId,
                    UserName = users.First(u => u.Id == p.Row.UserId).UserName,
                    DisplayName = Display(p.Row.UserId),
                    Role = p.Grant.Role,
                    RequestedByUserId = p.Grant.RequestedBy,
                    RequestedByName = Display(p.Grant.RequestedBy),
                    RequestedAt = p.Grant.RequestedAt,
                    ExpiresAt = p.Grant.ExpiresAt,
                    CanApprove = IdentityRules.ApprovalProblem(p.Grant.RequestedBy, me, p.Row.UserId) == null,
                })
                .ToList();
            return ResponseDtoHelper.Ok(list);
        }

        public async ValueTask<ResponseDto<RoleGrantDto>> ApproveRoleGrantAsync(int grantId, CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<RoleGrantDto>();

            UserClaim row = await _uow.Set<UserClaim>()
                .FirstOrDefaultAsync(c => c.Id == grantId && c.ClaimType == IdentityRules.PendingRoleGrantClaimType, cancellationToken);
            if (row == null || !PendingRoleGrant.TryParse(row.ClaimValue, out PendingRoleGrant grant))
                return ResponseDtoHelper.NotFound<RoleGrantDto>(AccountServiceMessages.GrantNotFound);

            User user = await _userManager.FindByIdAsync(row.UserId.ToString()).WithCancellationTokenAsync(cancellationToken);
            if (user == null || user.IsDeleted || global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.NotFound<RoleGrantDto>(AccountServiceMessages.UserNotFound);

            int me = Me;
            if (grant.IsExpired(DateTimeOffset.Now))
            {
                _uow.Set<UserClaim>().Remove(row);
                IdentityAudit.Add(_uow, _http, me, IdentityAudit.Roles, user.Id, IdentityAudit.GrantExpired, $"درخواست نقش {grant.Role} برای «{NameOf(user)}» منقضی شد");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper.BadRequest<RoleGrantDto>(AccountServiceMessages.GrantExpired);
            }

            string problem = IdentityRules.ApprovalProblem(grant.RequestedBy, me, user.Id);
            if (problem != null)
            {
                IdentityAudit.Add(_uow, _http, me, IdentityAudit.Access, user.Id, IdentityAudit.Refused,
                    $"تلاش ناروا برای تأیید نقش {grant.Role} برای «{NameOf(user)}» ({(problem == "SameApprover" ? "تأیید توسط درخواست‌کننده" : "تأیید توسط گیرنده‌ی نقش")})");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper.Forbidden<RoleGrantDto>(problem == "SameApprover" ? AccountServiceMessages.GrantSameApprover : AccountServiceMessages.GrantApproverIsTarget);
            }

            Role role = await _roleManager.FindByNameAsync(grant.Role).WithCancellationTokenAsync(cancellationToken);
            if (role == null)
                return ResponseDtoHelper.BadRequest<RoleGrantDto>(AccountServiceMessages.InvalidRoleDetected);

            if (!await _userManager.IsInRoleAsync(user, role.Name).WithCancellationTokenAsync(cancellationToken))
            {
                IdentityResult added = await _userManager.AddToRoleAsync(user, role.Name).WithCancellationTokenAsync(cancellationToken);
                if (!added.Succeeded)
                    return ResponseDtoHelper.BadRequest<RoleGrantDto>(AccountServiceMessages.FailedToAssignRole + added.DumpErrors());
            }

            _uow.Set<UserClaim>().Remove(row);
            IdentityAudit.Add(_uow, _http, me, IdentityAudit.Roles, user.Id, IdentityAudit.GrantApproved,
                $"تأیید نقش حساس {role.Name} برای «{NameOf(user)}» (درخواست‌کننده: کاربر {grant.RequestedBy}، تأییدکننده: کاربر {me})",
                before: new { role = role.Name, requestedBy = grant.RequestedBy }, after: new { role = role.Name, approvedBy = me });
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper.Ok(new RoleGrantDto
            {
                Id = grantId, UserId = user.Id, UserName = user.UserName, DisplayName = user.DisplayName, Role = role.Name,
                RequestedByUserId = grant.RequestedBy, RequestedAt = grant.RequestedAt, ExpiresAt = grant.ExpiresAt,
            }, AccountServiceMessages.GrantApproved);
        }

        public async ValueTask<ResponseDto<bool>> RejectRoleGrantAsync(int grantId, CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<bool>();

            UserClaim row = await _uow.Set<UserClaim>()
                .FirstOrDefaultAsync(c => c.Id == grantId && c.ClaimType == IdentityRules.PendingRoleGrantClaimType, cancellationToken);
            if (row == null || !PendingRoleGrant.TryParse(row.ClaimValue, out PendingRoleGrant grant))
                return ResponseDtoHelper.NotFound<bool>(AccountServiceMessages.GrantNotFound);

            User user = await _userManager.FindByIdAsync(row.UserId.ToString()).WithCancellationTokenAsync(cancellationToken);
            _uow.Set<UserClaim>().Remove(row);
            IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Roles, row.UserId, IdentityAudit.GrantRejected,
                $"رد یا پس‌گرفتن درخواست نقش {grant.Role} برای «{NameOf(user)}»",
                before: new { role = grant.Role, requestedBy = grant.RequestedBy });
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(true, AccountServiceMessages.GrantRejected);
        }

        // ------------------------------------------------------------------------------------------ reading users

        public async ValueTask<ResponseDto<ResponseUserDto>> GetUserInfoAsync(CancellationToken cancellationToken)
        {
            string currentUserId = _userManager.GetCurrentUserId();
            int userId = int.Parse(currentUserId);

            ResponseDto<ResponseUserDto> result = await LoadUserDtoAsync(userId, cancellationToken);

            if (result.Data is null)
                return ResponseDtoHelper.NotFound<ResponseUserDto>
                    (AccountServiceMessages.UserNotFound);
            return result;
        }

        public async Task<ResponseDto<ResponseDashboardUserDto>> GetUserDashboardInfoAsync(CancellationToken cancellationToken)
        {
            string currentUserId = _userManager.GetCurrentUserId();
            int userId = int.Parse(currentUserId);

            ResponseDashboardUserDto dashboardUser =
                await _userManager
                .Users
                .Where(x => x.Id == userId)
                .ProjectTo<ResponseDashboardUserDto>(_mapper.ConfigurationProvider)
                .SingleOrDefaultAsync(cancellationToken);

            if (dashboardUser is null)
                return ResponseDtoHelper.NotFound<ResponseDashboardUserDto>
                    (AccountServiceMessages.UserNotFound);

            return ResponseDtoHelper.Ok(dashboardUser);
        }

        /// <summary>The full user list (national code, birth date, phone, e-mail, lock-out state): PMO only. Pickers use <see cref="GetUserLookupAsync"/>.</summary>
        public async ValueTask<ResponseDto<PagedList<ResponseUserDto>>> GetAllUsersAsync(
            UserPaginationParams userPaginationParams, CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<PagedList<ResponseUserDto>>();

            userPaginationParams ??= new UserPaginationParams();

            // the service users of the AI seats are not in the user list
            IQueryable<User> users = _userManager.Users.Where(u => !u.UserName.StartsWith("seat."));

            if (userPaginationParams.IsDeleted.HasValue)
                users = users.Where(u => u.IsDeleted == userPaginationParams.IsDeleted);

            if (userPaginationParams.Id.HasValue)
                users = users.Where(u => u.Id == userPaginationParams.Id);


            if (!string.IsNullOrEmpty(userPaginationParams.Contains))
            {
                string[] strArray = userPaginationParams.Contains.Split(' ');

                users = users.Where(u =>
                    u.Name != null && strArray.Any(str => u.Name.Contains(str)) ||
                    u.Email != null && strArray.Any(str => u.Email.Contains(str)) ||
                    u.UserName != null && strArray.Any(str => u.UserName.Contains(str)) ||
                    u.PhoneNumber != null && strArray.Any(str => u.PhoneNumber.Contains(str)) ||
                    u.LastName != null && strArray.Any(str => u.LastName.Contains(str)));
            }

            if (!string.IsNullOrEmpty(userPaginationParams.Role))
            {
                users = from user in users
                        where user.Roles.Any(x => EF.Functions.Like(x.Role.Name, $"%{userPaginationParams.Role}%"))
                        select user;
            }

            IQueryable<ResponseUserDto> result;

            result = users.ProjectTo<ResponseUserDto>(_mapper.ConfigurationProvider);

            Task<PagedList<ResponseUserDto>> data = PagedList<ResponseUserDto>
                            .CreateAsync(result,
                                         userPaginationParams.PageNumber,
                                         userPaginationParams.PageSize,
                                         cancellationToken);


            return ResponseDtoHelper.Ok(await data);
        }

        /// <summary>
        /// The slim list for pickers (id, user name, name): every signed-in user may read it. Only active users, no personal data, no role.
        /// A role filter is accepted, but not for the sensitive roles and the PMO (who holds them is not for everyone to ask).
        /// </summary>
        public async ValueTask<ResponseDto<PagedList<ResponseUserLookupDto>>> GetUserLookupAsync(
            UserPaginationParams userPaginationParams, CancellationToken cancellationToken)
        {
            if (Me <= 0)
                return ResponseDtoHelper.Unauthorized<PagedList<ResponseUserLookupDto>>(AccountServiceMessages.NotSignedIn);

            userPaginationParams ??= new UserPaginationParams();

            IQueryable<User> users = _userManager.Users
                .Where(u => !u.IsDeleted && u.IsActive && !u.UserName.StartsWith("seat."));

            if (userPaginationParams.Id.HasValue)
                users = users.Where(u => u.Id == userPaginationParams.Id);

            if (!string.IsNullOrWhiteSpace(userPaginationParams.Contains))
            {
                // every typed word must appear in the first name, the last name or the user name ("ali rez" finds Ali Rezaei)
                foreach (string typed in userPaginationParams.Contains.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
                {
                    string word = typed;
                    users = users.Where(u =>
                        (u.Name != null && u.Name.Contains(word)) ||
                        (u.LastName != null && u.LastName.Contains(word)) ||
                        (u.UserName != null && u.UserName.Contains(word)));
                }
            }

            if (!string.IsNullOrWhiteSpace(userPaginationParams.Role))
            {
                string roleKey = userPaginationParams.Role.Trim().ToUpperInvariant();
                if (!CallerIsPmo && (roleKey == IdentityRules.Pmo || IdentityRules.IsSensitiveRole(roleKey)))
                    users = users.Where(u => false);
                else
                    users = users.Where(u => u.Roles.Any(x => x.Role.NormalizedName == roleKey));
            }

            IQueryable<ResponseUserLookupDto> result = users
                .OrderBy(u => u.LastName).ThenBy(u => u.Name).ThenBy(u => u.Id)
                .Select(u => new ResponseUserLookupDto { Id = u.Id, UserName = u.UserName, Name = u.Name, LastName = u.LastName })
                .Take(1000);

            PagedList<ResponseUserLookupDto> page = await PagedList<ResponseUserLookupDto>
                .CreateAsync(result, userPaginationParams.PageNumber, userPaginationParams.PageSize, cancellationToken);
            return ResponseDtoHelper.Ok(page);
        }

        public async ValueTask<ResponseDto<ResponseUserShortListDto>> GetEngineerUsersAsync(CancellationToken cancellationToken)
        {
            IQueryable<UserRole> _userRoleQueryable = _userRole.Include(role => role.User).Where(role => role.RoleId == 8);

            List<ResponseUserShortDto> resultQuery = await _userRoleQueryable
               .Select(role => new ResponseUserShortDto()
               {
                   Id = role.User.Id,
                   DisplayName = role.User.DisplayName
               }).ToListAsync();

            return ResponseDtoHelper.Ok(new ResponseUserShortListDto() { Success = true, Message = "", Data = resultQuery });
        }

        // ------------------------------------------------------------------------------------------ update and delete

        public async ValueTask<ResponseDto<ResponseUserDto>> UpdateUserAsync(UpdateUserDto updateUserDto, CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<ResponseUserDto>();

            if (updateUserDto is null)
                return ResponseDtoHelper.BadRequest<ResponseUserDto>(NasbNiroMessages.InvalidData);

            User user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == updateUserDto.Id, cancellationToken);

            if (user is null)
                return ResponseDtoHelper
                    .NotFound<ResponseUserDto>("کاربری با این شناسه یافت نشد.");

            // the service users of the AI seats are managed by the system only
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.BadRequest<ResponseUserDto>(SeatUserMessage);

            int[] roleIds = updateUserDto.RoleIds ?? Array.Empty<int>();
            List<Role> wanted = await _roleManager.Roles.Where(role => roleIds.Contains(role.Id)).ToListAsync(cancellationToken);

            // a PMO user may correct his or her own details, but not the list of roles; this is decided before anything is changed
            bool isSelf = user.Id == Me;
            if (isSelf)
            {
                IList<string> mine = await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken);
                IdentityRules.RolePlan plan = IdentityRules.PlanRoles(mine, wanted.Select(r => r.Name), Enumerable.Empty<string>());
                if (!plan.IsEmpty)
                {
                    IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Access, user.Id, IdentityAudit.Refused, $"تلاش برای تغییر نقش‌های خود «{NameOf(user)}» از راه ویرایش کاربر");
                    await _uow.SaveChangesAsync(cancellationToken);
                    return ResponseDtoHelper.Forbidden<ResponseUserDto>(AccountServiceMessages.CannotChangeOwnRoles);
                }
            }

            _mapper.Map(updateUserDto, user);

            IdentityResult result = await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

            if (result.Succeeded)
            {
                string message = null;
                if (!isSelf)
                {
                    RoleOutcome outcome = await ApplyRolesAsync(user, wanted, cancellationToken);
                    if (outcome.Error != null)
                        return ResponseDtoHelper.BadRequest<ResponseUserDto>(outcome.Error);
                    message = outcome.Pending.Count == 0
                        ? string.Join(",", await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken))
                        : outcome.PendingMessage();
                }

                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.User, user.Id, IdentityAudit.Updated, $"ویرایش کاربر «{NameOf(user)}»");
                await _uow.SaveChangesAsync(cancellationToken);

                return ResponseDtoHelper
                   .Ok((await LoadUserDtoAsync(user.Id, cancellationToken)).Data, message);
            }


            return ResponseDtoHelper
                    .BadRequest<ResponseUserDto>(result.DumpErrors());
        }

        /// <summary>One user as the screens show it: the signed-in user's own record, or any user for the PMO.</summary>
        public async ValueTask<ResponseDto<ResponseUserDto>> GetResponseUserDtoByIdAsync(GetUserDto getUserDto, CancellationToken cancellationToken)
        {
            if (getUserDto is null)
                return ResponseDtoHelper.BadRequest<ResponseUserDto>(NasbNiroMessages.InvalidData);

            if (getUserDto.Id != Me && !CallerIsPmo)
                return ResponseDtoHelper.Forbidden<ResponseUserDto>(AccountServiceMessages.OnlyOwnProfile);

            return await LoadUserDtoAsync(getUserDto.Id, cancellationToken);
        }

        private async Task<ResponseDto<ResponseUserDto>> LoadUserDtoAsync(int userId, CancellationToken cancellationToken)
        {
            IQueryable<User> query = _userManager.Users.Where(u => !u.UserName.StartsWith("seat."));
            IQueryable<ResponseUserDto> result;

            result = query.ProjectTo<ResponseUserDto>(_mapper.ConfigurationProvider);

            return ResponseDtoHelper.Ok(await
                result.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken));
        }

        public async Task<ResponseDto<bool>> DeleteUserAsync(int userId, CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<bool>();

            User user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user is null)
                return ResponseDtoHelper
                    .NotFound<bool>("کاربری با این شناسه یافت نشد.");

            // the service users of the AI seats are managed by the system only
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.BadRequest<bool>(SeatUserMessage);

            // a PMO user cannot delete his or her own account, so the last administrator is never removed
            if (user.Id == Me)
                return ResponseDtoHelper.BadRequest<bool>(AccountServiceMessages.CannotDeleteSelf);

            if (user.IsDeleted == true)
                return ResponseDtoHelper
                    .BadRequest<bool>("این کاربر قبلا حذف شده است.");

            user.IsDeleted = true;
            IdentityResult result = await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

            if (result.Succeeded)
            {
                // the deleted user's sessions end now (an access token already given expires within the hour)
                await _tokenStoreService.InvalidateUserTokensAsync(user.Id).WithCancellationTokenAsync(cancellationToken);
                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.User, user.Id, IdentityAudit.Deleted, $"حذف کاربر «{NameOf(user)}»");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper
                    .Ok(true, "کاربر حذف شد.");
            }

            return ResponseDtoHelper
                    .BadRequest<bool>(result.DumpErrors());
        }

        public async ValueTask<ResponseDto<IEnumerable<DeleteUserResultDto>>> DeleteUserCollectionAsync(IEnumerable<int> userIds, CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<IEnumerable<DeleteUserResultDto>>();

            if (userIds is null)
                return ResponseDtoHelper.BadRequest<IEnumerable<DeleteUserResultDto>>(NasbNiroMessages.InvalidData);

            List<DeleteUserResultDto> deleteUserResultDtos = new List<DeleteUserResultDto>();

            foreach (int userId in userIds)
            {
                bool exception = false;
                ResponseDto<bool> result = null;

                try
                {
                    result = await this.DeleteUserAsync(userId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    exception = true;
                }

                if (exception)
                {
                    deleteUserResultDtos.Add(new DeleteUserResultDto()
                    {
                        Id = userId,
                        Success = false,
                        Message = "حذف کاربر با خطا مواجه شد."
                    });

                    continue;
                }

                DeleteUserResultDto deleteContractResultDto
                    = new DeleteUserResultDto() { Id = userId, Success = result.Success, Message = result.Message };

                deleteUserResultDtos.Add(deleteContractResultDto);
            }

            return ResponseDtoHelper
                .Ok(deleteUserResultDtos.AsEnumerable());
        }

        // ------------------------------------------------------------------------------------------ profile photo (own photo, or any for the PMO)

        public async ValueTask<ResponseDto<string>> AttachProfilePhotoAsync(AttachProfilePhotoDto dto, CancellationToken cancellationToken)
        {
            if (dto is null)
                return ResponseDtoHelper.BadRequest<string>(AccountServiceMessages.NullDataReceived);

            if (dto.UserId != Me && !CallerIsPmo)
                return ResponseDtoHelper.Forbidden<string>(AccountServiceMessages.OnlyOwnProfile);

            User user = await _userManager
                .Users
                .Where(x => x.Id == dto.UserId)
                .SingleOrDefaultAsync(x => !x.IsDeleted, cancellationToken);

            if (user is null || user.IsDeleted)
                return ResponseDtoHelper.NotFound<string>(UserMessages.UserNotFound);

            if (user.ProfilePhotoId.HasValue)
            {
                try
                {
                    await _documentService.DeleteDocumentAsync(user.ProfilePhotoId.Value).WithCancellationTokenAsync(cancellationToken);
                }
                catch (Exception ex)
                {

                    _logger.LogError(ex.Message, ex);
                }
            }

            Guid documentId = await _documentService.UploadDocumentAsync(dto).WithCancellationTokenAsync(cancellationToken);

            user.ProfilePhotoId = documentId;

            IdentityResult result = await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

            if (result.Succeeded)
            {
                return ResponseDtoHelper.Ok(documentId.ToString());
            }

            return ResponseDtoHelper.BadRequest<string>(result.DumpErrors());
        }

        public async ValueTask<ResponseDto<bool>> DetachUserProfilePhotoAsync(int userId, CancellationToken cancellationToken)
        {
            if (userId != Me && !CallerIsPmo)
                return ResponseDtoHelper.Forbidden<bool>(AccountServiceMessages.OnlyOwnProfile);

            User user = await _userManager
                .Users
                .SingleOrDefaultAsync(
                    x => !x.IsDeleted && x.Id == userId,
                    cancellationToken);

            if (user is null)
                return ResponseDtoHelper.NotFound<bool>(UserMessages.UserNotFound);

            if (user.ProfilePhotoId.HasValue)
            {
                bool deleteDocumentResult = await _documentService.DeleteDocumentAsync(
                    user.ProfilePhotoId.Value).WithCancellationTokenAsync(cancellationToken);

                user.ProfilePhotoId = null;
                await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

                return ResponseDtoHelper.Ok(true);
            }
            else
            {
                return ResponseDtoHelper.NotFound<bool>(UserMessages.NoProfilePhotosFound);
            }
        }

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken)
        {
            return await _uow.SaveChangesAsync(cancellationToken) != 0;
        }
    }
}
