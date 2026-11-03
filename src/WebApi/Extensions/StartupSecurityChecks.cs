using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace WebApi.Extensions
{
    /// <summary>
    /// R1a2 (L7): settings that must never reach a real server. <see cref="Problems"/> is checked at start-up; when it finds anything outside the Development
    /// environment the server does not start (a server that starts with a published key would hand out tokens that anybody who has read the source code can make).
    /// </summary>
    public static class StartupSecurityChecks
    {
        /// <summary>The signing key that ships in appsettings.json for the build machine and the tests.</summary>
        public const string ShippedCiKey = "CI-ONLY-JWT-KEY-0123456789-abcdefghijklmnopq";

        /// <summary>English text of every setting that is not acceptable outside the Development environment; empty when all is well (and always empty in Development).</summary>
        public static IReadOnlyList<string> Problems(IConfiguration configuration, bool isDevelopment)
        {
            var problems = new List<string>();
            if (isDevelopment || configuration == null) return problems;

            string key = configuration["BearerTokens:Key"];
            if (string.IsNullOrWhiteSpace(key))
                problems.Add("BearerTokens:Key is empty.");
            else if (string.Equals(key, ShippedCiKey, StringComparison.Ordinal) || key.StartsWith("CI-ONLY", StringComparison.OrdinalIgnoreCase))
                problems.Add("BearerTokens:Key is the signing key that ships with the source code, so anybody could make tokens. Set a private key "
                             + "(install.ps1 writes one into appsettings.Production.json, or set the environment variable BearerTokens__Key).");
            return problems;
        }
    }
}
