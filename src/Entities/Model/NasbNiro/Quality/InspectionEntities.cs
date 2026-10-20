using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;

namespace Entities.Model.NasbNiro.Quality
{
    // Inspection records (PR-QC-003, WI-QC-003/009/011/012) and test reports (spec 5.4).

    public class QcRecord : BaseEntity
    {
        public string Number { get; set; }
        public QcRecordType Type { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public int? ItpActivityId { get; set; }
        public ItpActivity ItpActivity { get; set; }

        public int? ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        public string SerialNumber { get; set; }
        public decimal Quantity { get; set; }

        /// <summary>Operation or stage of the process route.</summary>
        public string Operation { get; set; }

        public string DrawingNumber { get; set; }
        public string DrawingRevision { get; set; }

        public int InspectorUserId { get; set; }

        /// <summary>The operator checked his own work (allowed only for activities that are not on the ITP).</summary>
        public bool SelfInspection { get; set; }

        public DateTime RecordDate { get; set; }
        public QcResult Result { get; set; }

        public int? NcrId { get; set; }
        public NonConformance Ncr { get; set; }

        /// <summary>Photo or scanned form, uploaded through api/v1/Documents.</summary>
        public Guid? AttachmentDocumentId { get; set; }

        public string Note { get; set; }

        public ICollection<QcMeasurement> Measurements { get; set; }
    }

    public class QcMeasurement
    {
        public int Id { get; set; }

        public int RecordId { get; set; }
        public QcRecord Record { get; set; }

        public string Characteristic { get; set; }
        public decimal Nominal { get; set; }

        /// <summary>Upper deviation allowed (positive number).</summary>
        public decimal TolPlus { get; set; }

        /// <summary>Lower deviation allowed (positive number).</summary>
        public decimal TolMinus { get; set; }

        public decimal Measured { get; set; }
        public string Unit { get; set; }

        public int? EquipmentId { get; set; }
        public MeasuringEquipment Equipment { get; set; }

        /// <summary>Computed by the server: nominal - TolMinus &lt;= measured &lt;= nominal + TolPlus.</summary>
        public bool Pass { get; set; }
    }

    public class TestReport : BaseEntity
    {
        public string Number { get; set; }
        public TestReportType Type { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public int? ItpActivityId { get; set; }
        public ItpActivity ItpActivity { get; set; }

        public int? ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public string SerialNumber { get; set; }

        public string ProcedureRef { get; set; }
        public string ProcedureRevision { get; set; }

        /// <summary>Code and edition (ASME V 2013, ISO 21940-11...).</summary>
        public string CodeEdition { get; set; }

        /// <summary>The user who entered the report.</summary>
        public int ExaminerUserId { get; set; }

        public int? ExaminerQualificationId { get; set; }
        public Qualification ExaminerQualification { get; set; }

        public DateTime ReportDate { get; set; }
        public string Environment { get; set; }
        public TestReportStatus Status { get; set; }
        public ReportResult Result { get; set; }

        public DateTimeOffset? SubmittedAt { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public string RejectReason { get; set; }

        /// <summary>NCR opened for the failed values (a report with failed values can only be approved with one linked).</summary>
        public int? NcrId { get; set; }
        public NonConformance Ncr { get; set; }

        public Guid? DocumentId { get; set; }
        public string Note { get; set; }

        public ICollection<TestReportValue> Values { get; set; }
        public ICollection<TestReportIndication> Indications { get; set; }
        public ICollection<TestReportEquipment> Equipment { get; set; }
        public ICollection<TestReportWitness> Witnesses { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class TestReportValue
    {
        public int Id { get; set; }

        public int ReportId { get; set; }
        public TestReport Report { get; set; }

        public string Parameter { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public decimal? Measured { get; set; }

        /// <summary>Computed by the server; null when the value is not measured or has no limit to judge by.</summary>
        public bool? Pass { get; set; }

        public string Source { get; set; }

        /// <summary>The limit is still to be set by engineering.</summary>
        public bool RequiresLimits { get; set; }
    }

    public class TestReportIndication
    {
        public int Id { get; set; }

        public int ReportId { get; set; }
        public TestReport Report { get; set; }

        public string Location { get; set; }
        public string Type { get; set; }
        public string Size { get; set; }
        public string Orientation { get; set; }
        public bool Accepted { get; set; }
    }

    public class TestReportEquipment
    {
        public int Id { get; set; }

        public int ReportId { get; set; }
        public TestReport Report { get; set; }

        public int EquipmentId { get; set; }
        public MeasuringEquipment Equipment { get; set; }
    }

    public class TestReportWitness
    {
        public int Id { get; set; }

        public int ReportId { get; set; }
        public TestReport Report { get; set; }

        public ReleaseParty Party { get; set; }
        public string Name { get; set; }
        public DateTime WitnessDate { get; set; }
    }
}
