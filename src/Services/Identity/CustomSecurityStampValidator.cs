using System;
using System.Threading.Tasks;
using Common.GuardToolkit;
using Entities.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services.Contracts.Identity;

namespace Services.Identity
{
    /// <summary>
    /// Keep track of on-line users
    /// </summary>
    public class CustomSecurityStampValidator : SecurityStampValidator<User>
    {
        private readonly IOptions<SecurityStampValidatorOptions> _options;
        private readonly IApplicationSignInManager _signInManager;
        private readonly ISiteStatService _siteStatService;

        // .NET 8+: ISystemClock is obsolete; the base class now takes an ILoggerFactory and uses TimeProvider.
        public CustomSecurityStampValidator(
            IOptions<SecurityStampValidatorOptions> options,
            IApplicationSignInManager signInManager,
            ISiteStatService siteStatService,
            ILoggerFactory loggerFactory)
            : base(options, (SignInManager<User>)signInManager, loggerFactory)
        {
            _options = options;
            _options.ThrowIfNull(nameof(_options));

            _signInManager = signInManager;
            _signInManager.ThrowIfNull(nameof(_signInManager));

            _siteStatService = siteStatService;
            _siteStatService.ThrowIfNull(nameof(_siteStatService));
        }

        public TimeSpan UpdateLastModifiedDate { get; set; } = TimeSpan.FromMinutes(2);

        public override async Task ValidateAsync(CookieValidatePrincipalContext context)
        {
            await base.ValidateAsync(context);
        }
    }
}
