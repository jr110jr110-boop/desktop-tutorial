using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Quality
{
    // Calibration (WI-QC-001, ISO 7.1.5, ISO 10012). Form codes follow the body of WI-QC-001: FO-QC-007..011 (the appendix prints FO-QA-007..011).

    public class MeasuringEquipment : BaseEntity
    {
        /// <summary>INN-IST-nnn, given by the system.</summary>
        public string Code { get; set; }

        public string Name { get; set; }
        public EquipmentType Type { get; set; }
        public string Maker { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string OwnerUnit { get; set; }
        public string Location { get; set; }
        public string Range { get; set; }
        public string Accuracy { get; set; }

        /// <summary>False: labelled NC (no calibration needed).</summary>
        public bool RequiresCalibration { get; set; } = true;

        public EquipmentStatus Status { get; set; }
        public string OutOfServiceReason { get; set; }
        public int IntervalMonths { get; set; }
        public DateTime? LastCalibrated { get; set; }
        public DateTime? NextDue { get; set; }
        public Guid? CorrectionTableDocumentId { get; set; }
        public string Note { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>Calibration interval per equipment class (configurable, ★ provisional company decision).</summary>
    public class CalibrationIntervalDefault
    {
        public int Id { get; set; }
        public EquipmentType EquipmentType { get; set; }
        public int Months { get; set; }
        public string Source { get; set; }
    }

    public class CalibrationEvent : BaseEntity
    {
        public int EquipmentId { get; set; }
        public MeasuringEquipment Equipment { get; set; }

        public DateTime EventDate { get; set; }
        public string LabName { get; set; }

        /// <summary>The laboratory is accredited to ISO/IEC 17025.</summary>
        public bool LabAccredited { get; set; }

        public string CertificateNumber { get; set; }
        public Guid? CertificateDocumentId { get; set; }

        /// <summary>Traceability of the standards to national or international standards (required).</summary>
        public string TraceabilityStatement { get; set; }

        public bool AsFoundPass { get; set; }
        public bool AsLeftPass { get; set; }
        public bool Adjusted { get; set; }
        public int IntervalMonths { get; set; }
        public DateTime? NextDue { get; set; }
        public string Note { get; set; }
    }

    /// <summary>
    /// Opened automatically when a calibration finds the instrument out of tolerance: lists every record made with it
    /// since its previous good calibration; each needs an assessment before the case is closed (ISO 7.1.5.2).
    /// </summary>
    public class OutOfToleranceCase : BaseEntity
    {
        public string Number { get; set; }

        public int EquipmentId { get; set; }
        public MeasuringEquipment Equipment { get; set; }

        public int CalibrationEventId { get; set; }
        public CalibrationEvent CalibrationEvent { get; set; }

        /// <summary>Records from this date are affected (date of the previous good calibration; empty = all records).</summary>
        public DateTime? FromDate { get; set; }

        public DateTime ToDate { get; set; }
        public OotStatus Status { get; set; }
        public string Note { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public int? ClosedByUserId { get; set; }

        public ICollection<OutOfToleranceItem> Items { get; set; }
    }

    public class OutOfToleranceItem
    {
        public int Id { get; set; }

        public int CaseId { get; set; }
        public OutOfToleranceCase Case { get; set; }

        /// <summary>"QcRecord" or "TestReport".</summary>
        public string SourceKind { get; set; }

        public int SourceId { get; set; }
        public string SourceNumber { get; set; }
        public DateTime SourceDate { get; set; }

        public OotAssessment Assessment { get; set; }
        public string AssessmentNote { get; set; }
        public int? NcrId { get; set; }
        public DateTimeOffset? AssessedAt { get; set; }
        public int? AssessedByUserId { get; set; }
    }
}
