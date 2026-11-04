using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using ViewModels.NasbNiro.Pagination;

namespace ViewModels.NasbNiro.ProductBankDtos
{
    // ------------------------------------------------------------------------------------------ stage dictionary, settings, item coding

    public class PbStageDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string TitleFa { get; set; }
        public string TitleEn { get; set; }
        public PbStageKind Kind { get; set; }
        public bool IsStockable { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class SavePbStageDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string TitleFa { get; set; }
        public string TitleEn { get; set; }
        public PbStageKind Kind { get; set; }
        public bool IsStockable { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PbSettingDto
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public string Title { get; set; }
        public string Source { get; set; }
    }

    public class SavePbSettingDto
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    /// <summary>The code and title an item of a part (at a stage) would get, and what is wrong with them.</summary>
    public class PbCodePreviewDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string PartNumber { get; set; }
        public string StageCode2 { get; set; }
        public string StageTitle { get; set; }
        public bool Exists { get; set; }
        public int? ItemId { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class PbCreateItemDto
    {
        public string PartNumber { get; set; }

        /// <summary>Empty = the part itself (code = part number); otherwise the item of the part at this stage.</summary>
        public string StageCode2 { get; set; }

        /// <summary>Title of the part (needed when the part has no item yet).</summary>
        public string PartTitle { get; set; }

        public string Unit { get; set; }
        public string Material { get; set; }
        public string Standard { get; set; }
        public int? ItemGroupId { get; set; }
        public ItemTrackingMode? TrackingMode { get; set; }

        /// <summary>The same title and part number already exist under another code: the caller knows and creates it anyway.</summary>
        public bool ConfirmNearDuplicate { get; set; }
    }

    public class PbMappingDto
    {
        public int Id { get; set; }
        public string LegacyCode { get; set; }
        public string LegacyTitle { get; set; }
        public string LegacyMaterial { get; set; }
        public int? LegacyItemId { get; set; }
        public string PartNumber { get; set; }
        public string StageCode2 { get; set; }
        public string PartTitle { get; set; }
        public PbMappingSource Source { get; set; }
        public decimal Confidence { get; set; }
        public PbMappingStatus Status { get; set; }
        public int ProposedByUserId { get; set; }
        public int? ReviewedByUserId { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public string ReviewNote { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }

        /// <summary>The caller may approve or reject it (engineering, not the person who proposed it).</summary>
        public bool CanDecide { get; set; }
    }

    public class PbMappingQuery : PaginationParams
    {
        public PbMappingStatus? Status { get; set; }
        public string Contains { get; set; }
        public decimal? MinConfidence { get; set; }
    }

    public class PbImportMappingRowDto
    {
        public string LegacyCode { get; set; }
        public string LegacyTitle { get; set; }
        public string LegacyMaterial { get; set; }
        public string PartNumber { get; set; }
        public string StageCode2 { get; set; }
    }

    public class PbImportMappingsResultDto
    {
        public int Created { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class SavePbMappingDto
    {
        public int? Id { get; set; }
        public string LegacyCode { get; set; }
        public string PartNumber { get; set; }
        public string StageCode2 { get; set; }
    }

    public class PbDecideDto
    {
        public int Id { get; set; }
        public bool Approve { get; set; }
        public string Note { get; set; }
    }

    public class PbAdoptResultDto
    {
        public int Adopted { get; set; }
        public bool AlreadyDone { get; set; }
        public string Marker { get; set; }
        public List<string> Conflicts { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------------------------------ BOM

    public class PbBomListDto
    {
        public int Id { get; set; }
        public int? ProductId { get; set; }
        public string Code { get; set; }
        public int Revision { get; set; }
        public string RevisionText { get; set; }
        public string Title { get; set; }
        public string AssemblyPartNumber { get; set; }
        public PbBomStatus Status { get; set; }
        public DateTime? DocumentDate { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        public int LineCount { get; set; }
        public int PreparedByUserId { get; set; }
        public DateTimeOffset CreateDate { get; set; }
    }

    public class PbBomQuery : PaginationParams
    {
        public int? ProductId { get; set; }
        public PbBomStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class PbBomLineDto
    {
        public int Id { get; set; }
        public int SortOrder { get; set; }
        public int? ItemNumber { get; set; }
        public bool IsHeading { get; set; }
        public int? ParentLineId { get; set; }

        /// <summary>This line is an alternative (option) of the line with this id: it is listed but never counted or ordered with the main line.</summary>
        public int? AlternativeOfLineId { get; set; }

        public string PartNumber { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
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
        public decimal? ScrapFactor { get; set; }

        /// <summary>The scrap factor in force: the entered one, else gross / net - 1.</summary>
        public decimal? ScrapFactorEffective { get; set; }

        public PbMakeOrBuy MakeOrBuy { get; set; }
        public string Description { get; set; }
        public int? ChildBomId { get; set; }
    }

    /// <summary>One person's signature on the BOM block (role of the step, user, time).</summary>
    public class PbSignatureDto
    {
        public PbBomSignature Step { get; set; }
        public string Title { get; set; }
        public string Roles { get; set; }
        public int? UserId { get; set; }
        public string UserName { get; set; }
        public DateTimeOffset? At { get; set; }
        public bool Done { get; set; }
    }

    public class PbBomDto : PbBomListDto
    {
        public string Platform { get; set; }
        public string IssueFor { get; set; }
        public string ChangeReason { get; set; }
        public int? SupersedesId { get; set; }
        public string ContentHash { get; set; }
        public bool ImportedSigned { get; set; }
        public string PaperSignatureNote { get; set; }
        public DateTimeOffset? PreparedAt { get; set; }
        public int? ReleasedByUserId { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public List<PbSignatureDto> Signatures { get; set; } = new List<PbSignatureDto>();
        public List<PbBomLineDto> Lines { get; set; } = new List<PbBomLineDto>();
        public int? NextSignature { get; set; }

        /// <summary>The setting BomSignatures:DistinctReviewers in force (the preparer always differs from every signer).</summary>
        public bool DistinctReviewers { get; set; }

        // per caller (the same predicates the actions use)
        public bool CanEdit { get; set; }
        public bool CanSubmit { get; set; }
        public bool CanSign { get; set; }
        public bool CanRelease { get; set; }
        public bool CanNewRevision { get; set; }
        public bool CanObsolete { get; set; }
    }

    public class SavePbBomLineDto
    {
        public int? ItemNumber { get; set; }
        public bool IsHeading { get; set; }

        /// <summary>Index (in the same list) of the heading line this line belongs to.</summary>
        public int? ParentIndex { get; set; }

        /// <summary>Index (in the same list) of the main line this line is an alternative of.</summary>
        public int? AlternativeOfIndex { get; set; }

        public string PartNumber { get; set; }
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
        public decimal? ScrapFactor { get; set; }
        public PbMakeOrBuy MakeOrBuy { get; set; } = PbMakeOrBuy.Purchase;
        public string Description { get; set; }
        public int? ChildBomId { get; set; }
    }

    public class SavePbBomDto
    {
        public int? Id { get; set; }
        public int? ProductId { get; set; }
        public string AssemblyPartNumber { get; set; }
        public string Title { get; set; }
        public string Platform { get; set; }
        public string IssueFor { get; set; }
        public DateTime? DocumentDate { get; set; }
        public string ChangeReason { get; set; }
        public List<SavePbBomLineDto> Lines { get; set; } = new List<SavePbBomLineDto>();
    }

    public class PbBomActionDto
    {
        public int Id { get; set; }
        public string Note { get; set; }
        public DateTime? EffectiveFrom { get; set; }
    }

    public class PbSignDto
    {
        public int Id { get; set; }
        public PbBomSignature Step { get; set; }
        public string Note { get; set; }
    }

    public class PbDiffFieldDto
    {
        public string Field { get; set; }
        public string Old { get; set; }
        public string New { get; set; }
    }

    public class PbDiffLineDto
    {
        public string PartNumber { get; set; }
        public string PartName { get; set; }
        public int? ItemNumber { get; set; }
        public decimal Qty { get; set; }
        public List<PbDiffFieldDto> Fields { get; set; } = new List<PbDiffFieldDto>();
    }

    public class PbBomDiffDto
    {
        public int FromId { get; set; }
        public int ToId { get; set; }
        public string FromCode { get; set; }
        public string ToCode { get; set; }
        public List<PbDiffLineDto> Added { get; set; } = new List<PbDiffLineDto>();
        public List<PbDiffLineDto> Removed { get; set; } = new List<PbDiffLineDto>();
        public List<PbDiffLineDto> Changed { get; set; } = new List<PbDiffLineDto>();
        public int Unchanged { get; set; }
    }

    public class PbWhereUsedRowDto
    {
        public int BomId { get; set; }
        public string BomCode { get; set; }
        public int Revision { get; set; }
        public PbBomStatus Status { get; set; }
        public string AssemblyPartNumber { get; set; }
        public int? ItemNumber { get; set; }
        public decimal Qty { get; set; }
        public bool InLatestRelease { get; set; }
    }

    public class PbWhereUsedDto
    {
        public string PartNumber { get; set; }
        public string PartName { get; set; }
        public List<PbWhereUsedRowDto> Rows { get; set; } = new List<PbWhereUsedRowDto>();
    }

    // ------------------------------------------------------------------------------------------ OPC routing

    public class PbOpcStepDto
    {
        public int Id { get; set; }
        public int Seq { get; set; }
        public string PartNumber { get; set; }
        public string PartName { get; set; }
        public string StageCode2 { get; set; }
        public string StageTitle { get; set; }
        public PbStageKind Kind { get; set; }
        public string Title { get; set; }
        public int? WorkCenterId { get; set; }
        public string ResourceClass { get; set; }
        public decimal? StandardHours { get; set; }
        public bool IsQcPoint { get; set; }
        public string RequiredEvidence { get; set; }
        public bool IsOutside { get; set; }
        public PbMakeOrBuy? MakeOrBuy { get; set; }
        public int? StageItemId { get; set; }
        public string StageItemCode { get; set; }
        public string Note { get; set; }
    }

    public class PbOpcListDto
    {
        public int Id { get; set; }
        public int? ProductId { get; set; }
        public string PartNumber { get; set; }
        public string Title { get; set; }
        public int Revision { get; set; }
        public PbRoutingStatus Status { get; set; }
        public int StepCount { get; set; }
        public DateTimeOffset? PublishedToMesAt { get; set; }
    }

    public class PbOpcDto : PbOpcListDto
    {
        public string ProjectNumber { get; set; }
        public string Platform { get; set; }
        public DateTime? DocumentDate { get; set; }
        public string ChangeReason { get; set; }
        public int? SupersedesId { get; set; }
        public int PreparedByUserId { get; set; }
        public int? CheckedByUserId { get; set; }
        public DateTimeOffset? CheckedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public bool ImportedSigned { get; set; }
        public List<PbOpcStepDto> Steps { get; set; } = new List<PbOpcStepDto>();
        public bool CanEdit { get; set; }
        public bool CanSubmit { get; set; }
        public bool CanCheck { get; set; }
        public bool CanApprove { get; set; }
        public bool CanPublish { get; set; }
    }

    public class SavePbOpcStepDto
    {
        public int Seq { get; set; }
        public string PartNumber { get; set; }
        public string PartName { get; set; }
        public string StageCode2 { get; set; }
        public PbStageKind? Kind { get; set; }
        public string Title { get; set; }
        public int? WorkCenterId { get; set; }
        public string ResourceClass { get; set; }
        public decimal? StandardHours { get; set; }
        public bool IsQcPoint { get; set; }
        public string RequiredEvidence { get; set; }
        public bool IsOutside { get; set; }
        public PbMakeOrBuy? MakeOrBuy { get; set; }
        public string Note { get; set; }
    }

    public class SavePbOpcDto
    {
        public int? Id { get; set; }
        public int? ProductId { get; set; }
        public string PartNumber { get; set; }
        public string Title { get; set; }
        public string ProjectNumber { get; set; }
        public string Platform { get; set; }
        public DateTime? DocumentDate { get; set; }
        public string ChangeReason { get; set; }
        public List<SavePbOpcStepDto> Steps { get; set; } = new List<SavePbOpcStepDto>();
    }

    public class PbOpcActionDto
    {
        public int Id { get; set; }
        public string Note { get; set; }
    }

    public class PbPublishResultDto
    {
        public int RoutingId { get; set; }
        public int Parts { get; set; }
        public int OperationsWritten { get; set; }
        public int StageItemsCreated { get; set; }
        public List<string> Skipped { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    // ------------------------------------------------------------------------------------------ product bank, WBS template

    public class PbProductDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Platform { get; set; }
        public string Description { get; set; }
        public int? EquipmentId { get; set; }
        public bool IsActive { get; set; }
        public int? ReleasedBomId { get; set; }
        public int? ReleasedRevision { get; set; }
        public int BomRevisions { get; set; }
        public int RoutingCount { get; set; }
        public int? WbsTemplateId { get; set; }
        public int? WbsTemplateVersion { get; set; }
    }

    public class SavePbProductDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Platform { get; set; }
        public string Description { get; set; }
        public int? EquipmentId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PbProductBankDto
    {
        public PbProductDto Product { get; set; }
        public List<PbBomListDto> Boms { get; set; } = new List<PbBomListDto>();
        public List<PbOpcListDto> Routings { get; set; } = new List<PbOpcListDto>();
        public List<PbWbsTemplateListDto> Templates { get; set; } = new List<PbWbsTemplateListDto>();
    }

    public class PbWbsTemplateListDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string Title { get; set; }
        public int Version { get; set; }
        public bool IsApproved { get; set; }
        public string Source { get; set; }
        public decimal TotalDurationDays { get; set; }
        public int NodeCount { get; set; }
    }

    public class PbWbsNodeDto
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public int Seq { get; set; }
        public int Uid { get; set; }
        public int OutlineLevel { get; set; }
        public string Title { get; set; }
        public PbWbsNodeKind Kind { get; set; }
        public string PhaseCode { get; set; }
        public decimal DurationDays { get; set; }
        public decimal? StartOffsetDays { get; set; }
        public string PartNumber { get; set; }
        public PbMakeOrBuy? MakeOrBuy { get; set; }
        public string ResourceClass { get; set; }
    }

    public class PbWbsLinkDto
    {
        public int FromNodeId { get; set; }
        public int ToNodeId { get; set; }
        public PbLinkType LinkType { get; set; }
        public decimal LagDays { get; set; }
    }

    public class PbWbsTemplateDto : PbWbsTemplateListDto
    {
        public int? ImportedByUserId { get; set; }
        public int? ApprovedByUserId { get; set; }
        public int? SecondApprovedByUserId { get; set; }
        public List<PbWbsNodeDto> Nodes { get; set; } = new List<PbWbsNodeDto>();
        public List<PbWbsLinkDto> Links { get; set; } = new List<PbWbsLinkDto>();
        public bool CanApprove { get; set; }

        /// <summary>What the importer did not take over (calendars, percent lags ...).</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class PbImportMspdiDto
    {
        public int ProductId { get; set; }
        public string Title { get; set; }

        /// <summary>The text of the XML file (Project "Save As XML" of Microsoft Project, MSPDI).</summary>
        public string Xml { get; set; }
    }

    public class PbSeedResultDto
    {
        public int Items { get; set; }
        public int Stages { get; set; }
        public int BomLines { get; set; }
        public int Routings { get; set; }
        public int TemplateNodes { get; set; }
        public int Mappings { get; set; }
        public int ProductId { get; set; }
        public int BomId { get; set; }
        public bool AlreadyInstalled { get; set; }
    }

    public class PbGenerateWbsDto
    {
        public int WorkOrderId { get; set; }
        public int? ChangeRequestId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ project instantiation and deviations

    public class PbInstantiateDto
    {
        public int WorkOrderId { get; set; }
        public int ProductId { get; set; }

        /// <summary>Needed when the contract's project-control baseline is current (the plan is locked): the approved change request that lists this work order (its code) as the product being added. One request is used once.</summary>
        public int? ChangeRequestId { get; set; }
    }

    /// <summary>A row of the project-control WBS (node or activity) that a baseline generated from the WBS template.</summary>
    public class PbPcWbsRowDto
    {
        public string Key { get; set; }
        public string ParentKey { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public bool IsActivity { get; set; }
        public string Phase { get; set; }
        public int DurationDays { get; set; }
        public string ResponsibleUnit { get; set; }
    }

    public class PbProjectLineDto
    {
        public int Id { get; set; }
        public int SortOrder { get; set; }
        public int? ItemNumber { get; set; }
        public bool IsHeading { get; set; }
        public string AssemblyPartNumber { get; set; }
        public string PartNumber { get; set; }
        public string PartName { get; set; }
        public decimal Qty { get; set; }
        public decimal TotalQty { get; set; }
        public string Unit { get; set; }
        public string Material { get; set; }
        public PbMakeOrBuy MakeOrBuy { get; set; }
        public bool IsRemoved { get; set; }
        public int? ChangedByChangeId { get; set; }
    }

    public class PbChangeDto
    {
        public int Id { get; set; }
        public int BaselineId { get; set; }
        public string Number { get; set; }
        public PbChangeKind Kind { get; set; }
        public int? TargetLineId { get; set; }
        public string TargetPartNumber { get; set; }
        public string PartNumber { get; set; }
        public string PartName { get; set; }
        public decimal? NewQty { get; set; }
        public string Unit { get; set; }
        public string Material { get; set; }
        public PbMakeOrBuy? MakeOrBuy { get; set; }
        public string Reason { get; set; }
        public PbChangeStatus Status { get; set; }
        public int RequesterUserId { get; set; }
        public int? EngineeringApprovedByUserId { get; set; }
        public int? PlanningApprovedByUserId { get; set; }
        public int? RejectedByUserId { get; set; }
        public string RejectReason { get; set; }
        public int? ChangeRequestId { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
        public bool NeedsChangeRequest { get; set; }
        public bool CanApproveEngineering { get; set; }
        public bool CanApprovePlanning { get; set; }
    }

    public class PbBaselineDto
    {
        public int Id { get; set; }
        public int WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public int BomHeaderId { get; set; }
        public string SourceBomCode { get; set; }
        public int SourceBomRevision { get; set; }
        public int? WbsTemplateId { get; set; }
        public int WbsTemplateVersion { get; set; }
        public int Count { get; set; }
        public DateTimeOffset CopiedAt { get; set; }
        public bool PcBaselineCurrent { get; set; }
        public List<PbProjectLineDto> Lines { get; set; } = new List<PbProjectLineDto>();
        public List<PbOpcListDto> Routings { get; set; } = new List<PbOpcListDto>();

        /// <summary>The project-control WBS is the only project WBS: these are its rows (nodes and activities) generated from the template for this work order.</summary>
        public int? PcProductNodeId { get; set; }
        public int? ChangeRequestId { get; set; }
        public List<PbPcWbsRowDto> PcWbs { get; set; } = new List<PbPcWbsRowDto>();
        public int PcDependencies { get; set; }

        /// <summary>Why the project-control WBS was not generated (contract review not accepted, no approved template ...); empty when it was.</summary>
        public string WbsNote { get; set; }

        public List<PbChangeDto> Changes { get; set; } = new List<PbChangeDto>();
    }

    public class SavePbChangeDto
    {
        public int? Id { get; set; }
        public int BaselineId { get; set; }
        public PbChangeKind Kind { get; set; }
        public int? TargetLineId { get; set; }
        public string PartNumber { get; set; }
        public string PartName { get; set; }
        public decimal? NewQty { get; set; }
        public string Unit { get; set; }
        public string Material { get; set; }
        public PbMakeOrBuy? MakeOrBuy { get; set; }
        public string Reason { get; set; }
    }

    public class PbChangeActionDto
    {
        public int Id { get; set; }
        public string Note { get; set; }

        /// <summary>The approved change request of project control (needed to apply a deviation once the PC baseline of the contract is current).</summary>
        public int? ChangeRequestId { get; set; }
    }
}
