using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// R1t D6: the tie-out of a package to the ledger. The totals that the package carries (VAT of the invoices, the VAT return, the insurance and the income tax of the payroll) are compared, account by account,
    /// with what the ledger posted in the same period, so a package that no longer agrees with the books (an invoice changed after the package was prepared, a voucher not posted yet, a manual voucher on a tax account)
    /// shows the difference before it is approved or sent. The package side is read from the stored tables of the package, never from live data.
    /// </summary>
    public partial class SepidarOutboxService
    {
        /// <summary>
        /// The sources of the vouchers that settle a liability or collect a receivable (payment of the VAT, the tax or the insurance to the office, receipts, cheques, petty cash, bank statement lines).
        /// They move the account without being part of what the package reports, so they are left out of the comparison and shown on their own.
        /// </summary>
        internal static readonly string[] SettlementSources = { "FnPayment", "FnReceipt", "FnReceivedCheque", "FnIssuedCheque", "FnPettyCashSettlement", "FnBankStatement" };

        private sealed class TieSpec
        {
            public string Key, Label, Role, FallbackCode, Source;
            /// <summary>True when the account grows with debits (input VAT), false for liabilities that grow with credits.</summary>
            public bool DebitNormal;
            public decimal Package;
        }

        private async Task<FnAccount> LedgerAccountAsync(string role, string fallbackCode, CancellationToken ct)
        {
            int? mapped = await _uow.Set<AccountMapping>().AsNoTracking().Where(m => m.Role == role && m.IsActive && m.AccountId != null).Select(m => m.AccountId).FirstOrDefaultAsync(ct);
            if (mapped.HasValue)
            {
                FnAccount a = await _uow.Set<FnAccount>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == mapped.Value, ct);
                if (a != null && a.IsActive) return a;
            }
            return await _uow.Set<FnAccount>().AsNoTracking().FirstOrDefaultAsync(x => x.Code == fallbackCode && x.IsActive, ct);
        }

        /// <summary>Debits and credits of an account in a period over the vouchers that are not drafts, opening or closing vouchers; the main company's books only.</summary>
        private IQueryable<FnVoucherLine> PeriodLines(int accountId, DateTime from, DateTime to)
        {
            IQueryable<FnVoucherLine> q = _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.AccountId == accountId && l.Voucher.Status != FnVoucherStatus.Draft
                && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.Opening && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose
                && l.Voucher.Date >= from && l.Voucher.Date <= to && l.Voucher.FiscalYear.LegalEntityId == FinanceSeed.MainEntityId);
            return VoucherLineVisibility.Visible(q, VoucherLineVisibility.Sees(_user));
        }

        private static async Task<decimal> NetAsync(IQueryable<FnVoucherLine> lines, bool debitNormal, CancellationToken ct)
        {
            var sums = await lines.GroupBy(l => 1).Select(g => new { D = g.Sum(x => x.Debit), C = g.Sum(x => x.Credit) }).FirstOrDefaultAsync(ct);
            return sums == null ? 0 : debitNormal ? sums.D - sums.C : sums.C - sums.D;
        }

        public async Task<ResponseDto<OutboxTieOutDto>> TieOutAsync(int id, CancellationToken ct)
        {
            FnOutboxPackage p = await _uow.Set<FnOutboxPackage>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p == null) return ResponseDtoHelper.NotFound<OutboxTieOutDto>("بسته پیدا نشد.");
            if (!CanSee(p.Kind)) return Bad<OutboxTieOutDto>(NotAllowed);
            await FinanceSeed.EnsureAsync(_uow, ct);

            List<OutboxTableJson> tables = OutboxTables.Parse(p.PayloadJson);
            var specs = new List<TieSpec>();
            var dto = new OutboxTieOutDto { PackageId = p.Id, Number = p.Number, Kind = p.Kind, Status = p.Status, FromDate = p.FromDate, ToDate = p.ToDate };
            string sourceType;
            switch (p.Kind)
            {
                case FnOutboxKind.SalesInvoicesTaxpayer:
                {
                    sourceType = "FnSalesInvoice";
                    OutboxTableJson invoices = OutboxTables.Find(tables, "Invoices");
                    int type = OutboxTables.Col(invoices, "Type"), vat = OutboxTables.Col(invoices, "VatAmount");
                    decimal sales = 0;
                    if (invoices != null)
                        foreach (List<System.Text.Json.JsonElement> row in invoices.Rows)
                            sales += string.Equals(OutboxTables.Text(row, type), nameof(FnInvoiceType.CreditNote), StringComparison.Ordinal) ? -OutboxTables.Number(row, vat) : OutboxTables.Number(row, vat);
                    specs.Add(new TieSpec { Key = AccountRoles.VatSales, Label = "مالیات ارزش افزوده فروش (فاکتورها منهای اعلامیه‌های بستانکار)", Role = AccountRoles.VatSales, FallbackCode = "30205", Package = sales });
                    dto.Notes.Add("فاکتورهای فروش دارایی با مالیات در گزارش مالیات ارزش افزوده می‌آیند و در بسته مودیان نیستند؛ اگر در این دوره فروش دارایی بوده، تفاوت آن در ستون «سایر منابع» دیده می‌شود.");
                    break;
                }
                case FnOutboxKind.VatSeason:
                {
                    sourceType = "FnSalesInvoice";
                    OutboxTableJson summary = OutboxTables.Find(tables, "Summary");
                    specs.Add(new TieSpec { Key = AccountRoles.VatSales, Label = "مالیات ارزش افزوده فروش (اظهارنامه)", Role = AccountRoles.VatSales, FallbackCode = "30205", Package = OutboxTables.Sum(summary, "SalesVat") });
                    specs.Add(new TieSpec { Key = AccountRoles.Vat, Label = "مالیات ارزش افزوده خرید قابل کسر (اظهارنامه)", Role = AccountRoles.Vat, FallbackCode = "10403", DebitNormal = true, Source = "SupplierInvoice", Package = OutboxTables.Sum(summary, "PurchasesVat") });
                    break;
                }
                case FnOutboxKind.PayrollInsuranceList:
                {
                    sourceType = "FnPayrollRun";
                    OutboxTableJson workers = OutboxTables.Find(tables, "Workers");
                    decimal total = new[] { "EmployeeShare", "EmployerShare", "Unemployment", "HardAndHarmful" }.Sum(c => OutboxTables.Sum(workers, c));
                    specs.Add(new TieSpec { Key = AccountRoles.InsurancePayable, Label = "حق بیمه پرداختنی (سهم بیمه‌شده، سهم کارفرما، بیکاری، مشاغل سخت)", Role = AccountRoles.InsurancePayable, FallbackCode = "30202", Package = total });
                    if (p.WorkshopId.HasValue) dto.Notes.Add("این بسته مربوط به یک کارگاه است؛ حساب دفتر کل به تفکیک کارگاه نیست و مبلغ کل ماه را نشان می‌دهد.");
                    break;
                }
                default:
                {
                    sourceType = "FnPayrollRun";
                    specs.Add(new TieSpec { Key = AccountRoles.PayrollTaxPayable, Label = "مالیات حقوق پرداختنی", Role = AccountRoles.PayrollTaxPayable, FallbackCode = "30203", Package = OutboxTables.Sum(OutboxTables.Find(tables, "Summary"), "TotalTax") });
                    dto.Notes.Add("پاداش و تسویه پایان خدمت هم به حساب مالیات حقوق می‌روند و در لیست ماهانه نیستند؛ تفاوت آن‌ها در ستون «سایر منابع» دیده می‌شود.");
                    break;
                }
            }

            foreach (TieSpec spec in specs)
            {
                FnAccount account = await LedgerAccountAsync(spec.Role, spec.FallbackCode, ct);
                var row = new OutboxTieOutRowDto { Key = spec.Key, Label = spec.Label, AccountCode = account?.Code ?? spec.FallbackCode, AccountTitle = account?.Title, PackageAmount = spec.Package };
                if (account == null)
                {
                    dto.Notes.Add($"حساب {spec.FallbackCode} ({spec.Key}) در نقشه حساب‌ها پیدا نشد؛ مبلغ دفتر کل صفر فرض شد.");
                }
                else
                {
                    IQueryable<FnVoucherLine> lines = PeriodLines(account.Id, p.FromDate, p.ToDate);
                    IQueryable<FnVoucherLine> notSettlement = lines.Where(l => l.Voucher.SourceType == null || !SettlementSources.Contains(l.Voucher.SourceType));
                    row.LedgerAmount = await NetAsync(notSettlement, spec.DebitNormal, ct);
                    row.LedgerFromSource = await NetAsync(notSettlement.Where(l => l.Voucher.SourceType == sourceType || (spec.Source != null && l.Voucher.SourceType == spec.Source)), spec.DebitNormal, ct);
                    row.SettlementMovement = await NetAsync(lines.Where(l => l.Voucher.SourceType != null && SettlementSources.Contains(l.Voucher.SourceType)), spec.DebitNormal, ct);
                }
                row.LedgerOther = row.LedgerAmount - row.LedgerFromSource;
                row.Difference = row.LedgerAmount - row.PackageAmount;
                row.DifferenceFromSource = row.LedgerFromSource - row.PackageAmount;
                row.Ties = row.Difference == 0;
                dto.Rows.Add(row);
            }
            dto.Ties = dto.Rows.All(r => r.Ties);
            dto.TotalAbsoluteDifference = dto.Rows.Sum(r => Math.Abs(r.Difference));
            if (!dto.Ties) dto.Notes.Add("تفاوت مثبت یعنی دفتر کل بیشتر از بسته است. تا روشن شدن علت تفاوت، بسته را تایید یا ارسال نکنید.");
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
