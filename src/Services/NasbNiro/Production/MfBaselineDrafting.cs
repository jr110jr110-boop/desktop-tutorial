using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.EngChange;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.EngChange;

namespace Services.NasbNiro.Production
{
    /// <summary>
    /// Package A2 (single BOM master): the components of a production order come from the project's current baseline (the released product BOM revision copied into the project, with its approved deviations);
    /// the legacy Bom / Process is only a read-only fallback for a project that has no baseline for the part, and the order says so in Persian (ComponentSource = LEGACY).
    /// </summary>
    public static class MfBaselineDrafting
    {
        public const string SourceBaseline = "BASELINE";
        public const string SourceLegacy = "LEGACY";
        public const string SourceManual = "MANUAL";

        public sealed class ComponentLine
        {
            public int ItemId { get; set; }
            public decimal Quantity { get; set; }
            public string Unit { get; set; }
        }

        public sealed class Expected
        {
            public string Source { get; set; }
            public string Note { get; set; }
            public List<ComponentLine> Lines { get; set; } = new List<ComponentLine>();
            public string Warning { get; set; }
        }

        /// <summary>The components the order of this output item must consume: from the project baseline, else (flagged) from the legacy process input.</summary>
        public static async Task<Expected> ExpectedAsync(IUnitOfWork uow, int workOrderId, int? processId, InventoryItem output, decimal quantity, CancellationToken ct)
        {
            var result = new Expected { Source = SourceLegacy };
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent && b.WorkOrderId == workOrderId).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            if (baseline != null && output != null && !string.IsNullOrWhiteSpace(output.PartNumber))
            {
                List<PbProjectBomLine> lines = await uow.Set<PbProjectBomLine>().AsNoTracking()
                    .Where(l => l.BaselineId == baseline.Id && !l.Deleted && !l.IsHeading && !l.IsRemoved && l.AssemblyPartNumber == output.PartNumber).OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToListAsync(ct);
                if (lines.Count > 0)
                {
                    var unresolved = new List<string>();
                    foreach (PbProjectBomLine l in lines)
                    {
                        InventoryItem item = l.ItemId.HasValue
                            ? await uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == l.ItemId.Value, ct)
                            : await uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => !i.Deleted && i.PartNumber == l.PartNumber && i.StageCode2 == null, ct);
                        if (item == null || item.Id == output.Id) { unresolved.Add(l.PartNumber); continue; }
                        ComponentLine existing = result.Lines.FirstOrDefault(x => x.ItemId == item.Id);
                        decimal q = Math.Round(l.Qty * quantity, 4);
                        if (existing != null) existing.Quantity += q;
                        else result.Lines.Add(new ComponentLine { ItemId = item.Id, Quantity = q, Unit = item.Unit });
                    }
                    result.Source = SourceBaseline;
                    result.Note = $"اجزاء از مبنای BOM پروژه (BOM {baseline.SourceBomCode} بازنگری {baseline.SourceBomRevision:00}) گرفته شد.";
                    if (unresolved.Count > 0) result.Warning = "کالای قطعه‌های زیر در انبار پیدا نشد و در اقلام نیامد: " + string.Join("، ", unresolved.Distinct());
                    return result;
                }
            }
            // legacy fallback: the input commodity of the process (read only)
            result.Source = SourceLegacy;
            result.Note = baseline == null
                ? "این پروژه مبنای BOM منتشرشده (بانک محصول) ندارد؛ اقلام از BOM قدیمی (فقط‌خواندنی) گرفته شد. برای کنترل کامل، محصول را از بانک محصول به پروژه بگیرید."
                : "مبنای BOM پروژه برای این قطعه ردیفی ندارد؛ اقلام از BOM قدیمی (فقط‌خواندنی) گرفته شد.";
            if (processId.HasValue)
            {
                string enter = await uow.Set<ProcessEntity>().AsNoTracking().Where(p => p.Id == processId.Value).Select(p => p.EnterCommodityCode).FirstOrDefaultAsync(ct);
                enter = string.IsNullOrWhiteSpace(enter) ? null : enter.Trim();
                InventoryItem component = enter == null ? null : await uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Code == enter && !i.Deleted, ct);
                if (component != null && (output == null || component.Id != output.Id)) result.Lines.Add(new ComponentLine { ItemId = component.Id, Quantity = quantity, Unit = component.Unit });
            }
            return result;
        }

        /// <summary>
        /// Is a planner's component list the one the master gives? Equal sets of items with equal quantities. A different list is allowed only when an approved engineering change order covers the part
        /// and the order stands at or after its effectivity (the new revision applies); a deviation changes the baseline first, so the expected list already contains it. Returns the Persian refusal or null.
        /// </summary>
        public static async Task<string> ChangeProblemAsync(IUnitOfWork uow, MfProductionOrder order, InventoryItem output, List<(int ItemId, decimal Quantity)> wanted, DateTime today, CancellationToken ct)
        {
            if (order.ProcessId == null && order.AutoDrafted == false && order.ComponentSource == null) return null;   // a manual order has no master to compare with
            Expected expected = await ExpectedAsync(uow, order.WorkOrderId, order.ProcessId, output, order.Quantity, ct);
            if (Same(expected.Lines, wanted)) return null;
            // transition: while the legacy BOM is unfrozen (a safety-profile switch) the components of an order that came from the legacy process stay editable
            if (expected.Source == SourceLegacy && (await ProductBank.PbSettingKeys.LoadAsync(uow, ct))[ProductBank.PbSettingKeys.LegacyBomFrozen] == "false") return null;
            EcOrder eco = await EcGate.OrderCoveringItemAsync(uow, output, ct);
            if (eco != null)
            {
                EcPosition pos = EcRules.Position(eco, order.Number, (order.SerialsPlanned ?? "").Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries), today);
                if (pos == EcPosition.After || pos == EcPosition.NotStated) return null;
            }
            return "اقلام مصرفی این دستور از مبنای BOM پروژه گرفته می‌شود و آزادانه عوض نمی‌شود. برای تغییر، یک انحراف تاییدشده (مهندسی و برنامه‌ریزی) در «مبنای BOM پروژه و انحراف‌ها» یا یک دستور تغییر مهندسی (ECO) تاییدشده لازم است؛ سپس «به‌روزرسانی اقلام از مبنا» را بزنید.";
        }

        private static bool Same(List<ComponentLine> expected, List<(int ItemId, decimal Quantity)> wanted)
        {
            var a = expected.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            var b = wanted.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            if (a.Count != b.Count) return false;
            foreach (var kv in a) if (!b.TryGetValue(kv.Key, out decimal q) || Math.Abs(q - kv.Value) > 0.0001m) return false;
            return true;
        }
    }
}
