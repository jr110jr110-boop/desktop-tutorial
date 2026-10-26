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
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    public interface IQualityDashboardService
    {
        Task<ResponseDto<QualityDashboardDto>> GetDashboardAsync(CancellationToken cancellationToken);

        /// <summary>The counts and alerts the CEO dashboard shows (computed, read-only).</summary>
        Task<QualityCountsDto> GetCountsAsync(CancellationToken cancellationToken);

        Task<ResponseDto<List<QualitySettingDto>>> GetSettingsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<List<QualitySettingDto>>> SaveSettingAsync(SaveQualitySettingDto dto, CancellationToken cancellationToken);
    }

    /// <summary>Deadlines and alerts of the quality module, computed from the records (no manual lists).</summary>
    public class QualityDashboardService : IQualityDashboardService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPersonnelService _personnel;

        public QualityDashboardService(IUnitOfWork uow, IInventoryUserContext user, IPersonnelService personnel)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _personnel = personnel ?? throw new ArgumentNullException(nameof(personnel));
        }

        // -------------------------------------------------------------------------------- settings

        public async Task<ResponseDto<List<QualitySettingDto>>> GetSettingsAsync(CancellationToken cancellationToken)
        {
            List<QualitySetting> stored = await _uow.Set<QualitySetting>().AsNoTracking().ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(QualitySettingKeys.Defaults().Select(d =>
            {
                QualitySetting row = stored.FirstOrDefault(s => s.Key == d.Key);
                QualitySettingRules.TryGetBounds(d.Key, out int min, out int max);
                return new QualitySettingDto { Key = d.Key, Title = d.Title, Value = row?.Value ?? d.Value, Source = d.Source, IsDefault = row == null, Min = min, Max = max };
            }).ToList());
        }

        public async Task<ResponseDto<List<QualitySettingDto>>> SaveSettingAsync(SaveQualitySettingDto dto, CancellationToken cancellationToken)
        {
            QualitySetting def = dto == null ? null : QualitySettingKeys.Defaults().FirstOrDefault(d => d.Key == dto.Key);
            if (def == null) return QualityText.Bad<List<QualitySettingDto>>("تنظیم نامعتبر است.");
            if (!_user.IsInRole(QualityRoles.Audit + ",CEO")) return QualityText.Bad<List<QualitySettingDto>>(QualityText.NotAllowed);
            // R1q D30: every setting has its own range (welder continuity 1..12 months, a committee of at least two, whole numbers only)
            string rangeError = QualitySettingRules.Check(def.Key, def.Title, dto.Value);
            if (rangeError != null) return QualityText.Bad<List<QualitySettingDto>>(rangeError);
            QualitySetting row = await _uow.Set<QualitySetting>().FirstOrDefaultAsync(s => s.Key == dto.Key, cancellationToken);
            if (row == null)
            {
                row = new QualitySetting { Key = def.Key, Title = def.Title };
                _uow.Set<QualitySetting>().Add(row);
            }
            row.Value = dto.Value; row.Source = "تعیین‌شده توسط شرکت";
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetSettingsAsync(cancellationToken);
        }

        // -------------------------------------------------------------------------------- counts

        private sealed class PointRow
        {
            public int WorkOrderId { get; set; }
            public string WorkOrderCode { get; set; }
            public int NoticeDays { get; set; }
            public DateTime? PlannedDate { get; set; }
            public DateTime? NotificationSentAt { get; set; }
            public bool HasH { get; set; }
            public bool HasW { get; set; }
        }

        private async Task<List<PointRow>> PointsAsync(CancellationToken cancellationToken) =>
            await _uow.Set<ItpActivity>().Where(a => !a.Itp.Deleted && (a.Itp.Status == ItpStatus.ClientApproved || a.Itp.Status == ItpStatus.InUse) &&
                    (a.Status == ItpActivityStatus.Planned || a.Status == ItpActivityStatus.Notified) &&
                    (a.PointVendor == ItpPoint.H || a.PointTpi == ItpPoint.H || a.PointClient == ItpPoint.H || a.PointVendor == ItpPoint.W || a.PointTpi == ItpPoint.W || a.PointClient == ItpPoint.W))
                .Select(a => new PointRow
                {
                    WorkOrderId = a.Itp.WorkOrderId, WorkOrderCode = a.Itp.WorkOrder.Code, NoticeDays = a.Itp.NoticeDays, PlannedDate = a.PlannedDate, NotificationSentAt = a.NotificationSentAt,
                    HasH = a.PointVendor == ItpPoint.H || a.PointTpi == ItpPoint.H || a.PointClient == ItpPoint.H,
                    HasW = a.PointVendor == ItpPoint.W || a.PointTpi == ItpPoint.W || a.PointClient == ItpPoint.W,
                }).ToListAsync(cancellationToken);

        /// <summary>A witness notice is due when it was sent and its period ends within three days, or it is not sent but should have gone by the planned date.</summary>
        private static bool NoticeDue(PointRow p, DateTime today)
        {
            if (!p.HasW) return false;
            if (p.NotificationSentAt.HasValue) return p.NotificationSentAt.Value.Date.AddDays(p.NoticeDays) <= today.AddDays(3);
            return p.PlannedDate.HasValue && p.PlannedDate.Value.Date.AddDays(-p.NoticeDays) <= today;
        }

        public async Task<QualityCountsDto> GetCountsAsync(CancellationToken cancellationToken)
        {
            QualityCountsDto c = new QualityCountsDto();
            await FillCountsAsync(c, cancellationToken);
            return c;
        }

        private async Task FillCountsAsync(QualityCountsDto c, CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
            int warn = (int)settings[QualitySettingKeys.CalibrationWarnDays];
            DateTime soon = today.AddDays(warn);

            c.OverdueCapas = await _uow.Set<Capa>().CountAsync(x => !x.Deleted &&
                (((x.Status == CapaStatus.Open || x.Status == CapaStatus.ActionsDefined) && x.DueDate < today) || (x.Status == CapaStatus.Implemented && x.EffectivenessDueDate < today)), cancellationToken);
            IQueryable<MeasuringEquipment> tracked = _uow.Set<MeasuringEquipment>().Where(e => !e.Deleted && e.RequiresCalibration && e.Status != EquipmentStatus.OutOfService);
            c.CalibrationOverdue = await tracked.CountAsync(e => e.NextDue == null || e.NextDue < today, cancellationToken);
            c.CalibrationDue30 = await tracked.CountAsync(e => e.NextDue >= today && e.NextDue <= soon, cancellationToken);

            List<PointRow> points = await PointsAsync(cancellationToken);
            c.PendingHPoints = points.Count(p => p.HasH);
            c.WitnessNoticesDue = points.Count(p => NoticeDue(p, today));

            int continuityWarn = (int)settings[QualitySettingKeys.ContinuityWarnDays];
            c.QualificationsLapsing = (await _personnel.GetContinuityAsync(new ContinuityQuery { Days = continuityWarn }, cancellationToken)).Data?.TotalCount ?? 0;
            c.ComplaintsOpen = await _uow.Set<CustomerComplaint>().CountAsync(x => !x.Deleted && x.Status != ComplaintStatus.Closed, cancellationToken);
            c.ComplaintsOverdue = await _uow.Set<CustomerComplaint>().CountAsync(x => !x.Deleted && x.Status != ComplaintStatus.Responded && x.Status != ComplaintStatus.Closed && x.ResponseDue < today, cancellationToken);
            c.OverdueNcrs = await _uow.Set<NonConformance>().CountAsync(n => !n.Deleted && n.Status == NcrStatus.Open && n.DueDate < today, cancellationToken);
            c.ReleaseNotesPending = await _uow.Set<ReleaseNote>().CountAsync(r => !r.Deleted && r.Status == ReleaseNoteStatus.Draft, cancellationToken);

            void Alert(int count, string level, string code, string text, string link)
            {
                if (count > 0) c.Alerts.Add(new QualityAlertDto { Level = level, Code = code, Text = string.Format(text, count), Count = count, Link = link });
            }
            Alert(c.OverdueCapas, "danger", "CAPA_OVERDUE", "{0} اقدام اصلاحی (CAPA) از موعد گذشته است", "/quality/capa");
            Alert(c.CalibrationOverdue, "danger", "CALIBRATION_OVERDUE", "{0} تجهیز اندازه‌گیری کالیبراسیون سررسیدگذشته دارد", "/quality/calibration");
            Alert(c.CalibrationDue30, "warning", "CALIBRATION_DUE", "{0} تجهیز اندازه‌گیری تا " + warn + " روز آینده باید کالیبره شود", "/quality/calibration");
            Alert(c.PendingHPoints, "warning", "H_POINTS", "{0} نقطه توقف (H) در برنامه‌های بازرسی تاییدشده در انتظار رهاسازی است", "/quality/itp");
            Alert(c.WitnessNoticesDue, "warning", "WITNESS_NOTICE", "{0} نقطه شاهد (W) مهلت اعلام به کارفرما/بازرس دارد", "/quality/itp");
            Alert(c.QualificationsLapsing, "warning", "QUALIFICATION", "{0} گواهی جوشکار در حال قطع تداوم (ASME IX) یا قطع‌شده است", "/quality/personnel");
            Alert(c.ComplaintsOverdue, "danger", "COMPLAINT_OVERDUE", "{0} شکایت مشتری از مهلت پاسخ گذشته است", "/quality/complaints");
            Alert(c.OverdueNcrs, "warning", "NCR_OVERDUE", "{0} گزارش عدم انطباق از مهلت تصمیم گذشته است", "/inventory/ncr");
        }

        // -------------------------------------------------------------------------------- dashboard

        public async Task<ResponseDto<QualityDashboardDto>> GetDashboardAsync(CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            var d = new QualityDashboardDto { GeneratedAt = DateTimeOffset.Now };
            await FillCountsAsync(d, cancellationToken);
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, cancellationToken);
            int warn = (int)settings[QualitySettingKeys.CalibrationWarnDays];

            // open NCRs by severity and age
            var ncrs = await _uow.Set<NonConformance>().Where(n => !n.Deleted && n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled)
                .Select(n => new { n.Severity, n.ReportDate, n.DueDate }).ToListAsync(cancellationToken);
            d.Ncrs = new NcrAgeDto
            {
                NotClassified = ncrs.Count(n => n.Severity == NcrSeverity.NotClassified), Minor = ncrs.Count(n => n.Severity == NcrSeverity.Minor), Major = ncrs.Count(n => n.Severity == NcrSeverity.Major),
                Age0To7 = ncrs.Count(n => (today - n.ReportDate.Date).TotalDays <= 7),
                Age8To30 = ncrs.Count(n => (today - n.ReportDate.Date).TotalDays > 7 && (today - n.ReportDate.Date).TotalDays <= 30),
                Age31Plus = ncrs.Count(n => (today - n.ReportDate.Date).TotalDays > 30),
                Overdue = ncrs.Count(n => n.DueDate.HasValue && n.DueDate.Value.Date < today),
            };

            d.CapaOpen = await _uow.Set<Capa>().CountAsync(x => !x.Deleted && x.Status != CapaStatus.Closed && x.Status != CapaStatus.Cancelled, cancellationToken);
            List<Capa> late = await _uow.Set<Capa>().Where(x => !x.Deleted &&
                (((x.Status == CapaStatus.Open || x.Status == CapaStatus.ActionsDefined) && x.DueDate < today) || (x.Status == CapaStatus.Implemented && x.EffectivenessDueDate < today)))
                .OrderBy(x => x.DueDate).Take(10).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, late.Select(x => x.OwnerUserId), cancellationToken);
            d.OverdueCapaList = late.Select(x => new DashboardCapaDto { Id = x.Id, Number = x.Number, Problem = x.Problem, DueDate = x.DueDate, OwnerName = names.Name(x.OwnerUserId), AutoDrafted = x.AutoDrafted }).ToList();

            d.OutOfToleranceOpen = await _uow.Set<OutOfToleranceCase>().CountAsync(x => !x.Deleted && x.Status == OotStatus.Open, cancellationToken);
            List<MeasuringEquipment> due = await _uow.Set<MeasuringEquipment>()
                .Where(e => !e.Deleted && e.RequiresCalibration && e.Status != EquipmentStatus.OutOfService && (e.NextDue == null || e.NextDue <= today.AddDays(warn)))
                .OrderBy(e => e.NextDue).Take(10).ToListAsync(cancellationToken);
            d.CalibrationList = due.Select(e => new DashboardEquipmentDto
            {
                Id = e.Id, Code = e.Code, Name = e.Name, NextDue = e.NextDue, DaysToDue = e.NextDue.HasValue ? (int)(e.NextDue.Value.Date - today).TotalDays : (int?)null,
            }).ToList();

            ResponseDto<ViewModels.NasbNiro.Pagination.PagedList<QualificationDto>> continuity = await _personnel.GetContinuityAsync(
                new ContinuityQuery { Days = (int)settings[QualitySettingKeys.ContinuityWarnDays], PageSize = 10 }, cancellationToken);
            if (continuity.Success && continuity.Data != null)
                foreach (QualificationDto q in continuity.Data)
                d.QualificationList.Add(new DashboardQualificationDto
                {
                    Id = q.Id, PersonName = q.PersonName, Method = q.Method, Kind = q.Kind.ToString(), Date = q.LapsesOn,
                    Reason = q.State == QualificationState.Lapsed ? "تداوم قطع شده" : "در حال قطع تداوم",
                });
            d.QualificationsExpired = await _uow.Set<Qualification>().CountAsync(q => !q.Deleted && !q.Revoked && !q.Superseded && q.ExpiryDate < today && q.Person.IsActive, cancellationToken);

            List<PointRow> points = await PointsAsync(cancellationToken);
            d.PendingPoints = points.GroupBy(p => new { p.WorkOrderId, p.WorkOrderCode }).Select(g => new PendingPointSummaryDto
            {
                WorkOrderId = g.Key.WorkOrderId, WorkOrderCode = g.Key.WorkOrderCode, HPoints = g.Count(p => p.HasH), WPoints = g.Count(p => p.HasW),
                EarliestWitnessDeadline = g.Where(p => p.NotificationSentAt.HasValue).Select(p => (DateTime?)p.NotificationSentAt.Value.Date.AddDays(p.NoticeDays)).Min(),
            }).OrderByDescending(p => p.HPoints).ToList();
            return ResponseDtoHelper.Ok(d);
        }
    }
}
