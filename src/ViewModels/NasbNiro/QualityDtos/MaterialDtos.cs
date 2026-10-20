using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.QualityDtos
{
    public class MaterialCertificateDto
    {
        public int Id { get; set; }
        public int LotId { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public CertificateType Type { get; set; }
        public string Number { get; set; }
        public string Issuer { get; set; }
        public Guid? DocumentId { get; set; }
        public string Summary { get; set; }
        public CertificateResult Result { get; set; }
        public string CreatorName { get; set; }
        public string VerifiedByName { get; set; }
        public DateTimeOffset? VerifiedAt { get; set; }
        public string VerificationNote { get; set; }
    }

    public class MaterialCertificateQuery : PaginationParams
    {
        public int? LotId { get; set; }
        public int? ItemId { get; set; }
        public CertificateResult? Result { get; set; }
        public string Contains { get; set; }
    }

    public class SaveMaterialCertificateDto
    {
        public int? Id { get; set; }
        public int LotId { get; set; }
        public CertificateType Type { get; set; }
        public string Number { get; set; }
        public string Issuer { get; set; }
        public Guid? DocumentId { get; set; }
        public string Summary { get; set; }
    }

    public class VerifyCertificateDto
    {
        public int Id { get; set; }
        public bool Accept { get; set; }
        public string Note { get; set; }
    }

    public class TraceabilityQuery
    {
        public string Serial { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
    }

    public class TraceReceiptDto
    {
        public string DocumentNumber { get; set; }
        public DateTime DocumentDate { get; set; }
        public string Provider { get; set; }
        public decimal Quantity { get; set; }
    }

    public class TraceLotDto
    {
        public int LotId { get; set; }
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string CertificateType { get; set; }
        public string CertificateNumber { get; set; }
        public string Relation { get; set; }
        public List<MaterialCertificateDto> Certificates { get; set; } = new List<MaterialCertificateDto>();
        public List<TraceReceiptDto> Receipts { get; set; } = new List<TraceReceiptDto>();
    }

    public class TraceNcrDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string Status { get; set; }
        public string Severity { get; set; }
        public string Description { get; set; }
    }

    public class TraceStageDto
    {
        public DateTimeOffset? When { get; set; }
        public string Label { get; set; }
        public string Detail { get; set; }
    }

    public class TraceabilityDto
    {
        public string Serial { get; set; }
        public List<TraceLotDto> Lots { get; set; } = new List<TraceLotDto>();
        public List<TraceNcrDto> Ncrs { get; set; } = new List<TraceNcrDto>();
        public List<TestReportListDto> TestReports { get; set; } = new List<TestReportListDto>();
        public List<WeldLogDto> WeldLogs { get; set; } = new List<WeldLogDto>();
        public List<TraceStageDto> StageHistory { get; set; } = new List<TraceStageDto>();
    }
}
