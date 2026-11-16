using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.EngInbox;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.EngInboxDtos
{
    public class EngInboxStatusDto
    {
        public string SourceName { get; set; }
        public bool IsConfigured { get; set; }

        /// <summary>The folder path shown to people.</summary>
        public string Location { get; set; }

        /// <summary>The Persian reason when the source is not configured.</summary>
        public string NotConfiguredReason { get; set; }

        public DateTimeOffset? LastScanAt { get; set; }
        public string LastScanNote { get; set; }
        public int LastFound { get; set; }
        public int LastIngested { get; set; }
        public int LastRejected { get; set; }
        public int LastDuplicates { get; set; }

        /// <summary>Per status: how many packages (every status is listed, also with 0).</summary>
        public List<EngInboxStatusCountDto> Counts { get; set; } = new List<EngInboxStatusCountDto>();

        /// <summary>False when this user cannot start a scan; the reason is then in <see cref="ScanDisabledReason"/>.</summary>
        public bool CanScan { get; set; }
        public string ScanDisabledReason { get; set; }
        public bool CanDismiss { get; set; }

        /// <summary>Persian reason when the engineering agent does not apply packages now (the apply step or the AG-ENG seat is switched off); null otherwise. The packages stay as they are.</summary>
        public string ApplyPausedReason { get; set; }
    }

    public class EngInboxStatusCountDto
    {
        public EngInboxStatus Status { get; set; }
        public int Count { get; set; }
    }

    public class EngInboxQuery : PaginationParams
    {
        public EngInboxStatus? Status { get; set; }
        public EngInboxKind? Kind { get; set; }

        /// <summary>Part of the platform code, standard, contract, project or title.</summary>
        public string Search { get; set; }
        public int? Id { get; set; }
    }

    public class EngInboxPackageDto
    {
        public int Id { get; set; }
        public string SourceName { get; set; }
        public EngInboxKind Kind { get; set; }
        public string PlatformCode { get; set; }
        public string Standard { get; set; }
        public string ContractCode { get; set; }
        public string ProjectCode { get; set; }
        public int PackageVersion { get; set; }
        public string Title { get; set; }
        public string PreparedByText { get; set; }
        public EngInboxStatus Status { get; set; }
        public string StatusNote { get; set; }
        public string RejectCode { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public int BomCount { get; set; }
        public int OpcCount { get; set; }
        public int DocCount { get; set; }
        public int MdlCount { get; set; }
        public DateTimeOffset IngestedAt { get; set; }
        public DateTimeOffset? StatusAt { get; set; }
        public string ApplyBlockReason { get; set; }
        public int? ProductId { get; set; }
        public int? BomHeaderId { get; set; }
        public int? RoutingId { get; set; }
        public int? LinkedContractId { get; set; }
        public int? LinkedWorkOrderId { get; set; }
        public string DismissReason { get; set; }
        public string RowVersion { get; set; }
        public int? OriginatorUserId { get; set; }
    }

    public class EngInboxFindingDto
    {
        /// <summary>error or warning.</summary>
        public string Severity { get; set; }
        public string Sheet { get; set; }
        public int Row { get; set; }
        public string Message { get; set; }
    }

    public class EngInboxBomLineDto
    {
        public int Level { get; set; }
        public string ParentCode { get; set; }
        public string PartCode { get; set; }
        public string Description { get; set; }
        public decimal Qty { get; set; }
        public string Unit { get; set; }
        public int MakeBuy { get; set; }
        public string Material { get; set; }
        public string DrawingNumber { get; set; }
        public string DrawingRevision { get; set; }
        public string Note { get; set; }
    }

    public class EngInboxOpcLineDto
    {
        public string PartCode { get; set; }
        public int OperationNumber { get; set; }
        public string Description { get; set; }
        public string WorkCenterCode { get; set; }
        public decimal SetupMinutes { get; set; }
        public decimal RunMinutes { get; set; }
        public bool IsInspection { get; set; }
        public bool IsSpecialProcess { get; set; }
        public string Predecessors { get; set; }
        public string StageCode { get; set; }
    }

    public class EngInboxDocDto
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public string Revision { get; set; }
        public string DocType { get; set; }
        public bool IsRequired { get; set; }
        public string FileName { get; set; }
        public long? FileSize { get; set; }
        public string FileSha256 { get; set; }
    }

    public class EngInboxMdlDto
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public string DocType { get; set; }
        public DateTime? CommitDate { get; set; }
    }

    public class EngInboxPackageDetailDto : EngInboxPackageDto
    {
        public int SchemaVersion { get; set; }
        public string ContentHash { get; set; }
        public string OriginalPath { get; set; }
        public string StoredPath { get; set; }
        public int ApplyAttempts { get; set; }
        public DateTimeOffset? LastApplyAt { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
        public int? SupersededByPackageId { get; set; }
        public string ProductCode { get; set; }
        public string BomCode { get; set; }
        public int? BomRevision { get; set; }
        public string BomStatus { get; set; }
        public int? BomStatusCode { get; set; }
        public string RoutingPartNumber { get; set; }
        public int? RoutingRevision { get; set; }
        public string RoutingStatus { get; set; }
        public int? RoutingStatusCode { get; set; }
        public bool CanDismiss { get; set; }
        public List<EngInboxFindingDto> Findings { get; set; } = new List<EngInboxFindingDto>();
        public List<EngInboxBomLineDto> BomLines { get; set; } = new List<EngInboxBomLineDto>();
        public List<EngInboxOpcLineDto> OpcLines { get; set; } = new List<EngInboxOpcLineDto>();
        public List<EngInboxDocDto> Documents { get; set; } = new List<EngInboxDocDto>();
        public List<EngInboxMdlDto> MdlLines { get; set; } = new List<EngInboxMdlDto>();
    }

    public class EngInboxDismissDto
    {
        public int Id { get; set; }
        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }

    public class EngInboxScanResultDto
    {
        public bool NotConfigured { get; set; }
        public int Found { get; set; }
        public int Ingested { get; set; }
        public int Duplicates { get; set; }
        public int Rejected { get; set; }
        public int Errors { get; set; }
        public string Note { get; set; }
    }
}
