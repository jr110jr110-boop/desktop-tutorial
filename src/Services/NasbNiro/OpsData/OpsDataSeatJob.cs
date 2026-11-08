using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Ai;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.OpsData;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Entities.Model.NasbNiro.Office;

namespace Services.NasbNiro.OpsData
{
    /// <summary>
    /// OPS-DATA (06:15): re-runs the rules over what was confirmed in the last days and looks for what no single entry shows (a batch consumed more than it received over several documents,
    /// more hours in a day than a person has, the same quantity typed again and again). Rule-based, no model. It writes findings, one AI note and one in-app notification per (document, rule);
    /// a second run finds nothing new. It never approves and never edits a document.
    /// </summary>
    public class OpsDataSeatJob : IAiSeatJob
    {
        private readonly IUnitOfWork _uow;
        private readonly IOpsDataEvaluator _evaluator;
        private readonly IAiNoteWriter _notes;
        private readonly INotificationService _notify;
        private readonly IOpsReadinessService _readiness;

        public OpsDataSeatJob(IUnitOfWork uow, IOpsDataEvaluator evaluator, IAiNoteWriter notes, INotificationService notify, IOpsReadinessService readiness)
        {
            _uow = uow; _evaluator = evaluator; _notes = notes; _notify = notify; _readiness = readiness;
        }

        public string Code => AiSeatCodes.OpsData;

        private sealed class Candidate
        {
            public OpsDocRef Doc;
            public RuleFinding Finding;
        }

        public async Task<AiSeatJobResult> RunAsync(AiSeatRunContext context, CancellationToken ct)
        {
            DateTime asOf = context.AsOf.Date;
            IReadOnlyDictionary<string, decimal> st = await _evaluator.SettingsAsync(ct);
            if (st.TryGetValue(OpsRuleCatalog.MasterSwitch, out decimal master) && master == 0m) return AiSeatJobResult.Done("disabled");
            int lookback = (int)(st.TryGetValue(OpsRuleCatalog.SeatLookbackDays, out decimal lb) && lb > 0 ? lb : 7);
            int repeatCount = (int)(st.TryGetValue(OpsRuleCatalog.SeatRepeatCount, out decimal rc) && rc > 0 ? rc : 4);
            decimal dayHours = st.TryGetValue(OpsRuleCatalog.SeatDayHours, out decimal dh) && dh > 0 ? dh : 16m;
            DateTime from = asOf.AddDays(-lookback);
            DateTimeOffset fromOffset = new DateTimeOffset(from, TimeZoneInfo.Local.GetUtcOffset(from));

            var candidates = new List<Candidate>();

            // 1. the rules again over the documents confirmed in the window
            List<int> docIds = await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => !d.Deleted && d.Status == InventoryDocumentStatus.Confirmed && d.ConfirmedAt != null && d.ConfirmedAt >= fromOffset)
                .OrderBy(d => d.Id).Select(d => d.Id).Take(500).ToListAsync(ct);
            foreach (var kv in await _evaluator.EvaluateInventoryAsync(docIds, asOf, ct))
                foreach (RuleFinding f in kv.Value.Findings) candidates.Add(new Candidate { Doc = kv.Value.Doc, Finding = f });

            List<int> opIds = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.StartAt >= from && b.EndAt != null).Select(b => b.OperationId).Distinct().OrderBy(i => i).Take(300).ToListAsync(ct);
            foreach (int id in opIds)
            {
                OpsEvaluation ev = await _evaluator.EvaluateOperationAsync(id, null, ct);
                if (ev != null) foreach (RuleFinding f in ev.Findings) candidates.Add(new Candidate { Doc = ev.Doc, Finding = f });
            }
            List<int> invoiceIds = await _uow.Set<SupplierInvoice>().AsNoTracking().Where(i => !i.Deleted && i.InvoiceDate >= from).OrderBy(i => i.Id).Select(i => i.Id).Take(200).ToListAsync(ct);
            foreach (int id in invoiceIds)
            {
                OpsEvaluation ev = await _evaluator.EvaluateInvoiceAsync(id, ct);
                if (ev != null) foreach (RuleFinding f in ev.Findings) candidates.Add(new Candidate { Doc = ev.Doc, Finding = f });
            }

