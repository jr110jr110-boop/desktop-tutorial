using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DataLayer.Context
{
    /// <summary>
    /// Agent layer (owner's mandate: the AI never approves). Whatever code runs as the service user of an AI seat (user name "seat.*") cannot put its own user id into a column that records a
    /// decision of a person: approved, signed, released, decided, confirmed, accepted, checked, verified, reviewed, inspected, finalised, posted, closed, paid, rejected, returned, cancelled
    /// or voided by (the QA, PMO and CEO signature columns too). The refusal is in the data layer, so no service can bypass it, whatever role the seat holds and whatever a service forgot to check.
    /// A human user and a background job without a seat principal are not touched. The coordinator seat (AG-MASTER) is also limited to the tables of the agent layer, the AI module and the inbox.
    /// </summary>
    public static class SeatApprovalGuard
    {
        /// <summary>The user names of the seats start with this (AiSeatNames.UserPrefix; a test keeps the two equal).</summary>
        public const string SeatPrefix = "seat.";

        /// <summary>The user name of the coordinator seat (AiSeatNames.UserName("AG-MASTER"); a test keeps the two equal).</summary>
        public const string MasterUserName = "seat.ag-master";

        /// <summary>The user name of the project-initiation seat (AiSeatNames.UserName("AG-INIT"); a test keeps the two equal). It sends the PMO-side notices to the units without any approving role.</summary>
        public const string InitUserName = "seat.ag-init";

        public const string Message = "تایید، امضا و تصمیم با انسان است؛ ایجنت هوش مصنوعی آن را انجام نمی‌دهد";

        public const string MasterMessage = "ایجنت اصلی فقط جدول‌های ایجنت‌ها، هوش مصنوعی و کارتابل را می‌نویسد و رکورد واحدهای دیگر را تغییر نمی‌دهد: ";

        /// <summary>
        /// The columns of a decision by a person (int user ids), matched on the property name anywhere in it: ApprovedByUserId, PlanningApprovedByUserId, TechnicalReviewedByUserId, ClosedByUserId, ...;
        /// and the signature columns of the QA, PMO and CEO (QaUserId, PmoUserId, CeoUserId, VoidQaUserId, ...). The type must be int or int?.
        /// </summary>
        private static readonly Regex DecisionColumn = new Regex(
            @"(?:Approved|Signed|Released|Decided|Confirmed|Accepted|Checked|Verified|Reviewed|Inspected|Finalis|Finaliz|Posted|Closed|Paid|Rejected|Returned|Cancelled|Voided)\w*UserId$|(?:Qa|Pmo|Ceo)UserId$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// (entity type name, property name) pairs that a seat may write although the name matches. One entry: the model sweep found a name that is not a decision.
        /// An entry needs a comment that says which seat writes it and why it is not a decision.
        /// </summary>
        private static readonly HashSet<(string Entity, string Property)> Exceptions = new HashSet<(string, string)>
        {
            // A document check-out (a person takes an engineering deliverable to edit it), not a decision: the name only matches "Checked...ByUserId". Any seat may hold a check-out.
            ("PcEngineeringDeliverable", "CheckedOutByUserId"),
        };

        private static readonly string[] MasterAllowedNamespaces =
        {
            "Entities.Model.NasbNiro.Agents",
            "Entities.Model.NasbNiro.Ai",
            "Entities.Model.NasbNiro.Office",
        };

        /// <summary>
        /// The only tables outside those namespaces that the coordinator seat may write: the standard-gap report is the MAIN agent's (the employer's rule). None of the three has a decision column that the master writes
        /// (the gap confirmation, EnStandardGap.ConfirmedByUserId, stays guarded like every other decision column).
        /// </summary>
        private static readonly HashSet<string> MasterAllowedTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Entities.Model.NasbNiro.Engineering.EnStandardGap",
            "Entities.Model.NasbNiro.Engineering.EnStandardGapLine",
            "Entities.Model.NasbNiro.Engineering.EnCaseEvent",
        };

        /// <summary>
        /// The only engineering tables the project-initiation seat may write: the two PMO-side actions (draft-distribution, distribute) write the distribution rows and the case event,
        /// and stamp the modify date of the handover and of the case. Every other engineering table is refused for this seat (its decisions stay with humans).
        /// </summary>
        private static readonly HashSet<string> InitAllowedEngineeringTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Entities.Model.NasbNiro.Engineering.EnDistribution",
            "Entities.Model.NasbNiro.Engineering.EnCaseEvent",
            "Entities.Model.NasbNiro.Engineering.EnHandover",
            "Entities.Model.NasbNiro.Engineering.EnCase",
        };

        public const string InitMessage = "ایجنت آغاز پروژه در ماژول مهندسی فقط ردیف‌های توزیع و رویداد پرونده را می‌نویسد: ";

        private static readonly ConcurrentDictionary<Type, string[]> ColumnsOfType = new ConcurrentDictionary<Type, string[]>();

        public static bool IsSeatName(string userName) => !string.IsNullOrWhiteSpace(userName) && userName.Trim().StartsWith(SeatPrefix, StringComparison.OrdinalIgnoreCase);

        public static bool IsInitName(string userName) => string.Equals(userName?.Trim(), InitUserName, StringComparison.OrdinalIgnoreCase);

        public static bool IsMasterName(string userName) => string.Equals(userName?.Trim(), MasterUserName, StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the name of the property is the name of a decision column (type is not looked at).</summary>
        public static bool MatchesName(string propertyName) => propertyName != null && DecisionColumn.IsMatch(propertyName);

        private static bool IsDecisionColumn(IReadOnlyProperty p) =>
            (p.ClrType == typeof(int) || p.ClrType == typeof(int?)) && DecisionColumn.IsMatch(p.Name) && !Exceptions.Contains((p.DeclaringType.ClrType?.Name, p.Name));

        /// <summary>Every (entity, property) of the model that the guard watches: the sweep of the tests prints it.</summary>
        public static List<(string Entity, string Property)> Sweep(IModel model) =>
            model.GetEntityTypes()
                .Where(t => t.ClrType != null)
                .SelectMany(t => t.GetProperties().Where(IsDecisionColumn).Select(p => (Entity: t.ClrType.Name, Property: p.Name)))
                .OrderBy(x => x.Entity, StringComparer.Ordinal).ThenBy(x => x.Property, StringComparer.Ordinal)
                .ToList();

        public static bool Allowed(Type entityType) => entityType != null && (MasterAllowedNamespaces.Contains(entityType.Namespace) || MasterAllowedTypes.Contains(entityType.FullName));

        internal static void Check(DbContext context, IHttpContextAccessor accessor)
        {
            ClaimsPrincipal user = accessor?.HttpContext?.User;
            string name = user?.Identity?.Name;
            if (!IsSeatName(name)) return;   // a human, or a background job without a principal: not touched

            bool master = IsMasterName(name), init = IsInitName(name);
            int seatId = int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

            foreach (EntityEntry entry in context.ChangeTracker.Entries().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted).ToList())
            {
                Type type = entry.Entity.GetType();
                if (master && !Allowed(type)) throw new InvalidOperationException(MasterMessage + type.Name);
                if (init && type.Namespace == "Entities.Model.NasbNiro.Engineering" && !InitAllowedEngineeringTypes.Contains(type.FullName)) throw new InvalidOperationException(InitMessage + type.Name);
                if (seatId == 0 || entry.State == EntityState.Deleted) continue;

                string[] columns = ColumnsOfType.GetOrAdd(type, _ => entry.Metadata.GetProperties().Where(IsDecisionColumn).Select(p => p.Name).ToArray());
                foreach (string column in columns)
                {
                    PropertyEntry p = entry.Property(column);
                    int? value = p.CurrentValue is int v ? v : (int?)null;
                    if (value != seatId) continue;
                    // an existing row: only a change counts (the seat that wrote the row earlier may save it again)
                    if (entry.State == EntityState.Modified && !p.IsModified) continue;
                    if (entry.State == EntityState.Modified && Equals(p.OriginalValue, p.CurrentValue)) continue;
                    throw new InvalidOperationException(Message + ": " + type.Name + "." + column);
                }
            }
        }
    }
}
