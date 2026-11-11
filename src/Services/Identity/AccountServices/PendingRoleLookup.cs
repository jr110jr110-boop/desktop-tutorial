using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace Services.Identity.AccountServices
{
    /// <summary>
    /// FX2: read-only look at the sensitive roles that wait for the second person (the open "role" requests, claims of the receiving user).
    /// Used by the "own pending roles" call, by the user list of the administrator and by the sign predicates of the project-control gates
    /// (a user whose role is not active yet is told why the sign button is disabled).
    /// </summary>
    public static class PendingRoleLookup
    {
        /// <summary>Persian label of a role in the messages about a pending request (the PMO role keeps the name the company uses for it).</summary>
        public static string Label(string role)
        {
            switch ((role ?? "").Trim().ToUpperInvariant())
            {
                case "PMO": return "PMO";
                case "CEO": return "مدیرعامل";
                case "FINANCEMANAGER": return "مدیر مالی";
                case "PAYROLL": return "حقوق و دستمزد";
                case "AUDITOR": return "حسابرس";
                case "HR": return "اداری و منابع انسانی";
                case "SECRETARIAT": return "دبیرخانه";
                default: return role;
            }
        }

        /// <summary>«نقش PMO شما منتظر تأیید نفر دوم است؛ پس از تأیید یک بار خارج و دوباره وارد شوید.» (several roles are joined with «، »).</summary>
        public static string WaitingText(IEnumerable<string> roles)
        {
            string[] labels = (roles ?? Enumerable.Empty<string>()).Select(Label).Distinct().ToArray();
            if (labels.Length == 0) return null;
            return (labels.Length == 1 ? "نقش " : "نقش‌های ") + string.Join("، ", labels) + " شما منتظر تأیید نفر دوم است؛ پس از تأیید یک بار خارج و دوباره وارد شوید.";
        }

        /// <summary>The open (not expired) sensitive-role requests of the given users, per user (role names in upper case, sorted, once each).</summary>
        public static async Task<Dictionary<int, List<PendingRoleGrant>>> OpenRoleRequestsAsync(IUnitOfWork uow, IEnumerable<int> userIds, CancellationToken cancellationToken)
        {
            int[] ids = (userIds ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct().ToArray();
            var result = new Dictionary<int, List<PendingRoleGrant>>();
            if (ids.Length == 0) return result;
            DateTimeOffset now = DateTimeOffset.Now;
            var rows = await uow.Set<UserClaim>().AsNoTracking()
                .Where(c => c.ClaimType == IdentityRules.PendingRoleGrantClaimType && ids.Contains(c.UserId))
                .Select(c => new { c.UserId, c.ClaimValue })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                if (!PendingRoleGrant.TryParse(row.ClaimValue, out PendingRoleGrant grant) || !grant.IsRole || grant.IsExpired(now)) continue;
                if (!result.TryGetValue(row.UserId, out List<PendingRoleGrant> list)) result[row.UserId] = list = new List<PendingRoleGrant>();
                if (!list.Any(g => g.Role == grant.Role)) list.Add(grant);
            }
            foreach (List<PendingRoleGrant> list in result.Values) list.Sort((a, b) => string.CompareOrdinal(a.Role, b.Role));
            return result;
        }

        /// <summary>The open sensitive-role requests of one user.</summary>
        public static async Task<List<PendingRoleGrant>> OpenRoleRequestsOfAsync(IUnitOfWork uow, int userId, CancellationToken cancellationToken)
        {
            var all = await OpenRoleRequestsAsync(uow, new[] { userId }, cancellationToken);
            return all.TryGetValue(userId, out List<PendingRoleGrant> list) ? list : new List<PendingRoleGrant>();
        }
    }
}
