using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Services.NasbNiro.Inventory;

namespace Services.NasbNiro.Ai
{
    // FIN-ANOMALY: fixed rules in C#. No model is involved: the text of a finding is a template over the data, so it is always right and always "verified".

    public sealed class FinLineInput
    {
        /// <summary>The supplier, customer or person of the line (never an employee: payroll stays out).</summary>
        public int? CounterpartyId { get; set; }
        public string CounterpartyTitle { get; set; }
        public decimal Amount { get; set; }
        public bool Credit { get; set; }
    }

    public sealed class FinVoucherInput
    {
        public int Id { get; set; }
        public int? Number { get; set; }
        public int TemporaryNumber { get; set; }
        public DateTime Date { get; set; }
        public decimal TotalAmount { get; set; }

        /// <summary>A draft, a reversed voucher or a reversal / correction of one: it cannot be one half of a duplicate.</summary>
        public bool ExcludeFromDuplicate { get; set; }
        public List<FinLineInput> Lines { get; set; } = new List<FinLineInput>();
    }

    public sealed class FinBankLineInput
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }

        /// <summary>The larger of the withdrawal and the deposit.</summary>
        public decimal Amount { get; set; }
        public bool Withdrawal { get; set; }
    }

    public sealed class FinIbanChangeInput
    {
        /// <summary>The id of the finance audit row of the change: the note is about this change, so a second change gets a second note.</summary>
        public int AuditId { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public DateTime At { get; set; }
    }

    public sealed class FinFinding
    {
        public string TargetType { get; set; }
        public int TargetId { get; set; }

        /// <summary>DUPLICATE, ROUND, OFFDAY, IBAN or UNMATCHED.</summary>
        public string Rule { get; set; }
        public string Text { get; set; }
    }

    public sealed class FinNoteDraft
    {
        public string TargetType { get; set; }
        public int TargetId { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }

        /// <summary>One note per record and rule.</summary>
        public string RuleCode { get; set; }
        public List<string> Rules { get; set; } = new List<string>();
    }

    /// <summary>What FIN-ANOMALY must never read: the payroll (personal pay data stays with PAYROLL, FINANCEMANAGER and the CEO).</summary>
    public static class FinPayroll
    {
        /// <summary>The account roles of payroll, severance, bonus, leave and employee advances.</summary>
        public static readonly string[] AccountRoles =
        {
            "PayrollPayable", "InsurancePayable", "PayrollTaxPayable", "PayrollExpenseAdmin", "PayrollExpenseIndirect", "PayrollExpenseDirect", "EmployerInsuranceAdmin",
            "EmployerInsuranceProduction", "SeveranceExpense", "SeveranceProvision", "BonusProvision", "LeaveProvision", "EmployeeAdvance", "LabourAbsorbed", "ServiceLabourAbsorbed",
        };

        private static readonly string[] SourceWords = { "payroll", "termination", "severance", "bonus", "salary", "leave", "employee" };

        public static bool IsPayrollSource(string sourceType) =>
            !string.IsNullOrEmpty(sourceType) && SourceWords.Any(w => sourceType.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

        public static bool IsEmployee(Entities.Model.NasbNiro.Finance.FnDetailAccount detail) => detail != null && detail.Type == Entities.Model.NasbNiro.Finance.FnDetailType.Employee;
    }

    public static class FinAnomalyRules
    {
        public const int DuplicateDays = 7;
        public const int IbanDays = 30;
        public const int UnmatchedDays = 7;

        public static string Rial(decimal amount) => amount.ToString("N0", CultureInfo.InvariantCulture) + " ریال";

        public static string VoucherName(FinVoucherInput v) => v.Number.HasValue ? $"شماره {v.Number.Value.ToString(CultureInfo.InvariantCulture)}" : $"موقت شماره {v.TemporaryNumber.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>
        /// Applies the five rules. <paramref name="isOff"/> says whether a day is a Friday or a company holiday. A round amount is at least <paramref name="roundAmount"/> and a multiple of <paramref name="roundUnit"/>.
        /// </summary>
        public static List<FinFinding> Analyze(DateTime asOf, IReadOnlyList<FinVoucherInput> vouchers, IReadOnlyList<FinBankLineInput> bankLines, IReadOnlyList<FinIbanChangeInput> ibanChanges,
            Func<DateTime, bool> isOff, decimal roundAmount, decimal roundUnit)
        {
            var findings = new List<FinFinding>();
            vouchers ??= Array.Empty<FinVoucherInput>();

            // 1. the same amount to the same counterparty in two vouchers within seven days
            var flagged = new Dictionary<int, List<string>>();
            var groups = vouchers.Where(v => !v.ExcludeFromDuplicate).SelectMany(v => v.Lines.Where(l => l.CounterpartyId.HasValue && l.Amount > 0).Select(l => new { Voucher = v, Line = l }))
                .GroupBy(x => new { x.Line.CounterpartyId, x.Line.Credit, x.Line.Amount });
            foreach (var g in groups)
            {
                List<FinVoucherInput> distinct = g.Select(x => x.Voucher).GroupBy(v => v.Id).Select(x => x.First()).OrderBy(v => v.Date).ThenBy(v => v.Id).ToList();
                if (distinct.Count < 2) continue;
                string who = g.First().Line.CounterpartyTitle ?? "طرف حساب";
                for (int i = 0; i < distinct.Count; i++)
                    for (int j = 0; j < distinct.Count; j++)
                    {
                        if (i == j || Math.Abs((distinct[i].Date.Date - distinct[j].Date.Date).TotalDays) > DuplicateDays) continue;
                        // one finding per voucher and amount, naming the other voucher
                        string text = $"مبلغ {Rial(g.Key.Amount)} برای «{who}» در سند {VoucherName(distinct[i])} ({InventoryText.PersianDate(distinct[i].Date)}) و سند {VoucherName(distinct[j])} ({InventoryText.PersianDate(distinct[j].Date)}) تکرار شده است؛ احتمال ثبت یا پرداخت تکراری را بررسی کنید.";
                        if (!findings.Any(f => f.TargetType == AiNoteTypes.FinanceJournal && f.TargetId == distinct[i].Id && f.Rule == "DUPLICATE" && f.Text == text))
                            findings.Add(new FinFinding { TargetType = AiNoteTypes.FinanceJournal, TargetId = distinct[i].Id, Rule = "DUPLICATE", Text = text });
                    }
            }

            foreach (FinVoucherInput v in vouchers)
            {
                // 2. a large round amount
                if (roundUnit > 0 && v.TotalAmount >= roundAmount && v.TotalAmount % roundUnit == 0)
                    findings.Add(new FinFinding
                    {
                        TargetType = AiNoteTypes.FinanceJournal, TargetId = v.Id, Rule = "ROUND",
                        Text = $"مبلغ سند {VoucherName(v)} برابر {Rial(v.TotalAmount)} است؛ عددی رند و درشت که باید با مدرک پشتیبان تطبیق داده شود.",
                    });
                // 3. a posting dated on a Friday or a holiday
                if (isOff != null && isOff(v.Date.Date))
                    findings.Add(new FinFinding
                    {
                        TargetType = AiNoteTypes.FinanceJournal, TargetId = v.Id, Rule = "OFFDAY",
                        Text = $"سند {VoucherName(v)} با تاریخ {InventoryText.PersianDate(v.Date)} ثبت شده که {(v.Date.DayOfWeek == DayOfWeek.Friday ? "جمعه" : "روز تعطیل")} است.",
                    });
            }

            // 4. the IBAN of a supplier changed lately: one finding per change (the text never holds the IBAN)
            foreach (FinIbanChangeInput c in (ibanChanges ?? Array.Empty<FinIbanChangeInput>()).Where(c => c.At.Date <= asOf.Date && (asOf.Date - c.At.Date).TotalDays <= IbanDays).OrderBy(c => c.AuditId))
                findings.Add(new FinFinding
                {
                    TargetType = AiNoteTypes.SupplierIbanChange, TargetId = c.AuditId, Rule = "IBAN",
                    Text = $"شماره شبای تامین‌کننده «{c.ProviderName}» در تاریخ {InventoryText.PersianDate(c.At)} تغییر کرده است. پیش از پرداخت بعدی، تغییر را با تامین‌کننده تأیید کنید.",
                });

            // 5. a bank line that no voucher explains for more than seven days
            foreach (FinBankLineInput b in bankLines ?? Array.Empty<FinBankLineInput>())
                if ((asOf.Date - b.Date.Date).TotalDays > UnmatchedDays)
                    findings.Add(new FinFinding
                    {
                        TargetType = AiNoteTypes.BankLine, TargetId = b.Id, Rule = "UNMATCHED",
                        Text = $"ردیف صورت‌حساب بانکی به تاریخ {InventoryText.PersianDate(b.Date)} و مبلغ {Rial(b.Amount)} ({(b.Withdrawal ? "برداشت" : "واریز")}) بیش از {UnmatchedDays} روز است که با هیچ سندی تطبیق داده نشده است.",
                    });
            return findings;
        }

        /// <summary>One note per record and rule (the findings of one rule on one record are joined; a voucher with two rules gets two notes).</summary>
        public static List<FinNoteDraft> ToNotes(IEnumerable<FinFinding> findings, IReadOnlyDictionary<int, FinVoucherInput> vouchers = null)
        {
            var result = new List<FinNoteDraft>();
            foreach (var g in findings.GroupBy(f => new { f.TargetType, f.TargetId, f.Rule }).OrderBy(g => g.Key.TargetType, StringComparer.Ordinal).ThenBy(g => g.Key.TargetId).ThenBy(g => g.Key.Rule, StringComparer.Ordinal))
            {
                string title;
                switch (g.Key.TargetType)
                {
                    case AiNoteTypes.FinanceJournal:
                        title = vouchers != null && vouchers.TryGetValue(g.Key.TargetId, out FinVoucherInput v) ? "ناهنجاری در سند " + VoucherName(v) : "ناهنجاری در سند حسابداری";
                        break;
                    case AiNoteTypes.SupplierIbanChange: title = "تغییر شبای تامین‌کننده"; break;
                    default: title = "ردیف بانکی تطبیق‌نشده"; break;
                }
                var text = new StringBuilder();
                foreach (FinFinding f in g.OrderBy(f => f.Rule, StringComparer.Ordinal)) text.AppendLine("• " + f.Text);
                text.Append("این یادداشت فقط هشدار است؛ بررسی و اقدام با حسابداری است.");
                result.Add(new FinNoteDraft { TargetType = g.Key.TargetType, TargetId = g.Key.TargetId, Title = title, Text = text.ToString(), RuleCode = g.Key.Rule, Rules = new List<string> { g.Key.Rule } });
            }
            return result;
        }
    }

    /// <summary>A rough text similarity (share of common words) for finding similar past reports.</summary>
    public static class AiTextSimilarity
    {
        private static readonly Regex Word = new Regex(@"[\p{L}\p{N}]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static HashSet<string> Words(string text) =>
            new HashSet<string>(Word.Matches(AiText.YeKe(text ?? string.Empty).ToLowerInvariant()).Select(m => m.Value));

        public static double Jaccard(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;
            int common = a.Count(x => b.Contains(x));
            return common / (double)(a.Count + b.Count - common);
        }
    }
}
