using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcurementDtos;
using static Services.NasbNiro.Ceo.Drills.DrillText;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>The finance drills: treasury, receivables, payables, cheques, the ledger by month, project profitability, bank reconciliation, vouchers waiting.</summary>
    internal static class FinanceDrills
    {
        private const string M = CeoModules.Finance;

        public static IEnumerable<DrillDefinition> Build()
        {
            yield return CashAccounts();
            yield return ReceivablesByCustomer();
            yield return ReceivableInvoicesOverdue();
            yield return PayablesBySupplier();
            yield return PayablesOverdue();
            yield return ChequesDue();
            yield return LedgerByMonth("fin.revenue_by_month", "درآمد به تفکیک ماه", "درآمد ثبت‌شده در دفتر کل (حساب‌های درآمد، اسناد قطعی، بدون اسناد بستن حساب‌ها) در هر ماه شمسی؛ مجموع سال جاری با سود خالص داشبورد مالی هم‌پایه است.",
                FnAccountType.Revenue, "درآمد");
            yield return LedgerByMonth("fin.cost_by_month", "هزینه به تفکیک ماه", "هزینه‌ی ثبت‌شده در دفتر کل (حساب‌های هزینه، اسناد قطعی، بدون اسناد بستن حساب‌ها) در هر ماه شمسی.",
                FnAccountType.Expense, "هزینه");
            yield return ProjectProfitability();
            yield return BankUnreconciled();
            yield return VouchersWaiting();
        }

        private static string KindText(string kind) => kind switch
        {
            "Bank" => "حساب بانکی",
            "CashBox" => "صندوق",
            "PettyCash" => "تنخواه",
            _ => kind,
        };

        // ------------------------------------------------------------------------------------------ treasury

        private static DrillDefinition CashAccounts() => new DrillDefinition
        {
            Key = "fin.cash_accounts", Module = M, Title = "حساب‌های نقد و بانک",
            Description = "موجودی هر حساب بانکی، صندوق و تنخواه‌دار با جمع کل (از رویدادهای حسابداری)؛ بیشترین موجودی اول.",
            Link = "/fn/treasury/cash-position", RecordType = "CashAccount", RecordLabel = "حساب",
            Columns = new[] { Col.Text("title", "عنوان"), Col.Status("kind", "نوع"), Col.Rial("balance", "موجودی") },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                CashPositionDto position = Take(await c.Get<ITreasuryService>().GetCashPositionAsync(ct));
                List<Row> rows = position.Accounts.OrderByDescending(a => a.Balance).ThenBy(a => a.Title)
                    .Select(a => new Row { ["id"] = a.Kind + "-" + a.Id.ToString(CultureInfo.InvariantCulture), ["title"] = a.Title, ["kind"] = KindText(a.Kind), ["balance"] = a.Balance }).ToList();
                return DrillResult.Slice(rows, c, "«نقد و بانک» داشبورد = صندوق‌ها + حساب‌های بانکی (تنخواه جدا شمرده می‌شود).", new Dictionary<string, object>
                {
                    ["cash"] = position.Cash, ["banks"] = position.Banks, ["pettyCash"] = position.PettyCash, ["total"] = position.Total, ["cashAndBank"] = position.Cash + position.Banks,
                });
            },
        };

        // ------------------------------------------------------------------------------------------ receivables

        private static DrillDefinition ReceivablesByCustomer() => new DrillDefinition
        {
            Key = "fin.receivables_by_customer", Module = M, Title = "مطالبات به تفکیک مشتری",
            Description = "مانده‌ی باز فاکتورهای فروش هر مشتری به تفکیک سن (جاری تا ۳۰ روز، ۳۱ تا ۶۰، ۶۱ تا ۹۰ و بالای ۹۰ روز از سررسید)؛ بیشترین مانده اول.",
            Link = "/fn/receivables/ageing", RecordType = "Customer", RecordLabel = "مشتری",
            Columns = new[]
            {
                Col.Text("customerName", "مشتری"), Col.Rial("current", "جاری (تا ۳۰ روز)"), Col.Rial("days31To60", "۳۱ تا ۶۰ روز"), Col.Rial("days61To90", "۶۱ تا ۹۰ روز"),
                Col.Rial("over90", "بالای ۹۰ روز"), Col.Rial("total", "جمع مانده"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                AgeingDto ageing = Take(await c.Get<IReceivablesService>().GetAgeingAsync(null, ct));
                List<Row> rows = ageing.Rows.OrderByDescending(r => r.Total).ThenBy(r => r.CustomerName).Select(r => new Row("/fn/receivables/customers/" + r.CustomerId.ToString(CultureInfo.InvariantCulture) + "/statement")
                {
                    ["id"] = r.CustomerId, ["customerName"] = r.CustomerName, ["current"] = r.Current, ["days31To60"] = r.Days31To60, ["days61To90"] = r.Days61To90, ["over90"] = r.Over90, ["total"] = r.Total,
                }).ToList();
                return DrillResult.Slice(rows, c, "سن از سررسید فاکتور حساب می‌شود (بدون سررسید: از تاریخ فاکتور).", new Dictionary<string, object>
                {
                    ["current"] = ageing.Current, ["days31To60"] = ageing.Days31To60, ["days61To90"] = ageing.Days61To90, ["over90"] = ageing.Over90, ["total"] = ageing.Total,
                });
            },
        };

        private static DrillDefinition ReceivableInvoicesOverdue() => new DrillDefinition
        {
            Key = "fin.receivable_invoices_overdue", Module = M, Title = "فاکتورهای فروش سررسیدگذشته",
            Description = "فاکتورهای فروش قطعی‌شده که مانده‌ی باز دارند و سررسیدشان (بدون سررسید: تاریخ فاکتور) گذشته است؛ بیشترین تأخیر اول.",
            Link = "/fn/receivables/invoices", RecordType = "SalesInvoice", RecordLabel = "فاکتور فروش",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Text("customerName", "مشتری"), Col.Date("dueDate", "سررسید"), Col.Num("daysOverdue", "روز تأخیر"), Col.Rial("openAmount", "مانده‌ی باز"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                PagedList<SalesInvoiceDto> open = Take(await c.Get<IReceivablesService>().GetInvoicesAsync(new InvoiceQuery { OpenOnly = true, PageNumber = 1, PageSize = 0 }, ct));
                DateTime today = c.Today;
                var late = open.Where(i => i.OpenAmount > 0 && (i.DueDate ?? i.Date).Date < today)
                    .Select(i => new { Invoice = i, Due = (i.DueDate ?? i.Date).Date }).OrderBy(x => x.Due).ThenByDescending(x => x.Invoice.OpenAmount).ToList();
                List<Row> rows = late.Select(x => new Row("/fn/receivables/customers/" + x.Invoice.CustomerId.ToString(CultureInfo.InvariantCulture) + "/statement")
                {
                    ["id"] = x.Invoice.Id, ["number"] = x.Invoice.Number, ["customerName"] = x.Invoice.CustomerName, ["dueDate"] = DrillDates.Text(x.Due),
                    ["daysOverdue"] = (int)(today - x.Due).TotalDays, ["openAmount"] = x.Invoice.OpenAmount,
                }).ToList();
                return DrillResult.Slice(rows, c, "پیوند هر ردیف صورت‌حساب مشتری است.", new Dictionary<string, object> { ["openAmount"] = late.Sum(x => x.Invoice.OpenAmount) });
            },
        };

        // ------------------------------------------------------------------------------------------ payables

        private static DrillDefinition PayablesBySupplier() => new DrillDefinition
        {
            Key = "fin.payables_by_supplier", Module = M, Title = "بدهی به تفکیک تأمین‌کننده",
            Description = "فاکتورهای تأییدشده‌ی هر تأمین‌کننده منهای پرداخت‌های انجام‌شده بابت فاکتور؛ مانده‌ی بدهی باز، بیشترین اول (هم‌پایه با «بدهی باز» داشبورد مدیرعامل).",
            Link = "/procurement/invoices", RecordType = "Supplier", RecordLabel = "تأمین‌کننده",
            Columns = new[]
            {
                Col.Text("providerName", "تأمین‌کننده"), Col.Num("invoices", "تعداد فاکتور تأییدشده"), Col.Rial("invoiced", "فاکتورهای تأییدشده"), Col.Rial("paid", "پرداخت‌شده بابت فاکتور"),
                Col.Rial("open", "مانده‌ی بدهی"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, ProcurementRoles.Readers);
                var payables = c.Get<IPayablesService>();
                PagedList<SupplierInvoiceDto> invoices = Take(await payables.GetInvoicesAsync(new ProcurementListQuery { Status = (int)SupplierInvoiceStatus.Approved, PageNumber = 1, PageSize = 0 }, ct));
                PagedList<PaymentRequestDto> paid = Take(await payables.GetPaymentsAsync(new ProcurementListQuery { Status = (int)PaymentRequestStatus.Paid, PageNumber = 1, PageSize = 0 }, ct));
                var bySupplier = new Dictionary<int, (string Name, int Invoices, decimal Invoiced, decimal Paid)>();
                foreach (SupplierInvoiceDto i in invoices)
                {
                    bySupplier.TryGetValue(i.ProviderId, out var v);
                    bySupplier[i.ProviderId] = (i.ProviderName, v.Invoices + 1, v.Invoiced + i.TotalAmount, v.Paid);
                }
                foreach (PaymentRequestDto p in paid.Where(p => p.InvoiceId.HasValue))
                {
                    bySupplier.TryGetValue(p.ProviderId, out var v);
                    bySupplier[p.ProviderId] = (v.Name ?? p.ProviderName, v.Invoices, v.Invoiced, v.Paid + p.Amount);
                }
                List<Row> rows = bySupplier.Select(kv => new { Id = kv.Key, kv.Value.Name, kv.Value.Invoices, kv.Value.Invoiced, kv.Value.Paid, Open = kv.Value.Invoiced - kv.Value.Paid })
                    .Where(x => x.Open != 0).OrderByDescending(x => x.Open).ThenBy(x => x.Name)
                    .Select(x => new Row("/procurement/suppliers") { ["id"] = x.Id, ["providerName"] = x.Name, ["invoices"] = x.Invoices, ["invoiced"] = x.Invoiced, ["paid"] = x.Paid, ["open"] = x.Open }).ToList();
                return DrillResult.Slice(rows, c, "پرداخت بابت فاکتور فقط درخواست‌های پرداخت «پرداخت‌شده»ی دارای فاکتور است.", new Dictionary<string, object>
                {
                    ["invoiced"] = rows.Sum(r => (decimal)r["invoiced"]), ["paid"] = rows.Sum(r => (decimal)r["paid"]), ["open"] = rows.Sum(r => (decimal)r["open"]),
                });
            },
        };

        private static DrillDefinition PayablesOverdue() => new DrillDefinition
        {
            Key = "fin.payables_overdue", Module = M, Title = "بدهی سررسیدگذشته به تأمین‌کنندگان (سن بدهی)",
            Description = "سن بدهی به تأمین‌کنندگان از دفتر کل به تاریخ امروز، یک ردیف برای هر تأمین‌کننده: جاری (تا ۳۰ روز)، ۳۱ تا ۶۰، ۶۱ تا ۹۰ و بیش از ۹۰ روز. «سررسیدگذشته» جمع سه ستون بالای ۳۰ روز است و همان رقم سرفصل «بدهی سررسیدگذشته» داشبورد مدیرعامل است (جدول یادداشت‌های صورت‌های مالی)؛ پیش‌پرداخت و علی‌الحساب تا وصول با فاکتور در این رقم نیستند. بیشترین سررسیدگذشته اول.",
            Link = "/fn/financial-statements", RecordType = "SupplierAgeing", RecordLabel = "تأمین‌کننده",
            Columns = new[]
            {
                Col.Text("supplier", "تأمین‌کننده"), Col.Rial("current", "جاری (تا ۳۰ روز)"), Col.Rial("days31To60", "۳۱ تا ۶۰ روز"), Col.Rial("days61To90", "۶۱ تا ۹۰ روز"),
                Col.Rial("over90", "بیش از ۹۰ روز"), Col.Rial("overdue", "سررسیدگذشته"), Col.Rial("total", "جمع بدهی"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                // the one function behind the headline figure (CeoFigures): the payables ageing of the general ledger as of today
                List<CeoOverduePayable> ageing = await CeoFigures.OverduePayablesAsync(c.Get<IPayableAgeingService>(), c.Today, ct);
                List<Row> rows = ageing.Select(r => new Row("/fn/financial-statements")
                {
                    ["supplier"] = r.Supplier, ["current"] = r.Current, ["days31To60"] = r.Days31To60, ["days61To90"] = r.Days61To90, ["over90"] = r.Over90,
                    ["overdue"] = r.Overdue, ["total"] = r.Total,
                }).ToList();
                return DrillResult.Slice(rows, c, "سن بدهی از دفتر کل به تاریخ امروز؛ «سررسیدگذشته» = ۳۱ تا ۶۰ + ۶۱ تا ۹۰ + بیش از ۹۰ روز، هم‌پایه با رقم سرفصل داشبورد.",
                    new Dictionary<string, object> { ["overdue"] = ageing.Sum(r => r.Overdue), ["total"] = ageing.Sum(r => r.Total) });
            },
        };

        // ------------------------------------------------------------------------------------------ cheques

        private static DrillDefinition ChequesDue() => new DrillDefinition
        {
            Key = "fin.cheques_due", Module = M, Title = "چک‌های نزدیک به سررسید",
            Description = "چک‌های صادرشده (پیش‌فرض) یا دریافتی در صندوق که تا چند روز آینده سررسید دارند یا سررسیدشان گذشته است؛ نزدیک‌ترین سررسید اول.",
            Link = "/fn/treasury/cheques", RecordType = "Cheque", RecordLabel = "چک",
            Parameters = new[]
            {
                Prm.Str("kind", "نوع چک: issued (صادرشده) یا received (دریافتی در صندوق)؛ پیش‌فرض issued", "issued", new[] { "issued", "received" }),
                Prm.Days(7, 365, "چک‌هایی که تا این تعداد روز آینده سررسید دارند"),
            },
            Columns = new[]
            {
                Col.Text("serial", "شماره‌ی چک"), Col.Text("bank", "بانک / حساب"), Col.Text("party", "گیرنده / صادرکننده"), Col.Date("dueDate", "سررسید"),
                Col.Num("daysToDue", "روز تا سررسید (منفی: گذشته)"), Col.Rial("amount", "مبلغ"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                DateTime today = c.Today;
                DateTime limit = today.AddDays(c.Args.Int("days"));
                var treasury = c.Get<ITreasuryService>();
                var items = new List<(int Id, string Serial, string Bank, string Party, DateTime Due, decimal Amount)>();
                if (c.Args.Str("kind") == "received")
                {
                    PagedList<ReceivedChequeDto> list = Take(await treasury.GetReceivedChequesAsync(new TreasuryQuery { Kind = (int)FnReceivedChequeStatus.InBox, PageNumber = 1, PageSize = 0 }, ct));
                    items.AddRange(list.Where(x => x.DueDate.Date <= limit).Select(x => (x.Id, x.Serial, x.BankName, x.CustomerName ?? x.AccountHolder, x.DueDate.Date, x.Amount)));
                }
                else
                {
                    PagedList<IssuedChequeDto> list = Take(await treasury.GetIssuedChequesAsync(new IssuedChequeQuery { Status = FnIssuedChequeStatus.Issued, PageNumber = 1, PageSize = 0 }, ct));
                    items.AddRange(list.Where(x => x.DueDate.HasValue && x.DueDate.Value.Date <= limit)
                        .Select(x => (x.Id, ((x.Series ?? string.Empty) + " " + x.Serial.ToString(CultureInfo.InvariantCulture)).Trim(), x.BankAccountTitle, x.PayeeName, x.DueDate.Value.Date, x.Amount)));
                }
                List<Row> rows = items.OrderBy(x => x.Due).ThenBy(x => x.Id).Select(x => new Row("/fn/treasury/cheques")
                {
                    ["id"] = x.Id, ["serial"] = x.Serial, ["bank"] = x.Bank, ["party"] = x.Party, ["dueDate"] = DrillDates.Text(x.Due), ["daysToDue"] = (int)(x.Due - today).TotalDays, ["amount"] = x.Amount,
                }).ToList();
                return DrillResult.Slice(rows, c, "شامل چک‌هایی است که سررسیدشان گذشته و هنوز وصول نشده است.", new Dictionary<string, object> { ["amount"] = items.Sum(x => x.Amount) });
            },
        };

        // ------------------------------------------------------------------------------------------ the ledger by month

        private static DrillDefinition LedgerByMonth(string key, string title, string description, FnAccountType type, string amountTitle) => new DrillDefinition
        {
            Key = key, Module = M, Title = title, Description = description,
            Link = "/fn/reports/income-statement", RecordType = "LedgerMonth", RecordLabel = "ماه",
            Parameters = Prm.Last12Months(),
            Columns = new[] { Col.Text("month", "ماه"), Col.Rial("amount", amountTitle), Col.Pct("changePercent", "تغییر نسبت به ماه قبل") },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                DateTime from = c.Args.Date("from").Date, to = c.Args.Date("to").Date;
                var daily = await c.Get<IUnitOfWork>().Set<FnVoucherLine>().AsNoTracking()
                    .Where(l => l.Voucher.Date >= from && l.Voucher.Date <= to && l.Voucher.Status == FnVoucherStatus.Final &&
                                l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose &&
                                l.Voucher.FiscalYear.LegalEntityId == FinanceSeed.MainEntityId && l.Account.Type == type)
                    .GroupBy(l => l.Voucher.Date)
                    .Select(g => new { Date = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToListAsync(ct);
                Dictionary<DateTime, decimal> byMonth = daily.GroupBy(d => DrillDates.MonthStart(d.Date))
                    .ToDictionary(g => g.Key, g => type == FnAccountType.Revenue ? g.Sum(x => x.Credit - x.Debit) : g.Sum(x => x.Debit - x.Credit));
                var rows = new List<Row>();
                decimal? previous = null;
                foreach (DateTime month in DrillDates.Months(from, to))
                {
                    decimal amount = byMonth.TryGetValue(month, out decimal v) ? v : 0m;
                    rows.Add(new Row { ["month"] = DrillDates.MonthLabel(month), ["amount"] = amount, ["changePercent"] = previous.HasValue && previous.Value != 0m ? Percent(amount - previous.Value, Math.Abs(previous.Value)) : (decimal?)null });
                    previous = amount;
                }
                return DrillResult.Slice(rows, c, "ماه‌ها به‌ترتیب زمان؛ ماه ناقص (اول بازه یا امروز) فقط روزهای بازه را دارد.", new Dictionary<string, object> { ["total"] = rows.Sum(r => (decimal)r["amount"]) });
            },
        };

        // ------------------------------------------------------------------------------------------ projects

        private static DrillDefinition ProjectProfitability() => new DrillDefinition
        {
            Key = "fin.project_profitability", Module = M, Title = "سودآوری پروژه‌ها",
            Description = "درآمد، هزینه و حاشیه‌ی سود هر پروژه از اسناد دفتر کل؛ بیشترین درآمد اول. بازه‌ی پیش‌فرض: سال مالی جاری.",
            Link = "/fn/reports/project-profitability", RecordType = "Project", RecordLabel = "پروژه",
            Parameters = new[]
            {
                Prm.Date("from", "از تاریخ (میلادی یا شمسی)؛ پیش‌فرض ابتدای سال مالی جاری"),
                Prm.Date("to", "تا تاریخ (میلادی یا شمسی)؛ پیش‌فرض پایان سال مالی جاری"),
            },
            Columns = new[]
            {
                Col.Text("code", "کد پروژه"), Col.Text("title", "عنوان"), Col.Rial("revenue", "درآمد"), Col.Rial("cost", "هزینه"), Col.Rial("margin", "حاشیه‌ی سود"), Col.Pct("marginPercent", "درصد حاشیه"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                PagedList<ProjectProfitabilityRowDto> all = Take(await c.Get<ILedgerReportService>().ProjectProfitabilityAsync(
                    new LedgerReportQuery { From = c.Args.DateOrNull("from"), To = c.Args.DateOrNull("to"), PageNumber = 1, PageSize = 0 }, ct));
                List<Row> rows = all.OrderByDescending(r => r.Revenue).ThenBy(r => r.ContractCode).Select(r => new Row(r.ContractId.HasValue ? "/project-control/project?contract=" + r.ContractId.Value.ToString(CultureInfo.InvariantCulture) : null)
                {
                    ["id"] = r.ContractId, ["code"] = r.ContractCode, ["title"] = r.ContractTitle, ["revenue"] = r.Revenue, ["cost"] = r.Cost, ["margin"] = r.Margin, ["marginPercent"] = r.MarginPercent,
                }).ToList();
                return DrillResult.Slice(rows, c, "فقط اسنادی که به پروژه وصل شده‌اند شمرده می‌شوند (درآمد: حساب‌های ۶۰۱، هزینه: حساب‌های ۷).",
                    new Dictionary<string, object> { ["revenue"] = all.Sum(r => r.Revenue), ["cost"] = all.Sum(r => r.Cost), ["margin"] = all.Sum(r => r.Margin) });
            },
        };

        // ------------------------------------------------------------------------------------------ bank and vouchers

        private static DrillDefinition BankUnreconciled() => new DrillDefinition
        {
            Key = "fin.bank_unreconciled", Module = M, Title = "مغایرت‌گیری بانکی تأییدنشده",
            Description = "مغایرت‌گیری‌های بانکی که هنوز تأیید نشده‌اند، با مانده‌ی دفتر، مانده‌ی بانک و اختلاف؛ بیشترین اختلاف اول.",
            Link = "/fn/treasury/reconciliations", RecordType = "Reconciliation", RecordLabel = "مغایرت‌گیری",
            Columns = new[]
            {
                Col.Text("bankAccountTitle", "حساب بانکی"), Col.Date("asOfDate", "تا تاریخ"), Col.Rial("bookBalance", "مانده‌ی دفتر"), Col.Rial("bankBalance", "مانده‌ی بانک"),
                Col.Rial("difference", "اختلاف"), Col.Num("unmatchedLines", "ردیف بدون تطبیق"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                PagedList<ReconciliationDto> open = Take(await c.Get<ITreasuryService>().GetReconciliationsAsync(new TreasuryQuery { Kind = 0, PageNumber = 1, PageSize = 0 }, ct));
                List<Row> rows = open.OrderByDescending(r => Math.Abs(r.Difference)).ThenByDescending(r => r.Id).Select(r => new Row("/fn/treasury/reconciliations")
                {
                    ["id"] = r.Id, ["bankAccountTitle"] = r.BankAccountTitle, ["asOfDate"] = DrillDates.Text(r.AsOfDate), ["bookBalance"] = r.BookBalance, ["bankBalance"] = r.BankBalance,
                    ["difference"] = r.Difference, ["unmatchedLines"] = r.UnmatchedLines,
                }).ToList();
                return DrillResult.Slice(rows, c, "اختلاف کل در جمع، قدر مطلق اختلاف هر مغایرت‌گیری است.",
                    new Dictionary<string, object> { ["difference"] = open.Sum(r => Math.Abs(r.Difference)), ["unmatchedLines"] = open.Sum(r => r.UnmatchedLines) });
            },
        };

        private static DrillDefinition VouchersWaiting() => new DrillDefinition
        {
            Key = "fin.vouchers_waiting", Module = M, Title = "اسناد حسابداری منتظر",
            Description = "اسناد پیش‌نویس (منتظر بررسی) و بررسی‌شده (منتظر قطعی‌سازی)؛ قدیمی‌ترین اول.",
            Link = "/fn/vouchers", RecordType = "Voucher", RecordLabel = "سند",
            Columns = new[]
            {
                Col.Text("number", "شماره"), Col.Date("date", "تاریخ"), Col.Status("state", "وضعیت"), Col.Text("description", "شرح"), Col.Rial("amount", "مبلغ"),
                Col.Text("creatorName", "ثبت‌کننده"), Col.Num("daysWaiting", "روز انتظار"), Col.Link(),
            },
            Run = async (c, ct) =>
            {
                Guard.Require(c, FinanceRoles.Readers);
                var gl = c.Get<IGlService>();
                PagedList<VoucherDto> drafts = Take(await gl.GetVouchersAsync(new VoucherQuery { Status = FnVoucherStatus.Draft, PageNumber = 1, PageSize = 0 }, ct));
                PagedList<VoucherDto> checkedVouchers = Take(await gl.GetVouchersAsync(new VoucherQuery { Status = FnVoucherStatus.Checked, PageNumber = 1, PageSize = 0 }, ct));
                DateTime today = c.Today;
                List<Row> rows = drafts.Concat(checkedVouchers).OrderBy(v => v.Date).ThenBy(v => v.Id).Select(v => new Row("/fn/vouchers/" + v.Id.ToString(CultureInfo.InvariantCulture))
                {
                    ["id"] = v.Id, ["number"] = v.Number.HasValue ? v.Number.Value.ToString(CultureInfo.InvariantCulture) : "پیش‌نویس " + v.TemporaryNumber.ToString(CultureInfo.InvariantCulture),
                    ["date"] = DrillDates.Text(v.Date), ["state"] = v.Status == FnVoucherStatus.Draft ? "منتظر بررسی" : "منتظر قطعی‌سازی",
                    ["description"] = v.Description != null && v.Description.Length > 80 ? v.Description.Substring(0, 80) : v.Description, ["amount"] = v.TotalAmount, ["creatorName"] = v.CreatorName,
                    ["daysWaiting"] = Math.Max(0, (int)(today - v.Date.Date).TotalDays),
                }).ToList();
                return DrillResult.Slice(rows, c, "روز انتظار از تاریخ سند حساب می‌شود.",
                    new Dictionary<string, object> { ["waitingCheck"] = drafts.TotalCount, ["waitingFinalize"] = checkedVouchers.TotalCount, ["amount"] = drafts.Concat(checkedVouchers).Sum(v => v.TotalAmount) });
            },
        };
    }
}
