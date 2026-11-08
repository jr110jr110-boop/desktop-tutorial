using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.QualityDtos
{
    // Package C: special processes (types, qualifications, runs, supplier approvals), material specifications and the structured certificate.

    // ------------------------------------------------------------------ process types

    public class SpParameterDto
    {
        public int Id { get; set; }
        public int SortOrder { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public bool Required { get; set; } = true;
    }

    public class SpTypeDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool RequiresMachine { get; set; }
        public bool RequiresSurvey { get; set; }
        public EquipmentType? RequiredInstrumentClass { get; set; }
        public int? ProcedureDeliverableId { get; set; }
        public string ProcedureNumber { get; set; }
        public bool ExistingControl { get; set; }
        public bool IsActive { get; set; }
        public List<SpParameterDto> Parameters { get; set; } = new List<SpParameterDto>();
        public string RowVersion { get; set; }
    }

    public class SaveSpTypeDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool RequiresMachine { get; set; } = true;
        public bool RequiresSurvey { get; set; }
        public EquipmentType? RequiredInstrumentClass { get; set; }
        public int? ProcedureDeliverableId { get; set; }
        public bool ExistingControl { get; set; }
        public bool IsActive { get; set; } = true;
        public List<SpParameterDto> Parameters { get; set; } = new List<SpParameterDto>();
        public string RowVersion { get; set; }
    }

    public class SpTypeQuery
    {
        public bool IncludeInactive { get; set; }
    }

    // ------------------------------------------------------------------ qualification

    public class SpQualEquipmentDto
    {
        public int Id { get; set; }
        public SpEquipmentKind Kind { get; set; }
        public int? MachineId { get; set; }
        public string MachineTitle { get; set; }
        public int? EquipmentId { get; set; }
        public string EquipmentCode { get; set; }
        public string EquipmentTitle { get; set; }

        /// <summary>For an instrument: its calibration state today (null = usable), the Persian reason otherwise.</summary>
        public string CalibrationProblem { get; set; }
        public SpSurveyKind SurveyKind { get; set; }
        public DateTime? SurveyDate { get; set; }
        public DateTime? SurveyDueDate { get; set; }
        public Guid? SurveyDocumentId { get; set; }
        public string SurveyNote { get; set; }
        public string SurveyProblem { get; set; }
    }

    public class SpQualOperatorDto
    {
        public int PersonId { get; set; }
        public string PersonName { get; set; }
        public string StampId { get; set; }

        /// <summary>The Persian reason the person has no valid competence record for the process today (null = qualified).</summary>
        public string CompetenceProblem { get; set; }
    }

    public class SpQualDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int TypeId { get; set; }
        public string TypeCode { get; set; }
        public string TypeTitle { get; set; }
        public string Title { get; set; }
        public int ProcedureDeliverableId { get; set; }
        public int ProcedureRevisionId { get; set; }
        public string ProcedureNumber { get; set; }
        public string ProcedureRevisionCode { get; set; }

        /// <summary>The procedure revision is still the one in force in the EDMS.</summary>
        public bool ProcedureInForce { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public int DaysLeft { get; set; }
        public SpQualStatus Status { get; set; }
        public string Note { get; set; }
        public int CreatorUserId { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string RejectReason { get; set; }
        public string VoidReason { get; set; }
        public int? SupersedesId { get; set; }
        public List<SpParameterDto> Limits { get; set; } = new List<SpParameterDto>();
        public List<SpQualEquipmentDto> Equipments { get; set; } = new List<SpQualEquipmentDto>();
        public List<SpQualOperatorDto> Operators { get; set; } = new List<SpQualOperatorDto>();
        public List<string> Actions { get; set; } = new List<string>();
        public string RowVersion { get; set; }
    }

    public class SpQualQuery : PaginationParams
    {
        public int? TypeId { get; set; }
        public SpQualStatus? Status { get; set; }
        public string Contains { get; set; }

        /// <summary>Approved qualifications that end within this many days (and the ones already expired).</summary>
        public int? ExpiringInDays { get; set; }
    }

    public class SaveSpQualEquipmentDto
    {
        public SpEquipmentKind Kind { get; set; }
        public int? MachineId { get; set; }
        public int? EquipmentId { get; set; }
        public SpSurveyKind SurveyKind { get; set; }
        public DateTime? SurveyDate { get; set; }
        public DateTime? SurveyDueDate { get; set; }
        public Guid? SurveyDocumentId { get; set; }
        public string SurveyNote { get; set; }
    }

    public class SaveSpQualDto
    {
        public int? Id { get; set; }
        public int TypeId { get; set; }
        public string Title { get; set; }
        public int ProcedureDeliverableId { get; set; }
        public int ProcedureRevisionId { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public string Note { get; set; }

        /// <summary>A renewal: the qualification this one replaces (it is superseded when this one is approved).</summary>
        public int? SupersedesId { get; set; }
        public List<SpParameterDto> Limits { get; set; } = new List<SpParameterDto>();
        public List<SaveSpQualEquipmentDto> Equipments { get; set; } = new List<SaveSpQualEquipmentDto>();
        public List<int> OperatorPersonIds { get; set; } = new List<int>();
        public string RowVersion { get; set; }
    }

    public class SpActionDto
    {
        public int Id { get; set; }

        /// <summary>Qualification: submit, approve, reject, void. Run: review, reject-review, void. Supplier approval: approve, reject, void. Material specification: submit, approve, return, void.</summary>
        public string Action { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------ run

    public class SpRunValueDto
    {
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal Value { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public bool Pass { get; set; }
    }

    public class SpRunInstrumentDto
    {
        public int EquipmentId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
    }

    public class SpRunDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int TypeId { get; set; }
        public string TypeCode { get; set; }
        public string TypeTitle { get; set; }
        public int? QualificationId { get; set; }
        public string QualificationNumber { get; set; }
        public string ProcedureText { get; set; }
        public bool IsSubcontract { get; set; }
        public int? SupplierId { get; set; }
        public string SupplierName { get; set; }
        public int? ReceiptDocumentId { get; set; }
        public int? OrderId { get; set; }
        public string OrderNumber { get; set; }
        public int? OperationId { get; set; }
        public string OperationText { get; set; }
        public int? PartOperationId { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
        public string SerialNumbers { get; set; }
        public decimal Quantity { get; set; }
        public int? OperatorPersonId { get; set; }
        public string OperatorName { get; set; }
        public string OperatorStamp { get; set; }
        public int? MachineId { get; set; }
        public string MachineTitle { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }
        public Guid? ChartDocumentId { get; set; }
        public string ChartFileName { get; set; }
        public Guid? CertificateDocumentId { get; set; }
        public string CertificateNumber { get; set; }
        public SpRunResult Result { get; set; }
        public string ResultText { get; set; }
        public SpRunStatus Status { get; set; }
        public SpReviewStatus ReviewStatus { get; set; }
        public string ReviewedByName { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string ReviewNote { get; set; }
        public int? NcrId { get; set; }
        public string NcrNumber { get; set; }
        public string VoidReason { get; set; }
        public string Note { get; set; }
        public int CreatorUserId { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public List<SpRunValueDto> Values { get; set; } = new List<SpRunValueDto>();
        public List<SpRunInstrumentDto> Instruments { get; set; } = new List<SpRunInstrumentDto>();
        public List<string> Actions { get; set; } = new List<string>();
        public string RowVersion { get; set; }
    }

    public class SpRunQuery : PaginationParams
    {
        public int? TypeId { get; set; }
        public int? OperationId { get; set; }
        public int? OrderId { get; set; }
        public int? PartOperationId { get; set; }
        public int? ReceiptDocumentId { get; set; }
        public SpRunResult? Result { get; set; }
        public SpReviewStatus? ReviewStatus { get; set; }
        public SpRunStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class SaveSpRunValueDto
    {
        public string Name { get; set; }
        public decimal? Value { get; set; }
    }

    public class SaveSpRunDto
    {
        /// <summary>A step of a production order (MES operation) or a legacy part operation: exactly one of the two.</summary>
        public int? OperationId { get; set; }
        public int? PartOperationId { get; set; }
        public int? QualificationId { get; set; }

        /// <summary>A subcontracted step: the supplier's run (no qualification of the company; the supplier must be approved for the process).</summary>
        public bool IsSubcontract { get; set; }
        public int? SupplierId { get; set; }
        public int? ReceiptDocumentId { get; set; }
        public string SerialNumbers { get; set; }
        public int? LotId { get; set; }
        public decimal? Quantity { get; set; }
        public int? OperatorPersonId { get; set; }
        public int? MachineId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }
        public Guid? ChartDocumentId { get; set; }
        public string ChartFileName { get; set; }
        public Guid? CertificateDocumentId { get; set; }
        public string CertificateNumber { get; set; }
        public List<int> InstrumentIds { get; set; } = new List<int>();
        public List<SaveSpRunValueDto> Values { get; set; } = new List<SaveSpRunValueDto>();
        public string Note { get; set; }
    }

    // ------------------------------------------------------------------ supplier approval

    public class SpSupplierApprovalDto
    {
        public int Id { get; set; }
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public int TypeId { get; set; }
        public string TypeCode { get; set; }
        public string TypeTitle { get; set; }
        public SpApprovalStatus Status { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public int DaysLeft { get; set; }
        public string AccreditationNumber { get; set; }
        public Guid? EvidenceDocumentId { get; set; }
        public DateTime? AuditDate { get; set; }
        public string Note { get; set; }
        public int CreatorUserId { get; set; }
        public string CreatorName { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string RejectReason { get; set; }
        public string VoidReason { get; set; }
        public List<string> Actions { get; set; } = new List<string>();
        public string RowVersion { get; set; }
    }

    public class SpSupplierQuery : PaginationParams
    {
        public int? ProviderId { get; set; }
        public int? TypeId { get; set; }
        public SpApprovalStatus? Status { get; set; }
    }

    public class SaveSpSupplierApprovalDto
    {
        public int? Id { get; set; }
        public int ProviderId { get; set; }
        public int TypeId { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public string AccreditationNumber { get; set; }
        public Guid? EvidenceDocumentId { get; set; }
        public DateTime? AuditDate { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------ material specification

    public class MaterialSpecLimitDto
    {
        public int Id { get; set; }
        public int SortOrder { get; set; }
        public CertValueKind Kind { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public bool Required { get; set; } = true;
    }

    public class MaterialSpecItemDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
    }

    public class MaterialSpecDto
    {
        public int Id { get; set; }
        public string Grade { get; set; }
        public string Standard { get; set; }
        public string SpecKey { get; set; }
        public string Title { get; set; }
        public CertificateType RequiredCertificateType { get; set; }
        public string RequiredCertificateTypeText { get; set; }
        public int Version { get; set; }
        public MatSpecStatus Status { get; set; }
        public string Reason { get; set; }
        public int? SupersedesId { get; set; }
        public int CreatorUserId { get; set; }
        public string CreatorName { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string VoidReason { get; set; }
        public string ContentHash { get; set; }
        public List<MaterialSpecLimitDto> Limits { get; set; } = new List<MaterialSpecLimitDto>();
        public List<MaterialSpecItemDto> Items { get; set; } = new List<MaterialSpecItemDto>();
        public List<string> Actions { get; set; } = new List<string>();
        public string RowVersion { get; set; }
    }

    public class MaterialSpecQuery : PaginationParams
    {
        public MatSpecStatus? Status { get; set; }
        public string Contains { get; set; }
        public int? ItemId { get; set; }
    }

    public class SaveMaterialSpecDto
    {
        /// <summary>Edit a draft; empty = a new specification (or the next version when <see cref="NewVersionOfId"/> is set).</summary>
        public int? Id { get; set; }
        public int? NewVersionOfId { get; set; }
        public string Grade { get; set; }
        public string Standard { get; set; }
        public string Title { get; set; }
        public CertificateType RequiredCertificateType { get; set; } = CertificateType.T31;
        public string Reason { get; set; }
        public List<MaterialSpecLimitDto> Limits { get; set; } = new List<MaterialSpecLimitDto>();
        public List<int> ItemIds { get; set; } = new List<int>();
        public string RowVersion { get; set; }
    }

    /// <summary>The certificate type a coded item needs, derived from the BOM line and the approved specification (not typed per receipt).</summary>
    public class RequiredCertificateDto
    {
        public int ItemId { get; set; }
        public CertificateType? Type { get; set; }
        public string TypeText { get; set; }
        public List<string> Sources { get; set; } = new List<string>();
        public List<MaterialSpecDto> Specs { get; set; } = new List<MaterialSpecDto>();
    }

    // ------------------------------------------------------------------ structured certificate

    public class CertificateValueDto
    {
        public CertValueKind Kind { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal Value { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public CertRowStatus Status { get; set; }
    }

    public class SaveCertificateValueDto
    {
        public CertValueKind Kind { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Value { get; set; }
    }

    public class CertificateSheetDto
    {
        public MaterialCertificateDto Certificate { get; set; }
        public MaterialSpecDto Spec { get; set; }
        public RequiredCertificateDto Required { get; set; }
        public List<string> Receipts { get; set; } = new List<string>();
        public List<string> Problems { get; set; } = new List<string>();
        public bool ReceiptAcceptable { get; set; }
    }

    public class CertExcelRequestDto
    {
        public int CertificateId { get; set; }
        public bool Preview { get; set; } = true;
    }

    public class CertExcelRowDto
    {
        public int Row { get; set; }
        public string Name { get; set; }
        public bool Ok { get; set; }
        public string Message { get; set; }
    }

    public class CertExcelResultDto
    {
        public bool Preview { get; set; }
        public int RowCount { get; set; }
        public int ValidRows { get; set; }
        public int ErrorRows { get; set; }
        public List<CertExcelRowDto> Rows { get; set; } = new List<CertExcelRowDto>();
        public List<string> Warnings { get; set; } = new List<string>();
        public MaterialCertificateDto Certificate { get; set; }
    }

    // ------------------------------------------------------------------ gate answer shown on the terminal

    public class SpGateStatusDto
    {
        public bool Applies { get; set; }
        public int? TypeId { get; set; }
        public string TypeTitle { get; set; }
        public bool Ready { get; set; }
        public string Problem { get; set; }
        public List<SpRunDto> Runs { get; set; } = new List<SpRunDto>();
    }
}
