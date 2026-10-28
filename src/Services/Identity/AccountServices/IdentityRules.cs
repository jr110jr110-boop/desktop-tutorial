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
        /// ★ Roles that open finance, pay, the CEO views, the audit trail, personnel data or the registry, and (since R1a2) the PMO role itself.
        /// Giving one of them needs a second PMO user to approve (the PMO who asks cannot approve it, and neither can the user who receives it).
        /// The one exception is the bootstrap of the second PMO user (<see cref="PmoBootstrapBelow"/>).
        /// </summary>
        public static readonly string[] SensitiveRoles = { "PMO", "FINANCEMANAGER", "PAYROLL", "CEO", "AUDITOR", "HR", "SECRETARIAT" };

        /// <summary>
        /// ★ While fewer than this many active PMO users exist a PMO grant is applied at once (there is nobody to approve it): an audit row "Bootstrap" is written and the CEO gets an inbox alert.
        /// From the second PMO user on, every PMO grant needs the approval of another PMO user.
        /// </summary>
        public const int PmoBootstrapBelow = 2;

        /// <summary>The temporary-password flag is a claim of the user (no table or column was added for it).</summary>
        public const string MustChangePasswordClaimType = "pmis:must-change-password";

        /// <summary>A role waiting for the second approver is a claim of the receiving user whose value is the JSON of <see cref="PendingRoleGrant"/>.</summary>
        public const string PendingRoleGrantClaimType = "pmis:pending-role-grant";

        /// <summary>R1a2 (H1): the record of a sensitive role that was given (who asked for it, who approved it, when): a claim of the receiving user, the JSON of <see cref="RoleGrantRecord"/>. It goes when the role is taken away.</summary>
        public const string RoleGrantedClaimType = "pmis:role-granted";

        /// <summary>The kinds of a pending request.</summary>
        public const string KindRole = "role";
        public const string KindReset = "reset";

        /// <summary>The "role" shown for a pending password reset in the list of requests.</summary>
        public const string ResetRoleLabel = "PASSWORD_RESET";

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
        /// A sensitive role that is not held yet is never added here: it becomes a request (or stays the open request),
        /// except the PMO role while <paramref name="pmoBootstrap"/> is true (fewer than <see cref="PmoBootstrapBelow"/> active PMO users).
        /// Taking a role away is immediate.
        /// </summary>
        public static RolePlan PlanRoles(IEnumerable<string> current, IEnumerable<string> desired, IEnumerable<string> alreadyPending, bool pmoBootstrap = false)
        {
            var have = new HashSet<string>((current ?? Enumerable.Empty<string>()).Select(Norm), StringComparer.Ordinal);
            var want = new HashSet<string>((desired ?? Enumerable.Empty<string>()).Select(Norm), StringComparer.Ordinal);
            var pending = new HashSet<string>((alreadyPending ?? Enumerable.Empty<string>()).Select(Norm), StringComparer.Ordinal);
            var plan = new RolePlan();
            foreach (string role in want.Where(r => !have.Contains(r)).OrderBy(r => r, StringComparer.Ordinal))
            {
                if (!IsSensitiveRole(role)) plan.AddNow.Add(role);
                else if (pmoBootstrap && role == Pmo) plan.AddNow.Add(role);   // the one exception: the second PMO user (the caller writes the audit row and the alert)
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

        /// <summary>
        /// The full rule of an approval (R1a2, H1): besides <see cref="ApprovalProblem(int,int,int)"/>
        ///  - "ApproverGrantedByRequester": the approver's own PMO role was given on the request of the same person who now asks (one PMO user cannot make an account for himself and let it approve);
        ///    a role given by the bootstrap is exempt, because the bootstrap is the audited start that makes two PMO users possible at all;
        ///  - "ApproverTooNew": the approver has held the PMO role for less than <paramref name="minDays"/> days.
        /// <paramref name="approverPmoRecord"/> is the record of the approver's PMO grant; null (the seeded administrator, users of before R1a2) means "held for long, nobody's doing".
        /// </summary>
        public static string ApprovalProblem(int requestedBy, int approver, int receiver, RoleGrantRecord approverPmoRecord, int minDays, DateTimeOffset now)
        {
            string basic = ApprovalProblem(requestedBy, approver, receiver);
            if (basic != null) return basic;
            if (approverPmoRecord == null) return null;
            if (!approverPmoRecord.Bootstrap && approverPmoRecord.GrantedBy == requestedBy) return "ApproverGrantedByRequester";
            if (minDays > 0 && now - approverPmoRecord.At < TimeSpan.FromDays(minDays)) return "ApproverTooNew";
            return null;
        }

        /// <summary>
        /// Reports "unknown name" without the name: the length and the first two characters, so an audit reader can tell a typing slip from an attack
        /// but a password typed into the name field never reaches the append-only trail (R1a2, M5).
        /// </summary>
        public static string NameHint(string typed)
        {
            if (string.IsNullOrEmpty(typed)) return "(خالی)";
            string first = Clip(typed, 2);
            return $"{typed.Length} نویسه، با شروع «{first}»";
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

    /// <summary>
    /// A request waiting for the second approver (the value of the claim <see cref="IdentityRules.PendingRoleGrantClaimType"/>): a sensitive role (<see cref="Kind"/> "role")
    /// or, since R1a2, the password reset of a PMO user or of a holder of a sensitive role (<see cref="Kind"/> "reset"; <see cref="Role"/> is then <see cref="IdentityRules.ResetRoleLabel"/>).
    /// </summary>
    public sealed class PendingRoleGrant
    {
        public string Role { get; set; }
        public int RequestedBy { get; set; }
        public DateTimeOffset RequestedAt { get; set; }

        /// <summary><see cref="IdentityRules.KindRole"/> (the default, and what every row written before R1a2 is) or <see cref="IdentityRules.KindReset"/>.</summary>
        public string Kind { get; set; } = IdentityRules.KindRole;

        public bool IsReset => string.Equals(Kind, IdentityRules.KindReset, StringComparison.Ordinal);

        public DateTimeOffset ExpiresAt => RequestedAt + IdentityRules.PendingGrantLifetime;

        public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

        internal sealed class Wire
        {
            public string role { get; set; }
            public int by { get; set; }
            public string at { get; set; }
            public string kind { get; set; }
        }

        public string ToClaimValue() => JsonSerializer.Serialize(new Wire { role = Role, by = RequestedBy, at = RequestedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture), kind = IsReset ? IdentityRules.KindReset : null });

        public static bool TryParse(string value, out PendingRoleGrant grant)
        {
            grant = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            try
            {
                Wire w = JsonSerializer.Deserialize<Wire>(value);
                if (w == null || string.IsNullOrWhiteSpace(w.role) || w.by <= 0) return false;
                if (!DateTimeOffset.TryParse(w.at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset at)) return false;
                grant = new PendingRoleGrant
                {
                    Role = w.role.Trim().ToUpperInvariant(), RequestedBy = w.by, RequestedAt = at,
                    Kind = string.Equals(w.kind, IdentityRules.KindReset, StringComparison.OrdinalIgnoreCase) ? IdentityRules.KindReset : IdentityRules.KindRole,
                };
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// R1a2 (H1): the record of a sensitive role that was given: who asked for it (<see cref="GrantedBy"/>), who approved it, when, and whether it came from the bootstrap of the second PMO user.
    /// It is a claim of the receiving user (<see cref="IdentityRules.RoleGrantedClaimType"/>); the approval rules read the approver's own PMO record.
    /// </summary>
    public sealed class RoleGrantRecord
    {
        public string Role { get; set; }
        public int GrantedBy { get; set; }
        public int ApprovedBy { get; set; }
        public DateTimeOffset At { get; set; }
        public bool Bootstrap { get; set; }

        internal sealed class Wire
        {
            public string role { get; set; }
            public int by { get; set; }
            public int approvedBy { get; set; }
            public string at { get; set; }
            public bool bootstrap { get; set; }
        }

        public string ToClaimValue() => JsonSerializer.Serialize(new Wire
        {
            role = Role, by = GrantedBy, approvedBy = ApprovedBy, at = At.ToString("O", System.Globalization.CultureInfo.InvariantCulture), bootstrap = Bootstrap,
        });

        public static bool TryParse(string value, out RoleGrantRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            try
            {
                Wire w = JsonSerializer.Deserialize<Wire>(value);
                if (w == null || string.IsNullOrWhiteSpace(w.role) || w.by <= 0) return false;
                if (!DateTimeOffset.TryParse(w.at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset at)) return false;
                record = new RoleGrantRecord { Role = w.role.Trim().ToUpperInvariant(), GrantedBy = w.by, ApprovedBy = w.approvedBy, At = at, Bootstrap = w.bootstrap };
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// The value of the "must change the password" claim (R1a2, L7). Why the flag was set decides whether it runs out:
    /// a temporary password that a PMO user set (<see cref="ForReset"/>) stops working after <c>Identity:TemporaryPasswordHours</c>;
    /// the password of the seeded administrator and the one-time change at an upgrade do not run out (nobody is to blame if the first sign-in comes late);
    /// the value "1" is what the flag was before R1a2 and does not run out either.
    /// </summary>
    public static class MustChangeFlag
    {
        public const string Seed = "seed";
        public const string Upgrade = "upgrade";
        private const string ResetPrefix = "reset:";

        public static string ForReset(DateTimeOffset issuedAt) => ResetPrefix + issuedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>True when the flag came from a reset and the temporary password is older than <paramref name="hours"/> hours. 0 or less switches the expiry off.</summary>
        public static bool IsExpired(string claimValue, DateTimeOffset now, int hours)
        {
            if (hours <= 0 || string.IsNullOrEmpty(claimValue) || !claimValue.StartsWith(ResetPrefix, StringComparison.Ordinal)) return false;
            if (!DateTimeOffset.TryParse(claimValue.Substring(ResetPrefix.Length), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset issued)) return false;
            return now - issued >= TimeSpan.FromHours(hours);
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
