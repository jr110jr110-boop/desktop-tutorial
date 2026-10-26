using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface ICalibrationService
    {
        Task<ResponseDto<PagedList<EquipmentListDto>>> GetEquipmentAsync(EquipmentQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<EquipmentDto>> GetEquipmentAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<EquipmentDto>> SaveEquipmentAsync(SaveEquipmentDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<EquipmentDto>> EquipmentActionAsync(EquipmentActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<EquipmentDto>> AddEventAsync(SaveCalibrationEventDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<EquipmentListDto>>> GetDueAsync(DueQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<List<CalibrationIntervalDto>>> GetIntervalsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<List<CalibrationIntervalDto>>> SaveIntervalAsync(CalibrationIntervalDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<OutOfToleranceListDto>>> GetCasesAsync(OutOfToleranceQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<OutOfToleranceDto>> GetCaseAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<OutOfToleranceDto>> AssessAsync(AssessOutOfToleranceDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<OutOfToleranceDto>> LinkReinspectionAsync(LinkOotReinspectionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<OutOfToleranceDto>> CloseCaseAsync(CloseOutOfToleranceDto dto, CancellationToken cancellationToken);
    }

    /// <summary>Measuring equipment, calibration events and the out-of-tolerance impact assessment (WI-QC-001, ISO 7.1.5).</summary>
    public class CalibrationService : ICalibrationService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public CalibrationService(IUnitOfWork uow, IInventoryUserContext user, global::Services.NasbNiro.Office.INotificationService notify)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        // -------------------------------------------------------------------------------- default intervals

        private static readonly (EquipmentType Type, int Months, string Source)[] IntervalSeed =
        {
            (EquipmentType.DimensionalHandTool, 12, "★ WI-QC-001 (تصمیم موقت)"),
            (EquipmentType.Gauge, 12, "★ WI-QC-001 (تصمیم موقت)"),
            (EquipmentType.PressureGauge, 6, "★ WI-QC-001 (تصمیم موقت)"),
            (EquipmentType.TemperatureInstrument, 12, "★ WI-QC-001 (تصمیم موقت)"),
            (EquipmentType.ElectricalMeter, 12, "★ WI-QC-001 (تصمیم موقت)"),
            (EquipmentType.NdtEquipment, 12, "★ WI-QC-001 (تجهیزات NDT، لوکس‌متر و UV، آزمون نیروی یوک)"),
            (EquipmentType.TestStandInstrument, 12, "★ WI-QC-001 (تصمیم موقت)"),
            (EquipmentType.Other, 12, "★ تصمیم موقت"),
        };

        private async Task EnsureIntervalsAsync(CancellationToken cancellationToken)
        {
            DbSet<CalibrationIntervalDefault> set = _uow.Set<CalibrationIntervalDefault>();
            List<EquipmentType> have = await set.Select(i => i.EquipmentType).ToListAsync(cancellationToken);
            bool added = false;
            foreach (var seed in IntervalSeed.Where(s => !have.Contains(s.Type)))
            {
                set.Add(new CalibrationIntervalDefault { EquipmentType = seed.Type, Months = seed.Months, Source = seed.Source });
                added = true;
            }
            if (added) await _uow.SaveChangesAsync(cancellationToken);
        }

        public async Task<ResponseDto<List<CalibrationIntervalDto>>> GetIntervalsAsync(CancellationToken cancellationToken)
        {
            await EnsureIntervalsAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await IntervalsAsync(cancellationToken));
        }

        private async Task<List<CalibrationIntervalDto>> IntervalsAsync(CancellationToken cancellationToken) =>
            await _uow.Set<CalibrationIntervalDefault>().OrderBy(i => i.EquipmentType)
                .Select(i => new CalibrationIntervalDto { EquipmentType = i.EquipmentType, Months = i.Months, Source = i.Source }).ToListAsync(cancellationToken);

        public async Task<ResponseDto<List<CalibrationIntervalDto>>> SaveIntervalAsync(CalibrationIntervalDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(EquipmentType), dto.EquipmentType) || dto.Months < 1 || dto.Months > 60) return Bad<List<CalibrationIntervalDto>>("فاصله کالیبراسیون باید بین ۱ تا ۶۰ ماه باشد.");
            if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<List<CalibrationIntervalDto>>(QualityText.NotAllowed);
            await EnsureIntervalsAsync(cancellationToken);
            CalibrationIntervalDefault row = await _uow.Set<CalibrationIntervalDefault>().FirstAsync(i => i.EquipmentType == dto.EquipmentType, cancellationToken);
            row.Months = dto.Months; row.Source = Clean(dto.Source) ?? "تعیین‌شده توسط شرکت";
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await IntervalsAsync(cancellationToken));
        }

        // -------------------------------------------------------------------------------- equipment

        private async Task<int> WarnDaysAsync(CancellationToken cancellationToken) =>
            (int)(await QualitySettingKeys.LoadAsync(_uow, cancellationToken))[QualitySettingKeys.CalibrationWarnDays];

        private static EquipmentListDto ToList(MeasuringEquipment e, DateTime today, int warn) => new EquipmentListDto
        {
            Id = e.Id, Code = e.Code, Name = e.Name, Type = e.Type, SerialNumber = e.SerialNumber, Location = e.Location,
            RequiresCalibration = e.RequiresCalibration, Status = CalibrationRules.Effective(e, today, warn), IntervalMonths = e.IntervalMonths,
            LastCalibrated = e.LastCalibrated, NextDue = e.NextDue, DaysToDue = e.NextDue.HasValue ? (int)(e.NextDue.Value.Date - today).TotalDays : (int?)null,
        };

        public async Task<ResponseDto<PagedList<EquipmentListDto>>> GetEquipmentAsync(EquipmentQuery query, CancellationToken cancellationToken)
        {
            query ??= new EquipmentQuery();
            DateTime today = DateTime.Today;
            int warn = await WarnDaysAsync(cancellationToken);
            IQueryable<MeasuringEquipment> rows = _uow.Set<MeasuringEquipment>().Where(e => !e.Deleted);
            if (query.Type.HasValue) rows = rows.Where(e => e.Type == query.Type.Value);
            if (query.Status.HasValue)
            {
                DateTime soon = today.AddDays(warn);
                switch (query.Status.Value)
                {
                    case EquipmentStatus.OutOfService: rows = rows.Where(e => e.Status == EquipmentStatus.OutOfService); break;
                    case EquipmentStatus.NotRequired: rows = rows.Where(e => e.Status != EquipmentStatus.OutOfService && !e.RequiresCalibration); break;
                    case EquipmentStatus.Overdue: rows = rows.Where(e => e.Status != EquipmentStatus.OutOfService && e.RequiresCalibration && (e.NextDue == null || e.NextDue < today)); break;
                    case EquipmentStatus.DueSoon: rows = rows.Where(e => e.Status != EquipmentStatus.OutOfService && e.RequiresCalibration && e.NextDue >= today && e.NextDue <= soon); break;
                    default: rows = rows.Where(e => e.Status != EquipmentStatus.OutOfService && e.RequiresCalibration && e.NextDue > soon); break;
                }
            }
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(e => e.Code.Contains(text) || e.Name.Contains(text) || e.SerialNumber.Contains(text));
            int total = await rows.CountAsync(cancellationToken);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize;
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MeasuringEquipment> items = await rows.OrderBy(e => e.Code).Skip((number - 1) * size).Take(size).ToListAsync(cancellationToken);
            PagedList<EquipmentListDto> page = await PagedList<EquipmentListDto>.CreateAsync(items.Select(e => ToList(e, today, warn)).ToList(), 1, 0, cancellationToken);
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)size);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<PagedList<EquipmentListDto>>> GetDueAsync(DueQuery query, CancellationToken cancellationToken)
        {
            query ??= new DueQuery();
            DateTime today = DateTime.Today;
            int days = query.Days <= 0 ? 30 : query.Days;
            DateTime limit = today.AddDays(days);
            int warn = await WarnDaysAsync(cancellationToken);
            IQueryable<MeasuringEquipment> rows = _uow.Set<MeasuringEquipment>().Where(e => !e.Deleted && e.RequiresCalibration && e.Status != EquipmentStatus.OutOfService &&
                (e.NextDue == null || e.NextDue <= limit));
            List<EquipmentListDto> list = (await rows.OrderBy(e => e.NextDue).ThenBy(e => e.Code).ToListAsync(cancellationToken)).Select(e => ToList(e, today, warn)).ToList();
            return ResponseDtoHelper.Ok(await PagedList<EquipmentListDto>.CreateAsync(list, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<EquipmentDto>> GetEquipmentAsync(int id, CancellationToken cancellationToken)
        {
            MeasuringEquipment e = await _uow.Set<MeasuringEquipment>().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (e == null) return QualityText.NotFound<EquipmentDto>("تجهیز اندازه‌گیری پیدا نشد.");
            int warn = await WarnDaysAsync(cancellationToken);
            List<CalibrationEvent> events = await _uow.Set<CalibrationEvent>().Where(x => x.EquipmentId == id && !x.Deleted).OrderByDescending(x => x.EventDate).ThenByDescending(x => x.Id).ToListAsync(cancellationToken);
            var cases = await _uow.Set<OutOfToleranceCase>().Where(c => c.EquipmentId == id && !c.Deleted).Select(c => new { c.Id, c.Number, c.CalibrationEventId }).ToListAsync(cancellationToken);
            EquipmentListDto l = ToList(e, DateTime.Today, warn);
            return ResponseDtoHelper.Ok(new EquipmentDto
            {
                Id = l.Id, Code = l.Code, Name = l.Name, Type = l.Type, SerialNumber = l.SerialNumber, Location = l.Location, RequiresCalibration = l.RequiresCalibration,
                Status = l.Status, IntervalMonths = l.IntervalMonths, LastCalibrated = l.LastCalibrated, NextDue = l.NextDue, DaysToDue = l.DaysToDue,
                Maker = e.Maker, Model = e.Model, OwnerUnit = e.OwnerUnit, Range = e.Range, Accuracy = e.Accuracy, OutOfServiceReason = e.OutOfServiceReason,
                CorrectionTableDocumentId = e.CorrectionTableDocumentId, Note = e.Note, RowVersion = e.RowVersion == null ? null : Convert.ToBase64String(e.RowVersion),
                CalibrationWaiverReason = e.CalibrationWaiverReason, CalibrationWaivedAt = e.CalibrationWaivedAt,
                Events = events.Select(x => new CalibrationEventDto
                {
                    Id = x.Id, EquipmentId = x.EquipmentId, EquipmentCode = e.Code, EventDate = x.EventDate, LabName = x.LabName, LabAccredited = x.LabAccredited,
                    CertificateNumber = x.CertificateNumber, CertificateDocumentId = x.CertificateDocumentId, TraceabilityStatement = x.TraceabilityStatement,
                    AsFoundPass = x.AsFoundPass, AsLeftPass = x.AsLeftPass, Adjusted = x.Adjusted, IntervalMonths = x.IntervalMonths, NextDue = x.NextDue, Note = x.Note,
                    OotCaseId = cases.FirstOrDefault(c => c.CalibrationEventId == x.Id)?.Id, OotNumber = cases.FirstOrDefault(c => c.CalibrationEventId == x.Id)?.Number,
                }).ToList(),
            });
        }

        private string WaiverProblem(SaveEquipmentDto dto, bool waives)
        {
            if (!waives) return null;
            if (!_user.IsInRole(QualityRoles.QaManager))
                return "اعلام اینکه تجهیز نیاز به کالیبراسیون ندارد تصمیم مدیر QA است و با ثبت دلیل انجام می‌شود؛ QC نمی‌تواند آن را خاموش کند.";
            if (Clean(dto.CalibrationWaiverReason) == null) return "دلیل بی‌نیازی تجهیز به کالیبراسیون را بنویسید.";
            return null;
        }

        public async Task<ResponseDto<EquipmentDto>> SaveEquipmentAsync(SaveEquipmentDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Name) == null || !Enum.IsDefined(typeof(EquipmentType), dto.Type)) return Bad<EquipmentDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Inspectors + "," + QualityRoles.QaManager)) return Bad<EquipmentDto>(QualityText.NotAllowed + " (تجهیزات اندازه‌گیری را QC ثبت می‌کند)");
            if (dto.IntervalMonths.HasValue && (dto.IntervalMonths.Value < 1 || dto.IntervalMonths.Value > 60)) return Bad<EquipmentDto>("فاصله کالیبراسیون باید بین ۱ تا ۶۰ ماه باشد.");
            await EnsureIntervalsAsync(cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            MeasuringEquipment e;
            // R1q D14: an instrument that needs no calibration is usable even when it was never calibrated, so declaring that (when registering it or later)
            // is a decision of the QA manager with a reason
            bool waives = !dto.RequiresCalibration;
            if (dto.Id.HasValue)
            {
                e = await _uow.Set<MeasuringEquipment>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (e == null) return QualityText.NotFound<EquipmentDto>("تجهیز اندازه‌گیری پیدا نشد.");
                waives = !dto.RequiresCalibration && e.RequiresCalibration;
                string waiverProblem = WaiverProblem(dto, waives);
                if (waiverProblem != null) return Bad<EquipmentDto>(waiverProblem);
                if (Clean(dto.RowVersion) != null)
                {
                    try { _uow.Entry(e).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); }
                    catch (FormatException) { return Bad<EquipmentDto>(QualityText.InvalidData); }
                }
            }
            else
            {
                string waiverProblem = WaiverProblem(dto, waives);
                if (waiverProblem != null) return Bad<EquipmentDto>(waiverProblem);
                int interval = dto.IntervalMonths ?? await _uow.Set<CalibrationIntervalDefault>().Where(i => i.EquipmentType == dto.Type).Select(i => i.Months).FirstOrDefaultAsync(cancellationToken);
                int number = await InventoryNumbers.NextAsync(_uow, QualityNumbers.Equipment, 0, cancellationToken);
                e = new MeasuringEquipment
                {
                    Code = $"INN-IST-{number:000}", IntervalMonths = interval > 0 ? interval : 12,
                    Status = dto.RequiresCalibration ? EquipmentStatus.Overdue : EquipmentStatus.NotRequired, CreateDate = now, CreatorUserId = Me,
                };
                _uow.Set<MeasuringEquipment>().Add(e);
            }
            // the reason, the person and the date are kept on the instrument and in the audit log
            if (waives)
            {
                e.CalibrationWaiverReason = Clean(dto.CalibrationWaiverReason); e.CalibrationWaivedByUserId = Me; e.CalibrationWaivedAt = now;
            }
            else if (dto.RequiresCalibration)
            {
                e.CalibrationWaiverReason = null; e.CalibrationWaivedByUserId = null; e.CalibrationWaivedAt = null;
            }
            e.Name = Clean(dto.Name); e.Type = dto.Type; e.Maker = Clean(dto.Maker); e.Model = Clean(dto.Model); e.SerialNumber = Clean(dto.SerialNumber);
            e.OwnerUnit = Clean(dto.OwnerUnit); e.Location = Clean(dto.Location); e.Range = Clean(dto.Range); e.Accuracy = Clean(dto.Accuracy);
            e.RequiresCalibration = dto.RequiresCalibration; e.CorrectionTableDocumentId = dto.CorrectionTableDocumentId; e.Note = Clean(dto.Note);
            if (dto.IntervalMonths.HasValue) e.IntervalMonths = dto.IntervalMonths.Value;
            if (e.Status != EquipmentStatus.OutOfService && !e.RequiresCalibration) e.Status = EquipmentStatus.NotRequired;
            e.ModifyDate = now; e.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<EquipmentDto>(QualityText.Concurrency);
            }
            return await GetEquipmentAsync(e.Id, cancellationToken);
        }

        public async Task<ResponseDto<EquipmentDto>> EquipmentActionAsync(EquipmentActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<EquipmentDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Inspectors)) return Bad<EquipmentDto>(QualityText.NotAllowed);
            MeasuringEquipment e = await _uow.Set<MeasuringEquipment>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (e == null) return QualityText.NotFound<EquipmentDto>("تجهیز اندازه‌گیری پیدا نشد.");
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "out-of-service":
                    if (Clean(dto.Reason) == null) return Bad<EquipmentDto>("دلیل خارج‌کردن از رده را بنویسید.");
                    e.Status = EquipmentStatus.OutOfService; e.OutOfServiceReason = Clean(dto.Reason);
                    break;
                case "return-to-service":
                    if (e.Status != EquipmentStatus.OutOfService) return Bad<EquipmentDto>("این تجهیز در رده خارج نیست.");
                    if (e.RequiresCalibration && (!e.NextDue.HasValue || e.NextDue.Value.Date < DateTime.Today))
                        return Bad<EquipmentDto>("پیش از بازگرداندن به سرویس، کالیبراسیون معتبر ثبت کنید.");
                    e.Status = e.RequiresCalibration ? EquipmentStatus.Calibrated : EquipmentStatus.NotRequired; e.OutOfServiceReason = null;
                    break;
                default:
                    return Bad<EquipmentDto>("عملیات نامعتبر است.");
            }
            e.ModifyDate = DateTimeOffset.Now; e.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<EquipmentDto>(QualityText.Concurrency); }
            return await GetEquipmentAsync(e.Id, cancellationToken);
        }

        // -------------------------------------------------------------------------------- calibration event

        public async Task<ResponseDto<EquipmentDto>> AddEventAsync(SaveCalibrationEventDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<EquipmentDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.Inspectors)) return Bad<EquipmentDto>(QualityText.NotAllowed + " (کالیبراسیون را QC ثبت می‌کند)");
            MeasuringEquipment e = await _uow.Set<MeasuringEquipment>().FirstOrDefaultAsync(x => x.Id == dto.EquipmentId && !x.Deleted, cancellationToken);
            if (e == null) return QualityText.NotFound<EquipmentDto>("تجهیز اندازه‌گیری پیدا نشد.");
            if (!e.RequiresCalibration) return Bad<EquipmentDto>("این تجهیز نیاز به کالیبراسیون ندارد.");
            DateTime date = (dto.EventDate ?? DateTime.Today).Date;
            if (date > DateTime.Today) return Bad<EquipmentDto>("تاریخ کالیبراسیون نمی‌تواند در آینده باشد.");
            if (Clean(dto.LabName) == null) return Bad<EquipmentDto>("نام آزمایشگاه کالیبراسیون را وارد کنید.");
            if (Clean(dto.CertificateNumber) == null) return Bad<EquipmentDto>("شماره گواهی کالیبراسیون را وارد کنید.");
            if (Clean(dto.TraceabilityStatement) == null) return Bad<EquipmentDto>("بیانیه ردیابی‌پذیری به استاندارد ملی/بین‌المللی (ISO 7.1.5.1) الزامی است.");
            if (dto.IntervalMonths.HasValue && (dto.IntervalMonths.Value < 1 || dto.IntervalMonths.Value > 60)) return Bad<EquipmentDto>("فاصله کالیبراسیون باید بین ۱ تا ۶۰ ماه باشد.");
            List<CalibrationEvent> previous = await _uow.Set<CalibrationEvent>().Where(x => x.EquipmentId == e.Id && !x.Deleted).ToListAsync(cancellationToken);
            if (previous.Any(x => x.EventDate.Date > date)) return Bad<EquipmentDto>("کالیبراسیون جدیدتری برای این تجهیز ثبت شده است.");
            if (previous.Any(x => Clean(x.CertificateNumber) == Clean(dto.CertificateNumber) && x.EventDate.Date == date)) return Bad<EquipmentDto>("این گواهی قبلاً ثبت شده است.");

            int interval = dto.IntervalMonths ?? e.IntervalMonths;
            if (interval < 1) interval = 12;
            DateTimeOffset now = DateTimeOffset.Now;
            DateTime? previousGood = previous.Where(x => x.AsLeftPass).Select(x => (DateTime?)x.EventDate.Date).OrderByDescending(x => x).FirstOrDefault();
            var ev = new CalibrationEvent
            {
                EquipmentId = e.Id, EventDate = date, LabName = Clean(dto.LabName), LabAccredited = dto.LabAccredited, CertificateNumber = Clean(dto.CertificateNumber),
                CertificateDocumentId = dto.CertificateDocumentId, TraceabilityStatement = Clean(dto.TraceabilityStatement), AsFoundPass = dto.AsFoundPass,
                AsLeftPass = dto.AsLeftPass, Adjusted = dto.Adjusted, IntervalMonths = interval, NextDue = dto.AsLeftPass ? date.AddMonths(interval) : (DateTime?)null,
                Note = Clean(dto.Note), CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<CalibrationEvent>().Add(ev);
            e.LastCalibrated = date; e.IntervalMonths = interval; e.NextDue = ev.NextDue;
            if (dto.AsLeftPass)
            {
                if (e.Status == EquipmentStatus.OutOfService && Clean(e.OutOfServiceReason) != null && !dto.AsFoundPass) { /* stays out of service until the impact case is closed */ }
                else { e.Status = EquipmentStatus.Calibrated; e.OutOfServiceReason = null; }
            }
            else
            {
                e.Status = EquipmentStatus.OutOfService; e.OutOfServiceReason = "نتیجه کالیبراسیون (as-left) مردود شد.";
            }
            e.ModifyDate = now; e.ModifierUserId = Me;

            if (!dto.AsFoundPass)
            {
                var kase = new OutOfToleranceCase
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.Oot, "OOT", date, cancellationToken),
                    Equipment = e, CalibrationEvent = ev, FromDate = previousGood, ToDate = date, Status = OotStatus.Open,
                    Items = new List<OutOfToleranceItem>(), CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
                };
                await FillItemsAsync(kase, e.Id, previousGood, date, cancellationToken);
                _uow.Set<OutOfToleranceCase>().Add(kase);
            }
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<EquipmentDto>(QualityText.Concurrency); }
            return await GetEquipmentAsync(e.Id, cancellationToken);
        }

        /// <summary>Every record and report that used the instrument since its previous good calibration (ISO 7.1.5.2).</summary>
        private async Task FillItemsAsync(OutOfToleranceCase kase, int equipmentId, DateTime? from, DateTime to, CancellationToken cancellationToken)
        {
            IQueryable<QcMeasurement> measurements = _uow.Set<QcMeasurement>()
                .Where(m => m.EquipmentId == equipmentId && !m.Record.Deleted && m.Record.RecordDate <= to);
            if (from.HasValue) measurements = measurements.Where(m => m.Record.RecordDate >= from.Value);
            var records = await measurements.Select(m => new { m.RecordId, m.Record.Number, m.Record.RecordDate }).Distinct().ToListAsync(cancellationToken);
            foreach (var r in records)
                kase.Items.Add(new OutOfToleranceItem { SourceKind = "QcRecord", SourceId = r.RecordId, SourceNumber = r.Number, SourceDate = r.RecordDate, Assessment = OotAssessment.Pending });
            IQueryable<TestReportEquipment> used = _uow.Set<TestReportEquipment>()
                .Where(x => x.EquipmentId == equipmentId && !x.Report.Deleted && x.Report.Status != TestReportStatus.Rejected && x.Report.ReportDate <= to);
            if (from.HasValue) used = used.Where(x => x.Report.ReportDate >= from.Value);
            var reports = await used.Select(x => new { x.ReportId, x.Report.Number, x.Report.ReportDate }).Distinct().ToListAsync(cancellationToken);
            foreach (var r in reports)
                kase.Items.Add(new OutOfToleranceItem { SourceKind = "TestReport", SourceId = r.ReportId, SourceNumber = r.Number, SourceDate = r.ReportDate, Assessment = OotAssessment.Pending });
            // R1q D16: incoming inspections now record the instruments used; they are part of the impact too
            var inspections = await _uow.Set<QcInspection>().Where(q => !q.Deleted && q.EquipmentIds != null && q.InspectedAt != null)
                .Select(q => new { q.Id, q.Number, q.EquipmentIds, q.InspectedAt }).ToListAsync(cancellationToken);
            string mark = equipmentId.ToString();
            foreach (var q in inspections)
            {
                DateTime day = q.InspectedAt.Value.Date;
                if (day > to.Date || (from.HasValue && day < from.Value.Date)) continue;
                if (!q.EquipmentIds.Split(',').Any(x => x.Trim() == mark)) continue;
                kase.Items.Add(new OutOfToleranceItem { SourceKind = "Inspection", SourceId = q.Id, SourceNumber = q.Number, SourceDate = day, Assessment = OotAssessment.Pending });
            }
        }

        // -------------------------------------------------------------------------------- out-of-tolerance cases

        public async Task<ResponseDto<PagedList<OutOfToleranceListDto>>> GetCasesAsync(OutOfToleranceQuery query, CancellationToken cancellationToken)
        {
            query ??= new OutOfToleranceQuery();
            IQueryable<OutOfToleranceCase> rows = _uow.Set<OutOfToleranceCase>().Where(c => !c.Deleted);
            if (query.Status.HasValue) rows = rows.Where(c => c.Status == query.Status.Value);
            if (query.EquipmentId.HasValue) rows = rows.Where(c => c.EquipmentId == query.EquipmentId.Value);
            IQueryable<OutOfToleranceListDto> projection = rows.OrderByDescending(c => c.Id).Select(c => new OutOfToleranceListDto
            {
                Id = c.Id, Number = c.Number, EquipmentId = c.EquipmentId, EquipmentCode = c.Equipment.Code, FromDate = c.FromDate, ToDate = c.ToDate, Status = c.Status,
                ItemCount = c.Items.Count, PendingCount = c.Items.Count(i => i.Assessment == OotAssessment.Pending),
            });
            return ResponseDtoHelper.Ok(await PagedList<OutOfToleranceListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<OutOfToleranceDto>> GetCaseAsync(int id, CancellationToken cancellationToken)
        {
            OutOfToleranceCase c = await _uow.Set<OutOfToleranceCase>().Include(x => x.Equipment).Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (c == null) return QualityText.NotFound<OutOfToleranceDto>("پرونده خارج از تلرانس پیدا نشد.");
            List<int> ncrIds = c.Items.Where(i => i.NcrId.HasValue).Select(i => i.NcrId.Value).ToList();
            Dictionary<int, string> ncrs = await _uow.Set<NonConformance>().Where(n => ncrIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, n => n.Number, cancellationToken);
            var reinspections = new Dictionary<int, string>();
            foreach (OutOfToleranceItem linked in c.Items.Where(i => i.ReinspectionRecordId.HasValue))
                reinspections[linked.Id] = await ReinspectionNumberAsync(linked.ReinspectionKind, linked.ReinspectionRecordId.Value, cancellationToken);
            return ResponseDtoHelper.Ok(new OutOfToleranceDto
            {
                Id = c.Id, Number = c.Number, EquipmentId = c.EquipmentId, EquipmentCode = c.Equipment.Code, FromDate = c.FromDate, ToDate = c.ToDate, Status = c.Status,
                ItemCount = c.Items.Count, PendingCount = c.Items.Count(i => i.Assessment == OotAssessment.Pending), CalibrationEventId = c.CalibrationEventId,
                Note = c.Note, ClosedAt = c.ClosedAt,
                Items = c.Items.OrderBy(i => i.SourceDate).ThenBy(i => i.Id).Select(i => new OutOfToleranceItemDto
                {
                    Id = i.Id, SourceKind = i.SourceKind, SourceId = i.SourceId, SourceNumber = i.SourceNumber, SourceDate = i.SourceDate, Assessment = i.Assessment,
                    AssessmentNote = i.AssessmentNote, NcrId = i.NcrId, NcrNumber = i.NcrId.HasValue && ncrs.TryGetValue(i.NcrId.Value, out string n) ? n : null,
                    ReinspectionKind = i.ReinspectionKind, ReinspectionRecordId = i.ReinspectionRecordId,
                    ReinspectionNumber = i.ReinspectionRecordId.HasValue ? reinspections.GetValueOrDefault(i.Id) : null,
                }).ToList(),
            });
        }

        public async Task<ResponseDto<OutOfToleranceDto>> AssessAsync(AssessOutOfToleranceDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || !Enum.IsDefined(typeof(OotAssessment), dto.Assessment) || dto.Assessment == OotAssessment.Pending) return Bad<OutOfToleranceDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<OutOfToleranceDto>(QualityText.NotAllowed);
            OutOfToleranceItem item = await _uow.Set<OutOfToleranceItem>().Include(i => i.Case).FirstOrDefaultAsync(i => i.Id == dto.ItemId, cancellationToken);
            if (item == null) return QualityText.NotFound<OutOfToleranceDto>("ردیف پرونده پیدا نشد.");
            if (item.Case.Status == OotStatus.Closed) return Bad<OutOfToleranceDto>("پرونده بسته شده است.");
            if (Clean(dto.Note) == null) return Bad<OutOfToleranceDto>("توضیح ارزیابی اثر را بنویسید.");
            int? ncrId = null;
            if (dto.Assessment == OotAssessment.Ncr)
            {
                if (dto.NcrId.HasValue)
                {
                    if (!await _uow.Set<NonConformance>().AnyAsync(n => n.Id == dto.NcrId.Value && !n.Deleted, cancellationToken)) return Bad<OutOfToleranceDto>("گزارش عدم انطباق پیدا نشد.");
                    ncrId = dto.NcrId;
                }
                else
                {
                    int? itemId = null; int? workOrderId = null; int? lotId = null; string serial = null;
                    if (item.SourceKind == "QcRecord")
                    {
                        var r = await _uow.Set<QcRecord>().Where(x => x.Id == item.SourceId).Select(x => new { x.ItemId, x.WorkOrderId, x.LotId, x.SerialNumber }).FirstOrDefaultAsync(cancellationToken);
                        itemId = r?.ItemId; workOrderId = r?.WorkOrderId; lotId = r?.LotId; serial = r?.SerialNumber;
                    }
                    else if (item.SourceKind == "Inspection")
                    {
                        var r = await _uow.Set<QcInspection>().Where(x => x.Id == item.SourceId).Select(x => new { x.ItemId, x.LotId }).FirstOrDefaultAsync(cancellationToken);
                        itemId = r?.ItemId; lotId = r?.LotId;
                    }
                    else
                    {
                        var r = await _uow.Set<TestReport>().Where(x => x.Id == item.SourceId).Select(x => new { x.ItemId, x.WorkOrderId, x.SerialNumber }).FirstOrDefaultAsync(cancellationToken);
                        itemId = r?.ItemId; workOrderId = r?.WorkOrderId; serial = r?.SerialNumber;
                    }
                    if (!itemId.HasValue) return Bad<OutOfToleranceDto>("کالای مرتبط با این سند مشخص نیست؛ گزارش عدم انطباق را دستی بسازید و شماره آن را وارد کنید.");
                    string code = await _uow.Set<MeasuringEquipment>().Where(x => x.Id == item.Case.EquipmentId).Select(x => x.Code).FirstAsync(cancellationToken);
                    NonConformance ncr = await QualityNcrFactory.DraftAsync(_uow, Me, NcrSource.InProcess, itemId.Value, lotId, 1, serial, workOrderId, null,
                        $"تجهیز اندازه‌گیری {code} در کالیبراسیون خارج از تلرانس بود؛ سند {item.SourceNumber} با آن انجام شده است: {Clean(dto.Note)}",
                        item.SourceNumber, item.Case.EquipmentId, cancellationToken);
                    await _uow.SaveChangesAsync(cancellationToken);
                    ncrId = ncr.Id;
                }
            }
            item.Assessment = dto.Assessment; item.AssessmentNote = Clean(dto.Note); item.NcrId = ncrId;
            item.AssessedAt = DateTimeOffset.Now; item.AssessedByUserId = Me;
            // R1q D40: "re-inspect" is a task, not a tick: QC gets an inbox item and the case cannot close until the new inspection is linked
            if (dto.Assessment != OotAssessment.Reinspect) { item.ReinspectionKind = null; item.ReinspectionRecordId = null; }
            await _uow.SaveChangesAsync(cancellationToken);
            await SyncReinspectionGateAsync(item, cancellationToken);
            if (ncrId.HasValue && !dto.NcrId.HasValue)
            {
                NonConformance drafted = await _uow.Set<NonConformance>().FirstOrDefaultAsync(n => n.Id == ncrId.Value, cancellationToken);
                await NcrGates.SyncAsync(_notify, drafted, cancellationToken);
            }
            return await GetCaseAsync(item.CaseId, cancellationToken);
        }

        private Task SyncReinspectionGateAsync(OutOfToleranceItem item, CancellationToken cancellationToken) =>
            global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "oot-reinspect:" + item.Id, item.Assessment == OotAssessment.Reinspect && !item.ReinspectionRecordId.HasValue,
                "quality", "oot-reinspection", item.Id, $"بازرسی مجدد سند {item.SourceNumber} (تجهیز خارج از تلرانس)", "/quality/out-of-tolerance/" + item.CaseId,
                QualityRoles.Inspectors, cancellationToken, DateTime.Today.AddDays(7));

        private async Task<string> ReinspectionNumberAsync(string kind, int id, CancellationToken cancellationToken)
        {
            switch (kind)
            {
                case NcrReinspection.Record: return await _uow.Set<QcRecord>().Where(x => x.Id == id).Select(x => x.Number).FirstOrDefaultAsync(cancellationToken);
                case NcrReinspection.Report: return await _uow.Set<TestReport>().Where(x => x.Id == id).Select(x => x.Number).FirstOrDefaultAsync(cancellationToken);
                case NcrReinspection.Inspection: return await _uow.Set<QcInspection>().Where(x => x.Id == id).Select(x => x.Number).FirstOrDefaultAsync(cancellationToken);
                default: return null;
            }
        }

        /// <summary>
        /// Links the new, accepted inspection that re-inspected a document assessed as "re-inspect" (R1q D40). The new one must be of the same product, made after the
        /// calibration that found the instrument out of tolerance, and must not be one of the documents of the case itself.
        /// </summary>
        public async Task<ResponseDto<OutOfToleranceDto>> LinkReinspectionAsync(LinkOotReinspectionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Kind) == null) return Bad<OutOfToleranceDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<OutOfToleranceDto>(QualityText.NotAllowed);
            OutOfToleranceItem item = await _uow.Set<OutOfToleranceItem>().Include(i => i.Case).ThenInclude(c => c.Items).FirstOrDefaultAsync(i => i.Id == dto.ItemId, cancellationToken);
            if (item == null) return QualityText.NotFound<OutOfToleranceDto>("ردیف پرونده پیدا نشد.");
            if (item.Case.Status == OotStatus.Closed) return Bad<OutOfToleranceDto>("پرونده بسته شده است.");
            if (item.Assessment != OotAssessment.Reinspect) return Bad<OutOfToleranceDto>("بازرسی مجدد فقط برای ردیفی ثبت می‌شود که ارزیابی آن «بازرسی مجدد» است.");
            string problem = await CheckReinspectionAsync(item, dto.Kind, dto.RecordId, cancellationToken);
            if (problem != null) return Bad<OutOfToleranceDto>(problem);
            item.ReinspectionKind = dto.Kind; item.ReinspectionRecordId = dto.RecordId;
            await _uow.SaveChangesAsync(cancellationToken);
            await SyncReinspectionGateAsync(item, cancellationToken);
            return await GetCaseAsync(item.CaseId, cancellationToken);
        }

        private async Task<string> CheckReinspectionAsync(OutOfToleranceItem item, string kind, int recordId, CancellationToken cancellationToken)
        {
            if (item.Case.Items.Any(i => i.SourceKind == kind && i.SourceId == recordId))
                return "این سند خودش از اسناد پرونده است (با همان تجهیز انجام شده)، بازرسی مجدد نیست.";
            // what the original document was about
            int? workOrderId = null; int? itemId = null; string serial = null;
            if (item.SourceKind == "QcRecord")
            {
                var o = await _uow.Set<QcRecord>().Where(x => x.Id == item.SourceId).Select(x => new { x.WorkOrderId, x.ItemId, x.SerialNumber }).FirstOrDefaultAsync(cancellationToken);
                workOrderId = o?.WorkOrderId; itemId = o?.ItemId; serial = o?.SerialNumber;
            }
            else if (item.SourceKind == "TestReport")
            {
                var o = await _uow.Set<TestReport>().Where(x => x.Id == item.SourceId).Select(x => new { x.WorkOrderId, x.ItemId, x.SerialNumber }).FirstOrDefaultAsync(cancellationToken);
                workOrderId = o?.WorkOrderId; itemId = o?.ItemId; serial = o?.SerialNumber;
            }
            else
            {
                itemId = await _uow.Set<QcInspection>().Where(x => x.Id == item.SourceId).Select(x => (int?)x.ItemId).FirstOrDefaultAsync(cancellationToken);
            }
            DateTime since = item.Case.ToDate.Date;
            DateTimeOffset created = item.Case.CreateDate;
            switch (kind)
            {
                case NcrReinspection.Record:
                    {
                        QcRecord r = await _uow.Set<QcRecord>().FirstOrDefaultAsync(x => x.Id == recordId && !x.Deleted, cancellationToken);
                        if (r == null) return "گزارش بازرسی مجدد پیدا نشد.";
                        if (r.Result != QcResult.Accept) return "بازرسی مجدد باید با نتیجه «قبول» ثبت شده باشد.";
                        if (r.RecordDate.Date < since || r.CreateDate < created) return "بازرسی مجدد باید بعد از کالیبراسیونی انجام شده باشد که تجهیز را خارج از تلرانس نشان داد.";
                        return Matches(workOrderId, itemId, serial, r.WorkOrderId, r.ItemId, r.SerialNumber);
                    }
                case NcrReinspection.Report:
                    {
                        TestReport r = await _uow.Set<TestReport>().FirstOrDefaultAsync(x => x.Id == recordId && !x.Deleted, cancellationToken);
                        if (r == null) return "گزارش آزمون مجدد پیدا نشد.";
                        if (r.Status != TestReportStatus.Approved || r.Result != ReportResult.Pass) return "آزمون مجدد باید تاییدشده و با نتیجه «قبول» باشد.";
                        if (r.ReportDate.Date < since || r.CreateDate < created) return "آزمون مجدد باید بعد از کالیبراسیونی انجام شده باشد که تجهیز را خارج از تلرانس نشان داد.";
                        return Matches(workOrderId, itemId, serial, r.WorkOrderId, r.ItemId, r.SerialNumber);
                    }
                case NcrReinspection.Inspection:
                    {
                        QcInspection r = await _uow.Set<QcInspection>().FirstOrDefaultAsync(x => x.Id == recordId && !x.Deleted, cancellationToken);
                        if (r == null) return "بازرسی مجدد پیدا نشد.";
                        if (r.Status != InspectionStatus.Done || r.AcceptedQuantity <= 0) return "بازرسی مجدد هنوز با نتیجه «قبول» ثبت نشده است.";
                        if (r.InspectedAt.HasValue && r.InspectedAt.Value < created) return "بازرسی مجدد باید بعد از کالیبراسیونی انجام شده باشد که تجهیز را خارج از تلرانس نشان داد.";
                        return Matches(null, itemId, null, null, r.ItemId, null);
                    }
                default:
                    return "نوع بازرسی مجدد نامعتبر است (گزارش بازرسی، گزارش آزمون یا بازرسی ورودی).";
            }
        }

        /// <summary>The new document must be about the same product: the same work order, item and serial number where the original names them.</summary>
        private static string Matches(int? workOrderId, int? itemId, string serial, int? newWorkOrderId, int? newItemId, string newSerial)
        {
            if (workOrderId.HasValue && newWorkOrderId != workOrderId) return "بازرسی مجدد مربوط به دستور کار سند اصلی نیست.";
            if (itemId.HasValue && newItemId != itemId) return "بازرسی مجدد مربوط به کالای سند اصلی نیست.";
            if (!string.IsNullOrWhiteSpace(serial) && !string.Equals(serial.Trim(), newSerial?.Trim(), StringComparison.OrdinalIgnoreCase))
                return "سریال بازرسی مجدد با سریال سند اصلی نمی‌خواند.";
            return null;
        }

        public async Task<ResponseDto<OutOfToleranceDto>> CloseCaseAsync(CloseOutOfToleranceDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<OutOfToleranceDto>(QualityText.InvalidData);
            if (!_user.IsInRole(QualityRoles.QualityManagers)) return Bad<OutOfToleranceDto>(QualityText.NotAllowed);
            OutOfToleranceCase c = await _uow.Set<OutOfToleranceCase>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (c == null) return QualityText.NotFound<OutOfToleranceDto>("پرونده خارج از تلرانس پیدا نشد.");
            if (c.Status == OotStatus.Closed) return Bad<OutOfToleranceDto>("پرونده قبلاً بسته شده است.");
            int pending = c.Items.Count(i => i.Assessment == OotAssessment.Pending);
            if (pending > 0) return Bad<OutOfToleranceDto>($"برای {pending} سند هنوز ارزیابی اثر (بدون اثر / بازرسی مجدد / عدم انطباق) ثبت نشده است.");
            int waitingReinspection = c.Items.Count(i => i.Assessment == OotAssessment.Reinspect && !i.ReinspectionRecordId.HasValue);
            if (waitingReinspection > 0) return Bad<OutOfToleranceDto>($"برای {waitingReinspection} سند با ارزیابی «بازرسی مجدد» هنوز بازرسی مجدد قبول‌شده‌ای پیوند نشده است؛ ابتدا بازرسی مجدد را انجام و ثبت کنید.");
            c.Status = OotStatus.Closed; c.ClosedAt = DateTimeOffset.Now; c.ClosedByUserId = Me; c.Note = Clean(dto.Note) ?? c.Note;
            c.ModifyDate = DateTimeOffset.Now; c.ModifierUserId = Me;
            // an instrument held out of service only because of this case may now return once it has a valid calibration
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetCaseAsync(c.Id, cancellationToken);
        }
    }
}
