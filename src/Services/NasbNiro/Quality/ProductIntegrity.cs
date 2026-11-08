using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectBalance;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    /// <summary>The result of the physical configuration audit of one work order.</summary>
    public sealed class PcaResult
    {
        public bool Applicable { get; set; }
        public string NotApplicableReason { get; set; }
        public List<PcaLineDto> Lines { get; } = new List<PcaLineDto>();
        public List<string> Problems => Lines.Where(l => l.Problem != null).Select(l => $"{l.PartNumber}: {l.Problem}").ToList();
        public bool Passed => !Applicable || Lines.All(l => l.Problem == null);
    }

    /// <summary>
    /// Package B (ISO 10007, H10): the physical configuration audit. Every line of the current project BOM baseline (with the approved deviations already applied) needs an issued / installed lot or serial of the
    /// same part number (or of an approved substitute) in the genealogy of the work order, and the quantities must match. A mismatch blocks the release with the list of the parts.
    /// </summary>
    public static class PhysicalConfigurationAudit
    {
        public static async Task<PcaResult> RunAsync(IUnitOfWork uow, int workOrderId, CancellationToken ct, int? releaseNoteId = null)
        {
            var result = new PcaResult();
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(uow, ct);
            if (st[QualitySettingKeys.PkbPcaEnabled] < 1) { result.NotApplicableReason = "ممیزی پیکربندی فیزیکی در تنظیمات غیرفعال است."; return result; }
            bool isProject = await uow.Set<Entities.Model.NasbNiro.ProjectControl.PcWorkOrderInfo>().AsNoTracking().AnyAsync(i => i.WorkOrderId == workOrderId && !i.Deleted, ct);
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => b.WorkOrderId == workOrderId && b.IsCurrent && !b.Deleted).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            if (baseline == null)
            {
                if (!isProject)
                {
                    result.NotApplicableReason = "اعمال نمی‌شود: این دستور کار پروژه‌ای نیست (ساخت برای موجودی انبار) و BOM پروژه ندارد.";
                    return result;
                }
                // H3: a project work order without its BOM baseline cannot be audited, so it cannot be released
                result.Applicable = true;
                result.Lines.Add(new PcaLineDto { PartNumber = "-", PartName = "BOM پروژه", Problem = "برای این دستور کار پروژه‌ای BOM پروژه (baseline) ساخته نشده است؛ ممیزی پیکربندی فیزیکی ممکن نیست و ترخیص مسدود است." });
                return result;
            }
            result.Applicable = true;
            decimal tolerance = st[QualitySettingKeys.PkbPcaTolerance] / 100m;
            decimal bulkExtra = st[QualitySettingKeys.PkbPcaBulkExtra] / 100m;

            List<PbProjectBomLine> all = await uow.Set<PbProjectBomLine>().AsNoTracking().Where(l => l.BaselineId == baseline.Id && !l.Deleted && !l.IsRemoved && !l.IsHeading).ToListAsync(ct);
            var byId = all.ToDictionary(l => l.Id);
            List<PbProjectBomLine> lines = all.Where(l => l.TotalQty > 0).ToList();
            if (lines.Count == 0) return result;

            // the units of the product released so far (this note included): the audit is done per released unit, cumulative over the partial deliveries of the work order
            int? productItemId = await uow.Set<PbBomHeader>().AsNoTracking().Where(h => h.Id == baseline.BomHeaderId).Select(h => h.ProductItemId).FirstOrDefaultAsync(ct);
            decimal unitsFactor = 1m;
            if (productItemId.HasValue && baseline.Count > 0)
            {
                decimal units = await uow.Set<ReleaseNoteLine>().AsNoTracking().Where(l => l.ItemId == productItemId.Value && l.ReleaseNote.WorkOrderId == workOrderId && !l.ReleaseNote.Deleted
                    && (l.ReleaseNote.Status == ReleaseNoteStatus.Released || (releaseNoteId.HasValue && l.ReleaseNoteId == releaseNoteId.Value))).SumAsync(l => (decimal?)l.Quantity, ct) ?? 0m;
                if (units > 0m) unitsFactor = Math.Min(units, baseline.Count) / baseline.Count;
            }

            // the items of the lines: by id, else by part number (the part number of the item or its code)
            List<string> partNumbers = all.Where(l => !l.ItemId.HasValue && l.PartNumber != null).Select(l => l.PartNumber.Trim()).Distinct().ToList();
            var byPart = (await uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && (partNumbers.Contains(i.PartNumber) || partNumbers.Contains(i.Code))).Select(i => new { i.Id, i.PartNumber, i.Code }).ToListAsync(ct)).ToList();
            int? ItemOf(PbProjectBomLine l)
            {
                if (l == null) return null;
                if (l.ItemId.HasValue) return l.ItemId;
                string p = l.PartNumber?.Trim();
                if (p == null) return null;
                var hit = byPart.FirstOrDefault(i => string.Equals(i.PartNumber, p, StringComparison.OrdinalIgnoreCase)) ?? byPart.FirstOrDefault(i => string.Equals(i.Code, p, StringComparison.OrdinalIgnoreCase));
                return hit?.Id;
            }

            // what really happened, level by level: the genealogy of the production orders (input -> output) and the installed assembly links (child serial or lot -> parent serial)
            List<int> orderIds = await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => o.WorkOrderId == workOrderId && !o.Deleted && o.Status != MfOrderStatus.Cancelled).Select(o => o.Id).ToListAsync(ct);
            var genealogy = orderIds.Count == 0 ? new List<(int Output, int Input, decimal Qty)>()
                : (await uow.Set<GenealogyLink>().AsNoTracking().Where(g => !g.Deleted && orderIds.Contains(g.ProductionOrderId)).Select(g => new { g.OutputItemId, g.InputItemId, g.Quantity }).ToListAsync(ct))
                    .Select(g => (Output: g.OutputItemId, Input: g.InputItemId, Qty: g.Quantity)).ToList();
            var installed = (await uow.Set<AssemblyLink>().AsNoTracking().Where(a => !a.Deleted && a.WorkOrderId == workOrderId && a.RemovedAt == null)
                .Select(a => new { Parent = (int?)a.ParentSerialUnit.ItemId, Child = a.ChildSerialUnit != null ? (int?)a.ChildSerialUnit.ItemId : (a.ChildLot != null ? (int?)a.ChildLot.ItemId : null), a.Quantity }).ToListAsync(ct))
                .Where(a => a.Child.HasValue).Select(a => (Parent: a.Parent ?? 0, Child: a.Child.Value, Qty: a.Quantity > 0 ? a.Quantity : 1m)).ToList();

            List<int> itemIds = lines.Select(ItemOf).Where(i => i.HasValue).Select(i => i.Value).Distinct().ToList();
            List<ItemSubstitute> subs = await uow.Set<ItemSubstitute>().AsNoTracking().Where(s => !s.Deleted && s.Status == SubstituteStatus.Approved && itemIds.Contains(s.ItemId)
                && (s.Scope == SubstituteScope.Global || s.WorkOrderId == workOrderId)).ToListAsync(ct);
            var stockUnits = await uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Unit }).ToDictionaryAsync(i => i.Id, i => i.Unit, ct);
            List<ItemUnitConversion> conversions = await uow.Set<ItemUnitConversion>().AsNoTracking().Where(c => !c.Deleted && c.IsActive && (c.ItemId == null || itemIds.Contains(c.ItemId.Value))).ToListAsync(ct);
            List<int> srcIds = lines.Where(l => l.SourceLineId.HasValue).Select(l => l.SourceLineId.Value).Distinct().ToList();
            var scrap = srcIds.Count == 0 ? new Dictionary<int, decimal>()
                : (await uow.Set<PbBomLine>().AsNoTracking().Where(l => srcIds.Contains(l.Id)).Select(l => new { l.Id, l.ScrapFactor, l.NetWeight, l.GrossWeight }).ToListAsync(ct))
                    .ToDictionary(l => l.Id, l => l.ScrapFactor ?? (l.NetWeight.HasValue && l.NetWeight.Value > 0 && l.GrossWeight.HasValue ? Math.Max(0m, l.GrossWeight.Value / l.NetWeight.Value - 1m) : 0m));

            foreach (var group in lines.GroupBy(l => (ItemOf(l)?.ToString(CultureInfo.InvariantCulture) ?? "p:" + (l.PartNumber ?? "").Trim().ToUpperInvariant()) + "|" + (l.ParentLineId.HasValue ? ItemOf(byId.TryGetValue(l.ParentLineId.Value, out PbProjectBomLine pl) ? pl : null)?.ToString(CultureInfo.InvariantCulture) ?? "?" : "top")))
            {
                PbProjectBomLine first = group.First();
                int? itemId = ItemOf(first);
                PbProjectBomLine parentLine = first.ParentLineId.HasValue && byId.TryGetValue(first.ParentLineId.Value, out PbProjectBomLine pp) ? pp : null;
                int? parentItem = parentLine != null ? ItemOf(parentLine) : productItemId;
                decimal required = group.Sum(l => l.TotalQty) * unitsFactor;
                bool bulk = CharacteristicRules.IsBulkUnit(first.Unit);
                var line = new PcaLineDto { PartNumber = first.PartNumber ?? "-", PartName = first.PartName, Required = required };
                if (!itemId.HasValue)
                {
                    line.Problem = "کالایی با این شماره قطعه در انبار تعریف نشده است؛ نصب یا مصرف آن قابل ردیابی نیست.";
                    result.Lines.Add(line);
                    continue;
                }
                IEnumerable<int> alternatives = new[] { itemId.Value }.Concat(subs.Where(x => x.ItemId == itemId.Value).Select(x => x.SubstituteItemId));
                decimal found = 0m;
                foreach (int alt in alternatives)
                {
                    decimal ratio = alt == itemId.Value ? 1m : subs.First(x => x.ItemId == itemId.Value && x.SubstituteItemId == alt).Ratio;
                    if (ratio <= 0m) continue;
                    decimal g = genealogy.Where(x => x.Input == alt && (!parentItem.HasValue || x.Output == parentItem.Value)).Sum(x => x.Qty);
                    if (!parentItem.HasValue || g == 0m) g = genealogy.Where(x => x.Input == alt && !parentItem.HasValue).Sum(x => x.Qty);
                    decimal a = installed.Where(x => x.Child == alt && (!parentItem.HasValue || x.Parent == parentItem.Value)).Sum(x => x.Qty);
                    decimal one = bulk ? g : Math.Max(g, a); // the genealogy and the assembly link record the same event for a counted part
                    if (bulk && alt == itemId.Value && stockUnits.TryGetValue(alt, out string su) && !SameUnit(su, first.Unit))
                    {
                        decimal? factor = ConversionFactor(conversions, alt, su, first.Unit);
                        if (!factor.HasValue) { line.Problem = $"تبدیل واحد {su} به {first.Unit} برای این ماده تعریف نشده است؛ مقایسه با BOM ممکن نیست."; break; }
                        one *= factor.Value;
                    }
                    found += one / ratio;
                }
                if (line.Problem != null) { result.Lines.Add(line); continue; }
                line.Found = found;
                decimal allowance = bulk ? (first.SourceLineId.HasValue && scrap.TryGetValue(first.SourceLineId.Value, out decimal sc) ? sc : 0m) + bulkExtra : tolerance;
                line.Problem = PcaRules.Judge(required, found, tolerance, allowance, bulk);
                result.Lines.Add(line);
            }
            return result;
        }

        private static bool SameUnit(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>The factor that turns a quantity in <paramref name="from"/> into <paramref name="to"/> (item-specific conversions first, then the general ones; the reverse conversion is used inverted). Null when none is defined.</summary>
        private static decimal? ConversionFactor(List<ItemUnitConversion> all, int itemId, string from, string to)
        {
            foreach (var scope in new[] { all.Where(c => c.ItemId == itemId), all.Where(c => c.ItemId == null) })
            {
                ItemUnitConversion direct = scope.FirstOrDefault(c => SameUnit(c.FromUnit, from) && SameUnit(c.ToUnit, to) && c.Factor > 0);
                if (direct != null) return direct.Factor;
                ItemUnitConversion reverse = scope.FirstOrDefault(c => SameUnit(c.FromUnit, to) && SameUnit(c.ToUnit, from) && c.Factor > 0);
                if (reverse != null) return 1m / reverse.Factor;
            }
            return null;
        }

        /// <summary>The quantity of every item that went into the work order: the genealogy links of its production orders, or the installed assembly links, whichever is larger (the two record the same event).</summary>
        public static async Task<Dictionary<int, decimal>> InstalledQuantitiesAsync(IUnitOfWork uow, int workOrderId, CancellationToken ct)
        {
            List<int> orderIds = await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => o.WorkOrderId == workOrderId && !o.Deleted && o.Status != MfOrderStatus.Cancelled).Select(o => o.Id).ToListAsync(ct);
            var genealogy = orderIds.Count == 0 ? new Dictionary<int, decimal>()
                : (await uow.Set<GenealogyLink>().AsNoTracking().Where(g => !g.Deleted && orderIds.Contains(g.ProductionOrderId)).GroupBy(g => g.InputItemId).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct))
                    .ToDictionary(x => x.Id, x => x.Qty);
            var assembly = new Dictionary<int, decimal>();
            var links = await uow.Set<AssemblyLink>().AsNoTracking().Where(a => !a.Deleted && a.WorkOrderId == workOrderId && a.RemovedAt == null)
                .Select(a => new { SerialItem = a.ChildSerialUnit != null ? (int?)a.ChildSerialUnit.ItemId : null, LotItem = a.ChildLot != null ? (int?)a.ChildLot.ItemId : null, a.Quantity }).ToListAsync(ct);
            foreach (var a in links)
            {
                int? id = a.SerialItem ?? a.LotItem;
                if (!id.HasValue) continue;
                assembly[id.Value] = (assembly.TryGetValue(id.Value, out decimal q) ? q : 0m) + (a.Quantity > 0 ? a.Quantity : 1m);
            }
            var merged = new Dictionary<int, decimal>(genealogy);
            foreach (var kv in assembly) merged[kv.Key] = merged.TryGetValue(kv.Key, out decimal g) ? Math.Max(g, kv.Value) : kv.Value;
            return merged;
        }
    }

    /// <summary>The pure judgement of one line of the physical configuration audit.</summary>
    public static class PcaRules
    {
        /// <summary>
        /// A counted part: the quantity found must equal the BOM within <paramref name="countTolerance"/> (zero by default). A bulk material (kg, m, l...): never less than the BOM quantity (less than the BOM allowance means
        /// the piece was made of something else) and at most the BOM quantity plus <paramref name="bulkAllowance"/> (the scrap allowance of the BOM line).
        /// </summary>
        public static string Judge(decimal required, decimal found, decimal countTolerance, decimal bulkAllowance, bool bulk)
        {
            string R(decimal v) => CharacteristicRules.Num(Math.Round(v, 4));
            if (found <= 0m) return $"هیچ بچ یا سریالی از این قطعه در شجره‌نامه ساخت ثبت نشده است (لازم: {R(required)}).";
            decimal low = required * (1m - countTolerance);
            decimal high = bulk ? required * (1m + bulkAllowance) : required * (1m + countTolerance);
            if (found < low) return $"مقدار نصب/مصرف‌شده کمتر از BOM است (لازم: {R(required)}، یافت‌شده: {R(found)}).";
            if (found > high) return $"مقدار نصب/مصرف‌شده بیشتر از BOM{(bulk ? " و ضایعات مجاز آن" : string.Empty)} است (لازم: {R(required)}، یافت‌شده: {R(found)}).";
            return null;
        }
    }

    /// <summary>Builds the as-built snapshot of a released unit (JSON + SHA-256). The record is immutable; a correction is a new version.</summary>
    public static class AsBuiltBuilder
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static string Serialize(AsBuiltSnapshotDto snapshot) => JsonSerializer.Serialize(snapshot, Json);

        public static AsBuiltSnapshotDto Parse(string json) => string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<AsBuiltSnapshotDto>(json, Json);

        /// <summary>
        /// Issues the as-built records of a released note: one per serial number, one per lot line. An earlier current record of the same unit becomes Superseded (version + 1, with the reason).
        /// Everything is added to the unit of work; the caller saves.
        /// </summary>
        public static async Task<List<AsBuiltRecord>> IssueAsync(IUnitOfWork uow, ReleaseNote note, WorkOrder wo, string reason, DateTimeOffset now, int userId, CancellationToken ct, int? onlyItemId = null, string onlySerial = null, bool pending = false)
        {
            var created = new List<AsBuiltRecord>();
            List<ReleaseNoteLine> lines = await uow.Set<ReleaseNoteLine>().AsNoTracking().Include(l => l.Item).Where(l => l.ReleaseNoteId == note.Id).OrderBy(l => l.RowNumber).ToListAsync(ct);
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => b.WorkOrderId == wo.Id && b.IsCurrent && !b.Deleted).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            PcaResult pca = await PhysicalConfigurationAudit.RunAsync(uow, wo.Id, ct, note.Id);
            // Package A1: the design reviews (verification, validation) that covered the project BOM, part of the as-built record
            global::Services.NasbNiro.ProductBank.DesignFcaResult designFca = await global::Services.NasbNiro.ProductBank.DesignGate.FcaAsync(uow, wo.Id, ct);
            List<AsBuiltDesignReviewDto> designReviews = new List<AsBuiltDesignReviewDto>();
            foreach (PbDesignReview dr in designFca.Reviews.OrderBy(x => x.Number, StringComparer.Ordinal))
            {
                string planNumber = await uow.Set<PbDesignPlan>().AsNoTracking().Where(x => x.Id == dr.PlanId).Select(x => x.Number).FirstOrDefaultAsync(ct);
                designReviews.Add(new AsBuiltDesignReviewDto { Number = dr.Number, Type = dr.Type.ToString(), Outcome = dr.Outcome.ToString(), PlanNumber = planNumber, ClosedAt = dr.ClosedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Covers = designFca.Bom == null ? null : designFca.Bom.Code + " r" + designFca.Bom.Revision.ToString(CultureInfo.InvariantCulture) });
            }
            List<AsBuiltDeviationDto> deviations = await DeviationsAsync(uow, wo.Id, baseline, ct);
            List<AsBuiltNcrDto> ncrs = (await uow.Set<NonConformance>().AsNoTracking().Where(n => n.WorkOrderId == wo.Id && !n.Deleted && n.Status != NcrStatus.Cancelled).OrderBy(n => n.Id).ToListAsync(ct))
                .Select(n => new AsBuiltNcrDto { Number = n.Number, Status = n.Status.ToString(), Disposition = n.Disposition.ToString(), ConcessionApproved = ConcessionRules.IsValid(n, false) }).ToList();
            List<AsBuiltRoutingDto> routings = baseline == null ? new List<AsBuiltRoutingDto>()
                : (await uow.Set<PbProjectRouting>().AsNoTracking().Where(r => r.BaselineId == baseline.Id && !r.Deleted).OrderBy(r => r.PartNumber).ToListAsync(ct))
                    .Select(r => new AsBuiltRoutingDto { PartNumber = r.PartNumber, Revision = r.Revision }).ToList();
            List<int> orderIds = await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => o.WorkOrderId == wo.Id && !o.Deleted && o.Status != MfOrderStatus.Cancelled).Select(o => o.Id).ToListAsync(ct);
            var orders = await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.ItemId, o.RoutingRevisionId }).ToListAsync(ct);
            List<int> routingRevisionIds = orders.Where(o => o.RoutingRevisionId.HasValue).Select(o => o.RoutingRevisionId.Value).Distinct().ToList();
            foreach (var rr in await uow.Set<MfRoutingRevision>().AsNoTracking().Where(r => routingRevisionIds.Contains(r.Id)).ToListAsync(ct))
            {
                string part = rr.ItemId.HasValue ? await uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == rr.ItemId).Select(i => i.PartNumber ?? i.Code).FirstOrDefaultAsync(ct) : "گروه کالا";
                if (!routings.Any(x => x.PartNumber == part && x.Revision == rr.Revision)) routings.Add(new AsBuiltRoutingDto { PartNumber = part, Revision = rr.Revision });
            }
            List<AsBuiltFaiDto> fais = (await uow.Set<QcFirstArticle>().AsNoTracking().Where(f => !f.Deleted && f.WorkOrderId == wo.Id && f.Status != FaiStatus.Voided).OrderBy(f => f.Id).ToListAsync(ct))
                .Select(f => new AsBuiltFaiDto { Number = f.Number, PartNumber = f.PartNumber, FingerprintText = f.FingerprintText, Status = f.Status.ToString() }).ToList();
            // Package C: the special process runs of the work order (never the voided ones) and, below, the certificates of the lots built in
            var spRunRows = await (from r in uow.Set<SpRun>().AsNoTracking()
                                   join t in uow.Set<SpProcessType>().AsNoTracking() on r.TypeId equals t.Id
                                   where !r.Deleted && r.WorkOrderId == wo.Id && r.Status == SpRunStatus.Recorded
                                   orderby r.Id
                                   select new { r, Type = t.Title }).ToListAsync(ct);
            var spRuns = new List<(SpRun Run, AsBuiltSpecialProcessDto Dto)>();
            foreach (var row in spRunRows)
            {
                SpRun r = row.r;
                string qualNumber = r.QualificationId.HasValue ? await uow.Set<SpQualification>().AsNoTracking().Where(q => q.Id == r.QualificationId.Value).Select(q => q.Number + " / " + q.ProcedureNumber + " " + q.ProcedureRevisionCode).FirstOrDefaultAsync(ct) : null;
                string step = r.OperationId.HasValue ? await (from o in uow.Set<MfOperation>().AsNoTracking() join po in uow.Set<MfProductionOrder>().AsNoTracking() on o.OrderId equals po.Id where o.Id == r.OperationId.Value select po.Number + "-" + o.Sequence).FirstOrDefaultAsync(ct) : null;
                string stamp = r.OperatorPersonId.HasValue ? await uow.Set<QualifiedPerson>().AsNoTracking().Where(p => p.Id == r.OperatorPersonId.Value).Select(p => p.StampId ?? p.PersonnelCode ?? p.Name).FirstOrDefaultAsync(ct) : null;
                string machine = r.MachineId.HasValue ? await uow.Set<Entities.Model.NasbNiro.Constract.Machine>().AsNoTracking().Where(m => m.Id == r.MachineId.Value).Select(m => m.Title).FirstOrDefaultAsync(ct) : null;
                string supplier = r.SupplierId.HasValue ? await uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => p.Id == r.SupplierId.Value).Select(p => p.Name).FirstOrDefaultAsync(ct) : null;
                spRuns.Add((r, new AsBuiltSpecialProcessDto
                {
                    RunNumber = r.Number, ProcessType = row.Type, QualificationNumber = qualNumber?.Split(" / ")[0], ProcedureText = qualNumber == null ? null : string.Join(" / ", qualNumber.Split(" / ").Skip(1)), Step = step, SerialNumbers = r.SerialNumbers,
                    Quantity = r.Quantity, OperatorStamp = stamp, Machine = machine, StartedAt = r.StartedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    EndedAt = r.EndedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), Supplier = supplier, CertificateNumber = r.CertificateNumber, Result = r.Result.ToString(), Review = r.ReviewStatus.ToString(),
                }));
            }
            var links = await uow.Set<GenealogyLink>().AsNoTracking().Where(g => !g.Deleted && orderIds.Contains(g.ProductionOrderId))
                .Select(g => new
                {
                    g.OutputItemId, OutputSerial = g.OutputSerialUnit.SerialNumber, InputPart = g.InputItem.PartNumber, InputCode = g.InputItem.Code, InputTitle = g.InputItem.Title,
                    Lot = g.InputLot.LotNumber, Heat = g.InputLot.HeatNumber, Cert = g.InputLot.CertificateNumber, Serial = g.InputSerialUnit.SerialNumber, g.Quantity,
                }).ToListAsync(ct);
            var assemblies = await uow.Set<AssemblyLink>().AsNoTracking().Where(a => !a.Deleted && a.WorkOrderId == wo.Id && a.RemovedAt == null)
                .Select(a => new
                {
                    Parent = a.ParentSerialUnit.SerialNumber, ParentItem = a.ParentSerialUnit.ItemId, ChildPart = a.ChildSerialUnit != null ? a.ChildSerialUnit.Item.PartNumber : a.ChildLot.Item.PartNumber,
                    ChildCode = a.ChildSerialUnit != null ? a.ChildSerialUnit.Item.Code : a.ChildLot.Item.Code, ChildTitle = a.ChildSerialUnit != null ? a.ChildSerialUnit.Item.Title : a.ChildLot.Item.Title,
                    Serial = a.ChildSerialUnit.SerialNumber, Lot = a.ChildLot.LotNumber, Heat = a.ChildLot.HeatNumber, Cert = a.ChildLot.CertificateNumber, a.Quantity,
                }).ToListAsync(ct);

            foreach (ReleaseNoteLine line in lines)
            {
                if (onlyItemId.HasValue && line.ItemId != onlyItemId.Value) continue;
                var units = new List<string>();
                if (line.Item.TrackingMode == ItemTrackingMode.Serial)
                    units.AddRange((line.SerialNumbers ?? string.Empty).Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0));
                else units.Add(null);
                ResolvedCharacteristics resolved = await CharacteristicResolver.ForItemAsync(uow, line.ItemId, null, ct);
                var drawings = new List<AsBuiltDrawingDto>();
                foreach (PartDrawing d in resolved.Drawings)
                {
                    QcCharacteristicList list = resolved.Lists.FirstOrDefault(x => x.RevisionId == d.RevisionId);
                    drawings.Add(new AsBuiltDrawingDto { PartNumber = line.Item.PartNumber ?? line.Item.Code, DocumentNumber = d.Number, RevisionCode = d.RevisionCode, ListVersion = list?.Version });
                }
                foreach (string serial in units)
                {
                    if (onlyItemId.HasValue && !string.Equals(serial, onlySerial, StringComparison.OrdinalIgnoreCase)) continue;
                    var snap = new AsBuiltSnapshotDto
                    {
                        WorkOrderCode = wo.Code, ReleaseNoteNumber = note.Number, ReleasedAt = note.ReleasedAt ?? now, ProductCode = line.Item.Code, ProductTitle = line.Item.Title, ItemCode = line.Item.Code,
                        ItemTitle = line.Item.Title, SerialNumber = serial, Quantity = serial == null ? line.Quantity : 1m, BomCode = baseline?.SourceBomCode, BomRevision = baseline?.SourceBomRevision, ProductCount = baseline?.Count ?? 0,
                        Routings = routings.OrderBy(r => r.PartNumber, StringComparer.Ordinal).ThenBy(r => r.Revision).ToList(), Drawings = drawings, Deviations = deviations, Ncrs = ncrs, FirstArticles = fais,
                        PcaFindings = pca.Applicable ? pca.Problems : new List<string>(),
                        DesignReviews = designReviews,
                    };
                    foreach (var g in links.Where(x => x.OutputItemId == line.ItemId && (serial == null || x.OutputSerial == null || string.Equals(x.OutputSerial, serial, StringComparison.OrdinalIgnoreCase))))
                        snap.Installed.Add(new AsBuiltInstalledDto { PartNumber = g.InputPart ?? g.InputCode, ItemCode = g.InputCode, ItemTitle = g.InputTitle, LotNumber = g.Lot, SerialNumber = g.Serial, HeatNumber = g.Heat, CertificateNumber = g.Cert, Quantity = g.Quantity, Via = "genealogy" });
                    foreach (var a in assemblies.Where(x => x.ParentItem == line.ItemId && (serial == null || string.Equals(x.Parent, serial, StringComparison.OrdinalIgnoreCase))))
                        snap.Installed.Add(new AsBuiltInstalledDto { PartNumber = a.ChildPart ?? a.ChildCode, ItemCode = a.ChildCode, ItemTitle = a.ChildTitle, LotNumber = a.Lot, SerialNumber = a.Serial, HeatNumber = a.Heat, CertificateNumber = a.Cert, Quantity = a.Quantity > 0 ? a.Quantity : 1m, Via = "assembly" });
                    snap.Installed = snap.Installed.OrderBy(i => i.PartNumber, StringComparer.Ordinal).ThenBy(i => i.LotNumber, StringComparer.Ordinal).ThenBy(i => i.SerialNumber, StringComparer.Ordinal).ToList();
                    snap.SpecialProcesses = spRuns.Where(x => serial == null || string.IsNullOrWhiteSpace(x.Run.SerialNumbers) || (x.Run.SerialNumbers ?? string.Empty).Split(new[] { '\n', '\r', ',', '،' }, StringSplitOptions.RemoveEmptyEntries).Any(t => string.Equals(t.Trim(), serial, StringComparison.OrdinalIgnoreCase)))
                        .Select(x => x.Dto).ToList();
                    List<string> lotNumbers = snap.Installed.Where(i => !string.IsNullOrEmpty(i.LotNumber)).Select(i => i.LotNumber).Distinct().ToList();
                    if (lotNumbers.Count > 0)
                    {
                        var certs = await (from c in uow.Set<MaterialCertificate>().AsNoTracking()
                                           where !c.Deleted && c.Result == CertificateResult.Accepted && lotNumbers.Contains(c.Lot.LotNumber)
                                           orderby c.Lot.LotNumber, c.Number
                                           select new { c.Lot.LotNumber, ItemCode = c.Lot.Item.Code, c.Lot.HeatNumber, c.Number, c.Type, c.Grade, c.Standard, c.Evaluation, c.InspectorOrganisation, c.InspectorName, c.ManufacturerSignatory }).ToListAsync(ct);
                        snap.Certificates = certs.Where(c => snap.Installed.Any(i => i.LotNumber == c.LotNumber && i.ItemCode == c.ItemCode)).Select(c => new AsBuiltCertificateDto   // Fix round 1 (Low): the lot of the same item, not just the same lot number
                        {
                            LotNumber = c.LotNumber, HeatNumber = c.HeatNumber, Number = c.Number, Type = CertificateRules.TypeText(c.Type), Grade = c.Grade, Standard = c.Standard, Evaluation = c.Evaluation.ToString(),
                            Inspector = c.Type == CertificateType.T32 ? (c.InspectorOrganisation + " / " + c.InspectorName).Trim(' ', '/') : c.ManufacturerSignatory,
                        }).ToList();
                    }
                    string json = Serialize(snap);
                    AsBuiltRecord current = await uow.Set<AsBuiltRecord>().FirstOrDefaultAsync(r => !r.Deleted && r.WorkOrderId == wo.Id && r.ItemId == line.ItemId && r.SerialNumber == serial && r.Status == AsBuiltStatus.Current, ct);
                    int version = (await uow.Set<AsBuiltRecord>().Where(r => r.WorkOrderId == wo.Id && r.ItemId == line.ItemId && r.SerialNumber == serial).Select(r => (int?)r.Version).MaxAsync(ct) ?? 0) + 1;
                    if (current != null && !pending) { current.Status = AsBuiltStatus.Superseded; current.SupersededByVersion = version; }
                    var record = new AsBuiltRecord
                    {
                        WorkOrderId = wo.Id, ReleaseNoteId = note.Id, ItemId = line.ItemId, SerialNumber = serial, Version = version, Status = pending ? AsBuiltStatus.Pending : AsBuiltStatus.Current,
                        Reason = version == 1 ? null : reason, ProductCode = line.Item.Code, BomText = baseline == null ? null : $"{baseline.SourceBomCode} بازنگری {baseline.SourceBomRevision}",
                        SnapshotJson = json, Sha256 = CharacteristicRules.Sha256Hex(json), CreateDate = now, ModifyDate = now, CreatorUserId = userId, ModifierUserId = userId,
                    };
                    if (serial != null)
                        record.SerialUnitId = await uow.Set<SerialUnit>().AsNoTracking().Where(u => u.ItemId == line.ItemId && u.SerialNumber == serial && !u.Deleted).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
                    uow.Set<AsBuiltRecord>().Add(record);
                    created.Add(record);
                }
            }
            return created;
        }

        private static async Task<List<AsBuiltDeviationDto>> DeviationsAsync(IUnitOfWork uow, int workOrderId, PbProjectBaseline baseline, CancellationToken ct)
        {
            var list = new List<AsBuiltDeviationDto>();
            if (baseline != null)
                foreach (PbProjectBomChange c in await uow.Set<PbProjectBomChange>().AsNoTracking().Where(c => c.BaselineId == baseline.Id && !c.Deleted && (c.Status == PbChangeStatus.Approved || c.Status == PbChangeStatus.Applied)).OrderBy(c => c.Id).ToListAsync(ct))
                    list.Add(new AsBuiltDeviationDto { Number = c.Number, Kind = c.Kind.ToString(), PartNumber = c.PartNumber, Reason = c.Reason, Status = c.Status.ToString() });
            foreach (ItemSubstitute s in await uow.Set<ItemSubstitute>().AsNoTracking().Where(s => !s.Deleted && s.Status == SubstituteStatus.Approved && s.Scope == SubstituteScope.Project && s.WorkOrderId == workOrderId).OrderBy(s => s.Id).ToListAsync(ct))
            {
                string a = await uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == s.ItemId).Select(i => i.PartNumber ?? i.Code).FirstOrDefaultAsync(ct);
                string b = await uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == s.SubstituteItemId).Select(i => i.PartNumber ?? i.Code).FirstOrDefaultAsync(ct);
                list.Add(new AsBuiltDeviationDto { Number = "SUB-" + s.Id.ToString(CultureInfo.InvariantCulture), Kind = "Substitute", PartNumber = a + " ← " + b, Reason = s.Reason, Status = s.Status.ToString() });
            }
            return list;
        }
    }
}
