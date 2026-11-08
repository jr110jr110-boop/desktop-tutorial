using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Package A2 (single BOM master): the structure and the components of a production order come from the project's current baseline (the released product BOM revision copied into the project, with its
    /// approved deviations). The legacy Bom / Process is only a read-only fallback for a project that has no baseline, and the order says so in Persian (ComponentSource = LEGACY).
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

            /// <summary>Items of the approved alternatives (options) of the BOM line this component comes from: the planner may swap in one of them with the same quantity.</summary>
            public List<int> AlternativeItemIds { get; set; } = new List<int>();
        }

        public sealed class Expected
        {
            public string Source { get; set; }
            public string Note { get; set; }
            public List<ComponentLine> Lines { get; set; } = new List<ComponentLine>();
            public string Warning { get; set; }
        }

        /// <summary>One production order the baseline asks for: a make part (or the top assembly) with the quantity of the whole work order.</summary>
        public sealed class DraftTarget
        {
            public string PartNumber { get; set; }
            public string PartName { get; set; }
            public InventoryItem Item { get; set; }
            public decimal Quantity { get; set; }
            public string Unit { get; set; }
        }

        private static bool SameUnit(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>1 FromUnit = Factor ToUnit, for the item first, then for every item; null when no conversion is registered.</summary>
        public static decimal? ConversionFactor(List<ItemUnitConversion> all, int itemId, string from, string to)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || SameUnit(from, to)) return 1m;
            foreach (ItemUnitConversion scope in all.Where(c => c.ItemId == itemId).Concat(all.Where(c => c.ItemId == null)))
            {
                if (SameUnit(scope.FromUnit, from) && SameUnit(scope.ToUnit, to) && scope.Factor > 0) return scope.Factor;
                if (SameUnit(scope.FromUnit, to) && SameUnit(scope.ToUnit, from) && scope.Factor > 0) return 1m / scope.Factor;
            }
            return null;
        }

        /// <summary>The item of a baseline line: its item, else the plain item of the part number (no stage), else the first item of the part.</summary>
        public static async Task<InventoryItem> ItemOfAsync(IUnitOfWork uow, int? itemId, string partNumber, CancellationToken ct)
        {
            if (itemId.HasValue)
            {
                InventoryItem direct = await uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId.Value, ct);
                if (direct != null) return direct;
            }
            if (string.IsNullOrWhiteSpace(partNumber)) return null;
            InventoryItem plain = await uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => !i.Deleted && i.PartNumber == partNumber && i.StageCode2 == null, ct);
            return plain ?? await uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.PartNumber == partNumber).OrderBy(i => i.Id).FirstOrDefaultAsync(ct);
        }

        private static decimal Quantity(decimal q, decimal? scrap) => Math.Round(scrap.HasValue && scrap.Value > 0 ? q * (1 + scrap.Value) : q, 4);

        /// <summary>Adds a component line (same item twice adds up); converts the unit of the BOM line to the unit of the item and applies the entered scrap factor.</summary>
        private static void AddLine(Expected result, List<ItemUnitConversion> conversions, InventoryItem item, decimal quantity, string lineUnit, decimal? scrap, IEnumerable<int> alternatives, List<string> warnings)
        {
            decimal q = quantity;
            decimal? factor = ConversionFactor(conversions, item.Id, lineUnit, item.Unit);
            if (factor.HasValue) q *= factor.Value;
            else warnings.Add($"واحد «{lineUnit}» ردیف BOM به واحد «{item.Unit}» کالای {item.Code} تبدیل نشد (ضریب تبدیل ثبت نشده)");
            q = Quantity(q, scrap);
            ComponentLine existing = result.Lines.FirstOrDefault(x => x.ItemId == item.Id);
            if (existing != null) { existing.Quantity += q; foreach (int a in alternatives) if (!existing.AlternativeItemIds.Contains(a)) existing.AlternativeItemIds.Add(a); }
            else result.Lines.Add(new ComponentLine { ItemId = item.Id, Quantity = q, Unit = item.Unit, AlternativeItemIds = alternatives.Distinct().ToList() });
        }

        private static decimal? ScrapOf(PbBomLine source) => source?.ScrapFactor;

        /// <summary>
        /// The components the order of this output item must consume: from the project baseline (the lines under the part, every occurrence of the part in the tree, with the scrap factor and the unit conversion),
        /// else (flagged) from the legacy process input. A stage item of a part (same part number, with a stage code) consumes no part components: they belong to the plain item of the part.
        /// </summary>
        public static async Task<Expected> ExpectedAsync(IUnitOfWork uow, int workOrderId, int? processId, InventoryItem output, decimal quantity, CancellationToken ct)
        {
            var result = new Expected { Source = SourceLegacy };
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent && b.WorkOrderId == workOrderId).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            if (baseline != null && output != null && !string.IsNullOrWhiteSpace(output.PartNumber))
            {
                string pn = output.PartNumber;
                PbBomHeader header = await uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(h => h.Id == baseline.BomHeaderId, ct);
                bool isTop = header != null && string.Equals(header.AssemblyPartNumber, pn, StringComparison.OrdinalIgnoreCase);
                List<PbProjectBomLine> all = await uow.Set<PbProjectBomLine>().AsNoTracking().Where(l => l.BaselineId == baseline.Id && !l.Deleted && !l.IsHeading && !l.IsRemoved).OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToListAsync(ct);
                List<PbProjectBomLine> occurrences = all.Where(l => l.PartNumber == pn).ToList();
                HashSet<int> occIds = occurrences.Select(o => o.Id).ToHashSet();
                List<PbProjectBomLine> children = all.Where(l => (l.ParentLineId.HasValue && occIds.Contains(l.ParentLineId.Value))
                    || (l.AssemblyPartNumber == pn)
                    || (isTop && l.AssemblyPartNumber == null && l.ParentLineId == null)).ToList();
                if (output.StageCode2 != null)
                {
                    // the components belong to the plain item of the part; with no plain item only the first stage item takes them
                    bool plain = await uow.Set<InventoryItem>().AsNoTracking().AnyAsync(i => !i.Deleted && i.PartNumber == pn && i.StageCode2 == null, ct);
                    int firstStage = plain ? 0 : await uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.PartNumber == pn && i.StageCode2 != null).OrderBy(i => i.Id).Select(i => i.Id).FirstOrDefaultAsync(ct);
                    if (plain || firstStage != output.Id) children = new List<PbProjectBomLine>();
                }
                if (children.Count > 0)
                {
                    decimal totalOfPart = isTop ? Math.Max(1, baseline.Count) : occurrences.Sum(o => o.TotalQty);
                    decimal scale = totalOfPart > 0 ? quantity / totalOfPart : quantity;
                    List<int> sourceIds = children.Where(c => c.SourceLineId.HasValue).Select(c => c.SourceLineId.Value).Distinct().ToList();
                    Dictionary<int, PbBomLine> sources = sourceIds.Count == 0 ? new Dictionary<int, PbBomLine>() : (await uow.Set<PbBomLine>().AsNoTracking().Where(s => sourceIds.Contains(s.Id)).ToListAsync(ct)).ToDictionary(s => s.Id);
                    List<PbBomLine> alternativeLines = sourceIds.Count == 0 ? new List<PbBomLine>() : await uow.Set<PbBomLine>().AsNoTracking().Where(s => !s.Deleted && s.AlternativeOfLineId != null && sourceIds.Contains(s.AlternativeOfLineId.Value)).ToListAsync(ct);
                    List<ItemUnitConversion> conversions = await uow.Set<ItemUnitConversion>().AsNoTracking().Where(c => !c.Deleted && c.IsActive).ToListAsync(ct);
                    var unresolved = new List<string>();
                    var warnings = new List<string>();
                    foreach (PbProjectBomLine l in children)
                    {
                        InventoryItem item = await ItemOfAsync(uow, l.ItemId, l.PartNumber, ct);
                        if (item == null || item.Id == output.Id) { unresolved.Add(l.PartNumber); continue; }
                        PbBomLine source = l.SourceLineId.HasValue && sources.TryGetValue(l.SourceLineId.Value, out PbBomLine s) ? s : null;
                        var alternatives = new List<int>();
                        if (l.SourceLineId.HasValue)
                            foreach (PbBomLine alt in alternativeLines.Where(a => a.AlternativeOfLineId == l.SourceLineId.Value))
                            {
                                InventoryItem altItem = await ItemOfAsync(uow, alt.ItemId, alt.PartNumber, ct);
                                if (altItem != null) alternatives.Add(altItem.Id);
                            }
                        decimal basis = l.TotalQty > 0 ? l.TotalQty * scale : l.Qty * quantity;
                        AddLine(result, conversions, item, basis, l.Unit, ScrapOf(source), alternatives, warnings);
                    }
                    result.Source = SourceBaseline;
                    result.Note = $"اجزاء از مبنای BOM پروژه (BOM {baseline.SourceBomCode} بازنگری {baseline.SourceBomRevision:00}) گرفته شد.";
                    var texts = new List<string>();
                    if (unresolved.Count > 0) texts.Add("کالای قطعه‌های زیر در انبار پیدا نشد و در اقلام نیامد: " + string.Join("، ", unresolved.Distinct()));
                    if (warnings.Count > 0) texts.Add(string.Join("؛ ", warnings.Distinct()));
                    if (texts.Count > 0) result.Warning = string.Join(" | ", texts);
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
        /// The production orders a project's baseline asks for: the top assembly (quantity = the number of products of the work order) and every make part (quantity of the whole work order).
        /// Null when the work order has no current baseline (the legacy drafting then applies, flagged).
        /// </summary>
        public static async Task<(List<DraftTarget> Targets, List<string> Notes)> PlanAsync(IUnitOfWork uow, int workOrderId, CancellationToken ct)
        {
            var notes = new List<string>();
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent && b.WorkOrderId == workOrderId).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            if (baseline == null) return (null, notes);
            PbBomHeader header = await uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(h => h.Id == baseline.BomHeaderId, ct);
            List<PbProjectBomLine> lines = await uow.Set<PbProjectBomLine>().AsNoTracking().Where(l => l.BaselineId == baseline.Id && !l.Deleted && !l.IsHeading && !l.IsRemoved).OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToListAsync(ct);
            var targets = new List<DraftTarget>();
            async Task Add(string part, string name, int? itemId, decimal qty, string unit)
            {
                if (string.IsNullOrWhiteSpace(part) || qty <= 0 || targets.Any(t => t.PartNumber == part)) return;
                InventoryItem item = await ItemOfAsync(uow, itemId, part, ct);
                if (item == null) { notes.Add($"قطعه {part}: کالایی با این شماره قطعه در انبار تعریف نشده است؛ دستور تولید ساخته نشد."); return; }
                targets.Add(new DraftTarget { PartNumber = part, PartName = name, Item = item, Quantity = qty, Unit = item.Unit ?? unit });
            }
            if (header != null && lines.Count > 0) await Add(header.AssemblyPartNumber, header.Title, null, Math.Max(1, baseline.Count), null);
            foreach (var g in lines.Where(l => l.MakeOrBuy == PbMakeOrBuy.Make && l.PartNumber != null).GroupBy(l => l.PartNumber).OrderBy(g => g.Min(x => x.SortOrder)))
                await Add(g.Key, g.First().PartName, g.First().ItemId, g.Sum(x => x.TotalQty), g.First().Unit);
            return (targets, notes);
        }

        private static bool SameUnitsOfList(List<ComponentLine> expected, List<(int ItemId, decimal Quantity)> wanted, bool allowAlternatives)
        {
            var a = expected.ToDictionary(x => x.ItemId, x => x.Quantity);
            var b = wanted.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            if (allowAlternatives)
            {
                // an approved alternative of a BOM line may stand in for the main item with the same quantity (never both)
                foreach (ComponentLine e in expected)
                {
                    if (b.ContainsKey(e.ItemId)) continue;
                    int alt = e.AlternativeItemIds.FirstOrDefault(x => b.ContainsKey(x) && !a.ContainsKey(x));
                    if (alt != 0) { b[e.ItemId] = b[alt]; b.Remove(alt); }
                }
            }
            if (a.Count != b.Count) return false;
            foreach (var kv in a) if (!b.TryGetValue(kv.Key, out decimal q) || Math.Abs(q - kv.Value) > 0.0001m) return false;
            return true;
        }

        /// <summary>The components of the NEW BOM revision an engineering change order names for the output part (exact list, per unit x quantity), or null when the order names none for the part.</summary>
        public static async Task<List<ComponentLine>> RevisionComponentsAsync(IUnitOfWork uow, int bomHeaderId, InventoryItem output, decimal quantity, CancellationToken ct)
        {
            PbBomHeader h = await uow.Set<PbBomHeader>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == bomHeaderId, ct);
            if (h == null || output == null || !string.Equals(h.AssemblyPartNumber, output.PartNumber, StringComparison.OrdinalIgnoreCase)) return null;
            var result = new Expected();
            List<ItemUnitConversion> conversions = await uow.Set<ItemUnitConversion>().AsNoTracking().Where(c => !c.Deleted && c.IsActive).ToListAsync(ct);
            var warnings = new List<string>();
            foreach (PbBomLine l in h.Lines.Where(l => !l.Deleted && !l.IsHeading && l.AlternativeOfLineId == null).OrderBy(l => l.SortOrder).ThenBy(l => l.Id))
            {
                InventoryItem item = await ItemOfAsync(uow, l.ItemId, l.PartNumber, ct);
                if (item == null || item.Id == output.Id) continue;
                AddLine(result, conversions, item, l.Qty * quantity, l.Unit, l.ScrapFactor, new List<int>(), warnings);
            }
            return result.Lines;
        }

        /// <summary>
        /// Is a component list (planner's) the one the master gives? Equal sets of items with equal quantities (an approved alternative of a BOM line may stand in for it). A different list is allowed only
        /// (a) while the legacy BOM is unfrozen and the order came from it (transition), or (b) when an APPROVED engineering change order lists THIS order with the disposition «change to the new revision» or «rework»
        /// and the list is exactly the component list of the new BOM revision the order names. A deviation changes the baseline first, so the expected list then already contains it. Returns the Persian refusal or null.
        /// </summary>
        public static async Task<string> ChangeProblemAsync(IUnitOfWork uow, MfProductionOrder order, InventoryItem output, List<(int ItemId, decimal Quantity)> wanted, DateTime today, CancellationToken ct)
        {
            Expected expected = await ExpectedAsync(uow, order.WorkOrderId, order.ProcessId, output, order.Quantity, ct);
            bool manualWithoutMaster = expected.Source == SourceLegacy && order.ProcessId == null && !order.AutoDrafted && order.ComponentSource == null;
            if (manualWithoutMaster) return null;   // a manual order of a part the project has no baseline for: nothing to compare with
            if (SameUnitsOfList(expected.Lines, wanted, true)) return null;
            // transition: while the legacy BOM is unfrozen (a safety-profile switch) the components of an order that came from the legacy process stay editable
            if (expected.Source == SourceLegacy && (await ProductBank.PbSettingKeys.LoadAsync(uow, ct))[ProductBank.PbSettingKeys.LegacyBomFrozen] == "false") return null;
            if (output != null && !string.IsNullOrWhiteSpace(output.PartNumber))
            {
                string key = EcRules.Key("ORDER", order.Id);
                List<(int OrderId, int BomHeaderId)> named = await EcGate.ApprovedBomChangesForOrderAsync(uow, order.Id, key, output.PartNumber, ct);
                foreach (var n in named)
                {
                    List<ComponentLine> revision = await RevisionComponentsAsync(uow, n.BomHeaderId, output, order.Quantity, ct);
                    if (revision != null && SameUnitsOfList(revision, wanted, false)) return null;
                }
            }
            return "اقلام مصرفی این دستور از مبنای BOM پروژه گرفته می‌شود و آزادانه عوض نمی‌شود. برای تغییر، یک انحراف تاییدشده (مهندسی و برنامه‌ریزی) در مبنای BOM پروژه ثبت کنید، یا دستور تغییر مهندسی تاییدشده‌ای که همین دستور را با تکلیف «تغییر به بازنگری جدید» یا «بازکاری» نام برده و اقلام دقیقاً اقلام بازنگری جدید BOM باشد.";
        }
    }
}
