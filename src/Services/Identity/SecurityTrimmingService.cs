using System.Security.Claims;
using Common.GuardToolkit;
using Entities.Identity.Constants;
using Microsoft.AspNetCore.Http;
using Services.Contracts.Identity;

namespace Services.Identity
{
    /// <summary>
    /// More info: http://www.dotnettips.info/post/2581
    /// </summary>
    /// <remarks>
    /// The check that the area/controller/action is decorated with the DynamicPermission policy used to rely on
    /// DNTCommon.Web.Core's IMvcActionsDiscoveryService. No controller uses that policy, so the check was dropped
    /// together with the library; the permission check itself is unchanged.
    /// </remarks>
    public class SecurityTrimmingService : ISecurityTrimmingService
    {
        private readonly HttpContext _httpContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SecurityTrimmingService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
            _httpContextAccessor.ThrowIfNull(nameof(_httpContextAccessor));

            _httpContext = _httpContextAccessor.HttpContext;
        }

        public bool CanCurrentUserAccess(string area, string controller, string action)
        {
            return _httpContext != null && CanUserAccess(_httpContext.User, area, controller, action);
        }

        public bool CanUserAccess(ClaimsPrincipal user, string area, string controller, string action)
        {
            var currentClaimValue = $"{area}:{controller}:{action}";

            if (user?.Identity == null || !user.Identity.IsAuthenticated)
            {
                return false;
            }

            if (user.IsInRole(RoleConstants.PMO))
            {
                // Admin users have access to all of the pages.
                return true;
            }

            // Check for dynamic permissions
            // A user gets its permissions claims from the `ApplicationClaimsPrincipalFactory` class automatically and it includes the role claims too.
            return user.HasClaim(claim => claim.Type == PolicyConstants.DynamicPermissionClaimType &&
                                          claim.Value == currentClaimValue);
        }
    }
}
