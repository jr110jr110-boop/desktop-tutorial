using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
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
            return await ComputeCoreAsync(today.AddDays(-3), today.AddDays(-1), null, ct);
        }

        private async Task<OfAttendancePolicy> LoadPolicyAsync(CancellationToken ct)
        {
            await EnsureCompanyPolicyAsync(ct);
            return await _uow.Set<OfAttendancePolicy>().AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct) ?? new OfAttendancePolicy();
        }

        /// <summary>Computes (or recomputes) the days of the range for every employee that has a shift or clock events. Today and the future are not computed; locked months are left alone.</summary>
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

            // one day more on each side: the boundary between two days depends on the shift of the neighbour
            DateTime assignFrom = from.AddDays(-1), assignTo = to.AddDays(1);
            IQueryable<OfShiftAssignment> assignQuery = _uow.Set<OfShiftAssignment>().AsNoTracking().Where(a => a.FromDate <= assignTo && (a.ToDate == null || a.ToDate >= assignFrom));
            if (employeeId.HasValue) assignQuery = assignQuery.Where(a => a.EmployeeId == employeeId.Value);
            List<OfShiftAssignment> assignments = await assignQuery.ToListAsync(ct);

            DateTime eventFrom = from.AddDays(-1);
            DateTime eventTo = to.AddDays(3);
            IQueryable<OfClockEvent> eventQuery = _uow.Set<OfClockEvent>().AsNoTracking().Where(e => e.Timestamp >= eventFrom && e.Timestamp < eventTo);
            if (employeeId.HasValue) eventQuery = eventQuery.Where(e => e.EmployeeId == employeeId.Value);
            var events = (await eventQuery.Select(e => new { e.EmployeeId, e.Timestamp }).ToListAsync(ct))
                .GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => g.Select(e => Minute(e.Timestamp)).Distinct().OrderBy(t => t).ToList());

            HashSet<int> ids = assignments.Select(a => a.EmployeeId).Concat(events.Keys).ToHashSet();
            if (employeeId.HasValue) ids.IntersectWith(new[] { employeeId.Value });
            if (ids.Count == 0) return result;
            List<int> idList = ids.ToList();

            List<EmpInfo> employees = (await _uow.Set<FnEmployee>().AsNoTracking().Where(e => idList.Contains(e.Id))
                .Select(e => new { e.Id, e.HireDate, e.EndDate, e.WorkshopId }).ToListAsync(ct))
                .Select(e => new EmpInfo { Id = e.Id, HireDate = e.HireDate.Date, EndDate = e.EndDate?.Date, WorkshopId = e.WorkshopId }).ToList();

            List<OfLeaveRequest> leaves = await _uow.Set<OfLeaveRequest>().AsNoTracking()
                .Where(r => !r.Deleted && (r.Status == OfLeaveStatus.Approved || r.Status == OfLeaveStatus.Recorded) && r.Kind != OfLeaveKind.ForgotClock
                    && r.FromDate <= to && r.ToDate >= from && idList.Contains(r.EmployeeId)).ToListAsync(ct);

            int firstYear = PersianCal.Year(from), lastYear = PersianCal.Year(to);
            List<PayrollLock> locks = (await _uow.Set<FnPayrollRun>().AsNoTracking().Where(r => r.Status >= FnPayrollStatus.Approved && r.Year >= firstYear && r.Year <= lastYear)
                .Select(r => new { r.Year, r.Month, r.WorkshopId }).ToListAsync(ct)).Select(r => new PayrollLock { Year = r.Year, Month = r.Month, WorkshopId = r.WorkshopId }).ToList();

            Dictionary<(int, DateTime), OfAttendanceDay> existing = (await _uow.Set<OfAttendanceDay>().Where(d => d.Date >= from && d.Date <= to && idList.Contains(d.EmployeeId)).ToListAsync(ct))
                .ToDictionary(d => (d.EmployeeId, d.Date.Date));

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
                    if (shift == null && punches.Count == 0) continue;
                    if (Locked(locks, date, emp.WorkshopId)) { result.SkippedLocked++; continue; }

                    var input = new DayInput
                    {
                        Date = date, HasShift = shift != null, NonWorking = !calendar.IsWorkingDay(date, shift), Punches = punches,
                        GraceMinutes = policy.GraceMinutes, MaxOvertimeMinutes = policy.MaxOvertimeMinutesPerDay, OvertimeNeedsPreApproval = policy.OvertimeNeedsPreApproval, RoundingMinutes = policy.RoundingMinutes,
                        DelayPenaltyFactor = policy.DelayPenaltyFactor,
                    };
                    if (shift != null)
                    {
                        input.StartMinute = shift.StartMinute; input.EndMinute = shift.EndMinute; input.BreakMinutes = shift.BreakMinutes;
                        input.NightStartMinute = shift.NightStartMinute; input.NightEndMinute = shift.NightEndMinute;
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
                    if (!existing.TryGetValue((emp.Id, date), out OfAttendanceDay row))
                    {
                        row = new OfAttendanceDay { EmployeeId = emp.Id, Date = date };
                        _uow.Set<OfAttendanceDay>().Add(row);
                        existing[(emp.Id, date)] = row;
                    }
                    row.ShiftId = shift?.Id; row.Status = r.Status ?? OfDayStatus.Absent; row.NonWorkingDay = r.NonWorkingDay; row.FirstIn = r.FirstIn; row.LastOut = r.LastOut;
                    row.WorkedMinutes = r.WorkedMinutes; row.DelayMinutes = r.DelayMinutes; row.DelayDeductionMinutes = r.DelayDeductionMinutes; row.EarlyLeaveMinutes = r.EarlyLeaveMinutes; row.OvertimeMinutes = r.OvertimeMinutes;
                    row.NightMinutes = r.NightMinutes; row.HolidayMinutes = r.HolidayMinutes; row.AbsenceDays = r.AbsenceDays; row.UnpaidLeaveDays = r.UnpaidLeaveDays;
                    row.PaidLeaveDays = r.PaidLeaveDays; row.SickLeaveDays = r.SickLeaveDays; row.MissionDays = r.MissionDays; row.Note = r.Note; row.ComputedAt = now;
                    result.Days++;
                    computedEmployees.Add(emp.Id);
                }
            }
            result.Employees = computedEmployees.Count;
            result.EmployeesWithoutShift = withoutShift.Count;
            if (_uow.Set<OfAttendanceDay>().Local.Any(d => d.ComputedAt == now)) await _uow.SaveChangesAsync(ct);
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
                EarlyLeaveMinutes = d.EarlyLeaveMinutes, OvertimeMinutes = d.OvertimeMinutes, NightMinutes = d.NightMinutes, HolidayMinutes = d.HolidayMinutes, AbsenceDays = d.AbsenceDays,
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
            if (!_user.IsInRole("HR,CEO,PMO,PROJECTMANAGER,PLANNING,CONSTRUCTION,FINANCEMANAGER,PRODUCTIONMANAGER,SHOPSUPERVISOR")) return Bad<List<WorkedHoursDto>>(OfficeText.NotAllowed);
            if (query == null || query.EmployeeIds == null || query.EmployeeIds.Count == 0) return Bad<List<WorkedHoursDto>>(OfficeText.InvalidData);
            if (query.EmployeeIds.Count > 500 || query.To.Date < query.From.Date || (query.To.Date - query.From.Date).TotalDays > 93) return Bad<List<WorkedHoursDto>>("بازه یا تعداد پرسنل بیش از حد مجاز است.");
            List<int> ids = query.EmployeeIds.Distinct().ToList();
            DateTime f = query.From.Date, t = query.To.Date;
            List<OfAttendanceDay> rows = await _uow.Set<OfAttendanceDay>().AsNoTracking().Where(d => ids.Contains(d.EmployeeId) && d.Date >= f && d.Date <= t && d.WorkedMinutes > 0)
                .OrderBy(d => d.EmployeeId).ThenBy(d => d.Date).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(d => new WorkedHoursDto { EmployeeId = d.EmployeeId, Date = d.Date, WorkedMinutes = d.WorkedMinutes, WorkedHours = Math.Round(d.WorkedMinutes / 60m, 2) }).ToList());
        }

        // ================================================================================== monthly close

        public async Task<ResponseDto<MonthStatusDto>> GetMonthStatusAsync(int year, int month, CancellationToken ct)
        {
            if (!_user.IsInRole("HR,CEO,PAYROLL,FINANCEMANAGER")) return Bad<MonthStatusDto>(OfficeText.NotAllowed);
            if (year < 1300 || year > 1600 || month < 1 || month > 12) return Bad<MonthStatusDto>(OfficeText.InvalidData);
            OfAttendanceMonth closed = await _uow.Set<OfAttendanceMonth>().AsNoTracking().FirstOrDefaultAsync(m => m.Year == year && m.Month == month, ct);
            var status = new MonthStatusDto
            {
                Year = year, Month = month, Closed = closed != null, ClosedAt = closed?.ClosedAt, ClosedAtText = OfficeText.Stamp(closed?.ClosedAt), Sheets = closed?.SheetCount ?? 0,
                LockedByPayroll = await _uow.Set<FnPayrollRun>().AnyAsync(r => r.Year == year && r.Month == month && r.Status >= FnPayrollStatus.Approved, ct),
            };
            if (closed != null)
            {
                List<FnAttendanceSheet> sheets = await _uow.Set<FnAttendanceSheet>().AsNoTracking().Include(x => x.Employee).Where(x => x.Year == year && x.Month == month).ToListAsync(ct);
                status.Rows = sheets.OrderBy(x => x.Employee.PersonnelCode).Select(x => new SheetRowDto
                {
                    EmployeeId = x.EmployeeId, EmployeeName = EmployeeName(x.Employee), PersonnelCode = x.Employee.PersonnelCode,
                    DaysWorked = x.DaysWorked, OvertimeHours = x.OvertimeHours, NightHours = x.NightHours, HolidayHours = x.HolidayHours, PaidLeaveDays = x.PaidLeaveDays,
                    SickLeaveDays = x.SickLeaveDays, UnpaidLeaveDays = x.UnpaidLeaveDays, AbsenceDays = x.AbsenceDays, MissionDays = x.MissionDays,
                }).ToList();
                // the breakdown (overtime before the cap, delay, lateness deduction) is read from the computed days of the month
                DateTime from = PersianCal.Start(year, month), to = PersianCal.End(year, month);
                List<int> ids = status.Rows.Select(r => r.EmployeeId).ToList();
                List<OfAttendanceDay> days = await _uow.Set<OfAttendanceDay>().AsNoTracking().Where(d => d.Date >= from && d.Date <= to && ids.Contains(d.EmployeeId)).ToListAsync(ct);
                Dictionary<int, int> scheduled = await ScheduledMinutesAsync(ct);
                foreach (SheetRowDto row in status.Rows)
                    FillBreakdown(row, days.Where(d => d.EmployeeId == row.EmployeeId).ToList(), scheduled);
            }
            return ResponseDtoHelper.Ok(status);
        }

        /// <summary>Scheduled minutes of each shift (end - start - break), to turn deducted minutes into a part of a day.</summary>
        private async Task<Dictionary<int, int>> ScheduledMinutesAsync(CancellationToken ct) =>
            (await _uow.Set<OfShift>().AsNoTracking().Select(s => new { s.Id, s.StartMinute, s.EndMinute, s.BreakMinutes }).ToListAsync(ct))
            .ToDictionary(s => s.Id, s => (s.EndMinute > s.StartMinute ? s.EndMinute - s.StartMinute : s.EndMinute + 1440 - s.StartMinute) - s.BreakMinutes);

        /// <summary>Fills the breakdown fields of a sheet row from the days of the month and returns the unrounded days deducted for lateness.</summary>
        private static decimal FillBreakdown(SheetRowDto row, List<OfAttendanceDay> days, Dictionary<int, int> scheduled)
        {
            decimal late = 0m;
            foreach (OfAttendanceDay d in days)
                if (d.DelayDeductionMinutes > 0 && d.ShiftId.HasValue && scheduled.TryGetValue(d.ShiftId.Value, out int minutes)) late += AttendanceMath.DeductionDays(d.DelayDeductionMinutes, minutes);
            row.OvertimeWorkedHours = Math.Round(days.Sum(d => d.OvertimeMinutes) / 60m, 2);
            row.DelayHours = Math.Round(days.Sum(d => d.DelayMinutes) / 60m, 2);
            row.DelayDeductionHours = Math.Round(days.Sum(d => d.DelayDeductionMinutes) / 60m, 2);
            row.LateDeductionDays = Math.Round(late, 2);
            return late;
        }

        public async Task<ResponseDto<CloseMonthResultDto>> CloseMonthAsync(CloseMonthDto dto, CancellationToken ct)
        {
            if (!IsHr) return Bad<CloseMonthResultDto>(OfficeText.NotAllowed);
            if (dto == null || dto.Year < 1300 || dto.Year > 1600 || dto.Month < 1 || dto.Month > 12) return Bad<CloseMonthResultDto>(OfficeText.InvalidData);
            DateTime start = PersianCal.Start(dto.Year, dto.Month);
            DateTime end = PersianCal.End(dto.Year, dto.Month);
            if (end >= DateTime.Today) return Bad<CloseMonthResultDto>("این ماه هنوز تمام نشده است؛ بستن ماه پس از آخرین روز آن ممکن است.");

            await ComputeCoreAsync(start, end, null, ct);
            List<OfAttendanceDay> days = await _uow.Set<OfAttendanceDay>().AsNoTracking().Where(d => d.Date >= start && d.Date <= end).ToListAsync(ct);
            int incomplete = days.Count(d => d.Status == OfDayStatus.Incomplete);
            if (incomplete > 0 && !dto.AcceptIncompleteAsAbsent)
                return Bad<CloseMonthResultDto>($"{incomplete} روز فقط یک تردد دارد. ترددها را اصلاح کنید (ثبت دستی یا فراموشی تردد) یا پذیرش آن‌ها به‌عنوان غیبت را تایید کنید.");

            List<int> ids = days.Select(d => d.EmployeeId).Distinct().ToList();
            OfAttendancePolicy policy = await LoadPolicyAsync(ct);
            Dictionary<int, int> scheduled = await ScheduledMinutesAsync(ct);
            Dictionary<int, FnEmployee> employees = await EmployeesAsync(ids, ct);
            Dictionary<int, FnAttendanceSheet> sheets = await _uow.Set<FnAttendanceSheet>().Where(s => s.Year == dto.Year && s.Month == dto.Month && ids.Contains(s.EmployeeId)).ToDictionaryAsync(s => s.EmployeeId, ct);
            var result = new CloseMonthResultDto { Year = dto.Year, Month = dto.Month, IncompleteDays = incomplete };
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
                    HolidayHours = Math.Round(g.Sum(d => d.HolidayMinutes) / 60m, 2), PaidLeaveDays = Math.Round(g.Sum(d => d.PaidLeaveDays), 2), SickLeaveDays = Math.Round(g.Sum(d => d.SickLeaveDays), 2),
                    UnpaidLeaveDays = g.Sum(d => (decimal)d.UnpaidLeaveDays), MissionDays = g.Sum(d => (decimal)d.MissionDays),
                };
                // overtime: the monthly cap (policy, default 22 hours) applies to the sum of the days; absence: real absence + single-punch days + the lateness deduction (once, as fractions of a day)
                decimal lateDays = FillBreakdown(row, g.ToList(), scheduled);
                row.OvertimeHours = Math.Round(AttendanceMath.MonthlyOvertime(g.Select(d => d.OvertimeMinutes), policy.MonthlyOvertimeCapMinutes) / 60m, 2);
                row.AbsenceDays = Math.Round(g.Sum(d => (decimal)d.AbsenceDays) + g.Count(d => d.Status == OfDayStatus.Incomplete) + lateDays, 2);
                if (!sheets.TryGetValue(emp.Id, out FnAttendanceSheet sheet))
                {
                    sheet = new FnAttendanceSheet { EmployeeId = emp.Id, Year = dto.Year, Month = dto.Month };
                    _uow.Set<FnAttendanceSheet>().Add(sheet);
                }
                sheet.DaysWorked = row.DaysWorked; sheet.OvertimeHours = row.OvertimeHours; sheet.NightHours = row.NightHours; sheet.HolidayHours = row.HolidayHours;
                sheet.PaidLeaveDays = row.PaidLeaveDays; sheet.SickLeaveDays = row.SickLeaveDays; sheet.UnpaidLeaveDays = row.UnpaidLeaveDays; sheet.AbsenceDays = row.AbsenceDays; sheet.MissionDays = row.MissionDays;
                sheet.UpdatedAt = now; sheet.UpdatedByUserId = Me;
                result.Rows.Add(row);
            }
            result.Sheets = result.Rows.Count;
            if (result.SkippedLocked > 0) result.Warnings.Add($"حقوق {result.SkippedLocked} نفر در این ماه تایید شده و کارکرد آن‌ها تغییر نکرد.");

            OfAttendanceMonth month = await _uow.Set<OfAttendanceMonth>().FirstOrDefaultAsync(m => m.Year == dto.Year && m.Month == dto.Month, ct);
            if (month == null) { month = new OfAttendanceMonth { Year = dto.Year, Month = dto.Month }; _uow.Set<OfAttendanceMonth>().Add(month); }
            month.ClosedAt = now; month.ClosedByUserId = Me; month.SheetCount = result.Sheets; month.SkippedLocked = result.SkippedLocked;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<CloseMonthResultDto>(OfficeText.Concurrency); }
            catch (DbUpdateException) { return Bad<CloseMonthResultDto>("ذخیره کارکرد ماه انجام نشد؛ دوباره تلاش کنید."); }
            return ResponseDtoHelper.Ok(result);
        }
    }
}
