using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.QualityDtos
{
    public class QualificationDto
    {
        public int Id { get; set; }
        public int PersonId { get; set; }
        public string PersonName { get; set; }
        public string StampId { get; set; }
        public QualificationKind Kind { get; set; }
        public string Method { get; set; }
        public NdtLevel Level { get; set; }
        public QualificationScheme Scheme { get; set; }
        public string Scope { get; set; }
        public string CertificateNumber { get; set; }
        public Guid? CertificateDocumentId { get; set; }
        public DateTime IssuedDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public QualificationState State { get; set; }
        public string StateText { get; set; }
        public DateTime? LastUse { get; set; }

        /// <summary>For welders: the date the qualification lapses unless the welder welds in this process (ASME IX QW-322).</summary>
        public DateTime? LapsesOn { get; set; }
        public bool Revoked { get; set; }
        public string RevokeReason { get; set; }
        public bool Superseded { get; set; }
        public int? RenewsQualificationId { get; set; }
    }

    public class QualifiedPersonDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string PersonnelCode { get; set; }
        public EmployerKind Employer { get; set; }
        public int? ProviderId { get; set; }
        public string StampId { get; set; }
        public int? UserId { get; set; }
        public bool IsActive { get; set; }
        public List<QualificationDto> Qualifications { get; set; } = new List<QualificationDto>();
    }

    public class QualifiedPersonQuery : PaginationParams
    {
        public string Contains { get; set; }
        public EmployerKind? Employer { get; set; }
        public bool? IsActive { get; set; }
    }

    public class SavePersonDto
    {
        public int? Id { get; set; }
        public string Name { get; set; }
        public string PersonnelCode { get; set; }
        public EmployerKind Employer { get; set; } = EmployerKind.Own;
        public int? ProviderId { get; set; }
        public string StampId { get; set; }
        public int? UserId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class QualificationQuery : PaginationParams
    {
        public int? PersonId { get; set; }
        public QualificationKind? Kind { get; set; }
        public string Method { get; set; }
        public QualificationState? State { get; set; }
        public string Contains { get; set; }
    }

    public class ContinuityQuery : PaginationParams
    {
        /// <summary>Welders whose qualification lapses within this many days, or has already lapsed (default 30).</summary>
        public int Days { get; set; } = 30;
    }

    public class SaveQualificationDto
    {
        public int PersonId { get; set; }
        public QualificationKind Kind { get; set; }
        public string Method { get; set; }
        public NdtLevel Level { get; set; }
        public QualificationScheme Scheme { get; set; } = QualificationScheme.AsmeIX;
        public string Scope { get; set; }
        public string CertificateNumber { get; set; }
        public Guid? CertificateDocumentId { get; set; }
        public DateTime IssuedDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        /// <summary>A lapsed or expired qualification is renewed by a new test: the new row replaces the old one.</summary>
        public int? RenewsQualificationId { get; set; }
    }

    public class QualificationActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Reason { get; set; }
    }

    public class PqrDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string CodeEdition { get; set; }
        public string Process { get; set; }
        public Guid? ResultsDocumentId { get; set; }
        public string Note { get; set; }
        public bool Approved { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ApprovedByName { get; set; }
    }

    public class SavePqrDto
    {
        public int? Id { get; set; }
        public string Number { get; set; }
        public string CodeEdition { get; set; }
        public string Process { get; set; }
        public Guid? ResultsDocumentId { get; set; }
        public string Note { get; set; }
    }

    public class WpsListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string Revision { get; set; }
        public string Process { get; set; }
        public string BasePNo { get; set; }
        public WpsStatus Status { get; set; }
        public int PqrCount { get; set; }
    }

    public class WpsDto : WpsListDto
    {
        public string FillerFNo { get; set; }
        public string FillerANo { get; set; }
        public decimal? ThicknessMin { get; set; }
        public decimal? ThicknessMax { get; set; }
        public string Positions { get; set; }
        public decimal? PreheatMinC { get; set; }
        public decimal? InterpassMaxC { get; set; }
        public string Pwht { get; set; }
        public string ShieldingGas { get; set; }
        public string Note { get; set; }
        public DateTimeOffset? QcApprovedAt { get; set; }
        public string QcApprovedByName { get; set; }
        public DateTimeOffset? EngineeringApprovedAt { get; set; }
        public string EngineeringApprovedByName { get; set; }
        public List<PqrDto> Pqrs { get; set; } = new List<PqrDto>();
    }

    public class WpsQuery : PaginationParams
    {
        public WpsStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class SaveWpsDto
    {
        public int? Id { get; set; }
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
        public string Note { get; set; }
        public List<int> PqrIds { get; set; } = new List<int>();
    }

    public class WpsActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
    }

    public class PqrActionDto
    {
        public int Id { get; set; }
        public string Action { get; set; }
    }

    public class WeldLogDto
    {
        public int Id { get; set; }
        public DateTime WeldDate { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int? ItemId { get; set; }
        public string SerialNumber { get; set; }
        public string JointNo { get; set; }
        public int WpsId { get; set; }
        public string WpsNumber { get; set; }
        public int QualificationId { get; set; }
        public string WelderName { get; set; }
        public string WelderStamp { get; set; }
        public string Process { get; set; }
        public string FillerBatch { get; set; }
        public decimal? PreheatC { get; set; }
        public decimal? InterpassC { get; set; }
        public VtResult Vt { get; set; }
        public string Note { get; set; }
        public List<int> ReportIds { get; set; } = new List<int>();
        public List<string> ReportNumbers { get; set; } = new List<string>();
    }

    public class WeldLogQuery : PaginationParams
    {
        public int? WorkOrderId { get; set; }
        public int? PersonId { get; set; }
        public string SerialNumber { get; set; }
    }

    public class SaveWeldLogDto
    {
        public DateTime? WeldDate { get; set; }
        public int WorkOrderId { get; set; }
        public int? ItemId { get; set; }
        public string SerialNumber { get; set; }
        public string JointNo { get; set; }
        public int WpsId { get; set; }
        public int QualificationId { get; set; }
        public string FillerBatch { get; set; }
        public decimal? PreheatC { get; set; }
        public decimal? InterpassC { get; set; }
        public VtResult Vt { get; set; }
        public string Note { get; set; }
        public List<int> ReportIds { get; set; } = new List<int>();
    }
}
