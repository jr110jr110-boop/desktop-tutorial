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
        public static async Task<PcaResult> RunAsync(IUnitOfWork uow, int workOrderId, CancellationToken ct)
        {
            var result = new PcaResult();
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(uow, ct);
            if (st[QualitySettingKeys.PkbPcaEnabled] < 1) { result.NotApplicableReason = "ممیزی پیکربندی فیزیکی در تنظیمات غیرفعال است."; return result; }
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => b.WorkOrderId == workOrderId && b.IsCurrent && !b.Deleted).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            if (baseline == null) { result.NotApplicableReason = "برای این دستور کار BOM پروژه (baseline) ساخته نشده است."; return result; }
            result.Applicable = true;
            decimal tolerance = st[QualitySettingKeys.PkbPcaTolerance] / 100m;

            List<PbProjectBomLine> all = await uow.Set<PbProjectBomLine>().AsNoTracking().Where(l => l.BaselineId == baseline.Id && !l.Deleted && !l.IsRemoved && !l.IsHeading).ToListAsync(ct);
            var parents = new HashSet<int>(all.Where(l => l.ParentLineId.HasValue).Select(l => l.ParentLineId.Value));
            List<PbProjectBomLine> leaves = all.Where(l => !parents.Contains(l.Id) && l.TotalQty > 0).ToList();
            if (leaves.Count == 0) return result;

            // the items of the lines: by id, else by part number (the part number of the item or its code)
            List<string> partNumbers = leaves.Where(l => !l.ItemId.HasValue && l.PartNumber != null).Select(l => l.PartNumber.Trim()).Distinct().ToList();
            var byPart = (await uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && (partNumbers.Contains(i.PartNumber) || partNumbers.Contains(i.Code))).Select(i => new { i.Id, i.PartNumber, i.Code }).ToListAsync(ct))
                .ToList();
            int? ItemOf(PbProjectBomLine l)
            {
                if (l.ItemId.HasValue) return l.ItemId;
                string p = l.PartNumber?.Trim();
                if (p == null) return null;
                var hit = byPart.FirstOrDefault(i => string.Equals(i.PartNumber, p, StringComparison.OrdinalIgnoreCase)) ?? byPart.FirstOrDefault(i => string.Equals(i.Code, p, StringComparison.OrdinalIgnoreCase));
                return hit?.Id;
            }

            Dictionary<int, decimal> actual = await InstalledQuantitiesAsync(uow, workOrderId, ct);

            // approved substitutes (a stand-in item counts for the original at its ratio)
            List<int> itemIds = leaves.Select(ItemOf).Where(i => i.HasValue).Select(i => i.Value).Distinct().ToList();
            List<ItemSubstitute> subs = await uow.Set<ItemSubstitute>().AsNoTracking().Where(s => !s.Deleted && s.Status == SubstituteStatus.Approved && itemIds.Contains(s.ItemId)
                && (s.Scope == SubstituteScope.Global || s.WorkOrderId == workOrderId)).ToListAsync(ct);

            foreach (var group in leaves.GroupBy(l => ItemOf(l)?.ToString(CultureInfo.InvariantCulture) ?? "p:" + (l.PartNumber ?? "").Trim().ToUpperInvariant()))
            {
                PbProjectBomLine first = group.First();
                int? itemId = ItemOf(first);
                decimal required = group.Sum(l => l.TotalQty);
                var line = new PcaLineDto { PartNumber = first.PartNumber ?? "-", PartName = first.PartName, Required = required };
                if (!itemId.HasValue)
                {
                    line.Problem = "کالایی با این شماره قطعه در انبار تعریف نشده است؛ نصب یا مصرف آن قابل ردیابی نیست.";
                    result.Lines.Add(line);
                    continue;
                }
                decimal found = actual.TryGetValue(itemId.Value, out decimal own) ? own : 0m;
                foreach (ItemSubstitute s in subs.Where(x => x.ItemId == itemId.Value))
                    if (actual.TryGetValue(s.SubstituteItemId, out decimal viaSub) && s.Ratio > 0) found += viaSub / s.Ratio;
                line.Found = found;
                decimal low = required * (1m - tolerance), high = required * (1m + tolerance);
                if (found <= 0m) line.Problem = $"هیچ بچ یا سریالی از این قطعه در شجره‌نامه ساخت ثبت نشده است (لازم: {CharacteristicRules.Num(required)}).";
                else if (found < low) line.Problem = $"مقدار نصب/مصرف‌شده کمتر از BOM است (لازم: {CharacteristicRules.Num(required)}، یافت‌شده: {CharacteristicRules.Num(found)}).";
                else if (found > high) line.Problem = $"مقدار نصب/مصرف‌شده بیشتر از BOM است (لازم: {CharacteristicRules.Num(required)}، یافت‌شده: {CharacteristicRules.Num(found)}).";
                result.Lines.Add(line);
            }
            return result;
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
        public static async Task<List<AsBuiltRecord>> IssueAsync(IUnitOfWork uow, ReleaseNote note, WorkOrder wo, string reason, DateTimeOffset now, int userId, CancellationToken ct, int? onlyItemId = null, string onlySerial = null)
        {
            var created = new List<AsBuiltRecord>();
            List<ReleaseNoteLine> lines = await uow.Set<ReleaseNoteLine>().AsNoTracking().Include(l => l.Item).Where(l => l.ReleaseNoteId == note.Id).OrderBy(l => l.RowNumber).ToListAsync(ct);
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => b.WorkOrderId == wo.Id && b.IsCurrent && !b.Deleted).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            PcaResult pca = await PhysicalConfigurationAudit.RunAsync(uow, wo.Id, ct);
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
                    };
                    foreach (var g in links.Where(x => x.OutputItemId == line.ItemId && (serial == null || x.OutputSerial == null || string.Equals(x.OutputSerial, serial, StringComparison.OrdinalIgnoreCase))))
                        snap.Installed.Add(new AsBuiltInstalledDto { PartNumber = g.InputPart ?? g.InputCode, ItemCode = g.InputCode, ItemTitle = g.InputTitle, LotNumber = g.Lot, SerialNumber = g.Serial, HeatNumber = g.Heat, CertificateNumber = g.Cert, Quantity = g.Quantity, Via = "genealogy" });
                    foreach (var a in assemblies.Where(x => x.ParentItem == line.ItemId && (serial == null || string.Equals(x.Parent, serial, StringComparison.OrdinalIgnoreCase))))
                        snap.Installed.Add(new AsBuiltInstalledDto { PartNumber = a.ChildPart ?? a.ChildCode, ItemCode = a.ChildCode, ItemTitle = a.ChildTitle, LotNumber = a.Lot, SerialNumber = a.Serial, HeatNumber = a.Heat, CertificateNumber = a.Cert, Quantity = a.Quantity > 0 ? a.Quantity : 1m, Via = "assembly" });
                    snap.Installed = snap.Installed.OrderBy(i => i.PartNumber, StringComparer.Ordinal).ThenBy(i => i.LotNumber, StringComparer.Ordinal).ThenBy(i => i.SerialNumber, StringComparer.Ordinal).ToList();
                    string json = Serialize(snap);
                    AsBuiltRecord current = await uow.Set<AsBuiltRecord>().FirstOrDefaultAsync(r => !r.Deleted && r.WorkOrderId == wo.Id && r.ItemId == line.ItemId && r.SerialNumber == serial && r.Status == AsBuiltStatus.Current, ct);
                    int version = (await uow.Set<AsBuiltRecord>().Where(r => r.WorkOrderId == wo.Id && r.ItemId == line.ItemId && r.SerialNumber == serial).Select(r => (int?)r.Version).MaxAsync(ct) ?? 0) + 1;
                    if (current != null) { current.Status = AsBuiltStatus.Superseded; current.SupersededByVersion = version; }
                    var record = new AsBuiltRecord
                    {
                        WorkOrderId = wo.Id, ReleaseNoteId = note.Id, ItemId = line.ItemId, SerialNumber = serial, Version = version, Status = AsBuiltStatus.Current,
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
