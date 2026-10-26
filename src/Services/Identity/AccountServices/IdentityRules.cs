using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Services.Identity.AccountServices
{
    /// <summary>
    /// The rules of user administration that need no database (so they are unit tested).
    /// ★ = provisional decisions of the security review R1a; each can be changed here in one place.
    /// </summary>
    public static class IdentityRules
    {
        /// <summary>★ Only the PMO administers users: register, update, delete, roles, the full user list, password reset, approval of sensitive roles.</summary>
        public const string Pmo = "PMO";

        /// <summary>
        /// ★ Roles that open finance, pay, the CEO views, the audit trail, personnel data or the registry.
        /// Giving one of them needs a second PMO user to approve (the PMO who asks cannot approve it, and neither can the user who receives it).
        /// </summary>
        public static readonly string[] SensitiveRoles = { "FINANCEMANAGER", "PAYROLL", "CEO", "AUDITOR", "HR", "SECRETARIAT" };

        /// <summary>The temporary-password flag is a claim of the user (no table or column was added for it).</summary>
        public const string MustChangePasswordClaimType = "pmis:must-change-password";

        /// <summary>A role waiting for the second approver is a claim of the receiving user whose value is the JSON of <see cref="PendingRoleGrant"/>.</summary>
        public const string PendingRoleGrantClaimType = "pmis:pending-role-grant";

        /// <summary>★ A request nobody approves within this time is void.</summary>
        public static readonly TimeSpan PendingGrantLifetime = TimeSpan.FromDays(7);

        public static bool IsSensitiveRole(string roleName) =>
            !string.IsNullOrWhiteSpace(roleName) && SensitiveRoles.Contains(roleName.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>What a role change does: which roles are given now, which wait for approval and which are taken away.</summary>
        public sealed class RolePlan
        {
            public List<string> AddNow { get; } = new List<string>();
            public List<string> NeedApproval { get; } = new List<string>();
            public List<string> AlreadyPending { get; } = new List<string>();
            public List<string> Remove { get; } = new List<string>();

            public bool IsEmpty => AddNow.Count == 0 && NeedApproval.Count == 0 && Remove.Count == 0;
        }

        /// <summary>
        /// Compares the roles the user has with the roles that are asked for. Names are compared without case.
        /// A sensitive role that is not held yet is never added here: it becomes a request (or stays the open request).
        /// Taking a role away is immediate.
        /// </summary>
        public static RolePlan PlanRoles(IEnumerable<string> current, IEnumerable<string> desired, IEnumerable<string> alreadyPending)
        {
            var have = new HashSet<string>((current ?? Enumerable.Empty<string>()).Select(Norm), StringComparer.Ordinal);
            var want = new HashSet<string>((desired ?? Enumerable.Empty<string>()).Select(Norm), StringComparer.Ordinal);
            var pending = new HashSet<string>((alreadyPending ?? Enumerable.Empty<string>()).Select(Norm), StringComparer.Ordinal);
            var plan = new RolePlan();
            foreach (string role in want.Where(r => !have.Contains(r)).OrderBy(r => r, StringComparer.Ordinal))
            {
                if (!IsSensitiveRole(role)) plan.AddNow.Add(role);
                else if (pending.Contains(role)) plan.AlreadyPending.Add(role);
                else plan.NeedApproval.Add(role);
            }
            foreach (string role in have.Where(r => !want.Contains(r)).OrderBy(r => r, StringComparer.Ordinal)) plan.Remove.Add(role);
            return plan;
        }

        private static string Norm(string role) => (role ?? "").Trim().ToUpperInvariant();

        /// <summary>Null when the approval is allowed; otherwise a short code of the reason ("NotSignedIn", "SameApprover" or "ApproverIsReceiver").</summary>
        public static string ApprovalProblem(int requestedBy, int approver, int receiver)
        {
            if (approver <= 0) return "NotSignedIn";
            if (approver == requestedBy) return "SameApprover";
            if (approver == receiver) return "ApproverIsReceiver";
            return null;
        }

        /// <summary>Safe text for the audit trail: no control characters, at most <paramref name="max"/> characters.</summary>
        public static string Clip(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string clean = new string(text.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
            return clean.Length <= max ? clean : clean.Substring(0, max);
        }

        private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string Lower = "abcdefghijkmnopqrstuvwxyz";
        private const string Digits = "23456789";
        private const string Symbols = "#-_.";

        /// <summary>A random temporary password (14 characters: upper case, lower case, digit and a symbol; no look-alike characters).</summary>
        public static string GenerateTemporaryPassword()
        {
            const int length = 14;
            var chars = new List<char>
            {
                Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols),
            };
            string all = Upper + Lower + Digits;
            while (chars.Count < length) chars.Add(Pick(all));
            // shuffle (Fisher-Yates) so the first characters are not predictable; a symbol is never first
            for (int i = chars.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }
            if (Symbols.IndexOf(chars[0]) >= 0)
            {
                int swap = 1 + RandomNumberGenerator.GetInt32(chars.Count - 1);
                (chars[0], chars[swap]) = (chars[swap], chars[0]);
            }
            return new string(chars.ToArray());
        }

        private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
    }

    /// <summary>A sensitive role waiting for the second approver (the value of the claim <see cref="IdentityRules.PendingRoleGrantClaimType"/>).</summary>
    public sealed class PendingRoleGrant
    {
        public string Role { get; set; }
        public int RequestedBy { get; set; }
        public DateTimeOffset RequestedAt { get; set; }

        public DateTimeOffset ExpiresAt => RequestedAt + IdentityRules.PendingGrantLifetime;

        public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

        internal sealed class Wire
        {
            public string role { get; set; }
            public int by { get; set; }
            public string at { get; set; }
        }

        public string ToClaimValue() => JsonSerializer.Serialize(new Wire { role = Role, by = RequestedBy, at = RequestedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture) });

        public static bool TryParse(string value, out PendingRoleGrant grant)
        {
            grant = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            try
            {
                Wire w = JsonSerializer.Deserialize<Wire>(value);
                if (w == null || string.IsNullOrWhiteSpace(w.role) || w.by <= 0) return false;
                if (!DateTimeOffset.TryParse(w.at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset at)) return false;
                grant = new PendingRoleGrant { Role = w.role.Trim().ToUpperInvariant(), RequestedBy = w.by, RequestedAt = at };
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>The password rule of the company (★ at least 10 characters with a letter and a digit); the validator and the temporary-password maker use the same code.</summary>
    public static class PasswordPolicy
    {
        public const int MinimumLength = 10;

        public static bool HasLetter(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsLetter);

        public static bool HasDigit(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsDigit);

        /// <summary>Persian text of the first broken rule, or null when the password is fine.</summary>
        public static string Problem(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinimumLength) return "کلمه‌ی عبور باید دست‌کم ۱۰ نویسه باشد.";
            if (!HasLetter(password)) return "کلمه‌ی عبور باید دست‌کم یک حرف داشته باشد.";
            if (!HasDigit(password)) return "کلمه‌ی عبور باید دست‌کم یک رقم داشته باشد.";
            return null;
        }
    }
}
