using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    // Helpers for the vouchers the F3 processes make themselves (revaluation, allocation, close-out, deferrals ...):
    // type Auto, status Checked, prepared by the system (user 0); the finance manager finalises.
    public partial class GlService
    {
        private sealed class AutoLine
        {
            public FnAccount Account;
            public FnDetailAccount Detail1;
            public int? Detail1Id;
            public FnDetailAccount Detail2;
            public int? Detail2Id;
            public decimal Debit;
            public decimal Credit;
            public string Description;
            public string CurrencyCode;
            public decimal? CurrencyAmount;
            public decimal? Rate;
            public int? WorkOrderId;
            public int? ContractId;
            public int? CostCenterId;
        }

        /// <summary>Account of a role (mapped by finance) or, when unmapped, the seeded default code.</summary>
        private async Task<FnAccount> RoleAccountAsync(string role, string fallbackCode, CancellationToken ct)
        {
            int? mapped = await _uow.Set<AccountMapping>().AsNoTracking().Where(m => m.Role == role && m.IsActive && m.AccountId != null).Select(m => m.AccountId).FirstOrDefaultAsync(ct);
            if (mapped.HasValue)
            {
                FnAccount a = await _uow.Set<FnAccount>().FirstOrDefaultAsync(x => x.Id == mapped.Value, ct);
                if (a != null && a.IsActive) return a;
            }
            return await _uow.Set<FnAccount>().FirstOrDefaultAsync(x => x.Code == fallbackCode && x.IsActive, ct);
        }

        private async Task<FnAccount> AccountByCodeAsync(string code, CancellationToken ct)
        {
            string c = Digits(Clean(code) ?? "");
            return c.Length == 0 ? null : await _uow.Set<FnAccount>().FirstOrDefaultAsync(a => a.Code == c, ct);
        }

        /// <summary>The ledger account and bank detail of a bank account (the detail is created when it does not exist yet).</summary>
        private async Task<(FnAccount Account, FnDetailAccount Detail, FnBankAccount Bank)> BankLedgerAsync(int bankAccountId, DetailResolver details, CancellationToken ct)
        {
            FnBankAccount bank = await _uow.Set<FnBankAccount>().Include(b => b.Account).FirstOrDefaultAsync(b => b.Id == bankAccountId, ct);
            if (bank == null) return (null, null, null);
            return (bank.Account, await details.GetAsync(FnDetailType.Bank, bank.Id, ct), bank);
        }

        /// <summary>Adds (does not save) an Auto voucher for the entity on a date. The caller saves once.</summary>
        private async Task<(FnVoucher Voucher, string Error)> AddAutoVoucherAsync(int entityId, DateTime date, string description, string sourceType, string sourceNumber,
            List<AutoLine> lines, FnVoucherType type, FnVoucher reverses, CancellationToken ct)
        {
            var check = await CheckDateAsync(date.Date, entityId, ct);
            if (check.Error != null) return (null, check.Error);
            lines = lines.Where(l => R(l.Debit) != 0 || R(l.Credit) != 0).ToList();
            if (lines.Count < 2) return (null, "سند خودکار حداقل دو ردیف دارد.");
            decimal debit = lines.Sum(l => R(l.Debit)), credit = lines.Sum(l => R(l.Credit));
            if (debit != credit) return (null, $"سند خودکار متوازن نیست: بدهکار {debit:0} و بستانکار {credit:0}.");
            var voucher = new FnVoucher
            {
                FiscalYearId = check.Year.Id, Date = date.Date, Type = type, Status = FnVoucherStatus.Checked, Description = description.Length > 1000 ? description.Substring(0, 1000) : description,
                SourceType = sourceType, SourceNumber = sourceNumber, CreatedByUserId = 0, CreatedAt = DateTimeOffset.Now, CheckedByUserId = 0, CheckedAt = DateTimeOffset.Now,
                TemporaryNumber = await InventoryNumbers.NextAsync(_uow, FinanceNumbers.Voucher, check.Year.Year, ct), TotalAmount = debit, ReversesVoucher = reverses,
                Lines = new List<FnVoucherLine>(),
            };
            int row = 1;
            foreach (AutoLine l in lines)
            {
                voucher.Lines.Add(new FnVoucherLine
                {
                    RowNumber = row++, Account = l.Account, Detail1 = l.Detail1, Detail1Id = l.Detail1 == null ? l.Detail1Id : null, Detail2 = l.Detail2, Detail2Id = l.Detail2 == null ? l.Detail2Id : null,
                    Debit = R(l.Debit), Credit = R(l.Credit), Description = l.Description != null && l.Description.Length > 500 ? l.Description.Substring(0, 500) : l.Description,
                    CurrencyCode = l.CurrencyCode, CurrencyAmount = l.CurrencyAmount, Rate = l.Rate, WorkOrderId = l.WorkOrderId, ContractId = l.ContractId, CostCenterId = l.CostCenterId,
                });
            }
            _uow.Set<FnVoucher>().Add(voucher);
            return (voucher, null);
        }

        private static string PersianYearNumber(DateTime date) => FinanceSeed.PersianYearOf(date).ToString(CultureInfo.InvariantCulture);

        private async Task<string> NextNumberAsync(string prefix, int kind, DateTime date, CancellationToken ct)
        {
            int year = FinanceSeed.PersianYearOf(date);
            return prefix + "-" + year.ToString(CultureInfo.InvariantCulture) + "-" + (await InventoryNumbers.NextAsync(_uow, kind, year, ct)).ToString("D4", CultureInfo.InvariantCulture);
        }
    }
}
