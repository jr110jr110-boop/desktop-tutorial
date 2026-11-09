using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.GoLive;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Context
{
    /// <summary>One table of the baseline (or of its dry run).</summary>
    public sealed class DriftBaselineRow
    {
        public string EntityType { get; set; }
        public string Label { get; set; }
        public int Differing { get; set; }
        public int Present { get; set; }
        public int Missing { get; set; }
    }

    /// <summary>
    /// Package G (fix round 1, M1): the record types that Package G newly put in the record-drift scope already have records, and some of them have historic trail rows with gaps (code of an earlier package, a manual fix). Reporting those gaps
    /// as a break of the chain on the first verification run would alert QA, PMO and the CEO and need a three-person acceptance for every row. So the FIRST verification run after the update takes the current state of the newly covered
    /// types as its starting point: for each record that differs from its trail one reconciling entry is written (the same kind of entry the accepted break writes) and one signed baseline entry holds the count per table. Only the first
    /// run does this (the flag is on the activation row); later changes are checked normally. <see cref="PreviewAsync"/> is the dry run for the rehearsal on a copy of the server database: it writes nothing.
    /// </summary>
    public static class RecordDriftBaseline
    {
        public const string MarkerType = "RecordDriftBaseline";

        /// <summary>The dry run: per table, how many records differ from their trail now (nothing is written).</summary>
        public static async Task<List<DriftBaselineRow>> PreviewAsync(IUnitOfWork uow, CancellationToken ct)
        {
            var rows = new List<DriftBaselineRow>();
            foreach (Type type in FinanceAuditTrail.GoLiveBaselineTypes.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                List<RecordDriftFinding> found = await RecordDrift.CollectInTypeAsync(uow, type, ct);
                rows.Add(new DriftBaselineRow { EntityType = type.Name, Label = AuditEntityLabels.Of(type.Name) ?? type.Name, Differing = found.Count(f => f.Mode == "column"), Present = found.Count(f => f.Mode == "present-after-delete"), Missing = found.Count(f => f.Mode == "missing") });
            }
            return rows;
        }

        /// <summary>True when the baseline is still to be taken (an activation exists and carries no baseline instant).</summary>
        public static Task<bool> IsDueAsync(IUnitOfWork uow, CancellationToken ct) => uow.Set<GlActivation>().AnyAsync(a => a.DriftBaselineAt == null, ct);

        /// <summary>Takes the baseline once. Returns the number of reconciling entries written, or -1 when it is not due.</summary>
        public static async Task<int> RunOnceAsync(IUnitOfWork uow, int? userId, CancellationToken ct)
        {
            GlActivation activation = await uow.Set<GlActivation>().Where(a => a.DriftBaselineAt == null).OrderBy(a => a.Id).FirstOrDefaultAsync(ct);
            if (activation == null) return -1;
            DateTimeOffset now = DateTimeOffset.Now;
            int total = 0;
            var counts = new Dictionary<string, int>();
            foreach (Type type in FinanceAuditTrail.GoLiveBaselineTypes.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                List<RecordDriftFinding> found = await RecordDrift.CollectInTypeAsync(uow, type, ct);
                foreach (RecordDriftFinding f in found)
                {
                    uow.Set<FnAuditLog>().Add(new FnAuditLog
                    {
                        EntityType = f.EntityType, EntityId = f.RowId, Action = f.Mode == "missing" ? "Deleted" : "Modified",
                        Summary = "نقطه شروع سوابق: وضعیت رکورد در نخستین بررسی پس از به‌روزرسانی مبنا شد",
                        AfterJson = f.Mode == "missing" ? null : JsonSerializer.Serialize(f.Differences), UserId = userId ?? 0, At = now,
                    });
                }
                if (found.Count > 0) { counts[type.Name] = found.Count; total += found.Count; }
            }
            if (total > 0)
                uow.Set<FnAuditLog>().Add(new FnAuditLog
                {
                    EntityType = MarkerType, EntityId = null, Action = "Baseline", Summary = $"نقطه شروع سوابق برای رکوردهای تازه‌پوشش‌یافته: {total} رکورد با سوابق تاریخی ناقص مبنا شد",
                    AfterJson = JsonSerializer.Serialize(new { total, tables = counts }), UserId = userId ?? 0, At = now,
                });
            activation.DriftBaselineAt = now;
            await uow.SaveChangesAsync(ct);
            return total;
        }
    }
}
