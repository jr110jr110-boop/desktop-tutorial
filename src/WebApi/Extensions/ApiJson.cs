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

        public static JsonSerializerSettings Create()
        {
            var settings = new JsonSerializerSettings();
            Apply(settings);
            return settings;
        }
    }
}
