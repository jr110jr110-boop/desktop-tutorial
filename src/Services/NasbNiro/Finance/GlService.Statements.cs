using System;
using System.Collections.Generic;
using System.Globalization;
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
    public interface IStatementsService
    {
        Task<ResponseDto<FnCashFlowDto>> CashFlowAsync(FnStatementQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<FnEquityDto>> EquityChangesAsync(FnStatementQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<FnRatiosDto>> RatiosAsync(FnStatementQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<FnDrillDto>> DrillDownAsync(string accountPrefix, FnStatementQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<List<FnViewDto>>> GetViewsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<FnViewDto>> SaveViewAsync(FnSaveViewDto dto, CancellationToken cancellationToken);
    }

    // Financial statements and analysis (F3 section 8). Everything is computed from the vouchers of one fiscal year; nothing is stored.
    public partial class GlService : IStatementsService
    {
        private sealed class Sums { public string Code; public FnVoucherType Type; public decimal Opening; public decimal Period; }

        private async Task<(FnFiscalYear Year, DateTime From, DateTime To, List<Sums> Rows, string Error)> StatementDataAsync(FnStatementQuery q, CancellationToken ct)
        {
            q ??= new FnStatementQuery();
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            FnFiscalYear year = q.FiscalYearId.HasValue
                ? await _uow.Set<FnFiscalYear>().AsNoTracking().FirstOrDefaultAsync(y => y.Id == q.FiscalYearId.Value, ct)
                : await _uow.Set<FnFiscalYear>().AsNoTracking().Where(y => y.LegalEntityId == entityId && y.StartDate <= DateTime.Today && y.EndDate >= DateTime.Today).FirstOrDefaultAsync(ct);
            if (year == null) return (null, default, default, null, "سال مالی پیدا نشد.");
            DateTime from = (q.From ?? year.StartDate).Date, to = (q.To ?? year.EndDate).Date;
            if (from < year.StartDate.Date || to > year.EndDate.Date || from > to) return (null, default, default, null, "بازه باید داخل سال مالی باشد.");
            IQueryable<FnVoucherLine> lines = _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.Voucher.FiscalYearId == year.Id && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose);
            lines = q.IncludeDraft ? lines : lines.Where(l => l.Voucher.Status == FnVoucherStatus.Final);
            lines = FnLabourMask.Visible(lines, FnLabourMask.Sees(_user));   // R1p D1/D2: protected labour lines (a debit and a credit of the same amount) are read by PAYROLL, FINANCEMANAGER and CEO only
            var rows = await lines.Where(l => l.Voucher.Date <= to)
                .GroupBy(l => new { l.Account.Code, l.Voucher.Type, Before = l.Voucher.Date < from || l.Voucher.Type == FnVoucherType.Opening })
                .Select(g => new { g.Key.Code, g.Key.Type, g.Key.Before, Net = g.Sum(x => x.Debit - x.Credit) }).ToListAsync(ct);
            var list = rows.GroupBy(r => r.Code).Select(g => new Sums { Code = g.Key, Opening = g.Where(x => x.Before).Sum(x => x.Net), Period = g.Where(x => !x.Before).Sum(x => x.Net) }).ToList();
            return (year, from, to, list, null);
        }

        private static decimal Net(IEnumerable<Sums> rows, Func<string, bool> pick, bool opening) => rows.Where(r => pick(r.Code)).Sum(r => opening ? r.Opening : r.Period);
        private static bool Starts(string code, params string[] prefixes) => prefixes.Any(p => code.StartsWith(p, StringComparison.Ordinal));
        private static readonly string[] DepreciationExpense = { "70203", "80104" };

        public async Task<ResponseDto<FnCashFlowDto>> CashFlowAsync(FnStatementQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnCashFlowDto>(NotAllowed);
            var d = await StatementDataAsync(query, ct);
            if (d.Error != null) return Bad<FnCashFlowDto>(d.Error);
            var dto = new FnCashFlowDto { FiscalYearId = d.Year.Id, From = d.From, To = d.To };
            Func<string, bool> isCash = c => Starts(c, "101");
            dto.OpeningCash = Net(d.Rows, isCash, true);
            decimal cashMovement = Net(d.Rows, isCash, false);
            dto.ClosingCash = dto.OpeningCash + cashMovement;
            dto.NetProfit = -Net(d.Rows, c => Starts(c, "6", "7", "8"), false);
            decimal depreciation = Net(d.Rows, c => DepreciationExpense.Contains(c), false);

            FnCashFlowLineDto Line(string key, string title, string section, decimal amount, IEnumerable<string> accounts) =>
                new FnCashFlowLineDto { Key = key, Title = title, Section = section, Amount = amount, Accounts = accounts.OrderBy(a => a, StringComparer.Ordinal).ToList() };
            IEnumerable<string> Codes(Func<string, bool> pick) => d.Rows.Where(r => pick(r.Code) && r.Period != 0).Select(r => r.Code);
            var lines = new List<FnCashFlowLineDto>
            {
                Line("NetProfit", "سود (زیان) خالص", "Operating", dto.NetProfit, Codes(c => Starts(c, "6", "7", "8"))),
                Line("Depreciation", "استهلاک (هزینه غیرنقدی)", "Operating", depreciation, DepreciationExpense.Where(c => d.Rows.Any(r => r.Code == c && r.Period != 0))),
            };
            // operating working capital: receivables, other receivables, inventory, prepayments, contracts in progress, payables, other payables, advances, provisions, severance provision
            Func<string, bool> opAssets = c => Starts(c, "103", "104", "105", "106", "107");
            Func<string, bool> opLiabs = c => Starts(c, "301", "302", "303", "304", "402");
            var wc = new (string Key, string Title, string[] Prefix)[]
            {
                ("Receivables", "افزایش (کاهش) دریافتنی‌های تجاری", new[] { "103" }), ("OtherReceivables", "افزایش (کاهش) سایر دریافتنی‌ها", new[] { "104" }),
                ("Inventory", "افزایش (کاهش) موجودی مواد و کالا", new[] { "105" }), ("Prepayments", "افزایش (کاهش) پیش‌پرداخت‌ها", new[] { "106" }), ("Contracts", "افزایش (کاهش) قراردادهای در جریان", new[] { "107" }),
                ("Payables", "افزایش (کاهش) پرداختنی‌های تجاری", new[] { "301" }), ("OtherPayables", "افزایش (کاهش) سایر پرداختنی‌ها", new[] { "302" }), ("Advances", "افزایش (کاهش) پیش‌دریافت‌ها", new[] { "303" }),
                ("Provisions", "افزایش (کاهش) ذخایر", new[] { "304" }), ("Severance", "افزایش (کاهش) ذخیره مزایای پایان خدمت", new[] { "402" }),
            };
            foreach (var w in wc)
            {
                Func<string, bool> pick = c => Starts(c, w.Prefix);
                lines.Add(Line(w.Key, w.Title, "Operating", -Net(d.Rows, pick, false), Codes(pick)));
            }
            Func<string, bool> ppe = c => Starts(c, "201", "203", "205");
            Func<string, bool> accDep = c => Starts(c, "202", "204");
            decimal accDepMovement = -Net(d.Rows, accDep, false); // credit increase
            lines.Add(Line("FixedAssets", "خرید (فروش) دارایی‌های ثابت و نامشهود و سرمایه‌گذاری بلندمدت", "Investing", -Net(d.Rows, ppe, false), Codes(ppe)));
            lines.Add(Line("ShortTermInvestments", "سرمایه‌گذاری‌های کوتاه‌مدت", "Investing", -Net(d.Rows, c => Starts(c, "102"), false), Codes(c => Starts(c, "102"))));
            lines.Add(Line("DisposalsAndOther", "استهلاک انباشته منهای استهلاک دوره (فروش و سایر)", "Investing", accDepMovement - depreciation, Codes(accDep)));
            lines.Add(Line("ShortTermFinancing", "تسهیلات مالی کوتاه‌مدت", "Financing", -Net(d.Rows, c => Starts(c, "305"), false), Codes(c => Starts(c, "305"))));
            lines.Add(Line("LongTermFinancing", "تسهیلات مالی بلندمدت", "Financing", -Net(d.Rows, c => Starts(c, "401"), false), Codes(c => Starts(c, "401"))));
            lines.Add(Line("Equity", "افزایش (کاهش) حقوق مالکانه (سرمایه، اندوخته، سود انباشته)", "Financing", -Net(d.Rows, c => Starts(c, "5"), false), Codes(c => Starts(c, "5"))));
            // anything else (unexpected account groups) is shown so that the statement always ties
            Func<string, bool> known = c => Starts(c, "101", "102", "103", "104", "105", "106", "107", "201", "202", "203", "204", "205", "301", "302", "303", "304", "305", "401", "402", "5", "6", "7", "8", "9");
            decimal other = -Net(d.Rows, c => !known(c), false);
            if (other != 0) lines.Add(Line("Other", "سایر", "Operating", other, Codes(c => !known(c))));
            dto.Lines = lines.Where(l => l.Amount != 0 || l.Key == "NetProfit").ToList();
            dto.Operating = lines.Where(l => l.Section == "Operating").Sum(l => l.Amount);
            dto.Investing = lines.Where(l => l.Section == "Investing").Sum(l => l.Amount);
            dto.Financing = lines.Where(l => l.Section == "Financing").Sum(l => l.Amount);
            dto.NetChange = dto.Operating + dto.Investing + dto.Financing;
            dto.Ties = dto.OpeningCash + dto.NetChange == dto.ClosingCash;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<FnEquityDto>> EquityChangesAsync(FnStatementQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnEquityDto>(NotAllowed);
            var d = await StatementDataAsync(query, ct);
            if (d.Error != null) return Bad<FnEquityDto>(d.Error);
            var dto = new FnEquityDto { FiscalYearId = d.Year.Id, From = d.From, To = d.To };
            var comps = new (string Key, string Title, string Prefix)[] { ("Capital", "سرمایه", "501"), ("Reserve", "اندوخته قانونی", "502"), ("RetainedEarnings", "سود (زیان) انباشته", "503"), ("Adjustments", "تعدیلات سنواتی", "504"), ("Summary", "خلاصه سود و زیان", "509") };
            foreach (var c in comps)
            {
                decimal opening = -Net(d.Rows, x => Starts(x, c.Prefix), true), movement = -Net(d.Rows, x => Starts(x, c.Prefix), false);
                if (opening == 0 && movement == 0 && c.Key == "Summary") continue;
                dto.Rows.Add(new FnEquityRowDto { Key = c.Key, Title = c.Title, Opening = opening, Movement = movement, Closing = opening + movement });
            }
            decimal openingProfit = -Net(d.Rows, x => Starts(x, "6", "7", "8"), true);
            dto.NetProfit = -Net(d.Rows, x => Starts(x, "6", "7", "8"), false);
            dto.Rows.Add(new FnEquityRowDto { Key = "NetProfit", Title = "سود (زیان) خالص دوره", Opening = openingProfit, Movement = dto.NetProfit, Closing = openingProfit + dto.NetProfit });
            dto.OpeningEquity = dto.Rows.Sum(r => r.Opening);
            dto.ClosingEquity = dto.Rows.Sum(r => r.Closing);
            decimal assets = Net(d.Rows, x => Starts(x, "1", "2"), true) + Net(d.Rows, x => Starts(x, "1", "2"), false);
            decimal liabilities = -(Net(d.Rows, x => Starts(x, "3", "4"), true) + Net(d.Rows, x => Starts(x, "3", "4"), false));
            dto.BalanceSheetEquity = assets - liabilities;
            dto.Ties = dto.ClosingEquity == dto.BalanceSheetEquity;
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<FnRatiosDto>> RatiosAsync(FnStatementQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnRatiosDto>(NotAllowed);
            var d = await StatementDataAsync(query, ct);
            if (d.Error != null) return Bad<FnRatiosDto>(d.Error);
            decimal Bal(params string[] prefix) => Net(d.Rows, c => Starts(c, prefix), true) + Net(d.Rows, c => Starts(c, prefix), false);
            decimal Move(params string[] prefix) => Net(d.Rows, c => Starts(c, prefix), false);
            int days = (int)(d.To - d.From).TotalDays + 1;
            decimal revenue = -Move("601"), cogs = Move("701", "702"), profit = -Move("6", "7", "8");
            decimal currentAssets = Bal("101", "102", "103", "104", "105", "106", "107"), currentLiabs = -Bal("301", "302", "303", "304", "305");
            decimal inventory = Bal("105"), prepaid = Bal("106", "107");
            decimal receivables = Bal("103"), payables = -Bal("301");
            decimal totalAssets = Bal("1", "2"), liabilities = -Bal("3", "4"), equity = totalAssets - liabilities;
            decimal? Div(decimal a, decimal b, int dec = 4) => b == 0 ? (decimal?)null : Math.Round(a / b, dec, MidpointRounding.AwayFromZero);
            var dto = new FnRatiosDto { FiscalYearId = d.Year.Id, From = d.From, To = d.To, Days = days, Revenue = revenue, CostOfSales = cogs, NetProfit = profit };
            void Add(string key, string title, decimal? value, string formula) => dto.Ratios.Add(new FnRatioDto { Key = key, Title = title, Value = value, Formula = formula });
            Add("CurrentRatio", "نسبت جاری", Div(currentAssets, currentLiabs), "دارایی‌های جاری ÷ بدهی‌های جاری");
            Add("QuickRatio", "نسبت آنی", Div(currentAssets - inventory - prepaid, currentLiabs), "(دارایی‌های جاری − موجودی − پیش‌پرداخت‌ها) ÷ بدهی‌های جاری");
            Add("DebtRatio", "نسبت بدهی", Div(liabilities, totalAssets), "کل بدهی‌ها ÷ کل دارایی‌ها");
            Add("EquityRatio", "نسبت مالکانه", Div(equity, totalAssets), "حقوق مالکانه ÷ کل دارایی‌ها");
            Add("GrossMargin", "حاشیه سود ناخالص", Div(revenue - cogs, revenue), "(درآمد − بهای تمام‌شده) ÷ درآمد");
            Add("NetMargin", "حاشیه سود خالص", Div(profit, revenue), "سود خالص ÷ درآمد");
            Add("DSO", "دوره وصول مطالبات (روز)", revenue == 0 ? null : Div(receivables * days, revenue, 2), "دریافتنی‌های تجاری × روزهای دوره ÷ درآمد");
            Add("DPO", "دوره پرداخت بدهی (روز)", cogs == 0 ? null : Div(payables * days, cogs, 2), "پرداختنی‌های تجاری × روزهای دوره ÷ بهای تمام‌شده");
            Add("DIO", "دوره گردش موجودی (روز)", cogs == 0 ? null : Div(inventory * days, cogs, 2), "موجودی × روزهای دوره ÷ بهای تمام‌شده");
            Add("InventoryTurnover", "گردش موجودی (دفعه)", Div(cogs, inventory, 2), "بهای تمام‌شده ÷ موجودی پایان دوره");
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<FnDrillDto>> DrillDownAsync(string accountPrefix, FnStatementQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnDrillDto>(NotAllowed);
            string prefix = Digits(Clean(accountPrefix) ?? "");
            if (prefix.Length == 0) return Bad<FnDrillDto>("کد حساب را وارد کنید.");
            var d = await StatementDataAsync(query, ct);
            if (d.Error != null) return Bad<FnDrillDto>(d.Error);
            IQueryable<FnVoucherLine> q = _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.Voucher.FiscalYearId == d.Year.Id && l.Account.Code.StartsWith(prefix) && l.Voucher.Date >= d.From && l.Voucher.Date <= d.To);
            if (!(query?.IncludeDraft ?? false)) q = q.Where(l => l.Voucher.Status == FnVoucherStatus.Final);
            bool seesProtected = FnLabourMask.Sees(_user);
            bool labourHidden = !seesProtected && await q.AnyAsync(l => l.LabourProtected, ct);
            q = FnLabourMask.Visible(q, seesProtected);   // R1p D1/D2: protected labour lines are read by PAYROLL, FINANCEMANAGER and CEO only
            var rows = await q.OrderBy(l => l.Voucher.Date).ThenBy(l => l.VoucherId).ThenBy(l => l.RowNumber).Take(2000)
                .Select(l => new FnDrillRowDto { VoucherId = l.VoucherId, Number = l.Voucher.Number, TemporaryNumber = l.Voucher.TemporaryNumber, Date = l.Voucher.Date, Description = l.Description ?? l.Voucher.Description, AccountCode = l.Account.Code, Debit = l.Debit, Credit = l.Credit, SourceType = l.Voucher.SourceType, SourceNumber = l.Voucher.SourceNumber }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(new FnDrillDto { AccountPrefix = prefix, Rows = rows, TotalDebit = rows.Sum(r => r.Debit), TotalCredit = rows.Sum(r => r.Credit), LabourHidden = labourHidden });
        }

        public async Task<ResponseDto<List<FnViewDto>>> GetViewsAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnViewDto>>(NotAllowed);
            var rows = await _uow.Set<FnReportView>().AsNoTracking().Where(v => v.Shared || v.OwnerUserId == Me).OrderBy(v => v.Title).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(v => new FnViewDto { Id = v.Id, ReportKey = v.ReportKey, Title = v.Title, FiltersJson = v.FiltersJson, ColumnsJson = v.ColumnsJson, Shared = v.Shared, OwnerUserId = v.OwnerUserId }).ToList());
        }

        public async Task<ResponseDto<FnViewDto>> SaveViewAsync(FnSaveViewDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnViewDto>(NotAllowed);
            if (dto == null || Clean(dto.ReportKey) == null || Clean(dto.Title) == null) return Bad<FnViewDto>("گزارش و عنوان نما را وارد کنید.");
            FnReportView v = dto.Id.HasValue ? await _uow.Set<FnReportView>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct) : null;
            if (dto.Id.HasValue && (v == null || v.OwnerUserId != Me)) return ResponseDtoHelper.NotFound<FnViewDto>("نما پیدا نشد.");
            if (v == null) { v = new FnReportView { OwnerUserId = Me, CreatedAt = DateTimeOffset.Now }; _uow.Set<FnReportView>().Add(v); }
            v.ReportKey = Clean(dto.ReportKey); v.Title = Clean(dto.Title); v.FiltersJson = dto.FiltersJson; v.ColumnsJson = dto.ColumnsJson; v.Shared = dto.Shared;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new FnViewDto { Id = v.Id, ReportKey = v.ReportKey, Title = v.Title, FiltersJson = v.FiltersJson, ColumnsJson = v.ColumnsJson, Shared = v.Shared, OwnerUserId = v.OwnerUserId });
        }
    }
}