            // 2. what the gate cannot see at entry
            await LotOverdrawAsync(from, candidates, ct);
            await RepeatAsync(from, repeatCount, candidates, ct);
            await CapacityAsync(from, dayHours, candidates, ct);

            // a finding is stored once per (document, line, rule); the gate's own findings are not repeated
            var keys = candidates.Select(c => c.Doc.Type + "|" + c.Doc.Id).Distinct().ToList();
            List<string> types = candidates.Select(c => c.Doc.Type).Distinct().ToList();
            List<int> ids = candidates.Select(c => c.Doc.Id).Distinct().ToList();
            var existing = new HashSet<string>();
            if (ids.Count > 0)
            {
                var rows = await _uow.Set<OpsFinding>().AsNoTracking().Where(f => types.Contains(f.DocumentType) && ids.Contains(f.DocumentId)).Select(f => new { f.DocumentType, f.DocumentId, f.LineId, f.RuleCode }).ToListAsync(ct);
                foreach (var r in rows) existing.Add(r.DocumentType + "|" + r.DocumentId + "|" + (r.LineId?.ToString(CultureInfo.InvariantCulture) ?? "-") + "|" + r.RuleCode);
            }

            int created = 0, notes = 0;
            DateTimeOffset now = DateTimeOffset.Now;
            var fresh = new List<(OpsFinding Row, OpsDocRef Doc)>();
            foreach (Candidate c in candidates)
            {
                string key = c.Doc.Type + "|" + c.Doc.Id + "|" + (c.Finding.LineId?.ToString(CultureInfo.InvariantCulture) ?? "-") + "|" + c.Finding.RuleCode;
                if (!existing.Add(key)) continue;
                var row = new OpsFinding
                {
                    Unit = c.Doc.Unit, DocumentType = c.Doc.Type, DocumentId = c.Doc.Id, DocumentNumber = c.Doc.Number, LineId = c.Finding.LineId, RowNumber = c.Finding.RowNumber, ItemId = c.Finding.ItemId,
                    ItemTitle = c.Finding.ItemTitle, RuleCode = c.Finding.RuleCode, Severity = OpsSeverity.Warning, Source = OpsSource.Seat, Status = OpsFindingStatus.Open,
                    Message = Cut(c.Finding.Message, 1000), MessageMasked = Cut(c.Finding.MessageMasked, 1000), EnteredValue = c.Finding.Entered, ReferenceValue = c.Finding.Reference,
                    DeviationPercent = c.Finding.DeviationPercent, IsMoney = c.Finding.IsMoney, DetectedAt = now, CreateDate = now, ModifyDate = now,
                };
                _uow.Set<OpsFinding>().Add(row);
                fresh.Add((row, c.Doc));
                created++;
            }
            if (created > 0) await _uow.SaveChangesAsync(ct);

            foreach (var (row, doc) in fresh)
            {
                bool added = await _notes.TryAddAsync(new AiNote
                {
                    SeatCode = Code, TargetType = doc.Type, TargetId = doc.Id, RuleCode = row.RuleCode, Title = $"{OpsRuleCatalog.TitleOf(row.RuleCode)} — {OpsDocTypes.Title(doc.Type)} {doc.Number}",
                    Text = row.MessageMasked != null && row.IsMoney ? row.MessageMasked : row.Message, Verified = true, SeatRunId = context.RunId,
                }, ct);
                if (added) notes++;
                await _notify.NotifyAsync(new NotifyRequest
                {
                    SourceKey = "opsn:" + row.Id, Module = "opsdata", SourceType = "ops-finding", SourceId = row.Id, Kind = OfInboxKind.Task, Priority = OfUrgency.Normal,
                    Title = $"کنترل داده: {OpsRuleCatalog.TitleOf(row.RuleCode)} — {OpsDocTypes.Title(doc.Type)} {doc.Number}", Link = "/ops-data/quality", Roles = OpsRoles.UnitReaders(doc.Unit).Replace(",CEO", string.Empty).Replace(",AUDITOR", string.Empty),
                }, ct);
            }

            // findings of a document that was cancelled or deleted since are no longer open
            int swept = await SweepAsync(ct);

