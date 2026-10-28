using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.OfficeDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Office
{
    public partial class AttendanceService
    {
        private sealed class EmpInfo
        {
            public int Id;
            public DateTime HireDate;
            public DateTime? EndDate;
            public int? WorkshopId;
        }

        private sealed class PayrollLock
        {
            public int Year;
            public int Month;
            public int? WorkshopId;
        }

        private static bool Locked(List<PayrollLock> locks, DateTime date, int? workshopId)
        {
            int y = PersianCal.Year(date), m = PersianCal.Month(date);
            return locks.Any(l => l.Year == y && l.Month == m && (l.WorkshopId == null || l.WorkshopId == workshopId));
        }

        /// <summary>
        /// The months whose days must not be recomputed: payroll approved (per workshop), or the HR close prepared / confirmed (every workshop: the sheets were written from exactly these days).
        /// A rejected close opens the month again.
        /// </summary>
        private async Task<List<PayrollLock>> FrozenMonthsAsync(int firstYear, int lastYear, CancellationToken ct)
        {
            List<PayrollLock> locks = (await _uow.Set<FnPayrollRun>().AsNoTracking().Where(r => r.Status >= FnPayrollStatus.Approved && r.Year >= firstYear && r.Year <= lastYear)
                .Select(r => new { r.Year, r.Month, r.WorkshopId }).ToListAsync(ct)).Select(r => new PayrollLock { Year = r.Year, Month = r.Month, WorkshopId = r.WorkshopId }).ToList();
            foreach (var m in await _uow.Set<OfAttendanceMonth>().AsNoTracking().Where(x => x.Year >= firstYear && x.Year <= lastYear && x.Status != OfMonthStatus.Rejected)
                .Select(x => new { x.Year, x.Month }).ToListAsync(ct))
                locks.Add(new PayrollLock { Year = m.Year, Month = m.Month, WorkshopId = null });
            return locks;
        }

        // ================================================================================== daily computation

        public async Task<ResponseDto<ComputeResultDto>> ComputeDaysAsync(ComputeDaysDto dto, CancellationToken ct)
        {
            if (!CanReadAll) return Bad<ComputeResultDto>(OfficeText.NotAllowed);
            if (dto == null) return Bad<ComputeResultDto>(OfficeText.InvalidData);
            if (dto.ToDate.Date < dto.FromDate.Date) return Bad<ComputeResultDto>("پایان بازه از شروع آن زودتر است.");
            if ((dto.ToDate.Date - dto.FromDate.Date).TotalDays > 62) return Bad<ComputeResultDto>("بازه محاسبه حداکثر ۶۲ روز است.");
            return ResponseDtoHelper.Ok(await ComputeCoreAsync(dto.FromDate, dto.ToDate, dto.EmployeeId, ct));
        }

        public async Task RecomputeAsync(DateTime from, DateTime to, int employeeId, CancellationToken ct)
        {
            if ((to.Date - from.Date).TotalDays > 62) return;
            await ComputeCoreAsync(from, to, employeeId, ct);
        }

        public async Task<ComputeResultDto> RunDailyAsync(CancellationToken ct)
        {
            DateTime today = DateTime.Today;
            ComputeResultDto result = await ComputeCoreAsync(today.AddDays(-3), today.AddDays(-1), null, ct);
            // the other nightly steps of the module report a failure as a warning: none of them may stop the attendance of the day
            try { await PurgePushLogsAsync(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { result.Warnings.Add("پاک‌سازی دوره‌ای گزارش دریافت دستگاه‌ها انجام نشد."); }
            // a future-dated wage takes effect when its month starts: the cached current wage of the employee follows the history
            try { await WageHistory.RefreshCachedWagesAsync(_uow, today, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { result.Warnings.Add("به‌روزرسانی مزد روزانه جاری کارکنان از تاریخچه مزد انجام نشد."); }
            return result;
        }

        private async Task<OfAttendancePolicy> LoadPolicyAsync(CancellationToken ct)
        {
            await EnsureCompanyPolicyAsync(ct);
            return await _uow.Set<OfAttendancePolicy>().AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct) ?? new OfAttendancePolicy();
        }

        private const string GapDayNote = "روز بدون شیفت و بدون تردد میان نخستین تخصیص شیفت پرسنل و پایان آخرین تخصیص او؛ غایب محسوب شد.";

        /// <summary>The note the first R1f version wrote on such a day: rows that still carry it are recognised, and removed when the rule no longer counts them.</summary>
        private const string LegacyGapDayNote = "روز بدون شیفت و بدون تردد برای پرسنلی که در این ماه تخصیص شیفت دارد؛ غایب محسوب شد.";

        /// <summary>The span of the shift assignments of one employee over the whole history: the first start and the end of the last one (null = the last assignment is open-ended).</summary>
        internal sealed class AssignmentSpan
        {
            public DateTime First;
            public DateTime? End;
        }

        /// <summary>
        /// The gap-day rule (D16, release R1f review M8, ★ company decision): a day with neither a shift nor a punch is ABSENT when it lies between the first start of the employee's shift assignments
        /// and the end of the last one, and no assignment covers it. When the last assignment is open-ended the span reaches the employee's EndDate (the day loop stops there) or has no end.
        /// Before the first assignment and after a closed last assignment the day is neutral: no row is written. Leave, mission and non-working days are decided by the day computation as usual.
        /// </summary>
        internal static bool InAssignmentSpan(Dictionary<int, AssignmentSpan> spans, int employeeId, DateTime date) =>
            spans.TryGetValue(employeeId, out AssignmentSpan span) && date >= span.First && (!span.End.HasValue || date <= span.End.Value);

        /// <summary>
        /// Removes the rows that an earlier run wrote as gap days and that the rule does not count any more (for example the days before the first assignment, which the first R1f version counted as absent);
        /// frozen months are left alone. Returns whether anything was removed (the caller saves).
        /// </summary>
        private async Task<bool> RemoveStaleGapDaysAsync(DateTime from, DateTime to, int? employeeId, DateTimeOffset now, CancellationToken ct)
        {
            IQueryable<OfAttendanceDay> q = _uow.Set<OfAttendanceDay>().Where(d => d.Date >= from && d.Date <= to && (d.Note == GapDayNote || d.Note == LegacyGapDayNote));
            if (employeeId.HasValue) q = q.Where(d => d.EmployeeId == employeeId.Value);
            // a row this run just rewrote carries the new stamp: it is a gap day again and stays
            List<OfAttendanceDay> stale = (await q.ToListAsync(ct)).Where(d => d.ComputedAt != now).ToList();
            if (stale.Count == 0) return false;
            List<int> ids = stale.Select(d => d.EmployeeId).Distinct().ToList();
            Dictionary<int, int?> workshops = (await _uow.Set<FnEmployee>().AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.WorkshopId }).ToListAsync(ct)).ToDictionary(e => e.Id, e => e.WorkshopId);
            List<PayrollLock> locks = await FrozenMonthsAsync(PersianCal.Year(from), PersianCal.Year(to), ct);
            bool removed = false;
            foreach (OfAttendanceDay d in stale)
            {
                workshops.TryGetValue(d.EmployeeId, out int? workshop);
                if (Locked(locks, d.Date, workshop)) continue;
                _uow.Set<OfAttendanceDay>().Remove(d);
                removed = true;
            }
            return removed;
        }

        /// <summary>Computes (or recomputes) the days of the range for every employee that has a shift or clock events. Today and the future are not computed; frozen months are left alone.</summary>
        internal async Task<ComputeResultDto> ComputeCoreAsync(DateTime fromDate, DateTime toDate, int? employeeId, CancellationToken ct)
        {
            DateTime from = fromDate.Date;
            DateTime to = toDate.Date;
            DateTime yesterday = DateTime.Today.AddDays(-1);
            if (to > yesterday) to = yesterday;
            var result = new ComputeResultDto();
            if (to < from) return result;

            OfAttendancePolicy policy = await LoadPolicyAsync(ct);
            OfficeCalendar calendar = await OfficeCalendar.LoadAsync(_uow, ct);
            Dictionary<int, OfShift> shifts = await _uow.Set<OfShift>().AsNoTracking().ToDictionaryAsync(s => s.Id, ct);

            // the assignments of the whole Persian months around the range (+ one day): a day nobody explained is Absent for an employee who has a shift assignment in that month,
            // and the boundary between two days depends on the shift of the neighbour
            DateTime assignFrom = PersianCal.Start(PersianCal.Year(from), PersianCal.Month(from)).AddDays(-1);
            DateTime assignTo = PersianCal.End(PersianCal.Year(to), PersianCal.Month(to)).AddDays(1);
            IQueryable<OfShiftAssignment> assignQuery = _uow.Set<OfShiftAssignment>().AsNoTracking().Where(a => a.FromDate <= assignTo && (a.ToDate == null || a.ToDate >= assignFrom));
            if (employeeId.HasValue) assignQuery = assignQuery.Where(a => a.EmployeeId == employeeId.Value);
            List<OfShiftAssignment> assignments = await assignQuery.ToListAsync(ct);

            // the span of each employee's assignments over the WHOLE history (the list above holds only the months around the range): the gap-day rule needs the first start and the last end
            IQueryable<OfShiftAssignment> spanQuery = _uow.Set<OfShiftAssignment>().AsNoTracking();
            if (employeeId.HasValue) spanQuery = spanQuery.Where(a => a.EmployeeId == employeeId.Value);
            Dictionary<int, AssignmentSpan> spans = (await spanQuery.GroupBy(a => a.EmployeeId)
                    .Select(g => new { EmployeeId = g.Key, First = g.Min(a => a.FromDate), Open = g.Count(a => a.ToDate == null), Last = g.Max(a => a.ToDate) }).ToListAsync(ct))
                .ToDictionary(x => x.EmployeeId, x => new AssignmentSpan { First = x.First.Date, End = x.Open > 0 ? (DateTime?)null : x.Last?.Date });

            // only approved events count: a manual request that waits for a second person is not a punch yet
            DateTime eventFrom = from.AddDays(-1);
            DateTime eventTo = to.AddDays(3);
            IQueryable<OfClockEvent> eventQuery = _uow.Set<OfClockEvent>().AsNoTracking().Where(e => e.Timestamp >= eventFrom && e.Timestamp < eventTo && e.Approval == OfClockApproval.Approved);
            if (employeeId.HasValue) eventQuery = eventQuery.Where(e => e.EmployeeId == employeeId.Value);
            var events = (await eventQuery.Select(e => new { e.EmployeeId, e.Timestamp }).ToListAsync(ct))
                .GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => g.Select(e => Minute(e.Timestamp)).Distinct().OrderBy(t => t).ToList());

            // an employee whose span reaches into the range is computed even when no assignment overlaps the months around it (a long gap between two assignments)
            HashSet<int> ids = assignments.Select(a => a.EmployeeId).Concat(events.Keys)
                .Concat(spans.Where(s => s.Value.First <= to && (!s.Value.End.HasValue || s.Value.End.Value >= from)).Select(s => s.Key)).ToHashSet();
            if (employeeId.HasValue) ids.IntersectWith(new[] { employeeId.Value });
            if (ids.Count == 0)
            {
                if (await RemoveStaleGapDaysAsync(from, to, employeeId, DateTimeOffset.Now, ct)) await _uow.SaveChangesAsync(ct);
                return result;
            }
            List<int> idList = ids.ToList();

            List<EmpInfo> employees = (await _uow.Set<FnEmployee>().AsNoTracking().Where(e => idList.Contains(e.Id))
                .Select(e => new { e.Id, e.HireDate, e.EndDate, e.WorkshopId }).ToListAsync(ct))
                .Select(e => new EmpInfo { Id = e.Id, HireDate = e.HireDate.Date, EndDate = e.EndDate?.Date, WorkshopId = e.WorkshopId }).ToList();

            List<OfLeaveRequest> leaves = await _uow.Set<OfLeaveRequest>().AsNoTracking()
                .Where(r => !r.Deleted && (r.Status == OfLeaveStatus.Approved || r.Status == OfLeaveStatus.Recorded) && r.Kind != OfLeaveKind.ForgotClock
                    && r.FromDate <= to && r.ToDate >= from && idList.Contains(r.EmployeeId)).ToListAsync(ct);

            int firstYear = PersianCal.Year(from), lastYear = PersianCal.Year(to);
            List<PayrollLock> locks = await FrozenMonthsAsync(firstYear, lastYear, ct);

            Dictionary<(int, DateTime), OfAttendanceDay> existing = (await _uow.Set<OfAttendanceDay>().Where(d => d.Date >= from && d.Date <= to && idList.Contains(d.EmployeeId)).ToListAsync(ct))
                .ToDictionary(d => (d.EmployeeId, d.Date.Date));

            // the night window of a day without a shift comes from the rate table (loaded when such a day has punches); a missing key is reported, and the close refuses while it is missing
            bool windowLoaded = false;
            int? nightStart = null, nightEnd = null;

            DateTimeOffset now = DateTimeOffset.Now;
            var computedEmployees = new HashSet<int>();
            var withoutShift = new HashSet<int>();
            foreach (EmpInfo emp in employees.OrderBy(e => e.Id))
            {
                List<OfShiftAssignment> mine = assignments.Where(a => a.EmployeeId == emp.Id).OrderBy(a => a.FromDate).ToList();
                if (mine.Count == 0) withoutShift.Add(emp.Id);
                List<DateTime> punchesOfEmployee = events.TryGetValue(emp.Id, out List<DateTime> list) ? list : new List<DateTime>();
                List<OfLeaveRequest> myLeaves = leaves.Where(l => l.EmployeeId == emp.Id).ToList();
                for (DateTime date = from; date <= to; date = date.AddDays(1))
                {
                    if (date < emp.HireDate || (emp.EndDate.HasValue && date > emp.EndDate.Value)) continue;
                    OfShift ShiftOn(DateTime x)
                    {
                        OfShiftAssignment found = mine.LastOrDefault(a => a.FromDate <= x && (a.ToDate == null || a.ToDate >= x));
                        return found != null && shifts.TryGetValue(found.ShiftId, out OfShift sh) ? sh : null;
                    }
                    OfShift shift = ShiftOn(date);
                    DateTime windowFrom = AttendanceMath.Boundary(date.AddDays(-1), ShiftOn(date.AddDays(-1)), shift);
                    DateTime windowTo = AttendanceMath.Boundary(date, shift, ShiftOn(date.AddDays(1)));
                    List<DateTime> punches = punchesOfEmployee.Where(t => t >= windowFrom && t < windowTo).ToList();
                    bool gapDay = false;
                    if (shift == null && punches.Count == 0)
                    {
                        // the gap-day rule (see InAssignmentSpan): inside the span of the employee's assignments nobody explained the day (no shift, no punch, no leave), so it is Absent and payroll deducts it;
                        // before the first assignment and after a closed last one it is neutral
                        if (!InAssignmentSpan(spans, emp.Id, date)) continue;
                        gapDay = true;
                    }
                    if (Locked(locks, date, emp.WorkshopId)) { result.SkippedLocked++; continue; }

                    var input = new DayInput
                    {
                        Date = date, HasShift = shift != null, NonWorking = !calendar.IsWorkingDay(date, shift), Punches = punches,
                        GraceMinutes = policy.GraceMinutes, MaxOvertimeMinutes = policy.MaxOvertimeMinutesPerDay, OvertimeRequiresApproval = policy.OvertimeRequiresApproval, RoundingMinutes = policy.RoundingMinutes,
                        DelayPenaltyFactor = policy.DelayPenaltyFactor, EarlyLeaveDeductionEnabled = !policy.EarlyLeaveDeductionDisabled,
                    };
                    if (shift != null)
                    {
                        input.StartMinute = shift.StartMinute; input.EndMinute = shift.EndMinute; input.BreakMinutes = shift.BreakMinutes;
                        input.NightStartMinute = shift.NightStartMinute; input.NightEndMinute = shift.NightEndMinute;
                    }
                    else if (punches.Count >= 2)
                    {
                        if (!windowLoaded)
                        {
                            windowLoaded = true;
                            (int? ns, int? ne, string error) = await NightWindowAsync(ct);
                            if (error != null) result.Warnings.Add(error);
                            else { nightStart = ns; nightEnd = ne; }
                        }
                        if (nightStart.HasValue && nightEnd.HasValue) { input.NightStartMinute = nightStart.Value; input.NightEndMinute = nightEnd.Value; }
                    }
                    foreach (OfLeaveRequest l in myLeaves.Where(x => x.FromDate.Date <= date && x.ToDate.Date >= date))
                    {
                        switch (l.Kind)
                        {
                            case OfLeaveKind.DailyLeave:
                                input.DailyPaidLeave = true; break;
                            case OfLeaveKind.SickLeave:
                                input.DailySickLeave = true; break;
                            case OfLeaveKind.UnpaidLeave:
                                input.DailyUnpaidLeave = true; break;
                            case OfLeaveKind.HourlyLeave:
                                if (l.FromMinute.HasValue && l.ToMinute.HasValue) input.HourlyLeave.Add((l.FromMinute.Value, l.ToMinute.Value));
                                break;
                            case OfLeaveKind.Mission:
                                if (l.FromMinute.HasValue && l.ToMinute.HasValue) input.HourlyMission.Add((l.FromMinute.Value, l.ToMinute.Value));
                                else input.DailyMission = true;
                                break;
                            case OfLeaveKind.Overtime:
                                input.ApprovedOvertimeMinutes += (int)Math.Round((l.Hours ?? 0m) * 60m);
                                break;
                        }
                    }
                    DayResult r = AttendanceMath.Compute(input);
                    if (gapDay && r.Status == OfDayStatus.Absent) r.Note = GapDayNote;
                    if (!existing.TryGetValue((emp.Id, date), out OfAttendanceDay row))
                    {
                        row = new OfAttendanceDay { EmployeeId = emp.Id, Date = date };
                        _uow.Set<OfAttendanceDay>().Add(row);
                        existing[(emp.Id, date)] = row;
                    }
                    row.ShiftId = shift?.Id; row.Status = r.Status ?? OfDayStatus.Absent; row.NonWorkingDay = r.NonWorkingDay; row.FirstIn = r.FirstIn; row.LastOut = r.LastOut;
                    row.WorkedMinutes = r.WorkedMinutes; row.DelayMinutes = r.DelayMinutes; row.DelayDeductionMinutes = r.DelayDeductionMinutes; row.EarlyLeaveMinutes = r.EarlyLeaveMinutes;
                    row.EarlyLeaveDeductionMinutes = r.EarlyLeaveDeductionMinutes; row.OvertimeMinutes = r.OvertimeMinutes; row.UnapprovedOvertimeMinutes = r.UnapprovedOvertimeMinutes;
                    row.NightMinutes = r.NightMinutes; row.HolidayMinutes = r.HolidayMinutes; row.AbsenceDays = r.AbsenceDays; row.UnpaidLeaveDays = r.UnpaidLeaveDays;
                    row.PaidLeaveDays = r.PaidLeaveDays; row.SickLeaveDays = r.SickLeaveDays; row.MissionDays = r.MissionDays; row.Note = r.Note; row.ComputedAt = now;
                    result.Days++;
                    computedEmployees.Add(emp.Id);
                }
            }
            result.Employees = computedEmployees.Count;
            result.EmployeesWithoutShift = withoutShift.Count;
            bool removed = await RemoveStaleGapDaysAsync(from, to, employeeId, now, ct);
            if (removed || _uow.Set<OfAttendanceDay>().Local.Any(d => d.ComputedAt == now)) await _uow.SaveChangesAsync(ct);
            return result;
        }

        // ================================================================================== reading

        private static AttendanceDayDto ToDto(OfAttendanceDay d, Dictionary<int, FnEmployee> employees, Dictionary<int, string> shifts)
        {
            employees.TryGetValue(d.EmployeeId, out FnEmployee e);
            string shiftTitle = null;
            if (d.ShiftId.HasValue) shifts.TryGetValue(d.ShiftId.Value, out shiftTitle);
            return new AttendanceDayDto
            {
                EmployeeId = d.EmployeeId, EmployeeName = EmployeeName(e), PersonnelCode = e?.PersonnelCode, Date = d.Date, DateText = OfficeText.Date(d.Date), ShiftId = d.ShiftId, ShiftTitle = shiftTitle,
                Status = d.Status, NonWorkingDay = d.NonWorkingDay, FirstIn = d.FirstIn, LastOut = d.LastOut, WorkedMinutes = d.WorkedMinutes, DelayMinutes = d.DelayMinutes, DelayDeductionMinutes = d.DelayDeductionMinutes,
                EarlyLeaveMinutes = d.EarlyLeaveMinutes, EarlyLeaveDeductionMinutes = d.EarlyLeaveDeductionMinutes, OvertimeMinutes = d.OvertimeMinutes, UnapprovedOvertimeMinutes = d.UnapprovedOvertimeMinutes,
                NightMinutes = d.NightMinutes, HolidayMinutes = d.HolidayMinutes, AbsenceDays = d.AbsenceDays,
                UnpaidLeaveDays = d.UnpaidLeaveDays, PaidLeaveDays = d.PaidLeaveDays, SickLeaveDays = d.SickLeaveDays, MissionDays = d.MissionDays, Note = d.Note,
            };
        }

        public async Task<ResponseDto<PagedList<AttendanceDayDto>>> GetDaysAsync(DayQuery query, CancellationToken ct)
        {
            query ??= new DayQuery();
            IQueryable<OfAttendanceDay> q = _uow.Set<OfAttendanceDay>().AsNoTracking();
            if (CanReadAll) { if (query.EmployeeId.HasValue) q = q.Where(d => d.EmployeeId == query.EmployeeId.Value); }
            else
            {
                int? mine = await MyEmployeeIdAsync(ct);
                if (!mine.HasValue) return ResponseDtoHelper.Ok(F2Page.Make(new List<AttendanceDayDto>(), 0, 1, Math.Max(query.PageSize, 1)));
                q = q.Where(d => d.EmployeeId == mine.Value);
            }
            if (query.From.HasValue) { DateTime f = query.From.Value.Date; q = q.Where(d => d.Date >= f); }
            if (query.To.HasValue) { DateTime t = query.To.Value.Date; q = q.Where(d => d.Date <= t); }
            if (query.Status.HasValue) q = q.Where(d => d.Status == query.Status.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfAttendanceDay> rows = await q.OrderBy(d => d.Date).ThenBy(d => d.EmployeeId).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            Dictionary<int, FnEmployee> employees = await EmployeesAsync(rows.Select(d => d.EmployeeId), ct);
            Dictionary<int, string> shifts = await _uow.Set<OfShift>().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Title, ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(d => ToDto(d, employees, shifts)).ToList(), total, number, size));
        }

        public async Task<ResponseDto<List<WorkedHoursDto>>> GetWorkedHoursAsync(WorkedHoursQuery query, CancellationToken ct)
        {
            // release R1f: PAYROLL reads the presence hours too (it checks the work-order hours of a monthly sheet against them)
            if (!_user.IsInRole("HR,PAYROLL,CEO,PMO,PROJECTMANAGER,PLANNING,CONSTRUCTION,FINANCEMANAGER,PRODUCTIONMANAGER,SHOPSUPERVISOR")) return Bad<List<WorkedHoursDto>>(OfficeText.NotAllowed);
            if (query == null || query.EmployeeIds == null || query.EmployeeIds.Count == 0) return Bad<List<WorkedHoursDto>>(OfficeText.InvalidData);
            if (query.EmployeeIds.Count > 500 || query.To.Date < query.From.Date || (query.To.Date - query.From.Date).TotalDays > 93) return Bad<List<WorkedHoursDto>>("بازه یا تعداد پرسنل بیش از حد مجاز است.");
            List<int> ids = query.EmployeeIds.Distinct().ToList();
            DateTime f = query.From.Date, t = query.To.Date;
            List<OfAttendanceDay> rows = await _uow.Set<OfAttendanceDay>().AsNoTracking().Where(d => ids.Contains(d.EmployeeId) && d.Date >= f && d.Date <= t && d.WorkedMinutes > 0)
                .OrderBy(d => d.EmployeeId).ThenBy(d => d.Date).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(d => new WorkedHoursDto { EmployeeId = d.EmployeeId, Date = d.Date, WorkedMinutes = d.WorkedMinutes, WorkedHours = Math.Round(d.WorkedMinutes / 60m, 2) }).ToList());
        }

        // ================================================================================== monthly close (two steps: HR prepares, a second person confirms)

        /// <summary>The signed-in user may confirm this month: it is Prepared, the user did not prepare it, and the user is the finance manager (or a second HR user while the company policy allows it).</summary>
        private async Task<bool> MayConfirmAsync(OfAttendanceMonth month, CancellationToken ct)
        {
            if (month == null || month.Status != OfMonthStatus.Prepared || Me <= 0 || month.ClosedByUserId == Me) return false;
            if (_user.IsInRole(FinanceRoles.Managers)) return true;
            if (!IsHr) return false;
            return (await LoadPolicyAsync(ct)).MonthConfirmByHrPeer;
        }

        public async Task<ResponseDto<MonthStatusDto>> GetMonthStatusAsync(int year, int month, CancellationToken ct)
        {
            if (!_user.IsInRole("HR,CEO,PAYROLL,FINANCEMANAGER")) return Bad<MonthStatusDto>(OfficeText.NotAllowed);
            if (year < 1300 || year > 1600 || month < 1 || month > 12) return Bad<MonthStatusDto>(OfficeText.InvalidData);
            return ResponseDtoHelper.Ok(await BuildMonthStatusAsync(year, month, ct));
        }

        private async Task<MonthStatusDto> BuildMonthStatusAsync(int year, int month, CancellationToken ct)
        {
            OfAttendanceMonth closed = await _uow.Set<OfAttendanceMonth>().AsNoTracking().FirstOrDefaultAsync(m => m.Year == year && m.Month == month, ct);
            var status = new MonthStatusDto
            {
                Year = year, Month = month, Closed = closed != null && closed.Status != OfMonthStatus.Rejected, Status = closed == null ? "None" : closed.Status.ToString(),
                Confirmed = closed != null && closed.Status == OfMonthStatus.Confirmed, ClosedAt = closed?.ClosedAt, ClosedAtText = OfficeText.Stamp(closed?.ClosedAt), Sheets = closed?.SheetCount ?? 0,
                RejectReason = closed?.RejectReason, SnapshotHash = closed?.SnapshotHash, ConfirmedAt = closed?.ConfirmedAt, ConfirmedAtText = OfficeText.Stamp(closed?.ConfirmedAt),
                LockedByPayroll = await _uow.Set<FnPayrollRun>().AnyAsync(r => r.Year == year && r.Month == month && r.Status >= FnPayrollStatus.Approved, ct),
            };
            if (closed == null) return status;

            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { closed.ClosedByUserId, closed.ConfirmedByUserId }, ct);
            status.PreparedByName = names.Name(closed.ClosedByUserId);
            status.ConfirmedByName = names.Name(closed.ConfirmedByUserId);
            status.CanConfirm = await MayConfirmAsync(closed, ct);

            List<FnAttendanceSheet> sheets = await _uow.Set<FnAttendanceSheet>().AsNoTracking().Include(x => x.Employee).Where(x => x.Year == year && x.Month == month).ToListAsync(ct);
            status.Rows = sheets.OrderBy(x => x.Employee.PersonnelCode).Select(x => new SheetRowDto
            {
                EmployeeId = x.EmployeeId, EmployeeName = EmployeeName(x.Employee), PersonnelCode = x.Employee.PersonnelCode,
                DaysWorked = x.DaysWorked, OvertimeHours = x.OvertimeHours, NightHours = x.NightHours, HolidayHours = x.HolidayHours, PaidLeaveDays = x.PaidLeaveDays,
                SickLeaveDays = x.SickLeaveDays, UnpaidLeaveDays = x.UnpaidLeaveDays, AbsenceDays = x.AbsenceDays, MissionDays = x.MissionDays,
                Source = x.Source.ToString(), Locked = x.Locked,
            }).ToList();
            status.NoWorkshop = sheets.Where(x => x.Employee.WorkshopId == null).OrderBy(x => x.Employee.PersonnelCode)
                .Select(x => new NoWorkshopEmployeeDto { EmployeeId = x.EmployeeId, PersonnelCode = x.Employee.PersonnelCode, EmployeeName = EmployeeName(x.Employee) }).ToList();
            // the snapshot: a month closed before it existed has none (null + a note); otherwise the stored text must match its own hash (sheets' numbers and the policy values) and the sheets must still hold it
            if (closed.SnapshotJson == null) { status.SnapshotMatches = null; status.SnapshotNote = LegacyMonthNote; }
            else
                status.SnapshotMatches = string.Equals(PayrollHash.OfMonth(closed.SnapshotJson, closed.PolicyJson), closed.SnapshotHash, StringComparison.Ordinal)
                    && string.Equals(SheetNumbers.SnapshotOfHrClose(sheets), closed.SnapshotJson, StringComparison.Ordinal);
            status.Policy = PolicyOfMonth(closed);
            status.OvertimeRequiresApproval = status.Policy?.OvertimeRequiresApproval;
            status.EarlyLeaveDeduction = status.Policy?.EarlyLeaveDeduction;
            status.MonthConfirmByHrPeer = status.Policy?.MonthConfirmByHrPeer;
            if (closed.Status == OfMonthStatus.Prepared) status.PendingRequests = await PendingRequestsOfMonthAsync(year, month, ct);

            // the breakdown (overtime before the cap, delay, lateness and early-leave deduction, unapproved overtime) is read from the computed days of the month
            DateTime from = PersianCal.Start(year, month), to = PersianCal.End(year, month);
            List<int> ids = status.Rows.Select(r => r.EmployeeId).ToList();
            List<OfAttendanceDay> days = await _uow.Set<OfAttendanceDay>().AsNoTracking().Where(d => d.Date >= from && d.Date <= to && ids.Contains(d.EmployeeId)).ToListAsync(ct);
            Dictionary<int, int> scheduled = await ScheduledMinutesAsync(ct);
            foreach (SheetRowDto row in status.Rows)
                FillBreakdown(row, days.Where(d => d.EmployeeId == row.EmployeeId).ToList(), scheduled);
            return status;
        }

        private const string LegacyMonthNote = "بدون snapshot — ماه قدیمی";

        /// <summary>The attendance policy values the month is prepared with: frozen into the month (PolicyJson) next to the snapshot of the sheets.</summary>
        private static PolicySnapshotDto PolicySnapshotOf(OfAttendancePolicy p) => new PolicySnapshotDto
        {
            GraceMinutes = p.GraceMinutes, MaxOvertimeMinutesPerDay = p.MaxOvertimeMinutesPerDay, OvertimeRequiresApproval = p.OvertimeRequiresApproval, RoundingMinutes = p.RoundingMinutes,
            EarlyLeaveDeduction = !p.EarlyLeaveDeductionDisabled, MonthConfirmByHrPeer = p.MonthConfirmByHrPeer, MonthlyOvertimeCapMinutes = p.MonthlyOvertimeCapMinutes, DelayPenaltyFactor = p.DelayPenaltyFactor,
        };

        private static PolicySnapshotDto PolicyOfMonth(OfAttendanceMonth month)
        {
            if (month == null || string.IsNullOrWhiteSpace(month.PolicyJson)) return null;
            try { return JsonSerializer.Deserialize<PolicySnapshotDto>(month.PolicyJson); }
            catch (JsonException) { return null; }
        }

        // ---- requests of a month (release R1f review M3)

        /// <summary>
        /// The requests of the month that nobody has decided yet and that therefore count nowhere: manual clock requests and device punches held for the key holder, import batches with rows in the month,
        /// submitted leave requests that touch it. At most 200 of each kind are listed.
        /// </summary>
        private async Task<List<PendingRequestDto>> PendingRequestsOfMonthAsync(int year, int month, CancellationToken ct)
        {
            DateTime start = PersianCal.Start(year, month), next = PersianCal.End(year, month).AddDays(1);
            var list = new List<PendingRequestDto>();
            var clock = await _uow.Set<OfClockEvent>().AsNoTracking()
                .Where(e => e.Approval == OfClockApproval.Pending && e.ImportBatchId == null && e.Timestamp >= start && e.Timestamp < next)
                .OrderBy(e => e.Timestamp).ThenBy(e => e.Id).Select(e => new { e.Id, e.EmployeeId, e.Timestamp, e.Source }).Take(200).ToListAsync(ct);
            var batches = await _uow.Set<OfClockEvent>().AsNoTracking()
                .Where(e => e.Approval == OfClockApproval.Pending && e.ImportBatchId != null && e.Timestamp >= start && e.Timestamp < next)
                .GroupBy(e => e.ImportBatchId.Value).Select(g => new { Id = g.Key, Rows = g.Count() }).OrderBy(x => x.Id).Take(200).ToListAsync(ct);
            var leaves = await _uow.Set<OfLeaveRequest>().AsNoTracking()
                .Where(r => !r.Deleted && r.Status == OfLeaveStatus.Submitted && r.FromDate < next && r.ToDate >= start)
                .OrderBy(r => r.Id).Select(r => new { r.Id, r.Number, r.EmployeeId, r.FromDate }).Take(200).ToListAsync(ct);
            Dictionary<int, FnEmployee> employees = await EmployeesAsync(clock.Select(c => c.EmployeeId).Concat(leaves.Select(l => l.EmployeeId)), ct);
            string Who(int employeeId) => employees.TryGetValue(employeeId, out FnEmployee e) ? $"{e.PersonnelCode} - {EmployeeName(e)}" : "?";
            foreach (var c in clock)
                list.Add(new PendingRequestDto
                {
                    Kind = "ClockRequest", Id = unchecked((int)c.Id),
                    Text = c.Source == OfClockSource.Manual ? $"درخواست ثبت دستی تردد {Who(c.EmployeeId)} در {OfficeText.Stamp(c.Timestamp)}" : $"تردد دستگاه {Who(c.EmployeeId)} در {OfficeText.Stamp(c.Timestamp)} منتظر تایید نفر دوم است",
                });
            foreach (var b in batches)
                list.Add(new PendingRequestDto { Kind = "ImportBatch", Id = b.Id, Text = $"بسته ورود تردد شماره {b.Id} با {b.Rows} تردد در این ماه منتظر تصمیم است" });
            foreach (var l in leaves)
                list.Add(new PendingRequestDto { Kind = "Leave", Id = l.Id, Text = $"درخواست {l.Number} پرسنل {Who(l.EmployeeId)} از {OfficeText.Date(l.FromDate)} منتظر تصمیم است" });
            return list;
        }

        /// <summary>
        /// A Persian refusal when rows that the preparation could not know exist: approved punches, leave approvals or sheets created or changed after the month was prepared. Null when nothing is newer.
        /// The prepared month is frozen, so such rows would never reach payroll unless HR prepares the month again.
        /// </summary>
        private async Task<string> NewerRowsRefusalAsync(OfAttendanceMonth month, CancellationToken ct)
        {
            DateTime start = PersianCal.Start(month.Year, month.Month), next = PersianCal.End(month.Year, month.Month).AddDays(1);
            DateTimeOffset since = month.ClosedAt;
            int events = await _uow.Set<OfClockEvent>().AsNoTracking().CountAsync(e => e.Timestamp >= start && e.Timestamp < next && e.Approval == OfClockApproval.Approved && (e.CreatedAt > since || (e.ApprovedAt != null && e.ApprovedAt > since)), ct);
            int leaves = await _uow.Set<OfLeaveRequest>().AsNoTracking().CountAsync(r => !r.Deleted && r.FromDate < next && r.ToDate >= start && (r.Status == OfLeaveStatus.Approved || r.Status == OfLeaveStatus.Recorded) && r.ApprovedAt != null && r.ApprovedAt > since, ct);
            int sheets = await _uow.Set<FnAttendanceSheet>().AsNoTracking().CountAsync(s => s.Year == month.Year && s.Month == month.Month && s.UpdatedAt > since, ct);
            if (events + leaves + sheets == 0) return null;
            var parts = new List<string>();
            if (events > 0) parts.Add($"{events} تردد");
            if (leaves > 0) parts.Add($"{leaves} تایید مرخصی یا ماموریت");
            if (sheets > 0) parts.Add($"{sheets} برگه کارکرد");
            return $"پس از آماده‌سازی این ماه، {string.Join(" و ", parts)} ثبت یا تغییر کرده است که در کارکرد ثابت‌شده نیامده؛ بستن ماه را رد کنید تا منابع انسانی دوباره آماده کند.";
        }

        /// <summary>Scheduled minutes of each shift (end - start - break), to turn deducted minutes into a part of a day.</summary>
        private async Task<Dictionary<int, int>> ScheduledMinutesAsync(CancellationToken ct) =>
            (await _uow.Set<OfShift>().AsNoTracking().Select(s => new { s.Id, s.StartMinute, s.EndMinute, s.BreakMinutes }).ToListAsync(ct))
            .ToDictionary(s => s.Id, s => (s.EndMinute > s.StartMinute ? s.EndMinute - s.StartMinute : s.EndMinute + 1440 - s.StartMinute) - s.BreakMinutes);

        /// <summary>Fills the breakdown fields of a sheet row from the days of the month and returns the unrounded days deducted for lateness and early leave.</summary>
        private static decimal FillBreakdown(SheetRowDto row, List<OfAttendanceDay> days, Dictionary<int, int> scheduled)
        {
            decimal late = 0m;
            foreach (OfAttendanceDay d in days)
            {
                // the delay and the early leave of one day are deducted together, at most one day in all
                int deducted = d.DelayDeductionMinutes + d.EarlyLeaveDeductionMinutes;
                if (deducted > 0 && d.ShiftId.HasValue && scheduled.TryGetValue(d.ShiftId.Value, out int minutes)) late += AttendanceMath.DeductionDays(deducted, minutes);
            }
            row.OvertimeWorkedHours = Math.Round(days.Sum(d => d.OvertimeMinutes) / 60m, 2);
            row.DelayHours = Math.Round(days.Sum(d => d.DelayMinutes) / 60m, 2);
            row.DelayDeductionHours = Math.Round(days.Sum(d => d.DelayDeductionMinutes) / 60m, 2);
            row.EarlyLeaveDeductionHours = Math.Round(days.Sum(d => d.EarlyLeaveDeductionMinutes) / 60m, 2);
            row.UnapprovedOvertimeHours = Math.Round(days.Sum(d => d.UnapprovedOvertimeMinutes) / 60m, 2);
            row.LateDeductionDays = Math.Round(late, 2);
            return late;
        }

        /// <summary>An inbox item for the second person of the close; it closes when the month is confirmed or rejected.</summary>
        private async Task MonthGateAsync(int year, int month, bool waiting, CancellationToken ct)
        {
            if (_notify == null) return;
            try
            {
                string key = $"att:month:{year}-{month}";
                if (!waiting) { await _notify.CloseAsync(key, null, ct); return; }
                OfAttendancePolicy policy = await LoadPolicyAsync(ct);
                await _notify.NotifyAsync(new NotifyRequest
                {
                    SourceKey = key, Module = "office", SourceType = "attendance-month", SourceId = year * 100 + month,
                    Title = $"بستن حضور و غیاب ماه {year}/{month:00} منتظر تایید است", Link = $"/office/attendance/month?year={year}&month={month}",
                    Roles = policy.MonthConfirmByHrPeer ? FinanceRoles.Managers + "," + OfficeRoles.Hr : FinanceRoles.Managers, ReplaceRecipients = true,
                }, ct);
            }
            catch (Exception)
            {
                // the inbox must never block a gate
            }
        }

        public async Task<ResponseDto<CloseMonthResultDto>> CloseMonthAsync(CloseMonthDto dto, CancellationToken ct)
        {
            if (!IsHr) return Bad<CloseMonthResultDto>(OfficeText.NotAllowed);
            if (dto == null || dto.Year < 1300 || dto.Year > 1600 || dto.Month < 1 || dto.Month > 12) return Bad<CloseMonthResultDto>(OfficeText.InvalidData);
            DateTime start = PersianCal.Start(dto.Year, dto.Month);
            DateTime end = PersianCal.End(dto.Year, dto.Month);
            if (end >= DateTime.Today) return Bad<CloseMonthResultDto>("این ماه هنوز تمام نشده است؛ بستن ماه پس از آخرین روز آن ممکن است.");

            // step one of two: a month can be prepared when it was never closed or its close was rejected
            OfAttendanceMonth month = await _uow.Set<OfAttendanceMonth>().FirstOrDefaultAsync(m => m.Year == dto.Year && m.Month == dto.Month, ct);
            if (month != null && month.Status == OfMonthStatus.Confirmed)
                return Bad<CloseMonthResultDto>("حضور و غیاب این ماه تایید و بسته شده است؛ تغییر کارکرد فقط با درخواست اصلاح کارکرد (به تایید منابع انسانی) ممکن است.");
            if (month != null && month.Status == OfMonthStatus.Prepared)
                return Bad<CloseMonthResultDto>("این ماه آماده شده و منتظر تایید است؛ برای آماده‌سازی دوباره، ابتدا آن را رد کنید.");

            // the rate-table keys the close needs: nothing is assumed silently
            PayrollRates rates = await PayrollRateLoader.LoadAsync(_uow, dto.Year, ct);
            string missing = rates.Require(dto.Year, PayrollKeys.NightStart, PayrollKeys.NightEnd);
            if (missing != null) return Bad<CloseMonthResultDto>(missing);

            await ComputeCoreAsync(start, end, null, ct);
            List<OfAttendanceDay> days = await _uow.Set<OfAttendanceDay>().AsNoTracking().Where(d => d.Date >= start && d.Date <= end).ToListAsync(ct);
            int incomplete = days.Count(d => d.Status == OfDayStatus.Incomplete);
            if (incomplete > 0 && !dto.AcceptIncompleteAsAbsent)
                return Bad<CloseMonthResultDto>($"{incomplete} روز فقط یک تردد دارد. ترددها را اصلاح کنید (ثبت دستی یا فراموشی تردد) یا پذیرش آن‌ها به‌عنوان غیبت را تایید کنید.");

            List<int> ids = days.Select(d => d.EmployeeId).Distinct().ToList();
            OfAttendancePolicy policy = await LoadPolicyAsync(ct);
            PolicySnapshotDto frozenPolicy = PolicySnapshotOf(policy);
            Dictionary<int, int> scheduled = await ScheduledMinutesAsync(ct);
            Dictionary<int, FnEmployee> employees = await EmployeesAsync(ids, ct);
            // every sheet of the month (tracked): the ones this close writes, and the other HR-close sheets that go into the frozen snapshot
            Dictionary<int, FnAttendanceSheet> sheets = await _uow.Set<FnAttendanceSheet>().Where(s => s.Year == dto.Year && s.Month == dto.Month).ToDictionaryAsync(s => s.EmployeeId, ct);
            var result = new CloseMonthResultDto
            {
                Year = dto.Year, Month = dto.Month, IncompleteDays = incomplete, Policy = frozenPolicy, OvertimeRequiresApproval = frozenPolicy.OvertimeRequiresApproval,
                EarlyLeaveDeduction = frozenPolicy.EarlyLeaveDeduction, MonthConfirmByHrPeer = frozenPolicy.MonthConfirmByHrPeer,
            };
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (IGrouping<int, OfAttendanceDay> g in days.GroupBy(d => d.EmployeeId).OrderBy(x => x.Key))
            {
                if (!employees.TryGetValue(g.Key, out FnEmployee emp)) continue;
                if (await MonthLockedAsync(dto.Year, dto.Month, emp.WorkshopId, ct)) { result.SkippedLocked++; continue; }
                decimal worked = g.Where(d => d.Status == OfDayStatus.Present && !d.NonWorkingDay).Sum(d => 1m - d.PaidLeaveDays);
                var row = new SheetRowDto
                {
                    EmployeeId = emp.Id, EmployeeName = EmployeeName(emp), PersonnelCode = emp.PersonnelCode,
                    DaysWorked = Math.Round(worked, 2), NightHours = Math.Round(g.Sum(d => d.NightMinutes) / 60m, 2),
                    PaidLeaveDays = Math.Round(g.Sum(d => d.PaidLeaveDays), 2), SickLeaveDays = Math.Round(g.Sum(d => d.SickLeaveDays), 2),
                    UnpaidLeaveDays = g.Sum(d => (decimal)d.UnpaidLeaveDays), MissionDays = g.Sum(d => (decimal)d.MissionDays), Source = FnSheetSource.HrClose.ToString(), Locked = true,
                };
                // overtime and holiday work share one monthly cap (policy, default 22 hours), filled in date order;
                // absence: real absence + single-punch days + the lateness and early-leave deduction (once, as fractions of a day)
                decimal lateDays = FillBreakdown(row, g.ToList(), scheduled);
                (int overtimeMinutes, int holidayMinutes) = AttendanceMath.MonthlyExtraWork(g.Select(d => (d.Date, d.OvertimeMinutes, d.HolidayMinutes)), policy.MonthlyOvertimeCapMinutes);
                row.OvertimeHours = Math.Round(overtimeMinutes / 60m, 2);
                row.HolidayHours = Math.Round(holidayMinutes / 60m, 2);
                row.AbsenceDays = Math.Round(g.Sum(d => (decimal)d.AbsenceDays) + g.Count(d => d.Status == OfDayStatus.Incomplete) + lateDays, 2);
                if (!sheets.TryGetValue(emp.Id, out FnAttendanceSheet sheet))
                {
                    sheet = new FnAttendanceSheet { EmployeeId = emp.Id, Year = dto.Year, Month = dto.Month };
                    _uow.Set<FnAttendanceSheet>().Add(sheet);
                    sheets[emp.Id] = sheet;
                }
                sheet.DaysWorked = row.DaysWorked; sheet.OvertimeHours = row.OvertimeHours; sheet.NightHours = row.NightHours; sheet.HolidayHours = row.HolidayHours;
                sheet.PaidLeaveDays = row.PaidLeaveDays; sheet.SickLeaveDays = row.SickLeaveDays; sheet.UnpaidLeaveDays = row.UnpaidLeaveDays; sheet.AbsenceDays = row.AbsenceDays; sheet.MissionDays = row.MissionDays;
                sheet.Source = FnSheetSource.HrClose; sheet.Locked = true; sheet.EditorUserIds = LedgerMath.WithEditor(sheet.EditorUserIds, Me);
                sheet.UpdatedAt = now; sheet.UpdatedByUserId = Me;
                result.Rows.Add(row);
            }
            result.Sheets = result.Rows.Count;
            if (result.SkippedLocked > 0) result.Warnings.Add($"حقوق {result.SkippedLocked} نفر در این ماه تایید شده و کارکرد آن‌ها تغییر نکرد.");

            // employees without an insurance workshop: listed here, and the payroll run is blocked until a workshop is assigned
            result.NoWorkshop = result.Rows.Where(r => employees[r.EmployeeId].WorkshopId == null).Select(r => new NoWorkshopEmployeeDto { EmployeeId = r.EmployeeId, PersonnelCode = r.PersonnelCode, EmployeeName = r.EmployeeName }).ToList();
            if (result.NoWorkshop.Count > 0)
                result.Warnings.Add($"{result.NoWorkshop.Count} نفر کارگاه بیمه ندارند؛ تا تعیین کارگاه، محاسبه حقوق برای آن‌ها ممکن نیست.");

            // the gap days (see InAssignmentSpan): per employee, so HR sees who gets absence days only because of the hole between two shift assignments
            result.GapDays = days.Where(d => d.Note == GapDayNote && employees.ContainsKey(d.EmployeeId)).GroupBy(d => d.EmployeeId).OrderBy(g => employees[g.Key].PersonnelCode, StringComparer.Ordinal)
                .Select(g => new GapDaysDto
                {
                    EmployeeId = g.Key, PersonnelCode = employees[g.Key].PersonnelCode, EmployeeName = EmployeeName(employees[g.Key]), Count = g.Count(),
                    Dates = g.Select(d => d.Date).OrderBy(d => d).ToList(), DateTexts = g.Select(d => d.Date).OrderBy(d => d).Select(d => OfficeText.Date(d)).ToList(),
                }).ToList();
            if (result.GapDays.Count > 0)
                result.Warnings.Add($"{result.GapDays.Sum(g => g.Count)} روز برای {result.GapDays.Count} نفر میان تخصیص‌های شیفت بدون شیفت و بدون تردد بود و غایب محسوب شد؛ فهرست روزها در نتیجه آمده است.");

            // requests nobody has decided: they count nowhere, and the confirmation is refused while any is open
            result.PendingRequests = await PendingRequestsOfMonthAsync(dto.Year, dto.Month, ct);
            if (result.PendingRequests.Count > 0)
                result.Warnings.Add($"{result.PendingRequests.Count} درخواست این ماه (تردد دستی، بسته ورود تردد یا مرخصی) هنوز تصمیم‌گیری نشده و در کارکرد نیامده است؛ تا تعیین تکلیف آن‌ها تایید ماه ممکن نیست.");

            // the frozen snapshot: exactly the numbers of the HR-close sheets as they are written now, and the policy values they were computed under; the confirmation checks the sheets still hold them
            string snapshot = SheetNumbers.SnapshotOfHrClose(sheets.Values);
            string policyJson = JsonSerializer.Serialize(frozenPolicy);
            if (month == null) { month = new OfAttendanceMonth { Year = dto.Year, Month = dto.Month }; _uow.Set<OfAttendanceMonth>().Add(month); }
            month.ClosedAt = now; month.ClosedByUserId = Me; month.SheetCount = result.Sheets; month.SkippedLocked = result.SkippedLocked;
            month.Status = OfMonthStatus.Prepared; month.ConfirmedAt = null; month.ConfirmedByUserId = null; month.RejectReason = null;
            month.SnapshotJson = snapshot; month.PolicyJson = policyJson; month.SnapshotHash = PayrollHash.OfMonth(snapshot, policyJson);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<CloseMonthResultDto>(OfficeText.Concurrency); }
            catch (DbUpdateException) { return Bad<CloseMonthResultDto>("ذخیره کارکرد ماه انجام نشد؛ دوباره تلاش کنید."); }
            result.Status = month.Status.ToString();
            result.SnapshotHash = month.SnapshotHash;
            await MonthGateAsync(dto.Year, dto.Month, true, ct);
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<MonthStatusDto>> ConfirmMonthAsync(ConfirmMonthDto dto, CancellationToken ct)
        {
            if (dto == null || dto.Year < 1300 || dto.Year > 1600 || dto.Month < 1 || dto.Month > 12) return Bad<MonthStatusDto>(OfficeText.InvalidData);
            bool finance = _user.IsInRole(FinanceRoles.Managers);
            OfAttendancePolicy policy = await LoadPolicyAsync(ct);
            if (!finance && !(IsHr && policy.MonthConfirmByHrPeer)) return Bad<MonthStatusDto>("تایید بستن حضور و غیاب ماه با مدیر مالی است.");
            OfAttendanceMonth month = await _uow.Set<OfAttendanceMonth>().FirstOrDefaultAsync(m => m.Year == dto.Year && m.Month == dto.Month, ct);
            if (month == null) return OfficeText.NotFound<MonthStatusDto>("این ماه هنوز آماده‌سازی نشده است.");
            if (month.Status == OfMonthStatus.Confirmed) return Bad<MonthStatusDto>("حضور و غیاب این ماه قبلاً تایید شده است.");
            if (month.Status == OfMonthStatus.Rejected) return Bad<MonthStatusDto>("بستن این ماه رد شده و منتظر آماده‌سازی دوباره منابع انسانی است.");
            if (month.ClosedByUserId == Me) return Bad<MonthStatusDto>("آماده‌کننده ماه نمی‌تواند همان ماه را تایید کند؛ تایید با کاربر دیگری است.");

            // requests of the month that nobody decided count nowhere, and rows created after the preparation are not in the frozen numbers: either way HR prepares the month again
            List<PendingRequestDto> pending = await PendingRequestsOfMonthAsync(dto.Year, dto.Month, ct);
            if (pending.Count > 0)
                return Bad<MonthStatusDto>($"{pending.Count} درخواست این ماه (تردد دستی، بسته ورود تردد یا مرخصی) هنوز تصمیم‌گیری نشده است و در کارکرد ثابت‌شده نیامده؛ آن‌ها را رد کنید یا بستن ماه را رد کنید تا پس از تصمیم‌گیری دوباره آماده شود.");
            string newer = await NewerRowsRefusalAsync(month, ct);
            if (newer != null) return Bad<MonthStatusDto>(newer);

            // the stored snapshot must match its own hash, and the sheets must still hold exactly the numbers that were frozen when HR prepared the month
            List<FnAttendanceSheet> sheets = await _uow.Set<FnAttendanceSheet>().AsNoTracking().Where(s => s.Year == dto.Year && s.Month == dto.Month).ToListAsync(ct);
            if (month.SnapshotJson != null && !string.Equals(PayrollHash.OfMonth(month.SnapshotJson, month.PolicyJson), month.SnapshotHash, StringComparison.Ordinal))
                return Bad<MonthStatusDto>("snapshot ثبت‌شده با مهر (hash) خود یکی نیست؛ ماه را رد کنید تا منابع انسانی دوباره آماده کند.");
            if (!string.Equals(SheetNumbers.SnapshotOfHrClose(sheets), month.SnapshotJson, StringComparison.Ordinal))
                return Bad<MonthStatusDto>("ارقام برگه‌های کارکرد با آنچه هنگام آماده‌سازی ثبت شد یکی نیست؛ ماه را رد کنید تا منابع انسانی دوباره آماده کند.");

            month.Status = OfMonthStatus.Confirmed; month.ConfirmedAt = DateTimeOffset.Now; month.ConfirmedByUserId = Me; month.RejectReason = null;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<MonthStatusDto>(OfficeText.Concurrency); }
            await MonthGateAsync(dto.Year, dto.Month, false, ct);
            return ResponseDtoHelper.Ok(await BuildMonthStatusAsync(dto.Year, dto.Month, ct));
        }

        public async Task<ResponseDto<MonthStatusDto>> RejectMonthAsync(RejectMonthDto dto, CancellationToken ct)
        {
            if (dto == null || dto.Year < 1300 || dto.Year > 1600 || dto.Month < 1 || dto.Month > 12) return Bad<MonthStatusDto>(OfficeText.InvalidData);
            string reason = OfficeText.Cap(Clean(dto.Reason), 1000);
            if (reason == null) return Bad<MonthStatusDto>("دلیل رد را بنویسید.");
            OfAttendanceMonth month = await _uow.Set<OfAttendanceMonth>().FirstOrDefaultAsync(m => m.Year == dto.Year && m.Month == dto.Month, ct);
            if (month == null) return OfficeText.NotFound<MonthStatusDto>("این ماه هنوز آماده‌سازی نشده است.");
            if (month.Status == OfMonthStatus.Rejected) return Bad<MonthStatusDto>("بستن این ماه قبلاً رد شده است.");
            bool finance = _user.IsInRole(FinanceRoles.Managers);
            if (month.Status == OfMonthStatus.Prepared)
            {
                // the confirmer, or HR taking its own preparation back
                if (!finance && !IsHr) return Bad<MonthStatusDto>(OfficeText.NotAllowed);
            }
            else
            {
                // a confirmed month is reopened only by the finance manager, and not once a payroll of the month is approved
                if (!finance) return Bad<MonthStatusDto>("بازگشایی ماه تاییدشده فقط با مدیر مالی است.");
                if (await _uow.Set<FnPayrollRun>().AnyAsync(r => r.Year == dto.Year && r.Month == dto.Month && r.Status >= FnPayrollStatus.Approved, ct))
                    return Bad<MonthStatusDto>("حقوق این ماه تایید شده است؛ ماه بازگشایی نمی‌شود.");
            }
            month.Status = OfMonthStatus.Rejected; month.RejectReason = reason; month.ConfirmedAt = null; month.ConfirmedByUserId = null;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<MonthStatusDto>(OfficeText.Concurrency); }
            await MonthGateAsync(dto.Year, dto.Month, false, ct);
            return ResponseDtoHelper.Ok(await BuildMonthStatusAsync(dto.Year, dto.Month, ct));
        }

        // ================================================================================== corrections of locked sheets (payroll proposes, HR decides)

        public async Task<ResponseDto<PagedList<AttendanceCorrectionDto>>> GetCorrectionsAsync(CorrectionQuery query, CancellationToken ct)
        {
            query ??= new CorrectionQuery();
            if (!_user.IsInRole("HR,CEO,PAYROLL,FINANCEMANAGER")) return Bad<PagedList<AttendanceCorrectionDto>>(OfficeText.NotAllowed);
            IQueryable<FnAttendanceCorrection> q = _uow.Set<FnAttendanceCorrection>().AsNoTracking();
            if (query.Year.HasValue) q = q.Where(c => c.Year == query.Year.Value);
            if (query.Month.HasValue) q = q.Where(c => c.Month == query.Month.Value);
            if (query.EmployeeId.HasValue) q = q.Where(c => c.EmployeeId == query.EmployeeId.Value);
            if (query.Status.HasValue) { FnChangeStatus st = (FnChangeStatus)query.Status.Value; q = q.Where(c => c.Status == st); }
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<FnAttendanceCorrection> rows = await q.OrderByDescending(c => c.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await CorrectionDtosAsync(rows, ct), total, number, size));
        }

        private async Task<List<AttendanceCorrectionDto>> CorrectionDtosAsync(List<FnAttendanceCorrection> rows, CancellationToken ct)
        {
            Dictionary<int, FnEmployee> employees = await EmployeesAsync(rows.Select(c => c.EmployeeId), ct);
            Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, rows.Select(c => (int?)c.ProposedByUserId).Concat(rows.Select(c => c.DecidedByUserId)), ct);
            bool ceo = _user.IsInRole(FinanceRoles.Ceo);
            HashSet<int> managerRecords = rows.Any(c => c.Status == FnChangeStatus.Pending) ? await SelfApproval.FinanceManagerEmployeesAsync(_uow, employees.Values, ct) : new HashSet<int>();
            return rows.Select(c =>
            {
                employees.TryGetValue(c.EmployeeId, out FnEmployee e);
                var dto = new AttendanceCorrectionDto
                {
                    Id = c.Id, SheetId = c.SheetId, EmployeeId = c.EmployeeId, PersonnelCode = e?.PersonnelCode, EmployeeName = EmployeeName(e), Year = c.Year, Month = c.Month,
                    Current = SheetNumbers.FromJson(c.CurrentJson), Proposed = SheetNumbers.FromJson(c.ProposedJson), Reason = c.Reason, Status = c.Status.ToString(),
                    ProposedByName = users.Name(c.ProposedByUserId), ProposedAt = c.ProposedAt, DecidedByName = users.Name(c.DecidedByUserId), DecidedAt = c.DecidedAt, DecisionNote = c.DecisionNote,
                };
                // the proposer never decides, and nobody decides a correction of his own sheet; the sheet of a finance manager may also be decided by the CEO
                bool own = e != null && e.UserId.HasValue && Me > 0 && e.UserId.Value == Me;
                bool mayDecide = IsHr || (ceo && managerRecords.Contains(c.EmployeeId));
                if (c.Status == FnChangeStatus.Pending && mayDecide && c.ProposedByUserId != Me && !own) { dto.AllowedActions.Add("approve"); dto.AllowedActions.Add("reject"); }
                return dto;
            }).ToList();
        }

        public async Task<ResponseDto<AttendanceCorrectionDto>> CorrectionActionAsync(CorrectionActionDto dto, CancellationToken ct)
        {
            // HR decides; the CEO only on the correction of a finance manager's own sheet (review H2)
            if (!IsHr && !_user.IsInRole(FinanceRoles.Ceo)) return Bad<AttendanceCorrectionDto>(OfficeText.NotAllowed);
            if (dto == null || dto.Id <= 0) return Bad<AttendanceCorrectionDto>(OfficeText.InvalidData);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            if (action != "approve" && action != "reject") return Bad<AttendanceCorrectionDto>("عملیات باید تایید یا رد باشد.");
            FnAttendanceCorrection c = await _uow.Set<FnAttendanceCorrection>().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
            if (c == null) return OfficeText.NotFound<AttendanceCorrectionDto>("درخواست اصلاح پیدا نشد.");
            if (c.Status != FnChangeStatus.Pending) return Bad<AttendanceCorrectionDto>("درباره این درخواست قبلاً تصمیم گرفته شده است.");
            if (c.ProposedByUserId == Me) return Bad<AttendanceCorrectionDto>("پیشنهاددهنده اصلاح نمی‌تواند خودش آن را تایید یا رد کند؛ کاربر دیگری از منابع انسانی تصمیم می‌گیرد.");
            FnEmployee subject = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.EmployeeId, ct);
            string selfRefusal = await SelfApproval.RefusalAsync(_uow, _user, subject, IsHr, OfficeText.NotAllowed, false, ct);
            if (selfRefusal != null) return Bad<AttendanceCorrectionDto>(selfRefusal);
            string note = OfficeText.Cap(Clean(dto.Note), 500);
            if (action == "reject" && note == null) return Bad<AttendanceCorrectionDto>("دلیل رد را بنویسید.");
            DateTimeOffset now = DateTimeOffset.Now;
            if (action == "approve")
            {
                FnAttendanceSheet sheet = await _uow.Set<FnAttendanceSheet>().FirstOrDefaultAsync(s => s.Id == c.SheetId, ct);
                FnEmployee emp = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == c.EmployeeId, ct);
                if (sheet == null || emp == null) return OfficeText.NotFound<AttendanceCorrectionDto>("برگه کارکرد پیدا نشد.");
                OfAttendanceMonth monthRow = await _uow.Set<OfAttendanceMonth>().FirstOrDefaultAsync(m => m.Year == c.Year && m.Month == c.Month, ct);
                if (monthRow == null || monthRow.Status != OfMonthStatus.Confirmed) return Bad<AttendanceCorrectionDto>("اصلاح فقط برای ماهی است که بستن حضور و غیاب آن تایید شده؛ در غیر این صورت ماه را رد و دوباره آماده کنید.");
                if (await MonthLockedAsync(c.Year, c.Month, emp.WorkshopId, ct)) return Bad<AttendanceCorrectionDto>("حقوق این ماه تایید شده است؛ کارکرد قابل اصلاح نیست.");
                if (!SheetNumbers.Same(SheetNumbers.Of(sheet), SheetNumbers.FromJson(c.CurrentJson)))
                    return Bad<AttendanceCorrectionDto>("کارکرد این برگه پس از ثبت درخواست تغییر کرده است؛ این درخواست را رد کنید تا درخواست تازه‌ای ثبت شود.");
                SheetNumbersDto proposed = SheetNumbers.FromJson(c.ProposedJson);
                OfAttendancePolicy policy = await LoadPolicyAsync(ct);
                string error = SheetNumbers.Validate(proposed, c.Year, c.Month, policy.MonthlyOvertimeCapMinutes / 60m);
                if (error != null) return Bad<AttendanceCorrectionDto>(error);
                // review L6: the correction is applied only to a month whose sheets still hold the frozen numbers, and the snapshot is frozen again with the corrected numbers (the policy values stay),
                // so the verification that payroll runs keeps passing and the status shows the match. A month closed before the snapshot existed has none and gets none.
                List<FnAttendanceSheet> monthSheets = await _uow.Set<FnAttendanceSheet>().Where(s => s.Year == c.Year && s.Month == c.Month).ToListAsync(ct);
                if (monthRow.SnapshotJson != null)
                {
                    if (!string.Equals(PayrollHash.OfMonth(monthRow.SnapshotJson, monthRow.PolicyJson), monthRow.SnapshotHash, StringComparison.Ordinal)
                        || !string.Equals(SheetNumbers.SnapshotOfHrClose(monthSheets), monthRow.SnapshotJson, StringComparison.Ordinal))
                        return Bad<AttendanceCorrectionDto>("ارقام برگه‌های این ماه با snapshot بستن ماه یکی نیست؛ اصلاح اعمال نمی‌شود. بستن ماه را رد کنید تا منابع انسانی دوباره آماده کند.");
                }
                SheetNumbers.Apply(proposed, sheet);
                // a sheet that payroll typed (not written by the HR close) comes under the close with its approved correction
                if (sheet.Source != FnSheetSource.HrClose) { sheet.Source = FnSheetSource.HrClose; sheet.Locked = true; }
                sheet.UpdatedAt = now; sheet.UpdatedByUserId = Me;
                // both the proposer and the approver touched the numbers: neither may approve the payroll run that uses them
                sheet.EditorUserIds = LedgerMath.WithEditor(LedgerMath.WithEditor(sheet.EditorUserIds, c.ProposedByUserId), Me);
                if (monthRow.SnapshotJson != null)
                {
                    monthRow.SnapshotJson = SheetNumbers.SnapshotOfHrClose(monthSheets);
                    monthRow.SnapshotHash = PayrollHash.OfMonth(monthRow.SnapshotJson, monthRow.PolicyJson);
                }
                c.Status = FnChangeStatus.Approved;
            }
            else c.Status = FnChangeStatus.Rejected;
            c.DecidedByUserId = Me; c.DecidedAt = now; c.DecisionNote = note;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<AttendanceCorrectionDto>(OfficeText.Concurrency); }
            await GateAsync("att:corr:" + c.Id, false, null, null, null, ct, c.Id);
            return ResponseDtoHelper.Ok((await CorrectionDtosAsync(new List<FnAttendanceCorrection> { c }, ct))[0]);
        }
    }
}
