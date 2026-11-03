using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Entities.Model.NasbNiro.Finance;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// Personal ledger lines (release R1f). The loan and advance receivable is kept per employee, so a voucher line whose detail account is an employee shows one person's money
    /// (the recovery of a payroll run, a loan that was paid out). PAYROLL, FINANCEMANAGER and the CEO read such lines as they are. Every other reader gets the personal lines of one account and
    /// one side of a voucher merged into a single anonymous line, so that no person can be told apart: the same idea as the protected groups of the labour postings. The totals never change.
    /// </summary>
    internal static class PersonalLedger
    {
        /// <summary>The title that takes the place of an employee detail account.</summary>
        public const string MaskedTitle = "کارمندان (محافظت‌شده)";
        /// <summary>The code suffix of the merged trial balance row (the detail code of an employee is EMP-n).</summary>
        public const string MaskedCode = "EMP-*";
        /// <summary>The description of a merged line.</summary>
        public const string MaskedNote = "ردیف‌های پرسنلی؛ جزئیات فقط برای حقوق و دستمزد، مدیر مالی و مدیرعامل";
        /// <summary>The refusal for an employee detail account asked for by name.</summary>
        public const string NotAllowed = "تفصیلی کارمند اطلاعات پرسنلی است و فقط برای حقوق و دستمزد، مدیر مالی و مدیرعامل در دسترس است.";

        /// <summary>Who reads personal ledger lines as they are.</summary>
        public static bool Sees(Inventory.IInventoryUserContext user) => user != null && user.IsInRole(FinanceRoles.PayrollReaders);

        /// <summary>
        /// The lines of a voucher for a reader who may not see persons: the lines that <paramref name="personal"/> flags are merged per account and side into one anonymous line placed where the first of them stood;
        /// the other lines keep their order and the rows are numbered again, so the number of rows does not tell how many persons there were.
        /// </summary>
        public static List<VoucherLineDto> Merge(IReadOnlyList<VoucherLineDto> lines, IReadOnlyList<bool> personal)
        {
            var result = new List<VoucherLineDto>();
            var merged = new Dictionary<(int AccountId, bool Debit), VoucherLineDto>();
            for (int i = 0; i < lines.Count; i++)
            {
                VoucherLineDto l = lines[i];
                if (!personal[i]) { result.Add(l); continue; }
                var key = (l.AccountId, l.Debit > 0);
                if (merged.TryGetValue(key, out VoucherLineDto into))
                {
                    into.Debit += l.Debit;
                    into.Credit += l.Credit;
                    continue;
                }
                into = new VoucherLineDto
                {
                    RowNumber = l.RowNumber, AccountId = l.AccountId, AccountCode = l.AccountCode, AccountTitle = l.AccountTitle,
                    Detail1Title = MaskedTitle, Debit = l.Debit, Credit = l.Credit, Description = MaskedNote, SourceRole = l.SourceRole,
                };
                merged[key] = into;
                result.Add(into);
            }
            for (int i = 0; i < result.Count; i++) result[i].RowNumber = i + 1;
            return result;
        }

        /// <summary>The first-detail party type of the employee lines of an accounting event.</summary>
        public const string EmployeeParty = "Employee";

        /// <summary>
        /// The lines of an accounting event for a reader who may not see persons (review M4): the employee-level lines (party type Employee: the loan and advance recoveries, the advances paid) are merged per account role
        /// and side into one anonymous line placed where the first of them stood; the rows are numbered again. The result is a new list: the merged lines are new objects, the other lines are the same objects as before (their row number is set again).
        /// </summary>
        public static List<AccountingEventLineDto> MergeEventLines(IReadOnlyList<AccountingEventLineDto> lines)
        {
            var result = new List<AccountingEventLineDto>();
            var merged = new Dictionary<(string Role, bool Debit), AccountingEventLineDto>();
            foreach (AccountingEventLineDto l in lines)
            {
                if (!string.Equals(l.PartyType, EmployeeParty, StringComparison.Ordinal)) { result.Add(l); continue; }
                var key = (l.AccountRole ?? "", l.Debit > 0);
                if (merged.TryGetValue(key, out AccountingEventLineDto into))
                {
                    into.Debit += l.Debit;
                    into.Credit += l.Credit;
                    continue;
                }
                into = new AccountingEventLineDto
                {
                    RowNumber = l.RowNumber, AccountRole = l.AccountRole, Debit = l.Debit, Credit = l.Credit, Description = MaskedNote, PartyType = l.PartyType,
                };
                merged[key] = into;
                result.Add(into);
            }
            for (int i = 0; i < result.Count; i++) result[i].RowNumber = i + 1;
            return result;
        }

        /// <summary>
        /// The same merge for the lines of an event as stored (the Sepidar voucher file): the rows come back numbered 1..n next to the line, the merged lines are new objects and the tracked originals are never changed.
        /// A reader who sees persons gets every line as it is.
        /// </summary>
        public static List<(int RowNumber, AccountingEventLine Line)> MergeEventLines(IEnumerable<AccountingEventLine> lines, bool seesPersons)
        {
            var result = new List<AccountingEventLine>();
            var merged = new Dictionary<(string Role, bool Debit), AccountingEventLine>();
            foreach (AccountingEventLine l in lines.OrderBy(x => x.RowNumber))
            {
                if (seesPersons || !string.Equals(l.PartyType, EmployeeParty, StringComparison.Ordinal)) { result.Add(l); continue; }
                var key = (l.AccountRole ?? "", l.Debit > 0);
                if (merged.TryGetValue(key, out AccountingEventLine into))
                {
                    into.Debit += l.Debit;
                    into.Credit += l.Credit;
                    continue;
                }
                into = new AccountingEventLine { RowNumber = l.RowNumber, AccountRole = l.AccountRole, Debit = l.Debit, Credit = l.Credit, Description = MaskedNote, PartyType = l.PartyType };
                merged[key] = into;
                result.Add(into);
            }
            var numbered = new List<(int, AccountingEventLine)>();
            for (int i = 0; i < result.Count; i++) numbered.Add((seesPersons ? result[i].RowNumber : i + 1, result[i]));
            return numbered;
        }

        /// <summary>True when the audit JSON of a voucher line names one of the given detail accounts as its first or second detail.</summary>
        public static bool MentionsDetail(string json, ICollection<int> detailIds)
        {
            if (string.IsNullOrWhiteSpace(json) || detailIds == null || detailIds.Count == 0) return false;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                foreach (string name in new[] { "Detail1Id", "Detail2Id" })
                    if (doc.RootElement.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int id) && detailIds.Contains(id)) return true;
            }
            catch (JsonException) { /* not a voucher line payload */ }
            return false;
        }

        /// <summary>One row of a ledger statement before the running balance is added.</summary>
        public sealed class Row
        {
            public DateTime Date { get; set; }
            public int VoucherId { get; set; }
            public int? Number { get; set; }
            public string AccountCode { get; set; }
            public string DetailTitle { get; set; }
            public string Description { get; set; }
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
            public bool Personal { get; set; }
        }

        /// <summary>The statement rows for a reader who may not see persons: the personal rows of one voucher, account and side become one anonymous row at the place of the first of them.</summary>
        public static List<Row> MergeRows(IReadOnlyList<Row> rows)
        {
            var result = new List<Row>();
            var merged = new Dictionary<(int VoucherId, string AccountCode, bool Debit), Row>();
            foreach (Row r in rows)
            {
                if (!r.Personal) { result.Add(r); continue; }
                var key = (r.VoucherId, r.AccountCode ?? "", r.Debit > 0);
                if (merged.TryGetValue(key, out Row into))
                {
                    into.Debit += r.Debit;
                    into.Credit += r.Credit;
                    continue;
                }
                into = new Row
                {
                    Date = r.Date, VoucherId = r.VoucherId, Number = r.Number, AccountCode = r.AccountCode, DetailTitle = MaskedTitle,
                    Description = MaskedNote, Debit = r.Debit, Credit = r.Credit, Personal = true,
                };
                merged[key] = into;
                result.Add(into);
            }
            return result;
        }
    }
}