            string ready = await _readiness.DailyAsync(asOf, ct);
            return AiSeatJobResult.Done($"checked={docIds.Count + opIds.Count + invoiceIds.Count}; findings={created}; notes={notes}; swept={swept}; {ready}");
        }

        private static string Cut(string s, int n) => s == null ? null : (s.Length <= n ? s : s.Substring(0, n));

        private async Task<int> SweepAsync(CancellationToken ct)
        {
            List<OpsFinding> open = await _uow.Set<OpsFinding>().Where(f => !f.Deleted && f.Status == OpsFindingStatus.Open && f.DocumentType == OpsDocTypes.InventoryDocument).Take(500).ToListAsync(ct);
            if (open.Count == 0) return 0;
            List<int> ids = open.Select(f => f.DocumentId).Distinct().ToList();
            HashSet<int> alive = (await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => ids.Contains(d.Id) && !d.Deleted && d.Status != InventoryDocumentStatus.Cancelled).Select(d => d.Id).ToListAsync(ct)).ToHashSet();
            int n = 0;
            foreach (OpsFinding f in open.Where(f => !alive.Contains(f.DocumentId)))
            {
                f.Status = OpsFindingStatus.Superseded; f.ModifyDate = DateTimeOffset.Now; n++;
            }
            if (n > 0) await _uow.SaveChangesAsync(ct);
            return n;
        }

        // ---------------------------------------------------------------------------------------------- S-LOT-OVERDRAW

        private async Task LotOverdrawAsync(DateTime from, List<Candidate> into, CancellationToken ct)
        {
            List<int> lots = await _uow.Set<StockTransaction>().AsNoTracking().Where(t => t.LotId != null && t.TransactionDate >= from).Select(t => t.LotId.Value).Distinct().Take(500).ToListAsync(ct);
            if (lots.Count == 0) return;
            var sums = await _uow.Set<StockTransaction>().AsNoTracking().Where(t => t.LotId != null && lots.Contains(t.LotId.Value)).GroupBy(t => t.LotId.Value)
                .Select(g => new { Lot = g.Key, Net = g.Sum(x => x.Quantity), Received = g.Where(x => x.Quantity > 0).Sum(x => x.Quantity) }).ToListAsync(ct);
            foreach (var s in sums.Where(x => x.Net < -0.0001m))
            {
                var last = await _uow.Set<StockTransaction>().AsNoTracking().Where(t => t.LotId == s.Lot && t.Quantity < 0).OrderByDescending(t => t.Id).Select(t => new { t.DocumentId, t.ItemId, t.Document.Number }).FirstOrDefaultAsync(ct);
                if (last == null) continue;
                string title = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == last.ItemId).Select(i => i.Title).FirstOrDefaultAsync(ct);
                decimal consumed = s.Received - s.Net;
                into.Add(new Candidate
                {
                    Doc = new OpsDocRef { Type = OpsDocTypes.InventoryDocument, Id = last.DocumentId, Number = last.Number ?? last.DocumentId.ToString(CultureInfo.InvariantCulture), Unit = OpsUnit.Warehouse, Confirmed = true },
                    Finding = new RuleFinding
                    {
                        RuleCode = OpsRuleCodes.SeatLotOverdraw, Severity = OpsSeverity.Warning, LineId = -s.Lot, ItemId = last.ItemId, ItemTitle = title, Entered = Math.Round(consumed, 4), Reference = Math.Round(s.Received, 4),
                        DeviationPercent = s.Received > 0 ? Math.Round((consumed - s.Received) / s.Received * 100m, 2) : (decimal?)null,
                        Message = $"بچ شماره {s.Lot.ToString(CultureInfo.InvariantCulture)} ({title}): جمع مصرف در سندهای انبار {consumed:0.####} است ولی جمع دریافت فقط {s.Received:0.####} است. سندهای مصرف را با هم بررسی کنید.",
                        MessageMasked = $"بچ شماره {s.Lot.ToString(CultureInfo.InvariantCulture)}: مصرف از دریافت بیشتر است.",
                    },
                });
            }
        }

        // ---------------------------------------------------------------------------------------------- S-REPEAT

        private async Task RepeatAsync(DateTime from, int repeatCount, List<Candidate> into, CancellationToken ct)
        {
            DateTimeOffset f0 = new DateTimeOffset(from, TimeZoneInfo.Local.GetUtcOffset(from));
            var lines = await _uow.Set<InventoryDocumentLine>().AsNoTracking()
                .Where(l => l.Document.Status == InventoryDocumentStatus.Confirmed && !l.Document.Deleted && l.Document.ConfirmedAt != null && l.Document.ConfirmedAt >= f0 && l.Quantity > 0)
                .Select(l => new { l.Id, l.DocumentId, l.ItemId, l.Quantity, l.RowNumber, Creator = l.Document.CreatorUserId, Number = l.Document.Number })
                .OrderBy(l => l.Id).Take(5000).ToListAsync(ct);
            foreach (var g in lines.GroupBy(l => new { l.Creator, l.ItemId, l.Quantity }).Where(g => g.Select(x => x.DocumentId).Distinct().Count() >= repeatCount))
            {
                var last = g.OrderByDescending(x => x.Id).First();
                // a round, fixed quantity (a standard pack) typed on purpose is common; the rule only reports it once per item and user, on the newest line
                string title = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == g.Key.ItemId).Select(i => i.Title).FirstOrDefaultAsync(ct);
                int docs = g.Select(x => x.DocumentId).Distinct().Count();
                into.Add(new Candidate
                {
                    Doc = new OpsDocRef { Type = OpsDocTypes.InventoryDocument, Id = last.DocumentId, Number = last.Number ?? last.DocumentId.ToString(CultureInfo.InvariantCulture), Unit = OpsUnit.Warehouse, Confirmed = true },
                    Finding = new RuleFinding
                    {
                        RuleCode = OpsRuleCodes.SeatRepeat, Severity = OpsSeverity.Warning, LineId = last.Id, RowNumber = last.RowNumber, ItemId = g.Key.ItemId, ItemTitle = title, Entered = g.Key.Quantity,
                        Message = $"«{title}»: مقدار {g.Key.Quantity:0.####} توسط یک کاربر در {docs.ToString(CultureInfo.InvariantCulture)} سند متوالی ثبت شده است. اگر مقدار واقعی هر بار متفاوت بوده، سندها را اصلاح کنید.",
                        MessageMasked = $"«{title}»: یک مقدار در {docs.ToString(CultureInfo.InvariantCulture)} سند متوالی تکرار شده است.",
                    },
                });
            }
        }

        // ---------------------------------------------------------------------------------------------- S-CAPACITY

        private async Task CapacityAsync(DateTime from, decimal dayHours, List<Candidate> into, CancellationToken ct)
        {
            var bookings = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.StartAt >= from && b.EndAt != null && b.Kind != MfBookingKind.Waiting)
                .Select(b => new { b.Id, b.EmployeeId, b.OperationId, b.StartAt, b.Hours }).OrderBy(b => b.Id).Take(5000).ToListAsync(ct);
            foreach (var g in bookings.GroupBy(b => new { b.EmployeeId, Day = b.StartAt.Date }).Where(g => g.Sum(x => x.Hours) > dayHours))
            {
                var last = g.OrderByDescending(x => x.Id).First();
                decimal total = g.Sum(x => x.Hours);
                string number = await _uow.Set<MfOperation>().AsNoTracking().Where(o => o.Id == last.OperationId).Select(o => o.Title).FirstOrDefaultAsync(ct);
                into.Add(new Candidate
                {
                    Doc = new OpsDocRef { Type = OpsDocTypes.Operation, Id = last.OperationId, Number = number ?? last.OperationId.ToString(CultureInfo.InvariantCulture), Unit = OpsUnit.Production, Confirmed = true },
                    Finding = new RuleFinding
                    {
                        RuleCode = OpsRuleCodes.SeatCapacity, Severity = OpsSeverity.Warning, LineId = last.Id, Entered = Math.Round(total, 2), Reference = dayHours,
                        DeviationPercent = Math.Round((total - dayHours) / dayHours * 100m, 2),
                        Message = $"برای یک نفر در تاریخ {InventoryText.PersianDate(g.Key.Day)} جمعاً {total:0.##} ساعت کار ثبت شده، در حالی که سقف روز {dayHours:0.##} ساعت است. ثبت‌ها را بررسی کنید.",
                        MessageMasked = null,
                    },
                });
            }
        }
    }
}
