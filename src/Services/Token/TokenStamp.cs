using System;
using System.Security.Cryptography;
using System.Text;

namespace Services.Token
{
    /// <summary>
    /// R1a2 (H2): the access token carries a keyed hash of the user's security stamp (claim <see cref="ClaimType"/>) and every request compares it with the stamp in the database.
    /// The stamp changes when the password changes or is reset, when a role is taken away or given by approval, when the user is deleted or deactivated and at logout,
    /// so every access token made before that stops working at once instead of living on for the rest of its hour.
    /// The raw stamp is never put into the token (a token is only signed, not encrypted).
    /// </summary>
    public static class TokenStamp
    {
        public const string ClaimType = "pmis:stamp";

        /// <summary>The value for the token claim: HMAC-SHA256 of the stamp with the token signing key, as 32 URL-safe characters.</summary>
        public static string Compute(string securityStamp, string signingKey)
        {
            byte[] key = Encoding.UTF8.GetBytes(signingKey ?? "");
            byte[] data = Encoding.UTF8.GetBytes("pmis-stamp|" + (securityStamp ?? ""));
            byte[] mac = HMACSHA256.HashData(key, data);
            return Convert.ToBase64String(mac).Replace('+', '-').Replace('/', '_').TrimEnd('=').Substring(0, 32);
        }

        /// <summary>True when the claim of the token belongs to the stamp the user has now (compared without leaking where they differ).</summary>
        public static bool Matches(string tokenClaim, string securityStamp, string signingKey)
        {
            if (string.IsNullOrEmpty(tokenClaim)) return false;
            byte[] expected = Encoding.UTF8.GetBytes(Compute(securityStamp, signingKey));
            byte[] given = Encoding.UTF8.GetBytes(tokenClaim);
            return given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected);
        }
    }
}
