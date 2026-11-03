using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.Pagination;
using OfficeDtos = ViewModels.NasbNiro.OfficeDtos;
using OfficeEntities = Entities.Model.NasbNiro.Office;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    // Release R1f: the maker-checker controls on the payroll inputs.
    //   - employee master: a new employee, and a change of the guarded fields (daily wage, exemption flags, children, hard-and-harmful, IBAN), is a request that a second PAYROLL / FINANCEMANAGER user decides;
    //     an approved wage change writes the wage history (the run reads the wage effective at its month)
    //   - loans and advances: a request approved by the finance manager (never the creator)
    //   - corrections of a sheet that the HR close locked: payroll proposes, HR (another person) decides
    //   - opening leave balances: payroll enters, the finance manager approves
    public partial class PayrollMasterService
    {
        // ================================================================================== guarded employee fields

        internal static GuardedFieldsDto GuardedOf(FnEmployee e) => new GuardedFieldsDto
        {
            DailyWage = e.DailyWage, ExemptionFlags = e.ExemptionFlags, ChildrenCount = e.ChildrenCount, HardAndHarmful = e.HardAndHarmful, BankIban = e.BankIban,
        };

        private static string NormFlags(string flags) => string.IsNullOrWhiteSpace(flags) ? "" : string.Join(",", flags.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));

        internal static bool SameGuarded(GuardedFieldsDto a, GuardedFieldsDto b) =>
            a.DailyWage == b.DailyWage && NormFlags(a.ExemptionFlags) == NormFlags(b.ExemptionFlags) && a.ChildrenCount == b.ChildrenCount && a.HardAndHarmful == b.HardAndHarmful
            && (a.BankIban ?? "") == (b.BankIban ?? "");

        internal static string GuardedJson(GuardedFieldsDto g) => JsonSerializer.Serialize(g);

        internal static GuardedFieldsDto GuardedFrom(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new GuardedFieldsDto();
            try { return JsonSerializer.Deserialize<GuardedFieldsDto>(json) ?? new GuardedFieldsDto(); }
            catch (JsonException) { return new GuardedFieldsDto(); }
        }

        /// <summary>A wage cannot start in (or before) a month that already has an approved payslip of the employee: that payslip was calculated with the earlier wage.</summary>
        private async Task<string> WageMonthGuardAsync(int employeeId, DateTime effectiveFrom, CancellationToken ct)
        {
            var last = await _uow.Set<FnPayslip>().AsNoTracking().Where(p => p.EmployeeId == employeeId && p.Run.Status >= FnPayrollStatus.Approved)
                .OrderByDescending(p => p.Run.Year).ThenByDescending(p => p.Run.Month).Select(p => new { p.Run.Year, p.Run.Month }).FirstOrDefaultAsync(ct);
            if (last == null) return null;
            if (PersianCal.Index(PersianCal.Year(effectiveFrom), PersianCal.Month(effectiveFrom)) <= PersianCal.Index(last.Year, last.Month))
                return $"حقوق ماه {last.Year}/{last.Month:00} این کارمند تایید شده است؛ مزد جدید باید از ماه بعد از آن اعمال شود.";
            return null;
        }

        // ================================================================================== employee change requests

        private async Task<List<EmployeeChangeDto>> ChangeDtosAsync(List<FnEmployeeChange> rows, CancellationToken ct)
        {
            List<int> ids = rows.Select(c => c.EmployeeId).Distinct().ToList();
            Dictionary<int, FnEmployee> employees = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(c => (int?)c.ProposedByUserId).Concat(rows.Select(c => c.DecidedByUserId)), ct);
            return rows.Select(c =>
            {
                employees.TryGetValue(c.EmployeeId, out FnEmployee e);
                return new EmployeeChangeDto
                {
                    Id = c.Id, EmployeeId = c.EmployeeId, PersonnelCode = e?.PersonnelCode, EmployeeName = e == null ? null : (e.FirstName + " " + e.LastName).Trim(), Kind = c.Kind.ToString(),
                    Status = c.Status.ToString(), Current = e == null ? null : GuardedOf(e), Proposed = GuardedFrom(c.ProposedJson), WageEffectiveFrom = c.WageEffectiveFrom, Reason = c.Reason,
                    ProposedByName = names.Name(c.ProposedByUserId), ProposedAt = c.ProposedAt, DecidedByName = names.Name(c.DecidedByUserId), DecidedAt = c.DecidedAt, DecisionNote = c.DecisionNote,
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<EmployeeChangeDto>>> GetEmployeeChangesAsync(EmployeeChangeQuery query, CancellationToken ct)
        {
            query ??= new EmployeeChangeQuery();
            if (!CanRead) return Bad<PagedList<EmployeeChangeDto>>(NotAllowed);
            IQueryable<FnEmployeeChange> q = _uow.Set<FnEmployeeChange>().AsNoTracking();
            if (query.EmployeeId.HasValue) q = q.Where(c => c.EmployeeId == query.EmployeeId.Value);
            if (query.Status.HasValue) { FnChangeStatus st = (FnChangeStatus)query.Status.Value; q = q.Where(c => c.Status == st); }
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnEmployeeChange> rows = await q.OrderByDescending(c => c.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await ChangeDtosAsync(rows, ct), total, number, size));
        }

        public async Task<ResponseDto<List<EmployeeWageDto>>> GetEmployeeWagesAsync(int employeeId, CancellationToken ct)
        {
            if (!CanRead) return Bad<List<EmployeeWageDto>>(NotAllowed);
            List<FnEmployeeWage> rows = await _uow.Set<FnEmployeeWage>().AsNoTracking().Where(w => w.EmployeeId == employeeId).OrderByDescending(w => w.EffectiveFrom).ThenByDescending(w => w.Id).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(w => w.ApprovedByUserId), ct);
            return ResponseDtoHelper.Ok(rows.Select(w => new EmployeeWageDto
            {
                Id = w.Id, EmployeeId = w.EmployeeId, EffectiveFrom = w.EffectiveFrom, DailyWage = w.DailyWage, IsBaseline = w.IsBaseline, ApprovedByName = names.Name(w.ApprovedByUserId), ApprovedAt = w.ApprovedAt,
            }).ToList());
        }

        /// <summary>Writes (or replaces, for the same month) a row of the wage history; the row list is kept in step so the caller can read the effective wage afterwards.</summary>
        private void PutWage(List<FnEmployeeWage> rows, int employeeId, DateTime from, decimal wage, FnEmployeeChange change, DateTimeOffset now)
        {
            FnEmployeeWage same = rows.FirstOrDefault(w => w.EffectiveFrom.Date == from.Date);
            if (same != null)
            {
                same.DailyWage = wage; same.CreatedByUserId = change.ProposedByUserId; same.CreatedAt = change.ProposedAt; same.ApprovedByUserId = Me; same.ApprovedAt = now; same.ChangeId = change.Id; same.IsBaseline = false;
                return;
            }
            var row = new FnEmployeeWage
            {
                EmployeeId = employeeId, EffectiveFrom = from.Date, DailyWage = wage, CreatedByUserId = change.ProposedByUserId, CreatedAt = change.ProposedAt, ApprovedByUserId = Me, ApprovedAt = now, ChangeId = change.Id,
            };
            _uow.Set<FnEmployeeWage>().Add(row);
            rows.Add(row);
        }

        public async Task<ResponseDto<EmployeeChangeDto>> EmployeeChangeActionAsync(EmployeeChangeActionDto dto, CancellationToken ct)
        {
            // the usual deciders are PAYROLL and FINANCEMANAGER; the CEO decides only the record of a finance manager (review H2)
            if (!CanWrite && !_user.IsInRole(FinanceRoles.Ceo)) return Bad<EmployeeChangeDto>(NotAllowed);
            if (dto == null || dto.Id <= 0) return Bad<EmployeeChangeDto>("اطلاعات ارسال‌شده کامل نیست.");
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            if (action != "approve" && action != "reject") return Bad<EmployeeChangeDto>("عملیات باید تایید یا رد باشد.");
            FnEmployeeChange c = await _uow.Set<FnEmployeeChange>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (c == null) return ResponseDtoHelper.NotFound<EmployeeChangeDto>("درخواست پیدا نشد.");
            if (c.Status != FnChangeStatus.Pending) return Bad<EmployeeChangeDto>("درباره این درخواست قبلاً تصمیم گرفته شده است.");
            FnEmployee e = await _uow.Set<FnEmployee>().FirstOrDefaultAsync(x => x.Id == c.EmployeeId, ct);
            if (e == null) return ResponseDtoHelper.NotFound<EmployeeChangeDto>("کارمند پیدا نشد.");
            // the employee concerned never decides his own request, and a finance manager's own request goes to the CEO
            string selfRefusal = await SelfApproval.RefusalAsync(_uow, _user, e, CanWrite, NotAllowed, true, ct);
            if (selfRefusal != null) return Bad<EmployeeChangeDto>(selfRefusal);
            // maker-checker: the person who proposed (or, for a new employee, anyone who edited the row since) does not decide
            if (c.ProposedByUserId == Me || (c.Kind == FnEmployeeChangeKind.Create && Touched(0, e.EditorUserIds, Me)))
                return Bad<EmployeeChangeDto>("کسی که این کارمند یا تغییر را ثبت یا ویرایش کرده نمی‌تواند آن را تایید یا رد کند؛ نفر دوم تصمیم می‌گیرد.");
            string note = Cap(Clean(dto.Note), 500);
            if (action == "reject" && note == null) return Bad<EmployeeChangeDto>("دلیل رد را بنویسید.");
            DateTimeOffset now = DateTimeOffset.Now;

            if (action == "approve")
            {
                GuardedFieldsDto g = GuardedFrom(c.ProposedJson);
                if (g.DailyWage <= 0) return Bad<EmployeeChangeDto>("مزد روزانه پیشنهادی نامعتبر است.");
                if (!string.IsNullOrEmpty(g.BankIban) && !IbanValid(g.BankIban)) return Bad<EmployeeChangeDto>("شماره شبا پیشنهادی نامعتبر است.");
                List<FnEmployeeWage> rows = await _uow.Set<FnEmployeeWage>().Where(w => w.EmployeeId == e.Id).ToListAsync(ct);
                DateTime hireStart = WageHistory.MonthStart(e.HireDate);
                if (c.Kind == FnEmployeeChangeKind.Create)
                {
                    // the wage history starts with the approved wage (from the hire month, or the month the request named)
                    PutWage(rows, e.Id, (c.WageEffectiveFrom ?? hireStart).Date, e.DailyWage, c, now);
                    e.PendingApproval = false;
                }
                else
                {
                    bool wageChanged = g.DailyWage != e.DailyWage;
                    if (wageChanged)
                    {
                        DateTime from = (c.WageEffectiveFrom ?? WageHistory.MonthStart(DateTime.Today)).Date;
                        string guard = await WageMonthGuardAsync(e.Id, from, ct);
                        if (guard != null) return Bad<EmployeeChangeDto>(guard);
                        // an employee that had no history yet: the old wage becomes the first row, so the months before the change keep it
                        if (rows.Count == 0 && from > hireStart)
                        {
                            var baseline = new FnEmployeeWage
                            {
                                EmployeeId = e.Id, EffectiveFrom = hireStart, DailyWage = e.DailyWage, CreatedByUserId = c.ProposedByUserId, CreatedAt = c.ProposedAt, ApprovedByUserId = Me, ApprovedAt = now, ChangeId = c.Id, IsBaseline = true,
                            };
                            _uow.Set<FnEmployeeWage>().Add(baseline);
                            rows.Add(baseline);
                        }
                        PutWage(rows, e.Id, from, g.DailyWage, c, now);
                        // FnEmployee.DailyWage stays the cached current value: the wage in force this month
                        e.DailyWage = WageHistory.WageAt(rows, g.DailyWage, WageHistory.MonthStart(DateTime.Today), out bool _);
                    }
                    e.ExemptionFlags = Clean(g.ExemptionFlags); e.ChildrenCount = g.ChildrenCount; e.HardAndHarmful = g.HardAndHarmful; e.BankIban = Clean(g.BankIban);
                }
                c.Status = FnChangeStatus.Approved;
                // a run that was calculated with the old values must be calculated again
                await ReopenCalculatedRunsAsync(null, ct);
            }
            else c.Status = FnChangeStatus.Rejected;
            c.DecidedByUserId = Me; c.DecidedAt = now; c.DecisionNote = note;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<EmployeeChangeDto>(Conflict); }
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "empchg:" + c.Id, false, "finance", "employee-change", c.Id, null, null, null, ct);
            return ResponseDtoHelper.Ok((await ChangeDtosAsync(new List<FnEmployeeChange> { c }, ct))[0]);
        }

        // ================================================================================== loan approval

        public async Task<ResponseDto<LoanDto>> LoanActionAsync(LoanActionDto dto, CancellationToken ct)
        {
            const string loanDeciders = NotAllowed + " (تایید وام و مساعده فقط توسط مدیر مالی)";
            bool manager = _user.IsInRole(FinanceRoles.Managers);
            if (!manager && !_user.IsInRole(FinanceRoles.Ceo)) return Bad<LoanDto>(loanDeciders);
            if (dto == null || dto.Id <= 0) return Bad<LoanDto>("اطلاعات ارسال‌شده کامل نیست.");
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            if (action != "approve" && action != "reject") return Bad<LoanDto>("عملیات باید تایید یا رد باشد.");
            FnEmployeeLoan l = await _uow.Set<FnEmployeeLoan>().Include(x => x.Employee).FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (l == null) return ResponseDtoHelper.NotFound<LoanDto>("وام پیدا نشد.");
            if (l.ApprovalStatus != FnLoanApproval.Pending) return Bad<LoanDto>("درباره این وام قبلاً تصمیم گرفته شده است.");
            // the borrower never decides his own loan, and the loan of a finance manager goes to the CEO (review H2)
            string selfRefusal = await SelfApproval.RefusalAsync(_uow, _user, l.Employee, manager, loanDeciders, true, ct);
            if (selfRefusal != null) return Bad<LoanDto>(selfRefusal);
            if (Touched(l.CreatedByUserId, l.EditorUserIds, Me)) return Bad<LoanDto>("کسی که وام را ثبت یا ویرایش کرده نمی‌تواند آن را تایید یا رد کند.");
            string note = Clean(dto.Note);
            if (action == "reject" && note == null) return Bad<LoanDto>("دلیل رد را بنویسید.");
            if (action == "approve")
            {
                l.ApprovalStatus = FnLoanApproval.Approved; l.ApprovedByUserId = Me; l.ApprovedAt = DateTimeOffset.Now; l.RejectReason = null;
                await ReopenCalculatedRunsAsync(null, ct);
            }
            else { l.ApprovalStatus = FnLoanApproval.Rejected; l.ApprovedByUserId = Me; l.ApprovedAt = DateTimeOffset.Now; l.RejectReason = note.Length > 500 ? note.Substring(0, 500) : note; }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<LoanDto>(Conflict); }
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "loan:" + l.Id, false, "finance", "payroll-loan", l.Id, null, null, null, ct);
            return ResponseDtoHelper.Ok(ToDto(l, l.Employee?.PersonnelCode, await InventoryUsers.GetNamesAsync(_uow, new int?[] { l.CreatedByUserId, l.ApprovedByUserId }, ct)));
        }

        // ================================================================================== corrections of locked sheets

        /// <summary>
        /// Payroll cannot overwrite the numbers of a sheet the HR close locked; it asks for a correction (with a reason). HR - another person than the proposer - approves or rejects it in the attendance module,
        /// and the approval writes the numbers to the sheet. Only a month whose HR close is confirmed and whose payroll is not approved yet can be corrected.
        /// </summary>
        public async Task<ResponseDto<OfficeDtos.AttendanceCorrectionDto>> ProposeCorrectionAsync(OfficeDtos.ProposeCorrectionDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<OfficeDtos.AttendanceCorrectionDto>(NotAllowed);
            if (dto == null || dto.Proposed == null || !ValidYear(dto.Year) || dto.Month < 1 || dto.Month > 12) return Bad<OfficeDtos.AttendanceCorrectionDto>("اطلاعات ارسال‌شده کامل نیست.");
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<OfficeDtos.AttendanceCorrectionDto>("دلیل اصلاح کارکرد را بنویسید.");
            FnAttendanceSheet sheet = await _uow.Set<FnAttendanceSheet>().AsNoTracking().FirstOrDefaultAsync(s => s.EmployeeId == dto.EmployeeId && s.Year == dto.Year && s.Month == dto.Month, ct);
            if (sheet == null) return ResponseDtoHelper.NotFound<OfficeDtos.AttendanceCorrectionDto>("برگه کارکرد این کارمند در این ماه پیدا نشد.");
            FnEmployee emp = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == sheet.EmployeeId, ct);
            if (emp == null) return ResponseDtoHelper.NotFound<OfficeDtos.AttendanceCorrectionDto>("کارمند پیدا نشد.");
            // nobody proposes a correction of his own attendance (review H2)
            if (emp.UserId.HasValue && Me > 0 && emp.UserId.Value == Me) return Bad<OfficeDtos.AttendanceCorrectionDto>(SelfApproval.OwnRecordPropose);
            if (await LockedAsync(sheet.Year, sheet.Month, emp.WorkshopId, ct)) return Bad<OfficeDtos.AttendanceCorrectionDto>("فیش حقوق این ماه تایید شده و کارکرد قابل اصلاح نیست.");
            // a month HR has not closed yet (or whose close was rejected) takes the numbers directly on the attendance page; once the close is prepared or confirmed, the numbers change only through this flow (review M2),
            // also for a sheet that payroll typed and that is not locked: its approved correction brings it under the close
            OfficeEntities.OfAttendanceMonth monthRow = await HrClose.GetAsync(_uow, sheet.Year, sheet.Month, ct);
            string closeRefusal = HrClose.Refusal(sheet.Year, sheet.Month, monthRow);
            if (closeRefusal != null)
                return Bad<OfficeDtos.AttendanceCorrectionDto>(!sheet.Locked && (monthRow == null || monthRow.Status == OfficeEntities.OfMonthStatus.Rejected)
                    ? "این برگه قفل نیست؛ کارکرد را مستقیم در صفحه کارکرد اصلاح کنید."
                    : "اصلاح کارکرد فقط برای ماهی است که بستن حضور و غیاب آن تایید شده؛ " + closeRefusal);
            if (await _uow.Set<FnAttendanceCorrection>().AnyAsync(c => c.SheetId == sheet.Id && c.Status == FnChangeStatus.Pending, ct))
                return Bad<OfficeDtos.AttendanceCorrectionDto>("برای این برگه درخواست اصلاح دیگری در انتظار تصمیم منابع انسانی است.");
            string numberError = SheetNumbers.Validate(dto.Proposed, sheet.Year, sheet.Month, await OvertimeCapHoursAsync(ct));
            if (numberError != null) return Bad<OfficeDtos.AttendanceCorrectionDto>(numberError);
            OfficeDtos.SheetNumbersDto current = SheetNumbers.Of(sheet);
            if (SheetNumbers.Same(current, dto.Proposed)) return Bad<OfficeDtos.AttendanceCorrectionDto>("ارقام پیشنهادی با کارکرد فعلی برابر است.");

            var row = new FnAttendanceCorrection
            {
                SheetId = sheet.Id, EmployeeId = sheet.EmployeeId, Year = sheet.Year, Month = sheet.Month, CurrentJson = SheetNumbers.ToJson(current), ProposedJson = SheetNumbers.ToJson(dto.Proposed),
                Reason = reason.Length > 500 ? reason.Substring(0, 500) : reason, Status = FnChangeStatus.Pending, ProposedByUserId = Me, ProposedAt = DateTimeOffset.Now,
            };
            _uow.Set<FnAttendanceCorrection>().Add(row);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<OfficeDtos.AttendanceCorrectionDto>(Conflict); }
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "att:corr:" + row.Id, true, "office", "attendance", row.Id,
                $"اصلاح کارکرد {row.Year}/{row.Month:00} کارمند با کد پرسنلی {emp.PersonnelCode}", "/office/attendance/corrections",
                await SelfApproval.GateRolesAsync(_uow, emp, global::Services.NasbNiro.Office.OfficeRoles.Hr, false, ct), ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { row.ProposedByUserId }, ct);
            return ResponseDtoHelper.Ok(new OfficeDtos.AttendanceCorrectionDto
            {
                Id = row.Id, SheetId = row.SheetId, EmployeeId = row.EmployeeId, PersonnelCode = emp.PersonnelCode, EmployeeName = (emp.FirstName + " " + emp.LastName).Trim(), Year = row.Year, Month = row.Month,
                Current = current, Proposed = dto.Proposed, Reason = row.Reason, Status = row.Status.ToString(), ProposedByName = names.Name(row.ProposedByUserId), ProposedAt = row.ProposedAt,
            });
        }

        // ================================================================================== opening leave balances

        private async Task<List<LeaveOpeningDto>> OpeningDtosAsync(List<FnLeaveOpening> rows, CancellationToken ct)
        {
            List<int> ids = rows.Select(o => o.EmployeeId).Distinct().ToList();
            Dictionary<int, FnEmployee> employees = await _uow.Set<FnEmployee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(o => (int?)o.EnteredByUserId).Concat(rows.Select(o => o.DecidedByUserId)), ct);
            return rows.Select(o =>
            {
                employees.TryGetValue(o.EmployeeId, out FnEmployee e);
                return new LeaveOpeningDto
                {
                    Id = o.Id, EmployeeId = o.EmployeeId, PersonnelCode = e?.PersonnelCode, EmployeeName = e == null ? null : (e.FirstName + " " + e.LastName).Trim(), Year = o.Year, Month = o.Month,
                    CarriedDays = o.CarriedDays, Note = o.Note, Status = o.Status.ToString(), EnteredByName = names.Name(o.EnteredByUserId), EnteredAt = o.EnteredAt, DecidedByName = names.Name(o.DecidedByUserId),
                    DecidedAt = o.DecidedAt, DecisionNote = o.DecisionNote,
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<LeaveOpeningDto>>> GetLeaveOpeningsAsync(LeaveOpeningQuery query, CancellationToken ct)
        {
            query ??= new LeaveOpeningQuery();
            if (!CanRead) return Bad<PagedList<LeaveOpeningDto>>(NotAllowed);
            IQueryable<FnLeaveOpening> q = _uow.Set<FnLeaveOpening>().AsNoTracking();
            if (query.Year.HasValue) q = q.Where(o => o.Year == query.Year.Value);
            if (query.EmployeeId.HasValue) q = q.Where(o => o.EmployeeId == query.EmployeeId.Value);
            if (query.Status.HasValue) { FnChangeStatus st = (FnChangeStatus)query.Status.Value; q = q.Where(o => o.Status == st); }
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnLeaveOpening> rows = await q.OrderByDescending(o => o.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await OpeningDtosAsync(rows, ct), total, number, size));
        }

        /// <summary>Validates and adds one opening request (not saved). Returns the Persian refusal, or null.</summary>
        private async Task<(string Error, FnLeaveOpening Row, FnEmployee Employee)> NewOpeningAsync(SaveLeaveOpeningDto dto, CancellationToken ct)
        {
            if (dto == null) return ("اطلاعات ارسال‌شده کامل نیست.", null, null);
            if (!ValidYear(dto.Year) || dto.Month < 1 || dto.Month > 12) return ("سال یا ماه نامعتبر است.", null, null);
            if (dto.CarriedDays < 0 || dto.CarriedDays > 9999) return ("تعداد روزهای مانده مرخصی نامعتبر است.", null, null);
            FnEmployee emp = null;
            if (dto.EmployeeId.HasValue) emp = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId.Value, ct);
            else if (Clean(dto.PersonnelCode) != null)
            {
                string pc = Clean(Digits(dto.PersonnelCode));
                emp = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.PersonnelCode == pc, ct);
            }
            if (emp == null) return ("کارمند پیدا نشد.", null, null);
            if (await _uow.Set<FnLeaveOpening>().AnyAsync(o => o.EmployeeId == emp.Id && o.Year == dto.Year && o.Status == FnChangeStatus.Pending, ct) ||
                _uow.Set<FnLeaveOpening>().Local.Any(o => o.EmployeeId == emp.Id && o.Year == dto.Year && o.Status == FnChangeStatus.Pending))
                return ("برای این کارمند و سال، مانده اول دوره در انتظار تایید مدیر مالی است.", null, null);
            FnLeaveBalance balance = await _uow.Set<FnLeaveBalance>().AsNoTracking().FirstOrDefaultAsync(b => b.EmployeeId == emp.Id && b.Year == dto.Year, ct);
            if (balance != null && dto.Month <= balance.LastMonth)
                return ($"مرخصی این کارمند تا ماه {balance.LastMonth} سال {dto.Year} در حقوق ثبت شده است؛ مانده اول دوره باید برای ماه بعد از آن باشد.", null, null);
            string note = Clean(dto.Note);
            var row = new FnLeaveOpening
            {
                EmployeeId = emp.Id, Year = dto.Year, Month = dto.Month, CarriedDays = Math.Round(dto.CarriedDays, 2, MidpointRounding.AwayFromZero), Note = note != null && note.Length > 500 ? note.Substring(0, 500) : note,
                Status = FnChangeStatus.Pending, EnteredByUserId = Me, EnteredAt = DateTimeOffset.Now,
            };
            _uow.Set<FnLeaveOpening>().Add(row);
            return (null, row, emp);
        }

        private async Task OpeningGateAsync(FnLeaveOpening o, bool waiting, FnEmployee employee, CancellationToken ct) =>
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "leaveopen:" + o.Id, waiting, "finance", "leave-opening", o.Id,
                waiting ? $"تایید مانده اول دوره مرخصی کارمند با کد پرسنلی {employee?.PersonnelCode}" : null, waiting ? "/finance/payroll/leave-openings" : null,
                waiting ? await SelfApproval.GateRolesAsync(_uow, employee, FinanceRoles.Managers, true, ct) : null, ct);

        public async Task<ResponseDto<LeaveOpeningDto>> SaveLeaveOpeningAsync(SaveLeaveOpeningDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<LeaveOpeningDto>(NotAllowed);
            var made = await NewOpeningAsync(dto, ct);
            if (made.Error != null) return Bad<LeaveOpeningDto>(made.Error);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<LeaveOpeningDto>(Conflict); }
            await OpeningGateAsync(made.Row, true, made.Employee, ct);
            return ResponseDtoHelper.Ok((await OpeningDtosAsync(new List<FnLeaveOpening> { made.Row }, ct))[0]);
        }

        public async Task<ResponseDto<ImportLeaveOpeningResultDto>> ImportLeaveOpeningsAsync(ImportLeaveOpeningDto dto, CancellationToken ct)
        {
            if (!CanWrite) return Bad<ImportLeaveOpeningResultDto>(NotAllowed);
            if (dto == null || dto.Rows == null || dto.Rows.Count == 0) return Bad<ImportLeaveOpeningResultDto>("ردیفی برای ورود ارسال نشده است.");
            if (dto.Rows.Count > 2000) return Bad<ImportLeaveOpeningResultDto>("حداکثر ۲۰۰۰ ردیف در هر بار.");
            var outcome = new ImportLeaveOpeningResultDto();
            int n = 0;
            foreach (SaveLeaveOpeningDto r in dto.Rows)
            {
                n++;
                var made = await NewOpeningAsync(r, ct);
                if (made.Error != null) { outcome.Errors.Add($"ردیف {n}: {made.Error}"); continue; }
                try { await _uow.SaveChangesAsync(ct); outcome.Imported++; }
                catch (DbUpdateException)
                {
                    outcome.Errors.Add($"ردیف {n}: ذخیره نشد؛ ورود متوقف شد (ردیف‌های قبلی ثبت شده‌اند).");
                    break;
                }
                await OpeningGateAsync(made.Row, true, made.Employee, ct);
            }
            return ResponseDtoHelper.Ok(outcome);
        }

        public async Task<ResponseDto<LeaveOpeningDto>> LeaveOpeningActionAsync(LeaveOpeningActionDto dto, CancellationToken ct)
        {
            const string openingDeciders = NotAllowed + " (تایید مانده اول دوره مرخصی فقط توسط مدیر مالی)";
            bool manager = _user.IsInRole(FinanceRoles.Managers);
            if (!manager && !_user.IsInRole(FinanceRoles.Ceo)) return Bad<LeaveOpeningDto>(openingDeciders);
            if (dto == null || dto.Id <= 0) return Bad<LeaveOpeningDto>("اطلاعات ارسال‌شده کامل نیست.");
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            if (action != "approve" && action != "reject") return Bad<LeaveOpeningDto>("عملیات باید تایید یا رد باشد.");
            FnLeaveOpening o = await _uow.Set<FnLeaveOpening>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (o == null) return ResponseDtoHelper.NotFound<LeaveOpeningDto>("مانده اول دوره پیدا نشد.");
            if (o.Status != FnChangeStatus.Pending) return Bad<LeaveOpeningDto>("درباره این مانده قبلاً تصمیم گرفته شده است.");
            // the employee concerned never decides his own opening balance, and a finance manager's goes to the CEO (review H2)
            FnEmployee openingSubject = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == o.EmployeeId, ct);
            string selfRefusal = await SelfApproval.RefusalAsync(_uow, _user, openingSubject, manager, openingDeciders, true, ct);
            if (selfRefusal != null) return Bad<LeaveOpeningDto>(selfRefusal);
            if (o.EnteredByUserId == Me) return Bad<LeaveOpeningDto>("واردکننده مانده اول دوره نمی‌تواند آن را تایید یا رد کند.");
            string note = Cap(Clean(dto.Note), 500);
            if (action == "reject" && note == null) return Bad<LeaveOpeningDto>("دلیل رد را بنویسید.");
            DateTimeOffset now = DateTimeOffset.Now;
            if (action == "approve")
            {
                // the approval re-bases the balance of the year: the carried days are what the employee has at the start of that month, the entitlement continues from it
                FnLeaveBalance b = await _uow.Set<FnLeaveBalance>().FirstOrDefaultAsync(x => x.EmployeeId == o.EmployeeId && x.Year == o.Year, ct);
                if (b != null && o.Month <= b.LastMonth)
                    return Bad<LeaveOpeningDto>($"مرخصی این کارمند تا ماه {b.LastMonth} سال {o.Year} در حقوق ثبت شده است؛ این مانده را رد کنید و برای ماه بعد از آن دوباره وارد کنید.");
                if (b == null)
                {
                    b = new FnLeaveBalance { EmployeeId = o.EmployeeId, Year = o.Year };
                    _uow.Set<FnLeaveBalance>().Add(b);
                }
                b.CarriedForward = o.CarriedDays; b.Entitled = 0m; b.Used = 0m; b.LastMonth = o.Month - 1;
                o.Status = FnChangeStatus.Approved;
            }
            else o.Status = FnChangeStatus.Rejected;
            o.DecidedByUserId = Me; o.DecidedAt = now; o.DecisionNote = note;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<LeaveOpeningDto>(Conflict); }
            await OpeningGateAsync(o, false, null, ct);
            return ResponseDtoHelper.Ok((await OpeningDtosAsync(new List<FnLeaveOpening> { o }, ct))[0]);
        }
    }
}
