using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Providers;

namespace Entities.Model.NasbNiro.Quality
{
    // Qualified personnel and welding (WI-QC-010, spec gaps G-01, G-03, G-17; ASME IX QW-200/300/322).

    public class QualifiedPerson : BaseEntity
    {
        public string Name { get; set; }
        public string PersonnelCode { get; set; }
        public EmployerKind Employer { get; set; }

        /// <summary>Contractor or provider of a person that is not an employee.</summary>
        public int? ProviderId { get; set; }
        public Provider Provider { get; set; }

        /// <summary>Welder stamp or ID.</summary>
        public string StampId { get; set; }

        public int? UserId { get; set; }
        public bool IsActive { get; set; } = true;

        public ICollection<Qualification> Qualifications { get; set; }
    }

    public class Qualification : BaseEntity
    {
        public int PersonId { get; set; }
        public QualifiedPerson Person { get; set; }

        public QualificationKind Kind { get; set; }

        /// <summary>Welding process (SMAW, GTAW...) or NDT method (PT, MT, UT, RT, VT), upper case.</summary>
        public string Method { get; set; }

        public NdtLevel Level { get; set; }
        public QualificationScheme Scheme { get; set; }

        /// <summary>Range of qualification: P-no, F-no, thickness, positions.</summary>
        public string Scope { get; set; }

        public string CertificateNumber { get; set; }
        public Guid? CertificateDocumentId { get; set; }
        public DateTime IssuedDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        public bool Revoked { get; set; }
        public string RevokeReason { get; set; }

        /// <summary>A newer qualification (new test after the old one lapsed) replaced this one.</summary>
        public bool Superseded { get; set; }

        public int? RenewsQualificationId { get; set; }
    }

    public class Wps : BaseEntity
    {
        public string Number { get; set; }
        public string Revision { get; set; }
        public string Process { get; set; }
        public string BasePNo { get; set; }
        public string FillerFNo { get; set; }
        public string FillerANo { get; set; }
        public decimal? ThicknessMin { get; set; }
        public decimal? ThicknessMax { get; set; }
        public string Positions { get; set; }
        public decimal? PreheatMinC { get; set; }
        public decimal? InterpassMaxC { get; set; }
        public string Pwht { get; set; }
        public string ShieldingGas { get; set; }
        public WpsStatus Status { get; set; }
        public string Note { get; set; }

        public DateTimeOffset? QcApprovedAt { get; set; }
        public int? QcApprovedByUserId { get; set; }
        public DateTimeOffset? EngineeringApprovedAt { get; set; }
        public int? EngineeringApprovedByUserId { get; set; }

        public ICollection<WpsPqr> Pqrs { get; set; }
    }

    public class Pqr : BaseEntity
    {
        public string Number { get; set; }
        public string CodeEdition { get; set; }
        public string Process { get; set; }
        public Guid? ResultsDocumentId { get; set; }
        public string Note { get; set; }

        public bool Approved { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
    }

    public class WpsPqr
    {
        public int Id { get; set; }

        public int WpsId { get; set; }
        public Wps Wps { get; set; }

        public int PqrId { get; set; }
        public Pqr Pqr { get; set; }
    }

    public class WeldLog : BaseEntity
    {
        public DateTime WeldDate { get; set; }

        public int WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public int? ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public string SerialNumber { get; set; }
        public string JointNo { get; set; }

        public int WpsId { get; set; }
        public Wps Wps { get; set; }

        public int QualificationId { get; set; }
        public Qualification Qualification { get; set; }

        public string FillerBatch { get; set; }
        public decimal? PreheatC { get; set; }
        public decimal? InterpassC { get; set; }
        public VtResult Vt { get; set; }
        public string Note { get; set; }

        public ICollection<WeldLogReport> Reports { get; set; }
    }

    public class WeldLogReport
    {
        public int Id { get; set; }

        public int WeldLogId { get; set; }
        public WeldLog WeldLog { get; set; }

        public int TestReportId { get; set; }
        public TestReport TestReport { get; set; }
    }
}
