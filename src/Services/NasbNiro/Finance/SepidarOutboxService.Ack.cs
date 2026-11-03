using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// R1t D6: the inbound side of the Sepidar door. The tax office answers each invoice of the taxpayer package with a unique tax id or a refusal; Sepidar hands the answer back as a small JSON or CSV file of
    /// rows <c>invoiceNo, taxId, status, message</c>. Importing it writes the tax id and the tax status on the invoices and moves the package to Accepted or Rejected, so the status of a package is evidence from the other side
    /// and not only a claim of the person who sent it (the manual marks stay, with a note and an audit row).
    /// </summary>
    public partial class SepidarOutboxService
    {
        /// <summary>The size of an answer file that is read (characters). A larger file is refused instead of being read into memory.</summary>
        internal const int AckMaxChars = 2000000;

        internal const int AckMaxRows = 20000;

        /// <summary>The values written in <c>FnSalesInvoice.TaxSentStatus</c>.</summary>
        internal static class TaxStatus
        {
            public const int NotSent = 0;
            public const int Sent = 1;
            public const int Accepted = 2;
            public const int Rejected = 3;
        }

        internal sealed class AckRow
        {
            /// <summary>The line of the file (1-based, counted after the header).</summary>
            public int Line { get; set; }
            public string InvoiceNo { get; set; }
            public string TaxId { get; set; }
            public int Status { get; set; }
            public string Message { get; set; }
        }

        // ================================================================================== reading the file

        /// <summary>The words (English, Persian, or the number of <see cref="TaxStatus"/>) that name an answer. Null when the word is not known.</summary>
        internal static int? ParseTaxStatus(string text)
        {
            string t = (text ?? string.Empty).Trim().ToLowerInvariant().Replace("‌", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
            switch (t)
            {
                case "accepted": case "accept": case "success": case "succeeded": case "approved": case "confirmed": case "ok": case "2":
                case "پذیرفته": case "پذیرفتهشده": case "تایید": case "تاییدشده": case "موفق":
                    return TaxStatus.Accepted;
                case "rejected": case "reject": case "failed": case "fail": case "error": case "denied": case "refused": case "3":
                case "رد": case "ردشده": case "ناموفق": case "خطا":
                    return TaxStatus.Rejected;
                case "sent": case "pending": case "inprogress": case "processing": case "queued": case "waiting": case "1":
                case "ارسالشده": case "درانتظار": case "درحالبررسی": case "درجریان":
                    return TaxStatus.Sent;
                default:
                    return null;
            }
        }

        private static string Norm(string name) => (name ?? string.Empty).Trim().ToLowerInvariant().Replace("‌", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);

        private static int FieldOf(string name)
        {
            switch (Norm(name))
            {
                case "invoiceno": case "invoicenumber": case "invoice": case "number": case "no": case "شمارهفاکتور": case "شمارهصورتحساب": return 0;
                case "taxid": case "uniquetaxid": case "taxnumber": case "moadianid": case "referencenumber": case "شناسهمالیاتی": case "شمارهمنحصربفردمالیاتی": case "شمارهپیگیری": return 1;
                case "status": case "state": case "result": case "وضعیت": case "نتیجه": return 2;
                case "message": case "msg": case "reason": case "description": case "error": case "پیام": case "توضیح": case "توضیحات": case "علت": return 3;
                default: return -1;
            }
        }

        /// <summary>
        /// Reads the answer file. The format is found from the first character: <c>[</c> or <c>{</c> is JSON (an array of rows, or an object with a "rows" array), anything else is CSV with a comma, a semicolon or a tab.
        /// A CSV header line is optional; without it the columns are invoiceNo, taxId, status, message. The problems found go in <paramref name="problems"/> (Persian text) and the rows that could be read are returned.
        /// </summary>
        internal static List<AckRow> ParseAck(string content, List<string> problems)
        {
            var rows = new List<AckRow>();
            string text = (content ?? string.Empty).TrimStart('﻿').Trim();
            if (text.Length == 0) { problems.Add("فایل یا متن پاسخ خالی است."); return rows; }
            List<string[]> raw;
            if (text[0] == '[' || text[0] == '{')
            {
                raw = ReadJsonRows(text, problems);
                if (raw == null) return rows;
            }
            else raw = ReadCsvRows(text);
            if (raw.Count > AckMaxRows) { problems.Add($"تعداد سطرهای پاسخ از {AckMaxRows} بیشتر است؛ فایل را تقسیم کنید."); return rows; }
            int line = 0;
            foreach (string[] cells in raw)
            {
                line++;
                string status = cells[2];
                int? parsed = ParseTaxStatus(status);
                string invoiceNo = Clean(cells[0]), taxId = Clean(cells[1]), message = Clean(cells[3]);
                if (string.IsNullOrEmpty(invoiceNo) && string.IsNullOrEmpty(taxId) && string.IsNullOrEmpty(status) && string.IsNullOrEmpty(message)) continue;
                if (parsed == null) { problems.Add($"سطر {line}: وضعیت «{status}» شناخته‌شده نیست (پذیرفته، ردشده یا در انتظار)."); continue; }
                rows.Add(new AckRow { Line = line, InvoiceNo = invoiceNo, TaxId = taxId, Status = parsed.Value, Message = message });
            }
            if (rows.Count == 0 && problems.Count == 0) problems.Add("پاسخ هیچ سطری ندارد.");
            return rows;
        }

        private static List<string[]> ReadJsonRows(string text, List<string> problems)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(text))
                {
                    JsonElement root = doc.RootElement;
                    IEnumerable<JsonElement> items;
                    if (root.ValueKind == JsonValueKind.Array) items = root.EnumerateArray().ToList();
                    else if (root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Any(p => Norm(p.Name) == "rows" || Norm(p.Name) == "items" || Norm(p.Name) == "invoices"))
                    {
                        JsonProperty list = root.EnumerateObject().First(p => Norm(p.Name) == "rows" || Norm(p.Name) == "items" || Norm(p.Name) == "invoices");
                        if (list.Value.ValueKind != JsonValueKind.Array) { problems.Add("فهرست سطرهای پاسخ باید آرایه باشد."); return null; }
                        items = list.Value.EnumerateArray().ToList();
                    }
                    else if (root.ValueKind == JsonValueKind.Object) items = new List<JsonElement> { root.Clone() };
                    else { problems.Add("قالب JSON پاسخ شناخته‌شده نیست."); return null; }
                    var result = new List<string[]>();
                    foreach (JsonElement item in items)
                    {
                        if (item.ValueKind != JsonValueKind.Object) { problems.Add("هر سطر پاسخ باید یک شیء JSON باشد."); return null; }
                        var cells = new string[4];
                        foreach (JsonProperty prop in item.EnumerateObject())
                        {
                            int f = FieldOf(prop.Name);
                            if (f < 0) continue;
                            cells[f] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ValueKind == JsonValueKind.Number ? prop.Value.GetRawText()
                                : prop.Value.ValueKind == JsonValueKind.True ? "true" : prop.Value.ValueKind == JsonValueKind.False ? "false" : null;
                        }
                        result.Add(cells);
                    }
                    return result;
                }
            }
            catch (JsonException)
            {
                problems.Add("JSON پاسخ خوانده نشد.");
                return null;
            }
        }

        private static List<string[]> ReadCsvRows(string text)
        {
            string firstLine = text.Split('\n')[0];
            char delimiter = ',';
            int best = firstLine.Count(c => c == ',');
            foreach (char d in new[] { ';', '\t' })
            {
                int n = firstLine.Count(c => c == d);
                if (n > best) { best = n; delimiter = d; }
            }
            List<List<string>> table = SplitCsv(text, delimiter);
            var map = new[] { 0, 1, 2, 3 };
            if (table.Count > 0 && table[0].Any(c => FieldOf(c) >= 0))
            {
                // a header line: the columns are named, in any order
                var named = new[] { -1, -1, -1, -1 };
                for (int c = 0; c < table[0].Count; c++)
                {
                    int f = FieldOf(table[0][c]);
                    if (f >= 0 && named[f] < 0) named[f] = c;
                }
                map = named;
                table.RemoveAt(0);
            }
            var result = new List<string[]>();
            foreach (List<string> cells in table)
            {
                var row = new string[4];
                for (int f = 0; f < 4; f++) row[f] = map[f] >= 0 && map[f] < cells.Count ? cells[map[f]] : null;
                result.Add(row);
            }
            return result;
        }

        /// <summary>A small CSV reader: quoted fields with doubled quotes and line breaks inside quotes; empty lines are skipped.</summary>
        internal static List<List<string>> SplitCsv(string text, char delimiter)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                    continue;
                }
                if (c == '"' && cell.Length == 0) quoted = true;
                else if (c == delimiter) { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear();
                    if (row.Any(x => x.Trim().Length > 0)) rows.Add(row);
                    row = new List<string>();
                }
                else cell.Append(c);
            }
            row.Add(cell.ToString());
            if (row.Any(x => x.Trim().Length > 0)) rows.Add(row);
            return rows;
        }

        // ================================================================================== writing the answer

        private static string StatusWord(int status) =>
            status == TaxStatus.Accepted ? "پذیرفته" : status == TaxStatus.Rejected ? "ردشده" : status == TaxStatus.Sent ? "ارسال‌شده و در انتظار پاسخ" : "ارسال‌نشده";

        public async Task<ResponseDto<OutboxAckResultDto>> AckAsync(int id, string content, CancellationToken ct)
        {
            FnOutboxPackage p = await _uow.Set<FnOutboxPackage>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p == null) return ResponseDtoHelper.NotFound<OutboxAckResultDto>("بسته پیدا نشد.");
            if (!CanSee(p.Kind) || !CanPrepare(p.Kind)) return Bad<OutboxAckResultDto>(NotAllowed);
            if (p.Status == FnOutboxStatus.Draft) return Bad<OutboxAckResultDto>("بسته هنوز تایید نشده است؛ پاسخ برای بسته‌ای که ارسال نشده ثبت نمی‌شود.");
            if (p.Status == FnOutboxStatus.Superseded) return Bad<OutboxAckResultDto>("این بسته با نسخه جدیدتر جایگزین شده است؛ پاسخ را روی بسته جاری ثبت کنید.");
            if (p.Status == FnOutboxStatus.Rejected) return Bad<OutboxAckResultDto>("این بسته ردشده است؛ بسته اصلاحی بسازید و پاسخ را روی آن ثبت کنید.");
            if (content != null && content.Length > AckMaxChars) return Bad<OutboxAckResultDto>("حجم فایل پاسخ از حد مجاز بیشتر است.");

            var problems = new List<string>();
            List<AckRow> rows = ParseAck(content, problems);
            if (problems.Count > 0) return Bad<OutboxAckResultDto>(ProblemText(problems));

            await FinanceSeed.EnsureAsync(_uow, ct);
            FnOutboxStatus before = p.Status;
            var result = new OutboxAckResultDto { PackageId = p.Id, Number = p.Number, Rows = rows.Count };
            string firstMessage = null, reference = null;
            var rejectedNumbers = new List<string>();
            int open;
            var planned = new List<(FnSalesInvoice Invoice, AckRow Row)>();
            Dictionary<string, FnSalesInvoice> invoices = null;
            int missing = 0;

            if (p.Kind == FnOutboxKind.SalesInvoicesTaxpayer)
            {
                List<string> numbers = OutboxTables.Column(OutboxTables.Find(OutboxTables.Parse(p.PayloadJson), "Invoices"), "Number").Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
                List<FnSalesInvoice> found = await _uow.Set<FnSalesInvoice>().Where(i => numbers.Contains(i.Number) && i.Date >= p.FromDate && i.Date <= p.ToDate).ToListAsync(ct);
                invoices = found.GroupBy(i => i.Number).ToDictionary(g => g.Key, g => g.OrderBy(i => i.Id).First(), StringComparer.Ordinal);
                missing = numbers.Count - invoices.Count;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                // first pass: every row is checked against what is recorded now; nothing is written unless all rows pass
                foreach (AckRow r in rows)
                {
                    if (string.IsNullOrEmpty(r.InvoiceNo)) { problems.Add($"سطر {r.Line}: شماره فاکتور خالی است."); continue; }
                    if (!seen.Add(r.InvoiceNo)) { problems.Add($"سطر {r.Line}: فاکتور {r.InvoiceNo} در پاسخ تکراری است."); continue; }
                    if (!invoices.TryGetValue(r.InvoiceNo, out FnSalesInvoice inv)) { problems.Add($"سطر {r.Line}: فاکتور {r.InvoiceNo} در این بسته نیست."); continue; }
                    if (r.Status == TaxStatus.Accepted)
                    {
                        if (string.IsNullOrEmpty(r.TaxId)) { problems.Add($"سطر {r.Line}: برای فاکتور پذیرفته‌شده {r.InvoiceNo} شناسه مالیاتی لازم است."); continue; }
                        if (r.TaxId.Length > 50 || !r.TaxId.All(ch => (ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))) { problems.Add($"سطر {r.Line}: شناسه مالیاتی فاکتور {r.InvoiceNo} فقط حرف و عدد لاتین تا ۵۰ نویسه است."); continue; }
                        if (inv.TaxSentStatus == TaxStatus.Accepted && !string.Equals(inv.TaxId, r.TaxId, StringComparison.Ordinal)) { problems.Add($"سطر {r.Line}: فاکتور {r.InvoiceNo} قبلاً با شناسه مالیاتی دیگری پذیرفته ثبت شده است؛ شناسه ثبت‌شده تغییر نمی‌کند."); continue; }
                        if (inv.TaxSentStatus == TaxStatus.Accepted) { result.Unchanged++; continue; }
                    }
                    else if (r.Status == TaxStatus.Rejected)
                    {
                        if (string.IsNullOrEmpty(r.Message)) { problems.Add($"سطر {r.Line}: برای فاکتور ردشده {r.InvoiceNo} علت رد (پیام) لازم است."); continue; }
                        if (inv.TaxSentStatus == TaxStatus.Accepted) { problems.Add($"سطر {r.Line}: فاکتور {r.InvoiceNo} قبلاً پذیرفته ثبت شده است و ردشده نمی‌شود."); continue; }
                        if (inv.TaxSentStatus == TaxStatus.Rejected) { result.Unchanged++; continue; }
                    }
                    else
                    {
                        if (inv.TaxSentStatus == TaxStatus.Accepted) { problems.Add($"سطر {r.Line}: فاکتور {r.InvoiceNo} قبلاً پذیرفته ثبت شده است."); continue; }
                        if (inv.TaxSentStatus == TaxStatus.Sent) { result.Unchanged++; continue; }
                    }
                    planned.Add((inv, r));
                }
                if (problems.Count > 0) return Bad<OutboxAckResultDto>(ProblemText(problems));

                // second pass: write the answers
                foreach (var (inv, r) in planned)
                {
                    int fromStatus = inv.TaxSentStatus;
                    string fromTaxId = inv.TaxId;
                    inv.TaxSentStatus = r.Status;
                    if (r.Status == TaxStatus.Accepted) inv.TaxId = r.TaxId;
                    firstMessage = firstMessage ?? r.Message;
                    if (r.Status == TaxStatus.Accepted) result.Accepted++;
                    else if (r.Status == TaxStatus.Rejected) result.Rejected++;
                    else result.Pending++;
                    AuditNotes.Add(_uow, Me, "FnSalesInvoice", inv.Id, "TaxAck", $"پاسخ سازمان مالیاتی برای فاکتور {inv.Number}: {StatusWord(r.Status)}" + (string.IsNullOrEmpty(r.Message) ? string.Empty : " — " + r.Message),
                        new { taxStatus = fromStatus, taxId = fromTaxId }, new { taxStatus = r.Status, taxId = inv.TaxId, package = p.Number, message = r.Message });
                }
                // the package is accepted when every invoice in it has been accepted (also by an earlier answer), rejected when all are answered and some refused, and stays sent while any is open
                // (an invoice cancelled after the package was prepared is not waited for)
                List<FnSalesInvoice> live = invoices.Values.Where(i => i.Status != FnDocStatus.Cancelled).ToList();
                open = live.Count(i => i.TaxSentStatus == TaxStatus.NotSent || i.TaxSentStatus == TaxStatus.Sent) + missing;
                result.Unanswered = live.Count(i => i.TaxSentStatus == TaxStatus.NotSent) + missing;
                rejectedNumbers.AddRange(live.Where(i => i.TaxSentStatus == TaxStatus.Rejected).Select(i => i.Number));
                if (open == 0 && rejectedNumbers.Count == 0) p.Status = FnOutboxStatus.Accepted;
                else if (open == 0) p.Status = FnOutboxStatus.Rejected;
                else p.Status = FnOutboxStatus.Sent;
            }
            else
            {
                // VAT season, insurance list and tax list: one answer for the whole package (the rows carry the status, the message and, in taxId, the tracking number)
                foreach (AckRow r in rows)
                {
                    if (r.Status == TaxStatus.Rejected && string.IsNullOrEmpty(r.Message)) { problems.Add($"سطر {r.Line}: برای پاسخ ردشده علت رد (پیام) لازم است."); continue; }
                    if (r.Status == TaxStatus.Accepted) result.Accepted++;
                    else if (r.Status == TaxStatus.Rejected) { result.Rejected++; firstMessage = firstMessage ?? r.Message; }
                    else result.Pending++;
                    reference = reference ?? r.TaxId;
                }
                if (problems.Count > 0) return Bad<OutboxAckResultDto>(ProblemText(problems));
                if (result.Rejected > 0) p.Status = FnOutboxStatus.Rejected;
                else if (result.Pending == 0 && result.Accepted > 0) p.Status = FnOutboxStatus.Accepted;
                else p.Status = FnOutboxStatus.Sent;
                open = p.Status == FnOutboxStatus.Sent ? 1 : 0;
            }

            if (planned.Count == 0 && p.Status == before && reference == null)
            {
                result.Status = p.Status;
                result.Message = "پاسخ تکراری بود؛ چیزی تغییر نکرد.";
                return ResponseDtoHelper.Ok(result);
            }

            if (before == FnOutboxStatus.Ready || before == FnOutboxStatus.Exported)
            {
                // an answer from the other side proves the package was sent, even when nobody marked it
                p.SentAt = DateTimeOffset.Now;
                if (string.IsNullOrEmpty(p.ExternalReference)) p.ExternalReference = "ack-import";
            }
            if (reference != null && reference.Length <= 200) p.ExternalReference = reference;
            if (p.Status == FnOutboxStatus.Rejected)
                p.Error = Clip("فاکتور یا بسته رد شد" + (rejectedNumbers.Count > 0 ? ": " + string.Join("، ", rejectedNumbers.Take(10)) + (rejectedNumbers.Count > 10 ? " و ..." : string.Empty) : string.Empty) + (firstMessage == null ? string.Empty : " — " + firstMessage), 2000);
            else p.Error = null;
            result.Status = p.Status;
            result.Message = p.Status == FnOutboxStatus.Accepted ? "همه پذیرفته شد؛ بسته پذیرفته‌شده است." : p.Status == FnOutboxStatus.Rejected ? "بسته ردشده ثبت شد؛ بسته اصلاحی بسازید." : $"پاسخ ثبت شد؛ بسته ارسال‌شده می‌ماند ({open} مورد منتظر پاسخ).";
            AuditNotes.Add(_uow, Me, "FnOutboxPackage", p.Id, "OutboxAck", $"پاسخ سازمان برای بسته {p.Number}: {result.Accepted} پذیرفته، {result.Rejected} ردشده، {result.Pending} در انتظار؛ وضعیت بسته {p.Status}.",
                new { status = before.ToString() }, new { status = p.Status.ToString(), accepted = result.Accepted, rejected = result.Rejected, pending = result.Pending, unanswered = result.Unanswered, reference = p.ExternalReference });
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<OutboxAckResultDto>(Conflict); }
            return ResponseDtoHelper.Ok(result);
        }

        private static string ProblemText(List<string> problems) =>
            "پاسخ ثبت نشد؛ " + string.Join(" ", problems.Take(5)) + (problems.Count > 5 ? $" و {problems.Count - 5} مورد دیگر." : string.Empty);

        private static string Clip(string text, int max) => text == null || text.Length <= max ? text : text.Substring(0, max);
    }
}
