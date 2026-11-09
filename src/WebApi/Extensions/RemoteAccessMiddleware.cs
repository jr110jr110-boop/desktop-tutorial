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

        public RemoteAccessMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<RemoteAccessMiddleware> logger)
        {
            _next = next;
            _policy = RemoteAccessPolicy.From(configuration);
            _logger = logger;
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
            bool signedIn = context.User?.Identity?.IsAuthenticated == true;
            var roles = signedIn ? context.User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value) : Enumerable.Empty<string>();
            RemoteAccessPolicy.Verdict v = _policy.Decide(context.Connection.RemoteIpAddress, context.Request.Method, context.Request.Path.Value, signedIn, roles);
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
        public static IApplicationBuilder UseRemoteAccessGate(this IApplicationBuilder app) => app.UseMiddleware<RemoteAccessMiddleware>();
    }
}
