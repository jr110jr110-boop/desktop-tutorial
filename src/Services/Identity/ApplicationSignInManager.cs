using System.Threading.Tasks;
using Common.GuardToolkit;
using Entities.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services.Contracts.Identity;

namespace Services.Identity
{
    /// <summary>
    /// More info: http://www.dotnettips.info/post/2578
    /// </summary>
    public class ApplicationSignInManager :
        SignInManager<User>,
        IApplicationSignInManager
    {
        private readonly IApplicationUserManager _userManager;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly IUserClaimsPrincipalFactory<User> _claimsFactory;
        private readonly IOptions<IdentityOptions> _optionsAccessor;
        private readonly ILogger<ApplicationSignInManager> _logger;
        private readonly IAuthenticationSchemeProvider _schemes;

        public ApplicationSignInManager(
            IApplicationUserManager userManager,
            IHttpContextAccessor contextAccessor,
            IUserClaimsPrincipalFactory<User> claimsFactory,
            IOptions<IdentityOptions> optionsAccessor,
            ILogger<ApplicationSignInManager> logger,
            IAuthenticationSchemeProvider schemes,
            IUserConfirmation<User> confirmation) // required by the SignInManager constructor since ASP.NET Core 3.0
            : base((UserManager<User>)userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
        {
            _userManager = userManager;
            _userManager.ThrowIfNull(nameof(_userManager));

            _contextAccessor = contextAccessor;
            _contextAccessor.ThrowIfNull(nameof(_contextAccessor));

            _claimsFactory = claimsFactory;
            _claimsFactory.ThrowIfNull(nameof(_claimsFactory));

            _optionsAccessor = optionsAccessor;
            _optionsAccessor.ThrowIfNull(nameof(_optionsAccessor));

            _logger = logger;
            _logger.ThrowIfNull(nameof(_logger));

            _schemes = schemes;
            _schemes.ThrowIfNull(nameof(_schemes));
        }

        #region BaseClass

        Task<bool> IApplicationSignInManager.IsLockedOutAsync(User user)
        {
            return base.IsLockedOut(user);
        }

        Task<SignInResult> IApplicationSignInManager.LockedOutAsync(User user)
        {
            return base.LockedOut(user);
        }

        Task<SignInResult> IApplicationSignInManager.PreSignInCheckAsync(User user)
        {
            return base.PreSignInCheck(user);
        }

        Task IApplicationSignInManager.ResetLockoutAsync(User user)
        {
            return base.ResetLockout(user);
        }

        Task<SignInResult> IApplicationSignInManager.SignInOrTwoFactorAsync(User user, bool isPersistent, string loginProvider, bool bypassTwoFactor)
        {
            return base.SignInOrTwoFactorAsync(user, isPersistent, loginProvider, bypassTwoFactor);
        }

        #endregion

        #region CustomMethods

        public bool IsCurrentUserSignedIn()
        {
            return IsSignedIn(_contextAccessor.HttpContext.User);
        }

        public Task<User> ValidateCurrentUserSecurityStampAsync()
        {
            return ValidateSecurityStampAsync(_contextAccessor.HttpContext.User);
        }

        #endregion
    }
}
