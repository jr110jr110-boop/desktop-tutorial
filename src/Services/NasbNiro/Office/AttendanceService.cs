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

        /// <summary>
        /// Imports the rows as a PENDING batch (release R1f): nothing counts until a second HR user - neither the importer nor an employee inside the batch - approves the batch.
        /// Rows for the importer's own employee record are refused.
        /// </summary>
        Task<ResponseDto<ClockImportResultDto>> ImportEventsAsync(ImportClockDto dto, CancellationToken cancellationToken);

        /// <summary>The import batches (HR and CEO read them; <c>WaitingForMe</c> lists the pending ones the signed-in user may decide).</summary>
        Task<ResponseDto<PagedList<ClockImportBatchDto>>> GetImportBatchesAsync(ClockBatchQuery query, CancellationToken cancellationToken);

        /// <summary>Approves or rejects a whole import batch: another HR user than the importer, who has no punch inside the batch. The decision is audited.</summary>
        Task<ResponseDto<ClockImportBatchDto>> ImportBatchActionAsync(ClockBatchActionDto dto, CancellationToken cancellationToken);

        /// <summary>Excel export of the attendance software (PW Kara): the first look at a file (sheets, columns, sample rows, a guessed mapping).</summary>
        Task<ResponseDto<ClockFilePreviewDto>> PreviewClockFileAsync(byte[] file, string sheet, CancellationToken cancellationToken);

        /// <summary>Imports the Excel file through a column mapping (posted, or a saved profile); idempotent: the same person and minute is skipped.</summary>
        Task<ResponseDto<ClockImportResultDto>> ImportClockFileAsync(byte[] file, ClockFileImportDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<ClockImportProfileDto>>> GetImportProfilesAsync(CancellationToken cancellationToken);
        Task<ResponseDto<ClockImportProfileDto>> SaveImportProfileAsync(SaveClockImportProfileDto dto, CancellationToken cancellationToken);

        /// <summary>
        /// Called by a device or the middleware on the LAN; authenticated by the device code and key, not by a user. <paramref name="remoteAddress"/> is the address the request came from:
        /// a device with an address list refuses (and logs) a push from any other address.
        /// </summary>
        Task<ResponseDto<DevicePushResultDto>> PushAsync(string deviceCode, string apiKey, DevicePushDto dto, CancellationToken cancellationToken, string remoteAddress = null);

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

        private Task<bool> MonthConfirmedAsync(DateTime date, CancellationToken ct)
        {
            int y = PersianCal.Year(date), mo = PersianCal.Month(date);
            return _uow.Set<OfAttendanceMonth>().AnyAsync(m => m.Year == y && m.Month == mo && m.Status == OfMonthStatus.Confirmed, ct);
        }

        /// <summary>The month of the date has been prepared and waits for its confirmation: its days are frozen, so a request or a punch that arrives now would be lost until HR sends the close back.</summary>
        private Task<bool> MonthPreparedAsync(DateTime date, CancellationToken ct)
        {
            int y = PersianCal.Year(date), mo = PersianCal.Month(date);
            return _uow.Set<OfAttendanceMonth>().AnyAsync(m => m.Year == y && m.Month == mo && m.Status == OfMonthStatus.Prepared, ct);
        }

        internal static string PreparedMonthRefusal(DateTime date) =>
            $"بستن حضور و غیاب ماه {MonthText(PersianCal.Year(date), PersianCal.Month(date))} آماده شده و منتظر تایید است و روزهای آن ثابت شده؛ این کار در آن ماه ثبت نمی‌شود. ابتدا بستن ماه را رد کنید (رد بستن ماه)، سپس درخواست را تصمیم بگیرید و ماه را دوباره آماده کنید.";

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
            if (!IsHr) return Bad<ShiftDto>(OfficeText.NotAllowed);   // R1t (D25): shifts are changed by HR only (the PMO reads)
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
            if (!IsHr) return Bad<ShiftAssignmentDto>(OfficeText.NotAllowed);   // R1t (D25): HR only (the PMO reads)
            if (dto == null || dto.EmployeeId <= 0 || dto.ShiftId <= 0) return Bad<ShiftAssignmentDto>(OfficeText.InvalidData);
            DateTime from = dto.FromDate.Date;
            DateTime? to = dto.ToDate?.Date;
            if (to.HasValue && to.Value < from) return Bad<ShiftAssignmentDto>("پایان تخصیص از شروع آن زودتر است.");
            if (!await _uow.Set<FnEmployee>().AnyAsync(e => e.Id == dto.EmployeeId && e.IsActive, ct)) return OfficeText.NotFound<ShiftAssignmentDto>("پرسنل پیدا نشد.");
            if (!await _uow.Set<OfShift>().AnyAsync(s => s.Id == dto.ShiftId && !s.Deleted && s.IsActive, ct)) return OfficeText.NotFound<ShiftAssignmentDto>("شیفت فعال پیدا نشد.");

            // release R1f: no back-dating into a month whose attendance close is confirmed (or prepared: its days are frozen); back-dating inside the open months needs a reason, which is stored with the assignment
            // (the assignment is audited). The bound is the month the assignment STARTS in - not the newest confirmed month - so HR can still repair an older month that was sent back (rejected)
            // while a newer month is already confirmed; a month that was never closed is open whatever the later months are. Closed months are never recomputed, so their sheets stay as they were closed.
            int startYear = PersianCal.Year(from), startMonth = PersianCal.Month(from);
            OfAttendanceMonth startRow = await _uow.Set<OfAttendanceMonth>().AsNoTracking().FirstOrDefaultAsync(x => x.Year == startYear && x.Month == startMonth, ct);
            if (startRow != null && startRow.Status != OfMonthStatus.Rejected)
                return Bad<ShiftAssignmentDto>($"شروع تخصیص ({OfficeText.Date(from)}) در ماهی است که بستن حضور و غیاب آن {(startRow.Status == OfMonthStatus.Prepared ? "آماده و منتظر تایید" : "تایید")} شده ({MonthText(startYear, startMonth)}); تخصیص شیفت به ماه بسته‌شده مجاز نیست{(startRow.Status == OfMonthStatus.Prepared ? " (ابتدا بستن ماه را رد کنید)" : "")}.");
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
            KeyRotatedAt = d.KeyRotatedAt, KeyRotatedAtText = OfficeText.Stamp(d.KeyRotatedAt), AllowedAddresses = d.AllowedAddresses,
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
            // the LAN addresses the device may push from: null on an edit keeps the stored list, an empty text clears it (any address), a new device starts with the list given (empty = any)
            string addresses = null;
            if (dto.AllowedAddresses != null && !DeviceAddress.TryNormalize(dto.AllowedAddresses, out addresses, out string addressError)) return Bad<DeviceKeyDto>(addressError);
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
            if (dto.AllowedAddresses != null) device.AllowedAddresses = addresses;
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

        /// <summary>Per request cache for the decision rights of a clock request (my own employee row, unit-manager checks, the key holders of the devices).</summary>
        private sealed class ClockDecider
        {
            public bool Loaded;
            public int? MyEmployeeId;
            public readonly Dictionary<int, bool> UnitManager = new Dictionary<int, bool>();
            public readonly Dictionary<string, HashSet<int>> KeyHolders = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The users who saw the key of a device: the one who created it and the one who last rotated it. A punch of such a user pushed through that device waits for a second person.</summary>
        private static HashSet<int> KeyHolderIds(OfDevice device)
        {
            var ids = new HashSet<int>();
            if (device == null) return ids;
            if (device.CreatorUserId > 0) ids.Add(device.CreatorUserId);
            if (device.KeyRotatedByUserId.HasValue && device.KeyRotatedByUserId.Value > 0) ids.Add(device.KeyRotatedByUserId.Value);
            return ids;
        }

        /// <summary>
        /// Four eyes on a clock request that waits for a second person: a manual request, or a device punch held because its employee created or last rotated the key of the device.
        /// Neither the author nor the person concerned may decide it (and, for a held device punch, nobody who holds the key of that device); another HR user or the unit manager of the employee may.
        /// An imported row is never decided alone: it belongs to a batch that a second HR user decides as a whole.
        /// </summary>
        private async Task<bool> CanDecideClockAsync(OfClockEvent ev, FnEmployee emp, ClockDecider cache, CancellationToken ct)
        {
            if (ev == null || ev.Approval != OfClockApproval.Pending || emp == null || Me <= 0 || ev.ImportBatchId.HasValue) return false;
            if (ev.Source == OfClockSource.Manual)
            {
                if (ev.CreatedByUserId == Me) return false;
            }
            else if (ev.Source == OfClockSource.Device)
            {
                string deviceCode = ev.DeviceCode ?? "";
                if (!cache.KeyHolders.TryGetValue(deviceCode, out HashSet<int> holders))
                {
                    OfDevice device = await _uow.Set<OfDevice>().AsNoTracking().FirstOrDefaultAsync(d => d.Code == deviceCode && !d.Deleted, ct);
                    holders = KeyHolderIds(device);
                    cache.KeyHolders[deviceCode] = holders;
                }
                if (holders.Contains(Me)) return false;
            }
            else return false;
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

        /// <summary>The requests the signed-in user may decide - manual requests and held device punches (any signed-in user: a unit manager is not an HR user).</summary>
        private async Task<ResponseDto<PagedList<ClockEventDto>>> WaitingClockRequestsAsync(ClockQuery query, CancellationToken ct)
        {
            if (Me <= 0) return ResponseDtoHelper.Ok(F2Page.Make(new List<ClockEventDto>(), 0, 1, Math.Max(query.PageSize, 1)));
            List<OfClockEvent> pending = await _uow.Set<OfClockEvent>().AsNoTracking().Where(e => (e.Source == OfClockSource.Manual || e.Source == OfClockSource.Device) && e.Approval == OfClockApproval.Pending && e.ImportBatchId == null)
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
            // a rejected import row carries no text of its own (20,000 rows would repeat it): the reason of the batch decision is shown instead
            List<int> batchIds = rows.Where(e => e.ImportBatchId.HasValue && e.Approval == OfClockApproval.Rejected && e.RejectReason == null).Select(e => e.ImportBatchId.Value).Distinct().ToList();
            Dictionary<int, string> batchNotes = batchIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<OfClockImportBatch>().AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.DecisionNote, ct);
            var cache = new ClockDecider();
            var result = new List<ClockEventDto>();
            foreach (OfClockEvent e in rows)
            {
                employees.TryGetValue(e.EmployeeId, out FnEmployee emp);
                string rejectReason = e.RejectReason;
                if (rejectReason == null && e.ImportBatchId.HasValue && batchNotes.TryGetValue(e.ImportBatchId.Value, out string batchNote)) rejectReason = batchNote;
                var dto = new ClockEventDto
                {
                    Id = e.Id, EmployeeId = e.EmployeeId, EmployeeName = EmployeeName(emp), PersonnelCode = emp?.PersonnelCode, Timestamp = e.Timestamp, TimestampText = OfficeText.Stamp(e.Timestamp),
                    DeviceCode = e.DeviceCode, Direction = e.Direction, Source = e.Source, Reason = e.Reason, CreatedByName = users.Name(e.CreatedByUserId),
                    Approval = e.Approval, ApprovedByName = users.Name(e.ApprovedByUserId), ApprovedAt = e.ApprovedAt, RejectReason = rejectReason, ImportBatchId = e.ImportBatchId,
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
            if (await MonthPreparedAsync(ts, ct)) return Bad<ClockEventDto>(PreparedMonthRefusal(ts));
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
            if (ev == null || (ev.Source == OfClockSource.Import && !ev.ImportBatchId.HasValue)) return OfficeText.NotFound<ClockEventDto>("درخواست تردد پیدا نشد.");
            if (ev.ImportBatchId.HasValue) return Bad<ClockEventDto>($"این تردد جزو بسته واردشده شماره {ev.ImportBatchId} است؛ تصمیم‌گیری درباره کل بسته انجام می‌شود (بسته‌های ورود تردد).");
            if (ev.Source == OfClockSource.Device && ev.Approval == OfClockApproval.Approved) return OfficeText.NotFound<ClockEventDto>("درخواست تردد پیدا نشد.");
            if (ev.Approval != OfClockApproval.Pending) return Bad<ClockEventDto>("درباره این درخواست قبلاً تصمیم گرفته شده است.");
            FnEmployee emp = await _uow.Set<FnEmployee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == ev.EmployeeId, ct);
            if (!await CanDecideClockAsync(ev, emp, new ClockDecider(), ct))
                return Bad<ClockEventDto>("این درخواست را فقط یکی از همکاران منابع انسانی یا مدیر مستقیم واحد همین پرسنل می‌تواند تصمیم‌گیری کند؛ ثبت‌کننده، خود شخص و (برای تردد دستگاه) کسی که کلید دستگاه را ساخته یا چرخانده مجاز نیستند.");
            string note = OfficeText.Cap(Clean(dto.Note), 1000);
            if (action == "reject" && note == null) return Bad<ClockEventDto>("دلیل رد را بنویسید.");
            if (action == "approve")
            {
                if (await MonthConfirmedAsync(ev.Timestamp, ct) || await MonthLockedAsync(PersianCal.Year(ev.Timestamp), PersianCal.Month(ev.Timestamp), emp.WorkshopId, ct))
                    return Bad<ClockEventDto>("این ماه بسته شده است؛ درخواست را فقط می‌توان رد کرد.");
                if (await MonthPreparedAsync(ev.Timestamp, ct)) return Bad<ClockEventDto>(PreparedMonthRefusal(ev.Timestamp) + " درخواست را می‌توان همین حالا رد کرد.");
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

        /// <summary>The outcome of storing the rows of one import or one push.</summary>
        private sealed class StoreResult
        {
            public int Added, Duplicates, Unknown, Rejected;

            /// <summary>Of the rejected rows, those in a month that is closed (payroll approved or the attendance close confirmed): the late sync of an offline device lands here.</summary>
            public int RejectedClosed;

            /// <summary>Of the new rows, those a device pushed for the employee who created or last rotated its key: stored Pending, a second person decides them.</summary>
            public readonly List<OfClockEvent> Held = new List<OfClockEvent>();
        }

        /// <summary>How the rows of one call are stored.</summary>
        private sealed class StoreOptions
        {
            /// <summary>An import: every new row is stored Pending inside this batch.</summary>
            public OfClockImportBatch Batch;

            /// <summary>An import: the user id of the importer; the rows of his own employee record are refused.</summary>
            public int? RefuseOwnUserId;

            /// <summary>A device push: the users who created or last rotated the key of the device; their own punches are stored Pending.</summary>
            public HashSet<int> HoldUserIds;
        }

        /// <summary>
        /// Adds the events that are not yet stored (same employee and minute) and tells how many were duplicates, unknown codes or refused.
        /// A device punch is Approved on arrival, except the punch of the employee who created or last rotated the device's key (Pending, a second person decides it);
        /// an imported row is always Pending inside its batch, and the importer's own employee record is refused.
        /// </summary>
        private async Task<StoreResult> StoreAsync(List<RawClock> rows, OfClockSource source, string defaultDevice, List<string> errors, CancellationToken ct, List<string> unknownCodes = null, StoreOptions options = null)
        {
            var result = new StoreResult();
            if (rows.Count == 0) return result;
            options ??= new StoreOptions();
            List<string> codes = rows.Select(r => r.Code).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            Dictionary<string, int> byCode = new Dictionary<string, int>();
            var workshopOf = new Dictionary<int, int?>();
            var userOf = new Dictionary<int, int?>();
            foreach (var e in await _uow.Set<FnEmployee>().AsNoTracking().Where(x => codes.Contains(x.PersonnelCode)).Select(x => new { x.Id, x.PersonnelCode, x.WorkshopId, x.UserId }).ToListAsync(ct))
            {
                byCode[e.PersonnelCode] = e.Id;
                workshopOf[e.Id] = e.WorkshopId;
                userOf[e.Id] = e.UserId;
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
            DateTimeOffset now = DateTimeOffset.Now;
            int? by = Me > 0 ? Me : (int?)null;
            foreach (RawClock r in rows)
            {
                if (!byCode.TryGetValue(r.Code ?? "", out int employeeId))
                {
                    result.Unknown++;
                    if (unknownCodes != null && unknownCodes.Count < 200 && !unknownCodes.Contains(r.Code ?? "")) unknownCodes.Add(r.Code ?? "");
                    if (errors.Count < 50) errors.Add($"{r.Line}: کد پرسنلی «{r.Code}» پیدا نشد.");
                    continue;
                }
                DateTime ts = Minute(r.Timestamp);
                if (ts > latest)
                {
                    result.Rejected++;
                    if (errors.Count < 50) errors.Add($"{r.Line}: زمان تردد در آینده است.");
                    continue;
                }
                // nobody imports the punches of his own employee record: another HR user does
                if (options.RefuseOwnUserId.HasValue && userOf[employeeId] == options.RefuseOwnUserId.Value)
                {
                    result.Rejected++;
                    if (errors.Count < 50) errors.Add($"{r.Line}: ترددِ پرسنل خودِ واردکننده وارد نمی‌شود؛ همکار دیگری از منابع انسانی باید آن را وارد کند.");
                    continue;
                }
                if (Locked(locks, ts, workshopOf[employeeId]))
                {
                    result.Rejected++;
                    result.RejectedClosed++;
                    if (errors.Count < 50) errors.Add($"{r.Line}: {ClockMonthClosed}");
                    continue;
                }
                if (!existing.Add((employeeId, ts))) { result.Duplicates++; continue; }
                bool held = options.HoldUserIds != null && userOf[employeeId].HasValue && options.HoldUserIds.Contains(userOf[employeeId].Value);
                var ev = new OfClockEvent
                {
                    EmployeeId = employeeId, Timestamp = ts, DeviceCode = Cut(r.Device ?? defaultDevice, 40), Direction = r.Direction, Source = source, CreatedByUserId = by, CreatedAt = now,
                    Approval = options.Batch != null || held ? OfClockApproval.Pending : OfClockApproval.Approved, ImportBatch = options.Batch,
                };
                _uow.Set<OfClockEvent>().Add(ev);
                result.Added++;
                if (held) result.Held.Add(ev);
            }
            return result;
        }

        private static string Cut(string s, int n) => s == null ? null : (s.Length <= n ? s : s.Substring(0, n));

        /// <summary>
        /// Stores the rows of an import (JSON or Excel) as a PENDING batch and logs it. Nothing counts and no day is recomputed until a second HR user approves the batch.
        /// Returns the batch (null when nothing new was stored) or a Persian error.
        /// </summary>
        private async Task<(StoreResult Stored, OfClockImportBatch Batch, string Error)> StoreImportAsync(List<RawClock> rows, string sourceName, string deviceCode, string reason, List<string> errors, List<string> unknownCodes,
            Func<Task> beforeSave, CancellationToken ct)
        {
            string hash = PayloadHash(rows);
            await ClockStoreLock.WaitAsync(ct);
            try
            {
                var batch = new OfClockImportBatch
                {
                    ImportedByUserId = Me, ImportedAt = DateTimeOffset.Now, Source = sourceName, DeviceCode = Cut(deviceCode, 40), PayloadHash = hash, Reason = reason, Status = OfClockBatchStatus.Pending,
                };
                StoreResult stored = await StoreAsync(rows, OfClockSource.Import, deviceCode, errors, ct, unknownCodes, new StoreOptions { Batch = batch, RefuseOwnUserId = Me > 0 ? Me : (int?)null });
                batch.Rows = stored.Added;
                LogPush(null, deviceCode, rows.Count, stored.Added, stored.Duplicates, hash, "Import");
                if (beforeSave != null) await beforeSave();
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { return (null, null, "ذخیره ترددها انجام نشد؛ دوباره تلاش کنید."); }
                return (stored, stored.Added > 0 ? batch : null, null);
            }
            finally { ClockStoreLock.Release(); }
        }

        /// <summary>An inbox item for the second HR user of an import batch; it closes when the batch is decided.</summary>
        private async Task BatchGateAsync(OfClockImportBatch batch, bool waiting, CancellationToken ct)
        {
            if (_notify == null || batch == null) return;
            string key = "att:batch:" + batch.Id;
            try
            {
                if (!waiting) { await _notify.CloseAsync(key, null, ct); return; }
                await _notify.NotifyAsync(new NotifyRequest
                {
                    SourceKey = key, Module = "office", SourceType = "clock-batch", SourceId = batch.Id,
                    Title = $"بسته ورود {batch.Rows} تردد ({(batch.Source == "Excel" ? "اکسل" : "فایل")}) منتظر تایید همکار دیگر منابع انسانی است", Link = "/office/attendance/clock/import-batches?waitingForMe=true",
                    Roles = OfficeRoles.Hr, ReplaceRecipients = true,
                }, ct);
            }
            catch (Exception)
            {
                // the inbox must never block a gate
            }
        }

        public async Task<ResponseDto<ClockImportResultDto>> ImportEventsAsync(ImportClockDto dto, CancellationToken ct)
        {
            if (!IsHr) return Bad<ClockImportResultDto>(OfficeText.NotAllowed);
            if (dto == null || ((dto.Rows == null || dto.Rows.Count == 0) && string.IsNullOrWhiteSpace(dto.Csv))) return Bad<ClockImportResultDto>("فایل ترددها خالی است.");
            await EnsureCompanyPolicyAsync(ct);
            var result = new ClockImportResultDto();
            List<RawClock> rows = ParseRows(dto, result.Errors);
            if (rows.Count > 20000) return Bad<ClockImportResultDto>("حداکثر ۲۰٬۰۰۰ تردد در هر بار بارگذاری مجاز است.");
            var unknownCodes = new List<string>();
            string device = Clean(dto.DeviceCode) ?? "IMPORT";
            (StoreResult stored, OfClockImportBatch batch, string error) = await StoreImportAsync(rows, "Json", device, OfficeText.Cap(Clean(dto.Reason), 1000), result.Errors, unknownCodes, null, ct);
            if (error != null) return Bad<ClockImportResultDto>(error);
            FillImportResult(result, stored, batch, unknownCodes, rows.Count);
            if (batch != null) await BatchGateAsync(batch, true, ct);
            return ResponseDtoHelper.Ok(result);
        }

        private static void FillImportResult(ClockImportResultDto result, StoreResult stored, OfClockImportBatch batch, List<string> unknownCodes, int rowsRead)
        {
            result.Imported = stored.Added;
            result.Duplicates = stored.Duplicates;
            result.Unknown = stored.Unknown;
            result.UnknownCodes = unknownCodes;
            result.RowsRead = rowsRead;
            result.Rejected += stored.Rejected;
            result.BatchId = batch?.Id;
            result.PendingApproval = batch != null;
            if (result.Errors.Count > 50) result.Errors = result.Errors.Take(50).ToList();
        }

        // ---- the decision on an import batch

        private async Task<List<ClockImportBatchDto>> BatchDtosAsync(List<OfClockImportBatch> rows, CancellationToken ct)
        {
            List<int> ids = rows.Select(b => b.Id).ToList();
            var stats = ids.Count == 0 ? new Dictionary<int, (int Employees, DateTime First, DateTime Last)>()
                : (await _uow.Set<OfClockEvent>().AsNoTracking().Where(e => e.ImportBatchId != null && ids.Contains(e.ImportBatchId.Value)).GroupBy(e => e.ImportBatchId.Value)
                    .Select(g => new { Id = g.Key, Employees = g.Select(x => x.EmployeeId).Distinct().Count(), First = g.Min(x => x.Timestamp), Last = g.Max(x => x.Timestamp) }).ToListAsync(ct))
                    .ToDictionary(x => x.Id, x => (x.Employees, x.First, x.Last));
            Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(b => new int?[] { b.ImportedByUserId, b.DecidedByUserId }), ct);
            // the batches the signed-in user has a punch in (his employee record, active or not) cannot be decided by him
            List<int> myEmployees = IsHr && Me > 0 ? await _uow.Set<FnEmployee>().AsNoTracking().Where(e => e.UserId == Me).Select(e => e.Id).ToListAsync(ct) : new List<int>();
            HashSet<int> mine = myEmployees.Count > 0 && ids.Count > 0
                ? (await _uow.Set<OfClockEvent>().AsNoTracking().Where(e => e.ImportBatchId != null && ids.Contains(e.ImportBatchId.Value) && myEmployees.Contains(e.EmployeeId)).Select(e => e.ImportBatchId.Value).Distinct().ToListAsync(ct)).ToHashSet()
                : new HashSet<int>();
            return rows.Select(b =>
            {
                stats.TryGetValue(b.Id, out var st);
                var dto = new ClockImportBatchDto
                {
                    Id = b.Id, ImportedByName = users.Name(b.ImportedByUserId), ImportedAt = b.ImportedAt, ImportedAtText = OfficeText.Stamp(b.ImportedAt), Rows = b.Rows, Source = b.Source, DeviceCode = b.DeviceCode,
                    PayloadHash = b.PayloadHash, Reason = b.Reason, Status = b.Status.ToString(), DecidedByName = users.Name(b.DecidedByUserId), DecidedAt = b.DecidedAt, DecidedAtText = OfficeText.Stamp(b.DecidedAt),
                    DecisionNote = b.DecisionNote, EmployeeCount = st.Employees,
                };
                if (st.Employees > 0)
                {
                    dto.FirstTimestamp = st.First; dto.LastTimestamp = st.Last; dto.FirstTimestampText = OfficeText.Stamp(st.First); dto.LastTimestampText = OfficeText.Stamp(st.Last);
                }
                if (b.Status == OfClockBatchStatus.Pending && IsHr && b.ImportedByUserId != Me && !mine.Contains(b.Id)) { dto.AllowedActions.Add("approve"); dto.AllowedActions.Add("reject"); }
                return dto;
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<ClockImportBatchDto>>> GetImportBatchesAsync(ClockBatchQuery query, CancellationToken ct)
        {
            query ??= new ClockBatchQuery();
            if (!CanReadAll) return Bad<PagedList<ClockImportBatchDto>>(OfficeText.NotAllowed);
            IQueryable<OfClockImportBatch> q = _uow.Set<OfClockImportBatch>().AsNoTracking();
            if (query.Status.HasValue) q = q.Where(b => b.Status == query.Status.Value);
            if (query.WaitingForMe == true)
            {
                int me = Me;
                q = IsHr ? q.Where(b => b.Status == OfClockBatchStatus.Pending && b.ImportedByUserId != me) : q.Where(b => false);
            }
            int total = await q.CountAsync(ct);
            F2Page.Window(query.PageNumber, query.PageSize, total, out int number, out int size);
            List<OfClockImportBatch> rows = await q.OrderByDescending(b => b.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            List<ClockImportBatchDto> dtos = await BatchDtosAsync(rows, ct);
            if (query.WaitingForMe == true) { dtos = dtos.Where(d => d.AllowedActions.Count > 0).ToList(); }
            return ResponseDtoHelper.Ok(F2Page.Make(dtos, query.WaitingForMe == true ? dtos.Count : total, number, size));
        }

        public async Task<ResponseDto<ClockImportBatchDto>> ImportBatchActionAsync(ClockBatchActionDto dto, CancellationToken ct)
        {
            if (!IsHr) return Bad<ClockImportBatchDto>(OfficeText.NotAllowed);
            if (dto == null || dto.Id <= 0) return Bad<ClockImportBatchDto>(OfficeText.InvalidData);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            if (action != "approve" && action != "reject") return Bad<ClockImportBatchDto>("عملیات باید تایید یا رد باشد.");
            string note = OfficeText.Cap(Clean(dto.Note), 1000);
            if (action == "reject" && note == null) return Bad<ClockImportBatchDto>("دلیل رد را بنویسید.");
            DateTimeOffset now = DateTimeOffset.Now;
            OfClockImportBatch batch;
            List<OfClockEvent> events;
            await ClockStoreLock.WaitAsync(ct);
            try
            {
                batch = await _uow.Set<OfClockImportBatch>().FirstOrDefaultAsync(b => b.Id == dto.Id, ct);
                if (batch == null) return OfficeText.NotFound<ClockImportBatchDto>("بسته ورود تردد پیدا نشد.");
                if (batch.Status != OfClockBatchStatus.Pending) return Bad<ClockImportBatchDto>("درباره این بسته قبلاً تصمیم گرفته شده است.");
                if (batch.ImportedByUserId == Me) return Bad<ClockImportBatchDto>("بسته را همان کسی که وارد کرده نمی‌تواند تایید یا رد کند؛ همکار دیگری از منابع انسانی باید تصمیم بگیرد.");
                events = await _uow.Set<OfClockEvent>().Where(e => e.ImportBatchId == batch.Id && e.Approval == OfClockApproval.Pending).ToListAsync(ct);
                // the decider has no punch inside the batch: nobody approves his own attendance
                List<int> employeeIds = events.Select(e => e.EmployeeId).Distinct().ToList();
                Dictionary<int, FnEmployee> employees = await EmployeesAsync(employeeIds, ct);
                int? myEmployee = await MyEmployeeIdAsync(ct);
                if (employees.Values.Any(e => e.UserId == Me) || (myEmployee.HasValue && employeeIds.Contains(myEmployee.Value)))
                    return Bad<ClockImportBatchDto>("پرسنل شما خودتان در این بسته تردد دارد؛ تصمیم‌گیری درباره آن با همکار دیگری از منابع انسانی است.");

                if (action == "approve")
                {
                    // a batch is approved as a whole: a row inside a month that is prepared, confirmed or paid cannot be added, so the batch can then only be rejected
                    foreach (IGrouping<(int Year, int Month), OfClockEvent> g in events.GroupBy(e => (PersianCal.Year(e.Timestamp), PersianCal.Month(e.Timestamp))).OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2))
                    {
                        DateTime sample = g.First().Timestamp;
                        if (await MonthPreparedAsync(sample, ct)) return Bad<ClockImportBatchDto>(PreparedMonthRefusal(sample) + " یا این بسته را رد کنید.");
                        bool closed = await MonthConfirmedAsync(sample, ct);
                        if (!closed)
                            foreach (int workshop in g.Select(e => employees.TryGetValue(e.EmployeeId, out FnEmployee emp) ? emp.WorkshopId : null).Distinct().Select(w => w ?? -1))
                                if (await MonthLockedAsync(g.Key.Year, g.Key.Month, workshop < 0 ? (int?)null : workshop, ct)) { closed = true; break; }
                        if (closed) return Bad<ClockImportBatchDto>($"این بسته ترددی در ماه {MonthText(g.Key.Year, g.Key.Month)} دارد که بسته و تایید شده است؛ بسته را فقط می‌توان رد کرد.");
                    }
                    // a minute that already has an approved punch of the employee (a device pushed it meanwhile) is a duplicate: not counted twice
                    var taken = new HashSet<(int, DateTime)>();
                    if (events.Count > 0)
                    {
                        DateTime from = events.Min(e => e.Timestamp).Date, to = events.Max(e => e.Timestamp).Date.AddDays(1);
                        foreach (var e in await _uow.Set<OfClockEvent>().AsNoTracking().Where(x => employeeIds.Contains(x.EmployeeId) && x.Timestamp >= from && x.Timestamp < to && x.Approval == OfClockApproval.Approved)
                            .Select(x => new { x.EmployeeId, x.Timestamp }).ToListAsync(ct))
                            taken.Add((e.EmployeeId, Minute(e.Timestamp)));
                    }
                    foreach (OfClockEvent e in events)
                    {
                        if (!taken.Add((e.EmployeeId, Minute(e.Timestamp))))
                        {
                            e.Approval = OfClockApproval.Rejected; e.RejectReason = "تکراری: برای همین دقیقه تردد تاییدشده وجود دارد."; e.ApprovedByUserId = Me; e.ApprovedAt = now;
                            continue;
                        }
                        e.Approval = OfClockApproval.Approved; e.ApprovedByUserId = Me; e.ApprovedAt = now;
                    }
                    batch.Status = OfClockBatchStatus.Approved;
                }
                else
                {
                    // the reason of the rejection sits on the batch; the rows are not given 20,000 copies of it
                    foreach (OfClockEvent e in events) { e.Approval = OfClockApproval.Rejected; e.ApprovedByUserId = Me; e.ApprovedAt = now; }
                    batch.Status = OfClockBatchStatus.Rejected;
                }
                batch.DecidedByUserId = Me; batch.DecidedAt = now; batch.DecisionNote = note;
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { return Bad<ClockImportBatchDto>("ذخیره تصمیم انجام نشد؛ دوباره تلاش کنید."); }
            }
            finally { ClockStoreLock.Release(); }
            await BatchGateAsync(batch, false, ct);
            if (action == "approve")
            {
                List<DateTime> approved = events.Where(e => e.Approval == OfClockApproval.Approved).Select(e => e.Timestamp).ToList();
                if (approved.Count > 0) await RecomputeAroundAsync(approved.Min().Date.AddDays(-1), approved.Max().Date.AddDays(1), ct);
            }
            return ResponseDtoHelper.Ok((await BatchDtosAsync(new List<OfClockImportBatch> { batch }, ct))[0]);
        }

        /// <summary>Recomputes the days around newly approved punches (in steps of 60 days; a span of more than a year is left to the daily job and the month close).</summary>
        private async Task RecomputeAroundAsync(DateTime min, DateTime max, CancellationToken ct)
        {
            if ((max - min).TotalDays > 400) return;
            for (DateTime from = min; from <= max; from = from.AddDays(60))
                await ComputeCoreAsync(from, from.AddDays(59) < max ? from.AddDays(59) : max, null, ct);
        }

        // ---- device pushes

        /// <summary>
        /// A wrong code, key or address is kept in the push log (outcome Rejected / RejectedAddress), but a wrong key must not be able to fill the table: one row per device code per minute and at most
        /// 30 rejected rows a minute. The log never changes the answer to the device. The two counts read the index (Outcome, ReceivedAt).
        /// </summary>
        private async Task LogRejectedPushAsync(int? deviceId, string code, string outcome, CancellationToken ct)
        {
            try
            {
                DateTimeOffset since = DateTimeOffset.Now.AddMinutes(-1);
                string cut = Cut(code, 40);
                string[] rejected = { "Rejected", "RejectedAddress" };
                if (await _uow.Set<OfDevicePushLog>().CountAsync(l => rejected.Contains(l.Outcome) && l.ReceivedAt >= since, ct) >= 30) return;
                if (await _uow.Set<OfDevicePushLog>().AnyAsync(l => rejected.Contains(l.Outcome) && l.ReceivedAt >= since && l.DeviceCode == cut, ct)) return;
                LogPush(deviceId, cut, 0, 0, 0, null, outcome);
                await _uow.SaveChangesAsync(ct);
            }
            catch (Exception)
            {
                // the log must never break the answer to the device
            }
        }

        /// <summary>The setting that tells how long the push log is kept (days); the default and the fallback for a wrong value is 90 (★).</summary>
        internal const string SettingPushLogDays = FinanceSeed.SettingPushLogDays;
        internal const int DefaultPushLogDays = 90;

        private async Task<int> PushLogDaysAsync(CancellationToken ct)
        {
            string text = await _uow.Set<FnSetting>().AsNoTracking().Where(s => s.Key == SettingPushLogDays).Select(s => s.Value).FirstOrDefaultAsync(ct);
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int days) && days >= 30 && days <= 3650 ? days : DefaultPushLogDays;
        }

        /// <summary>The retention of the push log: rows older than the setting are removed by the daily job (in slices, so one night never holds a huge delete).</summary>
        internal async Task<int> PurgePushLogsAsync(CancellationToken ct)
        {
            DateTimeOffset cutoff = DateTimeOffset.Now.AddDays(-await PushLogDaysAsync(ct));
            int removed = 0;
            for (int round = 0; round < 40; round++)
            {
                List<OfDevicePushLog> old = await _uow.Set<OfDevicePushLog>().Where(l => l.ReceivedAt < cutoff).OrderBy(l => l.Id).Take(2000).ToListAsync(ct);
                if (old.Count == 0) break;
                _uow.Set<OfDevicePushLog>().RemoveRange(old);
                await _uow.SaveChangesAsync(ct);
                removed += old.Count;
                if (old.Count < 2000) break;
            }
            return removed;
        }

        /// <summary>An inbox alert for HR: punches of a device were refused because their month is closed (a late sync of an offline device). One item per device and day.</summary>
        private async Task ClosedMonthPushAlertAsync(OfDevice device, int count, CancellationToken ct)
        {
            if (_notify == null || device == null || count <= 0) return;
            try
            {
                await _notify.NotifyAsync(new NotifyRequest
                {
                    SourceKey = $"att:push-closed:{device.Id}:{DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}", Module = "office", SourceType = "device-push", SourceId = device.Id,
                    Title = $"{count} تردد از دستگاه {device.Code} ذخیره نشد چون ماه آن بسته است (حضور و غیاب تایید یا حقوق تایید شده)؛ ترددها را بررسی کنید و در صورت نیاز با ثبت دستی یا اصلاح کارکرد جبران کنید.",
                    Link = "/office/attendance/device-pushes", Roles = OfficeRoles.Hr, Kind = OfInboxKind.Alert,
                }, ct);
            }
            catch (Exception)
            {
                // the inbox must never block a push
            }
        }

        public async Task<ResponseDto<DevicePushResultDto>> PushAsync(string deviceCode, string apiKey, DevicePushDto dto, CancellationToken ct, string remoteAddress = null)
        {
            string code = (Clean(deviceCode) ?? "").ToUpperInvariant();
            async Task<ResponseDto<DevicePushResultDto>> Rejected(int? deviceId, string outcome = "Rejected")
            {
                await LogRejectedPushAsync(deviceId, code, outcome, ct);
                return ResponseDtoHelper.Create<DevicePushResultDto>(false, DeviceRejected).HasHttpStatusCode(HttpStatusCode.Unauthorized);
            }
            if (code.Length == 0 || string.IsNullOrEmpty(apiKey)) return await Rejected(null);
            OfDevice device = await _uow.Set<OfDevice>().FirstOrDefaultAsync(d => d.Code == code && !d.Deleted && d.IsActive, ct);
            if (device == null) { HashKey(apiKey); return await Rejected(null); }
            byte[] given = Encoding.UTF8.GetBytes(HashKey(apiKey));
            byte[] stored = Encoding.UTF8.GetBytes(device.ApiKeyHash ?? "");
            if (given.Length != stored.Length || !CryptographicOperations.FixedTimeEquals(given, stored)) return await Rejected(device.Id);
            // release R1f: a device bound to LAN addresses is refused, and the attempt logged, from any other address: the key alone is not enough
            if (!DeviceAddress.Allows(device.AllowedAddresses, remoteAddress)) return await Rejected(device.Id, "RejectedAddress");
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
            StoreResult store;
            await ClockStoreLock.WaitAsync(ct);
            try
            {
                // the punches of the employee who created or last rotated this device's key are stored Pending: he has seen the key, so a second person decides whether the punch is real
                store = await StoreAsync(rows, OfClockSource.Device, device.Code, errors, ct, null, new StoreOptions { HoldUserIds = KeyHolderIds(device) });
                device.LastPushAt = DateTimeOffset.Now;
                // one lightweight row per push (device, count, hash of the payload) instead of one audit row per punch
                LogPush(device.Id, device.Code, dto.Events.Count, store.Added, store.Duplicates, hash, "Push");
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { return Bad<DevicePushResultDto>("ذخیره ترددها انجام نشد؛ دوباره تلاش کنید."); }
            }
            finally { ClockStoreLock.Release(); }
            result.Accepted = store.Added;
            result.Duplicates = store.Duplicates;
            result.Held = store.Held.Count;
            result.Rejected += store.Unknown + store.Rejected;
            result.Errors = errors.Take(50).ToList();
            if (store.Held.Count > 0)
            {
                Dictionary<int, FnEmployee> heldEmployees = await EmployeesAsync(store.Held.Select(e => e.EmployeeId), ct);
                foreach (OfClockEvent ev in store.Held.Take(50))
                {
                    heldEmployees.TryGetValue(ev.EmployeeId, out FnEmployee emp);
                    await ClockGateAsync(ev, emp, true, ct);
                }
            }
            if (store.RejectedClosed > 0) await ClosedMonthPushAlertAsync(device, store.RejectedClosed, ct);
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

        private bool _upgradesChecked;

        /// <summary>
        /// One-time, idempotent upgrade of an existing install to the company rules (answer 5): a row with the old defaults gets grace 15; any other row is left as the admin set it.
        /// The flag CompanyRulesApplied is the marker, so an admin who later chooses grace 0 again is not overwritten. The change is audited like any policy save (with the user who triggered it).
        /// Release R1f adds the one-time steps of <see cref="R1fUpgrades"/> (guarded by their own markers): the overtime approval switch is turned ON on the existing policy rows (an audit row records the
        /// change; a release note tells the company), and the sheets of the months closed before the two-step close are locked.
        /// Called on the write paths (save, compute, close, import); the read shows the same result without writing.
        /// </summary>
        private async Task EnsureCompanyPolicyAsync(CancellationToken ct)
        {
            OfAttendancePolicy p = await _uow.Set<OfAttendancePolicy>().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (p != null && !p.CompanyRulesApplied)
            {
                if (NeedsCompanyUpgrade(p)) p.GraceMinutes = 15;
                p.CompanyRulesApplied = true; p.UpdatedAt = DateTimeOffset.Now; p.UpdatedByUserId = Me;
                try { await _uow.SaveChangesAsync(ct); }
                catch (DbUpdateException) { await _uow.Entry(p).ReloadAsync(ct); }
            }
            if (_upgradesChecked) return;
            await R1fUpgrades.EnsureAsync(_uow, Me, ct);
            _upgradesChecked = true;
        }

        public async Task<ResponseDto<AttendancePolicyDto>> GetPolicyAsync(CancellationToken ct)
        {
            OfAttendancePolicy p = await _uow.Set<OfAttendancePolicy>().AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct) ?? new OfAttendancePolicy();
            if (NeedsCompanyUpgrade(p)) p.GraceMinutes = 15; // what the first write will store
            if (!await R1fUpgrades.OvertimeApprovalDoneAsync(_uow, ct)) p.OvertimeRequiresApproval = true; // what the first write will store
            return ResponseDtoHelper.Ok(ToDto(p));
        }

        public async Task<ResponseDto<AttendancePolicyDto>> SavePolicyAsync(AttendancePolicyDto dto, CancellationToken ct)
        {
            if (!(IsHr || _user.IsInRole(OfficeRoles.Ceo))) return Bad<AttendancePolicyDto>(OfficeText.NotAllowed);   // R1t (D25) + R1f: HR changes the policy, only the CEO switches the two controls off; the PMO reads
            if (dto == null) return Bad<AttendancePolicyDto>(OfficeText.InvalidData);
            if (dto.GraceMinutes < 0 || dto.GraceMinutes > 120) return Bad<AttendancePolicyDto>("مهلت تاخیر باید بین ۰ تا ۱۲۰ دقیقه باشد.");
            if (dto.MaxOvertimeMinutesPerDay.HasValue && (dto.MaxOvertimeMinutesPerDay < 0 || dto.MaxOvertimeMinutesPerDay > 720)) return Bad<AttendancePolicyDto>("سقف اضافه‌کار روزانه نامعتبر است.");
            if (dto.RoundingMinutes < 0 || dto.RoundingMinutes > 60) return Bad<AttendancePolicyDto>("گرد کردن اضافه‌کار باید بین ۰ تا ۶۰ دقیقه باشد.");
            if (dto.MonthlyOvertimeCapMinutes.HasValue && (dto.MonthlyOvertimeCapMinutes < 0 || dto.MonthlyOvertimeCapMinutes > 31 * 24 * 60)) return Bad<AttendancePolicyDto>("سقف ماهانه اضافه‌کار نامعتبر است (۰ تا ۴۴٬۶۴۰ دقیقه).");
            if (dto.DelayPenaltyFactor.HasValue && (dto.DelayPenaltyFactor < 0m || dto.DelayPenaltyFactor > 10m)) return Bad<AttendancePolicyDto>("ضریب کسر تاخیر باید بین ۰ تا ۱۰ باشد (۰ یعنی بدون کسر).");
            await EnsureCompanyPolicyAsync(ct);
            OfAttendancePolicy p = await _uow.Set<OfAttendancePolicy>().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            // the confirmation rule of the month close is a control, not a tuning knob: only the CEO changes who may confirm
            bool ceo = _user.IsInRole(OfficeRoles.Ceo);
            if (dto.MonthConfirmByHrPeer.HasValue && dto.MonthConfirmByHrPeer.Value != (p?.MonthConfirmByHrPeer ?? false) && !ceo)
                return Bad<AttendancePolicyDto>("تغییر اینکه چه کسی بستن ماه را تایید می‌کند فقط با مدیرعامل است.");
            // release R1f: the two company controls are switched OFF only by the CEO (turning one on stays with HR); otherwise HR could switch a control off, compute and prepare a month, and switch it back on
            if (dto.OvertimeRequiresApproval == false && (p?.OvertimeRequiresApproval ?? true) && !ceo)
                return Bad<AttendancePolicyDto>("خاموش کردن «تایید پیشینی اضافه‌کار» فقط با مدیرعامل است؛ روشن کردن آن با منابع انسانی است.");
            if (dto.EarlyLeaveDeduction == false && !(p?.EarlyLeaveDeductionDisabled ?? false) && !ceo)
                return Bad<AttendancePolicyDto>("خاموش کردن «کسر خروج زودهنگام» فقط با مدیرعامل است؛ روشن کردن آن با منابع انسانی است.");
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
