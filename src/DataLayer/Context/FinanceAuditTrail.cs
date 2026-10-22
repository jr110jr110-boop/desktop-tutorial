using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Common.IdentityToolkit;
using Entities.Model.NasbNiro.Finance;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DataLayer.Context
{
    /// <summary>
    /// Finance audit trail (release F3): every change of the audited finance records is written to the append-only
    /// <see cref="FnAuditLog"/> in the same SaveChanges as the change itself (before / after values of the changed fields,
    /// user, time, IP). Personal data is masked; nothing in the trail can be edited or deleted.
    /// </summary>
    internal static class FinanceAuditTrail
    {
        /// <summary>Audited record types. A null list means all scalar properties; otherwise only the listed ones are recorded.</summary>
        private static readonly Dictionary<Type, string[]> Audited = new Dictionary<Type, string[]>
        {
            { typeof(FnAccount), null },
            { typeof(FnDetailAccount), null },
            { typeof(FnVoucher), null },
            { typeof(FnFiscalYear), null },
            { typeof(FnPeriod), null },
            { typeof(AccountMapping), null },
            { typeof(FnTaxRate), null },
            { typeof(FnBankAccount), null },
            // only the bank account number (IBAN) of an employee is audited; no salary or other personal data
            { typeof(FnEmployee), new[] { "BankIban" } },
            { typeof(FnAsset), null },
            { typeof(FnLegalEntity), null },
            { typeof(FnIntercompanyPair), null },
            { typeof(FnApprovalThreshold), null },
            { typeof(FnAttachmentRule), null },
            { typeof(FnSetting), null },
            { typeof(FnCloseChecklist), null },
            { typeof(FnMigrationAccountMap), null },
            { typeof(FnParallelRun), null },
            { typeof(FnExportLayoutColumn), null },
            { typeof(FnDetailSepidarMap), null },
            { typeof(FnCurrency), null },
            { typeof(FnExchangeRate), null },
            { typeof(FnLetterOfCredit), null },
            { typeof(FnBudget), null },
            { typeof(FnBudgetLine), null },
            { typeof(FnOverheadRate), null },
            { typeof(FnAllocationKey), null },
            { typeof(FnStandardCost), null },
            { typeof(FnContractRevenue), null },
            { typeof(FnDeferral), null },
            { typeof(FnProvision), null },
        };

        private static readonly HashSet<string> Masked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BankIban" };
        private static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RowVersion" };

        public static void Capture(DbContext context, IHttpContextAccessor accessor)
        {
            List<EntityEntry> entries = context.ChangeTracker.Entries().ToList();
            foreach (EntityEntry e in entries)
            {
                if (e.Entity is FnAuditLog && (e.State == EntityState.Modified || e.State == EntityState.Deleted))
                    throw new InvalidOperationException("سوابق ممیزی قابل ویرایش یا حذف نیست.");
            }

            List<EntityEntry> audited = entries
                .Where(e => (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted) && Audited.ContainsKey(e.Entity.GetType()))
                .ToList();
            if (audited.Count == 0) return;

            int userId = 0;
            string ip = null;
            HttpContext http = accessor?.HttpContext;
            if (http != null)
            {
                if (int.TryParse(http.User?.Identity?.GetUserId(), out int id)) userId = id;
                ip = http.Connection?.RemoteIpAddress?.ToString();
            }
            DateTimeOffset now = DateTimeOffset.Now;

            foreach (EntityEntry entry in audited)
            {
                Type type = entry.Entity.GetType();
                string[] only = Audited[type];

                // system-made records are too many to list one by one (the vouchers keep their own history through the events)
                if (entry.State == EntityState.Added)
                {
                    if (entry.Entity is FnVoucher v && (v.Type == FnVoucherType.Auto || v.IsImported)) continue;
                    if (entry.Entity is FnDetailAccount d && d.LinkedEntityId.HasValue) continue;
                }

                var before = new Dictionary<string, object>();
                var after = new Dictionary<string, object>();
                foreach (PropertyEntry p in entry.Properties)
                {
                    string name = p.Metadata.Name;
                    if (Skipped.Contains(name) || p.Metadata.ClrType == typeof(byte[])) continue;
                    if (only != null && !only.Contains(name)) continue;
                    if (entry.State == EntityState.Added)
                    {
                        after[name] = Shape(name, p.CurrentValue);
                    }
                    else if (entry.State == EntityState.Deleted)
                    {
                        before[name] = Shape(name, p.OriginalValue);
                    }
                    else
                    {
                        if (!p.IsModified || Equals(p.OriginalValue, p.CurrentValue)) continue;
                        before[name] = Shape(name, p.OriginalValue);
                        after[name] = Shape(name, p.CurrentValue);
                    }
                }
                if (entry.State == EntityState.Modified && after.Count == 0) continue;
                if (only != null && before.Count == 0 && after.Count == 0) continue;

                int? entityId = null;
                if (entry.State != EntityState.Added)
                {
                    PropertyEntry idProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
                    if (idProperty != null && idProperty.CurrentValue is int idValue) entityId = idValue;
                }

                context.Set<FnAuditLog>().Add(new FnAuditLog
                {
                    EntityType = type.Name,
                    EntityId = entityId,
                    Action = entry.State == EntityState.Added ? "Added" : entry.State == EntityState.Deleted ? "Deleted" : "Modified",
                    Summary = Describe(entry),
                    BeforeJson = before.Count == 0 ? null : JsonSerializer.Serialize(before),
                    AfterJson = after.Count == 0 ? null : JsonSerializer.Serialize(after),
                    UserId = userId,
                    At = now,
                    Ip = ip,
                });
            }
        }

        private static object Shape(string name, object value)
        {
            if (value == null) return null;
            if (Masked.Contains(name) && value is string s) return s.Length <= 4 ? "****" : "****" + s.Substring(s.Length - 4);
            if (value is Enum) return value.ToString();
            if (value is DateTime dt) return dt.ToString("yyyy-MM-dd'T'HH:mm:ss");
            if (value is DateTimeOffset dto) return dto.ToString("yyyy-MM-dd'T'HH:mm:ssK");
            if (value is decimal || value is int || value is long || value is bool || value is string) return value;
            return value.ToString();
        }

        private static string Describe(EntityEntry entry)
        {
            string text = null;
            foreach (string name in new[] { "Code", "Number", "Title", "Name", "Role", "Key", "TemporaryNumber" })
            {
                PropertyEntry p = entry.Properties.FirstOrDefault(x => x.Metadata.Name == name);
                if (p == null) continue;
                object value = entry.State == EntityState.Deleted ? p.OriginalValue : p.CurrentValue;
                if (value == null) continue;
                string t = value.ToString();
                if (!string.IsNullOrWhiteSpace(t)) { text = name == "TemporaryNumber" ? "موقت " + t : t; break; }
            }
            if (entry.Entity is FnEmployee) text = "کارمند (شماره حساب)";
            if (text != null && text.Length > 280) text = text.Substring(0, 280);
            return text;
        }
    }
}
