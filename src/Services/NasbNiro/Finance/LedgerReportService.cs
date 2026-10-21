using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface ILedgerReportService
    {
        Task<ResponseDto<PagedList<JournalDayDto>>> JournalAsync(LedgerReportQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<LedgerStatementDto>> GeneralLedgerAsync(LedgerReportQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<TrialBalanceDto>> TrialBalanceAsync(LedgerReportQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<LedgerStatementDto>> AccountReviewAsync(LedgerReportQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<FinancialStatementDto>> BalanceSheetAsync(LedgerReportQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<FinancialStatementDto>> IncomeStatementAsync(LedgerReportQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<ProjectProfitabilityRowDto>>> ProjectProfitabilityAsync(LedgerReportQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<LedgerReconciliationDto>> ReconcileAsync(int oldVoucherDays, CancellationToken cancellationToken);
        /// <summary>CSV (UTF-8 with BOM, opens in Excel) of one report.</summary>
        Task<(byte[] Content, string FileName, string Error)> ExportAsync(string report, LedgerReportQuery query, CancellationToken cancellationToken);
    }

    /// <summary>Read-only ledger reports. Every figure is computed from the vouchers; nothing is stored.</summary>
    public class LedgerReportService : ILedgerReportService
    {
        private readonly IUnitOfWork _uow;
        private readonly Inventory.IInventoryUserContext _user;

        public LedgerReportService(IUnitOfWork uow, Inventory.IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);

        private sealed class Range
        {
            public FnFiscalYear Year;
            public DateTime From;
            public DateTime To;
        }

        private async Task<(Range Range, string Error)> RangeAsync(LedgerReportQuery q, CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            FnFiscalYear year;
            if (q.FiscalYearId.HasValue) year = await _uow.Set<FnFiscalYear>().AsNoTracking().FirstOrDefaultAsync(y => y.Id == q.FiscalYearId.Value, ct);
            else
            {
                // the user's default legal entity (or the one asked for) decides which ledger is reported
                int entityId = await EntityScope.ResolveAsync(_uow, q.LegalEntityId, _user.UserId, ct);
                DateTime today = DateTime.Today;
                year = await _uow.Set<FnFiscalYear>().AsNoTracking().Where(y => y.LegalEntityId == entityId).FirstOrDefaultAsync(y => y.StartDate <= today && y.EndDate >= today, ct)
                    ?? await _uow.Set<FnFiscalYear>().AsNoTracking().Where(y => y.LegalEntityId == entityId).OrderByDescending(y => y.Year).FirstOrDefaultAsync(ct);
            }
            if (year == null) return (null, "سال مالی پیدا نشد.");
            DateTime from = q.From.HasValue ? q.From.Value.Date : year.StartDate;
            DateTime to = q.To.HasValue ? q.To.Value.Date : year.EndDate;
            if (from < year.StartDate) from = year.StartDate;
            if (to > year.EndDate) to = year.EndDate;
            if (to < from) return (null, "بازه تاریخ نامعتبر است.");
            return (new Range { Year = year, From = from, To = to }, null);
        }

        private IQueryable<FnVoucherLine> Lines(int yearId, bool includeDraft)
        {
            IQueryable<FnVoucherLine> q = _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.Voucher.FiscalYearId == yearId);
            if (!includeDraft) q = q.Where(l => l.Voucher.Status == FnVoucherStatus.Final);
            return q;
        }

        private sealed class Sum2
        {
            public int AccountId;
            public int? DetailId;
            public decimal Debit;
            public decimal Credit;
        }

        private static async Task<List<Sum2>> GroupAsync(IQueryable<FnVoucherLine> lines, CancellationToken ct)
        {
            var rows = await lines.GroupBy(l => new { l.AccountId, l.Detail1Id })
                .Select(g => new { g.Key.AccountId, g.Key.Detail1Id, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToListAsync(ct);
            return rows.Select(r => new Sum2 { AccountId = r.AccountId, DetailId = r.Detail1Id, Debit = r.Debit, Credit = r.Credit }).ToList();
        }

        // ================================================================================== journal

        public async Task<ResponseDto<PagedList<JournalDayDto>>> JournalAsync(LedgerReportQuery q, CancellationToken ct)
        {
            q ??= new LedgerReportQuery();
            var r = await RangeAsync(q, ct);
            if (r.Error != null) return Bad<PagedList<JournalDayDto>>(r.Error);
            IQueryable<FnVoucher> vouchers = _uow.Set<FnVoucher>().AsNoTracking()
                .Where(v => v.FiscalYearId == r.Range.Year.Id && v.Date >= r.Range.From && v.Date <= r.Range.To);
            if (!q.IncludeDraft) vouchers = vouchers.Where(v => v.Status == FnVoucherStatus.Final);
            List<DateTime> dates = await vouchers.Select(v => v.Date).Distinct().OrderBy(d => d).ToListAsync(ct);
            int size = q.PageSize <= 0 ? Math.Max(dates.Count, 1) : q.PageSize, number = q.PageNumber <= 0 ? 1 : q.PageNumber;
            List<DateTime> pageDates = dates.Skip((number - 1) * size).Take(size).ToList();

            var days = new List<JournalDayDto>();
            if (pageDates.Count > 0)
            {
                DateTime first = pageDates[0], last = pageDates[pageDates.Count - 1];
                var sums = await _uow.Set<FnVoucherLine>().AsNoTracking()
                    .Where(l => l.Voucher.FiscalYearId == r.Range.Year.Id && l.Voucher.Date >= first && l.Voucher.Date <= last && (q.IncludeDraft || l.Voucher.Status == FnVoucherStatus.Final))
                    .GroupBy(l => new { l.Voucher.Date, l.AccountId })
                    .Select(g => new { g.Key.Date, g.Key.AccountId, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToListAsync(ct);
                var accounts = await _uow.Set<FnAccount>().AsNoTracking().ToListAsync(ct);
                Dictionary<int, FnAccount> byId = accounts.ToDictionary(a => a.Id);
                var voucherRows = await vouchers.Where(v => v.Date >= first && v.Date <= last)
                    .Select(v => new { v.Date, v.Id, v.Number, v.TemporaryNumber, v.Description, v.TotalAmount }).ToListAsync(ct);
                foreach (DateTime d in pageDates)
                {
                    var day = new JournalDayDto { Date = d };
                    foreach (var g in sums.Where(s => s.Date == d).GroupBy(s => byId[s.AccountId].ParentId ?? s.AccountId))
                    {
                        FnAccount general = byId.TryGetValue(g.Key, out FnAccount ga) ? ga : null;
                        day.Accounts.Add(new JournalAccountDto { Code = general?.Code, Title = general?.Title, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) });
                    }
                    day.Accounts = day.Accounts.OrderBy(a => a.Code).ToList();
                    day.TotalDebit = day.Accounts.Sum(a => a.Debit);
                    day.TotalCredit = day.Accounts.Sum(a => a.Credit);
                    day.Vouchers = voucherRows.Where(v => v.Date == d).OrderBy(v => v.Number ?? int.MaxValue).ThenBy(v => v.TemporaryNumber)
                        .Select(v => new JournalVoucherDto { Number = v.Number, VoucherId = v.Id, Description = v.Description, Amount = v.TotalAmount }).ToList();
                    days.Add(day);
                }
            }
            PagedList<JournalDayDto> page = await PagedList<JournalDayDto>.CreateAsync(days, 1, 0, ct);
            page.TotalCount = dates.Count; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(dates.Count / (double)Math.Max(size, 1));
            return ResponseDtoHelper.Ok(page);
        }

        // ================================================================================== general ledger, account review

        public Task<ResponseDto<LedgerStatementDto>> GeneralLedgerAsync(LedgerReportQuery q, CancellationToken ct) => StatementAsync(q ?? new LedgerReportQuery(), false, ct);

        public Task<ResponseDto<LedgerStatementDto>> AccountReviewAsync(LedgerReportQuery q, CancellationToken ct) => StatementAsync(q ?? new LedgerReportQuery(), true, ct);

        private async Task<ResponseDto<LedgerStatementDto>> StatementAsync(LedgerReportQuery q, bool allowDetail, CancellationToken ct)
        {
            var r = await RangeAsync(q, ct);
            if (r.Error != null) return Bad<LedgerStatementDto>(r.Error);
            IQueryable<FnVoucherLine> lines = Lines(r.Range.Year.Id, q.IncludeDraft);
            string code = Digits(Clean(q.AccountCode));
            FnAccount account = null;
            if (q.AccountId.HasValue) account = await _uow.Set<FnAccount>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == q.AccountId.Value, ct);
            else if (code != null) account = await _uow.Set<FnAccount>().AsNoTracking().FirstOrDefaultAsync(a => a.Code == code, ct);
            FnDetailAccount detail = null;
            if (allowDetail && q.DetailId.HasValue)
            {
                detail = await _uow.Set<FnDetailAccount>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == q.DetailId.Value, ct);
                if (detail == null) return ResponseDtoHelper.NotFound<LedgerStatementDto>("تفصیلی پیدا نشد.");
            }
            if (account == null && detail == null) return Bad<LedgerStatementDto>(allowDetail ? "حساب یا تفصیلی را مشخص کنید." : "کد حساب کل را مشخص کنید.");
            if (account == null && q.AccountId.HasValue) return ResponseDtoHelper.NotFound<LedgerStatementDto>("حساب پیدا نشد.");

            if (account != null) { string prefix = account.Code; lines = lines.Where(l => l.Account.Code.StartsWith(prefix)); }
            if (detail != null) { int did = detail.Id; lines = lines.Where(l => l.Detail1Id == did || l.Detail2Id == did); }

            var openingSum = await lines.Where(l => l.Voucher.Date < r.Range.From).GroupBy(l => 1)
                .Select(g => new { Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).FirstOrDefaultAsync(ct);
            decimal opening = openingSum == null ? 0 : openingSum.Debit - openingSum.Credit;
            var rows = await lines.Where(l => l.Voucher.Date >= r.Range.From && l.Voucher.Date <= r.Range.To)
                .OrderBy(l => l.Voucher.Date).ThenBy(l => l.Voucher.Number ?? int.MaxValue).ThenBy(l => l.VoucherId).ThenBy(l => l.RowNumber)
                .Select(l => new
                {
                    l.Voucher.Date, l.VoucherId, l.Voucher.Number, AccountCode = l.Account.Code, DetailTitle = l.Detail1 == null ? null : l.Detail1.Title,
                    Description = l.Description ?? l.Voucher.Description, l.Debit, l.Credit,
                }).ToListAsync(ct);

            var dto = new LedgerStatementDto
            {
                Code = account?.Code ?? detail.Code, Title = account?.Title ?? detail.Title, Opening = opening,
                TotalDebit = rows.Sum(x => x.Debit), TotalCredit = rows.Sum(x => x.Credit),
            };
            dto.Closing = opening + dto.TotalDebit - dto.TotalCredit;
            decimal running = opening;
            var all = new List<LedgerRowDto>();
            foreach (var x in rows)
            {
                running += x.Debit - x.Credit;
                all.Add(new LedgerRowDto
                {
                    Date = x.Date, VoucherId = x.VoucherId, VoucherNumber = x.Number, AccountCode = x.AccountCode, DetailTitle = x.DetailTitle,
                    Description = x.Description, Debit = x.Debit, Credit = x.Credit, Balance = running,
                });
            }
            int size = q.PageSize <= 0 ? Math.Max(all.Count, 1) : q.PageSize, number = q.PageNumber <= 0 ? 1 : q.PageNumber;
            dto.TotalRows = all.Count; dto.PageNumber = number; dto.PageSize = size;
            dto.Rows = all.Skip((number - 1) * size).Take(size).ToList();
            return ResponseDtoHelper.Ok(dto);
        }

        // ================================================================================== trial balance

        public async Task<ResponseDto<TrialBalanceDto>> TrialBalanceAsync(LedgerReportQuery q, CancellationToken ct)
        {
            q ??= new LedgerReportQuery();
            var r = await RangeAsync(q, ct);
            if (r.Error != null) return Bad<TrialBalanceDto>(r.Error);
            if (q.Level < 1 || q.Level > 3) return Bad<TrialBalanceDto>("سطح تراز باید ۱ (کل)، ۲ (معین) یا ۳ (تفصیلی) باشد.");
            IQueryable<FnVoucherLine> lines = Lines(r.Range.Year.Id, q.IncludeDraft);
            if (q.ExcludeClosing) lines = lines.Where(l => l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose);
            List<Sum2> opening = await GroupAsync(lines.Where(l => l.Voucher.Date < r.Range.From), ct);
            List<Sum2> period = await GroupAsync(lines.Where(l => l.Voucher.Date >= r.Range.From && l.Voucher.Date <= r.Range.To), ct);
            Dictionary<int, FnAccount> byId = (await _uow.Set<FnAccount>().AsNoTracking().ToListAsync(ct)).ToDictionary(a => a.Id);
            Dictionary<int, FnDetailAccount> details = (await _uow.Set<FnDetailAccount>().AsNoTracking().ToListAsync(ct)).ToDictionary(d => d.Id);

            // key = (account or its general, detail) according to the level
            (int, int?) KeyOf(Sum2 s)
            {
                int account = q.Level == 1 ? (byId[s.AccountId].ParentId ?? s.AccountId) : s.AccountId;
                return (account, q.Level == 3 ? s.DetailId : null);
            }
            var table = new Dictionary<(int, int?), decimal[]>();   // opening net, debit, credit
            decimal[] Row((int, int?) key)
            {
                if (!table.TryGetValue(key, out decimal[] v)) { v = new decimal[3]; table[key] = v; }
                return v;
            }
            foreach (Sum2 s in opening) Row(KeyOf(s))[0] += s.Debit - s.Credit;
            foreach (Sum2 s in period) { decimal[] v = Row(KeyOf(s)); v[1] += s.Debit; v[2] += s.Credit; }

            var rows = new List<TrialBalanceRowDto>();
            foreach (var kv in table)
            {
                FnAccount a = byId[kv.Key.Item1];
                decimal openingNet = kv.Value[0], debit = kv.Value[1], credit = kv.Value[2];
                if (openingNet == 0 && debit == 0 && credit == 0) continue;
                decimal closingNet = openingNet + debit - credit;
                var row = new TrialBalanceRowDto
                {
                    Code = a.Code + (kv.Key.Item2.HasValue && details.ContainsKey(kv.Key.Item2.Value) ? "/" + details[kv.Key.Item2.Value].Code : ""),
                    Title = a.Title + (kv.Key.Item2.HasValue && details.ContainsKey(kv.Key.Item2.Value) ? " - " + details[kv.Key.Item2.Value].Title : ""),
                    Level = q.Level, Type = a.Type,
                };
                if (q.Columns >= 6) { row.OpeningDebit = openingNet > 0 ? openingNet : 0; row.OpeningCredit = openingNet < 0 ? -openingNet : 0; }
                row.Debit = debit; row.Credit = credit;
                if (q.Columns >= 4) { row.ClosingDebit = closingNet > 0 ? closingNet : 0; row.ClosingCredit = closingNet < 0 ? -closingNet : 0; }
                rows.Add(row);
            }
            rows = rows.OrderBy(x => x.Code, StringComparer.Ordinal).ToList();
            var dto = new TrialBalanceDto
            {
                FiscalYearId = r.Range.Year.Id, From = r.Range.From, To = r.Range.To, IncludesDraft = q.IncludeDraft,
                OpeningDebit = rows.Sum(x => x.OpeningDebit), OpeningCredit = rows.Sum(x => x.OpeningCredit),
                TotalDebit = rows.Sum(x => x.Debit), TotalCredit = rows.Sum(x => x.Credit),
                ClosingDebit = rows.Sum(x => x.ClosingDebit), ClosingCredit = rows.Sum(x => x.ClosingCredit),
            };
            dto.Balanced = dto.OpeningDebit == dto.OpeningCredit && dto.TotalDebit == dto.TotalCredit && dto.ClosingDebit == dto.ClosingCredit;
            if (q.PageSize > 0)
            {
                int number = q.PageNumber <= 0 ? 1 : q.PageNumber;
                dto.Rows = rows.Skip((number - 1) * q.PageSize).Take(q.PageSize).ToList();
            }
            else dto.Rows = rows;
            return ResponseDtoHelper.Ok(dto);
        }

        // ================================================================================== financial statements

        private async Task<Dictionary<string, decimal>> LineBalancesAsync(Range range, bool includeDraft, bool balanceSheet, CancellationToken ct)
        {
            IQueryable<FnVoucherLine> lines = Lines(range.Year.Id, includeDraft).Where(l => l.Voucher.Date <= range.To);
            if (balanceSheet) lines = lines.Where(l => l.Voucher.Type != FnVoucherType.Closing);
            else lines = lines.Where(l => l.Voucher.Date >= range.From && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose);
            var sums = await GroupAsync(lines, ct);
            Dictionary<int, FnAccount> byId = (await _uow.Set<FnAccount>().AsNoTracking().ToListAsync(ct)).ToDictionary(a => a.Id);
            var result = new Dictionary<string, decimal>();
            foreach (Sum2 s in sums)
            {
                FnAccount a = byId[s.AccountId];
                if (a.Type == FnAccountType.Memorandum) continue;
                string key = a.StatementLine ?? (balanceSheet ? "BS.UNMAPPED" : "IS.UNMAPPED");
                if (balanceSheet && a.Temporary) key = "TEMP";     // profit of the year not yet closed
                result[key] = (result.TryGetValue(key, out decimal v) ? v : 0) + s.Debit - s.Credit;   // debit minus credit
            }
            return result;
        }

        public async Task<ResponseDto<FinancialStatementDto>> BalanceSheetAsync(LedgerReportQuery q, CancellationToken ct)
        {
            q ??= new LedgerReportQuery();
            var r = await RangeAsync(q, ct);
            if (r.Error != null) return Bad<FinancialStatementDto>(r.Error);
            Dictionary<string, decimal> bal = await LineBalancesAsync(r.Range, q.IncludeDraft, true, ct);
            decimal Net(string code) => bal.TryGetValue(code, out decimal v) ? v : 0;
            var dto = new FinancialStatementDto { Kind = "BalanceSheet", FiscalYearId = r.Range.Year.Id, From = r.Range.From, To = r.Range.To };
            string[] assetSections = { "دارایی‌های جاری", "دارایی‌های غیرجاری" };
            string[] liabilitySections = { "بدهی‌های جاری", "بدهی‌های غیرجاری" };
            decimal Section(string section, bool asset)
            {
                decimal total = 0;
                foreach (var line in FinanceSeed.StatementLines.Where(x => x.Code.StartsWith("BS.") && x.Section == section))
                {
                    decimal amount = asset ? Net(line.Code) : -Net(line.Code);
                    total += amount;
                    if (amount != 0) dto.Lines.Add(new StatementLineDto { Code = line.Code, Title = line.Title, Section = section, Amount = amount });
                }
                dto.Lines.Add(new StatementLineDto { Code = "TOTAL." + section, Title = "جمع " + section, Section = section, Amount = total, IsTotal = true });
                return total;
            }
            foreach (string s in assetSections) dto.TotalAssets += Section(s, true);
            dto.Lines.Add(new StatementLineDto { Code = "TOTAL.ASSETS", Title = "جمع دارایی‌ها", Section = "جمع", Amount = dto.TotalAssets, IsTotal = true });
            foreach (string s in liabilitySections) dto.TotalLiabilities += Section(s, false);
            decimal equity = Section("حقوق مالکانه", false);
            decimal currentProfit = -Net("TEMP");   // credit minus debit of the temporary accounts
            if (currentProfit != 0)
            {
                dto.Lines.Add(new StatementLineDto { Code = "BS.CURPROFIT", Title = "سود (زیان) دوره جاری (بسته‌نشده)", Section = "حقوق مالکانه", Amount = currentProfit });
                equity += currentProfit;
            }
            decimal unmapped = Net("BS.UNMAPPED");
            if (unmapped != 0) dto.Lines.Add(new StatementLineDto { Code = "BS.UNMAPPED", Title = "حساب‌های بدون سطر صورت مالی", Section = "نامشخص", Amount = unmapped });
            dto.TotalEquity = equity;
            dto.Lines.Add(new StatementLineDto { Code = "TOTAL.LIABILITIES", Title = "جمع بدهی‌ها", Section = "جمع", Amount = dto.TotalLiabilities, IsTotal = true });
            dto.Lines.Add(new StatementLineDto { Code = "TOTAL.EQUITY", Title = "جمع حقوق مالکانه", Section = "جمع", Amount = dto.TotalEquity, IsTotal = true });
            dto.Balanced = dto.TotalAssets + unmapped == dto.TotalLiabilities + dto.TotalEquity;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<FinancialStatementDto>> IncomeStatementAsync(LedgerReportQuery q, CancellationToken ct)
        {
            q ??= new LedgerReportQuery();
            var r = await RangeAsync(q, ct);
            if (r.Error != null) return Bad<FinancialStatementDto>(r.Error);
            Dictionary<string, decimal> bal = await LineBalancesAsync(r.Range, q.IncludeDraft, false, ct);
            decimal Credit(string code) => bal.TryGetValue(code, out decimal v) ? -v : 0;     // revenue side
            decimal Debit(string code) => bal.TryGetValue(code, out decimal v) ? v : 0;       // expense side
            var dto = new FinancialStatementDto { Kind = "IncomeStatement", FiscalYearId = r.Range.Year.Id, From = r.Range.From, To = r.Range.To };
            void Add(string code, decimal amount, bool total = false)
            {
                string title = total ? null : FinanceSeed.StatementLines.First(x => x.Code == code).Title;
                dto.Lines.Add(new StatementLineDto { Code = code, Title = title, Section = "صورت سود و زیان", Amount = amount, IsTotal = total });
            }
            decimal revenue = Credit("IS.REV"), cogs = Debit("IS.COGS");
            Add("IS.REV", revenue); Add("IS.COGS", cogs);
            dto.GrossProfit = revenue - cogs;
            dto.Lines.Add(new StatementLineDto { Code = "GROSS", Title = "سود (زیان) ناخالص", Section = "صورت سود و زیان", Amount = dto.GrossProfit, IsTotal = true });
            decimal adm = Debit("IS.ADM"), ooi = Credit("IS.OOI");
            Add("IS.ADM", adm); Add("IS.OOI", ooi);
            dto.OperatingProfit = dto.GrossProfit - adm + ooi;
            dto.Lines.Add(new StatementLineDto { Code = "OPERATING", Title = "سود (زیان) عملیاتی", Section = "صورت سود و زیان", Amount = dto.OperatingProfit, IsTotal = true });
            decimal fin = Debit("IS.FIN"), nonopi = Credit("IS.NONOPI"), nonope = Debit("IS.NONOPE");
            Add("IS.FIN", fin); Add("IS.NONOPI", nonopi); Add("IS.NONOPE", nonope);
            dto.ProfitBeforeTax = dto.OperatingProfit - fin + nonopi - nonope;
            dto.Lines.Add(new StatementLineDto { Code = "PBT", Title = "سود (زیان) قبل از مالیات", Section = "صورت سود و زیان", Amount = dto.ProfitBeforeTax, IsTotal = true });
            decimal tax = Debit("IS.TAX");
            Add("IS.TAX", tax);
            decimal unmapped = -Debit("IS.UNMAPPED");
            dto.NetProfit = dto.ProfitBeforeTax - tax + unmapped;
            if (unmapped != 0) dto.Lines.Add(new StatementLineDto { Code = "IS.UNMAPPED", Title = "حساب‌های بدون سطر صورت مالی", Section = "نامشخص", Amount = unmapped });
            dto.Lines.Add(new StatementLineDto { Code = "NET", Title = "سود (زیان) خالص", Section = "صورت سود و زیان", Amount = dto.NetProfit, IsTotal = true });
            return ResponseDtoHelper.Ok(dto);
        }

        // ================================================================================== project profitability

        public async Task<ResponseDto<PagedList<ProjectProfitabilityRowDto>>> ProjectProfitabilityAsync(LedgerReportQuery q, CancellationToken ct)
        {
            q ??= new LedgerReportQuery();
            var r = await RangeAsync(q, ct);
            if (r.Error != null) return Bad<PagedList<ProjectProfitabilityRowDto>>(r.Error);
            IQueryable<FnVoucherLine> lines = Lines(r.Range.Year.Id, q.IncludeDraft)
                .Where(l => l.ContractId != null && l.Voucher.Date >= r.Range.From && l.Voucher.Date <= r.Range.To && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose && l.Voucher.Type != FnVoucherType.Closing);
            var revenue = await lines.Where(l => l.Account.Code.StartsWith("601")).GroupBy(l => l.ContractId.Value)
                .Select(g => new { g.Key, Amount = g.Sum(x => x.Credit) - g.Sum(x => x.Debit) }).ToListAsync(ct);
            var cost = await lines.Where(l => l.Account.Code.StartsWith("7")).GroupBy(l => l.ContractId.Value)
                .Select(g => new { g.Key, Amount = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) }).ToListAsync(ct);
            List<int> ids = revenue.Select(x => x.Key).Concat(cost.Select(x => x.Key)).Distinct().ToList();
            var entries = await _uow.Set<CostEntry>().AsNoTracking().Where(c => c.ContractId != null && ids.Contains(c.ContractId.Value) && c.EntryDate >= r.Range.From && c.EntryDate <= r.Range.To)
                .GroupBy(c => c.ContractId.Value).Select(g => new { g.Key, Amount = g.Sum(c => c.Amount) }).ToListAsync(ct);
            var contracts = await _uow.Set<ContractEntity>().AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.Code, c.Title }).ToListAsync(ct);
            var list = new List<ProjectProfitabilityRowDto>();
            foreach (int id in ids)
            {
                decimal rev = revenue.FirstOrDefault(x => x.Key == id)?.Amount ?? 0, cst = cost.FirstOrDefault(x => x.Key == id)?.Amount ?? 0;
                decimal ce = entries.FirstOrDefault(x => x.Key == id)?.Amount ?? 0;
                var c = contracts.FirstOrDefault(x => x.Id == id);
                list.Add(new ProjectProfitabilityRowDto
                {
                    ContractId = id, ContractCode = c?.Code, ContractTitle = c?.Title, Revenue = rev, Cost = cst, Margin = rev - cst,
                    MarginPercent = rev == 0 ? (decimal?)null : Math.Round(100m * (rev - cst) / rev, 1), CostEntries = ce, CostDifference = cst - ce,
                });
            }
            list = list.OrderByDescending(x => x.Revenue).ThenBy(x => x.ContractCode).ToList();
            return ResponseDtoHelper.Ok(await PagedList<ProjectProfitabilityRowDto>.CreateAsync(list, q.PageNumber, q.PageSize, ct));
        }

        // ================================================================================== reconciliation

        internal static readonly AccountingEventStatus[] Unposted =
        {
            AccountingEventStatus.Pending, AccountingEventStatus.Exported, AccountingEventStatus.Failed, AccountingEventStatus.Sent,
        };

        public async Task<ResponseDto<LedgerReconciliationDto>> ReconcileAsync(int oldVoucherDays, CancellationToken ct)
        {
            await FinanceSeed.EnsureAsync(_uow, ct);
            if (oldVoucherDays <= 0) oldVoucherDays = 7;
            var dto = new LedgerReconciliationDto { OldVoucherDays = oldVoucherDays };
            IQueryable<FnVoucherLine> all = _uow.Set<FnVoucherLine>().AsNoTracking();
            IQueryable<AccountingEventLine> postedLines = _uow.Set<AccountingEventLine>().AsNoTracking().Where(l => l.Event.Status == AccountingEventStatus.Posted);

            void Row(string key, string title, decimal ledger, decimal sub, string note = null)
            {
                dto.Rows.Add(new ReconRowDto { Key = key, Title = title, Ledger = ledger, SubLedger = sub, Difference = ledger - sub, Ok = ledger == sub, Note = note });
            }

            // inventory ledger against the cost sub-ledger (ItemCost)
            decimal inventoryLedger = await all.Where(l => l.SourceRole == AccountRoles.Inventory || l.SourceRole.StartsWith(AccountRoles.Inventory + "."))
                .SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0;
            decimal itemCost = await _uow.Set<ItemCost>().AsNoTracking().SumAsync(c => (decimal?)c.Value, ct) ?? 0;
            Row("inventory", "موجودی کالا: دفتر کل و بهای تمام‌شده کالا", inventoryLedger, itemCost);

            // goods received not invoiced
            decimal grniLedger = await all.Where(l => l.SourceRole == AccountRoles.Grni).SumAsync(l => (decimal?)(l.Credit - l.Debit), ct) ?? 0;
            decimal grniEvents = await postedLines.Where(l => l.AccountRole == AccountRoles.Grni).SumAsync(l => (decimal?)(l.Credit - l.Debit), ct) ?? 0;
            Row("grni", "کالای دریافتی فاکتورنشده: دفتر کل و رسیدهای باز", grniLedger, grniEvents);

            // accounts payable per supplier against approved invoices minus paid payment requests
            var apLedger = await all.Where(l => l.SourceRole == AccountRoles.AccountsPayable && l.Detail1Id != null)
                .GroupBy(l => l.Detail1.LinkedEntityId).Select(g => new { Provider = g.Key, Amount = g.Sum(x => x.Credit) - g.Sum(x => x.Debit) }).ToListAsync(ct);
            var invoiced = await _uow.Set<SupplierInvoice>().AsNoTracking().Where(i => !i.Deleted && i.Status == SupplierInvoiceStatus.Approved)
                .GroupBy(i => i.ProviderId).Select(g => new { Provider = g.Key, Amount = g.Sum(i => i.TotalAmount) }).ToListAsync(ct);
            var paid = await _uow.Set<PaymentRequest>().AsNoTracking().Where(p => !p.Deleted && p.Status == PaymentRequestStatus.Paid && p.InvoiceId != null)
                .GroupBy(p => p.ProviderId).Select(g => new { Provider = g.Key, Amount = g.Sum(p => p.Amount) }).ToListAsync(ct);
            var providerIds = apLedger.Where(x => x.Provider.HasValue).Select(x => x.Provider.Value).Concat(invoiced.Select(x => x.Provider)).Concat(paid.Select(x => x.Provider)).Distinct().ToList();
            decimal apL = 0, apS = 0, apAbs = 0;
            var bad = new List<string>();
            foreach (int pid in providerIds)
            {
                decimal l = apLedger.Where(x => x.Provider == pid).Sum(x => x.Amount);
                decimal s = (invoiced.FirstOrDefault(x => x.Provider == pid)?.Amount ?? 0) - (paid.FirstOrDefault(x => x.Provider == pid)?.Amount ?? 0);
                apL += l; apS += s;
                if (l != s) { apAbs += Math.Abs(l - s); bad.Add($"تامین‌کننده {pid}: دفتر {l:0} و پرداختنی {s:0}"); }
            }
            dto.Rows.Add(new ReconRowDto
            {
                Key = "ap", Title = "حساب‌های پرداختنی به تفکیک تامین‌کننده", Ledger = apL, SubLedger = apS, Difference = apAbs, Ok = apAbs == 0,
                Note = bad.Count == 0 ? null : string.Join(" | ", bad.Take(10)),
            });

            // bank ledger against the treasury balances
            decimal bankLedger = await all.Where(l => (l.Account.Code == "10102" || l.Account.Code == "10103") && l.Detail1Id != null)
                .SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0;
            decimal bankEvents = await postedLines.Where(l => l.PartyType == "BankAccount").SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0;
            Row("bank", "بانک: دفتر کل و خزانه", bankLedger, bankEvents);

            // the ledger as a whole
            decimal totalDebit = await all.SumAsync(l => (decimal?)l.Debit, ct) ?? 0, totalCredit = await all.SumAsync(l => (decimal?)l.Credit, ct) ?? 0;
            Row("balance", "جمع بدهکار و بستانکار دفتر کل", totalDebit, totalCredit);

            dto.UnpostedEvents = await _uow.Set<AccountingEvent>().CountAsync(e => Unposted.Contains(e.Status), ct);
            DateTimeOffset limit = DateTimeOffset.Now.AddDays(-oldVoucherDays);
            dto.OldNonFinalVouchers = await _uow.Set<FnVoucher>().CountAsync(v => v.Status != FnVoucherStatus.Final && v.CreatedAt < limit, ct);
            dto.Clean = dto.Rows.All(x => x.Ok) && dto.UnpostedEvents == 0;
            return ResponseDtoHelper.Ok(dto);
        }

        // ================================================================================== export

        public async Task<(byte[] Content, string FileName, string Error)> ExportAsync(string report, LedgerReportQuery q, CancellationToken ct)
        {
            q ??= new LedgerReportQuery();
            q.PageNumber = 1; q.PageSize = 0;
            string name = (report ?? "").Trim().ToLowerInvariant();
            object data;
            switch (name)
            {
                case "journal":
                {
                    var res = await JournalAsync(q, ct);
                    if (!res.Success) return (null, null, res.Message);
                    var flat = new List<Dictionary<string, object>>();
                    foreach (JournalDayDto day in res.Data)
                        foreach (JournalAccountDto a in day.Accounts)
                            flat.Add(new Dictionary<string, object> { { "date", day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, { "accountCode", a.Code }, { "accountTitle", a.Title }, { "debit", a.Debit }, { "credit", a.Credit } });
                    data = flat;
                    break;
                }
                case "general-ledger": { var res = await GeneralLedgerAsync(q, ct); if (!res.Success) return (null, null, res.Message); data = res.Data.Rows; break; }
                case "account-review": { var res = await AccountReviewAsync(q, ct); if (!res.Success) return (null, null, res.Message); data = res.Data.Rows; break; }
                case "trial-balance": { var res = await TrialBalanceAsync(q, ct); if (!res.Success) return (null, null, res.Message); data = res.Data.Rows; break; }
                case "balance-sheet": { var res = await BalanceSheetAsync(q, ct); if (!res.Success) return (null, null, res.Message); data = res.Data.Lines; break; }
                case "income-statement": { var res = await IncomeStatementAsync(q, ct); if (!res.Success) return (null, null, res.Message); data = res.Data.Lines; break; }
                case "project-profitability": { var res = await ProjectProfitabilityAsync(q, ct); if (!res.Success) return (null, null, res.Message); data = res.Data.ToList(); break; }
                case "reconciliation": { var res = await ReconcileAsync(7, ct); if (!res.Success) return (null, null, res.Message); data = res.Data.Rows; break; }
                default: return (null, null, "گزارش نامعتبر است.");
            }
            return (CsvWriter.ToCsv(data), $"{name}-{DateTime.Today:yyyyMMdd}.csv", null);
        }
    }

    /// <summary>Flattens a list of objects to CSV (UTF-8 with BOM so Excel reads Persian text); nested lists are skipped.</summary>
    internal static class CsvWriter
    {
        public static byte[] ToCsv(object rows)
        {
            JsonElement root = JsonSerializer.SerializeToElement(rows);
            var sb = new StringBuilder();
            if (root.ValueKind == JsonValueKind.Array)
            {
                List<string> columns = null;
                foreach (JsonElement item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (columns == null)
                    {
                        columns = item.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Array && p.Value.ValueKind != JsonValueKind.Object).Select(p => p.Name).ToList();
                        sb.AppendLine(string.Join(",", columns.Select(Quote)));
                    }
                    sb.AppendLine(string.Join(",", columns.Select(c => item.TryGetProperty(c, out JsonElement v) ? Cell(v) : "")));
                }
                if (columns == null) sb.AppendLine("");
            }
            byte[] body = new UTF8Encoding(false).GetBytes(sb.ToString());
            byte[] result = new byte[body.Length + 3];
            result[0] = 0xEF; result[1] = 0xBB; result[2] = 0xBF;
            Buffer.BlockCopy(body, 0, result, 3, body.Length);
            return result;
        }

        private static string Cell(JsonElement v)
        {
            switch (v.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return "";
                case JsonValueKind.Number: return v.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return Quote(v.GetString());
            }
        }

        private static string Quote(string s)
        {
            if (s == null) return "";
            // spreadsheet formula injection: a text cell never starts with = + - @
            if (s.Length > 0 && (s[0] == '=' || s[0] == '+' || s[0] == '-' || s[0] == '@')) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
