using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Context
{
    /// <summary>
    /// Package 9 (CEO decision D11): the AUDIT seat is an auditor, not a doer. Whatever code runs as the service user of the seat can write nothing but its own findings, runs, reports and process audits
    /// (the AuditSeat tables), the AI logs and the in-app notifications; a change to any other table is refused before it is saved. The guard is in the data layer, so no service can bypass it.
    /// </summary>
    public static class AuditorWriteGuard
    {
        /// <summary>The user name of the seat (AiSeatNames.UserName("AUDIT"); a test keeps the two equal).</summary>
        public const string SeatUserName = "seat.audit";

        private static readonly string[] AllowedNamespaces =
        {
            "Entities.Model.NasbNiro.AuditSeat",
            "Entities.Model.NasbNiro.Ai",
            "Entities.Model.NasbNiro.Office",
        };

        public static bool IsAuditSeat(IHttpContextAccessor accessor) =>
            string.Equals(accessor?.HttpContext?.User?.Identity?.Name, SeatUserName, StringComparison.OrdinalIgnoreCase);

        public static bool Allowed(Type entityType) => entityType != null && AllowedNamespaces.Contains(entityType.Namespace);

        internal static void Check(DbContext context, IHttpContextAccessor accessor)
        {
            if (!IsAuditSeat(accessor)) return;
            foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted))
                if (!Allowed(entry.Entity.GetType()))
                    throw new InvalidOperationException("ممیز داخلی هوشمند فقط خواندنی است و جز یافته‌ها، اجراها و گزارش‌های خودش چیزی نمی‌نویسد: " + entry.Entity.GetType().Name);
        }
    }
}
