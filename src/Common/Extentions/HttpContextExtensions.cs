using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Common.Extentions
{
    public static class HttpContextExtensions
    {
        public static string GetUserIp(this HttpContext httpContext)
        {
            if (httpContext is null)
                throw new ArgumentNullException(nameof(httpContext));

            return httpContext.Features
                              .Get<IHttpConnectionFeature>()?
                              .RemoteIpAddress?
                              .ToString();
        }

        /// <summary>
        /// Asks the browser not to cache the current response
        /// (replacement for the DNTCommon.Web.Core helper with the same name).
        /// </summary>
        public static void DisableBrowserCache(this HttpContext httpContext)
        {
            if (httpContext is null)
                throw new ArgumentNullException(nameof(httpContext));

            httpContext.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate, max-age=0";
            httpContext.Response.Headers["Expires"] = "-1";
            httpContext.Response.Headers["Pragma"] = "no-cache";
        }
    }
}
