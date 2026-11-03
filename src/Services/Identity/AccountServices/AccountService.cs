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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services.Contracts;
using Services.Contracts.Identity;
using Services.NasbNiro.Contracts;
using Services.Resources;
using Services.Services;
using Services.Token;
using ViewModels.Identity.Settings;
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
    /// R1a2 (second review): the PMO role is sensitive too (one bootstrap exception), the approver must be independent of the requester, the password reset of a PMO or
    /// sensitive-role holder needs a second PMO, access tokens end with the security stamp, a refresh token works once (reuse is theft), and unknown-user failures are aggregated.
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
        private readonly IOptionsSnapshot<SiteSettings> _siteSettings;
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
            IHttpContextAccessor httpContextAccessor,
            IOptionsSnapshot<SiteSettings> siteSettings)
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
            _siteSettings = siteSettings.ThrowIfNull(nameof(siteSettings));
            _userRole = _uow.Set<UserRole>();
        }

        // ------------------------------------------------------------------------------------------ who is calling

        private ClaimsPrincipal Caller => _http.HttpContext?.User;

        /// <summary>Id of the signed-in user, 0 when nobody is signed in.</summary>
        private int Me => int.TryParse(Caller?.Identity?.GetUserId(), out int id) ? id : 0;

        /// <summary>
        /// The caller holds the PMO role. R1a2 (H-1): the service user of an AI seat holds roles (the weekly-report seat holds PMO) and acts in-process with a principal of its own;
        /// it is never a PMO user for the administration of users (it cannot sign in, and it can never be an approver).
        /// </summary>
        private bool CallerIsPmo => Caller?.IsInRole(IdentityRules.Pmo) == true && !global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(Caller.Identity.GetUserName());

        private static ResponseDto<T> OnlyPmoAnswer<T>() => ResponseDtoHelper.Forbidden<T>(AccountServiceMessages.OnlyPmo);

        private const string SeatUserMessage = "کاربر سرویس صندلی هوش مصنوعی را فقط خود سامانه مدیریت می‌کند.";

        private static string NameOf(User user) => user == null ? "؟" : (string.IsNullOrWhiteSpace(user.UserName) ? "#" + user.Id : user.UserName);

        /// <summary>The settings of section "Identity" (defaults when the section is absent).</summary>
        private IdentitySecurityOptions Rules => _siteSettings.Value?.Identity ?? new IdentitySecurityOptions();

        private IPAddress ClientAddress => _http.HttpContext?.Connection?.RemoteIpAddress;

        /// <summary>
        /// True when the call comes from the server itself (loopback address). A call that a reverse proxy on the same machine passed on also arrives from the loopback
        /// address; such a call carries a forwarded-for header and does not count.
        /// </summary>
        private bool FromServerItself =>
            ClientAddress != null && IPAddress.IsLoopback(ClientAddress)
            && _http.HttpContext?.Request?.Headers?.ContainsKey("X-Forwarded-For") != true;

        /// <summary>The error text of an answer that must not tell which of "unknown user", "wrong password", "inactive" and "deleted" it was (R1a2, L2).</summary>
        private static ResponseDto<ResponseLoginUserDto> GenericLoginRefusal() =>
            ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.InvalidLoginData);

        // a fixed hash for the password check that is made for a name that does not exist, so that answering takes about as long as for a real user (L2)
        private static readonly Lazy<string> DummyHash = new Lazy<string>(() => new PasswordHasher<User>().HashPassword(null, Guid.NewGuid().ToString("N")));

        private void BurnPasswordCheck(string password)
        {
            try { _userManager.PasswordHasher.VerifyHashedPassword(null, DummyHash.Value, password ?? ""); }
            catch (Exception) { /* only the time it takes matters */ }
        }

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

            // R1a2 (M-2): the PMO user who registers an account chooses its first password, so a sensitive role asked for it waits until the user has chosen a password of his own
            RoleOutcome outcome = await ApplyRolesAsync(user, roles, cancellationToken, receiverIsNew: true);
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
            AttachPending(created.Data, outcome); // R1a2 (L-9): { pending, grantId, grantIds } like admin-reset-password and edit-user-roles
            string note = outcome.Note();
            return note == null ? created : ResponseDtoHelper.Ok(created.Data, note);
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
                // R1a2 (L-3): counted per address and minute like the unknown names (not one append-only row for every attempt)
                await RecordUnknownUserAsync(loginUserDto.UserName, "ورود ناموفق", cancellationToken, "نام کاربری سرویس");
                return GenericLoginRefusal();
            }

            var user = await _userManager
                .FindByNameAsync(loginUserDto.UserName)
                .WithCancellationTokenAsync(cancellationToken);

            if (user is null)
            {
                // the same answer as a wrong password (R1a2, L2: and about the same time, so the answer does not tell which names exist);
                // the typed name is never written whole: users type the password into the name field (M5)
                BurnPasswordCheck(loginUserDto.Password);
                await RecordUnknownUserAsync(loginUserDto.UserName, "ورود ناموفق", cancellationToken);
                return GenericLoginRefusal();
            }

            // R1a2 (L2): an inactive or deleted account says so only to the person who knows its password; everybody else gets the answer of a wrong password
            if (!user.IsActive || user.IsDeleted)
            {
                // a wrong guess counts toward the lock-out like any other, and a lock is not reported here (it would tell an inactive account from an unknown name)
                bool knowsPassword = (await _signInManager.CheckPasswordSignInAsync(user, loginUserDto.Password, lockoutOnFailure: true).WithCancellationTokenAsync(cancellationToken)).Succeeded;
                await RecordSignInAsync(user.Id, user.Id, knowsPassword ? IdentityAudit.Blocked : IdentityAudit.Failure,
                    user.IsDeleted ? $"ورود حساب حذف‌شده «{NameOf(user)}»" : $"ورود حساب غیرفعال «{NameOf(user)}»", cancellationToken);
                if (!knowsPassword) return GenericLoginRefusal();
                return ResponseDtoHelper
                    .BadRequest<ResponseLoginUserDto>
                    (user.IsDeleted ? AccountServiceMessages.AccountIsDeleted : AccountServiceMessages.AccountIsNotActive);
            }

            // lockoutOnFailure: true for everybody (R1a): after MaxFailedAccessAttempts wrong passwords the account is locked for DefaultLockoutTimeSpan.
            // CheckPasswordSignInAsync does not make the identity cookie that PasswordSignInAsync made; the API signs in with the JWT only.
            // R1a2 (M1), the documented recovery: a PMO account is never locked out for a person at the server itself (loopback address), so
            // somebody's wrong passwords from the network cannot shut the administrators out for good.
            // R1a2 (L-1): the recovery has a cap. More than IdentityRules.LoopbackFailureCap wrong passwords from the server itself within five minutes
            // end it for that user for a while: his sign-in from the server is then held back by the lock-out like any other.
            SignInResult signInResult;
            string loopbackKey = user.Id + ":" + user.UserName;
            if (FromServerItself && LoopbackFailureTally.RecoveryOpen(loopbackKey, DateTimeOffset.Now)
                && await _userManager.IsInRoleAsync(user, IdentityRules.Pmo).WithCancellationTokenAsync(cancellationToken))
            {
                bool right = await _userManager.CheckPasswordAsync(user, loginUserDto.Password).WithCancellationTokenAsync(cancellationToken);
                if (right)
                {
                    if (await _userManager.IsLockedOutAsync(user).WithCancellationTokenAsync(cancellationToken))
                    {
                        await _userManager.SetLockoutEndDateAsync(user, null).WithCancellationTokenAsync(cancellationToken);
                        await RecordSignInAsync(user.Id, user.Id, IdentityAudit.LoopbackSignIn, $"حساب قفل‌شده‌ی «{NameOf(user)}» از خود سرور باز شد", cancellationToken);
                    }
                    await _userManager.ResetAccessFailedCountAsync(user).WithCancellationTokenAsync(cancellationToken);
                    LoopbackFailureTally.Clear(loopbackKey);
                    signInResult = SignInResult.Success;
                }
                else
                {
                    // a wrong password is counted here too (a guess from the server itself must not be free), but only the sign-in from the network is held back by the lock
                    await _userManager.AccessFailedAsync(user).WithCancellationTokenAsync(cancellationToken);
                    if (LoopbackFailureTally.Add(loopbackKey, DateTimeOffset.Now) == IdentityRules.LoopbackFailureCap + 1)
                        await RecordSignInAsync(user.Id, user.Id, IdentityAudit.LoopbackCapped,
                            $"بیش از {IdentityRules.LoopbackFailureCap} رمز نادرست از خود سرور برای «{NameOf(user)}»؛ ورود از سرور هم مثل بقیه تابع قفل حساب شد", cancellationToken);
                    signInResult = await _userManager.IsLockedOutAsync(user).WithCancellationTokenAsync(cancellationToken) ? SignInResult.LockedOut : SignInResult.Failed;
                }
            }
            else
            {
                signInResult = await _signInManager.CheckPasswordSignInAsync(
                      user,
                      loginUserDto.Password,
                      lockoutOnFailure: true)
                    .WithCancellationTokenAsync(cancellationToken);
            }

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
                return GenericLoginRefusal();
            }

            // the password is right but it is a temporary one (set by an administrator, or the installer's / the upgrade's): no tokens until the user chooses a new one
            UserClaim flag = await GetMustChangeFlagAsync(user.Id, cancellationToken);
            if (flag != null)
            {
                bool expired = MustChangeFlag.IsExpired(flag.ClaimValue, DateTimeOffset.Now, Rules.TemporaryPasswordHours);
                await RecordSignInAsync(user.Id, user.Id, IdentityAudit.MustChange,
                    expired ? $"ورود با رمز موقتِ منقضی‌شده «{NameOf(user)}»" : $"ورود با رمز موقت «{NameOf(user)}»؛ تغییر رمز لازم است", cancellationToken);
                return expired ? TemporaryPasswordExpiredAnswer() : MustChangeAnswer();
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

        /// <summary>R1a2 (L7): the temporary password of a reset ran out; the user asks a PMO user for a new one.</summary>
        private static ResponseDto<ResponseLoginUserDto> TemporaryPasswordExpiredAnswer() =>
            new ResponseDto<ResponseLoginUserDto>(false, AccountServiceMessages.TemporaryPasswordExpired,
                new ResponseLoginUserDto { Code = AccountServiceMessages.TemporaryPasswordExpiredCode })
            .HasHttpStatusCode(HttpStatusCode.Unauthorized);

        /// <summary>
        /// A sign-in or temporary-password attempt with a name that does not exist (R1a2, M1 and M5): counted per client address and minute, and a row is written only for the
        /// first attempt of the minute and at 10, 100, 1000 ... attempts. The row holds the length and the first two characters of the typed name, never the whole text.
        /// </summary>
        private async Task RecordUnknownUserAsync(string typedName, string what, CancellationToken cancellationToken, string kind = "نام کاربری ناشناخته")
        {
            int count = UnknownUserTally.Add(ClientAddress?.ToString(), DateTimeOffset.Now);
            if (!UnknownUserTally.IsRowWorthy(count)) return;
            string hint = IdentityRules.NameHint(typedName);
            string summary = count == 1
                ? $"{what}: {kind} ({hint})"
                : $"{what}: {kind} ({hint})؛ شمار تلاش‌های این نشانی در این دقیقه: {count}";
            await RecordSignInAsync(0, null, count == 1 ? IdentityAudit.Failure : IdentityAudit.UnknownUsers, summary, cancellationToken);
        }

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

        /// <summary>A refresh token that is presented a second time within this time of its use is taken for a double click of the same client and only refused; later it is taken for theft.</summary>
        private static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromSeconds(30);

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
            {
                // R1a2 (M1): Information, not Warning: an anonymous caller can send unknown tokens without limit
                _logger.LogInformation("A refresh token was refused: it is unknown, signed out or expired.");
                return ResponseDtoHelper.Unauthorized<ResponseLoginUserDto>(
                    AccountServiceMessages.InvalidRefreshToken);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;

            // R1a2 (L3): a refresh token works once. The record of a used token stays (marked), so a second presentation is recognised:
            // inside the short grace time it is a double click and only refused, later it means the token was copied: every session of the user ends.
            if (_tokenStoreService.IsConsumed(storagetoken))
            {
                if (now - storagetoken.AccessTokenExpiresDateTime <= RefreshReuseGrace)
                {
                    _logger.LogInformation("A refresh token that was used a moment ago was presented again (user {UserId}); refused.", storagetoken.UserId);
                    return ResponseDtoHelper.Unauthorized<ResponseLoginUserDto>(AccountServiceMessages.InvalidRefreshToken);
                }

                _logger.LogInformation("A refresh token that was already used was presented again (user {UserId}); the sessions of the user are ended.", storagetoken.UserId);
                await RevokeAfterReuseAsync(storagetoken, cancellationToken);
                return ResponseDtoHelper.Unauthorized<ResponseLoginUserDto>(AccountServiceMessages.RefreshTokenReused);
            }

            if (storagetoken.RefreshTokenExpiresDateTime < now)
            {
                await _tokenStoreService.DeleteTokenAsync(refreshToken);
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper.Unauthorized<ResponseLoginUserDto>(AccountServiceMessages.InvalidRefreshToken);
            }

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

            // the used record stays as the tripwire; the new pair replaces it (and, with one session per user, every other record of the user)
            _tokenStoreService.MarkConsumed(storagetoken, now);

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

        /// <summary>A used refresh token came back after the grace time: all sessions of its user end (refresh tokens and, through the security stamp, access tokens) and the audit trail says so.</summary>
        private async Task RevokeAfterReuseAsync(CustomUserToken record, CancellationToken cancellationToken)
        {
            await _tokenStoreService.InvalidateUserTokensAsync(record.UserId).WithCancellationTokenAsync(cancellationToken);
            if (record.User != null) RotateStamp(record.User);
            IdentityAudit.Add(_uow, _http, record.UserId, IdentityAudit.SignIn, record.UserId, IdentityAudit.TokenReuse,
                $"توکن تجدیدِ مصرف‌شده‌ی «{NameOf(record.User)}» دوباره ارائه شد؛ همه‌ی نشست‌های این کاربر بسته شد");
            await _uow.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Signs the user out: every refresh token of the user goes and the security stamp changes, so the access tokens made before stop working at once (R1a2, H2).
        /// The call is anonymous because the access token has usually expired: the user is the signed-in caller when there is one, otherwise the owner of the refresh token.
        /// A refresh token that belongs to somebody else is ignored.
        /// </summary>
        public async ValueTask<ResponseDto<bool>> LogoutAsync(string userId, string refreshToken, CancellationToken cancellationToken)
        {
            int id = int.TryParse(userId, out int parsed) ? parsed : 0;
            CustomUserToken record = string.IsNullOrWhiteSpace(refreshToken) ? null : await _tokenStoreService.FindTokenAsync(refreshToken);
            if (id <= 0 && record != null) id = record.UserId;
            string tokenToRevoke = record != null && record.UserId == id ? refreshToken : null;

            await _tokenStoreService.RevokeUserBearerTokensAsync(id > 0 ? id.ToString() : null, tokenToRevoke).WithCancellationTokenAsync(cancellationToken);

            if (id > 0)
            {
                User user = await _userManager.FindByIdAsync(id.ToString()).WithCancellationTokenAsync(cancellationToken);
                if (user != null)
                {
                    RotateStamp(user);
                    IdentityAudit.Add(_uow, _http, id, IdentityAudit.SignIn, id, IdentityAudit.Logout, $"خروج «{NameOf(user)}»");
                }
            }
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper
                     .Ok(true, AccountServiceMessages.LogedOutSuccessfully);
        }

        /// <summary>
        /// Gives the user a new security stamp: every access token made before stops working (the check runs on each request). The caller saves.
        /// Password changes and resets already do this through ASP.NET Core Identity; this is for role changes, deletion, logout and token theft.
        /// </summary>
        private static void RotateStamp(User user)
        {
            if (user != null) user.SecurityStamp = Guid.NewGuid().ToString("N");
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

            // Identity gives the user a new security stamp with the new password, so the access tokens made before it stop working (R1a2, H2)
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
        /// Runs <paramref name="work"/> in one database transaction (R1a2, L4): all its saves are stored together or none. Only for the SQL Server database: the in-memory
        /// database of the tests has no transactions, so the work simply runs. With the retrying execution strategy the whole delegate is the unit that is repeated, so it
        /// must load what it changes itself; a repeat starts with an empty change tracker.
        /// </summary>
        private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
        {
            if (!(_uow is DbContext db) || !db.Database.IsRelational())
                return await work();

            IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
            int attempt = 0;
            Func<CancellationToken, Task<T>> attemptOnce = async token =>
            {
                if (attempt++ > 0) db.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(token);
                T result = await work();
                await transaction.CommitAsync(token);
                return result;
            };
            return await strategy.ExecuteAsync<T>(attemptOnce, cancellationToken);
        }

        private sealed class ResetOutcome
        {
            public string Error { get; set; }
            public string TemporaryPassword { get; set; }
            public bool Generated { get; set; }
            public User User { get; set; }
        }

        /// <summary>
        /// Sets a temporary password for a user and makes the account ready for it: the "must change" flag (with the time of issue, so the password runs out), the end of every
        /// session, the end of a lock-out, the audit row, and (when the reset was approved by a second PMO user) the removal of the request and a note to the user's inbox.
        /// Everything happens in one transaction (R1a2, L4): the password is never changed without the flag that makes it temporary.
        /// </summary>
        private Task<ResetOutcome> ResetInTransactionAsync(int userId, string suppliedPassword, int requestedBy, int? approvedBy, int? pendingRowId, CancellationToken cancellationToken) =>
            InTransactionAsync(async () =>
            {
                var outcome = new ResetOutcome();
                User user = await _userManager.FindByIdAsync(userId.ToString()).WithCancellationTokenAsync(cancellationToken);
                if (user == null || user.IsDeleted)
                {
                    outcome.Error = AccountServiceMessages.UserNotFound;
                    return outcome;
                }
                outcome.User = user;

                bool generated = string.IsNullOrWhiteSpace(suppliedPassword);
                string temporary = generated ? null : suppliedPassword;
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
                {
                    outcome.Error = AccountServiceMessages.ChangePasswordFailed + Environment.NewLine + (result?.DumpErrors() ?? "");
                    return outcome;
                }

                await SetMustChangeFlagAsync(user.Id, true, cancellationToken, MustChangeFlag.ForReset(DateTimeOffset.Now));
                await _tokenStoreService.InvalidateUserTokensAsync(user.Id).WithCancellationTokenAsync(cancellationToken);
                // the reset also unlocks the account (a user who forgot the password usually locked it)
                await _userManager.SetLockoutEndDateAsync(user, null).WithCancellationTokenAsync(cancellationToken);
                await _userManager.ResetAccessFailedCountAsync(user).WithCancellationTokenAsync(cancellationToken);

                if (pendingRowId.HasValue)
                {
                    UserClaim pending = await _uow.Set<UserClaim>().FirstOrDefaultAsync(c => c.Id == pendingRowId.Value, cancellationToken);
                    if (pending != null) _uow.Set<UserClaim>().Remove(pending);
                }

                if (approvedBy.HasValue)
                {
                    IdentityAudit.Add(_uow, _http, approvedBy.Value, IdentityAudit.Password, user.Id, IdentityAudit.AdminReset,
                        $"بازنشانی رمز عبور «{NameOf(user)}» با درخواست کاربر {requestedBy} و تأیید کاربر {approvedBy.Value}؛ رمز موقت است و در ورود بعدی باید تغییر کند",
                        before: new { requestedBy }, after: new { approvedBy = approvedBy.Value, mustChange = true });
                    IdentityInbox.Alert(_uow, "reset-done-" + user.Id, AccountServiceMessages.ResetDoneInbox, recipientUserId: user.Id);
                }
                else
                {
                    IdentityAudit.Add(_uow, _http, requestedBy, IdentityAudit.Password, user.Id, IdentityAudit.AdminReset,
                        $"بازنشانی رمز عبور «{NameOf(user)}» توسط مدیر؛ رمز موقت است و در ورود بعدی باید تغییر کند");
                }
                await _uow.SaveChangesAsync(cancellationToken);

                outcome.Generated = generated;
                outcome.TemporaryPassword = temporary;
                return outcome;
            }, cancellationToken);

        /// <summary>
        /// A PMO user sets a temporary password for another user (never his or her own). The user cannot sign in with it
        /// until a new password is chosen, the old sessions end and a lock-out is lifted. No tokens are returned.
        /// R1a2 (H1c): when the user is a PMO user or holds a sensitive role the reset is only requested here; a different PMO user approves it
        /// (role-grants/{id}/approve), and the user gets a note in the inbox. Ordinary users are reset at once.
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

            User user = await _userManager.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == dto.UserId, cancellationToken);
            if (user == null || user.IsDeleted == true)
                return ResponseDtoHelper.NotFound<AdminResetPasswordResultDto>(AccountServiceMessages.UserNotFound);

            // the service users of the AI seats are managed by the system only
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.BadRequest<AdminResetPasswordResultDto>(SeatUserMessage);

            // R1a2 (M-2): a user who held a sensitive role (the PMO role included) within the last week is handled like one who holds it now; otherwise a PMO user could take
            // the role away, reset the password at once, learn it and ask for the role back as if it were routine
            List<string> sensitive = (await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken)).Where(IdentityRules.IsSensitiveRole).ToList();
            if (sensitive.Count == 0)
                sensitive = await RecentlySensitiveRolesAsync(user.Id, cancellationToken);
            if (sensitive.Count > 0)
                return await RequestResetAsync(user, sensitive, me, cancellationToken);

            ResetOutcome outcome = await ResetInTransactionAsync(user.Id, dto.TemporaryPassword, me, null, null, cancellationToken);
            if (outcome.Error != null)
                return ResponseDtoHelper.BadRequest<AdminResetPasswordResultDto>(outcome.Error);

            return ResponseDtoHelper.Ok(new AdminResetPasswordResultDto
            {
                UserId = user.Id,
                UserName = user.UserName,
                TemporaryPassword = outcome.Generated ? outcome.TemporaryPassword : null,
                MustChangePassword = true,
            }, AccountServiceMessages.PasswordResetSuccessfully);
        }

        /// <summary>
        /// The reset of a PMO or sensitive-role holder (or of somebody who was one in the last week, R1a2 M-2) is written down as a request (a claim of the user, kind "reset")
        /// that a different PMO user must approve. <paramref name="sensitive"/> are the roles that make him sensitive (for the trail).
        /// </summary>
        private async Task<ResponseDto<AdminResetPasswordResultDto>> RequestResetAsync(User user, IList<string> sensitive, int me, CancellationToken cancellationToken)
        {
            var open = await OpenGrantsOfAsync(user.Id, cancellationToken);
            if (open.Any(o => o.Grant.IsReset))
                return ResponseDtoHelper.BadRequest<AdminResetPasswordResultDto>(AccountServiceMessages.ResetAlreadyPending);

            // a request that nobody can ever approve (the requester and the user themselves are the only PMO users) would only give a false sense of safety.
            // R1a2 (H-1): the AI seats do not count as approvers (CountActivePmoAsync leaves them out)
            if (await CountActivePmoAsync(new[] { me, user.Id }, cancellationToken) == 0)
                return ResponseDtoHelper.BadRequest<AdminResetPasswordResultDto>(AccountServiceMessages.ResetNoApprover);

            var grant = new PendingRoleGrant { Role = IdentityRules.ResetRoleLabel, RequestedBy = me, RequestedAt = DateTimeOffset.Now, Kind = IdentityRules.KindReset };
            var row = new UserClaim { UserId = user.Id, ClaimType = IdentityRules.PendingRoleGrantClaimType, ClaimValue = grant.ToClaimValue() };
            _uow.Set<UserClaim>().Add(row);
            IdentityAudit.Add(_uow, _http, me, IdentityAudit.Password, user.Id, IdentityAudit.ResetRequested,
                $"درخواست بازنشانی رمز عبور «{NameOf(user)}» ({string.Join("، ", sensitive)})؛ تا تأیید مدیر (PMO) دیگر انجام نمی‌شود",
                after: new { requestedBy = me, expires = grant.ExpiresAt.ToString("O") });
            IdentityInbox.Alert(_uow, "reset-request-" + user.Id, AccountServiceMessages.ResetRequestedInbox, recipientUserId: user.Id);
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper.Ok(new AdminResetPasswordResultDto
            {
                UserId = user.Id,
                UserName = user.UserName,
                MustChangePassword = false,
                Pending = true,
                GrantId = row.Id,
            }, AccountServiceMessages.ResetNeedsApproval);
        }

        /// <summary>
        /// The last step of a temporary password: the user proves it, chooses a new password and is signed in.
        /// Anonymous call; a wrong temporary password counts toward the lock-out like a wrong sign-in.
        /// R1a2: an unknown, inactive or deleted account gets the answer of a wrong password (L2), a temporary password of a reset runs out after
        /// <c>Identity:TemporaryPasswordHours</c> (L7), and the typed name of an unknown user is never written whole to the trail (M5).
        /// </summary>
        public async ValueTask<ResponseDto<ResponseLoginUserDto>> ChangeTemporaryPasswordAsync(ChangeTemporaryPasswordDto dto, CancellationToken cancellationToken)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.UserName) || string.IsNullOrEmpty(dto.TemporaryPassword))
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.NullDataReceived);

            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(dto.UserName))
                return GenericLoginRefusal();

            User user = await _userManager.FindByNameAsync(dto.UserName).WithCancellationTokenAsync(cancellationToken);
            if (user == null)
            {
                BurnPasswordCheck(dto.TemporaryPassword);
                await RecordUnknownUserAsync(dto.UserName, "تغییر رمز موقت ناموفق", cancellationToken);
                return GenericLoginRefusal();
            }

            if (!user.IsActive || user.IsDeleted)
            {
                // only somebody who knows the password hears why it does not work; a wrong guess counts toward the lock-out like any other
                SignInResult probe = await _signInManager.CheckPasswordSignInAsync(user, dto.TemporaryPassword, lockoutOnFailure: true).WithCancellationTokenAsync(cancellationToken);
                await RecordSignInAsync(user.Id, user.Id, probe.Succeeded ? IdentityAudit.Blocked : IdentityAudit.Failure,
                    user.IsDeleted ? $"تغییر رمز موقتِ حساب حذف‌شده «{NameOf(user)}»" : $"تغییر رمز موقتِ حساب غیرفعال «{NameOf(user)}»", cancellationToken);
                if (!probe.Succeeded) return GenericLoginRefusal();
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(user.IsDeleted ? AccountServiceMessages.AccountIsDeleted : AccountServiceMessages.AccountIsNotActive);
            }

            PasswordCheck check = await CheckPasswordCountingFailuresAsync(user, dto.TemporaryPassword, cancellationToken);
            if (check == PasswordCheck.LockedOut)
            {
                await RecordSignInAsync(user.Id, user.Id, IdentityAudit.LockedOut, $"حساب «{NameOf(user)}» قفل است (تغییر رمز موقت)", cancellationToken);
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.AccountIsLockedOut);
            }
            if (check == PasswordCheck.Wrong)
            {
                await RecordSignInAsync(user.Id, user.Id, IdentityAudit.Failure, $"تغییر رمز موقت «{NameOf(user)}» ناموفق (رمز موقت نادرست)", cancellationToken);
                return GenericLoginRefusal();
            }

            UserClaim flag = await GetMustChangeFlagAsync(user.Id, cancellationToken);
            if (flag == null)
                return ResponseDtoHelper.BadRequest<ResponseLoginUserDto>(AccountServiceMessages.NoTemporaryPassword);

            if (MustChangeFlag.IsExpired(flag.ClaimValue, DateTimeOffset.Now, Rules.TemporaryPasswordHours))
            {
                await RecordSignInAsync(user.Id, user.Id, IdentityAudit.MustChange, $"تغییر رمز با رمز موقتِ منقضی‌شده «{NameOf(user)}»", cancellationToken);
                return TemporaryPasswordExpiredAnswer();
            }

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

        /// <summary>The flag row (its value says why it was set, see <see cref="MustChangeFlag"/>), or null.</summary>
        private Task<UserClaim> GetMustChangeFlagAsync(int userId, CancellationToken cancellationToken) =>
            _uow.Set<UserClaim>().AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId && c.ClaimType == IdentityRules.MustChangePasswordClaimType, cancellationToken);

        /// <summary>Sets or clears the flag; the caller saves. A flag that is set again takes the new <paramref name="value"/> (a new temporary password starts a new time limit).</summary>
        private async Task SetMustChangeFlagAsync(int userId, bool on, CancellationToken cancellationToken, string value = null)
        {
            List<UserClaim> rows = await _uow.Set<UserClaim>()
                .Where(c => c.UserId == userId && c.ClaimType == IdentityRules.MustChangePasswordClaimType)
                .ToListAsync(cancellationToken);
            if (on && rows.Count == 0)
                _uow.Set<UserClaim>().Add(new UserClaim { UserId = userId, ClaimType = IdentityRules.MustChangePasswordClaimType, ClaimValue = value ?? "1" });
            else if (on)
                rows[0].ClaimValue = value ?? rows[0].ClaimValue;
            else if (rows.Count > 0)
                _uow.RemoveRange(rows);
        }

        // ------------------------------------------------------------------------------------------ roles

        private sealed class RoleOutcome
        {
            public List<string> Added { get; } = new List<string>();
            public List<string> Removed { get; } = new List<string>();
            public List<string> Pending { get; } = new List<string>();
            public List<string> AlreadyPending { get; } = new List<string>();
            public List<string> Withdrawn { get; } = new List<string>();

            /// <summary>R1a2 (M-1): the removal of the PMO role was requested by this call; it was already requested; the request was withdrawn by an edit that keeps the role.</summary>
            public List<string> RevokePending { get; } = new List<string>();
            public List<string> RevokeAlreadyPending { get; } = new List<string>();
            public bool RevokeWithdrawn { get; set; }

            /// <summary>R1a2 (L-9): ids of the requests (sensitive roles, removal of the PMO role) of this user that wait for a second PMO user after this call.</summary>
            public List<int> GrantIds { get; } = new List<int>();

            public bool Bootstrapped { get; set; }
            public string Error { get; set; }

            public bool HasPending => GrantIds.Count > 0;

            public int? FirstGrantId => GrantIds.Count > 0 ? GrantIds[0] : (int?)null;

            public string AuditText()
            {
                var parts = new List<string>();
                if (Added.Count > 0) parts.Add("نقش‌های داده‌شده: " + string.Join("، ", Added));
                if (Removed.Count > 0) parts.Add("نقش‌های گرفته‌شده: " + string.Join("، ", Removed));
                if (Pending.Count > 0) parts.Add("در انتظار تأیید مدیر دوم: " + string.Join("، ", Pending));
                if (RevokePending.Count > 0) parts.Add("برداشتن در انتظار تأیید مدیر دوم: " + string.Join("، ", RevokePending));
                if (Withdrawn.Count > 0) parts.Add("درخواست‌های پس‌گرفته‌شده: " + string.Join("، ", Withdrawn));
                return parts.Count == 0 ? "" : " — " + string.Join("؛ ", parts);
            }

            public string PendingMessage() => AccountServiceMessages.SensitiveRolePending + string.Join("، ", Pending);

            /// <summary>
            /// The message of the answer: the open requests first, or a note about the bootstrap of the second PMO user, then the request to take the PMO role away that this call made,
            /// and (L1) the requests this edit withdrew (an edit that no longer asks for a sensitive role says so, so nobody expects the second approval of a request that is gone);
            /// null when none applies.
            /// </summary>
            public string Note()
            {
                var parts = new List<string>();
                if (Pending.Count > 0) parts.Add(PendingMessage());
                else if (Bootstrapped) parts.Add(AccountServiceMessages.PmoBootstrapApplied);
                if (RevokePending.Count > 0) parts.Add(AccountServiceMessages.RevokePending + string.Join("، ", RevokePending));
                if (Withdrawn.Count > 0) parts.Add(AccountServiceMessages.GrantWithdrawnByEdit + string.Join("، ", Withdrawn));
                if (RevokeWithdrawn) parts.Add(AccountServiceMessages.RevokeWithdrawnByEdit + IdentityRules.Pmo);
                return parts.Count == 0 ? null : string.Join(" ", parts);
            }
        }

        private static bool HasRole(IEnumerable<string> roleNames, string role) =>
            roleNames != null && roleNames.Any(n => string.Equals(n, role, StringComparison.OrdinalIgnoreCase));

        private static ChangeRoleResultDto ResultOf(IEnumerable<string> roles, RoleOutcome outcome) => new ChangeRoleResultDto
        {
            Roles = string.Join(",", roles ?? Enumerable.Empty<string>()),
            Pending = outcome != null && outcome.HasPending,
            GrantId = outcome?.FirstGrantId,
            GrantIds = outcome?.GrantIds.ToArray() ?? Array.Empty<int>(),
        };

        private static void AttachPending(ResponseUserDto dto, RoleOutcome outcome)
        {
            if (dto == null || outcome == null) return;
            dto.Pending = outcome.HasPending;
            dto.GrantId = outcome.FirstGrantId;
            dto.GrantIds = outcome.GrantIds.ToArray();
        }

        /// <summary>
        /// The open (not expired) requests of a user: sensitive roles asked for him or her, a password reset (check <see cref="PendingRoleGrant.IsReset"/>) and, since the review
        /// fixes, the removal of the PMO role or the deletion of the account (<see cref="PendingRoleGrant.IsRevoke"/>). Expired ones are not returned.
        /// </summary>
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
        /// The number of active PMO users other than <paramref name="excluding"/> (the role is resolved by name here and the users are filtered by its id).
        /// R1a2 (H-1): the service users of the AI seats are not counted. The weekly-report seat holds the PMO role and is made active a few seconds after the server starts; counted,
        /// it would stop the bootstrap of the second PMO user for ever and let a request nobody can approve look as if somebody could. A seat can never sign in or approve.
        /// </summary>
        private async Task<int> CountActivePmoAsync(IEnumerable<int> excluding, CancellationToken cancellationToken)
        {
            Role pmo = await _roleManager.FindByNameAsync(IdentityRules.Pmo).WithCancellationTokenAsync(cancellationToken);
            if (pmo == null) return 0;
            int[] skip = (excluding ?? Enumerable.Empty<int>()).ToArray();
            int roleId = pmo.Id;
            string seatPrefix = global::Services.NasbNiro.Ai.AiSeatNames.UserPrefix;
            return await _uow.Set<UserRole>().AsNoTracking()
                .Where(ur => ur.RoleId == roleId && !skip.Contains(ur.UserId) && ur.User.IsActive && !ur.User.IsDeleted && !ur.User.UserName.StartsWith(seatPrefix))
                .Select(ur => ur.UserId)
                .Distinct()
                .CountAsync(cancellationToken);
        }

        /// <summary>
        /// The record of the sensitive role <paramref name="role"/> the user holds now (who asked for it, who approved it, when), or null when none was kept (the seeded administrator on a
        /// new installation). A record of a role that was taken away is not returned (R1a2, M-2: it stays only as the memory of a recently held role).
        /// </summary>
        private async Task<RoleGrantRecord> GetGrantRecordAsync(int userId, string role, CancellationToken cancellationToken)
        {
            List<string> values = await _uow.Set<UserClaim>().AsNoTracking()
                .Where(c => c.UserId == userId && c.ClaimType == IdentityRules.RoleGrantedClaimType)
                .Select(c => c.ClaimValue)
                .ToListAsync(cancellationToken);
            RoleGrantRecord found = null;
            foreach (string value in values)
                if (RoleGrantRecord.TryParse(value, out RoleGrantRecord record) && !record.IsRemoved && string.Equals(record.Role, role, StringComparison.OrdinalIgnoreCase) && (found == null || record.At > found.At))
                    found = record;
            return found;
        }

        /// <summary>Writes the record of a sensitive role that was just given; the caller saves. <paramref name="requestedBy"/> is the PMO user who asked, <paramref name="approvedBy"/> the one who approved (0 for the bootstrap).</summary>
        private void AddGrantRecord(int userId, string role, int requestedBy, int approvedBy, bool bootstrap)
        {
            var record = new RoleGrantRecord { Role = role.Trim().ToUpperInvariant(), GrantedBy = requestedBy, ApprovedBy = approvedBy, At = DateTimeOffset.Now, Bootstrap = bootstrap };
            _uow.Set<UserClaim>().Add(new UserClaim { UserId = userId, ClaimType = IdentityRules.RoleGrantedClaimType, ClaimValue = record.ToClaimValue() });
        }

        /// <summary>
        /// R1a2 (M-2): marks the records of the sensitive roles that were taken away as removed instead of deleting them, so that a password reset of the former holder is still handled as a
        /// sensitive one for <see cref="IdentityRules.RecentSensitiveWindow"/>. A sensitive role that has no record (given before the records existed) gets one that is already marked.
        /// Ordinary roles have no record. The caller saves.
        /// </summary>
        private async Task MarkRolesRemovedAsync(int userId, IEnumerable<string> roles, CancellationToken cancellationToken)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            string[] sensitive = (roles ?? Enumerable.Empty<string>()).Where(IdentityRules.IsSensitiveRole).Select(r => r.Trim().ToUpperInvariant()).Distinct().ToArray();
            if (sensitive.Length == 0) return;
            List<UserClaim> rows = await _uow.Set<UserClaim>()
                .Where(c => c.UserId == userId && c.ClaimType == IdentityRules.RoleGrantedClaimType)
                .ToListAsync(cancellationToken);
            foreach (string role in sensitive)
            {
                bool marked = false;
                foreach (UserClaim row in rows)
                {
                    if (RoleGrantRecord.TryParse(row.ClaimValue, out RoleGrantRecord record) && record.Role == role && !record.IsRemoved)
                    {
                        record.RemovedAt = now;
                        row.ClaimValue = record.ToClaimValue();
                        marked = true;
                    }
                }
                if (!marked)
                    _uow.Set<UserClaim>().Add(new UserClaim
                    {
                        UserId = userId, ClaimType = IdentityRules.RoleGrantedClaimType,
                        ClaimValue = new RoleGrantRecord { Role = role, At = now, RemovedAt = now, Legacy = true }.ToClaimValue(),
                    });
            }
        }

        /// <summary>R1a2 (M-2): the sensitive roles the user lost less than a week ago (from the records that stay when a role is taken away).</summary>
        private async Task<List<string>> RecentlySensitiveRolesAsync(int userId, CancellationToken cancellationToken)
        {
            List<string> values = await _uow.Set<UserClaim>().AsNoTracking()
                .Where(c => c.UserId == userId && c.ClaimType == IdentityRules.RoleGrantedClaimType)
                .Select(c => c.ClaimValue)
                .ToListAsync(cancellationToken);
            var records = new List<RoleGrantRecord>();
            foreach (string value in values)
                if (RoleGrantRecord.TryParse(value, out RoleGrantRecord record)) records.Add(record);
            return IdentityRules.RecentlySensitiveRoles(records, DateTimeOffset.Now);
        }

        /// <summary>
        /// R1a2 (M-2): of these users, the ones whose password may still be known to the PMO user who registered them or reset it: the latest "Registered" or "AdminReset" row of the
        /// audit trail is newer than their latest password change of their own ("Changed", or "TempChanged" after a reset), see <see cref="IdentityRules.PasswordMayBeKnownToPmo"/>.
        /// No column was added for this; the append-only trail is the memory.
        /// </summary>
        private async Task<HashSet<int>> ReceiversWhoMustChangeAsync(IEnumerable<int> userIds, CancellationToken cancellationToken)
        {
            int[] ids = (userIds ?? Enumerable.Empty<int>()).Distinct().ToArray();
            var result = new HashSet<int>();
            if (ids.Length == 0) return result;

            var rows = await _uow.Set<Entities.Model.NasbNiro.Finance.FnAuditLog>().AsNoTracking()
                .Where(a => a.EntityId != null && ids.Contains(a.EntityId.Value)
                            && ((a.EntityType == IdentityAudit.User && a.Action == IdentityAudit.Registered)
                                || (a.EntityType == IdentityAudit.Password && (a.Action == IdentityAudit.AdminReset || a.Action == IdentityAudit.TempChanged || a.Action == IdentityAudit.Changed))))
                .Select(a => new { a.Id, EntityId = a.EntityId.Value, a.Action, a.UserId })
                .ToListAsync(cancellationToken);

            foreach (int id in ids)
            {
                long setByPmo = 0, own = 0;
                foreach (var row in rows.Where(r => r.EntityId == id))
                {
                    if (row.Action == IdentityAudit.Registered || row.Action == IdentityAudit.AdminReset) setByPmo = Math.Max(setByPmo, row.Id);
                    else if (row.UserId == id) own = Math.Max(own, row.Id);   // a change of his own: the user is the actor
                }
                if (IdentityRules.PasswordMayBeKnownToPmo(setByPmo, own)) result.Add(id);
            }
            return result;
        }

        private async Task<bool> ReceiverMustChangeOwnPasswordAsync(int userId, CancellationToken cancellationToken) =>
            (await ReceiversWhoMustChangeAsync(new[] { userId }, cancellationToken)).Contains(userId);

        /// <summary>
        /// Takes the lock that makes the count of the PMO users and the grant or removal that depends on it one step (R1a2, M-1): without it two calls at the same moment could both see
        /// "one PMO user" and both be bootstrapped, or two PMO users could step down together and leave nobody. An exclusive application lock (sp_getapplock) held until the transaction
        /// ends; only for SQL Server. The in-memory database of the tests has no concurrency worth guarding, so nothing is taken there.
        /// </summary>
        private async Task TakeBootstrapLockAsync(CancellationToken cancellationToken)
        {
            if (!(_uow is DbContext db) || !db.Database.IsRelational()) return;
            if (!string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal)) return;
            await db.Database.ExecuteSqlRawAsync(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'pmis:pmo-bootstrap', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 20000; "
                + "IF @r < 0 THROW 51000, N'The lock of the PMO bootstrap could not be taken.', 1;", cancellationToken);
        }

        /// <summary>
        /// Brings the roles of a user to the wanted list: roles are taken away at once (the PMO role is the exception, R1a2 M-1: its removal is a request, see below), ordinary roles are given at once,
        /// sensitive roles (the PMO role too, since R1a2) become a request that a second PMO user must approve.
        /// The one exception: while fewer than two active PMO users exist (AI seats are not counted) the PMO role is given at once, with an audit row and an alert to the CEO (the bootstrap of the second PMO user).
        /// When the PMO role is being given, the count and the grant are one step under a lock (M-1).
        /// An edit that no longer asks for a role withdraws the pending request for it (L1). A change of roles gives the user a new security stamp (H2). Saves.
        /// <paramref name="receiverIsNew"/>: the user was registered by this very call (a PMO user chose his password; the audit row of the registration is written after this).
        /// </summary>
        private async Task<RoleOutcome> ApplyRolesAsync(User user, IList<Role> wanted, CancellationToken cancellationToken, bool receiverIsNew = false)
        {
            IList<string> currentNames = await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken);
            bool wantsNewPmo = HasRole(wanted.Select(r => r.Name), IdentityRules.Pmo) && !HasRole(currentNames, IdentityRules.Pmo);
            if (!wantsNewPmo)
                return await ApplyRolesCoreAsync(user, wanted, currentNames, receiverIsNew, cancellationToken);

            int userId = user.Id;
            return await InTransactionAsync(async () =>
            {
                await TakeBootstrapLockAsync(cancellationToken);
                User current = await _userManager.FindByIdAsync(userId.ToString()).WithCancellationTokenAsync(cancellationToken);
                IList<string> names = await _userManager.GetRolesAsync(current).WithCancellationTokenAsync(cancellationToken);
                return await ApplyRolesCoreAsync(current, wanted, names, receiverIsNew, cancellationToken);
            }, cancellationToken);
        }

        private async Task<RoleOutcome> ApplyRolesCoreAsync(User user, IList<Role> wanted, IList<string> currentNames, bool receiverIsNew, CancellationToken cancellationToken)
        {
            var outcome = new RoleOutcome();
            var openAll = await OpenGrantsOfAsync(user.Id, cancellationToken);
            var open = openAll.Where(o => o.Grant.IsRole).ToList();
            var openRevoke = openAll.FirstOrDefault(o => o.Grant.IsRevoke && !o.Grant.IsUserDelete);
            string[] wantedNames = wanted.Select(r => r.Name).ToArray();

            bool wantsNewPmo = HasRole(wantedNames, IdentityRules.Pmo) && !HasRole(currentNames, IdentityRules.Pmo);
            int activePmo = wantsNewPmo ? await CountActivePmoAsync(null, cancellationToken) : int.MaxValue;
            bool bootstrap = wantsNewPmo && activePmo < IdentityRules.PmoBootstrapBelow;

            IdentityRules.RolePlan plan = IdentityRules.PlanRoles(currentNames, wantedNames, open.Select(o => o.Grant.Role), bootstrap);

            // R1a2 (M-1): taking the PMO role away is a request; one that nobody could approve is not made (nothing has been changed yet)
            if (plan.RevokeNeedsApproval.Count > 0 && openRevoke.Row == null
                && await CountActivePmoAsync(new[] { Me, user.Id }, cancellationToken) == 0)
            {
                outcome.Error = AccountServiceMessages.RevokeNoApprover;
                return outcome;
            }

            bool changed = false;

            // L1: a request for a role that this edit no longer asks for is withdrawn, so it cannot be approved later against the intention of the edit
            var wantedSet = new HashSet<string>(wantedNames.Select(n => n.Trim().ToUpperInvariant()), StringComparer.Ordinal);
            foreach (var withdrawn in open.Where(o => !wantedSet.Contains(o.Grant.Role)).ToList())
            {
                _uow.Set<UserClaim>().Remove(withdrawn.Row);
                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Roles, user.Id, IdentityAudit.GrantRejected,
                    $"پس‌گرفتن درخواست نقش {withdrawn.Grant.Role} برای «{NameOf(user)}» با ویرایش نقش‌ها",
                    before: new { role = withdrawn.Grant.Role, requestedBy = withdrawn.Grant.RequestedBy });
                outcome.Withdrawn.Add(withdrawn.Grant.Role);
            }

            // R1a2 (M-1): an edit that keeps the PMO role withdraws the request to take it away
            if (openRevoke.Row != null && wantedSet.Contains(IdentityRules.Pmo))
            {
                _uow.Set<UserClaim>().Remove(openRevoke.Row);
                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Roles, user.Id, IdentityAudit.GrantRejected,
                    $"پس‌گرفتن درخواست برداشتن نقش {IdentityRules.Pmo} از «{NameOf(user)}» با ویرایش نقش‌ها",
                    before: new { role = openRevoke.Grant.Role, kind = IdentityRules.KindRevoke, requestedBy = openRevoke.Grant.RequestedBy });
                outcome.RevokeWithdrawn = true;
            }

            if (plan.AddNow.Count > 0)
            {
                IdentityResult added = await _userManager.AddToRolesAsync(user, plan.AddNow).WithCancellationTokenAsync(cancellationToken);
                if (!added.Succeeded)
                {
                    outcome.Error = AccountServiceMessages.FailedToAssignRole + added.DumpErrors();
                    return outcome;
                }
                outcome.Added.AddRange(plan.AddNow);
                changed = true;

                foreach (string role in plan.AddNow.Where(IdentityRules.IsSensitiveRole))
                {
                    // only the PMO role can get here (the bootstrap); the record lets the approval rules see who made this PMO user.
                    // R1a2 (M-1): GrantedBy is the PMO user who really did it (the actor), so the chain can be followed in the trail; the record stays exempt from the
                    // "granted by the requester" rule because the bootstrap is the audited start that makes two PMO users possible at all
                    AddGrantRecord(user.Id, role, Me, 0, bootstrap: true);
                    IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Roles, user.Id, IdentityAudit.Bootstrap,
                        $"راه‌اندازی مدیر دوم: نقش {role} بدون تأیید دوم به «{NameOf(user)}» داده شد (کمتر از {IdentityRules.PmoBootstrapBelow} مدیر PMO فعال بود: {activePmo})",
                        after: new { role, grantedBy = Me, activePmoBefore = activePmo });
                    IdentityInbox.Alert(_uow, "bootstrap-" + role + "-" + user.Id,
                        AccountServiceMessages.BootstrapInbox + $"«{NameOf(user)}» ({role})، توسط کاربر {Me}", recipientRole: "CEO");
                    outcome.Bootstrapped = true;
                }
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
                changed = true;

                // R1a2 (M-2): the record of a sensitive role that is gone stays, marked as removed (a reset of this user is still handled as a sensitive one for a week)
                await MarkRolesRemovedAsync(user.Id, plan.Remove, cancellationToken);
            }

            DateTimeOffset now = DateTimeOffset.Now;

            // R1a2 (M-1): the removal of the PMO role waits for a second PMO user
            foreach (string role in plan.RevokeNeedsApproval)
            {
                if (openRevoke.Row != null)
                {
                    outcome.RevokeAlreadyPending.Add(role);
                    continue;
                }
                var revoke = new PendingRoleGrant { Role = role, RequestedBy = Me, RequestedAt = now, Kind = IdentityRules.KindRevoke };
                _uow.Set<UserClaim>().Add(new UserClaim { UserId = user.Id, ClaimType = IdentityRules.PendingRoleGrantClaimType, ClaimValue = revoke.ToClaimValue() });
                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Roles, user.Id, IdentityAudit.RevokeRequested,
                    $"درخواست برداشتن نقش {role} از «{NameOf(user)}»؛ تا تأیید مدیر (PMO) دیگر اعمال نمی‌شود",
                    after: new { role, requestedBy = Me, expires = revoke.ExpiresAt.ToString("O") });
                IdentityInbox.Alert(_uow, "revoke-request-" + user.Id, AccountServiceMessages.RevokeRequestedInbox, recipientUserId: user.Id);
                outcome.RevokePending.Add(role);
            }

            bool grantRequested = false;
            foreach (string role in plan.NeedApproval)
            {
                var grant = new PendingRoleGrant { Role = role, RequestedBy = Me, RequestedAt = now };
                _uow.Set<UserClaim>().Add(new UserClaim { UserId = user.Id, ClaimType = IdentityRules.PendingRoleGrantClaimType, ClaimValue = grant.ToClaimValue() });
                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Roles, user.Id, IdentityAudit.GrantRequested,
                    $"درخواست نقش حساس {role} برای «{NameOf(user)}»؛ تا تأیید مدیر (PMO) دیگر اعمال نمی‌شود",
                    after: new { role, requestedBy = Me, expires = grant.ExpiresAt.ToString("O") });
                outcome.Pending.Add(role);
                grantRequested = true;
            }
            // R1a2 (M-2): the approval waits until the receiver has chosen a password of his own; he is told so in his inbox
            if (grantRequested && (receiverIsNew || await ReceiverMustChangeOwnPasswordAsync(user.Id, cancellationToken)))
                IdentityInbox.Alert(_uow, "grant-own-password-" + user.Id, AccountServiceMessages.GrantNeedsOwnPasswordInbox, recipientUserId: user.Id);
            outcome.AlreadyPending.AddRange(plan.AlreadyPending);

            if (outcome.Added.Count > 0 || outcome.Removed.Count > 0)
                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Roles, user.Id, IdentityAudit.RolesChanged,
                    $"تغییر نقش‌های «{NameOf(user)}»" + outcome.AuditText(),
                    before: new { roles = currentNames }, after: new { added = outcome.Added, removed = outcome.Removed });

            // H2: the access tokens made with the old roles end now; the refresh token (still valid) brings a new one with the roles the user has from here on
            if (changed) RotateStamp(user);

            await _uow.SaveChangesAsync(cancellationToken);

            // R1a2 (L-9): the answer says which requests of this user are waiting now
            foreach (var waiting in (await OpenGrantsOfAsync(user.Id, cancellationToken)).Where(o => !o.Grant.IsReset && !o.Grant.IsUserDelete).OrderBy(o => o.Row.Id))
                outcome.GrantIds.Add(waiting.Row.Id);
            return outcome;
        }

        private enum OwnRoleChange { None, StepDown, Refused }

        /// <summary>
        /// What a PMO user asks for when he edits his own roles (R1a2, M-1): nothing; to take the PMO role from himself (allowed, see <see cref="StepDownAsync"/>); or anything else
        /// (refused: nobody gives himself a role or takes another one away).
        /// </summary>
        private async Task<OwnRoleChange> ClassifyOwnRoleChangeAsync(User user, IList<Role> wanted, CancellationToken cancellationToken)
        {
            IList<string> mine = await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken);
            IdentityRules.RolePlan plan = IdentityRules.PlanRoles(mine, wanted.Select(r => r.Name), Enumerable.Empty<string>());
            if (plan.IsEmpty) return OwnRoleChange.None;
            return plan.IsOnlyPmoRemoval ? OwnRoleChange.StepDown : OwnRoleChange.Refused;
        }

        /// <summary>
        /// R1a2 (M-1): a PMO user takes the PMO role from himself. Nobody can be forced to do it, so it needs no second approval (the trail says "SteppedDown"), except that the
        /// last active PMO user cannot (the company would be left without anybody to administer users). The count and the removal are one step under the lock of the bootstrap.
        /// </summary>
        private Task<(bool Done, string Error, IList<string> Roles)> StepDownAsync(int userId, CancellationToken cancellationToken) =>
            InTransactionAsync<(bool Done, string Error, IList<string> Roles)>(async () =>
            {
                await TakeBootstrapLockAsync(cancellationToken);
                User self = await _userManager.FindByIdAsync(userId.ToString()).WithCancellationTokenAsync(cancellationToken);
                if (self == null) return (false, AccountServiceMessages.UserNotFound, null);

                if (await CountActivePmoAsync(new[] { userId }, cancellationToken) == 0)
                {
                    IdentityAudit.Add(_uow, _http, userId, IdentityAudit.Access, userId, IdentityAudit.Refused, $"تلاش برای کنار رفتن از PMO «{NameOf(self)}» در حالی که آخرین مدیر فعال است");
                    await _uow.SaveChangesAsync(cancellationToken);
                    return (false, AccountServiceMessages.LastPmoCannotStepDown, null);
                }

                IList<string> before = await _userManager.GetRolesAsync(self).WithCancellationTokenAsync(cancellationToken);
                if (HasRole(before, IdentityRules.Pmo))
                {
                    IdentityResult removed = await _userManager.RemoveFromRoleAsync(self, IdentityRules.Pmo).WithCancellationTokenAsync(cancellationToken);
                    if (!removed.Succeeded) return (false, AccountServiceMessages.FailedToAssignRole + removed.DumpErrors(), null);
                    await MarkRolesRemovedAsync(userId, new[] { IdentityRules.Pmo }, cancellationToken);

                    // a request to take the role from him is moot now
                    foreach (var moot in (await OpenGrantsOfAsync(userId, cancellationToken)).Where(o => o.Grant.IsRevoke && !o.Grant.IsUserDelete))
                        _uow.Set<UserClaim>().Remove(moot.Row);

                    RotateStamp(self);
                    IdentityAudit.Add(_uow, _http, userId, IdentityAudit.Roles, userId, IdentityAudit.SteppedDown,
                        $"«{NameOf(self)}» نقش PMO را از خودش برداشت", before: new { roles = before }, after: new { removed = new[] { IdentityRules.Pmo } });
                    await _uow.SaveChangesAsync(cancellationToken);
                }
                return (true, null, await _userManager.GetRolesAsync(self).WithCancellationTokenAsync(cancellationToken));
            }, cancellationToken);

        public async ValueTask<ResponseDto<ChangeRoleResultDto>> ChangeRoleAsync(ChangeRoleDto changeRoleDto, CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<ChangeRoleResultDto>();

            if (changeRoleDto is null)
                return ResponseDtoHelper.BadRequest<ChangeRoleResultDto>
                    (AccountServiceMessages.NullDataReceived);

            User user = await _userManager
                .FindByIdAsync(changeRoleDto.UserId.ToString())
                .WithCancellationTokenAsync(cancellationToken);

            if (user is null)
                return ResponseDtoHelper.NotFound<ChangeRoleResultDto>
                    (AccountServiceMessages.UserNotFound);

            // the service users of the AI seats are managed by the system only
            if (global::Services.NasbNiro.Ai.AiSeatNames.IsSeatUser(user.UserName))
                return ResponseDtoHelper.BadRequest<ChangeRoleResultDto>(SeatUserMessage);

            int[] roleIds = changeRoleDto.RoleIds ?? Array.Empty<int>();
            List<Role> wanted = await _roleManager.Roles.Where(role => roleIds.Contains(role.Id)).ToListAsync(cancellationToken);

            // nobody changes his or her own roles: another PMO user does it (and a sensitive role needs a third look anyway).
            // R1a2 (M-1): the one exception is a PMO user who takes the PMO role from himself (unless he is the last active one)
            if (user.Id == Me)
            {
                if (await ClassifyOwnRoleChangeAsync(user, wanted, cancellationToken) == OwnRoleChange.StepDown)
                {
                    var step = await StepDownAsync(user.Id, cancellationToken);
                    return step.Done
                        ? ResponseDtoHelper.Ok(ResultOf(step.Roles, null), AccountServiceMessages.SteppedDown)
                        : ResponseDtoHelper.BadRequest<ChangeRoleResultDto>(step.Error);
                }

                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Access, user.Id, IdentityAudit.Refused, $"تلاش برای تغییر نقش‌های خود «{NameOf(user)}»");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper.Forbidden<ChangeRoleResultDto>(AccountServiceMessages.CannotChangeOwnRoles);
            }

            RoleOutcome outcome = await ApplyRolesAsync(user, wanted, cancellationToken);
            if (outcome.Error != null)
                return ResponseDtoHelper.BadRequest<ChangeRoleResultDto>(outcome.Error);

            IList<string> userCurrentRoles = await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken);
            string message = outcome.Note() ?? AccountServiceMessages.RolesChangedSuccessfully;
            return ResponseDtoHelper.Ok(ResultOf(userCurrentRoles, outcome), message);
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

        // ------------------------------------------------------------------------------------------ two-person approval of sensitive roles and of password resets

        /// <summary>Persian text of the code <see cref="IdentityRules.ApprovalProblem(int,int,int,RoleGrantRecord,int,DateTimeOffset)"/> returns; null for null.</summary>
        private static string ApprovalProblemText(string problem)
        {
            switch (problem)
            {
                case null: return null;
                case "NotSignedIn": return AccountServiceMessages.NotSignedIn;
                case "SameApprover": return AccountServiceMessages.GrantSameApprover;
                case "ApproverIsReceiver": return AccountServiceMessages.GrantApproverIsTarget;
                case "ApproverGrantedByRequester": return AccountServiceMessages.GrantApproverGrantedByRequester;
                case "ApproverTooNew": return AccountServiceMessages.GrantApproverTooNew;
                default: return AccountServiceMessages.GrantSameApprover;
            }
        }

        private static string ApprovalProblemAudit(string problem)
        {
            switch (problem)
            {
                case "SameApprover": return "تأیید توسط درخواست‌کننده";
                case "ApproverIsReceiver": return "تأیید توسط گیرنده‌ی نقش";
                case "ApproverGrantedByRequester": return "تأییدکننده‌ای که نقش PMO را به درخواست همین درخواست‌کننده گرفته";
                case "ApproverTooNew": return "تأییدکننده‌ای که هنوز مدت لازم PMO نبوده";
                default: return problem;
            }
        }

        /// <summary>Persian words for what a request asks (used in the trail): a reset, a deletion, the removal of a role, or a role.</summary>
        private static string WhatOf(PendingRoleGrant grant) =>
            grant.IsReset ? "بازنشانی رمز عبور"
            : grant.IsUserDelete ? "حذف حساب"
            : grant.IsRevoke ? "برداشتن نقش " + grant.Role
            : "نقش " + grant.Role;

        public async ValueTask<ResponseDto<List<RoleGrantDto>>> GetRoleGrantsAsync(CancellationToken cancellationToken)
        {
            if (!CallerIsPmo)
                return OnlyPmoAnswer<List<RoleGrantDto>>();

            DateTimeOffset now = DateTimeOffset.Now;
            int me = Me;
            RoleGrantRecord myPmoRecord = await GetGrantRecordAsync(me, IdentityRules.Pmo, cancellationToken);
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

            // R1a2 (M-2): a sensitive role waits until its receiver has chosen a password of his own (this does not depend on who looks)
            HashSet<int> mustChange = await ReceiversWhoMustChangeAsync(parsed.Where(p => p.Grant.IsRole).Select(p => p.Row.UserId), cancellationToken);

            var list = parsed
                .Where(p => users.Any(u => u.Id == p.Row.UserId && !u.IsDeleted))
                .Select(p =>
                {
                    string problem = IdentityRules.ApprovalProblem(p.Grant.RequestedBy, me, p.Row.UserId, myPmoRecord, Rules.ApproverMinDays, now);
                    string reason = ApprovalProblemText(problem);
                    if (reason == null && p.Grant.IsRole && mustChange.Contains(p.Row.UserId))
                        reason = AccountServiceMessages.GrantReceiverMustChangeOwnPassword;
                    return new RoleGrantDto
                    {
                        Id = p.Row.Id,
                        UserId = p.Row.UserId,
                        UserName = users.First(u => u.Id == p.Row.UserId).UserName,
                        DisplayName = Display(p.Row.UserId),
                        Role = p.Grant.Role,
                        Kind = p.Grant.Kind,
                        RequestedByUserId = p.Grant.RequestedBy,
                        RequestedByName = Display(p.Grant.RequestedBy),
                        RequestedAt = p.Grant.RequestedAt,
                        ExpiresAt = p.Grant.ExpiresAt,
                        CanApprove = reason == null,
                        CannotApproveReason = reason,
                    };
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
            DateTimeOffset now = DateTimeOffset.Now;
            string what = WhatOf(grant);
            if (grant.IsExpired(now))
            {
                _uow.Set<UserClaim>().Remove(row);
                IdentityAudit.Add(_uow, _http, me, IdentityAudit.Roles, user.Id, IdentityAudit.GrantExpired, $"درخواست {what} برای «{NameOf(user)}» منقضی شد");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper.BadRequest<RoleGrantDto>(AccountServiceMessages.GrantExpired);
            }

            // R1a2 (L1): the request was valid when it was made; it must still be valid now: the user is active and the requester is still a PMO user.
            // (R1a2, M-1: a request to take the PMO role from a user or to delete him does not need the user to be active; an inactive PMO user is exactly who one may want to remove)
            User requester = await _userManager.FindByIdAsync(grant.RequestedBy.ToString()).WithCancellationTokenAsync(cancellationToken);
            bool requesterStillPmo = requester != null && requester.IsActive && !requester.IsDeleted
                                     && await _userManager.IsInRoleAsync(requester, IdentityRules.Pmo).WithCancellationTokenAsync(cancellationToken);
            bool userStillValid = user.IsActive || grant.IsRevoke;
            if (!userStillValid || !requesterStillPmo)
            {
                _uow.Set<UserClaim>().Remove(row);
                IdentityAudit.Add(_uow, _http, me, IdentityAudit.Roles, user.Id, IdentityAudit.GrantVoid,
                    $"درخواست {what} برای «{NameOf(user)}» بی‌اعتبار شد ({(!userStillValid ? "کاربر غیرفعال است" : "درخواست‌کننده دیگر PMO نیست")})");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper.BadRequest<RoleGrantDto>(AccountServiceMessages.GrantVoid);
            }

            // R1a2 (H1b): the approver is independent of the requester: not the requester, not the receiver, not a PMO user whose PMO role the requester gave, and not a new one
            RoleGrantRecord approverRecord = await GetGrantRecordAsync(me, IdentityRules.Pmo, cancellationToken);
            string problem = IdentityRules.ApprovalProblem(grant.RequestedBy, me, user.Id, approverRecord, Rules.ApproverMinDays, now);
            if (problem != null)
            {
                IdentityAudit.Add(_uow, _http, me, IdentityAudit.Access, user.Id, IdentityAudit.Refused,
                    $"تلاش ناروا برای تأیید {what} برای «{NameOf(user)}» ({ApprovalProblemAudit(problem)})");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper.Forbidden<RoleGrantDto>(ApprovalProblemText(problem));
            }

            if (grant.IsReset)
            {
                ResetOutcome reset = await ResetInTransactionAsync(user.Id, null, grant.RequestedBy, me, row.Id, cancellationToken);
                if (reset.Error != null)
                    return ResponseDtoHelper.BadRequest<RoleGrantDto>(reset.Error);

                return ResponseDtoHelper.Ok(new RoleGrantDto
                {
                    Id = grantId, UserId = user.Id, UserName = user.UserName, DisplayName = user.DisplayName, Role = grant.Role, Kind = grant.Kind,
                    RequestedByUserId = grant.RequestedBy, RequestedAt = grant.RequestedAt, ExpiresAt = grant.ExpiresAt,
                    TemporaryPassword = reset.TemporaryPassword,
                }, AccountServiceMessages.ResetApproved);
            }

            // R1a2 (M-1): taking the PMO role from a user, or deleting a PMO user
            if (grant.IsRevoke)
                return await ApproveRevokeAsync(row, grant, user, me, cancellationToken);

            // R1a2 (M-2): the PMO user who registered the receiver, or who reset his password, may know it; the role waits until the receiver has chosen a password of his own
            if (await ReceiverMustChangeOwnPasswordAsync(user.Id, cancellationToken))
            {
                IdentityAudit.Add(_uow, _http, me, IdentityAudit.Access, user.Id, IdentityAudit.Refused,
                    $"تلاش برای تأیید {what} برای «{NameOf(user)}» پیش از آنکه خود گیرنده رمز عبورش را عوض کند");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper.Forbidden<RoleGrantDto>(AccountServiceMessages.GrantReceiverMustChangeOwnPassword);
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
            AddGrantRecord(user.Id, role.Name, grant.RequestedBy, me, bootstrap: false);
            RotateStamp(user); // H2: the access tokens made before the role was given end; the refresh token brings one that carries it
            IdentityAudit.Add(_uow, _http, me, IdentityAudit.Roles, user.Id, IdentityAudit.GrantApproved,
                $"تأیید نقش حساس {role.Name} برای «{NameOf(user)}» (درخواست‌کننده: کاربر {grant.RequestedBy}، تأییدکننده: کاربر {me})",
                before: new { role = role.Name, requestedBy = grant.RequestedBy }, after: new { role = role.Name, approvedBy = me });
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper.Ok(new RoleGrantDto
            {
                Id = grantId, UserId = user.Id, UserName = user.UserName, DisplayName = user.DisplayName, Role = role.Name, Kind = grant.Kind,
                RequestedByUserId = grant.RequestedBy, RequestedAt = grant.RequestedAt, ExpiresAt = grant.ExpiresAt,
            }, AccountServiceMessages.GrantApproved);
        }

        /// <summary>
        /// R1a2 (M-1): the approval of a request to take the PMO role from a user, or to delete a PMO user's account. The caller checked that the request is still valid and that the
        /// approver is independent (not the requester, not the user, not a PMO user whose role the requester gave, not a new one).
        /// </summary>
        private async Task<ResponseDto<RoleGrantDto>> ApproveRevokeAsync(UserClaim row, PendingRoleGrant grant, User user, int me, CancellationToken cancellationToken)
        {
            string name = NameOf(user);
            if (grant.IsUserDelete)
            {
                IdentityResult deleted = await DeleteAccountAsync(user, cancellationToken);
                if (!deleted.Succeeded)
                    return ResponseDtoHelper.BadRequest<RoleGrantDto>(deleted.DumpErrors());
                IdentityAudit.Add(_uow, _http, me, IdentityAudit.User, user.Id, IdentityAudit.Deleted,
                    $"حذف مدیر (PMO) «{name}» با درخواست کاربر {grant.RequestedBy} و تأیید کاربر {me}");
            }
            else
            {
                IList<string> held = await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken);
                if (HasRole(held, IdentityRules.Pmo))
                {
                    IdentityResult removed = await _userManager.RemoveFromRoleAsync(user, IdentityRules.Pmo).WithCancellationTokenAsync(cancellationToken);
                    if (!removed.Succeeded)
                        return ResponseDtoHelper.BadRequest<RoleGrantDto>(AccountServiceMessages.FailedToAssignRole + removed.DumpErrors());
                    await MarkRolesRemovedAsync(user.Id, new[] { IdentityRules.Pmo }, cancellationToken);
                    RotateStamp(user); // the access tokens made with the PMO role end now; the refresh token brings one without it
                }
            }

            _uow.Set<UserClaim>().Remove(row);
            IdentityAudit.Add(_uow, _http, me, IdentityAudit.Roles, user.Id, IdentityAudit.RevokeApproved,
                $"تأیید {WhatOf(grant)} برای «{name}» (درخواست‌کننده: کاربر {grant.RequestedBy}، تأییدکننده: کاربر {me})",
                before: new { role = grant.Role, requestedBy = grant.RequestedBy }, after: new { approvedBy = me });
            await _uow.SaveChangesAsync(cancellationToken);

            return ResponseDtoHelper.Ok(new RoleGrantDto
            {
                Id = row.Id, UserId = user.Id, UserName = user.UserName, DisplayName = user.DisplayName, Role = grant.Role, Kind = grant.Kind,
                RequestedByUserId = grant.RequestedBy, RequestedAt = grant.RequestedAt, ExpiresAt = grant.ExpiresAt,
            }, AccountServiceMessages.RevokeApproved);
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

            // R1a2 (L-4): the target of a password reset, or of the removal of his PMO role / the deletion of his account, cannot reject that request himself: a hijacked session of a
            // PMO user could otherwise block its own reset or removal for good. (He may reject a request to give himself a role: that only takes power away.)
            if ((grant.IsReset || grant.IsRevoke) && row.UserId == Me)
            {
                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Access, row.UserId, IdentityAudit.Refused,
                    $"تلاش برای رد {WhatOf(grant)} درباره‌ی حساب خود «{NameOf(user)}»");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper.Forbidden<bool>(AccountServiceMessages.CannotRejectOwnRequest);
            }

            _uow.Set<UserClaim>().Remove(row);
            IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Roles, row.UserId, IdentityAudit.GrantRejected,
                $"رد یا پس‌گرفتن درخواست {WhatOf(grant)} برای «{NameOf(user)}»",
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
                // R1a2 (L5): the roles whose name holds the typed text are found here (a few dozen roles); the users are then filtered by role id
                string typedRole = userPaginationParams.Role.Trim();
                int[] roleIds = (await _roleManager.Roles.AsNoTracking().Select(r => new { r.Id, r.Name }).ToListAsync(cancellationToken))
                    .Where(r => r.Name != null && r.Name.IndexOf(typedRole, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(r => r.Id)
                    .ToArray();
                users = users.Where(u => u.Roles.Any(x => roleIds.Contains(x.RoleId)));
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
                {
                    // R1a2 (L5): the role is found by name here and the users are filtered by its id (no join on the role name inside the user query)
                    Role found = await _roleManager.FindByNameAsync(roleKey).WithCancellationTokenAsync(cancellationToken);
                    int foundId = found?.Id ?? -1;
                    users = users.Where(u => u.Roles.Any(x => x.RoleId == foundId));
                }
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

            // a PMO user may correct his or her own details, but not the list of roles; this is decided before anything is changed.
            // R1a2 (M-1): the one exception is a PMO user who takes the PMO role from himself (unless he is the last active one)
            bool isSelf = user.Id == Me;
            bool steppingDown = false;
            if (isSelf)
            {
                OwnRoleChange own = await ClassifyOwnRoleChangeAsync(user, wanted, cancellationToken);
                if (own == OwnRoleChange.Refused)
                {
                    IdentityAudit.Add(_uow, _http, Me, IdentityAudit.Access, user.Id, IdentityAudit.Refused, $"تلاش برای تغییر نقش‌های خود «{NameOf(user)}» از راه ویرایش کاربر");
                    await _uow.SaveChangesAsync(cancellationToken);
                    return ResponseDtoHelper.Forbidden<ResponseUserDto>(AccountServiceMessages.CannotChangeOwnRoles);
                }
                steppingDown = own == OwnRoleChange.StepDown;
                // checked here as well so that nothing is saved when the answer is no (StepDownAsync checks again under the lock)
                if (steppingDown && await CountActivePmoAsync(new[] { Me }, cancellationToken) == 0)
                    return ResponseDtoHelper.BadRequest<ResponseUserDto>(AccountServiceMessages.LastPmoCannotStepDown);
            }

            _mapper.Map(updateUserDto, user);

            IdentityResult result = await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

            if (result.Succeeded)
            {
                string message = null;
                RoleOutcome outcome = null;
                if (!isSelf)
                {
                    outcome = await ApplyRolesAsync(user, wanted, cancellationToken);
                    if (outcome.Error != null)
                        return ResponseDtoHelper.BadRequest<ResponseUserDto>(outcome.Error);
                    message = outcome.Note()
                        ?? string.Join(",", await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken));
                }
                else if (steppingDown)
                {
                    var step = await StepDownAsync(user.Id, cancellationToken);
                    if (!step.Done)
                        return ResponseDtoHelper.BadRequest<ResponseUserDto>(step.Error);
                    message = AccountServiceMessages.SteppedDown;
                }

                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.User, user.Id, IdentityAudit.Updated, $"ویرایش کاربر «{NameOf(user)}»");
                await _uow.SaveChangesAsync(cancellationToken);

                ResponseUserDto updated = (await LoadUserDtoAsync(user.Id, cancellationToken)).Data;
                AttachPending(updated, outcome); // R1a2 (L-9): { pending, grantId, grantIds }
                return ResponseDtoHelper.Ok(updated, message);
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

            // R1a2 (M-1): deleting a PMO user is a request that a different PMO user approves (deleting the other PMO user is how one person could bring the bootstrap back).
            // The answer is a success with data false and a message that says that the deletion is waiting.
            if (HasRole(await _userManager.GetRolesAsync(user).WithCancellationTokenAsync(cancellationToken), IdentityRules.Pmo))
                return await RequestDeleteOfPmoAsync(user, cancellationToken);

            IdentityResult result = await DeleteAccountAsync(user, cancellationToken);

            if (result.Succeeded)
            {
                IdentityAudit.Add(_uow, _http, Me, IdentityAudit.User, user.Id, IdentityAudit.Deleted, $"حذف کاربر «{NameOf(user)}»");
                await _uow.SaveChangesAsync(cancellationToken);
                return ResponseDtoHelper
                    .Ok(true, "کاربر حذف شد.");
            }

            return ResponseDtoHelper
                    .BadRequest<bool>(result.DumpErrors());
        }

        /// <summary>
        /// Marks the account deleted (soft delete) and ends its sessions. R1a2 (H2): the security stamp changes, so the access tokens made before end at once, not when they expire.
        /// The caller writes the audit row and saves.
        /// </summary>
        private async Task<IdentityResult> DeleteAccountAsync(User user, CancellationToken cancellationToken)
        {
            user.IsDeleted = true;
            RotateStamp(user);
            IdentityResult result = await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);
            // the deleted user's sessions end now
            if (result.Succeeded) await _tokenStoreService.InvalidateUserTokensAsync(user.Id).WithCancellationTokenAsync(cancellationToken);
            return result;
        }

        /// <summary>R1a2 (M-1): the request to delete a PMO user (a claim of that user, kind "revoke", role USER_DELETE) that a different PMO user approves in role-grants/{id}/approve.</summary>
        private async Task<ResponseDto<bool>> RequestDeleteOfPmoAsync(User user, CancellationToken cancellationToken)
        {
            var open = await OpenGrantsOfAsync(user.Id, cancellationToken);
            if (open.Any(o => o.Grant.IsUserDelete))
                return ResponseDtoHelper.BadRequest<bool>(AccountServiceMessages.DeleteAlreadyPending);

            // a request that nobody could approve (the requester and the user are the only PMO users) is not made; the user can step down himself instead
            if (await CountActivePmoAsync(new[] { Me, user.Id }, cancellationToken) == 0)
                return ResponseDtoHelper.BadRequest<bool>(AccountServiceMessages.RevokeNoApprover);

            var grant = new PendingRoleGrant { Role = IdentityRules.DeleteRoleLabel, RequestedBy = Me, RequestedAt = DateTimeOffset.Now, Kind = IdentityRules.KindRevoke };
            _uow.Set<UserClaim>().Add(new UserClaim { UserId = user.Id, ClaimType = IdentityRules.PendingRoleGrantClaimType, ClaimValue = grant.ToClaimValue() });
            IdentityAudit.Add(_uow, _http, Me, IdentityAudit.User, user.Id, IdentityAudit.RevokeRequested,
                $"درخواست حذف مدیر (PMO) «{NameOf(user)}»؛ تا تأیید مدیر (PMO) دیگر انجام نمی‌شود",
                after: new { requestedBy = Me, expires = grant.ExpiresAt.ToString("O") });
            IdentityInbox.Alert(_uow, "revoke-request-" + user.Id, AccountServiceMessages.RevokeRequestedInbox, recipientUserId: user.Id);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(false, AccountServiceMessages.DeleteNeedsApproval);
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

            // R1a2 (H3): a file that a record refers to cannot be deleted, so the new photo is linked first and the old file is deleted after the link has moved
            Guid? previousPhoto = user.ProfilePhotoId;

            Guid documentId = await _documentService.UploadDocumentAsync(dto).WithCancellationTokenAsync(cancellationToken);

            user.ProfilePhotoId = documentId;

            IdentityResult result = await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);

            if (result.Succeeded)
            {
                if (previousPhoto.HasValue)
                {
                    try
                    {
                        await _documentService.DeleteDocumentAsync(previousPhoto.Value).WithCancellationTokenAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {

                        _logger.LogError(ex.Message, ex);
                    }
                }

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
                // R1a2 (H3): the user stops referring to the file first; only then can the file be deleted
                Guid photo = user.ProfilePhotoId.Value;
                user.ProfilePhotoId = null;
                IdentityResult unlinked = await _userManager.UpdateAsync(user).WithCancellationTokenAsync(cancellationToken);
                if (!unlinked.Succeeded)
                    return ResponseDtoHelper.BadRequest<bool>(unlinked.DumpErrors());

                bool deleteDocumentResult = await _documentService.DeleteDocumentAsync(photo).WithCancellationTokenAsync(cancellationToken);

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
