using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.Remote;

namespace WebApi.Extensions
{
    /// <summary>
    /// Production release P2 part D: the remote-access gate (see <see cref="RemoteAccessPolicy"/>). It sits after the authentication (it needs the roles) and before the authorization and the
    /// endpoints, so it sees every request: the API, the pages and the files. Off unless Remote:Enabled is true.
    /// </summary>
    public sealed class RemoteAccessMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly RemoteAccessPolicy _policy;
        private readonly ILogger<RemoteAccessMiddleware> _logger;

        private readonly bool _networkOnly;

        public RemoteAccessMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<RemoteAccessMiddleware> logger, bool networkOnly)
        {
            _next = next;
            _networkOnly = networkOnly;
            _policy = RemoteAccessPolicy.From(configuration);
            _logger = logger;
            if (networkOnly) return;   // the second instance logs the settings once
            foreach (string p in _policy.Problems) _logger?.LogWarning("Remote access setting: {Problem}", p);
            if (_policy.Enabled && _policy.Vpn.Count == 0) _logger?.LogWarning("Remote access is enabled but Remote:VpnNetworks is empty: only the company network is served.");
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_policy.Enabled)
            {
                await _next(context);
                return;
            }
            bool forwarded = context.Request.Headers.ContainsKey("X-Forwarded-For") || context.Request.Headers.ContainsKey("Forwarded");
            RemoteAccessPolicy.Verdict v;
            if (_networkOnly) v = _policy.DecideNetwork(context.Connection.RemoteIpAddress, forwarded);
            else
            {
                bool signedIn = context.User?.Identity?.IsAuthenticated == true;
                var roles = signedIn ? context.User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value) : Enumerable.Empty<string>();
                v = _policy.Decide(context.Connection.RemoteIpAddress, context.Request.Method, context.Request.Path.Value, signedIn, roles, forwarded);
            }
            switch (v)
            {
                case RemoteAccessPolicy.Verdict.Local:
                    await _next(context);
                    return;
                case RemoteAccessPolicy.Verdict.Remote:
                    context.Items[RemoteAccessPolicy.ItemKey] = true;
                    context.Response.Headers["X-Pmis-Remote"] = _policy.ReadOnly ? "read-only" : "full";
                    await _next(context);
                    return;
                default:
                    if (v == RemoteAccessPolicy.Verdict.OutsideNetwork)
                        _logger?.LogWarning("Remote access refused for {Address}: outside the company network and VPN ({Method} {Path})", context.Connection.RemoteIpAddress, context.Request.Method, context.Request.Path.Value);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, message = RemoteAccessPolicy.Message(v), state = 403 }));
                    return;
            }
        }
    }

    public static class RemoteAccessMiddlewareExtensions
    {
        /// <summary>The network check, first in the pipeline: the open internet gets nothing, not even the API description.</summary>
        public static IApplicationBuilder UseRemoteNetworkGate(this IApplicationBuilder app) => app.UseMiddleware<RemoteAccessMiddleware>(true);

        /// <summary>The role and read-only check, after the authentication.</summary>
        public static IApplicationBuilder UseRemoteAccessGate(this IApplicationBuilder app) => app.UseMiddleware<RemoteAccessMiddleware>(false);
    }
}
