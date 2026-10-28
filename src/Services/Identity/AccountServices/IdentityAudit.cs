using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.AspNetCore.Http;

namespace Services.Identity.AccountServices
{
    /// <summary>
    /// Rows of the append-only audit trail (<see cref="FnAuditLog"/>) for identity events that are not a plain data change:
    /// sign-in success and failure (with the IP address), password change and reset, requests and approvals of sensitive roles, refused attempts.
    /// The plain changes of the user, role and claim rows are written by the generic audit trail of the database context.
    /// The row is only added to the context; the caller's next SaveChanges stores it.
    /// </summary>
    public static class IdentityAudit
    {
        public const string SignIn = "Identity.SignIn";
        public const string Password = "Identity.Password";
        public const string Roles = "Identity.Roles";
        public const string User = "Identity.User";
        public const string Access = "Identity.Access";

        // the Action column holds 20 characters
        public const string Success = "Success";
        public const string Failure = "Failure";
        public const string LockedOut = "LockedOut";
        public const string Blocked = "Blocked";
        public const string MustChange = "MustChange";
        public const string Changed = "Changed";
        public const string ChangeRefused = "ChangeRefused";
        public const string AdminReset = "AdminReset";
        public const string TempChanged = "TempChanged";
        public const string RolesChanged = "RolesChanged";
        public const string GrantRequested = "GrantRequested";
        public const string GrantApproved = "GrantApproved";
        public const string GrantRejected = "GrantRejected";
        public const string GrantExpired = "GrantExpired";
        public const string Registered = "Registered";
        public const string Updated = "Updated";
        public const string Deleted = "Deleted";
        public const string Refused = "Refused";
        public const string PhotoChanged = "PhotoChanged";

        // R1a2
        public const string Bootstrap = "Bootstrap";                 // the second PMO user was given the role at once (fewer than two active PMO users existed)
        public const string ResetRequested = "ResetRequested";       // a PMO user asked for the password reset of a PMO or sensitive-role holder
        public const string GrantVoid = "GrantVoid";                 // a pending request that can no longer be approved (requester or user not valid any more, role dropped)
        public const string TokenReuse = "TokenReuse";               // a refresh token that was already used was presented again: every session of the user was ended
        public const string UnknownUsers = "UnknownUsers";           // sign-in attempts with names that do not exist, counted per address and minute
        public const string LoopbackSignIn = "LoopbackSignIn";       // a locked PMO account signed in from the server itself (the documented recovery)
        public const string Logout = "Logout";
        public const string UpgradeForced = "UpgradeForced";         // the one-time forced password change of the upgrade ran (this row is its marker)
        public const string FileReplaced = "FileReplaced";           // the bytes of a stored file were replaced through the generic Documents endpoint (SHA-256 before and after)

        /// <param name="userId">The person the row is about: the actor of an administrator's action, the user who signs in for a sign-in event (0 when unknown).</param>
        /// <param name="entityId">The user the event concerns (the target of an administrator's action).</param>
        public static void Add(IUnitOfWork uow, IHttpContextAccessor http, int userId, string entityType, int? entityId, string action, string summary, object before = null, object after = null)
        {
            string ip = http?.HttpContext?.Connection?.RemoteIpAddress?.ToString();
            uow.Set<FnAuditLog>().Add(new FnAuditLog
            {
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                Summary = IdentityRules.Clip(summary, 290),
                BeforeJson = before == null ? null : JsonSerializer.Serialize(before),
                AfterJson = after == null ? null : JsonSerializer.Serialize(after),
                UserId = userId,
                At = DateTimeOffset.Now,
                Ip = IdentityRules.Clip(ip, 60),
            });
        }
    }

    /// <summary>
    /// R1a2 (M1): sign-in attempts with a name that does not exist are counted per client address and minute, and a row of the append-only trail is written only for the
    /// first one and when the count of the minute reaches 10, 100, 1000 (and so on), instead of one row per attempt. An attacker who floods the sign-in with made-up
    /// names can no longer fill the database (the rate limiter keeps the count low in the first place). The counter lives in memory: a restart only starts it again.
    /// </summary>
    public static class UnknownUserTally
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, (long Minute, int Count)> ByAddress = new Dictionary<string, (long, int)>(StringComparer.Ordinal);

        /// <summary>Counts one more attempt of this address in the minute of <paramref name="now"/> and returns the count of that minute.</summary>
        public static int Add(string address, DateTimeOffset now)
        {
            string key = address ?? "?";
            long minute = now.UtcDateTime.Ticks / TimeSpan.TicksPerMinute;
            lock (Gate)
            {
                if (ByAddress.Count > 5000)
                    foreach (string stale in ByAddress.Where(kv => kv.Value.Minute < minute - 1).Select(kv => kv.Key).ToList()) ByAddress.Remove(stale);
                int count = ByAddress.TryGetValue(key, out var entry) && entry.Minute == minute ? entry.Count + 1 : 1;
                ByAddress[key] = (minute, count);
                return count;
            }
        }

        /// <summary>True for the first attempt of a minute and for 10, 100, 1000 ... attempts: the counts that get a row.</summary>
        public static bool IsRowWorthy(int count)
        {
            if (count <= 1) return count == 1;
            while (count % 10 == 0) count /= 10;
            return count == 1;
        }

        /// <summary>For the tests: forget every count.</summary>
        public static void Reset()
        {
            lock (Gate) ByAddress.Clear();
        }
    }

    /// <summary>R1a2: security alerts to the inbox of the office module. The row is only added to the context; the caller's next SaveChanges stores it.</summary>
    internal static class IdentityInbox
    {
        public static void Alert(IUnitOfWork uow, string sourceKey, string title, string recipientRole = null, int? recipientUserId = null)
        {
            string text = IdentityRules.Clip(title, 290);
            uow.Set<OfInboxItem>().Add(new OfInboxItem
            {
                Kind = OfInboxKind.Alert,
                Title = text,
                SourceModule = "Identity",
                SourceType = "Security",
                SourceKey = IdentityRules.Clip(sourceKey, 190),
                Priority = OfUrgency.Urgent,
                Status = OfInboxStatus.Unread,
                CreatedAt = DateTimeOffset.Now,
                RecipientRole = string.IsNullOrWhiteSpace(recipientRole) ? null : recipientRole.Trim().ToUpperInvariant(),
                RecipientUserId = recipientUserId,
            });
        }
    }
}
