using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.QualityDtos
{
    // Package B: characteristic lists, «بدون نقشه» flags, first article inspection, as-built record, physical configuration audit.

    // ------------------------------------------------------------------ characteristic lists

    public class CharLineDto
    {
        public int Id { get; set; }
        public int RowNumber { get; set; }
        public string No { get; set; }
        public string Name { get; set; }
        public decimal Nominal { get; set; }
        public decimal TolPlus { get; set; }
        public decimal TolMinus { get; set; }
        public decimal Lower { get; set; }
        public decimal Upper { get; set; }
        public string Unit { get; set; }
        public CharClass Class { get; set; }
        public string ClassText { get; set; }
        public EquipmentType InstrumentClass { get; set; }
        public string InstrumentText { get; set; }
        public string Method { get; set; }
        public int SamplePercent { get; set; }
        public bool Required { get; set; }
        public int? OperationSeq { get; set; }
        public string StageCode2 { get; set; }

        /// <summary>For the QC form: the list (and drawing) this characteristic belongs to.</summary>
        public int ListId { get; set; }
        public string DrawingNumber { get; set; }
        public string DrawingRevision { get; set; }
    }

    public class CharListListDto
    {
        public int Id { get; set; }
        public int DeliverableId { get; set; }
        public int RevisionId { get; set; }
        public string DocumentNumber { get; set; }
        public string RevisionCode { get; set; }
        public int Version { get; set; }
        public CharListStatus Status { get; set; }
        public int ItemCount { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
    }

    public class CharListDto : CharListListDto
    {
        public string DocumentTitle { get; set; }
        public string RevisionStatusText { get; set; }
        public bool RevisionInForce { get; set; }
        public string Reason { get; set; }
        public int? SupersedesListId { get; set; }
        public string PreparedByName { get; set; }
        public int CreatorUserId { get; set; }
        public List<int> EditorUserIds { get; set; } = new List<int>();
        public DateTimeOffset? SubmittedAt { get; set; }
        public string ApprovedByName { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string ContentHash { get; set; }
        public string ReturnNote { get; set; }
        public string VoidReason { get; set; }
        public List<CharLineDto> Items { get; set; } = new List<CharLineDto>();

        /// <summary>What the caller may do now: save, submit, return, approve, void, new-version.</summary>
        public List<string> Actions { get; set; } = new List<string>();

        public string RowVersion { get; set; }
    }

    public class CharListQuery : PaginationParams
    {
        public int? DeliverableId { get; set; }
        public int? RevisionId { get; set; }
        public CharListStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class SaveCharLineDto
    {
        public string No { get; set; }
        public string Name { get; set; }
        public decimal Nominal { get; set; }
        public decimal TolPlus { get; set; }
        public decimal TolMinus { get; set; }
        public string Unit { get; set; }
        public CharClass Class { get; set; }
        public EquipmentType InstrumentClass { get; set; }
        public string Method { get; set; }

        /// <summary>Empty = the company default of the class (critical 100 %).</summary>
        public int? SamplePercent { get; set; }
        public int? OperationSeq { get; set; }
        public string StageCode2 { get; set; }
    }

    public class SaveCharListDto
    {
        /// <summary>Empty = a new list (version 1, or the next version after a superseded one) for the revision.</summary>
        public int? Id { get; set; }
        public int RevisionId { get; set; }
        public string Reason { get; set; }
        public List<SaveCharLineDto> Items { get; set; } = new List<SaveCharLineDto>();
        public string RowVersion { get; set; }
    }

    public class CharListActionDto
    {
        public int Id { get; set; }

        /// <summary>submit, return, approve, void, new-version.</summary>
        public string Action { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class CharExcelRowDto
    {
        public int Row { get; set; }
        public string No { get; set; }
        public string Name { get; set; }
        public bool Ok { get; set; }
        public string Message { get; set; }
    }

    public class CharExcelResultDto
    {
        public bool Preview { get; set; }
        public int RowCount { get; set; }
        public int ValidRows { get; set; }
        public int ErrorRows { get; set; }
        public List<CharExcelRowDto> Rows { get; set; } = new List<CharExcelRowDto>();
        public List<string> Warnings { get; set; } = new List<string>();
        public int? ListId { get; set; }
    }

    public class CharExcelRequestDto
    {
        public int RevisionId { get; set; }
        public int? ListId { get; set; }
        public string Reason { get; set; }
        public bool Preview { get; set; }
    }

    // ------------------------------------------------------------------ the characteristics of a part (for the QC forms)

    public class PartDrawingDto
    {
        public int DeliverableId { get; set; }
        public int RevisionId { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string RevisionCode { get; set; }
        public int? ListId { get; set; }
        public int? ListVersion { get; set; }
        public string ListStatusText { get; set; }
    }

    public class PartCharacteristicsDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public bool HasDrawing { get; set; }
        public bool HasReleasedList { get; set; }
        public bool NoDrawingActive { get; set; }
        public string NoDrawingReason { get; set; }
        public List<PartDrawingDto> Drawings { get; set; } = new List<PartDrawingDto>();
        public List<CharLineDto> Lines { get; set; } = new List<CharLineDto>();

        /// <summary>A Persian sentence for the screen: what is missing for an inspection of this part.</summary>
        public string Message { get; set; }
    }

    // ------------------------------------------------------------------ «بدون نقشه»

    public class NoDrawingFlagDto
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string PartNumber { get; set; }
        public string Reason { get; set; }
        public NoDrawingStatus Status { get; set; }
        public int CreatorUserId { get; set; }
        public string RequestedByName { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public string DecidedByName { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
        public string VoidedByName { get; set; }
        public DateTimeOffset? VoidedAt { get; set; }
        public string VoidReason { get; set; }
        public List<string> Actions { get; set; } = new List<string>();
        public string RowVersion { get; set; }
    }

    public class NoDrawingQuery : PaginationParams
    {
        public int? ItemId { get; set; }
        public NoDrawingStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class RequestNoDrawingDto
    {
        public int ItemId { get; set; }
        public string Reason { get; set; }
    }

    public class NoDrawingActionDto
    {
        public int Id { get; set; }

        /// <summary>approve, reject, void.</summary>
        public string Action { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------ first article inspection

    public class FaiListDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string PartNumber { get; set; }
        public int? ProductionOrderId { get; set; }
        public string ProductionOrderNumber { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public FaiStatus Status { get; set; }
        public string Trigger { get; set; }
        public DateTimeOffset CreateDate { get; set; }
    }

    public class FaiDto : FaiListDto
    {
        public string Fingerprint { get; set; }
        public string FingerprintText { get; set; }
        public int? CharacteristicListId { get; set; }
        public int? QcRecordId { get; set; }
        public string QcRecordNumber { get; set; }
        public string MeasuredByName { get; set; }
        public DateTimeOffset? MeasuredAt { get; set; }
        public string QcAcceptedByName { get; set; }
        public DateTimeOffset? QcAcceptedAt { get; set; }
        public string QcAcceptNote { get; set; }
        public string QaApprovedByName { get; set; }
        public DateTimeOffset? QaApprovedAt { get; set; }
        public string QaApproveNote { get; set; }
        public string RejectedByName { get; set; }
        public string RejectReason { get; set; }
        public string VoidReason { get; set; }
        public string Note { get; set; }

        /// <summary>The characteristics to measure (every one of the list) with the value measured so far (from the quality record).</summary>
        public List<FaiLineDto> Lines { get; set; } = new List<FaiLineDto>();
        public List<string> Actions { get; set; } = new List<string>();
        public string RowRevision { get; set; }
        public string RowVersion { get; set; }
    }

    public class FaiLineDto
    {
        public int CharacteristicId { get; set; }
        public string No { get; set; }
        public string Name { get; set; }
        public decimal Nominal { get; set; }
        public decimal TolPlus { get; set; }
        public decimal TolMinus { get; set; }
        public decimal Lower { get; set; }
        public decimal Upper { get; set; }
        public string Unit { get; set; }
        public string ClassText { get; set; }
        public string InstrumentText { get; set; }
        public string Method { get; set; }
        public decimal? Measured { get; set; }
        public bool? Pass { get; set; }
        public string EquipmentCode { get; set; }
    }

    public class FaiQuery : PaginationParams
    {
        public int? ItemId { get; set; }
        public int? ProductionOrderId { get; set; }
        public FaiStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class StartFaiDto
    {
        public int ProductionOrderId { get; set; }
    }

    public class FaiMeasurementDto
    {
        public int CharacteristicId { get; set; }
        public decimal Measured { get; set; }
        public int? EquipmentId { get; set; }
    }

    public class MeasureFaiDto
    {
        public int Id { get; set; }
        public List<FaiMeasurementDto> Measurements { get; set; } = new List<FaiMeasurementDto>();
        public string Note { get; set; }
        public DateTime? RecordDate { get; set; }
    }

    public class FaiActionDto
    {
        public int Id { get; set; }

        /// <summary>qc-accept, qa-approve, reject, void.</summary>
        public string Action { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    // ------------------------------------------------------------------ as-built record

    public class AsBuiltListDto
    {
        public int Id { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int ReleaseNoteId { get; set; }
        public string ReleaseNoteNumber { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string SerialNumber { get; set; }
        public int Version { get; set; }
        public AsBuiltStatus Status { get; set; }
        public string Sha256 { get; set; }
        public string ProductCode { get; set; }
        public string BomText { get; set; }
        public string Reason { get; set; }
        public DateTimeOffset CreateDate { get; set; }
        public string IssuedByName { get; set; }
    }

    public class AsBuiltDto : AsBuiltListDto
    {
        /// <summary>The snapshot as stored (JSON text) and its parsed form for the screen.</summary>
        public string SnapshotJson { get; set; }
        public AsBuiltSnapshotDto Snapshot { get; set; }

        /// <summary>The hash of the stored snapshot, recomputed now: it must equal <see cref="AsBuiltListDto.Sha256"/>.</summary>
        public string RecomputedSha256 { get; set; }
        public bool HashOk { get; set; }
        public List<AsBuiltListDto> Versions { get; set; } = new List<AsBuiltListDto>();
    }

    public class AsBuiltQuery : PaginationParams
    {
        public int? WorkOrderId { get; set; }
        public int? ItemId { get; set; }
        public string Serial { get; set; }
        public bool IncludeSuperseded { get; set; }
    }

    public class ReissueAsBuiltDto
    {
        public int Id { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>The decision of PMO or the CEO on a re-issue that QA asked for (a second person; never the one who asked, never an AI seat).</summary>
    public class DecideAsBuiltDto
    {
        /// <summary>The pending (not yet current) version.</summary>
        public int Id { get; set; }
        public bool Approve { get; set; }
        public string Note { get; set; }
    }

    public class AsBuiltSnapshotDto
    {
        public int SchemaVersion { get; set; } = 1;
        public string WorkOrderCode { get; set; }
        public string ReleaseNoteNumber { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public string ProductCode { get; set; }
        public string ProductTitle { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string SerialNumber { get; set; }
        public decimal Quantity { get; set; }
        public string BomCode { get; set; }
        public int? BomRevision { get; set; }
        public int ProductCount { get; set; }
        public List<AsBuiltRoutingDto> Routings { get; set; } = new List<AsBuiltRoutingDto>();
        public List<AsBuiltDrawingDto> Drawings { get; set; } = new List<AsBuiltDrawingDto>();
        public List<AsBuiltInstalledDto> Installed { get; set; } = new List<AsBuiltInstalledDto>();
        public List<AsBuiltDeviationDto> Deviations { get; set; } = new List<AsBuiltDeviationDto>();
        public List<AsBuiltNcrDto> Ncrs { get; set; } = new List<AsBuiltNcrDto>();
        public List<AsBuiltFaiDto> FirstArticles { get; set; } = new List<AsBuiltFaiDto>();
        public List<string> PcaFindings { get; set; } = new List<string>();

        /// <summary>Package C: the special process runs of the work order (qualification, procedure revision, operator, machine, result, review) and the certificates of the lots built in.</summary>
        public List<AsBuiltSpecialProcessDto> SpecialProcesses { get; set; } = new List<AsBuiltSpecialProcessDto>();
        public List<AsBuiltCertificateDto> Certificates { get; set; } = new List<AsBuiltCertificateDto>();
    }

    public class AsBuiltSpecialProcessDto
    {
        public string RunNumber { get; set; }
        public string ProcessType { get; set; }
        public string QualificationNumber { get; set; }
        public string ProcedureText { get; set; }
        public string Step { get; set; }
        public string SerialNumbers { get; set; }
        public decimal Quantity { get; set; }
        public string OperatorStamp { get; set; }
        public string Machine { get; set; }
        public string StartedAt { get; set; }
        public string EndedAt { get; set; }
        public string Supplier { get; set; }
        public string CertificateNumber { get; set; }
        public string Result { get; set; }
        public string Review { get; set; }
    }

    public class AsBuiltCertificateDto
    {
        public string LotNumber { get; set; }
        public string HeatNumber { get; set; }
        public string Number { get; set; }
        public string Type { get; set; }
        public string Grade { get; set; }
        public string Standard { get; set; }
        public string Evaluation { get; set; }
        public string Inspector { get; set; }
    }

    public class AsBuiltRoutingDto
    {
        public string PartNumber { get; set; }
        public int Revision { get; set; }
    }

    public class AsBuiltDrawingDto
    {
        public string PartNumber { get; set; }
        public string DocumentNumber { get; set; }
        public string RevisionCode { get; set; }
        public int? ListVersion { get; set; }
    }

    public class AsBuiltInstalledDto
    {
        public string PartNumber { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string LotNumber { get; set; }
        public string SerialNumber { get; set; }
        public string HeatNumber { get; set; }
        public string CertificateNumber { get; set; }
        public decimal Quantity { get; set; }
        public string Via { get; set; }
    }

    public class AsBuiltDeviationDto
    {
        public string Number { get; set; }
        public string Kind { get; set; }
        public string PartNumber { get; set; }
        public string Reason { get; set; }
        public string Status { get; set; }
    }

    public class AsBuiltNcrDto
    {
        public string Number { get; set; }
        public string Status { get; set; }
        public string Disposition { get; set; }
        public bool ConcessionApproved { get; set; }
    }

    public class AsBuiltFaiDto
    {
        public string Number { get; set; }
        public string PartNumber { get; set; }
        public string FingerprintText { get; set; }
        public string Status { get; set; }
    }

    // ------------------------------------------------------------------ physical configuration audit

    public class PcaLineDto
    {
        public string PartNumber { get; set; }
        public string PartName { get; set; }
        public decimal Required { get; set; }
        public decimal Found { get; set; }
        public string Problem { get; set; }
    }
}
