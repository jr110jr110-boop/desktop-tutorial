using System;
using System.Globalization;
using System.Text.Json;
using Entities.Model.NasbNiro.Finance;
using DataLayer.Context;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// R1t: explicit rows of the append-only audit trail (<see cref="FnAuditLog"/>) for decisions that are more than a plain data change
    /// (a CEO approval voided by an edit, a checklist override with its second manager, a late posting, a manual package status).
    /// The row is only added to the context: the caller's next SaveChanges stores it together with the change it describes, and the
    /// hash chain of the trail (R1t D20) seals it there. JSON carries ISO dates; the summary is Persian text for the reader.
    /// </summary>
    internal static class AuditNotes
    {
        /// <summary>The Action column holds 20 characters and the Summary column 300.</summary>
        public const int ActionMax = 20;
        public const int SummaryMax = 290;

        public static void Add(IUnitOfWork uow, int userId, string entityType, int? entityId, string action, string summary, object before = null, object after = null)
        {
            uow.Set<FnAuditLog>().Add(new FnAuditLog
            {
                EntityType = Clip(entityType, 100),
                EntityId = entityId,
                Action = Clip(action, ActionMax),
                Summary = Clip(summary, SummaryMax),
                BeforeJson = before == null ? null : JsonSerializer.Serialize(before),
                AfterJson = after == null ? null : JsonSerializer.Serialize(after),
                UserId = userId,
                At = DateTimeOffset.Now,
            });
        }

        /// <summary>ISO 8601 text of a date for the JSON columns (the Persian text goes in the summary).</summary>
        public static string Iso(DateTimeOffset? at) => at?.ToString("o", CultureInfo.InvariantCulture);

        public static string Iso(DateTime? at) => at?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string Clip(string value, int max) => value == null ? null : value.Length <= max ? value : value.Substring(0, max);
    }
}
