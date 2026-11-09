using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Context
{
    /// <summary>The first record whose columns differ from what the audit trail last recorded for it.</summary>
    public sealed class RecordDriftFinding
    {
        /// <summary>The CLR type name of the record (as in <c>FnAuditLog.EntityType</c>).</summary>
        public string EntityType { get; set; }

        /// <summary>The id of the record (the row of the table that was edited, not the trail row).</summary>
        public int RowId { get; set; }

        /// <summary>The column that differs; "(ردیف)" when the whole row is missing or came back after a delete.</summary>
        public string Column { get; set; }

        public string Message { get; set; }

        /// <summary>The trail row that holds the last recorded value.</summary>
        public int? TrailRowId { get; set; }

        public DateTimeOffset? TrailAt { get; set; }

        /// <summary>Every column of the record that differs, with the current value in the shape the trail writes it (used to write the reconciling entry when the break is accepted).</summary>
        public Dictionary<string, object> Differences { get; set; } = new Dictionary<string, object>();

        /// <summary>Package G (fix round 1, M1): "column" (one or more columns differ), "present-after-delete" (the trail says deleted, the row is there; Differences holds every audited column) or "missing" (the row is gone without a Deleted entry).</summary>
        public string Mode { get; set; } = "column";
    }

    /// <summary>
    /// Package D (H1, fix round): the hash chain protects the trail, not the records. A person with access to the database who edits a measured value, a heat number or a certificate result directly leaves the chain intact.
    /// This check closes that gap for the record types of the package: for every row it folds the trail (the Added entry, then each Modified entry: the last after-value of every audited column) and compares it with the
    /// current row. A column that differs, a row that is gone without a Deleted entry, or a row that is still there after a Deleted entry is a finding. Only the columns the trail holds are compared (a record made before the
    /// package has no Added entry and is compared on the columns its later changes recorded); columns written as a marker or a masked value are not compared; numbers are compared with the tolerance of the column scale.
    /// </summary>
    public static class RecordDrift
    {
        private const int PageSize = 500;

        /// <summary>Checks all record types of the package and returns the first finding (null when every row agrees with its trail).</summary>
        public static async Task<RecordDriftFinding> FindFirstAsync(IUnitOfWork uow, CancellationToken ct)
        {
            MethodInfo method = typeof(RecordDrift).GetMethod(nameof(CheckTypeAsync), BindingFlags.NonPublic | BindingFlags.Static);
            foreach (Type type in FinanceAuditTrail.RecordsIntegrityTypes.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var task = (Task<RecordDriftFinding>)method.MakeGenericMethod(type).Invoke(null, new object[] { uow, ct, null });
                RecordDriftFinding f = await task;
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>Checks one record type (used by the tests).</summary>
        public static Task<RecordDriftFinding> FindInTypeAsync(IUnitOfWork uow, Type type, CancellationToken ct)
        {
            MethodInfo method = typeof(RecordDrift).GetMethod(nameof(CheckTypeAsync), BindingFlags.NonPublic | BindingFlags.Static);
            return (Task<RecordDriftFinding>)method.MakeGenericMethod(type).Invoke(null, new object[] { uow, ct, null });
        }

        /// <summary>Package G (fix round 1, M1): every record of the type that differs from its trail (not only the first), for the baseline of the newly covered types and for the dry run of the rehearsal. Nothing is written.</summary>
        public static async Task<List<RecordDriftFinding>> CollectInTypeAsync(IUnitOfWork uow, Type type, CancellationToken ct)
        {
            var all = new List<RecordDriftFinding>();
            MethodInfo method = typeof(RecordDrift).GetMethod(nameof(CheckTypeAsync), BindingFlags.NonPublic | BindingFlags.Static);
            await (Task<RecordDriftFinding>)method.MakeGenericMethod(type).Invoke(null, new object[] { uow, ct, all });
            return all;
        }

        private sealed class Fold
        {
            public bool Deleted;
            public int TrailRowId;
            public DateTimeOffset TrailAt;
            public readonly Dictionary<string, JsonElement> Values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> ValueTrailRow = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        private static async Task<RecordDriftFinding> CheckTypeAsync<T>(IUnitOfWork uow, CancellationToken ct, List<RecordDriftFinding> all) where T : class
        {
            Type type = typeof(T);
            PropertyInfo idProperty = type.GetProperty("Id");
            if (idProperty == null || idProperty.PropertyType != typeof(int)) return null;
            string name = type.Name;
            string[] only = FinanceAuditTrail.AuditedColumns(type);
            int from = 0;
            while (true)
            {
                int start = from;
                List<T> rows = await uow.Set<T>().AsNoTracking().IgnoreQueryFilters().Where(e => EF.Property<int>(e, "Id") >= start).OrderBy(e => EF.Property<int>(e, "Id")).Take(PageSize).ToListAsync(ct);
                bool last = rows.Count < PageSize;
                int to = last ? int.MaxValue : (int)idProperty.GetValue(rows[rows.Count - 1]);

                Dictionary<int, Fold> folds = new Dictionary<int, Fold>();
                List<FnAuditLog> trail = await uow.Set<FnAuditLog>().AsNoTracking().Where(a => a.EntityType == name && a.EntityId != null && a.EntityId >= start && a.EntityId <= to).OrderBy(a => a.Id).ToListAsync(ct);
                foreach (FnAuditLog a in trail)
                {
                    int id = a.EntityId.Value;
                    if (!folds.TryGetValue(id, out Fold f)) folds[id] = f = new Fold();
                    f.TrailRowId = a.Id; f.TrailAt = a.At;
                    if (a.Action == "Deleted") { f.Deleted = true; continue; }
                    f.Deleted = false;
                    if (string.IsNullOrEmpty(a.AfterJson)) continue;
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(a.AfterJson);
                        if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                        foreach (JsonProperty p in doc.RootElement.EnumerateObject()) { f.Values[p.Name] = p.Value.Clone(); f.ValueTrailRow[p.Name] = a.Id; }
                    }
                    catch (JsonException) { }
                }

                var present = new HashSet<int>();
                foreach (T row in rows)
                {
                    int id = (int)idProperty.GetValue(row);
                    present.Add(id);
                    if (!folds.TryGetValue(id, out Fold f)) continue;   // a record the trail never saw (made before the package): nothing to compare
                    if (f.Deleted)
                    {
                        RecordDriftFinding revived = Finding(name, id, "(ردیف)", f, "رکورد در سوابق حذف‌شده ثبت شده ولی هنوز در جدول هست.");
                        if (all == null) return revived;
                        revived.Mode = "present-after-delete";
                        foreach (PropertyInfo prop in type.GetProperties())
                            if ((only == null || only.Contains(prop.Name)) && !FinanceAuditTrail.IsMarkedColumn(type, prop.Name)) revived.Differences[prop.Name] = FinanceAuditTrail.ShapeOf(prop.Name, prop.GetValue(row));
                        all.Add(revived);
                        continue;
                    }
                    RecordDriftFinding found = null;
                    foreach (KeyValuePair<string, JsonElement> pair in f.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        if (only != null && !only.Contains(pair.Key)) continue;
                        if (FinanceAuditTrail.IsMarkedColumn(type, pair.Key)) continue;
                        PropertyInfo prop = type.GetProperty(pair.Key);
                        if (prop == null) continue;
                        object shaped = FinanceAuditTrail.ShapeOf(pair.Key, prop.GetValue(row));
                        JsonElement current = JsonSerializer.SerializeToElement(shaped);
                        if (Same(pair.Value, current)) continue;
                        if (found == null)
                        {
                            found = Finding(name, id, pair.Key, f, "مقدار ستون «" + pair.Key + "» با آخرین مقدار ثبت‌شده در سوابق نمی‌خواند (ثبت‌شده: " + Short(pair.Value) + "، فعلی: " + Short(current) + ").");
                            found.TrailRowId = f.ValueTrailRow[pair.Key];
                        }
                        found.Differences[pair.Key] = shaped;
                    }
                    if (found != null) { if (all == null) return found; all.Add(found); }
                }
                foreach (KeyValuePair<int, Fold> pair in folds.OrderBy(p => p.Key))
                    if (!pair.Value.Deleted && !present.Contains(pair.Key))
                    {
                        RecordDriftFinding gone = Finding(name, pair.Key, "(ردیف)", pair.Value, "رکورد در جدول نیست در حالی که سوابق حذف آن را ثبت نکرده است.");
                        if (all == null) return gone;
                        gone.Mode = "missing";
                        all.Add(gone);
                    }

                if (last) return null;
                from = to + 1;
            }
        }

        private static RecordDriftFinding Finding(string type, int id, string column, Fold f, string detail) => new RecordDriftFinding
        {
            EntityType = type, RowId = id, Column = column, TrailRowId = f.TrailRowId, TrailAt = f.TrailAt,
            Message = "رکورد «" + (AuditEntityLabels.Of(type) ?? type) + "» با شناسه " + id.ToString(CultureInfo.InvariantCulture) + " پس از ثبت در سوابق دست‌کاری شده است: " + detail,
        };

        private static string Short(JsonElement e)
        {
            string s = e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();
            return s != null && s.Length > 40 ? s.Substring(0, 40) + "…" : s;
        }

        private static bool Same(JsonElement trail, JsonElement current)
        {
            if (trail.ValueKind == JsonValueKind.Null || trail.ValueKind == JsonValueKind.Undefined) return current.ValueKind == JsonValueKind.Null || current.ValueKind == JsonValueKind.Undefined;
            if (trail.ValueKind == JsonValueKind.Number && current.ValueKind == JsonValueKind.Number)
            {
                if (trail.TryGetDecimal(out decimal a) && current.TryGetDecimal(out decimal b))
                {
                    if (a == Math.Truncate(a) && b == Math.Truncate(b)) return a == b;
                    return Math.Abs(a - b) <= 0.0051m;   // a number is rounded to the scale of its column when it is stored
                }
                return trail.GetRawText() == current.GetRawText();
            }
            if (trail.ValueKind != current.ValueKind) return false;
            if (trail.ValueKind == JsonValueKind.String) return string.Equals(trail.GetString(), current.GetString(), StringComparison.Ordinal);
            return trail.GetRawText() == current.GetRawText();
        }
    }
}
