using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    /// <summary>The ageing of what the company owes its suppliers, from the general ledger (the payables note of the financial statements).</summary>
    public interface IPayableAgeingService
    {
        /// <summary>
        /// What is owed to each supplier on a day, in the buckets of the notes schedule: up to 30 days, 31 to 60, 61 to 90 and over 90 days from the date of the document.
        /// The books of the main entity up to the day are read (drafts left out); payments, returns and notes are matched against the oldest documents first (FIFO).
        /// A supplier advance is kept in the advance account, not in the payable account, so it does not reduce what is owed until a payment request recovers it.
        /// Suppliers owed nothing are not listed; the biggest debt comes first. Empty when there is no payable account or no fiscal year holds the day.
        /// There is no role check here: the caller (a finance report or the CEO overview) decides who may see the figures.
        /// </summary>
        Task<List<FnNoteAgeingDto>> PayableAgeingAsync(DateTime asOf, CancellationToken ct);
    }

    // Finance release F3b: comparative statements (this year against the previous one) and the notes schedules that go with the statements.
    public partial class GlService : IPayableAgeingService
    {
        private async Task<(FnFiscalYear Current, FnFiscalYear Prior, string Error)> YearPairAsync(int? fiscalYearId, CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            FnFiscalYear cur;
            if (fiscalYearId.HasValue) cur = await _uow.Set<FnFiscalYear>().AsNoTracking().FirstOrDefaultAsync(y => y.Id == fiscalYearId.Value, ct);
            else
            {
                int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
                DateTime today = DateTime.Today;
                cur = await _uow.Set<FnFiscalYear>().AsNoTracking().Where(y => y.LegalEntityId == entityId && y.StartDate <= today && y.EndDate >= today).FirstOrDefaultAsync(ct)
                    ?? await _uow.Set<FnFiscalYear>().AsNoTracking().Where(y => y.LegalEntityId == entityId).OrderByDescending(y => y.Year).FirstOrDefaultAsync(ct);
            }
            if (cur == null) return (null, null, "سال مالی پیدا نشد.");
            FnFiscalYear prior = await _uow.Set<FnFiscalYear>().AsNoTracking().FirstOrDefaultAsync(y => y.LegalEntityId == cur.LegalEntityId && y.Year == cur.Year - 1, ct);
            return (cur, prior, null);
        }

        public async Task<ResponseDto<FnComparativeDto>> ComparativeAsync(string kind, int? fiscalYearId, bool includeDraft, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnComparativeDto>(NotAllowed);
            string k = Clean(kind)?.ToLowerInvariant();
            bool balance = k == "balancesheet" || k == "balance-sheet";
            if (!balance && k != "incomestatement" && k != "income-statement") return Bad<FnComparativeDto>("نوع صورت باید BalanceSheet یا IncomeStatement باشد.");
            var pair = await YearPairAsync(fiscalYearId, ct);
            if (pair.Error != null) return Bad<FnComparativeDto>(pair.Error);
            async Task<ResponseDto<FinancialStatementDto>> Load(int yearId) =>
                balance ? await _reports.BalanceSheetAsync(new LedgerReportQuery { FiscalYearId = yearId, IncludeDraft = includeDraft }, ct) : await _reports.IncomeStatementAsync(new LedgerReportQuery { FiscalYearId = yearId, IncludeDraft = includeDraft }, ct);
            var current = await Load(pair.Current.Id);
            if (!current.Success) return Bad<FnComparativeDto>(current.Message);
            FinancialStatementDto prior = null;
            if (pair.Prior != null)
            {
                var p = await Load(pair.Prior.Id);
                if (!p.Success) return Bad<FnComparativeDto>(p.Message);
                prior = p.Data;
            }
            var dto = new FnComparativeDto
            {
                Kind = balance ? "BalanceSheet" : "IncomeStatement", FiscalYearId = pair.Current.Id, PriorFiscalYearId = pair.Prior?.Id, Year = pair.Current.Year, PriorYear = pair.Prior?.Year,
                AsOf = current.Data.To, PriorAsOf = prior?.To,
            };
            decimal Prev(string code) => prior?.Lines.Where(l => l.Code == code).Sum(l => l.Amount) ?? 0;
            foreach (StatementLineDto l in current.Data.Lines)
                dto.Rows.Add(ComparativeRow(l, l.Amount, Prev(l.Code)));
            if (prior != null)
                foreach (StatementLineDto old in prior.Lines.Where(pl => !current.Data.Lines.Any(c => c.Code == pl.Code)))
                    dto.Rows.Add(ComparativeRow(old, 0, old.Amount));
            dto.Note = pair.Prior == null ? "سال مالی قبل تعریف نشده است؛ ستون سال قبل صفر است." : null;
            return ResponseDtoHelper.Ok(dto);
        }

        private static FnComparativeRowDto ComparativeRow(StatementLineDto l, decimal current, decimal prior) => new FnComparativeRowDto
        {
            Code = l.Code, Title = l.Title, Section = l.Section, IsTotal = l.IsTotal, Current = current, Prior = prior, Change = current - prior,
            ChangePercent = prior == 0 ? (decimal?)null : Math.Round((current - prior) * 100m / Math.Abs(prior), 2, MidpointRounding.AwayFromZero),
        };

        // ------------------------------------------------------------------------------------------ notes schedules

        public async Task<List<FnNoteAgeingDto>> PayableAgeingAsync(DateTime asOf, CancellationToken ct)
        {
            FnAccount payable = await RoleAccountAsync(AccountRoles.AccountsPayable, "30101", ct);
            var list = new List<FnNoteAgeingDto>();
            FnFiscalYear year = await LedgerYearAsync(asOf, ct);
            if (payable == null || year == null) return list;
            var rows = await (await BookLinesAsync(asOf, false, ct))
                .Where(l => l.AccountId == payable.Id
                    && l.Detail1 != null && l.Detail1.Type == FnDetailType.Supplier)
                .Select(l => new { Supplier = l.Detail1.LinkedEntityId, Title = l.Detail1.Title, l.Voucher.Date, l.Voucher.Id, l.RowNumber, l.Debit, l.Credit }).ToListAsync(ct);
            foreach (var g in rows.GroupBy(r => r.Supplier))
            {
                // payments and notes are matched against the oldest invoices first (FIFO); what is left is aged from the invoice date
                decimal paid = g.Sum(r => r.Debit);
                var row = new FnNoteAgeingDto { Name = g.First().Title };
                foreach (var c in g.Where(r => r.Credit > 0).OrderBy(r => r.Date).ThenBy(r => r.Id).ThenBy(r => r.RowNumber))
                {
                    decimal open = c.Credit;
                    if (paid > 0) { decimal used = Math.Min(paid, open); paid -= used; open -= used; }
                    if (open <= 0) continue;
                    int days = (int)(asOf - c.Date.Date).TotalDays;
                    if (days <= 30) row.Current += open; else if (days <= 60) row.Days31To60 += open; else if (days <= 90) row.Days61To90 += open; else row.Over90 += open;
                }
                row.Total = row.Current + row.Days31To60 + row.Days61To90 + row.Over90;
                if (row.Total > 0) list.Add(row);
            }
            return list.OrderByDescending(r => r.Total).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
        }

        private async Task<Dictionary<string, (string Title, decimal Balance)>> InventoryByGroupAsync(DateTime asOf, CancellationToken ct)
        {
            FnFiscalYear year = await LedgerYearAsync(asOf, ct);
            if (year == null) return new Dictionary<string, (string Title, decimal Balance)>();
            var rows = await (await BookLinesAsync(asOf, false, ct))
                .Where(l => l.Account.Code.StartsWith("105"))
                .GroupBy(l => new { l.Account.Code, l.Account.Title }).Select(g => new { g.Key.Code, g.Key.Title, Balance = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct);
            return rows.ToDictionary(r => r.Code, r => (r.Title, r.Balance));
        }

        private async Task<(decimal Outstanding, decimal Current)> LoanBalanceAsync(FnLoan loan, DateTime day, CancellationToken ct)
        {
            if (!loan.DisbursedDate.HasValue || loan.DisbursedDate.Value.Date > day) return (0, 0);
            var inst = await _uow.Set<FnLoanInstallment>().AsNoTracking().Where(i => i.LoanId == loan.Id).Select(i => new { i.DueDate, i.Principal, i.Status, i.PaidDate }).ToListAsync(ct);
            decimal repaid = inst.Where(i => i.Status == 1 && i.PaidDate.HasValue && i.PaidDate.Value.Date <= day).Sum(i => i.Principal);
            DateTime horizon = LoanCal.AddMonths(day, 12);
            decimal current = inst.Where(i => !(i.Status == 1 && i.PaidDate.HasValue && i.PaidDate.Value.Date <= day) && i.DueDate.Date > day && i.DueDate.Date <= horizon).Sum(i => i.Principal);
            decimal outstanding = Math.Max(0, loan.Amount - repaid);
            return (outstanding, Math.Min(current, outstanding));
        }

        public async Task<ResponseDto<FnNotesDto>> NotesAsync(int? fiscalYearId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnNotesDto>(NotAllowed);
            var pair = await YearPairAsync(fiscalYearId, ct);
            if (pair.Error != null) return Bad<FnNotesDto>(pair.Error);
            DateTime today = DateTime.Today;
            DateTime asOf = pair.Current.EndDate.Date < today ? pair.Current.EndDate.Date : today;
            if (asOf < pair.Current.StartDate.Date) asOf = pair.Current.StartDate.Date;
            DateTime? priorAsOf = pair.Prior?.EndDate.Date;
            var dto = new FnNotesDto { FiscalYearId = pair.Current.Id, AsOf = asOf, PriorAsOf = priorAsOf };

            var receivables = await _receivables.GetAgeingAsync(asOf, ct);
            if (!receivables.Success) return Bad<FnNotesDto>(receivables.Message);
            dto.ReceivablesAgeing = receivables.Data;
            if (priorAsOf.HasValue)
            {
                var prior = await _receivables.GetAgeingAsync(priorAsOf, ct);
                if (prior.Success) dto.PriorReceivablesAgeing = prior.Data;
            }

            dto.Payables = await PayableAgeingAsync(asOf, ct);
            dto.PayablesTotal = dto.Payables.Sum(p => p.Total);
            dto.PriorPayablesTotal = priorAsOf.HasValue ? (await PayableAgeingAsync(priorAsOf.Value, ct)).Sum(p => p.Total) : 0;

            foreach (FnLoan l in await _uow.Set<FnLoan>().AsNoTracking().Where(x => x.LegalEntityId == pair.Current.LegalEntityId && x.DisbursedDate != null).OrderBy(x => x.Id).ToListAsync(ct))
            {
                var now = await LoanBalanceAsync(l, asOf, ct);
                decimal before = priorAsOf.HasValue ? (await LoanBalanceAsync(l, priorAsOf.Value, ct)).Outstanding : 0;
                if (now.Outstanding == 0 && before == 0) continue;
                dto.Loans.Add(new FnNoteLoanDto { Number = l.Number, BankName = l.BankName, Original = l.Amount, Outstanding = now.Outstanding, CurrentPortion = now.Current, LongTerm = now.Outstanding - now.Current, PriorOutstanding = before });
            }

            // the provisions note is built from the ledger accounts (review 3, M6), not from the provision register: bonus, leave, severance, doubtful debts and warranty
            string[] provisionCodes = { "30402", "30403", "40201", "10309", "30404", "30405", "30406" };
            foreach (string pc in provisionCodes)
            {
                FnAccount pa = await AccountByCodeAsync(pc, ct);
                if (pa == null) continue;
                decimal cur = await (await BookLinesAsync(asOf, false, ct)).Where(l => l.AccountId == pa.Id).SumAsync(l => (decimal?)(l.Credit - l.Debit), ct) ?? 0;
                decimal before = priorAsOf.HasValue ? await (await BookLinesAsync(priorAsOf.Value, false, ct)).Where(l => l.AccountId == pa.Id).SumAsync(l => (decimal?)(l.Credit - l.Debit), ct) ?? 0 : 0;
                if (cur == 0 && before == 0) continue;
                dto.Provisions.Add(new FnNoteProvisionDto { Number = pc, Description = pa.Title, Balance = cur, PriorBalance = before });
            }

            var inventory = await InventoryByGroupAsync(asOf, ct);
            var inventoryBefore = priorAsOf.HasValue ? await InventoryByGroupAsync(priorAsOf.Value, ct) : new Dictionary<string, (string Title, decimal Balance)>();
            foreach (string code in inventory.Keys.Union(inventoryBefore.Keys).OrderBy(c => c, StringComparer.Ordinal))
            {
                decimal cur = inventory.TryGetValue(code, out var a) ? a.Balance : 0, before = inventoryBefore.TryGetValue(code, out var b) ? b.Balance : 0;
                if (cur == 0 && before == 0) continue;
                dto.Inventory.Add(new FnNoteInventoryDto { AccountCode = code, Title = inventory.TryGetValue(code, out var t1) ? t1.Title : inventoryBefore[code].Title, Balance = cur, PriorBalance = before });
            }
            dto.InventoryTotal = dto.Inventory.Sum(i => i.Balance); dto.PriorInventoryTotal = dto.Inventory.Sum(i => i.PriorBalance);
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
