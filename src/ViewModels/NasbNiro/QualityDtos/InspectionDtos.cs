using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.QualityDtos
{
    // ------------------------------------------------------------------ inspection records

    public class QcMeasurementDto
    {
        public int Id { get; set; }
        public string Characteristic { get; set; }
        public decimal Nominal { get; set; }
        public decimal TolPlus { get; set; }
        public decimal TolMinus { get; set; }
        public decimal Measured { get; set; }
        public string Unit { get; set; }
        public int? EquipmentId { get; set; }
        public string EquipmentCode { get; set; }
        public bool Pass { get; set; }
    }

    public class QcRecordListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public QcRecordType Type { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int? ItpActivityId { get; set; }
        public string ItemCode { get; set; }
        public string SerialNumber { get; set; }
        public DateTime RecordDate { get; set; }
        public QcResult Result { get; set; }
        public string InspectorName { get; set; }
        public bool SelfInspection { get; set; }
        public int? NcrId { get; set; }
        public string NcrNumber { get; set; }
    }

    public class QcRecordDto : QcRecordListDto
    {
        public int? ItemId { get; set; }
        public string ItemTitle { get; set; }
        public int? LotId { get; set; }
        public decimal Quantity { get; set; }
        public string Operation { get; set; }
        public string DrawingNumber { get; set; }
        public string DrawingRevision { get; set; }
        public Guid? AttachmentDocumentId { get; set; }
        public string Note { get; set; }
        public List<QcMeasurementDto> Measurements { get; set; } = new List<QcMeasurementDto>();
    }

    public class QcRecordQuery : PaginationParams
    {
        public int? WorkOrderId { get; set; }
        public int? ItpActivityId { get; set; }
        public QcResult? Result { get; set; }
        public QcRecordType? Type { get; set; }
        public string Contains { get; set; }
    }

    public class SaveQcMeasurementDto
    {
        public string Characteristic { get; set; }
        public decimal Nominal { get; set; }
        public decimal TolPlus { get; set; }
        public decimal TolMinus { get; set; }
        public decimal Measured { get; set; }
        public string Unit { get; set; }
        public int? EquipmentId { get; set; }
    }

    public class SaveQcRecordDto
    {
        public QcRecordType Type { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ItpActivityId { get; set; }
        public int? ItemId { get; set; }
        public int? LotId { get; set; }
        public string SerialNumber { get; set; }
        public decimal Quantity { get; set; }
        public string Operation { get; set; }
        public string DrawingNumber { get; set; }
        public string DrawingRevision { get; set; }
        public DateTime? RecordDate { get; set; }
        public QcResult Result { get; set; }
        public Guid? AttachmentDocumentId { get; set; }
        public string Note { get; set; }
        public List<SaveQcMeasurementDto> Measurements { get; set; } = new List<SaveQcMeasurementDto>();
    }

    // ------------------------------------------------------------------ test reports

    public class TestReportValueDto
    {
        public int Id { get; set; }
        public string Parameter { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public decimal? Measured { get; set; }
        public bool? Pass { get; set; }
        public string Source { get; set; }
        public bool RequiresLimits { get; set; }
    }

    public class TestReportIndicationDto
    {
        public int Id { get; set; }
        public string Location { get; set; }
        public string Type { get; set; }
        public string Size { get; set; }
        public string Orientation { get; set; }
        public bool Accepted { get; set; }
    }

    public class TestReportEquipmentDto
    {
        public int EquipmentId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public DateTime? NextDue { get; set; }
    }

    public class TestReportWitnessDto
    {
        public ReleaseParty Party { get; set; }
        public string Name { get; set; }
        public DateTime WitnessDate { get; set; }
    }

    public class TestReportListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public TestReportType Type { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int? ItpActivityId { get; set; }
        public DateTime ReportDate { get; set; }
        public TestReportStatus Status { get; set; }
        public ReportResult Result { get; set; }
        public string ExaminerName { get; set; }
        public int? NcrId { get; set; }
        public string NcrNumber { get; set; }
    }

    public class TestReportDto : TestReportListDto
    {
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string SerialNumber { get; set; }
        public string ProcedureRef { get; set; }
        public string ProcedureRevision { get; set; }
        public string CodeEdition { get; set; }
        public int ExaminerUserId { get; set; }
        public int? ExaminerQualificationId { get; set; }
        public string ExaminerQualificationNumber { get; set; }
        public string Environment { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApprovedByName { get; set; }
        public string RejectReason { get; set; }
        public Guid? DocumentId { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
        public List<string> Problems { get; set; } = new List<string>();
        public List<TestReportValueDto> Values { get; set; } = new List<TestReportValueDto>();
        public List<TestReportIndicationDto> Indications { get; set; } = new List<TestReportIndicationDto>();
        public List<TestReportEquipmentDto> Equipment { get; set; } = new List<TestReportEquipmentDto>();
        public List<TestReportWitnessDto> Witnesses { get; set; } = new List<TestReportWitnessDto>();
    }

    public class TestReportQuery : PaginationParams
    {
        public int? WorkOrderId { get; set; }
        public int? ItpActivityId { get; set; }
        public TestReportType? Type { get; set; }
        public TestReportStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class SaveTestReportValueDto
    {
        public string Parameter { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public decimal? Measured { get; set; }
    }

    public class SaveTestReportDto
    {
        public int? Id { get; set; }
        public string RowVersion { get; set; }
        public TestReportType Type { get; set; }
        public int? WorkOrderId { get; set; }
        public int? ItpActivityId { get; set; }
        public int? ItemId { get; set; }
        public string SerialNumber { get; set; }
        public string ProcedureRef { get; set; }
        public string ProcedureRevision { get; set; }
        public string CodeEdition { get; set; }
        public int? ExaminerQualificationId { get; set; }
        public DateTime? ReportDate { get; set; }
        public string Environment { get; set; }
        public Guid? DocumentId { get; set; }
        public string Note { get; set; }
        public List<SaveTestReportValueDto> Values { get; set; } = new List<SaveTestReportValueDto>();
        public List<TestReportIndicationDto> Indications { get; set; } = new List<TestReportIndicationDto>();
        public List<int> EquipmentIds { get; set; } = new List<int>();
        public List<TestReportWitnessDto> Witnesses { get; set; } = new List<TestReportWitnessDto>();
    }

    public class TestReportActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
    }

    public class StandardParameterDto
    {
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string Source { get; set; }
        public bool Mandatory { get; set; }
        public bool RequiresLimits { get; set; }
    }
}
