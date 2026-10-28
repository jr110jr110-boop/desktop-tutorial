using System;
using System.Security.Claims;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace WebApi.Extensions
{
    /// <summary>
    /// The JSON settings of every API answer: camelCase names, nulls written, reference loops ignored.
    /// Startup applies them to MVC; the answers that are written outside MVC (the 401 and 403 of the bearer handler, see
    /// <see cref="JwtBearerRefusals"/>) use the same code, so the casing and the shape cannot drift apart.
    /// </summary>
    public static class ApiJson
    {
        public static void Apply(JsonSerializerSettings settings)
        {
            settings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
            settings.NullValueHandling = NullValueHandling.Include;
            settings.ContractResolver = new DefaultContractResolver()
            {
                NamingStrategy = new CamelCaseNamingStrategy(true, true, true)
            };
        }

        /// <summary>
        /// R1a2 (M8): the same settings, with the sensitive legacy fields (prices, bank accounts) left out for the roles that do not work with them, see <see cref="LegacyFieldAccess"/>.
        /// Startup applies this to MVC once the HTTP context is available through DI; the user is asked again for every value that is written.
        /// </summary>
        public static void HideSensitiveFields(JsonSerializerSettings settings, Func<ClaimsPrincipal> currentUser)
        {
            settings.ContractResolver = new LegacyFieldContractResolver(currentUser);
        }

        public static JsonSerializerSettings Create()
        {
            var settings = new JsonSerializerSettings();
            Apply(settings);
            return settings;
        }
    }
}
