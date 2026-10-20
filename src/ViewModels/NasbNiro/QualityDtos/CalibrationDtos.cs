using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.QualityDtos
{
    public class EquipmentListDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public EquipmentType Type { get; set; }
        public string SerialNumber { get; set; }
        public string Location { get; set; }
        public bool RequiresCalibration { get; set; }
        public EquipmentStatus Status { get; set; }
        public int IntervalMonths { get; set; }
        public DateTime? LastCalibrated { get; set; }
        public DateTime? NextDue { get; set; }
        public int? DaysToDue { get; set; }
    }

    public class CalibrationEventDto
    {
        public int Id { get; set; }
        public int EquipmentId { get; set; }
        public string EquipmentCode { get; set; }
        public DateTime EventDate { get; set; }
        public string LabName { get; set; }
        public bool LabAccredited { get; set; }
        public string CertificateNumber { get; set; }
        public Guid? CertificateDocumentId { get; set; }
        public string TraceabilityStatement { get; set; }
        public bool AsFoundPass { get; set; }
        public bool AsLeftPass { get; set; }
        public bool Adjusted { get; set; }
        public int IntervalMonths { get; set; }
        public DateTime? NextDue { get; set; }
        public string Note { get; set; }
        public int? OotCaseId { get; set; }
        public string OotNumber { get; set; }
    }

    public class EquipmentDto : EquipmentListDto
    {
        public string Maker { get; set; }
        public string Model { get; set; }
        public string OwnerUnit { get; set; }
        public string Range { get; set; }
        public string Accuracy { get; set; }
        public string OutOfServiceReason { get; set; }
        public Guid? CorrectionTableDocumentId { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
        public List<CalibrationEventDto> Events { get; set; } = new List<CalibrationEventDto>();
    }

    public class EquipmentQuery : PaginationParams
    {
        public EquipmentType? Type { get; set; }
        public EquipmentStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class DueQuery : PaginationParams
    {
        /// <summary>Instruments overdue or due within this many days (default 30).</summary>
        public int Days { get; set; } = 30;
    }

    public class SaveEquipmentDto
    {
        public int? Id { get; set; }
        public string RowVersion { get; set; }
        public string Name { get; set; }
        public EquipmentType Type { get; set; }
        public string Maker { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string OwnerUnit { get; set; }
        public string Location { get; set; }
        public string Range { get; set; }
        public string Accuracy { get; set; }
        public bool RequiresCalibration { get; set; } = true;
        public int? IntervalMonths { get; set; }
        public Guid? CorrectionTableDocumentId { get; set; }
        public string Note { get; set; }
    }

    public class EquipmentActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
    }

    public class SaveCalibrationEventDto
    {
        public int EquipmentId { get; set; }
        public DateTime? EventDate { get; set; }
        public string LabName { get; set; }
        public bool LabAccredited { get; set; }
        public string CertificateNumber { get; set; }
        public Guid? CertificateDocumentId { get; set; }
        public string TraceabilityStatement { get; set; }
        public bool AsFoundPass { get; set; }
        public bool AsLeftPass { get; set; }
        public bool Adjusted { get; set; }
        public int? IntervalMonths { get; set; }
        public string Note { get; set; }
    }

    public class CalibrationIntervalDto
    {
        public EquipmentType EquipmentType { get; set; }
        public int Months { get; set; }
        public string Source { get; set; }
    }

    public class OutOfToleranceItemDto
    {
        public int Id { get; set; }
        public string SourceKind { get; set; }
        public int SourceId { get; set; }
        public string SourceNumber { get; set; }
        public DateTime SourceDate { get; set; }
        public OotAssessment Assessment { get; set; }
        public string AssessmentNote { get; set; }
        public int? NcrId { get; set; }
        public string NcrNumber { get; set; }
    }

    public class OutOfToleranceListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int EquipmentId { get; set; }
        public string EquipmentCode { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public OotStatus Status { get; set; }
        public int ItemCount { get; set; }
        public int PendingCount { get; set; }
    }

    public class OutOfToleranceDto : OutOfToleranceListDto
    {
        public int CalibrationEventId { get; set; }
        public string Note { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public List<OutOfToleranceItemDto> Items { get; set; } = new List<OutOfToleranceItemDto>();
    }

    public class OutOfToleranceQuery : PaginationParams
    {
        public OotStatus? Status { get; set; }
        public int? EquipmentId { get; set; }
    }

    public class AssessOutOfToleranceDto
    {
        public int ItemId { get; set; }
        public OotAssessment Assessment { get; set; }
        public string Note { get; set; }

        /// <summary>For the NCR assessment: an NCR that already exists; when empty the system drafts one.</summary>
        public int? NcrId { get; set; }
    }

    public class CloseOutOfToleranceDto
    {
        public int Id { get; set; }
        public string Note { get; set; }
    }
}
