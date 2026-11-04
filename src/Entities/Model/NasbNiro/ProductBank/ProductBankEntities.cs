using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.ProductBank
{
    // Package PB1: coded BOM, revisions, part-number item coding and the product bank. All tables start with "Pb".
    // The legacy Equipment -> EquipmentAssembly -> EquipmentBom -> EquipmentProcess chain and the project copy (Assembly -> Bom -> Process) are kept as they were.

    /// <summary>Kind of an OPC (operation process chart) step; the symbol of the chart: circle, square, arrow, triangle.</summary>
    public enum PbStageKind { Operation = 1, Qc = 2, Transport = 3, Storage = 4, InspectionHold = 5 }

    /// <summary>Platform condition of a BOM line: MPL (made), PL (purchased), customer supplied, subcontracted.</summary>
    public enum PbMakeOrBuy { Make = 1, Purchase = 2, CustomerSupplied = 3, Subcontract = 4 }

    public enum PbBomStatus { Draft = 0, UnderReview = 1, Approved = 2, Released = 3, Superseded = 4, Obsolete = 5 }

    /// <summary>The five signatures of the BOM block, in the order they are signed.</summary>
    public enum PbBomSignature { Prepared = 0, Checked = 1, QcChecked = 2, ManufacturingChecked = 3, Approved = 4 }

    public enum PbRoutingStatus { Draft = 0, UnderReview = 1, Approved = 2, Superseded = 3 }

    public enum PbMappingStatus { Draft = 0, Approved = 1, Rejected = 2 }
    public enum PbMappingSource { Suggested = 1, Imported = 2, Manual = 3 }

    public enum PbWbsNodeKind { Summary = 1, Task = 2, Milestone = 3 }

    /// <summary>MSPDI link types: finish-to-finish 0, finish-to-start 1, start-to-finish 2, start-to-start 3.</summary>
    public enum PbLinkType { FinishToFinish = 0, FinishToStart = 1, StartToFinish = 2, StartToStart = 3 }

    public enum PbChangeKind { Add = 1, Remove = 2, Replace = 3, Quantity = 4 }
    public enum PbChangeStatus { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3, Applied = 4, Cancelled = 5 }

    /// <summary>Settings of the module (text values, for example the separator of the stage code). ★ provisional company decisions.</summary>
    public class PbSetting
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
        public string Title { get; set; }
        public string Source { get; set; }
    }

    /// <summary>One two-letter stage code of the OPC charts (RW raw, CT cutting, RL rough lathing ...). The code of a part at a stage is PartNumber + separator + this code.</summary>
    public class PbStage : BaseEntity
    {
        public string Code { get; set; }
        public string TitleFa { get; set; }
        public string TitleEn { get; set; }
        public PbStageKind Kind { get; set; }

        /// <summary>The stage gets its own item (so stock at this stage can be held).</summary>
        public bool IsStockable { get; set; }

        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>"Legacy 14-digit warehouse code" to "BOM part number (+ stage)". Drafts are suggested from the item descriptions or imported; ENGINEERING approves, never the person who proposed it.</summary>
    public class PbLegacyMapping : BaseEntity
    {
        public string LegacyCode { get; set; }
        public string LegacyTitle { get; set; }
        public string LegacyMaterial { get; set; }
        public int? LegacyItemId { get; set; }

        public string PartNumber { get; set; }
        public string StageCode2 { get; set; }
        public string PartTitle { get; set; }

        public PbMappingSource Source { get; set; }

        /// <summary>0 to 100: how sure the suggestion is (name and material match).</summary>
        public decimal Confidence { get; set; }

        public PbMappingStatus Status { get; set; }
        public int ProposedByUserId { get; set; }
        public int? ReviewedByUserId { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string ReviewNote { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
    }

    /// <summary>A product of the product bank (a turbine model, a valve ...): the BOM revisions, OPC routings and WBS templates hang on it.</summary>
    public class PbProduct : BaseEntity
    {
        /// <summary>Number of the top assembly, for example 16.11.00.000.2.</summary>
        public string Code { get; set; }

        public string Title { get; set; }
        public string Platform { get; set; }
        public string Description { get; set; }

        /// <summary>The legacy equipment of the contract screens this product stands for (a work order of that equipment takes the bank baseline).</summary>
        public int? EquipmentId { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>One revision of a BOM (the header with the signature block). Only a Draft revision is edited; every other revision is read-only.</summary>
    public class PbBomHeader : BaseEntity
    {
        public int? ProductId { get; set; }
        public PbProduct Product { get; set; }

        /// <summary>The parent assembly the BOM is of (16.11.00.000.2).</summary>
        public string AssemblyPartNumber { get; set; }

        public int? ProductItemId { get; set; }
        public string Title { get; set; }

        /// <summary>Document code, for example 16.11.00.000.2-BOM.</summary>
        public string Code { get; set; }

        /// <summary>0, 1, 2 ... shown as 00, 01.</summary>
        public int Revision { get; set; }

        public PbBomStatus Status { get; set; }
        public string Platform { get; set; }
        public string IssueFor { get; set; }
        public DateTime? DocumentDate { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        public string ChangeReason { get; set; }

        public int? SupersedesId { get; set; }
        public PbBomHeader Supersedes { get; set; }

        /// <summary>SHA-256 of the lines at the submission; a released revision is verified against it.</summary>
        public string ContentHash { get; set; }

        public int PreparedByUserId { get; set; }
        public DateTimeOffset? PreparedAt { get; set; }
        public int? CheckedByUserId { get; set; }
        public DateTimeOffset? CheckedAt { get; set; }
        public int? QcCheckedByUserId { get; set; }
        public DateTimeOffset? QcCheckedAt { get; set; }
        public int? ManufacturingCheckedByUserId { get; set; }
        public DateTimeOffset? ManufacturingCheckedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? ReleasedByUserId { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }

        /// <summary>The revision was entered as already signed on paper (the first release of the company's own BOM): the signature columns stay empty and the note names the paper document.</summary>
        public bool ImportedSigned { get; set; }

        public string PaperSignatureNote { get; set; }

        /// <summary>Why the last reviewer sent the revision back to Draft.</summary>
        public string ReturnNote { get; set; }

        public ICollection<PbBomLine> Lines { get; set; } = new List<PbBomLine>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PbBomLine : BaseEntity
    {
        public int BomHeaderId { get; set; }
        public PbBomHeader BomHeader { get; set; }

        public int SortOrder { get; set; }
        public int? ItemNumber { get; set; }

        /// <summary>A heading line (Casing, Bearing, STD ...) groups the lines under it.</summary>
        public bool IsHeading { get; set; }

        public int? ParentLineId { get; set; }
        public PbBomLine ParentLine { get; set; }

        public string PartNumber { get; set; }
        public int? ItemId { get; set; }
        public string PartName { get; set; }
        public decimal Qty { get; set; }
        public string Unit { get; set; }
        public string Standard { get; set; }
        public string Material { get; set; }
        public decimal? RawDiameter { get; set; }
        public decimal? RawLength { get; set; }
        public decimal? RawWidth { get; set; }
        public decimal? RawThickness { get; set; }
        public decimal? NetWeight { get; set; }
        public decimal? GrossWeight { get; set; }

        /// <summary>Entered scrap factor (gross / net - 1); empty = derived from the weights.</summary>
        public decimal? ScrapFactor { get; set; }

        public PbMakeOrBuy MakeOrBuy { get; set; }
        public string Description { get; set; }

        /// <summary>The BOM of a sub-assembly (multi-level BOM). A released parent may name only released child BOMs.</summary>
        public int? ChildBomId { get; set; }
        public PbBomHeader ChildBom { get; set; }

        /// <summary>This line is an alternative (option) of another line of the same BOM, for example the clockwise or counter-clockwise part: it is listed, never counted or ordered with the main line.</summary>
        public int? AlternativeOfLineId { get; set; }
        public PbBomLine AlternativeOfLine { get; set; }
    }

    /// <summary>The OPC routing of a part or of an assembly (its columns are the steps of its parts), with revisions.</summary>
    public class PbOpcRouting : BaseEntity
    {
        public int? ProductId { get; set; }
        public PbProduct Product { get; set; }

        /// <summary>Assembly or part the chart is of (Assy NO. 16.13.02.000.2).</summary>
        public string PartNumber { get; set; }

        public string Title { get; set; }
        public string ProjectNumber { get; set; }
        public string Platform { get; set; }
        public int Revision { get; set; }
        public PbRoutingStatus Status { get; set; }
        public DateTime? DocumentDate { get; set; }
        public string ChangeReason { get; set; }
        public int? SupersedesId { get; set; }
        public PbOpcRouting Supersedes { get; set; }
        public string ContentHash { get; set; }

        /// <summary>Generation (the creator), check and approval of the chart: three different people.</summary>
        public int PreparedByUserId { get; set; }
        public int? CheckedByUserId { get; set; }
        public DateTimeOffset? CheckedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public bool ImportedSigned { get; set; }

        /// <summary>The last time the steps were written to the MES routing (OperationTemplate rows + a routing revision waiting for the two MES approvals).</summary>
        public DateTimeOffset? PublishedToMesAt { get; set; }

        public ICollection<PbOpcStep> Steps { get; set; } = new List<PbOpcStep>();

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public class PbOpcStep : BaseEntity
    {
        public int RoutingId { get; set; }
        public PbOpcRouting Routing { get; set; }

        /// <summary>Step number inside its part (its column of the chart).</summary>
        public int Seq { get; set; }

        /// <summary>The part this column is of (an assembly chart has several).</summary>
        public string PartNumber { get; set; }

        public string PartName { get; set; }
        public string StageCode2 { get; set; }
        public PbStageKind Kind { get; set; }
        public string Title { get; set; }
        public int? WorkCenterId { get; set; }
        public string ResourceClass { get; set; }

        /// <summary>★ Standard time in hours; empty until the engineers fill it.</summary>
        public decimal? StandardHours { get; set; }

        public bool IsQcPoint { get; set; }

        /// <summary>Comma list of the evidence kinds the QC point needs: DIM, VT, UT, MT, PT, NDT, TENSILE, HARDNESS, CHEM, HYDRO, BALANCE, RUNOUT.</summary>
        public string RequiredEvidence { get; set; }

        public bool IsOutside { get; set; }
        public PbMakeOrBuy? MakeOrBuy { get; set; }

        /// <summary>The item of the part at this stage (PartNumber-stage), so that stock at the stage can be held.</summary>
        public int? StageItemId { get; set; }

        public string Note { get; set; }
    }

    public class PbWbsTemplate : BaseEntity
    {
        public int ProductId { get; set; }
        public PbProduct Product { get; set; }
        public string Title { get; set; }
        public int Version { get; set; }

        /// <summary>Draft or approved (0 / 1); approval by ENGINEERING + PLANNING, never the importer.</summary>
        public bool IsApproved { get; set; }

        public string Source { get; set; }
        public decimal TotalDurationDays { get; set; }
        public int? ImportedByUserId { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? SecondApprovedByUserId { get; set; }
        public DateTimeOffset? SecondApprovedAt { get; set; }
    }

    public class PbWbsTemplateNode : BaseEntity
    {
        public int TemplateId { get; set; }
        public PbWbsTemplate Template { get; set; }
        public int? ParentId { get; set; }
        public PbWbsTemplateNode Parent { get; set; }

        /// <summary>Position in the template (the outline order of the file).</summary>
        public int Seq { get; set; }

        public int Uid { get; set; }
        public int OutlineLevel { get; set; }
        public string Title { get; set; }
        public PbWbsNodeKind Kind { get; set; }

        /// <summary>E engineering, P procurement, C construction (the three activities of every part).</summary>
        public string PhaseCode { get; set; }

        public decimal DurationDays { get; set; }
        public decimal? StartOffsetDays { get; set; }
        public string PartNumber { get; set; }
        public PbMakeOrBuy? MakeOrBuy { get; set; }
        public string ResourceClass { get; set; }
    }

    public class PbWbsTemplateLink : BaseEntity
    {
        public int TemplateId { get; set; }
        public int FromNodeId { get; set; }
        public int ToNodeId { get; set; }
        public PbLinkType LinkType { get; set; }
        public decimal LagDays { get; set; }
    }

    /// <summary>What a project (work order) took from the product bank: the released BOM revision, the OPC routing revisions and the WBS template, copied as the project baseline.</summary>
    public class PbProjectBaseline : BaseEntity
    {
        public int WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public int ProductId { get; set; }
        public int BomHeaderId { get; set; }
        public string SourceBomCode { get; set; }
        public int SourceBomRevision { get; set; }
        public int? WbsTemplateId { get; set; }
        public int WbsTemplateVersion { get; set; }

        /// <summary>The number of products of the work order the quantities were multiplied by.</summary>
        public int Count { get; set; }

        public DateTimeOffset CopiedAt { get; set; }
        public bool IsCurrent { get; set; } = true;

        /// <summary>The product node of the project-control WBS (PcWbsNode) this baseline generated or extended; empty while the WBS is not generated. The project-control WBS is the only project WBS.</summary>
        public int? PcProductNodeId { get; set; }

        /// <summary>The approved project-control change request that let the product into a contract whose plan was already locked by a baseline; one request is used once.</summary>
        public int? ChangeRequestId { get; set; }
    }

    public class PbProjectBomLine : BaseEntity
    {
        public int BaselineId { get; set; }
        public int? SourceLineId { get; set; }
        public int SortOrder { get; set; }
        public int? ItemNumber { get; set; }
        public bool IsHeading { get; set; }

        /// <summary>The assembly the line belongs to (the top assembly, or a sub-assembly of a multi-level BOM that was exploded into the project).</summary>
        public string AssemblyPartNumber { get; set; }

        public string PartNumber { get; set; }
        public int? ItemId { get; set; }
        public string PartName { get; set; }

        /// <summary>PB2: the project line of the assembly this line belongs to (empty for a top-level line and for lines copied before PB2). The tree gives the effective quantity.</summary>
        public int? ParentLineId { get; set; }
        public PbProjectBomLine ParentLine { get; set; }

        /// <summary>Quantity for ONE unit of the parent assembly (for a top-level line: for one product). PB2: with a parent, the quantity per parent, so a change of the assembly scales its parts.</summary>
        public decimal Qty { get; set; }

        /// <summary>Quantity for the whole work order (the tree product of the quantities x Count), kept up to date by the deviations.</summary>
        public decimal TotalQty { get; set; }

        public string Unit { get; set; }
        public string Material { get; set; }
        public PbMakeOrBuy MakeOrBuy { get; set; }
        public bool IsRemoved { get; set; }
        public int? ChangedByChangeId { get; set; }
    }

    public class PbProjectRouting : BaseEntity
    {
        public int BaselineId { get; set; }
        public int OpcRoutingId { get; set; }
        public int Revision { get; set; }
        public string PartNumber { get; set; }
    }

    /// <summary>A deviation of one project from the bank BOM: add, remove, replace or change the quantity of a line. Approved by ENGINEERING and PLANNING, neither of them the requester.</summary>
    public class PbProjectBomChange : BaseEntity
    {
        public int BaselineId { get; set; }
        public string Number { get; set; }
        public PbChangeKind Kind { get; set; }
        public int? TargetLineId { get; set; }

        public string PartNumber { get; set; }
        public int? ItemId { get; set; }
        public string PartName { get; set; }
        public decimal? NewQty { get; set; }
        public string Unit { get; set; }
        public string Material { get; set; }
        public PbMakeOrBuy? MakeOrBuy { get; set; }

        public string Reason { get; set; }
        public PbChangeStatus Status { get; set; }
        public int? EngineeringApprovedByUserId { get; set; }
        public DateTimeOffset? EngineeringApprovedAt { get; set; }
        public int? PlanningApprovedByUserId { get; set; }
        public DateTimeOffset? PlanningApprovedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public string RejectReason { get; set; }

        /// <summary>Once the PC baseline of the contract is current, the deviation is applied only through an approved change request of project control.</summary>
        public int? ChangeRequestId { get; set; }

        public DateTimeOffset? AppliedAt { get; set; }
        public int? AppliedByUserId { get; set; }
    }
}
