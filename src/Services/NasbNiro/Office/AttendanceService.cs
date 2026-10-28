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

        /// <summary>Creates a device (the key is returned once) or edits its title, place and state. HR and PMO only. An edit never issues a key.</summary>
        Task<ResponseDto<DeviceKeyDto>> SaveDeviceAsync(SaveDeviceDto dto, CancellationToken cancellationToken);

        /// <summary>Issues a new key for a device (the old one stops working); the new key is returned once. A separate action from the edit, HR and PMO only, and audited.</summary>
        Task<ResponseDto<DeviceKeyDto>> RotateDeviceKeyAsync(int id, CancellationToken cancellationToken);

        /// <summary>The lightweight push log: device, number of events, hash of the payload (instead of one audit row per punch).</summary>
        Task<ResponseDto<PagedList<DevicePushLogDto>>> GetPushLogsAsync(DevicePushLogQuery query, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<ClockEventDto>>> GetClockEventsAsync(ClockQuery query, CancellationToken cancellationToken);

        /// <summary>A manual clock event is a REQUEST: HR records it for another employee with a reason, and it counts only after a second HR user or the employee's unit manager approved it.</summary>
        Task<ResponseDto<ClockEventDto>> AddManualEventAsync(ManualClockDto dto, CancellationToken cancellationToken);

        /// <summary>Approve or reject a manual clock request: never the author, never the person concerned.</summary>
        Task<ResponseDto<ClockEventDto>> ClockRequestActionAsync(ClockRequestActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<ClockImportResultDto>> ImportEventsAsync(ImportClockDto dto, CancellationToken cancellationToken);

        /// <summary>Excel export of the attendance software (PW Kara): the first look at a file (sheets, columns, sample rows, a guessed mapping).</summary>
        Task<ResponseDto<ClockFilePreviewDto>> PreviewClockFileAsync(byte[] file, string sheet, CancellationToken cancellationToken);

        /// <summary>Imports the Excel file through a column mapping (posted, or a saved profile); idempotent: the same person and minute is skipped.</summary>
        Task<ResponseDto<ClockImportResultDto>> ImportClockFileAsync(byte[] file, ClockFileImportDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<ClockImportProfileDto>>> GetImportProfilesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<ClockImportProfileDto>> SaveImportProfileAsync(SaveClockImportProfileDto dto, CancellationToken cancellationToken);

        /// <summary>Called by a device or the middleware on the LAN; authenticated by the device code and key, not by a user.</summary>
        Task<ResponseDto<DevicePushResultDto>> PushAsync(string deviceCode, string apiKey, DevicePushDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<AttendancePolicyDto>> GetPolicyAsync(CancellationToken cancellationToken);
        Task<ResponseDto<AttendancePolicyDto>> SavePolicyAsync(AttendancePolicyDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<ComputeResultDto>> ComputeDaysAsync(ComputeDaysDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<AttendanceDayDto>>> GetDaysAsync(DayQuery query, CancellationToken cancellationToken);

        /// <summary>Step one of the month close: HR PREPARES the month (the sheets are written and locked, a snapshot is frozen). Payroll can use the month only after step two.</summary>
        Task<ResponseDto<CloseMonthResultDto>> CloseMonthAsync(CloseMonthDto dto, CancellationToken cancellationToken);

        /// <summary>Step two: the finance manager (or, when the company policy allows, a second HR user) CONFIRMS the prepared month. The preparer never confirms.</summary>
        Task<ResponseDto<MonthStatusDto>> ConfirmMonthAsync(ConfirmMonthDto dto, CancellationToken cancellationToken);

        /// <summary>Sends a prepared (or confirmed, while no payroll is approved) month back to HR with a reason.</summary>
        Task<ResponseDto<MonthStatusDto>> RejectMonthAsync(RejectMonthDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<MonthStatusDto>> GetMonthStatusAsync(int year, int month, CancellationToken cancellationToken);

        /// <summary>Corrections of locked attendance sheets that payroll proposed; HR (never the proposer) decides.</summary>
        Task<ResponseDto<PagedList<AttendanceCorrectionDto>>> GetCorrectionsAsync(CorrectionQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<AttendanceCorrectionDto>> CorrectionActionAsync(CorrectionActionDto dto, CancellationToken cancellationToken);
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
        private readonly INotificationService _notify;

        public AttendanceService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => OfficeText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => OfficeText.Bad<T>(m);
        private bool IsHr => _user.IsInRole(OfficeRoles.Hr);
        private bool CanReadAll => _user.IsInRole(OfficeRoles.AttendanceReaders);
        private bool IsOrgAdmin => _user.IsInRole(OfficeRoles.OrgAdmins);
        private bool IsDeviceAdmin => _user.IsInRole(OfficeRoles.DeviceAdmins);

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

        /// <summary>The night window from the rate-table keys NightWindowStartMinute / NightWindowEndMinute (minutes from midnight). The error names the missing key; nothing is assumed.</summary>
        private async Task<(int? NightStart, int? NightEnd, string Error)> NightWindowAsync(CancellationToken ct)
        {
            int year = PersianCal.Year(DateTime.Today);
            PayrollRates rates = await PayrollRateLoader.LoadAsync(_uow, year, ct);
            string error = rates.Require(year, PayrollKeys.NightStart, PayrollKeys.NightEnd);
            if (error != null) return (null, null, error);
            return ((int)rates.Get(PayrollKeys.NightStart), (int)rates.Get(PayrollKeys.NightEnd), null);
        }

        /// <summary>A short label of a Persian month for the messages: 1405/08.</summary>
        private static string MonthText(int year, int month) => $"{year}/{month:00}";

        /// <summary>The newest month whose HR close is confirmed; null while none is.</summary>
        private async Task<(int Year, int Month)?> LastConfirmedMonthAsync(CancellationToken ct)
        {
            var m = await _uow.Set<OfAttendanceMonth>().AsNoTracking().Where(x => x.Status == OfMonthStatus.Confirmed).OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
                .Select(x => new { x.Year, x.Month }).FirstOrDefaultAsync(ct);
            return m == null ? ((int, int)?)null : (m.Year, m.Month);
        }

        private Task<bool> MonthConfirmedAsync(DateTime date, CancellationToken ct)
        {
            int y = PersianCal.Year(date), mo = PersianCal.Month(date);
            return _uow.Set<OfAttendanceMonth>().AnyAsync(m => m.Year == y && m.Month == mo && m.Status == OfMonthStatus.Confirmed, ct);
        }

        private Task GateAsync(string key, bool waiting, string title, string link, string roles, CancellationToken ct, int id = 0) =>
            OfficeGates.SyncAsync(_notify, key, waiting, "office", "attendance", id, title, link, roles, ct);

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
            // release R1f: the night window is a rate-table key (NightWindowStartMinute / NightWindowEndMinute), not a literal; a missing key refuses and names itself
            int? nightStart = null, nightEnd = null;
            bool windowTyped = !string.IsNullOrWhiteSpace(dto.NightStart) || !string.IsNullOrWhiteSpace(dto.NightEnd);
            if (windowTyped)
            {
                nightStart = OfficeText.ParseMinute(dto.NightStart);
                nightEnd = OfficeText.ParseMinute(dto.NightEnd);
            }
            else
            {
                (int? NightStart, int? NightEnd, string Error) window = await NightWindowAsync(ct);
                if (window.Error != null) return Bad<ShiftDto>(window.Error);
                nightStart = window.NightStart; nightEnd = window.NightEnd;
            }
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
                    FromDate = a.FromDate, FromDateText = OfficeText.Date(a.FromDate), ToDate = a.ToDate, ToDateText = OfficeText.Date(a.ToDate), Reason = a.Reason,
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

            // release R1f: no back-dating into a month whose attendance close is confirmed; back-dating inside the open months needs a reason, which is stored with the assignment (the assignment is audited)
            (int Year, int Month)? lastConfirmed = await LastConfirmedMonthAsync(ct);
            if (lastConfirmed.HasValue && PersianCal.Index(PersianCal.Year(from), PersianCal.Month(from)) <= PersianCal.Index(lastConfirmed.Value.Year, lastConfirmed.Value.Month))
                return Bad<ShiftAssignmentDto>($"شروع تخصیص ({OfficeText.Date(from)}) در ماهی است که بستن حضور و غیاب آن تایید شده ({MonthText(lastConfirmed.Value.Year, lastConfirmed.Value.Month)}) یا پیش از آن است؛ تخصیص شیفت به ماه‌های بسته‌شده مجاز نیست.");
            string reason = OfficeText.Cap(Clean(dto.Reason), 1000);
            if (from < DateTime.Today && reason == null) return Bad<ShiftAssignmentDto>("شروع تخصیص در گذشته است؛ دلیل ثبت تخصیص با تاریخ گذشته را بنویسید.");

            // a new assignment ends the one that is still running before it; an assignment that starts on or after it is a conflict
            List<OfShiftAssignment> overlapping = await _uow.Set<OfShiftAssignment>()
                .Where(a => a.EmployeeId == dto.EmployeeId && (a.ToDate == null || a.ToDate >= from) && (to == null || a.FromDate <= to)).ToListAsync(ct);
            foreach (OfShiftAssignment old in overlapping)
            {
                if (old.FromDate >= from) return Bad<ShiftAssignmentDto>("برای این پرسنل در همین بازه تخصیص دیگری وجود دارد.");
                old.ToDate = from.AddDays(-1);
            }
            var assignment = new OfShiftAssignment { EmployeeId = dto.EmployeeId, ShiftId = dto.ShiftId, FromDate = from, ToDate = to, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now, Reason = reason };
            _uow.Set<OfShiftAssignment>().Add(assignment);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await AssignmentDtosAsync(new List<OfShiftAssignment> { assignment }, ct))[0]);
        }

        // ================================================================================== devices

        private static DeviceDto ToDto(OfDevice d) => new DeviceDto
        {
            Id = d.Id, Code = d.Code, Title = d.Title, Location = d.Location, IsActive = d.IsActive, LastPushAt = d.LastPushAt, LastPushAtText = OfficeText.Stamp(d.LastPushAt),
            KeyRotatedAt = d.KeyRotatedAt, KeyRotatedAtText = OfficeText.Stamp(d.KeyRotatedAt),
        };

        public async Task<ResponseDto<List<DeviceDto>>> GetDevicesAsync(CancellationToken ct)
        {
            if (!IsOrgAdmin) return Bad<List<DeviceDto>>(OfficeText.NotAllowed);
            List<OfDevice> rows = await _uow.Set<OfDevice>().AsNoTracking().Where(d => !d.Deleted).OrderBy(d => d.Code).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(x => ToDto(x)).ToList());
        }

        public async Task<ResponseDto<DeviceKeyDto>> SaveDeviceAsync(SaveDeviceDto dto, CancellationToken ct)
        {
            // release R1f: only HR and PMO create or edit devices (the CEO does not); the key is returned once, on creation, and an edit never issues one
            if (!IsDeviceAdmin) return Bad<DeviceKeyDto>(OfficeText.NotAllowed);
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
                device.ApiKeyHash = HashKey(key);
            }
            if (await _uow.Set<OfDevice>().AnyAsync(d => !d.Deleted && d.Code == code && d.Id != device.Id, ct)) return Bad<DeviceKeyDto>("دستگاه دیگری با همین کد وجود دارد.");
            device.Code = code; device.Title = Clean(dto.Title); device.Location = Clean(dto.Location); device.IsActive = dto.IsActive; device.ModifyDate = now; device.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new DeviceKeyDto { Device = ToDto(device), ApiKey = key });
        }

        public async Task<ResponseDto<DeviceKeyDto>> RotateDeviceKeyAsync(int id, CancellationToken ct)
        {
            if (!IsDeviceAdmin) return Bad<DeviceKeyDto>(OfficeText.NotAllowed);
            OfDevice device = await _uow.Set<OfDevice>().FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, ct);
            if (device == null) return OfficeText.NotFound<DeviceKeyDto>("دستگاه پیدا نشد.");
            string key = NewKey();
            DateTimeOffset now = DateTimeOffset.Now;
            // the key itself is never stored or audited; the audit trail records who rotated it and when (KeyRotatedAt / KeyRotatedByUserId)
            device.ApiKeyHash = HashKey(key); device.KeyRotatedAt = now; device.KeyRotatedByUserId = Me; device.ModifyDate = now; device.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new DeviceKeyDto { Device = ToDto(device), ApiKey = key });
        }

        public async Task<ResponseDto<PagedList<DevicePushLogDto>>> GetPushLogsAsync(DevicePushLogQuery query, CancellationToken ct)
        {
            query ??= new DevicePushLogQuery();
            if (!IsOrgAdmin) return Bad<PagedList<DevicePushLogDto>>(OfficeText.NotAllowed);
            IQueryable<OfDevicePushLog> q = _uow.Set<OfDevicePushLog>().AsNoTracking();
            if (query.DeviceId.HasValue) q = q.Where(l => l.DeviceId == query.DeviceId.Value);
            if (query.From.HasValue) { DateTimeOffset f = new DateTimeOffset(query.From.Value.Date); q = q.Where(l => l.ReceivedAt >= f); }
            if (query.To.HasValue) { DateTimeOffset t = new DateTimeOffset(query.To.Value.Date.AddDays(1)); q = q.Where(l => l.ReceivedAt < t); }
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfDevicePushLog> rows = await q.OrderByDescending(l => l.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(rows.Select(l => new DevicePushLogDto
            {
                Id = l.Id, DeviceId = l.DeviceId, DeviceCode = l.DeviceCode, ReceivedAt = l.ReceivedAt, ReceivedAtText = OfficeText.Stamp(l.ReceivedAt), EventCount = l.EventCount, Accepted = l.Accepted,
                Duplicates = l.Duplicates, PayloadHash = l.PayloadHash, Outcome = l.Outcome,
            }).ToList(), total, number, size));
        }

        /// <summary>
        /// SHA-256 of the received events in a canonical order: the same content gives the same hash whatever the order it arrived in.
        /// One line per event, "code|yyyy-MM-ddTHH:mm|direction" in the invariant culture (the request culture is Persian, whose calendar would change the year), lines sorted ordinally and joined by a line feed,
        /// so that anybody who holds the file or the device log can recompute the hash.
        /// </summary>
        internal static string PayloadHash(IEnumerable<RawClock> rows) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
                rows.Select(r => r.Code + "|" + r.Timestamp.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) + "|" + ((int)r.Direction).ToString(CultureInfo.InvariantCulture)).OrderBy(x => x, StringComparer.Ordinal)))))
                .ToLowerInvariant();

        private void LogPush(int? deviceId, string deviceCode, int count, int accepted, int duplicates, string hash, string outcome) =>
            _uow.Set<OfDevicePushLog>().Add(new OfDevicePushLog
            {
                DeviceId = deviceId, DeviceCode = Cut(deviceCode, 40), ReceivedAt = DateTimeOffset.Now, EventCount = count, Accepted = accepted, Duplicates = duplicates, PayloadHash = hash, Outcome = outcome,
            });

        private static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

        // ================================================================================== clock events

        private static DateTime Minute(DateTime t) => new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, DateTimeKind.Unspecified);

        /// <summary>Per request cache for the decision rights of a clock request (my own employee row, unit-manager checks).</summary>
        private sealed class ClockDecider
        {
            public bool Loaded;
            public int? MyEmployeeId;
            public readonly Dictionary<int, bool> UnitManager = new Dictionary<int, bool>();
        }

        /// <summary>
        /// Four eyes on a manual clock request: neither its author nor the person concerned may decide it; another HR user or the unit manager of the employee may.
        /// A device punch or an imported file is never a request, so it has nothing to decide.
        /// </summary>
        private async Task<bool> CanDecideClockAsync(OfClockEvent ev, FnEmployee emp, ClockDecider cache, CancellationToken ct)
        {
            if (ev == null || ev.Source != OfClockSource.Manual || ev.Approval != OfClockApproval.Pending || emp == null || Me <= 0) return false;
            if (ev.CreatedByUserId == Me) return false;
            if (emp.UserId == Me) return false;
            if (!cache.Loaded) { cache.MyEmployeeId = await MyEmployeeIdAsync(ct); cache.Loaded = true; }
            if (cache.MyEmployeeId.HasValue && cache.MyEmployeeId.Value == emp.Id) return false;
            if (IsHr) return true;
            if (!cache.UnitManager.TryGetValue(emp.Id, out bool manager))
            {
                manager = await OfficeActors.IsUnitManagerOfAsync(_uow, Me, emp, ct);
                cache.UnitManager[emp.Id] = manager;
            }
            return manager;
        }

        public async Task<ResponseDto<PagedList<ClockEventDto>>> GetClockEventsAsync(ClockQuery query, CancellationToken ct)
        {
            query ??= new ClockQuery();
            if (query.WaitingForMe == true) return await WaitingClockRequestsAsync(query, ct);
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
            if (query.Approval.HasValue) q = q.Where(e => e.Approval == query.Approval.Value);
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfClockEvent> rows = await q.OrderByDescending(e => e.Timestamp).ThenByDescending(e => e.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(F2Page.Make(await ClockDtosAsync(rows, ct), total, number, size));
        }

        /// <summary>The manual requests the signed-in user may decide (any signed-in user: a unit manager is not an HR user).</summary>
        private async Task<ResponseDto<PagedList<ClockEventDto>>> WaitingClockRequestsAsync(ClockQuery query, CancellationToken ct)
        {
            if (Me <= 0) return ResponseDtoHelper.Ok(F2Page.Make(new List<ClockEventDto>(), 0, 1, Math.Max(query.PageSize, 1)));
            List<OfClockEvent> pending = await _uow.Set<OfClockEvent>().AsNoTracking().Where(e => e.Source == OfClockSource.Manual && e.Approval == OfClockApproval.Pending)
                .OrderByDescending(e => e.Timestamp).ThenByDescending(e => e.Id).Take(500).ToListAsync(ct);
            Dictionary<int, FnEmployee> employees = await EmployeesAsync(pending.Select(e => e.EmployeeId), ct);
            var cache = new ClockDecider();
            var mine = new List<OfClockEvent>();
            foreach (OfClockEvent ev in pending)
            {
                employees.TryGetValue(ev.EmployeeId, out FnEmployee emp);
                if (await CanDecideClockAsync(ev, emp, cache, ct)) mine.Add(ev);
            }
            F2Page.Window(query.PageNumber, query.PageSize, mine.Count, out int number, out int size);
            List<OfClockEvent> page = mine.Skip((number - 1) * size).Take(size).ToList();
            return ResponseDtoHelper.Ok(F2Page.Make(await ClockDtosAsync(page, ct), mine.Count, number, size));
        }

        private async Task<List<ClockEventDto>> ClockDtosAsync(List<OfClockEvent> rows, CancellationToken ct)
        {
            Dictionary<int, FnEmployee> employees = await EmployeesAsync(rows.Select(e => e.EmployeeId), ct);
            Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, rows.Select(e => e.CreatedByUserId).Concat(rows.Select(e => e.ApprovedByUserId)), ct);
            var cache = new ClockDecider();
            var result = new List<ClockEventDto>();
            foreach (OfClockEvent e in rows)
            {
                employees.TryGetValue(e.EmployeeId, out FnEmployee emp);
                var dto = new ClockEventDto
                {
                    Id = e.Id, EmployeeId = e.EmployeeId, EmployeeName = EmployeeName(emp), PersonnelCode = emp?.PersonnelCode, Timestamp = e.Timestamp, TimestampText = OfficeText.Stamp(e.Timestamp),
                    DeviceCode = e.DeviceCode, Direction = e.Direction, Source = e.Source, Reason = e.Reason, CreatedByName = users.Name(e.CreatedByUserId),
                    Approval = e.Approval, ApprovedByName = users.Name(e.ApprovedByUserId), ApprovedAt = e.ApprovedAt, RejectReason = e.RejectReason,
                };
                if (await CanDecideClockAsync(e, emp, cache, ct)) { dto.AllowedActions.Add("approve"); dto.AllowedActions.Add("reject"); }
                result.Add(dto);
            }
            return result;
        }

        private async Task<bool> MonthLockedAsync(int year, int month, int? workshopId, CancellationToken ct) =>
            await _uow.Set<FnPayrollRun>().AnyAsync(r => r.Year == year && r.Month == month && r.Status >= FnPayrollStatus.Approved && (r.WorkshopId == null || r.WorkshopId == workshopId), ct);

        private const string ClockMonthClosed = "این ماه بسته شده است (حضور و غیاب تایید یا حقوق تایید شده)؛ ترددها قابل تغییر نیست.";

        /// <summary>An inbox item for the second person: the HR role and, when the chart says so, the unit manager of the employee. It closes when the request is decided.</summary>
        private async Task ClockGateAsync(OfClockEvent ev, FnEmployee emp, bool waiting, CancellationToken ct)
        {
            if (_notify == null) return;
            string key = "att:clock:" + ev.Id;
            try
            {
                if (!waiting) { await _notify.CloseAsync(key, null, ct); return; }
                var request = new NotifyRequest
                {
                    SourceKey = key, Module = "office", SourceType = "clock-request", SourceId = unchecked((int)ev.Id),
                    Title = $"درخواست ثبت دستی تردد {EmployeeName(emp)} ({OfficeText.Stamp(ev.Timestamp)}) منتظر تایید است", Link = "/office/attendance/clock?waitingForMe=true",
                    Roles = OfficeRoles.Hr, ReplaceRecipients = true,
                };
                int? approver = emp == null ? null : await OfficeActors.FindApproverAsync(_uow, emp, ct);
                if (approver.HasValue) request.PositionIds.Add(approver.Value);
                await _notify.NotifyAsync(request, ct);
            }
            catch (Exception)
            {
                // the inbox must never block a gate
            }
        }

        public async Task<ResponseDto<ClockEventDto>> AddManualEventAsync(ManualClockDto dto, CancellationToken ct)
        {
            if (!IsHr) return Bad<ClockEventDto>(OfficeText.NotAllowed);
            if (dto == null || dto.EmployeeId <= 0) return Bad<ClockEventDto>(OfficeText.InvalidData);
            string reason = OfficeText.Cap(Clean(dto.Reason), 1000);
            if (reason == null) return Bad<ClockEventDto>("دلیل ثبت دستی تردد را بنویسید.");
            if (dto.Direction == OfClockDirection.Unknown) return Bad<ClockEventDto>("جهت تردد (ورود یا خروج) را مشخص کنید.");
            DateTime ts = Minute(dto.Timestamp);
            if (ts > DateTime.Now.AddMinutes(5)) return Bad<ClockEventDto>("زمان تردد در آینده است.");
            FnEmployee emp = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
            if (emp == null) return OfficeText.NotFound<ClockEventDto>("پرسنل پیدا نشد.");

            // a manual punch is a request about ANOTHER person: nobody records a punch of their own (subject and actor differ)
            int? myEmployee = await MyEmployeeIdAsync(ct);
            if (emp.UserId == Me || (myEmployee.HasValue && myEmployee.Value == emp.Id))
                return Bad<ClockEventDto>("ثبت دستی تردد برای خودِ شخص مجاز نیست؛ یکی از همکاران منابع انسانی باید آن را ثبت کند.");
            if (await MonthConfirmedAsync(ts, ct) || await MonthLockedAsync(PersianCal.Year(ts), PersianCal.Month(ts), emp.WorkshopId, ct)) return Bad<ClockEventDto>(ClockMonthClosed);
            if (await _uow.Set<OfClockEvent>().AnyAsync(e => e.EmployeeId == emp.Id && e.Timestamp == ts && e.Approval != OfClockApproval.Rejected, ct)) return Bad<ClockEventDto>("برای این زمان تردد ثبت شده است.");

            // the request counts nowhere until a second person approves it: the day computation reads approved events only
            var ev = new OfClockEvent
            {
                EmployeeId = emp.Id, Timestamp = ts, DeviceCode = null, Direction = dto.Direction, Source = OfClockSource.Manual, Reason = reason, CreatedByUserId = Me, CreatedAt = DateTimeOffset.Now,
                Approval = OfClockApproval.Pending,
            };
            _uow.Set<OfClockEvent>().Add(ev);
            await _uow.SaveChangesAsync(ct);
            await ClockGateAsync(ev, emp, true, ct);
            return ResponseDtoHelper.Ok((await ClockDtosAsync(new List<OfClockEvent> { ev }, ct))[0]);
        }

        public async Task<ResponseDto<ClockEventDto>> ClockRequestActionAsync(ClockRequestActionDto dto, CancellationToken ct)
        {
            if (dto == null || dto.Id <= 0) return Bad<ClockEventDto>(OfficeText.InvalidData);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            if (action != "approve" && action != "reject") return Bad<ClockEventDto>("عملیات باید تایید یا رد باشد.");
            OfClockEvent ev = await _uow.Set<OfClockEvent>().FirstOrDefaultAsync(e => e.Id == dto.Id, ct);
            if (ev == null || ev.Source != OfClockSource.Manual) return OfficeText.NotFound<ClockEventDto>("درخواست تردد پیدا نشد.");
            if (ev.Approval != OfClockApproval.Pending) return Bad<ClockEventDto>("درباره این درخواست قبلاً تصمیم گرفته شده است.");
            FnEmployee emp = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == ev.EmployeeId, ct);
            if (!await CanDecideClockAsync(ev, emp, new ClockDecider(), ct))
                return Bad<ClockEventDto>("این درخواست را فقط یکی از همکاران منابع انسانی یا مدیر مستقیم واحد همین پرسنل می‌تواند تصمیم‌گیری کند؛ ثبت‌کننده و خود شخص مجاز نیستند.");
            string note = OfficeText.Cap(Clean(dto.Note), 1000);
            if (action == "reject" && note == null) return Bad<ClockEventDto>("دلیل رد را بنویسید.");
            if (action == "approve")
            {
                if (await MonthConfirmedAsync(ev.Timestamp, ct) || await MonthLockedAsync(PersianCal.Year(ev.Timestamp), PersianCal.Month(ev.Timestamp), emp.WorkshopId, ct))
                    return Bad<ClockEventDto>("این ماه بسته شده است؛ درخواست را فقط می‌توان رد کرد.");
                if (await _uow.Set<OfClockEvent>().AnyAsync(e => e.Id != ev.Id && e.EmployeeId == ev.EmployeeId && e.Timestamp == ev.Timestamp && e.Approval == OfClockApproval.Approved, ct))
                    return Bad<ClockEventDto>("برای همین دقیقه تردد تاییدشده وجود دارد؛ این درخواست را رد کنید.");
                ev.Approval = OfClockApproval.Approved;
            }
            else
            {
                ev.Approval = OfClockApproval.Rejected;
                ev.RejectReason = note;
            }
            // the decider (approver or rejecter) is recorded in ApprovedByUserId / ApprovedAt; the entity is audited
            ev.ApprovedByUserId = Me; ev.ApprovedAt = DateTimeOffset.Now;
            await _uow.SaveChangesAsync(ct);
            await ClockGateAsync(ev, emp, false, ct);
            if (action == "approve") await ComputeCoreAsync(ev.Timestamp.Date.AddDays(-1), ev.Timestamp.Date.AddDays(1), ev.EmployeeId, ct);
            return ResponseDtoHelper.Ok((await ClockDtosAsync(new List<OfClockEvent> { ev }, ct))[0]);
        }

        // ---- file and push import

        internal sealed class RawClock
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
        private async Task<(int Added, int Duplicates, int Unknown, int Rejected)> StoreAsync(List<RawClock> rows, OfClockSource source, string defaultDevice, List<string> errors, CancellationToken ct, List<string> unknownCodes = null)
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
            // release R1f: a month whose attendance close is confirmed is frozen for every workshop
            foreach (var m in await _uow.Set<OfAttendanceMonth>().AsNoTracking().Where(x => x.Status == OfMonthStatus.Confirmed && x.Year >= firstYear && x.Year <= lastYear).Select(x => new { x.Year, x.Month }).ToListAsync(ct))
                locks.Add(new PayrollLock { Year = m.Year, Month = m.Month, WorkshopId = null });
            DateTime latest = DateTime.Now.AddMinutes(5);
            DateTime min = rows.Min(r => r.Timestamp).Date;
            DateTime max = rows.Max(r => r.Timestamp).Date.AddDays(1);
            List<int> ids = byCode.Values.Distinct().ToList();
            var existing = new HashSet<(int, DateTime)>();
            // only approved events make a minute taken: a request that is still pending or was rejected must not swallow a real punch of the device
            foreach (var e in await _uow.Set<OfClockEvent>().AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && x.Timestamp >= min && x.Timestamp < max && x.Approval == OfClockApproval.Approved)
                .Select(x => new { x.EmployeeId, x.Timestamp }).ToListAsync(ct))
                existing.Add((e.EmployeeId, Minute(e.Timestamp)));
            int added = 0, duplicates = 0, unknown = 0, rejected = 0;
            DateTimeOffset now = DateTimeOffset.Now;
            int? by = Me > 0 ? Me : (int?)null;
            foreach (RawClock r in rows)
            {
                if (!byCode.TryGetValue(r.Code ?? "", out int employeeId))
                {
                    unknown++;
                    if (unknownCodes != null && unknownCodes.Count < 200 && !unknownCodes.Contains(r.Code ?? "")) unknownCodes.Add(r.Code ?? "");
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
                    if (errors.Count < 50) errors.Add($"{r.Line}: {ClockMonthClosed}");
                    continue;
                }
                if (!existing.Add((employeeId, ts))) { duplicates++; continue; }
                _uow.Set<OfClockEvent>().Add(new OfClockEvent { EmployeeId = employeeId, Timestamp = ts, DeviceCode = Cut(r.Device ?? defaultDevice, 40), Direction = r.Direction, Source = source, CreatedByUserId = by, CreatedAt = now, Approval = OfClockApproval.Approved });
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
            var unknownCodes = new List<string>();
            int added, duplicates, unknown, rejected;
            string importHash = PayloadHash(rows);
            await ClockStoreLock.WaitAsync(ct);
            try
            {
                (added, duplicates, unknown, rejected) = await StoreAsync(rows, OfClockSource.Import, Clean(dto.DeviceCode) ?? "IMPORT", result.Errors, ct, unknownCodes);
                LogPush(null, Clean(dto.DeviceCode) ?? "IMPORT", rows.Count, added, duplicates, importHash, "Import");
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { return Bad<ClockImportResultDto>("ذخیره ترددها انجام نشد؛ دوباره تلاش کنید."); }
            }
            finally { ClockStoreLock.Release(); }
            result.Imported = added;
            result.Duplicates = duplicates;
            result.Unknown = unknown;
            result.UnknownCodes = unknownCodes;
            result.RowsRead = rows.Count;
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

        /// <summary>
        /// A wrong code or key is kept in the push log (outcome Rejected), but a wrong key must not be able to fill the table: one row per device code per minute and at most 30 rejected rows a minute.
        /// The log never changes the answer to the device.
        /// </summary>
        private async Task LogRejectedPushAsync(int? deviceId, string code, CancellationToken ct)
        {
            try
            {
                DateTimeOffset since = DateTimeOffset.Now.AddMinutes(-1);
                string cut = Cut(code, 40);
                if (await _uow.Set<OfDevicePushLog>().CountAsync(l => l.Outcome == "Rejected" && l.ReceivedAt >= since, ct) >= 30) return;
                if (await _uow.Set<OfDevicePushLog>().AnyAsync(l => l.Outcome == "Rejected" && l.ReceivedAt >= since && l.DeviceCode == cut, ct)) return;
                LogPush(deviceId, cut, 0, 0, 0, null, "Rejected");
                await _uow.SaveChangesAsync(ct);
            }
            catch (Exception)
            {
                // the log must never break the answer to the device
            }
        }

        public async Task<ResponseDto<DevicePushResultDto>> PushAsync(string deviceCode, string apiKey, DevicePushDto dto, CancellationToken ct)
        {
            string code = (Clean(deviceCode) ?? "").ToUpperInvariant();
            async Task<ResponseDto<DevicePushResultDto>> Rejected(int? deviceId)
            {
                await LogRejectedPushAsync(deviceId, code, ct);
                return ResponseDtoHelper.Create<DevicePushResultDto>(false, DeviceRejected).HasHttpStatusCode(HttpStatusCode.Unauthorized);
            }
            if (code.Length == 0 || string.IsNullOrEmpty(apiKey)) return await Rejected(null);
            OfDevice device = await _uow.Set<OfDevice>().FirstOrDefaultAsync(d => d.Code == code && !d.Deleted && d.IsActive, ct);
            if (device == null) { HashKey(apiKey); return await Rejected(null); }
            byte[] given = Encoding.UTF8.GetBytes(HashKey(apiKey));
            byte[] stored = Encoding.UTF8.GetBytes(device.ApiKeyHash ?? "");
            if (given.Length != stored.Length || !CryptographicOperations.FixedTimeEquals(given, stored)) return await Rejected(device.Id);
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
            string hash = PayloadHash(rows);
            int added, duplicates, unknown, rejectedRows;
            await ClockStoreLock.WaitAsync(ct);
            try
            {
                (added, duplicates, unknown, rejectedRows) = await StoreAsync(rows, OfClockSource.Device, device.Code, errors, ct);
                device.LastPushAt = DateTimeOffset.Now;
                // one lightweight row per push (device, count, hash of the payload) instead of one audit row per punch
                LogPush(device.Id, device.Code, dto.Events.Count, added, duplicates, hash, "Push");
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { return Bad<DevicePushResultDto>("ذخیره ترددها انجام نشد؛ دوباره تلاش کنید."); }
            }
            finally { ClockStoreLock.Release(); }
            result.Accepted = added;
            result.Duplicates = duplicates;
            result.Rejected += unknown + rejectedRows;
            result.Errors = errors.Take(50).ToList();
            return ResponseDtoHelper.Ok(result);
        }

        // ================================================================================== policy

        private static AttendancePolicyDto ToDto(OfAttendancePolicy p) => new AttendancePolicyDto
        {
            GraceMinutes = p.GraceMinutes, MaxOvertimeMinutesPerDay = p.MaxOvertimeMinutesPerDay, OvertimeRequiresApproval = p.OvertimeRequiresApproval, RoundingMinutes = p.RoundingMinutes,
            EarlyLeaveDeduction = !p.EarlyLeaveDeductionDisabled, MonthConfirmByHrPeer = p.MonthConfirmByHrPeer,
            MonthlyOvertimeCapMinutes = p.MonthlyOvertimeCapMinutes, DelayPenaltyFactor = p.DelayPenaltyFactor,
        };

        /// <summary>True for a policy row that still holds the first-release defaults (grace 0 with the overtime approval switch on) and was never moved to the company rules.</summary>
        internal static bool NeedsCompanyUpgrade(OfAttendancePolicy p) => p != null && !p.CompanyRulesApplied && p.GraceMinutes == 0 && p.OvertimeRequiresApproval;

        /// <summary>
        /// One-time, idempotent upgrade of an existing install to the company rules (answer 5): a row with the old defaults gets grace 15; any other row is left as the admin set it.
        /// Release R1f: the overtime approval switch is no longer touched here (its starred default is on). The flag CompanyRulesApplied is the marker, so an admin who later chooses grace 0 again
        /// is not overwritten. The change is audited like any policy save (with the user who triggered it).
        /// Called on the write paths (save, compute, close, import); the read shows the same result without writing.
        /// </summary>
        private async Task EnsureCompanyPolicyAsync(CancellationToken ct)
        {
            OfAttendancePolicy p = await _uow.Set<OfAttendancePolicy>().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (p == null || p.CompanyRulesApplied) return;
            if (NeedsCompanyUpgrade(p)) p.GraceMinutes = 15;
            p.CompanyRulesApplied = true; p.UpdatedAt = DateTimeOffset.Now; p.UpdatedByUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { await _uow.Entry(p).ReloadAsync(ct); }
        }

        public async Task<ResponseDto<AttendancePolicyDto>> GetPolicyAsync(CancellationToken ct)
        {
            OfAttendancePolicy p = await _uow.Set<OfAttendancePolicy>().AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct) ?? new OfAttendancePolicy();
            if (NeedsCompanyUpgrade(p)) p.GraceMinutes = 15; // what the first write will store
            return ResponseDtoHelper.Ok(ToDto(p));
        }

        public async Task<ResponseDto<AttendancePolicyDto>> SavePolicyAsync(AttendancePolicyDto dto, CancellationToken ct)
        {
            if (!IsOrgAdmin) return Bad<AttendancePolicyDto>(OfficeText.NotAllowed);
            if (dto == null) return Bad<AttendancePolicyDto>(OfficeText.InvalidData);
            if (dto.GraceMinutes < 0 || dto.GraceMinutes > 120) return Bad<AttendancePolicyDto>("مهلت تاخیر باید بین ۰ تا ۱۲۰ دقیقه باشد.");
            if (dto.MaxOvertimeMinutesPerDay.HasValue && (dto.MaxOvertimeMinutesPerDay < 0 || dto.MaxOvertimeMinutesPerDay > 720)) return Bad<AttendancePolicyDto>("سقف اضافه‌کار روزانه نامعتبر است.");
            if (dto.RoundingMinutes < 0 || dto.RoundingMinutes > 60) return Bad<AttendancePolicyDto>("گرد کردن اضافه‌کار باید بین ۰ تا ۶۰ دقیقه باشد.");
            if (dto.MonthlyOvertimeCapMinutes.HasValue && (dto.MonthlyOvertimeCapMinutes < 0 || dto.MonthlyOvertimeCapMinutes > 31 * 24 * 60)) return Bad<AttendancePolicyDto>("سقف ماهانه اضافه‌کار نامعتبر است (۰ تا ۴۴٬۶۴۰ دقیقه).");
            if (dto.DelayPenaltyFactor.HasValue && (dto.DelayPenaltyFactor < 0m || dto.DelayPenaltyFactor > 10m)) return Bad<AttendancePolicyDto>("ضریب کسر تاخیر باید بین ۰ تا ۱۰ باشد (۰ یعنی بدون کسر).");
            await EnsureCompanyPolicyAsync(ct);
            OfAttendancePolicy p = await _uow.Set<OfAttendancePolicy>().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            // the confirmation rule of the month close is a control, not a tuning knob: only the CEO changes who may confirm
            if (dto.MonthConfirmByHrPeer.HasValue && dto.MonthConfirmByHrPeer.Value != (p?.MonthConfirmByHrPeer ?? false) && !_user.IsInRole(OfficeRoles.Ceo))
                return Bad<AttendancePolicyDto>("تغییر اینکه چه کسی بستن ماه را تایید می‌کند فقط با مدیرعامل است.");
            if (p == null) { p = new OfAttendancePolicy { CompanyRulesApplied = true }; _uow.Set<OfAttendancePolicy>().Add(p); }
            p.GraceMinutes = dto.GraceMinutes; p.MaxOvertimeMinutesPerDay = dto.MaxOvertimeMinutesPerDay;
            if (dto.OvertimeRequiresApproval.HasValue) p.OvertimeRequiresApproval = dto.OvertimeRequiresApproval.Value;
            if (dto.EarlyLeaveDeduction.HasValue) p.EarlyLeaveDeductionDisabled = !dto.EarlyLeaveDeduction.Value;
            if (dto.MonthConfirmByHrPeer.HasValue) p.MonthConfirmByHrPeer = dto.MonthConfirmByHrPeer.Value;
            p.RoundingMinutes = dto.RoundingMinutes;
            if (dto.MonthlyOvertimeCapMinutes.HasValue) p.MonthlyOvertimeCapMinutes = dto.MonthlyOvertimeCapMinutes.Value;
            if (dto.DelayPenaltyFactor.HasValue) p.DelayPenaltyFactor = Math.Round(dto.DelayPenaltyFactor.Value, 2);
            p.UpdatedAt = DateTimeOffset.Now; p.UpdatedByUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(ToDto(p));
        }
    }
}
