using System;
using System.Linq;
using Common.GuardToolkit;
using Common.PersianToolkit;
using DNTPersianUtils.Core;
using Microsoft.AspNetCore.Identity;

namespace Services.Identity
{
    /// <summary>
    /// More info: http://www.dotnettips.info/post/2579
    /// </summary>
    /// <remarks>
    /// ASP.NET Core 3+ split ILookupNormalizer.Normalize() into NormalizeName() and NormalizeEmail().
    /// Both call the original Normalize() logic, so the normalized user names/emails already stored
    /// in the database keep matching exactly as before.
    /// </remarks>
    public class CustomNormalizer : ILookupNormalizer
    {
        public string NormalizeName(string name)
        {
            return Normalize(name);
        }

        public string NormalizeEmail(string email)
        {
            return Normalize(email);
        }

        public string Normalize(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            key = key.Trim();

            if (key.IsEmailAddress())
            {
                key = fixGmailDots(key);
            }
            else
            {
                // The local Common.PersianToolkit helpers are called explicitly, because DNTPersianUtils.Core
                // now has extension methods with the same names.
                key = key.ApplyCorrectYeKe();
                key = UnicodeExtensions.RemoveDiacritics(key);
                key = UnicodeExtensions.CleanUnderLines(key);
                key = UnicodeExtensions.RemovePunctuation(key);
                key = key.Trim().Replace(" ", "");
            }

            key = key.ToUpperInvariant();
            return key;
        }

        private static string fixGmailDots(string email)
        {
            email = email.ToLowerInvariant().Trim();
            var emailParts = email.Split('@');
            var name = emailParts[0].Replace(".", string.Empty);

            var plusIndex = name.IndexOf("+", StringComparison.OrdinalIgnoreCase);
            if (plusIndex != -1)
            {
                name = name.Substring(0, plusIndex);
            }

            var emailDomain = emailParts[1];
            emailDomain = emailDomain.Replace("googlemail.com", "gmail.com");

            string[] domainsAllowedDots =
            {
                "gmail.com",
                "facebook.com"
            };

            var isFromDomainsAllowedDots = domainsAllowedDots.Any(domain => emailDomain.Equals(domain));
            return !isFromDomainsAllowedDots ? email : string.Format("{0}@{1}", name, emailDomain);
        }
    }
}
