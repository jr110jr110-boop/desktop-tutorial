using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace WebApi.Extensions
{
    /// <summary>
    /// R1a2 (M1): the limit of the calls that anybody can make without a token (sign in, refresh token, temporary password, sign out):
    /// a fixed one-minute window per client address. Kept apart from <c>Startup</c> so that a unit test can run the very same partition.
    /// </summary>
    public static class IdentityRateLimit
    {
        /// <summary>The partition of a request: its client address (a request without an address shares the partition "?").</summary>
        public static RateLimitPartition<string> Partition(HttpContext httpContext, int permitsPerMinute) =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "?",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, permitsPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
    }
}
