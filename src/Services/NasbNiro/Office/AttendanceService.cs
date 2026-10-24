using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
    public interface IAttendanceService
    {
        Task<ResponseDto<List<ShiftDto>>> GetShiftsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<ShiftDto>> SaveShiftAsync(SaveShiftDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<ShiftAssignmentDto>>> GetAssignmentsAsync(ShiftAssignmentQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ShiftAssignmentDto>> SaveAssignmentAsync(SaveShiftAssignmentDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<DeviceDto>>> GetDevicesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<DeviceKeyDto>> SaveDeviceAsync(SaveDeviceDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<ClockEventDto>>> GetClockEventsAsync(ClockQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<ClockEventDto>> AddManualEventAsync(ManualClockDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ClockImportResultDto>> ImportEventsAsync(ImportClockDto dto, CancellationToken cancellationToken);

        /// <summary>Called by a device or the middleware on the LAN; authenticated by the device code and key, not by a user.</summary>
        Task<ResponseDto<DevicePushResultDto>> PushAsync(string deviceCode, string apiKey, DevicePushDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<AttendancePolicyDto>> GetPolicyAsync(CancellationToken cancellationToken);
        Task<ResponseDto<AttendancePolicyDto>> SavePolicyAsync(AttendancePolicyDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<ComputeResultDto>> ComputeDaysAsync(ComputeDaysDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<AttendanceDayDto>>> GetDaysAsync(DayQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<CloseMonthResultDto>> CloseMonthAsync(CloseMonthDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<MonthStatusDto>> GetMonthStatusAsync(int year, int month, CancellationToken cancellationToken);
        Task<ResponseDto<List<WorkedHoursDto>>> GetWorkedHoursAsync(WorkedHoursQuery query, CancellationToken cancellationToken);

        /// <summary>For the leave service: recomputes the finished days of one employee after an approval changed them. No permission check; not exposed by the API.</summary>
        Task RecomputeAsync(DateTime from, DateTime to, int employeeId, CancellationToken cancellationToken);

        /// <summary>The daily job: computes the last three finished days (a device may push late). No user is needed.</summary>
        Task<ComputeResultDto> RunDailyAsync(CancellationToken cancellationToken);
    }

    /// <summary>Shifts, devices, clock events, the attendance policy, the daily computation and the monthly close for payroll.</summary>
    public partial class AttendanceService : IAttendanceService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public AttendanceService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => OfficeText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => OfficeText.Bad<T>(m);
        private bool IsHr => _user.IsInRole(OfficeRoles.Hr);
        private bool CanReadAll => _user.IsInRole(OfficeRoles.AttendanceReaders);
        private bool IsOrgAdmin => _user.IsInRole(OfficeRoles.OrgAdmins);

        private const string DeviceRejected = "دستگاه یا کلید نامعتبر است.";

        private static string EmployeeName(FnEmployee e) => e == null ? null : (e.FirstName + " " + e.LastName).Trim();

        private async Task<int?> MyEmployeeIdAsync(CancellationToken ct) =>
            Me <= 0 ? (int?)null : await _uow.Set<FnEmployee>().AsNoTracking().Where(e => e.UserId == Me && e.IsActive).Select(e => (int?)e.Id).FirstOrDefaultAsync(ct);

        private async Task<Dictionary<int, FnEmployee>> EmployeesAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            List<int> list = ids.Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, FnEmployee>();
            return await _uow.Set<FnEmployee>().AsNoTracking().Where(e => list.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        }

        private static string HashKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key ?? "")));

        // ================================================================================== shifts

        private static ShiftDto ToDto(OfShift s)
        {
            int span = s.EndMinute > s.StartMinute ? s.EndMinute - s.StartMinute : s.EndMinute + 1440 - s.StartMinute;
            return new ShiftDto
            {
                Id = s.Id, Title = s.Title, Start = OfficeText.Minute(s.StartMinute), End = OfficeText.Minute(s.EndMinute), BreakMinutes = s.BreakMinutes,
                NightStart = OfficeText.Minute(s.NightStartMinute), NightEnd = OfficeText.Minute(s.NightEndMinute),
                WorkDays = Enumerable.Range(0, 7).Where(d => (s.WorkDaysMask & (1 << d)) != 0).ToList(), IsActive = s.IsActive,
                CrossesMidnight = s.EndMinute <= s.StartMinute, ScheduledMinutes = span - s.BreakMinutes,
            };
        }

        public async Task<ResponseDto<List<ShiftDto>>> GetShiftsAsync(CancellationToken ct)
        {
            List<OfShift> rows = await _uow.Set<OfShift>().AsNoTracking().Where(s => !s.Deleted).OrderBy(s => s.Title).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(x => ToDto(x)).ToList());
        }

        public async Task<ResponseDto<ShiftDto>> SaveShiftAsync(SaveShiftDto dto, CancellationToken ct)
        {
            if (!IsOrgAdmin) return Bad<ShiftDto>(OfficeText.NotAllowed);
            if (dto == null) return Bad<ShiftDto>(OfficeText.InvalidData);
            string title = Clean(dto.Title);
            if (title == null) return Bad<ShiftDto>("عنوان شیفت را وارد کنید.");
            int? start = OfficeText.ParseMinute(dto.Start);
            int? end = OfficeText.ParseMinute(dto.End);
            int? nightStart = string.IsNullOrWhiteSpace(dto.NightStart) ? 22 * 60 : OfficeText.ParseMinute(dto.NightStart);
            int? nightEnd = string.IsNullOrWhiteSpace(dto.NightEnd) ? 6 * 60 : OfficeText.ParseMinute(dto.NightEnd);
            if (!start.HasValue || !end.HasValue || start == end) return Bad<ShiftDto>("ساعت شروع و پایان شیفت نامعتبر است.");
            if (!nightStart.HasValue || !nightEnd.HasValue || nightStart == nightEnd) return Bad<ShiftDto>("بازه ساعات شب نامعتبر است.");
            int span = end.Value > start.Value ? end.Value - start.Value : end.Value + 1440 - start.Value;
            if (dto.BreakMinutes < 0 || dto.BreakMinutes >= span) return Bad<ShiftDto>("مدت استراحت نامعتبر است.");
            List<int> days = (dto.WorkDays ?? new List<int>()).Distinct().ToList();
            if (days.Count == 0 || days.Any(d => d < 0 || d > 6)) return Bad<ShiftDto>("روزهای کاری شیفت نامعتبر است (۰ یکشنبه تا ۶ شنبه).");
            int mask = 0;
            foreach (int d in days) mask |= 1 << d;

            OfShift shift;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                shift = await _uow.Set<OfShift>().FirstOrDefaultAsync(s => s.Id == dto.Id.Value && !s.Deleted, ct);
                if (shift == null) return OfficeText.NotFound<ShiftDto>("شیفت پیدا نشد.");
            }
            else
            {
                shift = new OfShift { CreateDate = now, CreatorUserId = Me };
                _uow.Set<OfShift>().Add(shift);
            }
            if (await _uow.Set<OfShift>().AnyAsync(s => !s.Deleted && s.Title == title && s.Id != shift.Id, ct)) return Bad<ShiftDto>("شیفت دیگری با همین عنوان وجود دارد.");
            shift.Title = title; shift.StartMinute = start.Value; shift.EndMinute = end.Value; shift.BreakMinutes = dto.BreakMinutes;
            shift.NightStartMinute = nightStart.Value; shift.NightEndMinute = nightEnd.Value; shift.WorkDaysMask = mask; shift.IsActive = dto.IsActive;
            shift.ModifyDate = now; shift.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(shift));
        }

        public async Task<ResponseDto<PagedList<ShiftAssignmentDto>>> GetAssignmentsAsync(ShiftAssignmentQuery query, CancellationToken ct)
        {
            query ??= new ShiftAssignmentQuery();
            if (!CanReadAll && !IsOrgAdmin) return Bad<PagedList<ShiftAssignmentDto>>(OfficeText.NotAllowed);
            IQueryable<OfShiftAssignment> q = _uow.Set<OfShiftAssignment>().AsNoTracking();
            if (query.EmployeeId.HasValue) q = q.Where(a => a.EmployeeId == query.EmployeeId.Value);
            if (query.ShiftId.HasValue) q = q.Where(a => a.ShiftId == query.ShiftId.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfShiftAssignment> rows = await q.OrderByDescending(a => a.FromDate).ThenBy(a => a.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await AssignmentDtosAsync(rows, ct), total, number, size));
        }

        private async Task<List<ShiftAssignmentDto>> AssignmentDtosAsync(List<OfShiftAssignment> rows, CancellationToken ct)
        {
            Dictionary<int, FnEmployee> employees = await EmployeesAsync(rows.Select(a => a.EmployeeId), ct);
            List<int> shiftIds = rows.Select(a => a.ShiftId).Distinct().ToList();
            Dictionary<int, string> shifts = await _uow.Set<OfShift>().AsNoTracking().Where(s => shiftIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Title, ct);
            return rows.Select(a =>
            {
                employees.TryGetValue(a.EmployeeId, out FnEmployee e);
                shifts.TryGetValue(a.ShiftId, out string shiftTitle);
                return new ShiftAssignmentDto
                {
                    Id = a.Id, EmployeeId = a.EmployeeId, EmployeeName = EmployeeName(e), PersonnelCode = e?.PersonnelCode, ShiftId = a.ShiftId, ShiftTitle = shiftTitle,
                    FromDate = a.FromDate, FromDateText = OfficeText.Date(a.FromDate), ToDate = a.ToDate, ToDateText = OfficeText.Date(a.ToDate),
                };
            }).ToList();
        }

        public async Task<ResponseDto<ShiftAssignmentDto>> SaveAssignmentAsync(SaveShiftAssignmentDto dto, CancellationToken ct)
        {
            if (!IsOrgAdmin) return Bad<ShiftAssignmentDto>(OfficeText.NotAllowed);
            if (dto == null || dto.EmployeeId <= 0 || dto.ShiftId <= 0) return Bad<ShiftAssignmentDto>(OfficeText.InvalidData);
            DateTime from = dto.FromDate.Date;
            DateTime? to = dto.ToDate?.Date;
            if (to.HasValue && to.Value < from) return Bad<ShiftAssignmentDto>("پایان تخصیص از شروع آن زودتر است.");
            if (!await _uow.Set<FnEmployee>().AnyAsync(e => e.Id == dto.EmployeeId && e.IsActive, ct)) return OfficeText.NotFound<ShiftAssignmentDto>("پرسنل پیدا نشد.");
            if (!await _uow.Set<OfShift>().AnyAsync(s => s.Id == dto.ShiftId && !s.Deleted && s.IsActive, ct)) return OfficeText.NotFound<ShiftAssignmentDto>("شیفت فعال پیدا نشد.");

            // a new assignment ends the one that is still running before it; an assignment that starts on or after it is a conflict
            List<OfShiftAssignment> overlapping = await _uow.Set<OfShiftAssignment>()
                .Where(a => a.EmployeeId == dto.EmployeeId && (a.ToDate == null || a.ToDate >= from) && (to == null || a.FromDate <= to)).ToListAsync(ct);
            foreach (OfShiftAssignment old in overlapping)
            {
                if (old.FromDate >= from) return Bad<ShiftAssignmentDto>("برای این پرسنل در همین بازه تخصیص دیگری وجود دارد.");
                old.ToDate = from.AddDays(-1);
            }
            var assignment = new OfShiftAssignment { EmployeeId = dto.EmployeeId, ShiftId = dto.ShiftId, FromDate = from, ToDate = to, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now };
            _uow.Set<OfShiftAssignment>().Add(assignment);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await AssignmentDtosAsync(new List<OfShiftAssignment> { assignment }, ct))[0]);
        }

        // ================================================================================== devices

        private static DeviceDto ToDto(OfDevice d) => new DeviceDto
        {
            Id = d.Id, Code = d.Code, Title = d.Title, Location = d.Location, IsActive = d.IsActive, LastPushAt = d.LastPushAt, LastPushAtText = OfficeText.Stamp(d.LastPushAt),
        };

        public async Task<ResponseDto<List<DeviceDto>>> GetDevicesAsync(CancellationToken ct)
        {
            if (!IsOrgAdmin) return Bad<List<DeviceDto>>(OfficeText.NotAllowed);
            List<OfDevice> rows = await _uow.Set<OfDevice>().AsNoTracking().Where(d => !d.Deleted).OrderBy(d => d.Code).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(x => ToDto(x)).ToList());
        }

        public async Task<ResponseDto<DeviceKeyDto>> SaveDeviceAsync(SaveDeviceDto dto, CancellationToken ct)
        {
            if (!IsOrgAdmin) return Bad<DeviceKeyDto>(OfficeText.NotAllowed);
            if (dto == null) return Bad<DeviceKeyDto>(OfficeText.InvalidData);
            string code = (Clean(dto.Code) ?? "").ToUpperInvariant();
            if (code.Length == 0 || code.Length > 40 || !code.All(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
                return Bad<DeviceKeyDto>("کد دستگاه فقط حروف لاتین، عدد، خط تیره و زیرخط (حداکثر ۴۰ نویسه) باشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            OfDevice device;
            string key = null;
            if (dto.Id.HasValue)
            {
                device = await _uow.Set<OfDevice>().FirstOrDefaultAsync(d => d.Id == dto.Id.Value && !d.Deleted, ct);
                if (device == null) return OfficeText.NotFound<DeviceKeyDto>("دستگاه پیدا نشد.");
            }
            else
            {
                device = new OfDevice { CreateDate = now, CreatorUserId = Me };
                _uow.Set<OfDevice>().Add(device);
                key = NewKey();
            }
            if (await _uow.Set<OfDevice>().AnyAsync(d => !d.Deleted && d.Code == code && d.Id != device.Id, ct)) return Bad<DeviceKeyDto>("دستگاه دیگری با همین کد وجود دارد.");
            if (dto.RotateKey && key == null) key = NewKey();
            if (key != null) device.ApiKeyHash = HashKey(key);
            device.Code = code; device.Title = Clean(dto.Title); device.Location = Clean(dto.Location); device.IsActive = dto.IsActive; device.ModifyDate = now; device.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new DeviceKeyDto { Device = ToDto(device), ApiKey = key });
        }

        private static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

        // ================================================================================== clock events

        private static DateTime Minute(DateTime t) => new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, DateTimeKind.Unspecified);

        public async Task<ResponseDto<PagedList<ClockEventDto>>> GetClockEventsAsync(ClockQuery query, CancellationToken ct)
        {
            query ??= new ClockQuery();
            IQueryable<OfClockEvent> q = _uow.Set<OfClockEvent>().AsNoTracking();
            if (CanReadAll) { if (query.EmployeeId.HasValue) q = q.Where(e => e.EmployeeId == query.EmployeeId.Value); }
            else
            {
                int? mine = await MyEmployeeIdAsync(ct);
                if (!mine.HasValue) return ResponseDtoHelper.Ok(F2Page.Make(new List<ClockEventDto>(), 0, 1, Math.Max(query.PageSize, 1)));
                q = q.Where(e => e.EmployeeId == mine.Value);
            }
            if (query.From.HasValue) { DateTime f = query.From.Value.Date; q = q.Where(e => e.Timestamp >= f); }
            if (query.To.HasValue) { DateTime t = query.To.Value.Date.AddDays(1); q = q.Where(e => e.Timestamp < t); }
            if (query.Source.HasValue) q = q.Where(e => e.Source == query.Source.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfClockEvent> rows = await q.OrderByDescending(e => e.Timestamp).ThenByDescending(e => e.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await ClockDtosAsync(rows, ct), total, number, size));
        }

        private async Task<List<ClockEventDto>> ClockDtosAsync(List<OfClockEvent> rows, CancellationToken ct)
        {
            Dictionary<int, FnEmployee> employees = await EmployeesAsync(rows.Select(e => e.EmployeeId), ct);
            Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, rows.Select(e => e.CreatedByUserId), ct);
            return rows.Select(e =>
            {
                employees.TryGetValue(e.EmployeeId, out FnEmployee emp);
                return new ClockEventDto
                {
                    Id = e.Id, EmployeeId = e.EmployeeId, EmployeeName = EmployeeName(emp), PersonnelCode = emp?.PersonnelCode, Timestamp = e.Timestamp, TimestampText = OfficeText.Stamp(e.Timestamp),
                    DeviceCode = e.DeviceCode, Direction = e.Direction, Source = e.Source, Reason = e.Reason, CreatedByName = users.Name(e.CreatedByUserId),
                };
            }).ToList();
        }

        private async Task<bool> MonthLockedAsync(int year, int month, int? workshopId, CancellationToken ct) =>
            await _uow.Set<FnPayrollRun>().AnyAsync(r => r.Year == year && r.Month == month && r.Status >= FnPayrollStatus.Approved && (r.WorkshopId == null || r.WorkshopId == workshopId), ct);

        public async Task<ResponseDto<ClockEventDto>> AddManualEventAsync(ManualClockDto dto, CancellationToken ct)
        {
            if (!IsHr) return Bad<ClockEventDto>(OfficeText.NotAllowed);
            if (dto == null || dto.EmployeeId <= 0) return Bad<ClockEventDto>(OfficeText.InvalidData);
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<ClockEventDto>("دلیل ثبت دستی تردد را بنویسید.");
            if (dto.Direction == OfClockDirection.Unknown) return Bad<ClockEventDto>("جهت تردد (ورود یا خروج) را مشخص کنید.");
            DateTime ts = Minute(dto.Timestamp);
            if (ts > DateTime.Now.AddMinutes(5)) return Bad<ClockEventDto>("زمان تردد در آینده است.");
            FnEmployee emp = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
            if (emp == null) return OfficeText.NotFound<ClockEventDto>("پرسنل پیدا نشد.");
            if (await MonthLockedAsync(PersianCal.Year(ts), PersianCal.Month(ts), emp.WorkshopId, ct)) return Bad<ClockEventDto>("حقوق این ماه تایید شده و ترددها قابل تغییر نیست.");
            if (await _uow.Set<OfClockEvent>().AnyAsync(e => e.EmployeeId == emp.Id && e.Timestamp == ts, ct)) return Bad<ClockEventDto>("برای این زمان تردد ثبت شده است.");
            var ev = new OfClockEvent { EmployeeId = emp.Id, Timestamp = ts, DeviceCode = null, Direction = dto.Direction, Source = OfClockSource.Manual, Reason = reason, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now };
            _uow.Set<OfClockEvent>().Add(ev);
            await _uow.SaveChangesAsync(ct);
            await ComputeCoreAsync(ts.Date.AddDays(-1), ts.Date.AddDays(1), emp.Id, ct);
            return ResponseDtoHelper.Ok((await ClockDtosAsync(new List<OfClockEvent> { ev }, ct))[0]);
        }

        // ---- file and push import

        private sealed class RawClock
        {
            public string Code;
            public DateTime Timestamp;
            public string Device;
            public OfClockDirection Direction;
            public string Line;
        }

        internal static bool TryParseDate(string text, out DateTime date)
        {
            date = default;
            string t = OfficeText.Digits(text ?? "").Trim();
            string[] parts = t.Split(new[] { '/', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int y) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int m)
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int d)) return false;
            try
            {
                date = y < 1700 ? new PersianCalendar().ToDateTime(y, m, d, 0, 0, 0, 0) : new DateTime(y, m, d);
                return true;
            }
            catch (ArgumentOutOfRangeException) { return false; }
        }

        internal static bool TryParseTime(string text, out int minuteOfDay)
        {
            minuteOfDay = 0;
            int? m = OfficeText.ParseMinute(OfficeText.Digits(text ?? ""));
            if (!m.HasValue) return false;
            minuteOfDay = m.Value;
            return true;
        }

        private static OfClockDirection ParseDirection(string text)
        {
            string t = (OfficeText.Digits(text ?? "")).Trim().ToLowerInvariant();
            if (t == "in" || t == "1" || t == "ورود" || t == "i") return OfClockDirection.In;
            if (t == "out" || t == "2" || t == "خروج" || t == "o") return OfClockDirection.Out;
            return OfClockDirection.Unknown;
        }

        private static List<RawClock> ParseRows(ImportClockDto dto, List<string> errors)
        {
            var rows = new List<RawClock>();
            int line = 0;
            foreach (ClockRowDto r in dto.Rows ?? new List<ClockRowDto>())
            {
                line++;
                if (!TryParseDate(r.Date, out DateTime date) || !TryParseTime(r.Time, out int minute)) { errors.Add($"ردیف {line}: تاریخ یا ساعت نامعتبر است."); continue; }
                rows.Add(new RawClock { Code = OfficeText.Digits(Clean(r.Code) ?? ""), Timestamp = date.AddMinutes(minute), Device = Clean(r.Device), Direction = ParseDirection(r.Direction), Line = "ردیف " + line });
            }
            if (!string.IsNullOrWhiteSpace(dto.Csv))
            {
                int n = 0;
                foreach (string raw in dto.Csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    n++;
                    string[] f = raw.Split(new[] { ',', ';', '\t', '،' }).Select(x => x.Trim().Trim('"')).ToArray();
                    if (f.Length < 3) { errors.Add($"سطر {n}: ستون‌ها کامل نیست."); continue; }
                    if (!TryParseDate(f[1], out DateTime date) || !TryParseTime(f[2], out int minute))
                    {
                        if (n == 1) continue; // a header line
                        errors.Add($"سطر {n}: تاریخ یا ساعت نامعتبر است.");
                        continue;
                    }
                    rows.Add(new RawClock
                    {
                        Code = OfficeText.Digits(f[0]), Timestamp = date.AddMinutes(minute), Device = f.Length > 3 ? Clean(f[3]) : null, Direction = f.Length > 4 ? ParseDirection(f[4]) : OfClockDirection.Unknown, Line = "سطر " + n,
                    });
                }
            }
            return rows;
        }

        /// <summary>Adds the events that are not yet stored (same employee and minute) and tells how many were duplicates or unknown codes.</summary>
        private async Task<(int Added, int Duplicates, int Unknown, int Rejected)> StoreAsync(List<RawClock> rows, OfClockSource source, string defaultDevice, List<string> errors, CancellationToken ct)
        {
            if (rows.Count == 0) return (0, 0, 0, 0);
            List<string> codes = rows.Select(r => r.Code).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            Dictionary<string, int> byCode = new Dictionary<string, int>();
            var workshopOf = new Dictionary<int, int?>();
            foreach (var e in await _uow.Set<FnEmployee>().AsNoTracking().Where(x => codes.Contains(x.PersonnelCode)).Select(x => new { x.Id, x.PersonnelCode, x.WorkshopId }).ToListAsync(ct))
            {
                byCode[e.PersonnelCode] = e.Id;
                workshopOf[e.Id] = e.WorkshopId;
            }
            // months whose payroll is approved take no new clock events (the days are not recomputed there, so the clock would no longer match the payroll)
            int firstYear = PersianCal.Year(rows.Min(r => r.Timestamp)), lastYear = PersianCal.Year(rows.Max(r => r.Timestamp));
            List<PayrollLock> locks = (await _uow.Set<FnPayrollRun>().AsNoTracking().Where(r => r.Status >= FnPayrollStatus.Approved && r.Year >= firstYear && r.Year <= lastYear)
                .Select(r => new { r.Year, r.Month, r.WorkshopId }).ToListAsync(ct)).Select(r => new PayrollLock { Year = r.Year, Month = r.Month, WorkshopId = r.WorkshopId }).ToList();
            DateTime latest = DateTime.Now.AddMinutes(5);
            DateTime min = rows.Min(r => r.Timestamp).Date;
            DateTime max = rows.Max(r => r.Timestamp).Date.AddDays(1);
            List<int> ids = byCode.Values.Distinct().ToList();
            var existing = new HashSet<(int, DateTime)>();
            foreach (var e in await _uow.Set<OfClockEvent>().AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && x.Timestamp >= min && x.Timestamp < max).Select(x => new { x.EmployeeId, x.Timestamp }).ToListAsync(ct))
                existing.Add((e.EmployeeId, Minute(e.Timestamp)));
            int added = 0, duplicates = 0, unknown = 0, rejected = 0;
            DateTimeOffset now = DateTimeOffset.Now;
            int? by = Me > 0 ? Me : (int?)null;
            foreach (RawClock r in rows)
            {
                if (!byCode.TryGetValue(r.Code ?? "", out int employeeId))
                {
                    unknown++;
                    if (errors.Count < 50) errors.Add($"{r.Line}: کد پرسنلی «{r.Code}» پیدا نشد.");
                    continue;
                }
                DateTime ts = Minute(r.Timestamp);
                if (ts > latest)
                {
                    rejected++;
                    if (errors.Count < 50) errors.Add($"{r.Line}: زمان تردد در آینده است.");
                    continue;
                }
                if (Locked(locks, ts, workshopOf[employeeId]))
                {
                    rejected++;
                    if (errors.Count < 50) errors.Add($"{r.Line}: حقوق این ماه تایید شده و ترددها قابل تغییر نیست.");
                    continue;
                }
                if (!existing.Add((employeeId, ts))) { duplicates++; continue; }
                _uow.Set<OfClockEvent>().Add(new OfClockEvent { EmployeeId = employeeId, Timestamp = ts, DeviceCode = Cut(r.Device ?? defaultDevice, 40), Direction = r.Direction, Source = source, CreatedByUserId = by, CreatedAt = now });
                added++;
            }
            return (added, duplicates, unknown, rejected);
        }

        private static string Cut(string s, int n) => s == null ? null : (s.Length <= n ? s : s.Substring(0, n));

        public async Task<ResponseDto<ClockImportResultDto>> ImportEventsAsync(ImportClockDto dto, CancellationToken ct)
        {
            if (!IsHr) return Bad<ClockImportResultDto>(OfficeText.NotAllowed);
            if (dto == null || ((dto.Rows == null || dto.Rows.Count == 0) && string.IsNullOrWhiteSpace(dto.Csv))) return Bad<ClockImportResultDto>("فایل ترددها خالی است.");
            var result = new ClockImportResultDto();
            List<RawClock> rows = ParseRows(dto, result.Errors);
            if (rows.Count > 20000) return Bad<ClockImportResultDto>("حداکثر ۲۰٬۰۰۰ تردد در هر بار بارگذاری مجاز است.");
            var (added, duplicates, unknown, rejected) = await StoreAsync(rows, OfClockSource.Import, Clean(dto.DeviceCode) ?? "IMPORT", result.Errors, ct);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<ClockImportResultDto>("ذخیره ترددها انجام نشد؛ دوباره تلاش کنید."); }
            result.Imported = added;
            result.Duplicates = duplicates;
            result.Rejected = rejected;
            if (result.Errors.Count > 50) result.Errors = result.Errors.Take(50).ToList();
            if (added > 0)
            {
                DateTime min = rows.Min(r => r.Timestamp).Date.AddDays(-1);
                DateTime max = rows.Max(r => r.Timestamp).Date.AddDays(1);
                if ((max - min).TotalDays <= 62) await ComputeCoreAsync(min, max, null, ct);
            }
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<DevicePushResultDto>> PushAsync(string deviceCode, string apiKey, DevicePushDto dto, CancellationToken ct)
        {
            ResponseDto<DevicePushResultDto> Rejected() =>
                ResponseDtoHelper.Create<DevicePushResultDto>(false, DeviceRejected).HasHttpStatusCode(HttpStatusCode.Unauthorized);
            string code = (Clean(deviceCode) ?? "").ToUpperInvariant();
            if (code.Length == 0 || string.IsNullOrEmpty(apiKey)) return Rejected();
            OfDevice device = await _uow.Set<OfDevice>().FirstOrDefaultAsync(d => d.Code == code && !d.Deleted && d.IsActive, ct);
            if (device == null) { HashKey(apiKey); return Rejected(); }
            byte[] given = Encoding.UTF8.GetBytes(HashKey(apiKey));
            byte[] stored = Encoding.UTF8.GetBytes(device.ApiKeyHash ?? "");
            if (given.Length != stored.Length || !CryptographicOperations.FixedTimeEquals(given, stored)) return Rejected();
            if (dto == null || dto.Events == null) return Bad<DevicePushResultDto>(OfficeText.InvalidData);
            if (dto.Events.Count > 5000) return Bad<DevicePushResultDto>("حداکثر ۵۰۰۰ تردد در هر ارسال مجاز است.");
            var result = new DevicePushResultDto();
            var errors = new List<string>();
            var rows = new List<RawClock>();
            int n = 0;
            foreach (DevicePushEventDto e in dto.Events)
            {
                n++;
                if (string.IsNullOrWhiteSpace(e.EmployeeCode) || e.Timestamp == default) { result.Rejected++; errors.Add($"رویداد {n}: کد یا زمان نامعتبر است."); continue; }
                rows.Add(new RawClock { Code = OfficeText.Digits(e.EmployeeCode.Trim()), Timestamp = e.Timestamp, Device = device.Code, Direction = e.Direction, Line = "رویداد " + n });
            }
            var (added, duplicates, unknown, rejectedRows) = await StoreAsync(rows, OfClockSource.Device, device.Code, errors, ct);
            device.LastPushAt = DateTimeOffset.Now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<DevicePushResultDto>("ذخیره ترددها انجام نشد؛ دوباره تلاش کنید."); }
            result.Accepted = added;
            result.Duplicates = duplicates;
            result.Rejected += unknown + rejectedRows;
            result.Errors = errors.Take(50).ToList();
            return ResponseDtoHelper.Ok(result);
        }

        // ================================================================================== policy

        private static AttendancePolicyDto ToDto(OfAttendancePolicy p) => new AttendancePolicyDto
        {
            GraceMinutes = p.GraceMinutes, MaxOvertimeMinutesPerDay = p.MaxOvertimeMinutesPerDay, OvertimeNeedsPreApproval = p.OvertimeNeedsPreApproval, RoundingMinutes = p.RoundingMinutes,
        };

        public async Task<ResponseDto<AttendancePolicyDto>> GetPolicyAsync(CancellationToken ct)
        {
            OfAttendancePolicy p = await _uow.Set<OfAttendancePolicy>().AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct) ?? new OfAttendancePolicy();
            return ResponseDtoHelper.Ok(ToDto(p));
        }

        public async Task<ResponseDto<AttendancePolicyDto>> SavePolicyAsync(AttendancePolicyDto dto, CancellationToken ct)
        {
            if (!IsOrgAdmin) return Bad<AttendancePolicyDto>(OfficeText.NotAllowed);
            if (dto == null) return Bad<AttendancePolicyDto>(OfficeText.InvalidData);
            if (dto.GraceMinutes < 0 || dto.GraceMinutes > 120) return Bad<AttendancePolicyDto>("مهلت تاخیر باید بین ۰ تا ۱۲۰ دقیقه باشد.");
            if (dto.MaxOvertimeMinutesPerDay.HasValue && (dto.MaxOvertimeMinutesPerDay < 0 || dto.MaxOvertimeMinutesPerDay > 720)) return Bad<AttendancePolicyDto>("سقف اضافه‌کار روزانه نامعتبر است.");
            if (dto.RoundingMinutes < 0 || dto.RoundingMinutes > 60) return Bad<AttendancePolicyDto>("گرد کردن اضافه‌کار باید بین ۰ تا ۶۰ دقیقه باشد.");
            OfAttendancePolicy p = await _uow.Set<OfAttendancePolicy>().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (p == null) { p = new OfAttendancePolicy(); _uow.Set<OfAttendancePolicy>().Add(p); }
            p.GraceMinutes = dto.GraceMinutes; p.MaxOvertimeMinutesPerDay = dto.MaxOvertimeMinutesPerDay; p.OvertimeNeedsPreApproval = dto.OvertimeNeedsPreApproval;
            p.RoundingMinutes = dto.RoundingMinutes; p.UpdatedAt = DateTimeOffset.Now; p.UpdatedByUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(p));
        }
    }
}
