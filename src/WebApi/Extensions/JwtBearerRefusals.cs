using System.Text;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;

namespace WebApi.Extensions
{
    /// <summary>
    /// R1a: what the bearer-token handler answers when it refuses a request: 401 when there is no valid token, 403 when the role attribute
    /// of the action does not list any role of the signed-in user. Without these two events the handler answers with an empty body
    /// (and <c>UseStatusCodePages</c> turns that into plain text) while the services answer a <c>ResponseDto</c>; the front-end and the
    /// smoke checks would have to read two shapes. Both kinds of refusal now carry the same JSON
    /// (<c>success:false</c>, <c>message</c>, <c>data:null</c>, <c>httpStatusCode</c>), written with the settings of <see cref="ApiJson"/>.
    /// The HTTP status code is unchanged (401 / 403).
    /// </summary>
    public static class JwtBearerRefusals
    {
        /// <summary>401: no token, an invalid or expired token, or an account that is deleted or inactive.</summary>
        public const string SignInRequired = "برای دسترسی باید وارد سامانه شوید.";

        /// <summary>403: the user is signed in, but none of his roles is listed by the action. Contains «مجوز» like the refusals of the services.</summary>
        public const string NotPermitted = "شما مجوز دسترسی به این عملیات را ندارید.";

        /// <summary>429: the rate limit of the sign-in calls (R1a2, M1) was passed; the same JSON as every other refusal.</summary>
        public const string TooManyRequests = "تعداد درخواست‌ها از حد مجاز بیشتر است؛ کمی بعد دوباره تلاش کنید.";

        private static readonly JsonSerializerSettings Settings = ApiJson.Create();

        /// <summary>The answer of the rate limiter (HTTP 429) with the JSON body and, when the limiter knows it, the Retry-After header.</summary>
        public static Task WriteTooManyRequestsAsync(HttpResponse response, System.TimeSpan? retryAfter)
        {
            if (response.HasStarted) return Task.CompletedTask;
            if (retryAfter.HasValue) response.Headers[HeaderNames.RetryAfter] = ((int)System.Math.Ceiling(retryAfter.Value.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            ResponseDto<object> body = new ResponseDto<object>(false, TooManyRequests, null).HasHttpStatusCode((System.Net.HttpStatusCode)429);
            return WriteAsync(response, body);
        }

        public static Task OnChallengeAsync(JwtBearerChallengeContext context)
        {
            // The handler would set 401 and the WWW-Authenticate header and leave the body empty. Handling the response here means it
            // does neither, so both are set below exactly as the handler sets them, and the JSON body is added.
            context.HandleResponse();

            HttpResponse response = context.Response;
            if (response.HasStarted) return Task.CompletedTask;

            response.Headers[HeaderNames.WWWAuthenticate] = ChallengeHeader(context);
            return WriteAsync(response, ResponseDtoHelper.Unauthorized<object>(SignInRequired));
        }

        public static Task OnForbiddenAsync(ForbiddenContext context)
        {
            HttpResponse response = context.Response;
            if (response.HasStarted) return Task.CompletedTask;

            return WriteAsync(response, ResponseDtoHelper.Forbidden<object>(NotPermitted));
        }

        private static Task WriteAsync(HttpResponse response, ResponseDto<object> body)
        {
            response.StatusCode = (int)body.HttpStatusCode;
            response.ContentType = "application/json; charset=utf-8";
            return response.WriteAsync(JsonConvert.SerializeObject(body, Settings), Encoding.UTF8);
        }

        /// <summary>
        /// The value of the WWW-Authenticate header, built the way the bearer handler builds it (RFC 6750, section 3.1):
        /// "Bearer" alone when nothing failed (no token sent), otherwise with the error and its description (for example an expired token).
        /// </summary>
        private static string ChallengeHeader(JwtBearerChallengeContext context)
        {
            string challenge = string.IsNullOrEmpty(context.Options.Challenge) ? "Bearer" : context.Options.Challenge;

            if (string.IsNullOrEmpty(context.Error) && string.IsNullOrEmpty(context.ErrorDescription) && string.IsNullOrEmpty(context.ErrorUri))
                return challenge;

            var builder = new StringBuilder(challenge);
            if (challenge.IndexOf(' ') > 0) builder.Append(',');

            if (!string.IsNullOrEmpty(context.Error))
                builder.Append(" error=\"").Append(context.Error).Append('"');

            if (!string.IsNullOrEmpty(context.ErrorDescription))
            {
                if (!string.IsNullOrEmpty(context.Error)) builder.Append(',');
                builder.Append(" error_description=\"").Append(context.ErrorDescription).Append('"');
            }

            if (!string.IsNullOrEmpty(context.ErrorUri))
            {
                if (!string.IsNullOrEmpty(context.Error) || !string.IsNullOrEmpty(context.ErrorDescription)) builder.Append(',');
                builder.Append(" error_uri=\"").Append(context.ErrorUri).Append('"');
            }

            return builder.ToString();
        }
    }
}
