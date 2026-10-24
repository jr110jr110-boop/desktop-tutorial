using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.OfficeDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Office
{
    public interface ILeaveService
    {
        Task<ResponseDto<PagedList<LeaveDto>>> GetLeavesAsync(LeaveQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<LeaveDto>> GetLeaveAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<LeaveDto>> SaveLeaveAsync(SaveLeaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<LeaveDto>> LeaveActionAsync(LeaveActionDto dto, CancellationToken cancellationToken);

        /// <summary>The leave balance of an employee (the signed-in user's own when no employee is given) at a month of a Persian year (this month by default).</summary>
        Task<ResponseDto<LeaveBalanceDto>> GetBalanceAsync(int? employeeId, int? year, int? month, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Leave, mission, overtime pre-approval and forgotten clock requests: the employee drafts, the unit manager position approves, HR records.
    /// The annual balance is read from the payroll leave balance and reduced, without a second book, by the requests that payroll has not booked yet.
    /// </summary>
    public class LeaveService : ILeaveService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IAttendanceService _attendance;

        public LeaveService(IUnitOfWork uow, IInventoryUserContext user, IAttendanceService attendance)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _attendance = attendance ?? throw new ArgumentNullException(nameof(attendance));
        }

        private int Me => _user.UserId;
        private bool IsHr => _user.IsInRole(OfficeRoles.Hr);
        private bool IsAll => _user.IsInRole(OfficeRoles.AttendanceReaders);
        private static string Clean(string s) => OfficeText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => OfficeText.Bad<T>(m);

        private const string NoRecord = "پرونده پرسنلی شما در سامانه ثبت نشده است؛ با واحد اداری تماس بگیرید.";
        private const int DefaultDayMinutes = 480;

        private static bool IsDayKind(OfLeaveKind k, int? fromMinute) =>
            k == OfLeaveKind.DailyLeave || k == OfLeaveKind.SickLeave || k == OfLeaveKind.UnpaidLeave || (k == OfLeaveKind.Mission && !fromMinute.HasValue);

        private static bool UsesBalance(OfLeaveKind k) => k == OfLeaveKind.DailyLeave || k == OfLeaveKind.HourlyLeave;


        private static string KindText(OfLeaveKind k)
        {
            switch (k)
            {
                case OfLeaveKind.DailyLeave: return "مرخصی روزانه";
                case OfLeaveKind.HourlyLeave: return "مرخصی ساعتی";
                case OfLeaveKind.SickLeave: return "مرخصی استعلاجی";
                case OfLeaveKind.UnpaidLeave: return "مرخصی بدون حقوق";
                case OfLeaveKind.Mission: return "ماموریت";
                case OfLeaveKind.Overtime: return "اضافه‌کار";
                default: return "فراموشی تردد";
            }
        }

        // -------------------------------------------------------------------------------- helpers

        private async Task<FnEmployee> EmployeeAsync(int id, CancellationToken ct) => await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

        private async Task<FnEmployee> MyEmployeeAsync(CancellationToken ct) =>
            Me <= 0 ? null : await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.UserId == Me && e.IsActive, ct);

        private async Task<bool> PayrollLockedAsync(DateTime date, int? workshopId, CancellationToken ct)
        {
            int y = PersianCal.Year(date), m = PersianCal.Month(date);
            return await _uow.Set<FnPayrollRun>().AnyAsync(r => r.Year == y && r.Month == m && r.Status >= FnPayrollStatus.Approved && (r.WorkshopId == null || r.WorkshopId == workshopId), ct);
        }

        /// <summary>The leave touches a month whose payroll is approved (any month between the two dates).</summary>
        private async Task<bool> PayrollLockedRangeAsync(OfLeaveRequest r, int? workshopId, CancellationToken ct)
        {
            for (DateTime d = r.FromDate.Date; d <= r.ToDate.Date; d = PersianCal.End(PersianCal.Year(d), PersianCal.Month(d)).AddDays(1))
                if (await PayrollLockedAsync(d, workshopId, ct)) return true;
            return await PayrollLockedAsync(r.ToDate, workshopId, ct);
        }

        private const string LockedMonth = "حقوق این ماه تایید شده و این درخواست دیگر تایید، ثبت یا رد نمی‌شود.";

        /// <summary>The manager position of the employee's unit; the unit above when the employee is the manager.</summary>
        private async Task<int?> FindApproverAsync(FnEmployee emp, CancellationToken ct)
        {
            int employeeId = emp.Id;
            int? userId = emp.UserId;
            OfPosition own = await _uow.Set<OfPosition>().AsNoTracking().Where(p => !p.Deleted && p.IsActive && (p.EmployeeId == employeeId || (userId != null && p.UserId == userId))).OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
            if (own == null) return null;
            int? unitId = own.UnitId;
            for (int i = 0; i < 12 && unitId.HasValue; i++)
            {
                int current = unitId.Value;
                OfUnit unit = await _uow.Set<OfUnit>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == current && !u.Deleted, ct);
                if (unit == null) return null;
                if (unit.ManagerPositionId.HasValue && unit.ManagerPositionId.Value != own.Id) return unit.ManagerPositionId;
                unitId = unit.ParentId;
            }
            return null;
        }

        private async Task<int> DayMinutesAsync(int employeeId, DateTime date, CancellationToken ct)
        {
            OfShiftAssignment a = await _uow.Set<OfShiftAssignment>().AsNoTracking().Where(x => x.EmployeeId == employeeId && x.FromDate <= date && (x.ToDate == null || x.ToDate >= date)).OrderByDescending(x => x.FromDate).FirstOrDefaultAsync(ct);
            if (a == null) return DefaultDayMinutes;
            OfShift s = await _uow.Set<OfShift>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == a.ShiftId, ct);
            if (s == null) return DefaultDayMinutes;
            return s.EndMinute > s.StartMinute ? s.EndMinute - s.StartMinute : s.EndMinute + 1440 - s.StartMinute;
        }

        private async Task<int> WorkingDaysAsync(int employeeId, DateTime from, DateTime to, CancellationToken ct)
        {
            OfficeCalendar calendar = await OfficeCalendar.LoadAsync(_uow, ct);
            Dictionary<int, OfShift> shifts = await _uow.Set<OfShift>().AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
            List<OfShiftAssignment> assignments = await _uow.Set<OfShiftAssignment>().AsNoTracking().Where(a => a.EmployeeId == employeeId && a.FromDate <= to && (a.ToDate == null || a.ToDate >= from)).OrderBy(a => a.FromDate).ToListAsync(ct);
            int count = 0;
            for (DateTime d = from.Date; d <= to.Date; d = d.AddDays(1))
            {
                OfShiftAssignment a = assignments.LastOrDefault(x => x.FromDate <= d && (x.ToDate == null || x.ToDate >= d));
                OfShift shift = a != null && shifts.TryGetValue(a.ShiftId, out OfShift found) ? found : null;
                if (calendar.IsWorkingDay(d, shift)) count++;
            }
            return count;
        }

        // -------------------------------------------------------------------------------- balance

        private async Task<LeaveBalanceDto> BalanceAsync(int employeeId, int year, int month, int? excludeLeaveId, CancellationToken ct)
        {
            decimal rate = (await _uow.Set<FnPayrollRateTable>().AsNoTracking().Where(r => (r.Year == 0 || r.Year == year) && r.Key == "LeaveEntitlementMonthly").OrderByDescending(r => r.Year)
                .Select(r => (decimal?)r.Value).FirstOrDefaultAsync(ct)) ?? 2.5m;
            decimal carryMax = (await _uow.Set<FnPayrollRateTable>().AsNoTracking().Where(r => (r.Year == 0 || r.Year == year) && r.Key == "LeaveCarryForwardMax").OrderByDescending(r => r.Year)
                .Select(r => (decimal?)r.Value).FirstOrDefaultAsync(ct)) ?? 0m;
            FnLeaveBalance b = await _uow.Set<FnLeaveBalance>().AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.Year == year, ct);
            var dto = new LeaveBalanceDto { EmployeeId = employeeId, Year = year, EntitlementRate = rate };
            if (b != null)
            {
                dto.CarriedForward = b.CarriedForward; dto.Entitled = b.Entitled; dto.Used = b.Used; dto.LastMonth = b.LastMonth;
            }
            else
            {
                FnLeaveBalance prev = await _uow.Set<FnLeaveBalance>().AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.Year == year - 1, ct);
                dto.CarriedForward = prev == null ? 0m : Math.Max(0m, Math.Min(prev.CarriedForward + prev.Entitled - prev.Used, carryMax));
            }
            dto.ProjectedEntitlement = rate * Math.Max(0, month - dto.LastMonth);
            DateTime ys = PersianCal.Start(year, 1), ye = PersianCal.End(year, 12);
            var pendingRows = await _uow.Set<OfLeaveRequest>().AsNoTracking()
                .Where(r => !r.Deleted && r.EmployeeId == employeeId && (r.Status == OfLeaveStatus.Approved || r.Status == OfLeaveStatus.Recorded) && r.FromDate >= ys && r.FromDate <= ye
                    && (r.Kind == OfLeaveKind.DailyLeave || r.Kind == OfLeaveKind.HourlyLeave) && (excludeLeaveId == null || r.Id != excludeLeaveId))
                .Select(r => new { r.Kind, r.FromDate, r.ToDate, r.Days }).ToListAsync(ct);
            // sick leave is paid by insurance and never uses the annual balance. Payroll books only the days inside each month it has approved, so a request that crosses
            // a month boundary stays pending for the days after the last booked month
            decimal pending = 0m;
            if (dto.LastMonth < 12)
            {
                DateTime firstOpen = PersianCal.Start(year, dto.LastMonth + 1);
                foreach (var r in pendingRows)
                {
                    if (r.FromDate >= firstOpen) { pending += r.Days; continue; }
                    if (r.ToDate < firstOpen || r.Kind == OfLeaveKind.HourlyLeave) continue;
                    DateTime last = r.ToDate > ye ? ye : r.ToDate;
                    pending += await WorkingDaysAsync(employeeId, firstOpen, last, ct);
                }
            }
            dto.PendingDays = pending;
            dto.Available = dto.CarriedForward + dto.Entitled - dto.Used + dto.ProjectedEntitlement - dto.PendingDays;
            return dto;
        }

        public async Task<ResponseDto<LeaveBalanceDto>> GetBalanceAsync(int? employeeId, int? year, int? month, CancellationToken ct)
        {
            int id;
            if (employeeId.HasValue)
            {
                FnEmployee target = await EmployeeAsync(employeeId.Value, ct);
                if (target == null) return OfficeText.NotFound<LeaveBalanceDto>("پرسنل پیدا نشد.");
                if (!(IsAll || (Me > 0 && target.UserId == Me))) return Bad<LeaveBalanceDto>(OfficeText.NotAllowed);
                id = target.Id;
            }
            else
            {
                FnEmployee mine = await MyEmployeeAsync(ct);
                if (mine == null) return Bad<LeaveBalanceDto>(NoRecord);
                id = mine.Id;
            }
            DateTime today = DateTime.Today;
            int y = year ?? PersianCal.Year(today);
            int m = month ?? (y == PersianCal.Year(today) ? PersianCal.Month(today) : 12);
            if (y < 1300 || y > 1600 || m < 1 || m > 12) return Bad<LeaveBalanceDto>(OfficeText.InvalidData);
            return ResponseDtoHelper.Ok(await BalanceAsync(id, y, m, null, ct));
        }

        // -------------------------------------------------------------------------------- reading

        private sealed class Ctx
        {
            public List<int> Pos = new List<int>();
            public bool IsHr;
            public bool IsAll;
        }

        private async Task<Ctx> ContextAsync(CancellationToken ct)
        {
            List<ActingPosition> acting = await OfficeActors.ActingAsync(_uow, Me, ct);
            return new Ctx { Pos = acting.Select(a => a.PositionId).ToList(), IsHr = IsHr, IsAll = IsAll };
        }

        private bool CanSee(OfLeaveRequest r, FnEmployee emp, Ctx c) =>
            c.IsAll || r.CreatorUserId == Me || (emp != null && emp.UserId == Me) || (r.ApproverPositionId.HasValue && c.Pos.Contains(r.ApproverPositionId.Value));

        private List<string> Actions(OfLeaveRequest r, FnEmployee emp, Ctx c)
        {
            var a = new List<string>();
            bool owner = r.CreatorUserId == Me || (emp != null && emp.UserId == Me);
            bool selfEmployee = emp != null && emp.UserId == Me;
            bool approver = r.ApproverPositionId.HasValue && c.Pos.Contains(r.ApproverPositionId.Value) && !selfEmployee;
            switch (r.Status)
            {
                case OfLeaveStatus.Draft:
                    if (owner || c.IsHr) { a.Add("save"); a.Add("submit"); a.Add("cancel"); }
                    break;
                case OfLeaveStatus.Submitted:
                    if (approver) { a.Add("approve"); a.Add("reject"); }
                    if (owner || c.IsHr) a.Add("cancel");
                    break;
                case OfLeaveStatus.Approved:
                    if (c.IsHr && !selfEmployee) { a.Add("record"); a.Add("reject"); }
                    if (owner || c.IsHr) a.Add("cancel");
                    break;
                case OfLeaveStatus.Recorded:
                    if (c.IsHr && r.Kind != OfLeaveKind.ForgotClock) a.Add("cancel");
                    break;
            }
            return a;
        }

        private async Task<List<LeaveDto>> DtosAsync(List<OfLeaveRequest> rows, Ctx c, CancellationToken ct)
        {
            List<int> empIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
            Dictionary<int, FnEmployee> employees = empIds.Count == 0 ? new Dictionary<int, FnEmployee>() : await _uow.Set<FnEmployee>().AsNoTracking().Where(e => empIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
            Dictionary<int, string> positions = await OfficeActors.PositionTitlesAsync(_uow, rows.Select(r => r.ApproverPositionId), ct);
            Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.ApprovedByUserId, r.RecordedByUserId }), ct);
            return rows.Select(r =>
            {
                employees.TryGetValue(r.EmployeeId, out FnEmployee e);
                return new LeaveDto
                {
                    Id = r.Id, Number = r.Number, EmployeeId = r.EmployeeId, EmployeeName = e == null ? null : (e.FirstName + " " + e.LastName).Trim(), Kind = r.Kind, FromDate = r.FromDate, ToDate = r.ToDate,
                    FromDateText = OfficeText.Date(r.FromDate), ToDateText = OfficeText.Date(r.ToDate), FromTime = r.FromMinute.HasValue ? OfficeText.Minute(r.FromMinute.Value) : null,
                    ToTime = r.ToMinute.HasValue ? OfficeText.Minute(r.ToMinute.Value) : null, Hours = r.Hours, Direction = r.Direction, Days = r.Days, Reason = r.Reason,
                    AttachmentDocumentId = r.AttachmentDocumentId, Status = r.Status, ApproverPositionId = r.ApproverPositionId, ApproverPositionTitle = positions.Title(r.ApproverPositionId),
                    ApprovedByName = users.Name(r.ApprovedByUserId), ApprovedOnBehalf = r.ApprovedOnBehalf, ApprovedAt = r.ApprovedAt, ApproverComment = r.ApproverComment,
                    RecordedByName = users.Name(r.RecordedByUserId), RecordedAt = r.RecordedAt, RejectReason = r.RejectReason, AllowedActions = Actions(r, e, c),
                    RowVersion = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<LeaveDto>>> GetLeavesAsync(LeaveQuery query, CancellationToken ct)
        {
            query ??= new LeaveQuery();
            Ctx c = await ContextAsync(ct);
            int me = Me;
            IQueryable<OfLeaveRequest> q = _uow.Set<OfLeaveRequest>().AsNoTracking().Where(r => !r.Deleted);
            FnEmployee mine = await MyEmployeeAsync(ct);
            int? myEmployeeId = mine?.Id;
            List<int> pos = c.Pos;
            if (!c.IsAll) q = q.Where(r => r.CreatorUserId == me || (myEmployeeId != null && r.EmployeeId == myEmployeeId) || (r.ApproverPositionId != null && pos.Contains(r.ApproverPositionId.Value)));
            if (query.MineOnly == true) q = q.Where(r => r.CreatorUserId == me || (myEmployeeId != null && r.EmployeeId == myEmployeeId));
            if (query.WaitingForMe == true)
            {
                bool hr = c.IsHr;
                q = q.Where(r => (r.Status == OfLeaveStatus.Submitted && r.ApproverPositionId != null && pos.Contains(r.ApproverPositionId.Value)) || (hr && r.Status == OfLeaveStatus.Approved));
            }
            if (query.EmployeeId.HasValue && c.IsAll) q = q.Where(r => r.EmployeeId == query.EmployeeId.Value);
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            if (query.Kind.HasValue) q = q.Where(r => r.Kind == query.Kind.Value);
            if (query.From.HasValue) { DateTime f = query.From.Value.Date; q = q.Where(r => r.ToDate >= f); }
            if (query.To.HasValue) { DateTime t = query.To.Value.Date; q = q.Where(r => r.FromDate <= t); }
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(r => r.Number.Contains(text) || (r.Reason != null && r.Reason.Contains(text)));
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfLeaveRequest> rows = await q.OrderByDescending(r => r.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await DtosAsync(rows, c, ct), total, number, size));
        }

        public async Task<ResponseDto<LeaveDto>> GetLeaveAsync(int id, CancellationToken ct)
        {
            Ctx c = await ContextAsync(ct);
            OfLeaveRequest r = await _uow.Set<OfLeaveRequest>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (r == null) return OfficeText.NotFound<LeaveDto>("درخواست پیدا نشد.");
            FnEmployee emp = await EmployeeAsync(r.EmployeeId, ct);
            if (!CanSee(r, emp, c)) return Bad<LeaveDto>(OfficeText.NotAllowed);
            return ResponseDtoHelper.Ok((await DtosAsync(new List<OfLeaveRequest> { r }, c, ct))[0]);
        }

        // -------------------------------------------------------------------------------- save

        private async Task<string> ConflictAsync(OfLeaveRequest r, CancellationToken ct)
        {
            bool day = IsDayKind(r.Kind, r.FromMinute);
            bool hourly = r.Kind == OfLeaveKind.HourlyLeave || (r.Kind == OfLeaveKind.Mission && r.FromMinute.HasValue);
            if (!day && !hourly) return null;
            int id = r.Id;
            DateTime from = r.FromDate.Date, to = r.ToDate.Date;
            List<OfLeaveRequest> others = await _uow.Set<OfLeaveRequest>().AsNoTracking()
                .Where(x => !x.Deleted && x.EmployeeId == r.EmployeeId && x.Id != id && x.FromDate <= to && x.ToDate >= from
                    && (x.Status == OfLeaveStatus.Draft || x.Status == OfLeaveStatus.Submitted || x.Status == OfLeaveStatus.Approved || x.Status == OfLeaveStatus.Recorded)
                    && (x.Kind == OfLeaveKind.DailyLeave || x.Kind == OfLeaveKind.SickLeave || x.Kind == OfLeaveKind.UnpaidLeave || x.Kind == OfLeaveKind.HourlyLeave || x.Kind == OfLeaveKind.Mission)).ToListAsync(ct);
            foreach (OfLeaveRequest o in others)
            {
                bool otherDay = IsDayKind(o.Kind, o.FromMinute);
                if (day || otherDay) return $"در این بازه درخواست دیگری ({o.Number}) برای همین پرسنل ثبت شده است.";
                // two hourly requests of the same day clash only when their time windows overlap
                if (r.FromMinute.HasValue && r.ToMinute.HasValue && o.FromMinute.HasValue && o.ToMinute.HasValue && r.FromMinute < o.ToMinute && o.FromMinute < r.ToMinute)
                    return $"ساعت‌های این درخواست با درخواست {o.Number} هم‌پوشانی دارد.";
            }
            return null;
        }

        public async Task<ResponseDto<LeaveDto>> SaveLeaveAsync(SaveLeaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<LeaveDto>(OfficeText.InvalidData);
            Ctx c = await ContextAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            OfLeaveRequest r;
            FnEmployee emp;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<OfLeaveRequest>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (r == null) return OfficeText.NotFound<LeaveDto>("درخواست پیدا نشد.");
                emp = await EmployeeAsync(r.EmployeeId, ct);
                if (emp == null) return Bad<LeaveDto>("پرسنل پیدا نشد.");
                bool owner = r.CreatorUserId == Me || emp.UserId == Me;
                if (!(owner || c.IsHr)) return Bad<LeaveDto>(OfficeText.NotAllowed);
                if (r.Status != OfLeaveStatus.Draft) return Bad<LeaveDto>("فقط پیش‌نویس قابل ویرایش است.");
                if (!string.IsNullOrEmpty(dto.RowVersion))
                {
                    try { _uow.Entry(r).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
                    catch (FormatException) { return Bad<LeaveDto>(OfficeText.InvalidData); }
                }
            }
            else
            {
                if (c.IsHr && dto.EmployeeId.HasValue) emp = await EmployeeAsync(dto.EmployeeId.Value, ct);
                else emp = await MyEmployeeAsync(ct);
                if (emp == null) return Bad<LeaveDto>(c.IsHr && dto.EmployeeId.HasValue ? "پرسنل پیدا نشد." : NoRecord);
                r = new OfLeaveRequest { EmployeeId = emp.Id, CreateDate = now, CreatorUserId = Me, Status = OfLeaveStatus.Draft };
            }

            DateTime from = dto.FromDate.Date;
            DateTime to = (dto.ToDate ?? dto.FromDate).Date;
            if (from == default) return Bad<LeaveDto>("تاریخ شروع را وارد کنید.");
            if (to < from) return Bad<LeaveDto>("تاریخ پایان از تاریخ شروع زودتر است.");
            if ((to - from).TotalDays > 120) return Bad<LeaveDto>("بازه درخواست حداکثر ۱۲۰ روز است.");
            if (await PayrollLockedAsync(from, emp.WorkshopId, ct) || await PayrollLockedAsync(to, emp.WorkshopId, ct)) return Bad<LeaveDto>("حقوق این ماه تایید شده و درخواست جدید برای آن ثبت نمی‌شود.");

            int? fromMinute = null, toMinute = null;
            decimal days = 0m;
            decimal? hours = null;
            OfClockDirection? direction = null;
            string reason = Clean(dto.Reason);
            switch (dto.Kind)
            {
                case OfLeaveKind.DailyLeave:
                case OfLeaveKind.SickLeave:
                case OfLeaveKind.UnpaidLeave:
                case OfLeaveKind.Mission:
                    if (dto.Kind == OfLeaveKind.Mission && (!string.IsNullOrWhiteSpace(dto.FromTime) || !string.IsNullOrWhiteSpace(dto.ToTime)))
                    {
                        if (to != from) return Bad<LeaveDto>("ماموریت ساعتی باید در یک روز باشد.");
                        if (!AttendanceService.TryParseTime(dto.FromTime, out int mf) || !AttendanceService.TryParseTime(dto.ToTime, out int mt) || mt <= mf) return Bad<LeaveDto>("ساعت شروع و پایان ماموریت نامعتبر است.");
                        fromMinute = mf; toMinute = mt;
                    }
                    if (dto.Kind == OfLeaveKind.Mission && reason == null) return Bad<LeaveDto>("مقصد و موضوع ماموریت را بنویسید.");
                    if (dto.Kind != OfLeaveKind.Mission)
                    {
                        int working = await WorkingDaysAsync(emp.Id, from, to, ct);
                        if (working == 0) return Bad<LeaveDto>("در این بازه روز کاری وجود ندارد.");
                        days = working;
                    }
                    break;
                case OfLeaveKind.HourlyLeave:
                    {
                        if (to != from) return Bad<LeaveDto>("مرخصی ساعتی باید در یک روز باشد.");
                        if (!AttendanceService.TryParseTime(dto.FromTime, out int hf) || !AttendanceService.TryParseTime(dto.ToTime, out int ht) || ht <= hf) return Bad<LeaveDto>("ساعت شروع و پایان مرخصی نامعتبر است.");
                        int working = await WorkingDaysAsync(emp.Id, from, to, ct);
                        if (working == 0) return Bad<LeaveDto>("این روز، روز کاری نیست.");
                        int dayMinutes = await DayMinutesAsync(emp.Id, from, ct);
                        fromMinute = hf; toMinute = ht;
                        days = Math.Round((decimal)(ht - hf) / dayMinutes, 4);
                        break;
                    }
                case OfLeaveKind.Overtime:
                    if (!dto.Hours.HasValue || dto.Hours.Value <= 0m || dto.Hours.Value > 12m) return Bad<LeaveDto>("ساعت اضافه‌کار باید بین ۰ و ۱۲ ساعت در روز باشد.");
                    hours = Math.Round(dto.Hours.Value, 2);
                    break;
                case OfLeaveKind.ForgotClock:
                    {
                        if (to != from) return Bad<LeaveDto>("فراموشی تردد برای یک روز ثبت می‌شود.");
                        if (from > DateTime.Today) return Bad<LeaveDto>("فراموشی تردد برای روز آینده معنا ندارد.");
                        if (!AttendanceService.TryParseTime(dto.FromTime, out int fc)) return Bad<LeaveDto>("ساعت تردد فراموش‌شده را وارد کنید.");
                        if (!dto.Direction.HasValue || dto.Direction.Value == OfClockDirection.Unknown) return Bad<LeaveDto>("ورود یا خروج بودن تردد را مشخص کنید.");
                        if (reason == null) return Bad<LeaveDto>("دلیل فراموشی تردد را بنویسید.");
                        fromMinute = fc;
                        direction = dto.Direction;
                        break;
                    }
                default:
                    return Bad<LeaveDto>(OfficeText.InvalidData);
            }
            if (dto.AttachmentDocumentId.HasValue && !await _uow.Set<Entities.Model.Document>().AnyAsync(d => d.Id == dto.AttachmentDocumentId.Value, ct)) return Bad<LeaveDto>("فایل پیوست پیدا نشد.");

            r.Kind = dto.Kind; r.FromDate = from; r.ToDate = to; r.FromMinute = fromMinute; r.ToMinute = toMinute; r.Hours = hours; r.Direction = direction; r.Days = days;
            r.Reason = reason; r.AttachmentDocumentId = dto.AttachmentDocumentId; r.ModifyDate = now; r.ModifierUserId = Me;
            string clash = await ConflictAsync(r, ct);
            if (clash != null) return Bad<LeaveDto>(clash);

            if (dto.Submit)
            {
                // a request that is saved and submitted in one call is checked as a whole first: a refused call saves nothing and takes no number
                if (r.Kind == OfLeaveKind.SickLeave && !r.AttachmentDocumentId.HasValue) return Bad<LeaveDto>("برای مرخصی استعلاجی باید گواهی پزشکی پیوست شود.");
                string submitProblem = await BalanceProblemAsync(r, ct);
                if (submitProblem != null) return Bad<LeaveDto>(submitProblem);
                if (!(await FindApproverAsync(emp, ct)).HasValue) return Bad<LeaveDto>("مدیر واحد شما در چارت سازمانی مشخص نیست؛ با واحد اداری تماس بگیرید.");
            }
            if (r.Id == 0)
            {
                r.Number = await InventoryNumbers.NextNumberAsync(_uow, OfficeNumbers.LeaveRequest, "LV", DateTime.Today, ct);
                _uow.Set<OfLeaveRequest>().Add(r);
            }
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<LeaveDto>(OfficeText.Concurrency); }
            catch (DbUpdateException) { return Bad<LeaveDto>("ذخیره درخواست انجام نشد؛ دوباره تلاش کنید."); }
            if (dto.Submit)
            {
                ResponseDto<LeaveDto> submitted = await LeaveActionAsync(new LeaveActionDto { Id = r.Id, Action = "submit" }, ct);
                return submitted;
            }
            return await GetLeaveAsync(r.Id, ct);
        }

        // -------------------------------------------------------------------------------- actions

        private void AddInbox(string key, OfInboxKind kind, string title, OfLeaveRequest r, int? positionId, int? userId, string role)
        {
            _uow.Set<OfInboxItem>().Add(new OfInboxItem
            {
                RecipientPositionId = positionId, RecipientUserId = userId, RecipientRole = role, Kind = kind, Title = title.Length > 300 ? title.Substring(0, 300) : title,
                SourceModule = "office", SourceType = "leave", SourceId = r.Id, SourceKey = key, Link = "/office/leaves/" + r.Id, Status = OfInboxStatus.Unread, CreatedAt = DateTimeOffset.Now,
            });
        }

        private async Task CloseKeysAsync(string reason, CancellationToken ct, params string[] keys)
        {
            List<OfInboxItem> items = await _uow.Set<OfInboxItem>().Where(i => i.SourceKey != null && keys.Contains(i.SourceKey) && (i.Status == OfInboxStatus.Unread || i.Status == OfInboxStatus.Read)).ToListAsync(ct);
            foreach (OfInboxItem i in items) { i.Status = OfInboxStatus.Done; i.ClosedAt = DateTimeOffset.Now; i.ClosedReason = reason; }
        }

        private async Task<string> BalanceProblemAsync(OfLeaveRequest r, CancellationToken ct)
        {
            if (!UsesBalance(r.Kind)) return null;
            LeaveBalanceDto b = await BalanceAsync(r.EmployeeId, PersianCal.Year(r.FromDate), PersianCal.Month(r.FromDate), r.Id, ct);
            return r.Days > b.Available ? $"مانده مرخصی کافی نیست؛ مانده قابل استفاده {b.Available:0.##} روز و درخواست {r.Days:0.##} روز است." : null;
        }

        public async Task<ResponseDto<LeaveDto>> LeaveActionAsync(LeaveActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<LeaveDto>(OfficeText.InvalidData);
            Ctx c = await ContextAsync(ct);
            OfLeaveRequest r = await _uow.Set<OfLeaveRequest>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return OfficeText.NotFound<LeaveDto>("درخواست پیدا نشد.");
            FnEmployee emp = await EmployeeAsync(r.EmployeeId, ct);
            if (emp == null) return Bad<LeaveDto>("پرسنل پیدا نشد.");
            if (!CanSee(r, emp, c)) return Bad<LeaveDto>(OfficeText.NotAllowed);
            if (!string.IsNullOrEmpty(dto.RowVersion))
            {
                try { _uow.Entry(r).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
                catch (FormatException) { return Bad<LeaveDto>(OfficeText.InvalidData); }
            }
            string action = dto.Action.Trim().ToLowerInvariant();
            if (!Actions(r, emp, c).Contains(action) && action != "submit") return Bad<LeaveDto>("این کار در وضعیت فعلی درخواست یا برای شما مجاز نیست.");
            DateTimeOffset now = DateTimeOffset.Now;
            string comment = Clean(dto.Comment);
            string label = $"{KindText(r.Kind)} {r.Number}";
            bool touchesAttendance = false;

            switch (action)
            {
                case "submit":
                    {
                        if (r.Status != OfLeaveStatus.Draft) return Bad<LeaveDto>("فقط پیش‌نویس ارسال می‌شود.");
                        bool owner = r.CreatorUserId == Me || emp.UserId == Me;
                        if (!(owner || c.IsHr)) return Bad<LeaveDto>(OfficeText.NotAllowed);
                        if (r.Kind == OfLeaveKind.SickLeave && !r.AttachmentDocumentId.HasValue) return Bad<LeaveDto>("برای مرخصی استعلاجی باید گواهی پزشکی پیوست شود.");
                        if (await PayrollLockedAsync(r.FromDate, emp.WorkshopId, ct)) return Bad<LeaveDto>("حقوق این ماه تایید شده و درخواست جدید برای آن ثبت نمی‌شود.");
                        string problem = await BalanceProblemAsync(r, ct);
                        if (problem != null) return Bad<LeaveDto>(problem);
                        int? approver = await FindApproverAsync(emp, ct);
                        if (!approver.HasValue) return Bad<LeaveDto>("مدیر واحد شما در چارت سازمانی مشخص نیست؛ با واحد اداری تماس بگیرید.");
                        r.Status = OfLeaveStatus.Submitted; r.SubmittedAt = now; r.ApproverPositionId = approver;
                        AddInbox("leave:" + r.Id, OfInboxKind.Approval, $"تایید {label} - {(emp.FirstName + " " + emp.LastName).Trim()} ({OfficeText.Date(r.FromDate)})", r, approver, null, null);
                        break;
                    }
                case "approve":
                    {
                        ActingPosition acting = c.Pos.Contains(r.ApproverPositionId ?? -1) ? (await OfficeActors.ActingAsync(_uow, Me, ct)).First(a => a.PositionId == r.ApproverPositionId) : null;
                        if (acting == null || emp.UserId == Me) return Bad<LeaveDto>(OfficeText.NotAllowed);
                        if (await PayrollLockedRangeAsync(r, emp.WorkshopId, ct)) return Bad<LeaveDto>(LockedMonth);
                        string problem = await BalanceProblemAsync(r, ct);
                        if (problem != null) return Bad<LeaveDto>(problem);
                        r.Status = OfLeaveStatus.Approved; r.ApprovedByUserId = Me; r.ApprovedOnBehalf = acting.OnBehalf; r.ApprovedAt = now; r.ApproverComment = comment;
                        await CloseKeysAsync("تایید شد", ct, "leave:" + r.Id);
                        AddInbox("leave-record:" + r.Id, OfInboxKind.Task, $"ثبت {label} در حضور و غیاب - {(emp.FirstName + " " + emp.LastName).Trim()}", r, null, null, OfficeRoles.Hr);
                        if (emp.UserId.HasValue) AddInbox("leave-result:" + r.Id, OfInboxKind.Alert, $"{label} شما تایید شد.", r, null, emp.UserId, null);
                        touchesAttendance = true;
                        break;
                    }
                case "record":
                    {
                        if (r.Status != OfLeaveStatus.Approved || !c.IsHr || emp.UserId == Me) return Bad<LeaveDto>(OfficeText.NotAllowed);
                        if (await PayrollLockedRangeAsync(r, emp.WorkshopId, ct)) return Bad<LeaveDto>(LockedMonth);
                        if (r.Kind == OfLeaveKind.ForgotClock)
                        {
                            DateTime ts = r.FromDate.Date.AddMinutes(r.FromMinute ?? 0);
                            if (await PayrollLockedAsync(ts, emp.WorkshopId, ct)) return Bad<LeaveDto>("حقوق این ماه تایید شده و ترددها قابل تغییر نیست.");
                            if (!await _uow.Set<OfClockEvent>().AnyAsync(e => e.EmployeeId == r.EmployeeId && e.Timestamp == ts, ct))
                                _uow.Set<OfClockEvent>().Add(new OfClockEvent { EmployeeId = r.EmployeeId, Timestamp = ts, Direction = r.Direction ?? OfClockDirection.Unknown, Source = OfClockSource.Manual, Reason = $"{r.Number}: {r.Reason}", CreatedByUserId = Me, CreatedAt = now });
                        }
                        r.Status = OfLeaveStatus.Recorded; r.RecordedByUserId = Me; r.RecordedAt = now;
                        await CloseKeysAsync("ثبت شد", ct, "leave-record:" + r.Id);
                        touchesAttendance = true;
                        break;
                    }
                case "reject":
                    {
                        if (comment == null) return Bad<LeaveDto>("دلیل رد درخواست را بنویسید.");
                        if (await PayrollLockedRangeAsync(r, emp.WorkshopId, ct)) return Bad<LeaveDto>(LockedMonth);
                        r.Status = OfLeaveStatus.Rejected; r.RejectReason = comment;
                        await CloseKeysAsync("رد شد", ct, "leave:" + r.Id, "leave-record:" + r.Id);
                        if (emp.UserId.HasValue) AddInbox("leave-result:" + r.Id, OfInboxKind.Alert, $"{label} شما رد شد: {comment}", r, null, emp.UserId, null);
                        break;
                    }
                case "cancel":
                    {
                        bool wasCounted = r.Status == OfLeaveStatus.Approved || r.Status == OfLeaveStatus.Recorded;
                        bool owner = r.CreatorUserId == Me || emp.UserId == Me;
                        if (wasCounted && !c.IsHr && r.FromDate.Date <= DateTime.Today) return Bad<LeaveDto>("پس از شروع، فقط واحد اداری می‌تواند درخواست تاییدشده را باطل کند.");
                        if (wasCounted && await PayrollLockedAsync(r.FromDate, emp.WorkshopId, ct)) return Bad<LeaveDto>("حقوق این ماه تایید شده و درخواست قابل ابطال نیست.");
                        if (!(owner || c.IsHr)) return Bad<LeaveDto>(OfficeText.NotAllowed);
                        r.Status = OfLeaveStatus.Cancelled; r.RejectReason = comment;
                        await CloseKeysAsync("باطل شد", ct, "leave:" + r.Id, "leave-record:" + r.Id);
                        touchesAttendance = wasCounted;
                        break;
                    }
                case "save":
                    return Bad<LeaveDto>("برای ویرایش، درخواست را از مسیر ذخیره ارسال کنید.");
                default:
                    return Bad<LeaveDto>(OfficeText.InvalidData);
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<LeaveDto>(OfficeText.Concurrency); }
            catch (DbUpdateException) { return Bad<LeaveDto>("ذخیره انجام نشد؛ دوباره تلاش کنید."); }

            if (touchesAttendance && r.ToDate.Date < DateTime.Today)
            {
                try { await _attendance.RecomputeAsync(r.FromDate, r.ToDate, r.EmployeeId, ct); }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { /* the daily job recomputes the days; the approval itself is already saved */ }
            }
            return await GetLeaveAsync(r.Id, ct);
        }
    }
}
