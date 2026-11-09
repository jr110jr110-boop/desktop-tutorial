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

        /// <summary>
        /// Fix round 1 (H4): the day the deviating event happened (event rules only). An event before the audit horizon goes into the one backlog finding of its rule instead of a finding of its own.
        /// A hit without a date is always individual.
        /// </summary>
        public DateTime? EventDate { get; set; }

        /// <summary>True for a hit that belongs to the backlog whatever its date (for example a lot that has only the legacy certificate number, from before the certificate register of Package C).</summary>
        public bool InBacklog { get; set; }

        public AusHit Backlog(bool value = true) { InBacklog = value; return this; }

        public AusHit On(DateTime? d) { EventDate = d; return this; }
        public AusHit On(DateTimeOffset? d) { EventDate = d?.LocalDateTime; return this; }

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

    /// <summary>Fix round 1 (H1): how the automatic re-check of a finding of the rule works.</summary>
    public enum AusRecheckMode
    {
        /// <summary>A current state (an overdue date, an open item): the object is evaluated again and must no longer deviate.</summary>
        State = 0,

        /// <summary>A past, immutable event: the re-check passes when the owner's corrective action is recorded and no new occurrence of the rule happened for the same object or owner since the action.</summary>
        Event = 1,
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

        /// <summary>The ceiling of HITS per pass (not of candidate rows): predicates are in SQL, so the rows read are the deviations.</summary>
        public int Max { get; set; } = 2000;

        /// <summary>Fix round 1 (H2): the pass resumes after this row id (the checkpoint of an earlier pass that was cut); 0 = from the start.</summary>
        public int Cursor { get; set; }

        /// <summary>Set when the pass stopped before the end (hit ceiling or time budget): the run stores <see cref="NextCursor"/> and the next run continues there. Nothing is dropped silently.</summary>
        public bool Continued { get; set; }

        public int NextCursor { get; set; }

        /// <summary>The soft end of the time budget: paged scans stop here and leave a checkpoint (the hard limit cancels the token).</summary>
        public DateTimeOffset? Deadline { get; set; }

        /// <summary>Kept for the callers that only ask whether the pass was incomplete.</summary>
        public bool Truncated => Continued;

        public CancellationToken Ct { get; set; }

        public decimal S(string key) => Settings != null && Settings.TryGetValue(key, out decimal v) ? v : AusSettingKeys.DefaultOf(key);
        public int Days(string key) => (int)S(key);

        /// <summary>Zero tolerance (D12): a threshold of 0 means no tolerance beyond the shortest cycle of the process itself (<paramref name="cycle"/>); the PMIS proposal is shown on the setting for the CEO to confirm.</summary>
        public decimal Tol(string key, decimal cycle) { decimal v = S(key); return v > 0 ? v : cycle; }

        private static System.Linq.Expressions.Expression<Func<T, bool>> IdWhere<T>(bool equal, int value)
        {
            var p = System.Linq.Expressions.Expression.Parameter(typeof(T), "x");
            var id = System.Linq.Expressions.Expression.Property(p, "Id");
            var c = System.Linq.Expressions.Expression.Constant(value);
            return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(equal ? System.Linq.Expressions.Expression.Equal(id, c) : System.Linq.Expressions.Expression.GreaterThan(id, c), p);
        }

        private static System.Linq.Expressions.Expression<Func<T, int>> IdKey<T>()
        {
            var p = System.Linq.Expressions.Expression.Parameter(typeof(T), "x");
            return System.Linq.Expressions.Expression.Lambda<Func<T, int>>(System.Linq.Expressions.Expression.Property(p, "Id"), p);
        }

        /// <summary>Re-check focus and checkpoint, pushed into SQL on the entity the hit refers to (so a cap never hides the object and a continued pass resumes where it stopped). The entity has an int Id.</summary>
        public IQueryable<T> Scope<T>(IQueryable<T> q) where T : class
        {
            if (FocusId.HasValue) return q.Where(IdWhere<T>(true, FocusId.Value));
            return Cursor > 0 ? q.Where(IdWhere<T>(false, Cursor)) : q;
        }

        /// <summary>The same with the key named by an expression (an entity whose key is not called Id).</summary>
        public IQueryable<T> ScopeBy<T>(IQueryable<T> q, System.Linq.Expressions.Expression<Func<T, int>> id)
        {
            if (!FocusId.HasValue && Cursor <= 0) return q;
            var body = FocusId.HasValue ? System.Linq.Expressions.Expression.Equal(id.Body, System.Linq.Expressions.Expression.Constant(FocusId.Value))
                                        : System.Linq.Expressions.Expression.GreaterThan(id.Body, System.Linq.Expressions.Expression.Constant(Cursor));
            return q.Where(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, id.Parameters));
        }

        public IQueryable<T> Focus<T>(IQueryable<T> q) where T : class => FocusId.HasValue ? q.Where(IdWhere<T>(true, FocusId.Value)) : q;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo> IdProps = new System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo>();

        private static int? IdOf<T>(T row)
        {
            System.Reflection.PropertyInfo p = IdProps.GetOrAdd(typeof(T), t => t.GetProperty("Id"));
            return p?.GetValue(row) is int i ? i : (int?)null;
        }

        /// <summary>
        /// The rows of a query whose predicate is in SQL (so they are the deviations), up to the hit ceiling. More than the ceiling does not raise a finding about the run: the pass is marked continued and the next run
        /// resumes after the last row kept (the query must be ordered by Id and start from <see cref="Scope{T}"/>).
        /// </summary>
        public async Task<List<T>> BoundedAsync<T>(IQueryable<T> query)
        {
            List<T> rows = await query.Take(Max + 1).ToListAsync(Ct);
            if (rows.Count > Max)
            {
                rows.RemoveAt(rows.Count - 1);
                int? last = IdOf(rows[rows.Count - 1]);
                if (last.HasValue) { Continued = true; NextCursor = last.Value; }
                else Continued = true;
            }
            return rows;
        }

        /// <summary>
        /// A set-based paged scan for rules that must judge each row in memory (gate helpers, hashes): keyset pages by Id of the base table, each page judged, stopping at the hit ceiling or the soft deadline with a checkpoint.
        /// <paramref name="baseQuery"/> is the unpaged candidate query (predicates in SQL where possible).
        /// </summary>
        public async Task<List<AusHit>> ScanAsync<T>(IQueryable<T> baseQuery, Func<List<T>, Task<List<AusHit>>> judge, int page = 300, bool useFocus = true) where T : class
        {
            var hits = new List<AusHit>();
            int last = FocusId.HasValue ? 0 : Cursor;
            while (true)
            {
                IQueryable<T> q = baseQuery;
                if (useFocus && FocusId.HasValue) q = q.Where(IdWhere<T>(true, FocusId.Value));
                List<T> rows = await q.Where(IdWhere<T>(false, last)).OrderBy(IdKey<T>()).Take(page).ToListAsync(Ct);
                if (rows.Count == 0) return hits;
                hits.AddRange(await judge(rows));
                last = IdOf(rows[rows.Count - 1]) ?? int.MaxValue;
                if (rows.Count < page || (useFocus && FocusId.HasValue)) return hits;
                if (hits.Count >= Max || (Deadline.HasValue && DateTimeOffset.Now >= Deadline.Value)) { Continued = true; NextCursor = last; return hits; }
            }
        }
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

        /// <summary>Fix round 1 (H1): State (default) or Event.</summary>
        public AusRecheckMode Mode { get; set; }

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

        /// <summary>The rules about a past, immutable event (the record cannot change afterwards): their re-check is the Event mode.</summary>
        private static readonly HashSet<string> EventRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AUD-MC-001", "AUD-MC-002", "AUD-MC-003", "AUD-MC-004", "AUD-MC-005", "AUD-MC-006", "AUD-EX-005", "AUD-DC-001", "AUD-DC-002", "AUD-BL-001", "AUD-QL-001", "AUD-QL-002", "AUD-QL-003", "AUD-QL-004",
            "AUD-CAL-002", "AUD-NC-003", "AUD-SP-001", "AUD-AI-001", "AUD-INT-004", "AUD-DS-003", "AUD-EC-003", "AUD-LB-001",
        };

        internal static AusRuleDef Def(string code, string process, AusSeverity severity, string title, string standard, string clause, string procedure, string summary, bool incremental, Func<AusContext, Task<List<AusHit>>> query, string ownerRole = null) =>
            new AusRuleDef { Code = code, ProcessCode = process, Severity = severity, Title = title, Standard = standard, Clause = clause, Procedure = procedure, QuerySummary = summary, Incremental = incremental, Query = query, OwnerRole = ownerRole, Mode = EventRules.Contains(code) ? AusRecheckMode.Event : AusRecheckMode.State };

        /// <summary>Fix round 1 (H6): every text is cut to the size of its column; a cut text ends with the «…» marker and its full text goes into the facts (so into the evidence JSON).</summary>
        internal static string Cut(string s, int n) => s == null || s.Length <= n ? s : s.Substring(0, Math.Max(0, n - 1)) + "…";

        internal static AusHit Hit(string type, int id, string number, string text, string sub = null, int? owner = null, string link = null)
        {
            var h = new AusHit { ObjectType = Cut(type, 60), ObjectId = id, Number = Cut(number, 80), Text = Cut(text, 1990), Sub = sub, OwnerUserId = owner.HasValue && owner.Value > 0 ? owner : null, Link = Cut(link, 200) };
            if (number != null && number.Length > 80) h.Facts["fullNumber"] = number;
            if (text != null && text.Length > 1990) h.Facts["fullText"] = text;
            return h;
        }

        internal static string P(DateTime? d) => d.HasValue ? InventoryText.PersianDate(d.Value) : "-";
        internal static string P(DateTimeOffset? d) => d.HasValue ? InventoryText.PersianDate(d.Value.LocalDateTime) : "-";
    }
}
