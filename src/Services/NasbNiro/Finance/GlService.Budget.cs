using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    public interface IBudgetService
    {
        Task<ResponseDto<List<FnBudgetDto>>> GetBudgetsAsync(int? fiscalYearId, CancellationToken cancellationToken);
        Task<ResponseDto<FnBudgetDto>> GetBudgetAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<FnBudgetDto>> SaveBudgetAsync(FnSaveBudgetDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<FnBudgetDto>> ImportAsync(FnBudgetImportDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<string>> ExportAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<FnBudgetDto>> ApproveAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<FnBudgetDto>> ReviseAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<FnBudgetReportDto>> ReportAsync(FnBudgetReportQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<FnCommitmentCheckDto>> CheckAsync(FnCommitmentCheckRequest request, CancellationToken cancellationToken);
        Task<ResponseDto<FnCommitmentCheckDto>> CheckPurchaseRequestAsync(int purchaseRequestId, string overrideReason, CancellationToken cancellationToken);
    }

    // Budgeting and commitment control (F3 section 4). Commitment = the full-year budget of the most specific matching line minus the year-to-date actual.
    public partial class GlService : IBudgetService
    {
        private sealed class BudgetKey
        {
            public string AccountCode; public int? CostCenterId; public int? ContractId; public string Display;
            public int Score => (AccountCode?.Length ?? 0) * 100 + (CostCenterId.HasValue ? 10 : 0) + (ContractId.HasValue ? 1 : 0);
            public string Id => (AccountCode ?? "-") + "|" + (CostCenterId?.ToString(CultureInfo.InvariantCulture) ?? "-") + "|" + (ContractId?.ToString(CultureInfo.InvariantCulture) ?? "-");
            public bool Matches(string code, int? cc, int? contract) =>
                (AccountCode == null || (code != null && code.StartsWith(AccountCode, StringComparison.Ordinal))) && (!CostCenterId.HasValue || CostCenterId == cc) && (!ContractId.HasValue || ContractId == contract);
        }

        private sealed class ActualRow { public string Code; public FnAccountNature Nature; public int? CostCenterId; public int? ContractId; public DateTime Date; public decimal Debit; public decimal Credit; public decimal Value => Nature == FnAccountNature.Credit ? Credit - Debit : Debit - Credit; }

        private async Task<FnBudgetDto> BudgetDtoAsync(FnBudget b, CancellationToken ct)
        {
            var lines = await _uow.Set<FnBudgetLine>().AsNoTracking().Where(l => l.BudgetId == b.Id).OrderBy(l => l.Id).ToListAsync(ct);
            var accounts = await _uow.Set<FnAccount>().AsNoTracking().Where(a => lines.Select(l => l.AccountId).Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, ct);
            var centers = await _uow.Set<CostCenter>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            return new FnBudgetDto
            {
                Id = b.Id, Number = b.Number, LegalEntityId = b.LegalEntityId, FiscalYearId = b.FiscalYearId, Version = b.Version, Kind = b.Kind, Status = b.Status, CommitMode = b.CommitMode, Title = b.Title,
                ManagerApprovedByUserId = b.ManagerApprovedByUserId, CeoApprovedByUserId = b.CeoApprovedByUserId, Total = lines.Sum(l => l.Amount),
                Lines = lines.Select(l => new FnBudgetLineDto
                {
                    Id = l.Id, AccountCode = l.AccountId.HasValue ? accounts.GetValueOrDefault(l.AccountId.Value) : null, CostCenterCode = l.CostCenterId.HasValue ? centers.GetValueOrDefault(l.CostCenterId.Value) : null,
                    ContractId = l.ContractId, Period = l.Period, Amount = l.Amount, IsCapital = l.IsCapital, Note = l.Note,
                }).ToList(),
            };
        }

        public async Task<ResponseDto<List<FnBudgetDto>>> GetBudgetsAsync(int? fiscalYearId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<FnBudgetDto>>(NotAllowed);
            IQueryable<FnBudget> q = _uow.Set<FnBudget>().AsNoTracking();
            if (fiscalYearId.HasValue) q = q.Where(b => b.FiscalYearId == fiscalYearId.Value);
            var list = await q.OrderByDescending(b => b.Id).Take(100).ToListAsync(ct);
            var result = new List<FnBudgetDto>();
            foreach (FnBudget b in list) { FnBudgetDto d = await BudgetDtoAsync(b, ct); d.Lines = new List<FnBudgetLineDto>(); result.Add(d); }
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<FnBudgetDto>> GetBudgetAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnBudgetDto>(NotAllowed);
            FnBudget b = await _uow.Set<FnBudget>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return b == null ? ResponseDtoHelper.NotFound<FnBudgetDto>("بودجه پیدا نشد.") : ResponseDtoHelper.Ok(await BudgetDtoAsync(b, ct));
        }

        private async Task<(List<FnBudgetLine> Lines, string Error)> BuildBudgetLinesAsync(IEnumerable<FnBudgetLineDto> dtos, CancellationToken ct)
        {
            var result = new List<FnBudgetLine>();
            var accounts = await _uow.Set<FnAccount>().AsNoTracking().ToDictionaryAsync(a => a.Code, a => a.Id, ct);
            var centers = await _uow.Set<CostCenter>().AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.Id, ct);
            int row = 0;
            foreach (FnBudgetLineDto d in dtos ?? new List<FnBudgetLineDto>())
            {
                row++;
                string code = Clean(d.AccountCode) == null ? null : Digits(d.AccountCode);
                int? accountId = null, cc = null;
                if (code != null) { if (!accounts.TryGetValue(code, out int aid)) return (null, $"ردیف {row}: حساب {code} پیدا نشد."); accountId = aid; }
                string ccCode = Clean(d.CostCenterCode);
                if (ccCode != null) { if (!centers.TryGetValue(ccCode, out int cid)) return (null, $"ردیف {row}: مرکز هزینه {ccCode} پیدا نشد."); cc = cid; }
                if (!accountId.HasValue && !cc.HasValue && !d.ContractId.HasValue) return (null, $"ردیف {row}: حساب، مرکز هزینه یا قرارداد را وارد کنید.");
                if (d.Period < 1 || d.Period > 12) return (null, $"ردیف {row}: ماه باید بین ۱ و ۱۲ باشد.");
                if (d.Amount < 0) return (null, $"ردیف {row}: مبلغ منفی مجاز نیست.");
                if (d.ContractId.HasValue && !await _uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AnyAsync(c => c.Id == d.ContractId.Value, ct)) return (null, $"ردیف {row}: قرارداد پیدا نشد.");
                result.Add(new FnBudgetLine { AccountId = accountId, CostCenterId = cc, ContractId = d.ContractId, Period = d.Period, Amount = R(d.Amount), IsCapital = d.IsCapital, Note = Clean(d.Note) });
            }
            return (result, null);
        }

        public async Task<ResponseDto<FnBudgetDto>> SaveBudgetAsync(FnSaveBudgetDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnBudgetDto>(NotAllowed);
            if (dto == null) return Bad<FnBudgetDto>("اطلاعات ارسال‌شده کامل نیست.");
            var built = await BuildBudgetLinesAsync(dto.Lines, ct);
            if (built.Error != null) return Bad<FnBudgetDto>(built.Error);
            FnBudget b;
            if (dto.Id.HasValue)
            {
                b = await _uow.Set<FnBudget>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (b == null) return ResponseDtoHelper.NotFound<FnBudgetDto>("بودجه پیدا نشد.");
                if (b.Status != FnBudgetStatus.Draft) return Bad<FnBudgetDto>("فقط پیش‌نویس بودجه ویرایش می‌شود؛ برای تغییر، بازنگری بسازید.");
                _uow.Set<FnBudgetLine>().RemoveRange(await _uow.Set<FnBudgetLine>().Where(l => l.BudgetId == b.Id).ToListAsync(ct));
            }
            else
            {
                FnFiscalYear year = await _uow.Set<FnFiscalYear>().AsNoTracking().FirstOrDefaultAsync(y => y.Id == dto.FiscalYearId, ct);
                if (year == null) return ResponseDtoHelper.NotFound<FnBudgetDto>("سال مالی پیدا نشد.");
                int version = (await _uow.Set<FnBudget>().Where(x => x.FiscalYearId == year.Id).Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
                b = new FnBudget
                {
                    FiscalYearId = year.Id, LegalEntityId = year.LegalEntityId, Version = version, Status = FnBudgetStatus.Draft, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
                    Number = await NextNumberAsync("BUD", FinanceNumbers.Budget, year.StartDate, ct),
                };
                _uow.Set<FnBudget>().Add(b);
            }
            b.Title = Clean(dto.Title); b.Kind = dto.Kind; b.CommitMode = dto.CommitMode;
            foreach (FnBudgetLine l in built.Lines) { l.Budget = b; _uow.Set<FnBudgetLine>().Add(l); }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await BudgetDtoAsync(b, ct));
        }

        public async Task<ResponseDto<FnBudgetDto>> ImportAsync(FnBudgetImportDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnBudgetDto>(NotAllowed);
            if (dto == null || Clean(dto.Csv) == null) return Bad<FnBudgetDto>("متن فایل خالی است.");
            var lines = new List<FnBudgetLineDto>();
            string[] rows = dto.Csv.Replace("\r", "").Split('\n');
            int n = 0;
            foreach (string raw in rows)
            {
                n++;
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string[] c = raw.Split(',');
                if (n == 1 && c[0].Trim().Equals("accountCode", StringComparison.OrdinalIgnoreCase)) continue;
                if (c.Length < 5) return Bad<FnBudgetDto>($"سطر {n}: ستون‌ها کامل نیست (accountCode,costCenterCode,contractId,period,amount,isCapital,note).");
                if (!int.TryParse(c[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int period)) return Bad<FnBudgetDto>($"سطر {n}: ماه نامعتبر است.");
                if (!decimal.TryParse(c[4].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount)) return Bad<FnBudgetDto>($"سطر {n}: مبلغ نامعتبر است.");
                int? contract = int.TryParse(c[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cid) ? cid : (int?)null;
                lines.Add(new FnBudgetLineDto
                {
                    AccountCode = c[0].Trim(), CostCenterCode = c[1].Trim(), ContractId = contract, Period = period, Amount = amount,
                    IsCapital = c.Length > 5 && (c[5].Trim() == "1" || c[5].Trim().Equals("true", StringComparison.OrdinalIgnoreCase)), Note = c.Length > 6 ? c[6].Trim() : null,
                });
            }
            FnBudget b = await _uow.Set<FnBudget>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.BudgetId, ct);
            if (b == null) return ResponseDtoHelper.NotFound<FnBudgetDto>("بودجه پیدا نشد.");
            return await SaveBudgetAsync(new FnSaveBudgetDto { Id = b.Id, FiscalYearId = b.FiscalYearId, Title = b.Title, Kind = b.Kind, CommitMode = b.CommitMode, Lines = lines }, ct);
        }

        public async Task<ResponseDto<string>> ExportAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<string>(NotAllowed);
            FnBudget b = await _uow.Set<FnBudget>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b == null) return ResponseDtoHelper.NotFound<string>("بودجه پیدا نشد.");
            FnBudgetDto d = await BudgetDtoAsync(b, ct);
            var sb = new StringBuilder("accountCode,costCenterCode,contractId,period,amount,isCapital,note\n");
            foreach (FnBudgetLineDto l in d.Lines)
                sb.Append(l.AccountCode).Append(',').Append(l.CostCenterCode).Append(',').Append(l.ContractId).Append(',').Append(l.Period).Append(',')
                  .Append(l.Amount.ToString("0.####", CultureInfo.InvariantCulture)).Append(',').Append(l.IsCapital ? "1" : "0").Append(',').Append((l.Note ?? "").Replace(',', ' ')).Append('\n');
            return ResponseDtoHelper.Ok(sb.ToString());
        }

        public async Task<ResponseDto<FnBudgetDto>> ApproveAsync(int id, CancellationToken ct)
        {
            bool isManager = _user.IsInRole(FinanceRoles.Managers), isCeo = _user.IsInRole(FinanceRoles.Ceo);
            if (!isManager && !isCeo) return Bad<FnBudgetDto>(NotAllowed + " (تأیید بودجه با مدیر مالی و مدیرعامل است)");
            FnBudget b = await _uow.Set<FnBudget>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b == null) return ResponseDtoHelper.NotFound<FnBudgetDto>("بودجه پیدا نشد.");
            if (b.Status != FnBudgetStatus.Draft) return Bad<FnBudgetDto>("بودجه در وضعیت پیش‌نویس نیست.");
            if (!await _uow.Set<FnBudgetLine>().AnyAsync(l => l.BudgetId == id, ct)) return Bad<FnBudgetDto>("بودجه ردیف ندارد.");
            if (b.ManagerApprovedByUserId == Me || b.CeoApprovedByUserId == Me) return Bad<FnBudgetDto>("شما قبلاً این بودجه را تأیید کرده‌اید؛ تأیید دوم باید شخص دیگری باشد.");
            if (isManager && b.ManagerApprovedByUserId == null) { b.ManagerApprovedByUserId = Me; b.ManagerApprovedAt = DateTimeOffset.Now; }
            else if (isCeo && b.CeoApprovedByUserId == null) { b.CeoApprovedByUserId = Me; b.CeoApprovedAt = DateTimeOffset.Now; }
            else return Bad<FnBudgetDto>("تأیید این نقش قبلاً ثبت شده است.");
            if (b.ManagerApprovedByUserId.HasValue && b.CeoApprovedByUserId.HasValue)
            {
                b.Status = FnBudgetStatus.Approved;
                foreach (FnBudget old in await _uow.Set<FnBudget>().Where(x => x.FiscalYearId == b.FiscalYearId && x.Status == FnBudgetStatus.Approved && x.Id != b.Id).ToListAsync(ct))
                { old.Status = FnBudgetStatus.Superseded; old.SupersededByBudgetId = b.Id; }
            }
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await BudgetDtoAsync(b, ct));
        }

        public async Task<ResponseDto<FnBudgetDto>> ReviseAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<FnBudgetDto>(NotAllowed);
            FnBudget src = await _uow.Set<FnBudget>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (src == null) return ResponseDtoHelper.NotFound<FnBudgetDto>("بودجه پیدا نشد.");
            if (src.Status != FnBudgetStatus.Approved) return Bad<FnBudgetDto>("فقط از بودجه تأییدشده بازنگری ساخته می‌شود.");
            if (await _uow.Set<FnBudget>().AnyAsync(x => x.FiscalYearId == src.FiscalYearId && x.Status == FnBudgetStatus.Draft, ct)) return Bad<FnBudgetDto>("برای این سال یک پیش‌نویس بودجه باز است.");
            FnBudgetDto d = await BudgetDtoAsync(src, ct);
            return await SaveBudgetAsync(new FnSaveBudgetDto { FiscalYearId = src.FiscalYearId, Title = (src.Title ?? "") + " (بازنگری)", Kind = FnBudgetKind.Revision, CommitMode = src.CommitMode, Lines = d.Lines }, ct);
        }

        // ------------------------------------------------------------------------------------------ actuals and reports

        private async Task<List<ActualRow>> ActualsAsync(int fiscalYearId, int entityId, CancellationToken ct)
        {
            var rows = await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.Voucher.FiscalYearId == fiscalYearId && l.Voucher.Status != FnVoucherStatus.Draft
                    && l.Voucher.Type != FnVoucherType.Opening && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose)
                .GroupBy(l => new { l.Account.Code, l.Account.Nature, l.CostCenterId, l.ContractId, l.Voucher.Date })
                .Select(g => new { g.Key.Code, g.Key.Nature, g.Key.CostCenterId, g.Key.ContractId, g.Key.Date, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToListAsync(ct);
            return rows.Select(r => new ActualRow { Code = r.Code, Nature = r.Nature, CostCenterId = r.CostCenterId, ContractId = r.ContractId, Date = r.Date, Debit = r.Debit, Credit = r.Credit }).ToList();
        }

        private static int PeriodOf(List<FnPeriod> periods, DateTime date)
        {
            FnPeriod p = periods.FirstOrDefault(x => date.Date >= x.StartDate.Date && date.Date <= x.EndDate.Date);
            return p?.Number ?? 0;
        }

        public async Task<ResponseDto<FnBudgetReportDto>> ReportAsync(FnBudgetReportQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<FnBudgetReportDto>(NotAllowed);
            query ??= new FnBudgetReportQuery();
            FnBudget b = query.BudgetId.HasValue
                ? await _uow.Set<FnBudget>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.BudgetId.Value, ct)
                : await _uow.Set<FnBudget>().AsNoTracking().Where(x => x.Status == FnBudgetStatus.Approved && (!query.FiscalYearId.HasValue || x.FiscalYearId == query.FiscalYearId.Value)).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
            if (b == null) return ResponseDtoHelper.NotFound<FnBudgetReportDto>("بودجه پیدا نشد.");
            int from = query.FromPeriod ?? 1, to = query.ToPeriod ?? 12;
            if (from < 1 || to > 12 || from > to) return Bad<FnBudgetReportDto>("بازه ماه نامعتبر است.");
            string groupBy = (Clean(query.GroupBy) ?? "account").ToLowerInvariant();
            if (groupBy != "account" && groupBy != "costcenter" && groupBy != "contract") return Bad<FnBudgetReportDto>("گروه‌بندی باید account، costcenter یا contract باشد.");
            string method = (Clean(query.Forecast) ?? "Remaining");
            if (method != "Remaining" && method != "RunRate") return Bad<FnBudgetReportDto>("روش پیش‌بینی باید Remaining یا RunRate باشد.");

            var lines = await _uow.Set<FnBudgetLine>().AsNoTracking().Where(l => l.BudgetId == b.Id).ToListAsync(ct);
            var accountCodes = await _uow.Set<FnAccount>().AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code, ct);
            var centers = await _uow.Set<CostCenter>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c, ct);
            var periods = await _uow.Set<FnPeriod>().AsNoTracking().Where(p => p.FiscalYearId == b.FiscalYearId).OrderBy(p => p.Number).ToListAsync(ct);
            var keys = new Dictionary<string, BudgetKey>();
            foreach (FnBudgetLine l in lines)
            {
                var k = new BudgetKey { AccountCode = l.AccountId.HasValue ? accountCodes[l.AccountId.Value] : null, CostCenterId = l.CostCenterId, ContractId = l.ContractId };
                keys.TryAdd(k.Id, k);
            }
            var rowsBy = keys.Values.ToDictionary(k => k.Id, k => new FnBudgetReportRowDto());
            foreach (FnBudgetLine l in lines)
            {
                string kid = new BudgetKey { AccountCode = l.AccountId.HasValue ? accountCodes[l.AccountId.Value] : null, CostCenterId = l.CostCenterId, ContractId = l.ContractId }.Id;
                FnBudgetReportRowDto r = rowsBy[kid];
                r.FullYearBudget += l.Amount;
                if (l.Period >= from && l.Period <= to) r.Budget += l.Amount;
            }
            decimal unbudgeted = 0;
            var ytd = keys.Values.ToDictionary(k => k.Id, k => 0m);
            foreach (ActualRow a in await ActualsAsync(b.FiscalYearId, b.LegalEntityId, ct))
            {
                int p = PeriodOf(periods, a.Date);
                if (p == 0 || p > to) continue;
                BudgetKey best = keys.Values.Where(k => k.Matches(a.Code, a.CostCenterId, a.ContractId)).OrderByDescending(k => k.Score).FirstOrDefault();
                if (best == null) { if (p >= from && (a.Code.StartsWith("6") || a.Code.StartsWith("7") || a.Code.StartsWith("8"))) unbudgeted += a.Value; continue; }
                ytd[best.Id] += a.Value;
                if (p >= from) rowsBy[best.Id].Actual += a.Value;
            }
            // remaining budget after the report window (for the forecast)
            var future = lines.Where(l => l.Period > to).GroupBy(l => new BudgetKey { AccountCode = l.AccountId.HasValue ? accountCodes[l.AccountId.Value] : null, CostCenterId = l.CostCenterId, ContractId = l.ContractId }.Id)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
            var report = new FnBudgetReportDto { BudgetId = b.Id, FromPeriod = from, ToPeriod = to, GroupBy = groupBy, ForecastMethod = method };
            var grouped = new Dictionary<string, FnBudgetReportRowDto>();
            foreach (BudgetKey k in keys.Values)
            {
                FnBudgetReportRowDto r = rowsBy[k.Id];
                string gKey = groupBy == "account" ? (k.AccountCode ?? "-") : groupBy == "costcenter" ? (k.CostCenterId.HasValue ? centers[k.CostCenterId.Value].Code : "-") : (k.ContractId?.ToString(CultureInfo.InvariantCulture) ?? "-");
                if (!grouped.TryGetValue(gKey, out FnBudgetReportRowDto g))
                {
                    string title = groupBy == "account" ? accountCodes.Where(x => x.Value == k.AccountCode).Select(x => x.Value).FirstOrDefault() : groupBy == "costcenter" && k.CostCenterId.HasValue ? centers[k.CostCenterId.Value].Title : null;
                    g = new FnBudgetReportRowDto { Key = gKey, Title = title };
                    grouped[gKey] = g;
                }
                g.Budget += r.Budget; g.Actual += r.Actual; g.FullYearBudget += r.FullYearBudget;
                decimal forecast = method == "RunRate" ? R(ytd[k.Id] * 12m / to) : ytd[k.Id] + future.GetValueOrDefault(k.Id);
                g.Forecast += forecast;
            }
            foreach (FnBudgetReportRowDto g in grouped.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                g.Variance = g.Actual - g.Budget;
                g.VariancePercent = g.Budget == 0 ? 0 : Math.Round(g.Variance * 100m / g.Budget, 2, MidpointRounding.AwayFromZero);
                report.Rows.Add(g);
            }
            report.TotalBudget = report.Rows.Sum(r => r.Budget); report.TotalActual = report.Rows.Sum(r => r.Actual); report.TotalVariance = report.TotalActual - report.TotalBudget; report.UnbudgetedActual = unbudgeted;
            return ResponseDtoHelper.Ok(report);
        }

        // ------------------------------------------------------------------------------------------ commitment check

        public async Task<ResponseDto<FnCommitmentCheckDto>> CheckAsync(FnCommitmentCheckRequest request, CancellationToken ct)
        {
            if (!_user.IsInRole(global::Services.NasbNiro.Procurement.ProcurementRoles.Readers + ",AUDITOR")) return Bad<FnCommitmentCheckDto>(NotAllowed);
            if (request == null || request.Amount < 0) return Bad<FnCommitmentCheckDto>("مبلغ نامعتبر است.");
            DateTime date = (request.Date ?? DateTime.Today).Date;
            FnFiscalYear year = await _uow.Set<FnFiscalYear>().AsNoTracking().Where(y => y.StartDate <= date && y.EndDate >= date).OrderBy(y => y.LegalEntityId).FirstOrDefaultAsync(ct);
            var result = new FnCommitmentCheckDto { Amount = request.Amount, Allowed = true, Mode = FnCommitMode.Off };
            if (year == null) { result.Message = "سال مالی برای این تاریخ تعریف نشده است."; return ResponseDtoHelper.Ok(result); }
            FnBudget b = await _uow.Set<FnBudget>().AsNoTracking().FirstOrDefaultAsync(x => x.FiscalYearId == year.Id && x.Status == FnBudgetStatus.Approved, ct);
            if (b == null) { result.Message = "بودجه تأییدشده‌ای برای این سال نیست."; return ResponseDtoHelper.Ok(result); }
            result.Mode = b.CommitMode;
            string code = Clean(request.AccountCode) == null ? null : Digits(request.AccountCode);
            int? cc = null;
            if (Clean(request.CostCenterCode) != null) cc = await _uow.Set<CostCenter>().AsNoTracking().Where(c => c.Code == request.CostCenterCode.Trim()).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
            var accountCodes = await _uow.Set<FnAccount>().AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code, ct);
            var lines = await _uow.Set<FnBudgetLine>().AsNoTracking().Where(l => l.BudgetId == b.Id).ToListAsync(ct);
            var keyed = lines.Select(l => new { Line = l, Key = new BudgetKey { AccountCode = l.AccountId.HasValue ? accountCodes[l.AccountId.Value] : null, CostCenterId = l.CostCenterId, ContractId = l.ContractId } })
                .Where(x => x.Key.Matches(code, cc, request.ContractId) && (x.Key.AccountCode == null || code != null)).ToList();
            if (keyed.Count == 0) { result.Message = "برای این حساب/مرکز هزینه/پروژه ردیف بودجه نیست."; return ResponseDtoHelper.Ok(result); }
            BudgetKey best = keyed.OrderByDescending(x => x.Key.Score).First().Key;
            result.HasBudget = true;
            result.BudgetToDate = keyed.Where(x => x.Key.Id == best.Id).Sum(x => x.Line.Amount);
            var periods = await _uow.Set<FnPeriod>().AsNoTracking().Where(p => p.FiscalYearId == year.Id).ToListAsync(ct);
            var allKeys = lines.Select(l => new BudgetKey { AccountCode = l.AccountId.HasValue ? accountCodes[l.AccountId.Value] : null, CostCenterId = l.CostCenterId, ContractId = l.ContractId }).GroupBy(k => k.Id).Select(g => g.First()).ToList();
            foreach (ActualRow a in await ActualsAsync(year.Id, b.LegalEntityId, ct))
            {
                BudgetKey owner = allKeys.Where(k => k.Matches(a.Code, a.CostCenterId, a.ContractId)).OrderByDescending(k => k.Score).FirstOrDefault();
                if (owner != null && owner.Id == best.Id) result.ActualToDate += a.Value;
            }
            result.Remaining = result.BudgetToDate - result.ActualToDate - request.Amount;
            if (result.Remaining >= 0 || b.CommitMode == FnCommitMode.Off) { result.Message = result.Remaining >= 0 ? "در حدود بودجه است." : "کنترل بودجه خاموش است."; return ResponseDtoHelper.Ok(result); }
            string over = $"از بودجه باقی‌مانده {R(-result.Remaining):N0} ریال بیشتر است (بودجه {R(result.BudgetToDate):N0}، هزینه تاکنون {R(result.ActualToDate):N0}).";
            if (b.CommitMode == FnCommitMode.Warn) { result.Warning = true; result.Message = "هشدار: " + over; return ResponseDtoHelper.Ok(result); }
            if (_user.IsInRole(FinanceRoles.Ceo) && Clean(request.OverrideReason) != null) { result.Warning = true; result.Overridden = true; result.Message = "عبور با تصمیم مدیرعامل: " + request.OverrideReason.Trim() + " | " + over; return ResponseDtoHelper.Ok(result); }
            result.Allowed = false; result.Message = "مسدود: " + over + " عبور فقط با دلیل مدیرعامل ممکن است.";
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<FnCommitmentCheckDto>> CheckPurchaseRequestAsync(int purchaseRequestId, string overrideReason, CancellationToken ct)
        {
            if (!_user.IsInRole(global::Services.NasbNiro.Procurement.ProcurementRoles.Readers + ",AUDITOR")) return Bad<FnCommitmentCheckDto>(NotAllowed);
            var pr = await _uow.Set<Entities.Model.NasbNiro.Procurement.PurchaseRequest>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == purchaseRequestId, ct);
            if (pr == null) return ResponseDtoHelper.NotFound<FnCommitmentCheckDto>("درخواست خرید پیدا نشد.");
            return await CheckAsync(new FnCommitmentCheckRequest { CostCenterCode = pr.CostCenter, ContractId = pr.ContractId, Amount = pr.EstimatedAmount, Date = pr.RequestDate, OverrideReason = overrideReason }, ct);
        }
    }
}
