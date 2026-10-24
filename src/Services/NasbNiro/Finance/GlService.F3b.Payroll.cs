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
    // Finance release F3b: termination settlement, yearly bonus run and the annual payroll tax reconciliation.
    // Personal salary data: only the payroll roles (PayrollReaders / PayrollUsers) reach these endpoints; the auditor does not.
    // The vouchers carry totals only, never a person.
    public partial class GlService
    {
        private const string PayrollNotAllowed = "شما مجوز دسترسی به اطلاعات حقوق و دستمزد را ندارید";
        private bool CanReadPayroll => _user.IsInRole(FinanceRoles.PayrollReaders);
        private bool CanWritePayroll => _user.IsInRole(FinanceRoles.PayrollUsers);

        private static string RateText(IEnumerable<FnPayrollRateTable> rows, string key, int year)
        {
            FnPayrollRateTable r = rows.Where(x => x.Key == key && (x.Year == 0 || x.Year == year)).OrderByDescending(x => x.Year).FirstOrDefault();
            if (r == null) return key + ": ندارد";
            return $"{key} = {r.Value.ToString("0.####", CultureInfo.InvariantCulture)} ({(Clean(r.Source) ?? "بدون منبع")}{(r.Verify ? "، تایید مالی لازم است" : "")})";
        }

        /// <summary>Booked balance (credit minus debit, never below zero) of the account of a role up to a date: provisions that are already in the ledger.</summary>
        private async Task<decimal> ProvisionBalanceAsync(string role, string fallbackCode, DateTime date, CancellationToken ct)
        {
            FnAccount a = await RoleAccountAsync(role, fallbackCode, ct);
            FnFiscalYear year = await LedgerYearAsync(date, ct);
            if (a == null || year == null) return 0;
            var s = await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.AccountId == a.Id && l.Voucher.FiscalYearId == year.Id && l.Voucher.Status != FnVoucherStatus.Draft && l.Voucher.Date <= date && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose)
                .GroupBy(l => 1).Select(g => new { C = g.Sum(x => x.Credit), D = g.Sum(x => x.Debit) }).FirstOrDefaultAsync(ct);
            return s == null ? 0 : Math.Max(0, s.C - s.D);
        }

        /// <summary>The fiscal year that holds a date: the ledger balances of F3b reports are read inside it (the opening voucher of a year carries the earlier years).</summary>
        private async Task<FnFiscalYear> LedgerYearAsync(DateTime day, CancellationToken ct)
        {
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            return await _uow.Set<FnFiscalYear>().AsNoTracking().Where(y => y.LegalEntityId == entityId && y.StartDate <= day && y.EndDate >= day).FirstOrDefaultAsync(ct);
        }

        private static string EmployeeName(FnEmployee e) => ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim();

        // ================================================================================== termination settlement

        private sealed class TermCalc
        {
            public int Months; public decimal Years, MonthlyBase, Daily, DaysPerYear, LeaveDays, NoticeDays, Severance, Leave, Notice, Total;
            public List<string> Sources = new List<string>();
            public string Error;
        }

        private async Task<TermCalc> TerminationCalcAsync(FnEmployee e, DateTime termination, decimal? leaveDays, decimal noticeDays, decimal? monthlyBase, CancellationToken ct)
        {
            var c = new TermCalc();
            if (termination.Date < e.HireDate.Date) { c.Error = "تاریخ پایان کار قبل از تاریخ استخدام است."; return c; }
            if (noticeDays < 0 || (leaveDays.HasValue && leaveDays.Value < 0) || (monthlyBase.HasValue && monthlyBase.Value < 0)) { c.Error = "روزها و مبلغ نمی‌توانند منفی باشند."; return c; }
            int year = FinanceSeed.PersianYearOf(termination);
            List<FnPayrollRateTable> rateRows = await _uow.Set<FnPayrollRateTable>().AsNoTracking().Where(r => r.Year == 0 || r.Year == year).ToListAsync(ct);
            var rates = new PayrollRates(rateRows, year);
            if (!rates.Has("TerminationSeveranceDaysPerYear")) { c.Error = "نرخ «TerminationSeveranceDaysPerYear» (روز حق سنوات برای هر سال) در جدول نرخ‌ها وارد نشده است."; return c; }
            c.DaysPerYear = rates.Get("TerminationSeveranceDaysPerYear");
            c.Sources.Add(RateText(rateRows, "TerminationSeveranceDaysPerYear", year));
            c.Months = PersianCal.FullMonths(e.HireDate.Date, termination.Date.AddDays(1));
            c.Years = Math.Round(c.Months / 12m, 4, MidpointRounding.AwayFromZero);

            if (monthlyBase.HasValue) { c.MonthlyBase = R(monthlyBase.Value); c.Sources.Add("پایه ماهانه: ورودی کاربر"); }
            else
            {
                HashSet<string> baseCodes = (await _uow.Set<FnPayrollItem>().AsNoTracking().Where(i => i.SeveranceBase && i.Kind == FnPayrollItemKind.Earning).Select(i => i.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                FnPayslip slip = await _uow.Set<FnPayslip>().AsNoTracking().Include(p => p.Lines).Include(p => p.Run)
                    .Where(p => p.EmployeeId == e.Id && (p.Run.Status == FnPayrollStatus.Posted || p.Run.Status == FnPayrollStatus.Paid) && p.Run.Month >= 1 && p.Run.Month <= 12)
                    .OrderByDescending(p => p.Run.Year).ThenByDescending(p => p.Run.Month).AsSplitQuery().FirstOrDefaultAsync(ct);
                if (slip != null && baseCodes.Count > 0)
                {
                    decimal sum = slip.Lines.Where(l => l.Kind == FnPayrollItemKind.Earning && baseCodes.Contains(l.ItemCode)).Sum(l => l.Amount);
                    // a part month is scaled to the 30-day payroll basis
                    c.MonthlyBase = slip.DaysPaid > 0 && slip.DaysPaid != 30m ? R(sum * 30m / slip.DaysPaid) : R(sum);
                    c.Sources.Add($"پایه ماهانه: اقلام مبنای سنوات آخرین فیش حقوقی ثبت‌شده ({slip.Run.Year}/{slip.Run.Month:00})");
                }
                else
                {
                    c.MonthlyBase = R(e.DailyWage * 30m);
                    c.Sources.Add("پایه ماهانه: مزد روزانه قرارداد × ۳۰ (فیش حقوقی ثبت‌شده‌ای نیست)");
                }
            }
            if (c.MonthlyBase <= 0) { c.Error = "پایه ماهانه محاسبه سنوات صفر است؛ پایه را وارد کنید یا مزد روزانه کارمند را تکمیل کنید."; return c; }
            c.Daily = R(c.MonthlyBase / 30m);
            if (leaveDays.HasValue) { c.LeaveDays = leaveDays.Value; c.Sources.Add("روزهای مرخصی استفاده‌نشده: ورودی کاربر"); }
            else
            {
                FnLeaveBalance b = await _uow.Set<FnLeaveBalance>().AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == e.Id && x.Year == year, ct);
                c.LeaveDays = b == null ? 0 : Math.Max(0, b.CarriedForward + b.Entitled - b.Used);
                c.Sources.Add(b == null ? "روزهای مرخصی استفاده‌نشده: مانده مرخصی سال ثبت نشده است (صفر)" : "روزهای مرخصی استفاده‌نشده: مانده مرخصی سال پایان کار");
            }
            c.NoticeDays = noticeDays;
            c.Severance = R(c.MonthlyBase * c.DaysPerYear * c.Months / 360m);
            c.Leave = R(c.MonthlyBase * c.LeaveDays / 30m);
            c.Notice = R(c.MonthlyBase * c.NoticeDays / 30m);
            c.Total = c.Severance + c.Leave + c.Notice;
            c.Sources.Add("مالیات و بیمه مبالغ مرخصی و اخطار در این محاسبه نیامده است؛ حسابدار حقوق آن را جداگانه تعیین می‌کند.");
            return c;
        }

        private FnTerminationDto TerminationDto(FnTermination t, FnEmployee e, List<string> sources) => new FnTerminationDto
        {
            Id = t.Id, Number = t.Number, EmployeeId = t.EmployeeId, EmployeeName = EmployeeName(e), PersonnelCode = e.PersonnelCode, HireDate = t.HireDate, TerminationDate = t.TerminationDate,
            YearsOfService = t.YearsOfService, MonthlyBase = t.MonthlyBase, DailyWage = t.DailyWage, SeveranceDaysPerYear = t.SeveranceDaysPerYear, UnusedLeaveDays = t.UnusedLeaveDays, NoticeDays = t.NoticeDays,
            SeverancePay = t.SeverancePay, LeavePay = t.LeavePay, NoticePay = t.NoticePay, Total = t.Total, FromSeveranceProvision = t.FromSeveranceProvision, FromLeaveProvision = t.FromLeaveProvision,
            Status = t.Status, VoucherId = t.VoucherId, Note = t.Note, Sources = sources ?? new List<string>(),
        };

        public async Task<ResponseDto<List<FnTerminationDto>>> GetTerminationsAsync(CancellationToken ct)
        {
            if (!CanReadPayroll) return Bad<List<FnTerminationDto>>(PayrollNotAllowed);
            var rows = await _uow.Set<FnTermination>().AsNoTracking().Include(t => t.Employee).OrderByDescending(t => t.Id).Take(300).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(t => TerminationDto(t, t.Employee, null)).ToList());
        }

        public async Task<ResponseDto<FnTerminationDto>> SaveTerminationAsync(FnTerminationInputDto dto, CancellationToken ct)
        {
            if (!CanWritePayroll) return Bad<FnTerminationDto>(PayrollNotAllowed);
            if (dto == null || dto.TerminationDate == default) return Bad<FnTerminationDto>("کارمند و تاریخ پایان کار را وارد کنید.");
            FnEmployee e = await _uow.Set<FnEmployee>().FirstOrDefaultAsync(x => x.Id == dto.EmployeeId, ct);
            if (e == null) return ResponseDtoHelper.NotFound<FnTerminationDto>("کارمند پیدا نشد.");
            FnTermination t = null;
            if (dto.Id.HasValue)
            {
                t = await _uow.Set<FnTermination>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (t == null) return ResponseDtoHelper.NotFound<FnTerminationDto>("تسویه‌حساب پیدا نشد.");
                if (t.Status != FnSettlementStatus.Draft) return Bad<FnTerminationDto>("فقط پیش‌نویس ویرایش می‌شود.");
                if (t.EmployeeId != dto.EmployeeId) return Bad<FnTerminationDto>("کارمند تسویه‌حساب تغییر نمی‌کند.");
            }
            int self = t?.Id ?? 0;
            if (await _uow.Set<FnTermination>().AnyAsync(x => x.EmployeeId == dto.EmployeeId && x.Status != FnSettlementStatus.Cancelled && x.Id != self, ct)) return Bad<FnTerminationDto>("برای این کارمند تسویه‌حساب دیگری ثبت شده است.");
            TermCalc c = await TerminationCalcAsync(e, dto.TerminationDate, dto.UnusedLeaveDays, dto.NoticeDays, dto.MonthlyBase, ct);
            if (c.Error != null) return Bad<FnTerminationDto>(c.Error);
            if (t == null)
            {
                t = new FnTermination { Number = await NextNumberAsync("TRM", FinanceNumbers.Termination, dto.TerminationDate, ct), EmployeeId = e.Id, PreparedByUserId = Me, PreparedAt = DateTimeOffset.Now, Status = FnSettlementStatus.Draft };
                _uow.Set<FnTermination>().Add(t);
            }
            t.EditorUserIds = WithEditor(t.EditorUserIds, Me); t.PreparedByUserId = Me; t.PreparedAt = DateTimeOffset.Now;
            ApplyTermination(t, e, dto.TerminationDate, c, Clean(dto.Note));
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(TerminationDto(t, e, c.Sources));
        }

        private static void ApplyTermination(FnTermination t, FnEmployee e, DateTime termination, TermCalc c, string note)
        {
            t.HireDate = e.HireDate.Date; t.TerminationDate = termination.Date; t.YearsOfService = c.Years; t.MonthlyBase = c.MonthlyBase; t.DailyWage = c.Daily; t.SeveranceDaysPerYear = c.DaysPerYear;
            t.UnusedLeaveDays = c.LeaveDays; t.NoticeDays = c.NoticeDays; t.SeverancePay = c.Severance; t.LeavePay = c.Leave; t.NoticePay = c.Notice; t.Total = c.Total; t.Note = note;
        }

        public async Task<ResponseDto<FnTerminationDto>> TerminationActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (!CanWritePayroll) return Bad<FnTerminationDto>(PayrollNotAllowed);
            if (dto == null || Clean(dto.Action) == null) return Bad<FnTerminationDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnTermination t = await _uow.Set<FnTermination>().Include(x => x.Employee).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (t == null) return ResponseDtoHelper.NotFound<FnTerminationDto>("تسویه‌حساب پیدا نشد.");
            if (t.Status != FnSettlementStatus.Draft) return Bad<FnTerminationDto>("فقط پیش‌نویس تایید یا باطل می‌شود.");
            string action = dto.Action.Trim().ToLowerInvariant();
            if (action == "cancel") { t.Status = FnSettlementStatus.Cancelled; await _uow.SaveChangesAsync(ct); return ResponseDtoHelper.Ok(TerminationDto(t, t.Employee, null)); }
            if (action != "approve") return Bad<FnTerminationDto>("عمل نامعتبر است.");
            if (Touched(t.PreparedByUserId, t.EditorUserIds, Me)) return Bad<FnTerminationDto>(SameUser);
            // recomputed now: payslips, leave and the rate table may have changed since the draft
            TermCalc c = await TerminationCalcAsync(t.Employee, t.TerminationDate, t.UnusedLeaveDays, t.NoticeDays, t.MonthlyBase, ct);
            if (c.Error != null) return Bad<FnTerminationDto>(c.Error);
            if (c.Total != t.Total) return Bad<FnTerminationDto>("مبالغ پس از تهیه پیش‌نویس تغییر کرده است؛ پیش‌نویس را دوباره ذخیره کنید.");
            DateTime date = (dto.Date ?? t.TerminationDate).Date;
            decimal severanceBalance = await ProvisionBalanceAsync(AccountRoles.SeveranceProvision, "40201", date, ct), leaveBalance = await ProvisionBalanceAsync(AccountRoles.LeaveProvision, "30403", date, ct);
            decimal fromSeverance = Math.Min(t.SeverancePay, severanceBalance), fromLeave = Math.Min(t.LeavePay, leaveBalance);
            FnAccount sevProv = await RoleAccountAsync(AccountRoles.SeveranceProvision, "40201", ct), sevExp = await RoleAccountAsync(AccountRoles.SeveranceExpense, "80103", ct),
                leaveProv = await RoleAccountAsync(AccountRoles.LeaveProvision, "30403", ct), admin = await RoleAccountAsync(AccountRoles.PayrollExpenseAdmin, "80101", ct),
                payable = await RoleAccountAsync(AccountRoles.PayrollPayable, "30201", ct);
            if (sevProv == null || sevExp == null || leaveProv == null || admin == null || payable == null) return Bad<FnTerminationDto>("حساب‌های ذخیره، هزینه یا حقوق پرداختنی پیدا نشد.");
            string text = $"تسویه‌حساب پایان خدمت {t.Number}";
            var lines = new List<AutoLine>
            {
                new AutoLine { Account = sevProv, Debit = fromSeverance, Description = text + " - از محل ذخیره سنوات" },
                new AutoLine { Account = sevExp, Debit = t.SeverancePay - fromSeverance, Description = text + " - هزینه سنوات" },
                new AutoLine { Account = leaveProv, Debit = fromLeave, Description = text + " - از محل ذخیره مرخصی" },
                new AutoLine { Account = admin, Debit = t.LeavePay - fromLeave + t.NoticePay, Description = text + " - مرخصی و اخطار" },
                new AutoLine { Account = payable, Credit = t.Total, Description = text },
            };
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            var v = await AddAutoVoucherAsync(entityId, date, text, "Termination", t.Number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
            if (v.Error != null) return Bad<FnTerminationDto>(v.Error);
            t.Voucher = v.Voucher; t.FromSeveranceProvision = fromSeverance; t.FromLeaveProvision = fromLeave; t.Status = FnSettlementStatus.Posted; t.ApprovedByUserId = Me; t.ApprovedAt = DateTimeOffset.Now;
            if (t.Employee.EndDate == null || t.Employee.EndDate.Value.Date > t.TerminationDate) t.Employee.EndDate = t.TerminationDate;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(TerminationDto(t, t.Employee, c.Sources));
        }

        // ================================================================================== yearly bonus (عیدی)

        private sealed class BonusCalc
        {
            public decimal BonusDays, CapDays, MinimumDailyWage, Cap;
            public List<FnBonusLine> Lines = new List<FnBonusLine>();
            public List<string> Sources = new List<string>();
            public string Error;
        }

        private async Task<BonusCalc> BonusCalcAsync(int year, List<int> employeeIds, CancellationToken ct)
        {
            var c = new BonusCalc();
            List<FnPayrollRateTable> rateRows = await _uow.Set<FnPayrollRateTable>().AsNoTracking().Where(r => r.Year == 0 || r.Year == year).ToListAsync(ct);
            var rates = new PayrollRates(rateRows, year);
            foreach (string key in new[] { "BonusDaysPerYear", "BonusCapDaysOfMinimumWage", "MinimumDailyWage" })
                if (!rates.Has(key)) { c.Error = $"نرخ «{key}» برای سال {year} در جدول نرخ‌ها وارد نشده است."; return c; }
            List<FnTaxBracket> brackets = await _uow.Set<FnTaxBracket>().AsNoTracking().Where(b => b.Year == year).OrderBy(b => b.FromAmount).ToListAsync(ct);
            if (brackets.Count == 0) { c.Error = $"جدول پلکان مالیات حقوق سال {year} وارد نشده است."; return c; }
            c.BonusDays = rates.Get("BonusDaysPerYear"); c.CapDays = rates.Get("BonusCapDaysOfMinimumWage"); c.MinimumDailyWage = rates.Get("MinimumDailyWage"); c.Cap = R(c.CapDays * c.MinimumDailyWage);
            foreach (string key in new[] { "BonusDaysPerYear", "BonusCapDaysOfMinimumWage", "MinimumDailyWage" }) c.Sources.Add(RateText(rateRows, key, year));
            c.Sources.Add("مالیات: تفاوت مالیات حقوق ماهانه (پلکان سال) با و بدون بخش مشمول عیدی در ماه آخرین فیش حقوقی؛ حق بیمه بر عیدی محاسبه نشده است.");

            DateTime yStart = FiscalCalendars.Current.YearStart(year).Date, yEnd = FiscalCalendars.Current.YearEnd(year).Date;
            int yearDays = (int)(yEnd - yStart).TotalDays + 1;
            IQueryable<FnEmployee> q = _uow.Set<FnEmployee>().AsNoTracking().Where(e => e.HireDate <= yEnd && (e.EndDate == null || e.EndDate >= yStart));
            if (employeeIds != null && employeeIds.Count > 0) q = q.Where(e => employeeIds.Contains(e.Id));
            List<FnEmployee> employees = await q.OrderBy(e => e.PersonnelCode).ThenBy(e => e.Id).ToListAsync(ct);
            List<int> ids = employees.Select(e => e.Id).ToList();
            var slips = await _uow.Set<FnPayslip>().AsNoTracking().Where(p => ids.Contains(p.EmployeeId) && p.Run.Year == year && (p.Run.Status == FnPayrollStatus.Posted || p.Run.Status == FnPayrollStatus.Paid))
                .Select(p => new { p.EmployeeId, p.Run.Month, p.TaxableTotal }).ToListAsync(ct);
            foreach (FnEmployee e in employees)
            {
                DateTime from = e.HireDate.Date > yStart ? e.HireDate.Date : yStart, to = e.EndDate.HasValue && e.EndDate.Value.Date < yEnd ? e.EndDate.Value.Date : yEnd;
                int employed = (int)(to - from).TotalDays + 1;
                if (employed <= 0 || e.DailyWage <= 0) continue;
                decimal gross = R(e.DailyWage * c.BonusDays * employed / yearDays);
                var last = slips.Where(s => s.EmployeeId == e.Id).OrderByDescending(s => s.Month).FirstOrDefault();
                decimal monthly = last?.TaxableTotal ?? 0;
                decimal taxableBonus = Math.Max(0, gross - c.Cap);
                decimal tax = R(PayrollEngine.BracketTax((monthly + taxableBonus) * 12m, brackets) / 12m - PayrollEngine.BracketTax(monthly * 12m, brackets) / 12m);
                c.Lines.Add(new FnBonusLine { EmployeeId = e.Id, DailyWage = e.DailyWage, EmployedDays = employed, YearDays = yearDays, Gross = gross, MonthlyTaxable = monthly, Tax = tax, Net = gross - tax });
            }
            return c;
        }

        private async Task<FnBonusRunDto> BonusDtoAsync(FnBonusRun r, List<string> sources, CancellationToken ct)
        {
            List<int> ids = r.Lines.Select(l => l.EmployeeId).ToList();
            Dictionary<int, FnEmployee> emp = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
            return new FnBonusRunDto
            {
                Id = r.Id, Number = r.Number, Year = r.Year, PayDate = r.PayDate, Status = r.Status, BonusDays = r.BonusDays, CapDays = r.CapDays, MinimumDailyWage = r.MinimumDailyWage, TotalGross = r.TotalGross,
                TotalTax = r.TotalTax, TotalNet = r.TotalNet, FromProvision = r.FromProvision, ExpensedNow = r.ExpensedNow, VoucherId = r.VoucherId, Note = r.Note, Sources = sources ?? new List<string>(),
                Lines = r.Lines.OrderBy(l => emp.TryGetValue(l.EmployeeId, out FnEmployee e) ? e.PersonnelCode : "").ThenBy(l => l.EmployeeId).Select(l => new FnBonusLineDto
                {
                    EmployeeId = l.EmployeeId, EmployeeName = emp.TryGetValue(l.EmployeeId, out FnEmployee e2) ? EmployeeName(e2) : null, DailyWage = l.DailyWage, EmployedDays = l.EmployedDays, YearDays = l.YearDays,
                    Gross = l.Gross, MonthlyTaxable = l.MonthlyTaxable, Tax = l.Tax, Net = l.Net,
                }).ToList(),
            };
        }

        public async Task<ResponseDto<List<FnBonusRunDto>>> GetBonusRunsAsync(CancellationToken ct)
        {
            if (!CanReadPayroll) return Bad<List<FnBonusRunDto>>(PayrollNotAllowed);
            var rows = await _uow.Set<FnBonusRun>().AsNoTracking().Include(r => r.Lines).OrderByDescending(r => r.Id).Take(50).AsSplitQuery().ToListAsync(ct);
            var list = new List<FnBonusRunDto>();
            foreach (FnBonusRun r in rows) list.Add(await BonusDtoAsync(r, null, ct));
            return ResponseDtoHelper.Ok(list);
        }

        private static void ApplyBonus(FnBonusRun r, BonusCalc c)
        {
            r.BonusDays = c.BonusDays; r.CapDays = c.CapDays; r.MinimumDailyWage = c.MinimumDailyWage;
            r.TotalGross = c.Lines.Sum(l => l.Gross); r.TotalTax = c.Lines.Sum(l => l.Tax); r.TotalNet = c.Lines.Sum(l => l.Net);
        }

        public async Task<ResponseDto<FnBonusRunDto>> SaveBonusRunAsync(FnBonusInputDto dto, CancellationToken ct)
        {
            if (!CanWritePayroll) return Bad<FnBonusRunDto>(PayrollNotAllowed);
            if (dto == null || dto.Year < 1300 || dto.Year > 1600 || dto.PayDate == default) return Bad<FnBonusRunDto>("سال و تاریخ پرداخت را وارد کنید.");
            FnBonusRun r = null;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<FnBonusRun>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id.Value, ct);
                if (r == null) return ResponseDtoHelper.NotFound<FnBonusRunDto>("محاسبه عیدی پیدا نشد.");
                if (r.Status != FnSettlementStatus.Draft) return Bad<FnBonusRunDto>("فقط پیش‌نویس ویرایش می‌شود.");
            }
            int self = r?.Id ?? 0;
            if (await _uow.Set<FnBonusRun>().AnyAsync(x => x.Year == dto.Year && x.Status != FnSettlementStatus.Cancelled && x.Id != self, ct)) return Bad<FnBonusRunDto>($"برای سال {dto.Year} محاسبه عیدی دیگری ثبت شده است.");
            BonusCalc c = await BonusCalcAsync(dto.Year, dto.EmployeeIds, ct);
            if (c.Error != null) return Bad<FnBonusRunDto>(c.Error);
            if (c.Lines.Count == 0) return Bad<FnBonusRunDto>("کارمندی با مزد روزانه برای این سال پیدا نشد.");
            if (r == null)
            {
                r = new FnBonusRun { Number = await NextNumberAsync("BON", FinanceNumbers.BonusRun, dto.PayDate, ct), Status = FnSettlementStatus.Draft, Lines = new List<FnBonusLine>() };
                _uow.Set<FnBonusRun>().Add(r);
            }
            else
            {
                _uow.Set<FnBonusLine>().RemoveRange(r.Lines.ToList());
                await _uow.SaveChangesAsync(ct);   // the old lines go first: the (run, employee) pair is unique
            }
            r.Year = dto.Year; r.PayDate = dto.PayDate.Date; r.Note = Clean(dto.Note); r.EditorUserIds = WithEditor(r.EditorUserIds, Me); r.PreparedByUserId = Me; r.PreparedAt = DateTimeOffset.Now;
            ApplyBonus(r, c);
            r.Lines = c.Lines;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await BonusDtoAsync(r, c.Sources, ct));
        }

        public async Task<ResponseDto<FnBonusRunDto>> BonusRunActionAsync(DocActionDto dto, CancellationToken ct)
        {
            if (!CanWritePayroll) return Bad<FnBonusRunDto>(PayrollNotAllowed);
            if (dto == null || Clean(dto.Action) == null) return Bad<FnBonusRunDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnBonusRun r = await _uow.Set<FnBonusRun>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (r == null) return ResponseDtoHelper.NotFound<FnBonusRunDto>("محاسبه عیدی پیدا نشد.");
            if (r.Status != FnSettlementStatus.Draft) return Bad<FnBonusRunDto>("فقط پیش‌نویس تایید یا باطل می‌شود.");
            string action = dto.Action.Trim().ToLowerInvariant();
            if (action == "cancel") { r.Status = FnSettlementStatus.Cancelled; await _uow.SaveChangesAsync(ct); return ResponseDtoHelper.Ok(await BonusDtoAsync(r, null, ct)); }
            if (action != "approve") return Bad<FnBonusRunDto>("عمل نامعتبر است.");
            if (Touched(r.PreparedByUserId, r.EditorUserIds, Me)) return Bad<FnBonusRunDto>(SameUser);
            BonusCalc c = await BonusCalcAsync(r.Year, r.Lines.Select(l => l.EmployeeId).ToList(), ct);
            if (c.Error != null) return Bad<FnBonusRunDto>(c.Error);
            if (c.Lines.Sum(l => l.Net) != r.TotalNet || c.Lines.Sum(l => l.Tax) != r.TotalTax || c.Lines.Count != r.Lines.Count) return Bad<FnBonusRunDto>("مبالغ یا کارکنان پس از تهیه پیش‌نویس تغییر کرده است؛ پیش‌نویس را دوباره ذخیره کنید.");
            DateTime date = (dto.Date ?? r.PayDate).Date;
            decimal balance = await ProvisionBalanceAsync(AccountRoles.BonusProvision, "30402", date, ct);
            decimal fromProvision = Math.Min(r.TotalGross, balance);
            FnAccount prov = await RoleAccountAsync(AccountRoles.BonusProvision, "30402", ct), admin = await RoleAccountAsync(AccountRoles.PayrollExpenseAdmin, "80101", ct),
                tax = await RoleAccountAsync(AccountRoles.PayrollTaxPayable, "30203", ct), payable = await RoleAccountAsync(AccountRoles.PayrollPayable, "30201", ct);
            if (prov == null || admin == null || tax == null || payable == null) return Bad<FnBonusRunDto>("حساب‌های ذخیره عیدی، هزینه حقوق یا پرداختنی‌ها پیدا نشد.");
            string text = $"عیدی سال {r.Year} ({r.Number})";
            var lines = new List<AutoLine>
            {
                new AutoLine { Account = prov, Debit = fromProvision, Description = text + " - از محل ذخیره" },
                new AutoLine { Account = admin, Debit = r.TotalGross - fromProvision, Description = text + " - هزینه دوره" },
                new AutoLine { Account = tax, Credit = r.TotalTax, Description = text + " - مالیات" },
                new AutoLine { Account = payable, Credit = r.TotalNet, Description = text + " - خالص پرداختنی" },
            };
            int entityId = await EntityScope.ResolveAsync(_uow, null, Me, ct);
            var v = await AddAutoVoucherAsync(entityId, date, text, "BonusRun", r.Number, lines, FnVoucherType.Auto, null, ct, FnVoucherStatus.Checked, true);
            if (v.Error != null) return Bad<FnBonusRunDto>(v.Error);
            r.Voucher = v.Voucher; r.FromProvision = fromProvision; r.ExpensedNow = r.TotalGross - fromProvision; r.Status = FnSettlementStatus.Posted; r.ApprovedByUserId = Me; r.ApprovedAt = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await BonusDtoAsync(r, c.Sources, ct));
        }

        // ================================================================================== annual payroll tax reconciliation

        public async Task<ResponseDto<FnTaxReconciliationDto>> PayrollTaxReconciliationAsync(int year, CancellationToken ct)
        {
            if (!CanReadPayroll) return Bad<FnTaxReconciliationDto>(PayrollNotAllowed);
            if (year < 1300 || year > 1600) return Bad<FnTaxReconciliationDto>("سال نامعتبر است.");
            List<FnTaxBracket> brackets = await _uow.Set<FnTaxBracket>().AsNoTracking().Where(b => b.Year == year).OrderBy(b => b.FromAmount).ToListAsync(ct);
            if (brackets.Count == 0) return Bad<FnTaxReconciliationDto>($"جدول پلکان مالیات حقوق سال {year} وارد نشده است.");
            var slips = await _uow.Set<FnPayslip>().AsNoTracking().Where(p => p.Run.Year == year && (p.Run.Status == FnPayrollStatus.Posted || p.Run.Status == FnPayrollStatus.Paid))
                .GroupBy(p => p.EmployeeId).Select(g => new { EmployeeId = g.Key, Count = g.Count(), Taxable = g.Sum(p => p.TaxableTotal), Tax = g.Sum(p => p.Tax) }).ToListAsync(ct);
            var bonus = await _uow.Set<FnBonusLine>().AsNoTracking().Where(l => l.Run.Year == year && l.Run.Status == FnSettlementStatus.Posted)
                .Select(l => new { l.EmployeeId, l.Gross, l.Tax, Cap = l.Run.CapDays * l.Run.MinimumDailyWage }).ToListAsync(ct);
            List<int> ids = slips.Select(s => s.EmployeeId).Union(bonus.Select(b => b.EmployeeId)).Distinct().ToList();
            Dictionary<int, FnEmployee> emp = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
            var dto = new FnTaxReconciliationDto { Year = year };
            foreach (int id in ids)
            {
                var s = slips.FirstOrDefault(x => x.EmployeeId == id);
                decimal taxable = s?.Taxable ?? 0, withheld = s?.Tax ?? 0;
                foreach (var b in bonus.Where(x => x.EmployeeId == id)) { taxable += Math.Max(0, b.Gross - R(b.Cap)); withheld += b.Tax; }
                decimal recomputed = R(PayrollEngine.BracketTax(taxable, brackets));
                emp.TryGetValue(id, out FnEmployee e);
                dto.Rows.Add(new FnTaxReconRowDto
                {
                    EmployeeId = id, EmployeeName = e == null ? null : EmployeeName(e), PersonnelCode = e?.PersonnelCode, Payslips = s?.Count ?? 0, Taxable = taxable, TaxWithheld = withheld,
                    TaxRecomputed = recomputed, Difference = recomputed - withheld,
                });
            }
            dto.Rows = dto.Rows.OrderBy(r => r.PersonnelCode, StringComparer.Ordinal).ThenBy(r => r.EmployeeId).ToList();
            dto.TotalTaxable = dto.Rows.Sum(r => r.Taxable); dto.TotalWithheld = dto.Rows.Sum(r => r.TaxWithheld); dto.TotalRecomputed = dto.Rows.Sum(r => r.TaxRecomputed); dto.TotalDifference = dto.Rows.Sum(r => r.Difference);
            FnAccount taxAccount = await RoleAccountAsync(AccountRoles.PayrollTaxPayable, "30203", ct);
            DateTime from = FiscalCalendars.Current.YearStart(year).Date, to = FiscalCalendars.Current.YearEnd(year).Date;
            dto.LedgerWithheld = taxAccount == null ? 0 : await _uow.Set<FnVoucherLine>().AsNoTracking()
                .Where(l => l.AccountId == taxAccount.Id && l.Voucher.Status != FnVoucherStatus.Draft && l.Voucher.Type != FnVoucherType.Closing && l.Voucher.Type != FnVoucherType.Opening && l.Voucher.Type != FnVoucherType.YearEndTemporaryClose
                    && l.Voucher.Type != FnVoucherType.Reversal && l.Voucher.Date >= from && l.Voucher.Date <= to).SumAsync(l => (decimal?)l.Credit, ct) ?? 0;
            dto.TiesToLedger = dto.LedgerWithheld == dto.TotalWithheld;
            dto.Note = "مالیات بازمحاسبه‌شده = پلکان سال روی جمع حقوق مشمول مالیات (و بخش مشمول عیدی) کل سال هر کارمند. تفاوت مثبت: مالیات بیشتری باید پرداخت شود؛ منفی: بیش‌کسر شده است. جمع کسرشده باید با اعتبار حساب مالیات حقوق پرداختنی دفتر کل در همان سال برابر باشد.";
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
