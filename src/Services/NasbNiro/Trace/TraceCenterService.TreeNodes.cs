using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Inventory;
using ViewModels.NasbNiro.TraceDtos;

namespace Services.NasbNiro.Trace
{
    public sealed partial class TraceCenterService
    {
        // ------------------------------------------------------------------------------------------ what hangs under a part

        private List<TraceNodeDto> PartDetails(TreeCtx c, string pn)
        {
            var list = new List<TraceNodeDto>();
            var itemIds = new HashSet<int>(c.Items.Where(kv => kv.Value.PartNumber == pn).Select(kv => kv.Key));
            var seedKeys = new HashSet<string>(c.SeedLots.Select(LotKey).Concat(c.SeedSerials.Select(SerialKey)));
            List<UnitInfo> units = c.Units.Values.Where(u => seedKeys.Contains(u.Key) && itemIds.Contains(u.ItemId)).OrderBy(u => u.IsLot ? 0 : 1).ThenBy(u => u.Number, StringComparer.Ordinal).ToList();
            List<OrderInfo> orders = c.Orders.Values.Where(o => o.ItemId.HasValue && itemIds.Contains(o.ItemId.Value)).OrderBy(o => o.Id).ToList();
            List<OpInfo> allOps = orders.SelectMany(o => c.OpsByOrder.TryGetValue(o.Id, out List<OpInfo> ops) ? ops : new List<OpInfo>()).ToList();
            var placed = new HashSet<string>();

            // ----- as designed: the OPC steps, each with the stock at that stage and what the shop floor did
            c.StepsByPart.TryGetValue(pn, out List<StepInfo> steps);
            foreach (StepInfo step in (steps ?? new List<StepInfo>()).OrderBy(s => s.Seq))
            {
                if (Full(c)) break;
                c.Stages.TryGetValue(step.StageCode ?? "", out string stageFa);
                TraceNodeDto n = Node(c, "stage", "asDesigned", $"{step.Seq}. {stageFa ?? step.StageCode} — {step.Title}", step.StageCode);
                // an operation has no reference to the OPC step it came from: the same title and sequence is the best match, the same title alone the next best
                bool SameTitle(OpInfo o) => string.Equals((o.Title ?? "").Trim(), (step.Title ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                OpInfo op = allOps.FirstOrDefault(o => o.Sequence == step.Seq && SameTitle(o)) ?? allOps.FirstOrDefault(SameTitle);
                if (op != null) { n.Status = TraceLabels.Operation(op.Status); n.Tone = TraceLabels.OperationTone(op.Status); n.Basis = "asBuilt"; }
                Fact(n, "نقطه کنترل کیفیت", step.IsQc ? "بله" : null);
                if (!string.IsNullOrWhiteSpace(step.Evidence))
                    Fact(n, "مدارک لازم", string.Join("، ", step.Evidence.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(e => TraceLabels.EvidenceFa.TryGetValue(e.Trim().ToUpperInvariant(), out string fa) ? fa : e.Trim())));
                if (step.Outside) Fact(n, "برون‌سپاری", "بله");
                if (step.Hours.HasValue) Fact(n, "زمان استاندارد (ساعت)", Num(step.Hours.Value));
                Fact(n, "مسیر ساخت", $"{step.RoutingTitle} (ویرایش {step.Revision})");
                if (step.StageItemId.HasValue && c.Items.TryGetValue(step.StageItemId.Value, out var stageItem))
                {
                    n.Code = stageItem.Code;
                    n.Link = new TraceLinkDto { Type = "item", Id = step.StageItemId, Number = stageItem.Code };
                    foreach (UnitInfo u in units.Where(u => u.ItemId == step.StageItemId.Value && !placed.Contains(u.Key)))
                    {
                        if (Full(c)) break;
                        placed.Add(u.Key);
                        n.Children.Add(UnitNode(c, u.Key, c.MaterialDepth, new HashSet<string>(), false, null));
                    }
                }
                list.Add(n);
            }

            // ----- as built: orders with their operations and QC
            foreach (OrderInfo o in orders)
            {
                if (Full(c)) break;
                TraceNodeDto n = Node(c, "order", "asBuilt", $"دستور تولید {o.Number}", ItemCode(c, o.ItemId ?? 0), TraceLabels.Order(o.Status), TraceLabels.OrderTone(o.Status));
                n.Quantity = o.Quantity; n.Unit = o.Unit;
                n.Link = new TraceLinkDto { Type = "mesOrder", Id = o.Id, Number = o.Number };
                if (c.OpsByOrder.TryGetValue(o.Id, out List<OpInfo> ops))
                    foreach (OpInfo op in ops)
                    {
                        TraceNodeDto m = Node(c, "operation", "asBuilt", $"{op.Sequence}. {op.Title}", null, TraceLabels.Operation(op.Status), TraceLabels.OperationTone(op.Status));
                        Fact(m, "تعداد سالم / ضایعات / اصلاح", $"{Num(op.Good)} / {Num(op.Scrap)} / {Num(op.Rework)}");
                        if (op.QcNumber != null) Fact(m, "رکورد QC", $"{op.QcNumber} — {(op.QcResult.HasValue ? TraceLabels.QcRecord(op.QcResult.Value) : "")}");
                        else if (op.QcAccepted) Fact(m, "QC", "تایید شده");
                        if (op.NcrNumber != null) Fact(m, "گزارش عدم انطباق", op.NcrNumber);
                        if (op.Concession) Fact(m, "ادامه با ارفاق", "بله (بر پایه NCR تاییدشده)");
                        if (op.IsOutside) Fact(m, "برون‌سپاری", "بله");
                        if (op.QcResult == Entities.Model.NasbNiro.Quality.QcResult.Reject || op.Status == Entities.Model.NasbNiro.Production.MfOperationStatus.Rejected) m.Tone = "bad";
                        else if (op.QcNumber != null && op.QcResult == Entities.Model.NasbNiro.Quality.QcResult.Accept) m.Tone = "ok";
                        m.Files = FilesOf(c, AttachmentOwnerType.ProductionOperation, op.Id);
                        n.Children.Add(m);
                    }
                list.Add(n);
            }

            // ----- the units that belong to no stage node
            foreach (UnitInfo u in units.Where(u => !placed.Contains(u.Key)))
            {
                if (Full(c)) break;
                placed.Add(u.Key);
                list.Add(UnitNode(c, u.Key, c.MaterialDepth, new HashSet<string>(), false, null));
            }

            // ----- NCRs of the part that name no lot
            foreach (NcrInfo ncr in c.Ncrs.Where(n => itemIds.Contains(n.ItemId) && !n.LotId.HasValue).OrderBy(n => n.Id))
            {
                if (Full(c)) break;
                list.Add(NcrNode(c, ncr, null));
            }
            return list;
        }

        // ------------------------------------------------------------------------------------------ a lot or serial piece

        private static string ViaText(InputEdge e, TreeCtx c)
        {
            switch (e.Via)
            {
                case "production": return c.Orders.TryGetValue(e.OrderId ?? 0, out OrderInfo o) ? $"مصرف‌شده در دستور {o.Number}" : "مصرف‌شده در دستور تولید";
                case "conversion": return "مرحله قبل (تبدیل مرحله)";
                case "assembly": return "نصب‌شده در مجموعه";
                case "lot": return "بچ / ذوب ماده این قطعه";
                default: return null;
            }
        }

        private TraceNodeDto UnitNode(TreeCtx c, string key, int depthLeft, HashSet<string> path, bool asMaterial, InputEdge edge)
        {
            UnitInfo u = c.Units[key];
            c.Items.TryGetValue(u.ItemId, out var item);
            if (u.Untracked)
            {
                TraceNodeDto raw = Node(c, "material", "asBuilt", item.Title ?? item.Code, item.Code, "بدون بچ و سریال", "warn");
                if (edge != null) { raw.Quantity = edge.Quantity > 0 ? edge.Quantity : (decimal?)null; Fact(raw, "نحوه ارتباط", ViaText(edge, c)); }
                Fact(raw, "نکته", "این کالا ردیابی بچ یا سریال ندارد؛ پیوند فقط در سطح کالاست.");
                return raw;
            }
            TraceNodeDto n = Node(c, asMaterial ? "material" : "unit", "asBuilt", u.IsLot ? "بچ " + u.Number : "سریال " + u.Number, item.Code, u.Status);
            n.Date = u.Date;
            n.Link = new TraceLinkDto { Type = u.IsLot ? "lot" : "serial", Id = u.Id, Number = u.Number };
            if (edge != null)
            {
                if (edge.Quantity > 0) n.Quantity = edge.Quantity;
                Fact(n, "نحوه ارتباط", ViaText(edge, c));
                if (edge.Shared) Fact(n, "نکته", "سهم برابر از مصرف دستور (چند سریال از یک دستور)");
            }
            Fact(n, "کالا", item.Title);
            Fact(n, "شماره ذوب", u.Heat);
            Fact(n, "شماره گواهی ماده", u.Cert);
            if (u.ProviderId.HasValue && c.Providers.TryGetValue(u.ProviderId.Value, out string provider)) Fact(n, "تامین‌کننده", provider);
            if (!u.IsLot && u.LotId.HasValue && c.Units.TryGetValue(LotKey(u.LotId.Value), out UnitInfo lot)) { Fact(n, "بچ", lot.Number); Fact(n, "شماره ذوب بچ", lot.Heat); }

            n.Files = FilesOf(c, u.IsLot ? AttachmentOwnerType.InventoryLot : AttachmentOwnerType.SerialUnit, u.Id);
            if (u.CertDocument.HasValue && n.Files.All(f => f.DocumentId != u.CertDocument.Value))
                n.Files.Add(new TraceFileDto { DocumentId = u.CertDocument.Value, Title = "گواهی ماده", FileName = "گواهی ماده", KindTitle = "گواهی", Legacy = true });

            if (Full(c)) { n.Truncated = true; return n; }

            // the operations done on it (traceability operations with their QC)
            foreach (PartOpInfo op in c.PartOps.Where(p => u.IsLot ? p.LotId == u.Id : p.SerialId == u.Id).OrderBy(p => p.Sequence).ThenBy(p => p.Id))
            {
                TraceNodeDto m = Node(c, "operation", "asBuilt", $"{op.Sequence}. {op.Title}", null, $"{TraceLabels.PartOperation(op.Status)} / {TraceLabels.Qc(op.Qc)}", TraceLabels.QcTone(op.Qc));
                m.Date = op.QcAt?.DateTime;
                Fact(m, "یادداشت QC", op.Note);
                m.Files = FilesOf(c, AttachmentOwnerType.PartOperation, op.Id);
                n.Children.Add(m);
            }

            // what it was made of
            if (depthLeft > 0 && c.Inputs.TryGetValue(key, out List<InputEdge> inputs))
            {
                var next = new HashSet<string>(path) { key };
                foreach (InputEdge e in inputs.OrderBy(e => e.Via).ThenBy(e => e.ToKey, StringComparer.Ordinal))
                {
                    if (next.Contains(e.ToKey) || !c.Units.ContainsKey(e.ToKey)) continue;
                    if (Full(c)) { n.Truncated = true; break; }
                    n.Children.Add(UnitNode(c, e.ToKey, depthLeft - 1, next, true, e));
                }
            }
            else if (depthLeft <= 0 && c.Inputs.ContainsKey(key)) n.Truncated = true;

            // where it came from (the purchase chain), its inspections and NCRs
            List<ReceiptRow> rows = c.Receipts.Where(r => u.IsLot ? r.LotId == u.Id : r.SerialUnitId == u.Id).ToList();
            foreach (var group in rows.GroupBy(r => r.DocumentId).OrderBy(g => g.First().DocumentDate))
            {
                if (Full(c)) { n.Truncated = true; break; }
                n.Children.Add(ReceiptNode(c, group.ToList(), u));
            }
            List<int> docIds = rows.Select(r => r.DocumentId).Distinct().ToList();
            foreach (InspInfo i in c.Inspections.Where(i => (u.IsLot && i.LotId == u.Id) || docIds.Contains(i.ReceiptDocumentId)).GroupBy(i => i.Id).Select(g => g.First()).OrderBy(i => i.Id))
                n.Children.Add(InspectionNode(c, i, rows.FirstOrDefault(r => r.DocumentId == i.ReceiptDocumentId)));
            foreach (NcrInfo ncr in c.Ncrs.Where(x => u.IsLot ? x.LotId == u.Id : (x.ItemId == u.ItemId && !string.IsNullOrEmpty(x.SerialNumbers) && x.SerialNumbers.Split(new[] { ',', ';', '؛', '،', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Any(s => string.Equals(s.Trim(), u.Number, StringComparison.OrdinalIgnoreCase)))).OrderBy(x => x.Id))
                n.Children.Add(NcrNode(c, ncr, rows.FirstOrDefault()));
            if (c.Deliveries.TryGetValue(key, out List<ReverseDeliveryDto> deliveries))
                foreach (ReverseDeliveryDto d in deliveries)
                {
                    TraceNodeDto m = Node(c, "delivery", "asBuilt", $"تحویل به مشتری {d.DocumentNumber}", d.ItemCode, "تحویل‌شده", "ok");
                    m.Date = d.DocumentDate; m.Quantity = d.Quantity;
                    m.Link = new TraceLinkDto { Type = "document", Id = d.DocumentId, Number = d.DocumentNumber };
                    Fact(m, "کارفرما", d.Customer); Fact(m, "قرارداد", d.ContractCode); Fact(m, "پروژه", d.WorkOrderCode); Fact(m, "مقصد", d.Destination);
                    n.Children.Add(m);
                }
            return n;
        }

        // ------------------------------------------------------------------------------------------ purchase chain, inspection, NCR

        private TraceNodeDto ReceiptNode(TreeCtx c, List<ReceiptRow> rows, UnitInfo unit)
        {
            ReceiptRow r = rows[0];
            decimal quantity = rows.Sum(x => x.Quantity);
            TraceNodeDto n = Node(c, "receipt", "asBuilt", $"رسید خرید {r.DocumentNumber}", r.DocumentNumber, "تاییدشده", "ok");
            n.Date = r.DocumentDate; n.Quantity = quantity;
            n.Link = new TraceLinkDto { Type = "document", Id = r.DocumentId, Number = r.DocumentNumber };
            n.DossierNumber = r.DocumentNumber;
            Fact(n, "تامین‌کننده", r.ProviderName);
            if (SeesPrices && r.LineUnitPrice.HasValue) Fact(n, "قیمت واحد رسید (ریال)", r.LineUnitPrice.Value.ToString("N0", CultureInfo.InvariantCulture));
            n.Files = FilesOf(c, AttachmentOwnerType.WarehouseDocument, r.DocumentId);
            n.Files.AddRange(RequestFilesOf(c, r));

            if (r.PoId.HasValue)
            {
                TraceNodeDto po = Node(c, "purchase", "asBuilt", $"سفارش خرید {r.PoNumber}", r.PoNumber, r.PoStatus, "info");
                po.Link = new TraceLinkDto { Type = "po", Id = r.PoId, Number = r.PoNumber };
                po.DossierNumber = r.PoNumber;
                Fact(po, "تامین‌کننده", r.ProviderName);
                if (SeesPrices && r.PoUnitPrice.HasValue) Fact(po, "قیمت واحد سفارش (ریال)", r.PoUnitPrice.Value.ToString("N0", CultureInfo.InvariantCulture));
                po.Files = FilesOf(c, AttachmentOwnerType.PurchaseOrder, r.PoId.Value);
                po.Files.AddRange(RequestFilesOf(c, r));
                if (r.InquiryId.HasValue)
                {
                    TraceNodeDto inq = Node(c, "inquiry", "asBuilt", $"استعلام قیمت {r.InquiryNumber}", r.InquiryNumber, r.InquiryStatus, "info");
                    inq.Link = new TraceLinkDto { Type = "inquiry", Id = r.InquiryId, Number = r.InquiryNumber };
                    inq.Files = FilesOf(c, AttachmentOwnerType.Inquiry, r.InquiryId.Value);
                    po.Children.Add(inq);
                }
                if (r.PrId.HasValue)
                {
                    TraceNodeDto pr = Node(c, "request", "asBuilt", $"درخواست خرید {r.PrNumber}", r.PrNumber, r.PrStatus, "info");
                    pr.Link = new TraceLinkDto { Type = "request", Id = r.PrId, Number = r.PrNumber };
                    pr.DossierNumber = r.PrNumber;
                    var own = new List<TraceFileDto>();
                    if (r.RequestLineId.HasValue && c.RequestLineFiles.TryGetValue(r.RequestLineId.Value, out List<TraceFileDto> lf)) own.AddRange(lf);
                    if (c.RequestFiles.TryGetValue(r.PrId.Value, out List<TraceFileDto> pf)) own.AddRange(pf);
                    pr.Files = own;
                    po.Children.Add(pr);
                }
                n.Children.Add(po);
            }
            return n;
        }

        private TraceNodeDto InspectionNode(TreeCtx c, InspInfo i, ReceiptRow receipt)
        {
            string tone = i.Status == Entities.Model.NasbNiro.Inventory.InspectionStatus.Pending ? "warn" : i.Rejected > 0 ? "bad" : "ok";
            TraceNodeDto n = Node(c, "inspection", "asBuilt", $"بازرسی ورودی {i.Number}", i.Number, TraceLabels.Inspection(i.Status), tone);
            n.Date = i.InspectedAt?.DateTime;
            n.Link = new TraceLinkDto { Type = "inspection", Id = i.Id, Number = i.Number };
            n.DossierNumber = i.Number;
            if (i.Status == Entities.Model.NasbNiro.Inventory.InspectionStatus.Done) Fact(n, "پذیرفته / ردشده", $"{Num(i.Accepted)} / {Num(i.Rejected)}");
            Fact(n, "شماره گزارش", i.ReportNumber);
            n.Files = FilesOf(c, AttachmentOwnerType.QcInspection, i.Id);
            n.Files.AddRange(RequestFilesOf(c, receipt));
            return n;
        }

        private TraceNodeDto NcrNode(TreeCtx c, NcrInfo ncr, ReceiptRow receipt)
        {
            string tone = ncr.Status == Entities.Model.NasbNiro.Inventory.NcrStatus.Closed || ncr.Status == Entities.Model.NasbNiro.Inventory.NcrStatus.Cancelled ? "info" : "bad";
            TraceNodeDto n = Node(c, "ncr", "asBuilt", $"گزارش عدم انطباق {ncr.Number}", ncr.Number, TraceLabels.Ncr(ncr.Status), tone);
            n.Date = ncr.Date;
            n.Link = new TraceLinkDto { Type = "ncr", Id = ncr.Id, Number = ncr.Number };
            n.DossierNumber = ncr.Number;
            Fact(n, "شدت", TraceLabels.Severity(ncr.Severity));
            Fact(n, "شرح", ncr.Description != null && ncr.Description.Length > 160 ? ncr.Description.Substring(0, 160) + "…" : ncr.Description);
            n.Files = FilesOf(c, AttachmentOwnerType.Ncr, ncr.Id);
            n.Files.AddRange(RequestFilesOf(c, receipt));
            return n;
        }
    }
}
