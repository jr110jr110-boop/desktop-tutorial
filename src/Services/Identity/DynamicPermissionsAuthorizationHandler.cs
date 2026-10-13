using System.Threading.Tasks;
using Common.GuardToolkit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Services.Contracts.Identity;

namespace Services.Identity
{
    /// <summary>
    /// More info: http://www.dotnettips.info/post/2581
    /// </summary>
    public class DynamicPermissionRequirement : IAuthorizationRequirement
    {
    }

    public class DynamicPermissionsAuthorizationHandler : AuthorizationHandler<DynamicPermissionRequirement>
    {
        private readonly ISecurityTrimmingService _securityTrimmingService;

        public DynamicPermissionsAuthorizationHandler(ISecurityTrimmingService securityTrimmingService)
        {
            _securityTrimmingService = securityTrimmingService;
            _securityTrimmingService.ThrowIfNull(nameof(_securityTrimmingService));
        }

        protected override Task HandleRequirementAsync(
             AuthorizationHandlerContext context,
             DynamicPermissionRequirement requirement)
        {
            // With endpoint routing (ASP.NET Core 3+) the resource is the HttpContext;
            // with the old MVC routing it was an AuthorizationFilterContext.
            RouteValueDictionary routeValues;
            switch (context.Resource)
            {
                case HttpContext httpContext:
                    routeValues = httpContext.Request.RouteValues;
                    break;
                case AuthorizationFilterContext mvcContext:
                    routeValues = mvcContext.RouteData.Values;
                    break;
                default:
                    return Task.CompletedTask;
            }

            var area = getRouteValue(routeValues, "area");
            var controller = getRouteValue(routeValues, "controller");
            var action = getRouteValue(routeValues, "action");

            if (_securityTrimmingService.CanCurrentUserAccess(area, controller, action))
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }

            return Task.CompletedTask;
        }

        private static string getRouteValue(RouteValueDictionary routeValues, string key)
        {
            return routeValues != null && routeValues.TryGetValue(key, out var value) && value != null
                ? value.ToString()
                : string.Empty;
        }
    }
}
