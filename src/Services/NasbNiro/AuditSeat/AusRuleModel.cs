using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.AuditSeat;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;

namespace Services.NasbNiro.AuditSeat
{
    /// <summary>One deviation a rule found: the object, what is wrong, who answers for it and the facts that prove it (the evidence).</summary>
    public sealed class AusHit
    {
        public string ObjectType { get; set; }
        public int ObjectId { get; set; }

        /// <summary>Tells apart several deviations of the same object (a column, a state).</summary>
        public string Sub { get; set; }

        public string Number { get; set; }

        /// <summary>Persian: what is wrong, with the numbers.</summary>
        public string Text { get; set; }

        /// <summary>The person who made or answers for the record (the creator of the record, the responsible user), when the record names one.</summary>
        public int? OwnerUserId { get; set; }

        public string OwnerRole { get; set; }
        public string Link { get; set; }

        /// <summary>A rule may raise the default severity of one object (an expired welder on a pressure part).</summary>
        public AusSeverity? Severity { get; set; }

        public SortedDictionary<string, string> Facts { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public AusHit Fact(string key, object value)
        {
            Facts[key] = value switch
            {
                null => "",
                DateTime d => AusEvidence.Day(d),
                DateTimeOffset o => AusEvidence.Day(o.LocalDateTime),
                decimal m => m.ToString("0.####", CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
            return this;
        }

        public string Key => AusRules.ObjectKey(ObjectType, ObjectId, Sub);
    }

    /// <summary>What a rule sees while it runs.</summary>
    public sealed class AusContext
    {
        public IUnitOfWork Uow { get; set; }
        public IServiceProvider Services { get; set; }
        public DateTime Today { get; set; }
        public DateTimeOffset Now { get; set; }
        public IReadOnlyDictionary<string, decimal> Settings { get; set; }

        /// <summary>Event rules look only at what happened since this moment (the last pass with some overlap); null = the whole history.</summary>
        public DateTimeOffset? Since { get; set; }

        /// <summary>A re-check of one finding: the rule may restrict its query to this record.</summary>
        public int? FocusId { get; set; }

        public int Max { get; set; } = 2000;

        /// <summary>Set by <see cref="BoundedAsync{T}"/> when a query had more rows than the ceiling.</summary>
        public bool Truncated { get; set; }

        public CancellationToken Ct { get; set; }

        public decimal S(string key) => Settings != null && Settings.TryGetValue(key, out decimal v) ? v : AusSettingKeys.DefaultOf(key);
        public int Days(string key) => (int)S(key);

        /// <summary>The query's rows up to the ceiling; one more than the ceiling marks the pass as truncated (a finding about the run is raised, nothing is silently dropped).</summary>
        public async Task<List<T>> BoundedAsync<T>(IQueryable<T> query)
        {
            List<T> rows = await query.Take(Max + 1).ToListAsync(Ct);
            if (rows.Count > Max) { Truncated = true; rows.RemoveAt(rows.Count - 1); }
            return rows;
        }

        public IQueryable<T> Focus<T>(IQueryable<T> q) where T : BaseEntity => FocusId.HasValue ? q.Where(x => x.Id == FocusId.Value) : q;
    }

    /// <summary>One rule of the catalogue.</summary>
    public sealed class AusRuleDef
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Standard { get; set; }
        public string Clause { get; set; }

        /// <summary>The company procedure, form or program decision that states the requirement.</summary>
        public string Procedure { get; set; }

        public string ProcessCode { get; set; }
        public AusSeverity Severity { get; set; }

        /// <summary>English, for the report of the catalogue: what the query looks at.</summary>
        public string QuerySummary { get; set; }

        /// <summary>The event rules read only what is new since the last pass (a full pass every ★7 days).</summary>
        public bool Incremental { get; set; }

        /// <summary>The role that answers when the record names nobody; default is the owner role of the process.</summary>
        public string OwnerRole { get; set; }

        public Func<AusContext, Task<List<AusHit>>> Query { get; set; }

        public string ResolveOwnerRole() => OwnerRole ?? AusProcesses.Find(ProcessCode)?.OwnerRole;
        public string ResolveOwnerUnit() => AusProcesses.Find(ProcessCode)?.OwnerUnit;
    }

    public static class AusEvidence
    {
        public static string Day(DateTime d) => InventoryText.PersianDate(d);

        public static string Sha256Hex(string text)
        {
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? "")).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        /// <summary>The immutable snapshot of a deviation at detection: rule, clause, object and the facts, in a fixed order. Its SHA-256 goes into the audit chain with the finding.</summary>
        public static string Snapshot(AusRuleDef rule, AusHit hit, DateTimeOffset at, int runId)
        {
            var doc = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["rule"] = rule.Code,
                ["standard"] = rule.Standard,
                ["clause"] = rule.Clause,
                ["procedure"] = rule.Procedure,
                ["object"] = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["type"] = hit.ObjectType, ["id"] = hit.ObjectId, ["number"] = hit.Number, ["sub"] = hit.Sub, ["link"] = hit.Link },
                ["facts"] = hit.Facts,
                ["text"] = hit.Text,
                ["detectedAt"] = at.ToString("o", CultureInfo.InvariantCulture),
                ["runId"] = runId,
            };
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
    }

    /// <summary>The catalogue: every rule of the AUDIT seat. The files AusRules.*.cs hold the rules by process.</summary>
    public static partial class AusCatalog
    {
        private static readonly Lazy<IReadOnlyList<AusRuleDef>> Lazy = new Lazy<IReadOnlyList<AusRuleDef>>(() =>
        {
            var list = new List<AusRuleDef>();
            list.AddRange(IntegrityRules());
            list.AddRange(GovernanceRules());
            list.AddRange(QualityRules());
            list.AddRange(EngineeringRules());
            list.AddRange(ProjectRules());
            list.AddRange(SupportRules());
            return list;
        });

        public static IReadOnlyList<AusRuleDef> All => Lazy.Value;

        public static AusRuleDef Find(string code) => All.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));

        internal static AusRuleDef Def(string code, string process, AusSeverity severity, string title, string standard, string clause, string procedure, string summary, bool incremental, Func<AusContext, Task<List<AusHit>>> query, string ownerRole = null) =>
            new AusRuleDef { Code = code, ProcessCode = process, Severity = severity, Title = title, Standard = standard, Clause = clause, Procedure = procedure, QuerySummary = summary, Incremental = incremental, Query = query, OwnerRole = ownerRole };

        internal static AusHit Hit(string type, int id, string number, string text, string sub = null, int? owner = null, string link = null) =>
            new AusHit { ObjectType = type, ObjectId = id, Number = number, Text = text, Sub = sub, OwnerUserId = owner.HasValue && owner.Value > 0 ? owner : null, Link = link };

        internal static string P(DateTime? d) => d.HasValue ? InventoryText.PersianDate(d.Value) : "-";
        internal static string P(DateTimeOffset? d) => d.HasValue ? InventoryText.PersianDate(d.Value.LocalDateTime) : "-";
    }
}
