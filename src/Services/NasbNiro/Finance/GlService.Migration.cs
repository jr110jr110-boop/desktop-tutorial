using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    // Migration from Sepidar and the parallel run (F3 section 1). Finance manager only.
    public partial class GlService
    {
        private const int ImportRowLimit = 20000;

        private async Task<Dictionary<string, string>> SepidarMapAsync(CancellationToken ct) =>
            await _uow.Set<FnMigrationAccountMap>().AsNoTracking().Select(m => new { m.SepidarCode, m.Account.Code }).ToDictionaryAsync(x => x.SepidarCode, x => x.Code, ct);

        private static string MapCode(Dictionary<string, string> map, string code)
        {
            string c = Digits(Clean(code) ?? "");
            return map.TryGetValue(c, out string pmis) ? pmis : c;
        }

        private static bool TryNature(string text, out FnAccountNature nature)
        {
            nature = FnAccountNature.Either;
            string t = Clean(text);
            if (t == null) return false;
            if (t.Equals("Debit", StringComparison.OrdinalIgnoreCase) || t == "بدهکار") { nature = FnAccountNature.Debit; return true; }
            if (t.Equals("Credit", StringComparison.OrdinalIgnoreCase) || t == "بستانکار") { nature = FnAccountNature.Credit; return true; }
            if (t.Equals("Either", StringComparison.OrdinalIgnoreCase) || t == "هر دو") { nature = FnAccountNature.Either; return true; }
            return false;
        }

        // ------------------------------------------------------------------------------------------ accounts

        public async Task<ResponseDto<ImportReportDto>> ImportAccountsAsync(SepidarAccountImportDto dto, bool commit, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<ImportReportDto>(NotAllowed + " (مدیر مالی)");
            List<SepidarAccountRowDto> rows = (dto?.Rows ?? new List<SepidarAccountRowDto>()).Where(r => r != null).ToList();
            if (rows.Count == 0) return Bad<ImportReportDto>("فایل هیچ ردیفی ندارد.");
            if (rows.Count > ImportRowLimit) return Bad<ImportReportDto>("حداکثر ۲۰٬۰۰۰ ردیف در هر بار.");
            await FinanceSeed.EnsureAsync(_uow, ct);

            Dictionary<string, FnAccount> accounts = await _uow.Set<FnAccount>().ToDictionaryAsync(a => a.Code, ct);
            Dictionary<string, int> mapped = await _uow.Set<FnMigrationAccountMap>().AsNoTracking().ToDictionaryAsync(m => m.SepidarCode, m => m.AccountId, ct);
            var report = new ImportReportDto { DryRun = !commit, Total = rows.Count };
            var seen = new HashSet<string>();
            var created = new List<(FnAccount Account, string SepidarCode)>();
            var maps = new List<(string SepidarCode, FnAccount Account)>();
            int n = 0;
            foreach (SepidarAccountRowDto r in rows.OrderBy(x => Digits(Clean(x.Code) ?? "").Length))
            {
                n++;
                string code = Digits(Clean(r.Code) ?? "");
                var line = new ImportRowReportDto { Row = n, Code = code, Title = Clean(r.Title) };
                report.Rows.Add(line);
                if (code.Length == 0 || Clean(r.Title) == null) { line.Status = "Error"; line.Message = "کد یا عنوان حساب خالی است."; report.Errors++; continue; }
                if (!seen.Add(code)) { line.Status = "Error"; line.Message = "کد حساب در فایل تکراری است."; report.Errors++; continue; }
                if (r.Level < 1 || r.Level > 3) { line.Status = "Error"; line.Message = "سطح حساب باید ۱ تا ۳ باشد."; report.Errors++; continue; }

                if (mapped.ContainsKey(code))
                {
                    line.Status = "Mapped"; line.PmisCode = accounts.Values.FirstOrDefault(a => a.Id == mapped[code])?.Code; report.Mapped++; continue;
                }
                string pmisCode = Digits(Clean(r.PmisCode) ?? "");
                if (pmisCode.Length > 0)
                {
                    if (!accounts.TryGetValue(pmisCode, out FnAccount target)) { line.Status = "Error"; line.Message = $"حساب {pmisCode} در کدینگ PMIS نیست."; report.Errors++; continue; }
                    line.Status = "Mapped"; line.PmisCode = target.Code; report.Mapped++;
                    maps.Add((code, target));
                    continue;
                }
                if (accounts.TryGetValue(code, out FnAccount same))
                {
                    line.Status = "Existing"; line.PmisCode = same.Code; report.Existing++;
                    maps.Add((code, same));
                    continue;
                }
                // a new account: its parent is the longest shorter code that exists
                FnAccount parent = null;
                for (int len = code.Length - 1; len >= 1 && parent == null; len--) accounts.TryGetValue(code.Substring(0, len), out parent);
                if (parent == null) { line.Status = "Error"; line.Message = "حساب والد پیدا نشد؛ ابتدا حساب‌های سطح بالاتر را وارد کنید."; report.Errors++; continue; }
                if ((int)parent.Level >= r.Level) { line.Status = "Error"; line.Message = "سطح حساب باید پایین‌تر از سطح حساب والد باشد."; report.Errors++; continue; }
                FnAccountNature nature = TryNature(r.Nature, out FnAccountNature parsed) ? parsed : parent.Nature;
                var account = new FnAccount
                {
                    Code = code, Title = Clean(r.Title), Level = (FnAccountLevel)r.Level, ParentId = parent.Id, Nature = nature, Type = parent.Type, Temporary = parent.Temporary,
                    DetailTypes = "", Detail2Types = "", RequiresDetail = false, CurrencyAllowed = false, StatementLine = parent.StatementLine, IsActive = true,
                    SepidarCode = code, Jurisdiction = parent.Jurisdiction,
                };
                accounts[code] = account;
                created.Add((account, code));
                maps.Add((code, account));
                line.Status = "New"; line.PmisCode = code; report.New++;
            }
            report.Rows = report.Rows.OrderBy(x => x.Row).ToList();
            if (!commit || report.Errors > 0) return ResponseDtoHelper.Ok(report);

            foreach (var c in created) _uow.Set<FnAccount>().Add(c.Account);
            if (created.Count > 0) await _uow.SaveChangesAsync(ct);
            foreach (var m in maps)
            {
                if (mapped.ContainsKey(m.SepidarCode)) continue;
                if (Clean(m.Account.SepidarCode) == null) m.Account.SepidarCode = m.SepidarCode;
                _uow.Set<FnMigrationAccountMap>().Add(new FnMigrationAccountMap { SepidarCode = m.SepidarCode, AccountId = m.Account.Id, CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me });
            }
            await _uow.SaveChangesAsync(ct);
            report.Committed = true;
            return ResponseDtoHelper.Ok(report);
        }

        // ------------------------------------------------------------------------------------------ details

        private static FnDetailType? GuessDetailType(string title)
        {
            string t = title ?? "";
            if (t.Contains("بانک")) return FnDetailType.Bank;
            if (t.Contains("تنخواه")) return FnDetailType.PettyCash;
            if (t.Contains("صندوق")) return FnDetailType.CashBox;
            if (t.Contains("مرکز هزینه")) return FnDetailType.CostCenter;
            if (t.Contains("پروژه")) return FnDetailType.Project;
            if (t.Contains("تامین") || t.Contains("تأمین")) return FnDetailType.Supplier;
            if (t.Contains("مشتری") || t.Contains("خریدار")) return FnDetailType.Customer;
            if (t.Contains("پرسنل") || t.Contains("کارمند")) return FnDetailType.Employee;
            return null;
        }

        public async Task<ResponseDto<ImportReportDto>> ImportDetailsAsync(SepidarDetailImportDto dto, bool commit, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<ImportReportDto>(NotAllowed + " (مدیر مالی)");
            List<SepidarDetailRowDto> rows = (dto?.Rows ?? new List<SepidarDetailRowDto>()).Where(r => r != null).ToList();
            if (rows.Count == 0) return Bad<ImportReportDto>("فایل هیچ ردیفی ندارد.");
            if (rows.Count > ImportRowLimit) return Bad<ImportReportDto>("حداکثر ۲۰٬۰۰۰ ردیف در هر بار.");
            HashSet<string> existing = (await _uow.Set<FnDetailAccount>().AsNoTracking().Select(d => d.Code).ToListAsync(ct)).ToHashSet();
            var report = new ImportReportDto { DryRun = !commit, Total = rows.Count };
            var seen = new HashSet<string>();
            var toAdd = new List<FnDetailAccount>();
            int n = 0;
            foreach (SepidarDetailRowDto r in rows)
            {
                n++;
                string code = Clean(r.Code);
                var line = new ImportRowReportDto { Row = n, Code = code, Title = Clean(r.Title) };
                report.Rows.Add(line);
                if (code == null || line.Title == null) { line.Status = "Error"; line.Message = "کد یا عنوان تفصیلی خالی است."; report.Errors++; continue; }
                if (!seen.Add(code)) { line.Status = "Error"; line.Message = "کد تفصیلی در فایل تکراری است."; report.Errors++; continue; }
                if (existing.Contains(code)) { line.Status = "Existing"; report.Existing++; continue; }
                FnDetailType type;
                if (Clean(r.Type) != null)
                {
                    if (!Enum.TryParse(r.Type.Trim(), true, out type) || !Enum.IsDefined(typeof(FnDetailType), type)) { line.Status = "Error"; line.Message = "نوع تفصیلی نامعتبر است."; report.Errors++; continue; }
                    line.Status = "New"; report.New++;
                }
                else
                {
                    FnDetailType? guess = GuessDetailType(line.Title);
                    type = guess ?? FnDetailType.Other;
                    if (guess.HasValue) { line.Status = "New"; report.New++; }
                    else { line.Status = "Review"; line.Message = "نوع تفصیلی حدس زده نشد (سایر)؛ در صورت لزوم نوع را اصلاح کنید."; report.Review++; }
                }
                line.GuessedType = type.ToString();
                toAdd.Add(new FnDetailAccount { Code = code, Title = line.Title, Type = type, IsActive = true });
            }
            if (!commit || report.Errors > 0) return ResponseDtoHelper.Ok(report);
            foreach (FnDetailAccount d in toAdd) _uow.Set<FnDetailAccount>().Add(d);
            if (toAdd.Count > 0) await _uow.SaveChangesAsync(ct);
            report.Committed = true;
            return ResponseDtoHelper.Ok(report);
        }

        // ------------------------------------------------------------------------------------------ opening balances

        public async Task<ResponseDto<OpeningReportDto>> ImportOpeningBalancesAsync(SepidarOpeningImportDto dto, bool commit, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<OpeningReportDto>(NotAllowed + " (مدیر مالی)");
            List<SepidarOpeningRowDto> rows = (dto?.Rows ?? new List<SepidarOpeningRowDto>()).Where(r => r != null).ToList();
            if (rows.Count == 0) return Bad<OpeningReportDto>("فایل هیچ ردیفی ندارد.");
            if (rows.Count > ImportRowLimit) return Bad<OpeningReportDto>("حداکثر ۲۰٬۰۰۰ ردیف در هر بار.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            Dictionary<string, string> map = await SepidarMapAsync(ct);
            HashSet<string> accountCodes = (await _uow.Set<FnAccount>().AsNoTracking().Select(a => a.Code).ToListAsync(ct)).ToHashSet();
            HashSet<string> detailCodes = (await _uow.Set<FnDetailAccount>().AsNoTracking().Select(d => d.Code).ToListAsync(ct)).ToHashSet();

            var report = new OpeningReportDto { DryRun = !commit, Rows = rows.Count };
            var mappedRows = new List<OpeningRowDto>();
            foreach (SepidarOpeningRowDto r in rows)
            {
                string code = MapCode(map, r.AccountCode);
                if (code.Length == 0 || !accountCodes.Contains(code))
                {
                    string shown = Clean(r.AccountCode) ?? "(خالی)";
                    if (!report.UnknownAccounts.Contains(shown)) report.UnknownAccounts.Add(shown);
                }
                string detail = Clean(r.DetailCode);
                if (detail != null && !detailCodes.Contains(detail) && !report.UnknownDetails.Contains(detail)) report.UnknownDetails.Add(detail);
                if (r.Debit < 0 || r.Credit < 0) report.Errors.Add($"مبلغ منفی برای حساب {r.AccountCode}.");
                report.TotalDebit += R(r.Debit);
                report.TotalCredit += R(r.Credit);
                mappedRows.Add(new OpeningRowDto { AccountCode = code, DetailCode = detail, Debit = R(r.Debit), Credit = R(r.Credit) });
            }
            report.Difference = report.TotalDebit - report.TotalCredit;
            report.Balanced = report.Difference == 0 && report.TotalDebit > 0;
            if (!report.Balanced) report.Errors.Add($"تراز متوازن نیست: جمع بدهکار {report.TotalDebit:0} و جمع بستانکار {report.TotalCredit:0}.");

            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            FnFiscalYear year = dto.FiscalYearId.HasValue
                ? await _uow.Set<FnFiscalYear>().AsNoTracking().FirstOrDefaultAsync(y => y.Id == dto.FiscalYearId.Value, ct)
                : await FinanceSeed.EnsureEntityYearAsync(_uow, await _uow.Set<FnLegalEntity>().FirstOrDefaultAsync(x => x.Id == entityId, ct), DateTime.Today, ct);
            if (year == null) return ResponseDtoHelper.NotFound<OpeningReportDto>("سال مالی پیدا نشد.");
            report.AlreadyHasOpening = await _uow.Set<FnVoucher>().AnyAsync(v => v.FiscalYearId == year.Id && v.Type == FnVoucherType.Opening, ct);
            if (report.AlreadyHasOpening) report.Errors.Add("برای این سال قبلاً سند افتتاحیه ثبت شده است.");

            if (!commit || report.UnknownAccounts.Count > 0 || report.UnknownDetails.Count > 0 || report.Errors.Count > 0) return ResponseDtoHelper.Ok(report);
            var result = await ImportOpeningCoreAsync(new OpeningImportDto { FiscalYearId = year.Id, Description = dto.Description, Rows = mappedRows }, false, ct);
            if (!result.Success) { report.Errors.Add(result.Message); return ResponseDtoHelper.Ok(report); }
            report.Committed = true;
            report.VoucherId = result.Data.Id;
            return ResponseDtoHelper.Ok(report);
        }

        // ------------------------------------------------------------------------------------------ imported years and history

        public async Task<ResponseDto<FiscalYearDto>> CreateImportedYearAsync(CreateImportedYearDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<FiscalYearDto>(NotAllowed + " (مدیر مالی)");
            if (dto == null || dto.StartDate == default || dto.EndDate == default || dto.EndDate.Date < dto.StartDate.Date) return Bad<FiscalYearDto>("تاریخ شروع و پایان سال را درست وارد کنید.");
            if (dto.Year < 1300 || dto.Year > 1600) return Bad<FiscalYearDto>("سال مالی خارج از محدوده مجاز است.");
            if ((dto.EndDate.Date - dto.StartDate.Date).TotalDays > 400) return Bad<FiscalYearDto>("طول سال مالی بیش از حد مجاز است.");
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            if (await _uow.Set<FnFiscalYear>().AnyAsync(y => y.LegalEntityId == entityId && y.Year == dto.Year, ct)) return Bad<FiscalYearDto>("این سال مالی قبلاً تعریف شده است.");
            if (await _uow.Set<FnFiscalYear>().AnyAsync(y => y.LegalEntityId == entityId && y.StartDate <= dto.EndDate.Date && y.EndDate >= dto.StartDate.Date, ct))
                return Bad<FiscalYearDto>("بازه سال با یک سال مالی دیگر همپوشانی دارد.");
            FnLegalEntity entity = await _uow.Set<FnLegalEntity>().FirstOrDefaultAsync(x => x.Id == entityId, ct);
            IFiscalCalendar cal = FiscalCalendars.Current;
            var fy = new FnFiscalYear
            {
                Year = dto.Year, Title = Clean(dto.Title) ?? dto.Year.ToString(CultureInfo.InvariantCulture), StartDate = dto.StartDate.Date, EndDate = dto.EndDate.Date,
                Status = FnYearStatus.Closed, Jurisdiction = entity?.Jurisdiction ?? "IR", LegalEntityId = entityId, IsImported = true, ClosedAt = DateTimeOffset.Now, Periods = new List<FnPeriod>(),
            };
            int number = 1;
            for (int idx = PersianCal.Index(fy.StartDate); idx <= PersianCal.Index(fy.EndDate); idx++)
            {
                DateTime s = PersianCal.StartOfIndex(idx), e = PersianCal.EndOfIndex(idx);
                fy.Periods.Add(new FnPeriod
                {
                    Number = number++, Title = cal.PeriodTitle(PersianCal.YearOfIndex(idx), PersianCal.MonthOfIndex(idx)),
                    StartDate = s < fy.StartDate ? fy.StartDate : s, EndDate = e > fy.EndDate ? fy.EndDate : e, Status = FnPeriodStatus.Closed, LockedAt = DateTimeOffset.Now,
                });
            }
            _uow.Set<FnFiscalYear>().Add(fy);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await LoadYearsAsync(fy.Id, entityId, ct)).First());
        }

        public async Task<ResponseDto<HistoricalReportDto>> ImportHistoricalAsync(HistoricalImportDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<HistoricalReportDto>(NotAllowed + " (مدیر مالی)");
            List<HistoricalVoucherRowDto> rows = (dto?.Rows ?? new List<HistoricalVoucherRowDto>()).Where(r => r != null).ToList();
            if (rows.Count == 0) return Bad<HistoricalReportDto>("فایل هیچ ردیفی ندارد.");
            if (rows.Count > 50000) return Bad<HistoricalReportDto>("حداکثر ۵۰٬۰۰۰ ردیف در هر بار.");
            FnFiscalYear year = await _uow.Set<FnFiscalYear>().AsNoTracking().FirstOrDefaultAsync(y => y.Id == dto.FiscalYearId, ct);
            if (year == null) return ResponseDtoHelper.NotFound<HistoricalReportDto>("سال مالی پیدا نشد.");
            if (!year.IsImported) return Bad<HistoricalReportDto>("فقط در سال واردشده از سپیدار می‌توان سند تاریخی وارد کرد.");
            Dictionary<string, string> map = await SepidarMapAsync(ct);
            HashSet<int> existingNumbers = (await _uow.Set<FnVoucher>().AsNoTracking().Where(v => v.FiscalYearId == year.Id && v.Number != null).Select(v => v.Number.Value).ToListAsync(ct)).ToHashSet();

            var report = new HistoricalReportDto { DryRun = dto.DryRun };
            var prepared = new List<(int Number, DateTime Date, string Description, List<FnVoucherLine> Lines)>();
            foreach (var group in rows.GroupBy(r => r.Number).OrderBy(g => g.Key))
            {
                int number = group.Key;
                if (number <= 0) { report.Errors.Add("شماره سند باید مثبت باشد."); continue; }
                if (existingNumbers.Contains(number)) { report.Errors.Add($"سند {number}: این شماره قبلاً در سال ثبت شده است."); continue; }
                DateTime date = group.First().Date.Date;
                if (group.Any(g => g.Date.Date != date)) { report.Errors.Add($"سند {number}: ردیف‌ها تاریخ‌های متفاوت دارند."); continue; }
                if (date < year.StartDate || date > year.EndDate) { report.Errors.Add($"سند {number}: تاریخ خارج از سال مالی است."); continue; }
                var lines = new List<SaveVoucherLineDto>();
                foreach (HistoricalVoucherRowDto r in group)
                {
                    string code = MapCode(map, r.AccountCode);
                    if (r.Debit > 0 && r.Credit > 0)
                    {
                        lines.Add(new SaveVoucherLineDto { AccountCode = code, Detail1Code = r.DetailCode, Debit = r.Debit });
                        lines.Add(new SaveVoucherLineDto { AccountCode = code, Detail1Code = r.DetailCode, Credit = r.Credit });
                    }
                    else if (r.Debit != 0 || r.Credit != 0)
                        lines.Add(new SaveVoucherLineDto { AccountCode = code, Detail1Code = r.DetailCode, Debit = r.Debit, Credit = r.Credit, Description = Clean(group.First().Description) });
                }
                var built = await BuildLinesAsync(lines, false, ct);
                if (built.Error != null) { report.Errors.Add($"سند {number}: {built.Error}"); continue; }
                prepared.Add((number, date, Clean(group.First().Description), built.Lines));
            }
            report.Vouchers = prepared.Count;
            report.Lines = prepared.Sum(p => p.Lines.Count);
            report.TotalDebit = prepared.Sum(p => p.Lines.Sum(l => l.Debit));
            report.TotalCredit = prepared.Sum(p => p.Lines.Sum(l => l.Credit));
            if (dto.DryRun || report.Errors.Count > 0) return ResponseDtoHelper.Ok(report);

            foreach (var p in prepared)
            {
                _uow.Set<FnVoucher>().Add(new FnVoucher
                {
                    FiscalYearId = year.Id, Number = p.Number, TemporaryNumber = p.Number, Date = p.Date, Type = FnVoucherType.Normal, Status = FnVoucherStatus.Final,
                    Description = p.Description, SourceType = "SepidarHistory", IsImported = true, TotalAmount = p.Lines.Sum(l => l.Debit),
                    CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, CheckedByUserId = Me, CheckedAt = DateTimeOffset.Now, FinalizedByUserId = Me, FinalizedAt = DateTimeOffset.Now,
                    Lines = p.Lines,
                });
            }
            await _uow.SaveChangesAsync(ct);
            report.Committed = true;
            return ResponseDtoHelper.Ok(report);
        }

        // ------------------------------------------------------------------------------------------ parallel run

        private async Task<ParallelRunDto> RunDtoAsync(FnParallelRun run, bool withDifferences, CancellationToken ct)
        {
            string title = await _uow.Set<FnPeriod>().AsNoTracking().Where(p => p.Id == run.PeriodId).Select(p => p.Title).FirstOrDefaultAsync(ct);
            var dto = new ParallelRunDto
            {
                Id = run.Id, LegalEntityId = run.LegalEntityId, PeriodId = run.PeriodId, PeriodTitle = title, Rows = run.Rows, Matched = run.Matched, PmisOnly = run.PmisOnly,
                SepidarOnly = run.SepidarOnly, Differs = run.Differs, Unexplained = run.Unexplained, UploadedAt = run.UploadedAt, SignedOff = run.SignedOffAt.HasValue,
                SignedOffByUserId = run.SignedOffByUserId, SignedOffAt = run.SignedOffAt, SignOffNote = run.SignOffNote, UnexplainedAtSignOff = run.UnexplainedAtSignOff,
            };
            if (withDifferences)
            {
                dto.Differences = await _uow.Set<FnParallelDifference>().AsNoTracking().Where(d => d.RunId == run.Id).OrderBy(d => d.Id)
                    .Select(d => new ParallelDifferenceDto
                    {
                        Id = d.Id, Kind = d.Kind, AccountCode = d.AccountCode, AccountTitle = d.AccountTitle, DetailCode = d.DetailCode,
                        PmisDebit = d.PmisDebit, PmisCredit = d.PmisCredit, SepidarDebit = d.SepidarDebit, SepidarCredit = d.SepidarCredit, Explanation = d.Explanation,
                    }).ToListAsync(ct);
            }
            return dto;
        }

        public async Task<ResponseDto<ParallelRunDto>> CompareAsync(ParallelCompareDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<ParallelRunDto>(NotAllowed);
            if (dto == null) return Bad<ParallelRunDto>("اطلاعات ارسال‌شده کامل نیست.");
            if ((dto.Rows?.Count ?? 0) > ImportRowLimit) return Bad<ParallelRunDto>("حداکثر ۲۰٬۰۰۰ ردیف در هر بار.");
            FnPeriod p = await _uow.Set<FnPeriod>().AsNoTracking().Include(x => x.FiscalYear).FirstOrDefaultAsync(x => x.Id == dto.PeriodId, ct);
            if (p == null) return ResponseDtoHelper.NotFound<ParallelRunDto>("دوره پیدا نشد.");
            Dictionary<string, string> map = await SepidarMapAsync(ct);

            // PMIS side: the period's movement per (account, first detail)
            IQueryable<FnVoucherLine> lq = _uow.Set<FnVoucherLine>().AsNoTracking().Where(l => l.Voucher.FiscalYearId == p.FiscalYearId && l.Voucher.Date >= p.StartDate && l.Voucher.Date <= p.EndDate);
            if (!dto.IncludeDraft) lq = lq.Where(l => l.Voucher.Status == FnVoucherStatus.Final);
            var pmisRows = await lq.GroupBy(l => new { l.AccountId, l.Detail1Id })
                .Select(g => new { g.Key.AccountId, g.Key.Detail1Id, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToListAsync(ct);
            Dictionary<int, FnAccount> accounts = await _uow.Set<FnAccount>().AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
            List<int> detailIds = pmisRows.Where(r => r.Detail1Id.HasValue).Select(r => r.Detail1Id.Value).Distinct().ToList();
            Dictionary<int, string> detailCodes = await _uow.Set<FnDetailAccount>().AsNoTracking().Where(d => detailIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Code, ct);

            var pmis = new Dictionary<(string, string), (decimal Debit, decimal Credit, string Title)>();
            foreach (var r in pmisRows)
            {
                FnAccount a = accounts[r.AccountId];
                var key = (a.Code, r.Detail1Id.HasValue ? detailCodes[r.Detail1Id.Value] : "");
                pmis[key] = (r.Debit, r.Credit, a.Title);
            }
            var sep = new Dictionary<(string, string), (decimal Debit, decimal Credit)>();
            foreach (ParallelRowDto r in dto.Rows ?? new List<ParallelRowDto>())
            {
                if (r == null) continue;
                var key = (MapCode(map, r.AccountCode), Clean(r.DetailCode) ?? "");
                sep.TryGetValue(key, out var cur);
                sep[key] = (cur.Debit + R(r.Debit), cur.Credit + R(r.Credit));
            }

            var run = new FnParallelRun
            {
                LegalEntityId = p.FiscalYear.LegalEntityId, PeriodId = p.Id, IncludeDraft = dto.IncludeDraft, UploadedByUserId = Me, UploadedAt = DateTimeOffset.Now,
                Differences = new List<FnParallelDifference>(),
            };
            foreach (var key in pmis.Keys.Union(sep.Keys).OrderBy(k => k.Item1, StringComparer.Ordinal).ThenBy(k => k.Item2, StringComparer.Ordinal))
            {
                bool inP = pmis.TryGetValue(key, out var pv), inS = sep.TryGetValue(key, out var sv);
                run.Rows++;
                FnDiffKind kind;
                if (inP && inS) kind = pv.Debit == sv.Debit && pv.Credit == sv.Credit ? FnDiffKind.Matched : FnDiffKind.AmountDiffers;
                else kind = inP ? FnDiffKind.PmisOnly : FnDiffKind.SepidarOnly;
                switch (kind)
                {
                    case FnDiffKind.Matched: run.Matched++; continue;
                    case FnDiffKind.PmisOnly: run.PmisOnly++; break;
                    case FnDiffKind.SepidarOnly: run.SepidarOnly++; break;
                    default: run.Differs++; break;
                }
                string title = inP ? pv.Title : accounts.Values.FirstOrDefault(a => a.Code == key.Item1)?.Title;
                run.Differences.Add(new FnParallelDifference
                {
                    Kind = kind, AccountCode = key.Item1, AccountTitle = title, DetailCode = key.Item2 == "" ? null : key.Item2,
                    PmisDebit = inP ? pv.Debit : 0, PmisCredit = inP ? pv.Credit : 0, SepidarDebit = inS ? sv.Debit : 0, SepidarCredit = inS ? sv.Credit : 0,
                });
            }
            run.Unexplained = run.Differences.Count;
            _uow.Set<FnParallelRun>().Add(run);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await RunDtoAsync(run, true, ct));
        }

        public async Task<ResponseDto<List<ParallelRunDto>>> GetRunsAsync(int? periodId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<List<ParallelRunDto>>(NotAllowed);
            IQueryable<FnParallelRun> q = _uow.Set<FnParallelRun>().AsNoTracking();
            if (periodId.HasValue) q = q.Where(r => r.PeriodId == periodId.Value);
            List<FnParallelRun> runs = await q.OrderByDescending(r => r.Id).Take(200).ToListAsync(ct);
            var list = new List<ParallelRunDto>();
            foreach (FnParallelRun r in runs) list.Add(await RunDtoAsync(r, false, ct));
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<ParallelRunDto>> GetRunAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<ParallelRunDto>(NotAllowed);
            FnParallelRun run = await _uow.Set<FnParallelRun>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run == null) return ResponseDtoHelper.NotFound<ParallelRunDto>("مقایسه پیدا نشد.");
            return ResponseDtoHelper.Ok(await RunDtoAsync(run, true, ct));
        }

        public async Task<ResponseDto<ParallelRunDto>> ExplainAsync(int runId, ExplainDifferenceDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Accountants)) return Bad<ParallelRunDto>(NotAllowed);
            if (dto == null || Clean(dto.Explanation) == null) return Bad<ParallelRunDto>("توضیح مغایرت را بنویسید.");
            FnParallelRun run = await _uow.Set<FnParallelRun>().FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run == null) return ResponseDtoHelper.NotFound<ParallelRunDto>("مقایسه پیدا نشد.");
            if (run.SignedOffAt.HasValue) return Bad<ParallelRunDto>("مقایسه تاییدشده تغییر نمی‌کند.");
            FnParallelDifference d = await _uow.Set<FnParallelDifference>().FirstOrDefaultAsync(x => x.Id == dto.DifferenceId && x.RunId == runId, ct);
            if (d == null) return ResponseDtoHelper.NotFound<ParallelRunDto>("مغایرت پیدا نشد.");
            d.Explanation = Clean(dto.Explanation); d.ExplainedByUserId = Me; d.ExplainedAt = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            run.Unexplained = await _uow.Set<FnParallelDifference>().CountAsync(x => x.RunId == runId && x.Explanation == null, ct);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await RunDtoAsync(run, true, ct));
        }

        public async Task<ResponseDto<ParallelRunDto>> SignOffAsync(int runId, SignOffDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Managers)) return Bad<ParallelRunDto>(NotAllowed + " (مدیر مالی)");
            FnParallelRun run = await _uow.Set<FnParallelRun>().FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run == null) return ResponseDtoHelper.NotFound<ParallelRunDto>("مقایسه پیدا نشد.");
            if (run.SignedOffAt.HasValue) return Bad<ParallelRunDto>("این مقایسه قبلاً تایید شده است.");
            run.Unexplained = await _uow.Set<FnParallelDifference>().CountAsync(x => x.RunId == runId && x.Explanation == null, ct);
            run.SignedOffByUserId = Me; run.SignedOffAt = DateTimeOffset.Now; run.SignOffNote = Clean(dto?.Note); run.UnexplainedAtSignOff = run.Unexplained;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await RunDtoAsync(run, true, ct));
        }

        public async Task<ResponseDto<ParallelStatusDto>> ParallelStatusAsync(int? legalEntityId, CancellationToken ct)
        {
            if (!_user.IsInRole(FinanceRoles.Readers)) return Bad<ParallelStatusDto>(NotAllowed);
            await FinanceSeed.EnsureAsync(_uow, ct);
            int entityId = await EntityScope.ResolveAsync(_uow, legalEntityId, Me, ct);
            string setting = await SettingAsync(FinanceSeed.SettingParallelMonths, "3", ct);
            int required = int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rm) && rm > 0 ? rm : 3;
            List<FnParallelRun> runs = await _uow.Set<FnParallelRun>().AsNoTracking().Where(r => r.LegalEntityId == entityId).ToListAsync(ct);
            List<int> periodIds = runs.Select(r => r.PeriodId).Distinct().ToList();
            Dictionary<int, FnPeriod> periods = await _uow.Set<FnPeriod>().AsNoTracking().Where(p => periodIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            // the run that counts for a period is its latest one
            var latest = runs.GroupBy(r => r.PeriodId).Select(g => g.OrderByDescending(r => r.Id).First()).OrderByDescending(r => periods[r.PeriodId].StartDate).ToList();
            var status = new ParallelStatusDto { RequiredMonths = required };
            int consecutive = 0;
            bool streak = true;
            int? previousIndex = null;
            foreach (FnParallelRun r in latest.Take(24))
            {
                FnPeriod p = periods[r.PeriodId];
                bool clean = r.SignedOffAt.HasValue && r.Unexplained == 0 && r.UnexplainedAtSignOff == 0;
                int idx = PersianCal.Index(p.StartDate);
                if (streak && (!clean || (previousIndex.HasValue && previousIndex.Value - idx != 1))) streak = false;
                if (streak) consecutive++;
                previousIndex = idx;
                status.Periods.Add(new ParallelPeriodStatusDto { PeriodId = p.Id, PeriodTitle = p.Title, RunId = r.Id, SignedOff = r.SignedOffAt.HasValue, Unexplained = r.Unexplained, Clean = clean });
            }
            status.ConsecutiveCleanPeriods = consecutive;
            status.GoLiveReady = consecutive >= required;
            return ResponseDtoHelper.Ok(status);
        }
    }
}
