using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.EngInbox
{
    // ENG-F1: the engineering inbox. The engineering unit drops a package (one workbook + the referenced document files) into a folder on the server; the engineering agent reads it, keeps a read-only STRUCTURED copy of
    // the BOM / OPC / document list / MDL here (material balance, purchasing, work orders and scheduling compute on the data) and continues the chain through the product bank services. The document FILES stay in the
    // folder (reference only: name, size and SHA-256 are kept). All tables start with "EngInbox". Nothing cascades.

    public enum EngInboxKind { Platform = 1, Project = 2 }

    /// <summary>Ingested: waiting for the agent to apply it. Rejected: the workbook failed validation (the findings say why). Applied: the product bank has the drafts. ApplyBlocked: a rule of the product bank or a missing master
    /// stopped the agent (the reason says what). Superseded: a higher version of the same package arrived. Dismissed: the engineering unit closed it with a reason.</summary>
    public enum EngInboxStatus { Ingested = 1, Rejected = 2, Applied = 3, ApplyBlocked = 4, Superseded = 5, Dismissed = 6 }

    public class EngInboxPackage : BaseEntity
    {
        /// <summary>Folder or Api (the source that delivered it).</summary>
        public string SourceName { get; set; }

        public EngInboxKind Kind { get; set; }

        /// <summary>The key of the package: empty text (never null) for the parts that the kind does not have, so the unique index treats them as equal.</summary>
        public string PlatformCode { get; set; } = "";
        public string Standard { get; set; } = "";
        public string ContractCode { get; set; } = "";
        public string ProjectCode { get; set; } = "";

        public int PackageVersion { get; set; }
        public string Title { get; set; }

        /// <summary>Free text from the workbook (not a user of the system).</summary>
        public string PreparedByText { get; set; }

        /// <summary>The user (login name in the workbook «تهیه‌کننده») who prepared the package: a human, active user. Null only on a rejected package. This person can never check, approve, release or publish what the agent made from the package (maker is not checker).</summary>
        public int? OriginatorUserId { get; set; }

        public int SchemaVersion { get; set; }

        /// <summary>SHA-256 of the normalised content and the referenced files (never of the raw workbook).</summary>
        public string ContentHash { get; set; }

        /// <summary>The package folder as the source saw it, relative to the inbox root.</summary>
        public string OriginalPath { get; set; }

        /// <summary>The size and last write time of the files of the folder as the source saw them: a rejected package that is seen again unchanged is not stored a second time.</summary>
        public string SourceSignature { get; set; }

        /// <summary>Where the folder was moved to (_Processed or _Rejected), relative to the inbox root.</summary>
        public string StoredPath { get; set; }

        public EngInboxStatus Status { get; set; }

        /// <summary>One Persian sentence about the status (for a duplicate: «تکراری»).</summary>
        public string StatusNote { get; set; }

        /// <summary>Set on a rejection that is not cured by the same version corrected: VersionConflict (same version, other content).</summary>
        public string RejectCode { get; set; }

        /// <summary>The findings of the validation as JSON: [{severity, sheet, row, message}].</summary>
        public string FindingsJson { get; set; }

        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public int BomCount { get; set; }
        public int OpcCount { get; set; }
        public int DocCount { get; set; }
        public int MdlCount { get; set; }

        public DateTimeOffset IngestedAt { get; set; }
        public DateTimeOffset? StatusAt { get; set; }

        public int? SupersededByPackageId { get; set; }

        // ---------------------------------------------------------------------------------------------- apply (product bank / project link)
        public DateTimeOffset? LastApplyAt { get; set; }
        public int ApplyAttempts { get; set; }

        /// <summary>The Persian reason of the last blocked apply (exact; what must be done).</summary>
        public string ApplyBlockReason { get; set; }

        public DateTimeOffset? AppliedAt { get; set; }

        /// <summary>The product bank product the package made or found (platform packages).</summary>
        public int? ProductId { get; set; }

        /// <summary>The BOM header of the root assembly.</summary>
        public int? BomHeaderId { get; set; }

        /// <summary>The OPC routing of the root assembly.</summary>
        public int? RoutingId { get; set; }

        /// <summary>Project packages: the contract and the work order the codes named.</summary>
        public int? LinkedContractId { get; set; }
        public int? LinkedWorkOrderId { get; set; }

        public int? DismissedByUserId { get; set; }
        public string DismissReason { get; set; }

        public ICollection<EngInboxBomLine> BomLines { get; set; } = new List<EngInboxBomLine>();
        public ICollection<EngInboxOpcLine> OpcLines { get; set; } = new List<EngInboxOpcLine>();
        public ICollection<EngInboxDocRef> Documents { get; set; } = new List<EngInboxDocRef>();
        public ICollection<EngInboxMdlLine> MdlLines { get; set; } = new List<EngInboxMdlLine>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>1 = PbBomHeader, 2 = PbOpcRouting, 3 = PbProduct (the product bank records the agent created or edited from a package; no column was added to the product bank tables).</summary>
    public static class EngInboxAppliedKind
    {
        public const byte BomHeader = 1;
        public const byte Routing = 2;
        public const byte Product = 3;
    }

    /// <summary>One row per BOM header, routing or product that applying a package created or edited. The product bank services read it to refuse the originator of the package on every check, approval, release and publication.</summary>
    public class EngInboxAppliedItem : BaseEntity
    {
        public int PackageId { get; set; }
        public EngInboxPackage Package { get; set; }

        /// <summary>See <see cref="EngInboxAppliedKind"/>.</summary>
        public byte Kind { get; set; }

        public int EntityId { get; set; }
    }

    public class EngInboxBomLine : BaseEntity
    {
        public int PackageId { get; set; }
        public EngInboxPackage Package { get; set; }
        public int SortOrder { get; set; }
        public int SourceRow { get; set; }
        public int Level { get; set; }
        public string ParentCode { get; set; }
        public string PartCode { get; set; }
        public string Description { get; set; }
        public decimal Qty { get; set; }
        public string Unit { get; set; }

        /// <summary>1 make, 2 purchase, 3 customer supplied, 4 subcontract (the values of PbMakeOrBuy).</summary>
        public int MakeBuy { get; set; }

        public string Material { get; set; }
        public string DrawingNumber { get; set; }
        public string DrawingRevision { get; set; }
        public string Note { get; set; }
    }

    public class EngInboxOpcLine : BaseEntity
    {
        public int PackageId { get; set; }
        public EngInboxPackage Package { get; set; }
        public int SortOrder { get; set; }
        public int SourceRow { get; set; }
        public string PartCode { get; set; }
        public int OperationNumber { get; set; }
        public string Description { get; set; }
        public string WorkCenterCode { get; set; }
        public decimal SetupMinutes { get; set; }
        public decimal RunMinutes { get; set; }
        public bool IsInspection { get; set; }
        public bool IsSpecialProcess { get; set; }

        /// <summary>Operation numbers of the same part, comma separated.</summary>
        public string Predecessors { get; set; }

        public string StageCode { get; set; }
    }

    /// <summary>A document the package refers to. The file itself is never stored in the database or copied into the file store of Orchestra: only its name, size and SHA-256.</summary>
    public class EngInboxDocRef : BaseEntity
    {
        public int PackageId { get; set; }
        public EngInboxPackage Package { get; set; }
        public int SourceRow { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Revision { get; set; }
        public string DocType { get; set; }
        public bool IsRequired { get; set; }
        public string FileName { get; set; }
        public long? FileSize { get; set; }
        public string FileSha256 { get; set; }
    }

    /// <summary>The master document list the client finalised at the kick-off meeting (project packages).</summary>
    public class EngInboxMdlLine : BaseEntity
    {
        public int PackageId { get; set; }
        public EngInboxPackage Package { get; set; }
        public int SourceRow { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string DocType { get; set; }
        public DateTime? CommitDate { get; set; }
    }

    /// <summary>One row: when the inbox was last scanned and what the scan found (the cheap probe of the ingest step and the status line of the page).</summary>
    public class EngInboxScanState : BaseEntity
    {
        public DateTimeOffset? LastScanAt { get; set; }
        public int LastFound { get; set; }
        public int LastIngested { get; set; }
        public int LastRejected { get; set; }
        public int LastDuplicates { get; set; }
        public string LastNote { get; set; }
    }
}
