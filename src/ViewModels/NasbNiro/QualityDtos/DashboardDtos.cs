using System;
using System.Collections.Generic;

namespace ViewModels.NasbNiro.QualityDtos
{
    public class QualityAlertDto
    {
        /// <summary>danger, warning or info.</summary>
        public string Level { get; set; }
        public string Code { get; set; }
        public string Text { get; set; }
        public int Count { get; set; }
        public string Link { get; set; }
    }

    /// <summary>The figures the CEO dashboard adds from the quality module.</summary>
    public class QualityCountsDto
    {
        public int OverdueCapas { get; set; }
        public int CalibrationOverdue { get; set; }
        public int CalibrationDue30 { get; set; }
        public int PendingHPoints { get; set; }
        public int WitnessNoticesDue { get; set; }
        public int QualificationsLapsing { get; set; }
        public int ComplaintsOpen { get; set; }
        public int ComplaintsOverdue { get; set; }
        public int OverdueNcrs { get; set; }
        public int ReleaseNotesPending { get; set; }
        public List<QualityAlertDto> Alerts { get; set; } = new List<QualityAlertDto>();
    }

    public class NcrAgeDto
    {
        public int NotClassified { get; set; }
        public int Minor { get; set; }
        public int Major { get; set; }
        public int Age0To7 { get; set; }
        public int Age8To30 { get; set; }
        public int Age31Plus { get; set; }
        public int Overdue { get; set; }
    }

    public class DashboardCapaDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string Problem { get; set; }
        public DateTime? DueDate { get; set; }
        public string OwnerName { get; set; }
        public bool AutoDrafted { get; set; }
    }

    public class DashboardEquipmentDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public DateTime? NextDue { get; set; }
        public int? DaysToDue { get; set; }
    }

    public class DashboardQualificationDto
    {
        public int Id { get; set; }
        public string PersonName { get; set; }
        public string Method { get; set; }
        public string Kind { get; set; }
        public DateTime? Date { get; set; }
        public string Reason { get; set; }
    }

    public class PendingPointSummaryDto
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int HPoints { get; set; }
        public int WPoints { get; set; }
        public DateTime? EarliestWitnessDeadline { get; set; }
    }

    public class QualityDashboardDto : QualityCountsDto
    {
        public DateTimeOffset GeneratedAt { get; set; }
        public NcrAgeDto Ncrs { get; set; } = new NcrAgeDto();
        public int CapaOpen { get; set; }
        public int OutOfToleranceOpen { get; set; }
        public int QualificationsExpired { get; set; }
        public List<DashboardCapaDto> OverdueCapaList { get; set; } = new List<DashboardCapaDto>();
        public List<DashboardEquipmentDto> CalibrationList { get; set; } = new List<DashboardEquipmentDto>();
        public List<DashboardQualificationDto> QualificationList { get; set; } = new List<DashboardQualificationDto>();
        public List<PendingPointSummaryDto> PendingPoints { get; set; } = new List<PendingPointSummaryDto>();
    }

    public class QualitySettingDto
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public decimal Value { get; set; }
        public string Source { get; set; }
        public bool IsDefault { get; set; }

        /// <summary>The range the value may take (R1q D30); the screen shows it and refuses a value outside it.</summary>
        public int Min { get; set; }

        public int Max { get; set; }
    }

    public class SaveQualitySettingDto
    {
        public string Key { get; set; }
        public decimal Value { get; set; }
    }
}
