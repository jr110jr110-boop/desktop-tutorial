using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DataLayer.Context
{
    /// <summary>
    /// R1a2 (M4): the keyed hash that the audit trail writes next to a masked national code, phone number or bank account (<c>NationalCodeHash</c> and so on), so that a change which keeps
    /// the visible last four digits is still detectable. It was an unsalted SHA-256 cut to 64 bits: anybody with read access to the trail could try every national code
    /// (ten digits) against it. Now it is HMAC-SHA256 with a secret server key (<c>Identity:AuditHmacKey</c>, made by install.ps1 like the JWT key), so the values cannot be guessed back
    /// without the key. When no key is set no hash is written at all, only the masked text.
    /// </summary>
    public static class AuditHmac
    {
        private static byte[] _key;

        /// <summary>Sets the key (null or empty switches the hash off). Called once at start-up; tests call it too.</summary>
        public static void Configure(string key)
        {
            _key = string.IsNullOrWhiteSpace(key) ? null : Encoding.UTF8.GetBytes(key.Trim());
        }

        public static bool IsConfigured => _key != null;

        /// <summary>The hash (32 hexadecimal characters) of the value without spaces and upper-cased, or null when no key is set or the value is null.</summary>
        public static string Compute(string value)
        {
            byte[] key = _key;
            if (key == null || value == null) return null;
            string normalised = new string(value.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
            return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(normalised))).Substring(0, 32).ToLowerInvariant();
        }
    }
}
