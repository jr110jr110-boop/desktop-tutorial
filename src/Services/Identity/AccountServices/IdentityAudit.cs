using System;
using System.Text.Json;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
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
}
