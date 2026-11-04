using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.ProjectBalance;

namespace ViewModels.NasbNiro.ProjectBalanceDtos
{
    /// <summary>A project (work order with a BOM baseline) in the picker of the balance screen.</summary>
    public class BalanceProjectDto
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public string WorkOrderTitle { get; set; }
        public int ContractId { get; set; }
        public string ContractCode { get; set; }
        public int BaselineId { get; set; }
        public string BomCode { get; set; }
        public int BomRevision { get; set; }
        public int Count { get; set; }
        public int? LatestRunId { get; set; }
        public int? LatestRunNumber { get; set; }
        public DateTimeOffset? LatestRunAt { get; set; }
        public int LineCount { get; set; }
        public int ShortLineCount { get; set; }
        public int ReservationCount { get; set; }
        public int DraftRequestCount { get; set; }
    }

    public class BalanceRunRequestDto
    {
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }

        /// <summary>Also create / update the draft purchase requests of the shortage (the "drafts" button does the same on the latest run).</summary>
        public bool CreateDrafts { get; set; }
    }

    public class BalanceRunListItemDto
    {
        public int Id { get; set; }
        public int RunNumber { get; set; }
        public DateTimeOffset RunAt { get; set; }
        public int RunByUserId { get; set; }
        public string RunByName { get; set; }
        public string Trigger { get; set; }
        public string BomCode { get; set; }
        public int BomRevision { get; set; }
        public int LineCount { get; set; }
        public int ShortLineCount { get; set; }
        public int NotCodedCount { get; set; }
    }

    public class BalanceLineDto
    {
        public int Id { get; set; }
        public int SortOrder { get; set; }
        public BalanceRequirementKind Kind { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string PartNumber { get; set; }
        public string PartName { get; set; }
        public string Unit { get; set; }
        public int MakeOrBuy { get; set; }
        public int? WbsNodeId { get; set; }
        public string WbsNodeCode { get; set; }
        public string WbsNodeTitle { get; set; }
        public int? ActivityId { get; set; }
        public string ActivityCode { get; set; }
        public DateTime? NeedDate { get; set; }
        public DateTime? UseByDate { get; set; }
        public int LeadTimeDays { get; set; }
        public int? TotalFloat { get; set; }
        public int Priority { get; set; }
        public decimal GrossQty { get; set; }
        public decimal ScrapQty { get; set; }
        public decimal RequiredQty { get; set; }
        public decimal IssuedQty { get; set; }
        public decimal ReservedQty { get; set; }
        public decimal ReservedNewQty { get; set; }
        public decimal OnHandQty { get; set; }
        public decimal AvailableQty { get; set; }
        public decimal OnOrderPrQty { get; set; }
        public decimal OnOrderPoQty { get; set; }
        public decimal SubstituteQty { get; set; }
        public int? SubstituteItemId { get; set; }

        /// <summary>The BOM unit differs from the unit of the item and no conversion is defined: check the quantity by hand.</summary>
        public bool UnitMismatch { get; set; }
        public string SubstituteItemCode { get; set; }
        public decimal ShortQty { get; set; }
        public int? PurchaseRequestId { get; set; }
        public string PurchaseRequestNumber { get; set; }
        public int? PurchaseRequestLineId { get; set; }
        public string Note { get; set; }
    }

    public class BalanceRunDto
    {
        public int Id { get; set; }
        public int RunNumber { get; set; }
        public int ContractId { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int BaselineId { get; set; }
        public int BomHeaderId { get; set; }
        public string BomCode { get; set; }
        public int BomRevision { get; set; }
        public DateTimeOffset RunAt { get; set; }
        public int RunByUserId { get; set; }
        public string RunByName { get; set; }
        public string Trigger { get; set; }
        public string SettingsJson { get; set; }
        public int? PreviousRunId { get; set; }
        public int LineCount { get; set; }
        public int ShortLineCount { get; set; }
        public int NotCodedCount { get; set; }
        public string Note { get; set; }

        /// <summary>True when this is the newest run of its baseline: only then the draft purchase requests can be made from it.</summary>
        public bool IsLatest { get; set; }

        public List<BalanceLineDto> Lines { get; set; } = new List<BalanceLineDto>();
    }

    public class BalanceRunBatchDto
    {
        public List<BalanceRunDto> Runs { get; set; } = new List<BalanceRunDto>();
        public List<BalanceDraftsDto> Drafts { get; set; } = new List<BalanceDraftsDto>();
        public List<string> Notes { get; set; } = new List<string>();
    }

    public class BalanceDiffRowDto
    {
        /// <summary>Added, Removed or Changed.</summary>
        public string Change { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string PartNumber { get; set; }
        public string PartName { get; set; }
        public string Unit { get; set; }
        public decimal? OldRequired { get; set; }
        public decimal? NewRequired { get; set; }
        public decimal? OldShort { get; set; }
        public decimal? NewShort { get; set; }
        public decimal? OldReserved { get; set; }
        public decimal? NewReserved { get; set; }
    }

    public class BalanceDiffDto
    {
        public int FromRunId { get; set; }
        public int ToRunId { get; set; }
        public int FromRunNumber { get; set; }
        public int ToRunNumber { get; set; }
        public int Added { get; set; }
        public int Removed { get; set; }
        public int Changed { get; set; }
        public List<BalanceDiffRowDto> Rows { get; set; } = new List<BalanceDiffRowDto>();
    }

    public class BalanceDraftRequestDto
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public string GroupKey { get; set; }
        public bool Created { get; set; }
        public int LineCount { get; set; }
        public int LinkedDocuments { get; set; }
        public int Priority { get; set; }
    }

    public class BalanceDraftsDto
    {
        public int RunId { get; set; }
        public int WorkOrderId { get; set; }
        public List<BalanceDraftRequestDto> Requests { get; set; } = new List<BalanceDraftRequestDto>();
        public int Created { get; set; }
        public int Updated { get; set; }
        public int RemovedEmpty { get; set; }
        public List<string> Notes { get; set; } = new List<string>();
    }

    public class NodeMaterialDto
    {
        public int NodeId { get; set; }
        public string NodeCode { get; set; }
        public string NodeTitle { get; set; }
        public int? RunId { get; set; }
        public int? RunNumber { get; set; }
        public DateTimeOffset? RunAt { get; set; }
        public int LineCount { get; set; }
        public int ShortLineCount { get; set; }
        public List<BalanceLineDto> Lines { get; set; } = new List<BalanceLineDto>();
    }

    // ------------------------------------------------------------------------------------------ reservations

    public class ReservationDto
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public string ContractCode { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public int BaselineId { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal IssuedQuantity { get; set; }
        public decimal RemainingQuantity { get; set; }
        public ReservationStatus Status { get; set; }
        public bool IsHard { get; set; }
        public int? WarehouseId { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
        public int? SerialUnitId { get; set; }
        public DateTime? NeedDate { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }
        public int? SubstituteForItemId { get; set; }
        public string SubstituteForItemCode { get; set; }
        public string Reason { get; set; }
        public int? ReleaseRequestedByUserId { get; set; }
        public string ReleaseRequestedByName { get; set; }
        public DateTimeOffset? ReleaseRequestedAt { get; set; }
        public string ReleaseReason { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public decimal ReleasedQuantity { get; set; }
        public DateTimeOffset CreateDate { get; set; }

        /// <summary>What the caller may do with it now (by role and by the two-person rule).</summary>
        public bool CanEdit { get; set; }
        public bool CanRequestRelease { get; set; }
        public bool CanApproveRelease { get; set; }

        /// <summary>The reservation was released: the balance does not reserve this item for this project again until a planner enables it.</summary>
        public bool AutoReserveBlocked { get; set; }
        public string ReEnableReason { get; set; }
        public bool CanReEnable { get; set; }
    }

    public class ReservationQuery
    {
        public int? WorkOrderId { get; set; }
        public int? ContractId { get; set; }
        public int? ItemId { get; set; }
        public ReservationStatus? Status { get; set; }
        public string Contains { get; set; }
    }

    public class UpdateReservationDto
    {
        public int Id { get; set; }
        public decimal? Quantity { get; set; }
        public bool? IsHard { get; set; }
        public int? WarehouseId { get; set; }
        public int? LotId { get; set; }
        public int? SerialUnitId { get; set; }
        public DateTime? NeedDate { get; set; }

        /// <summary>Required for every change: the audit trail keeps it.</summary>
        public string Reason { get; set; }
    }

    public class ReservationActionDto
    {
        public int Id { get; set; }
        public string Reason { get; set; }

        /// <summary>Release approval: true approves, false sends the request back.</summary>
        public bool Approve { get; set; }
    }

    // ------------------------------------------------------------------------------------------ substitutes

    public class SubstituteDto
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public string ItemUnit { get; set; }
        public int SubstituteItemId { get; set; }
        public string SubstituteItemCode { get; set; }
        public string SubstituteItemTitle { get; set; }
        public string SubstituteItemUnit { get; set; }
        public decimal Ratio { get; set; }
        public SubstituteScope Scope { get; set; }
        public int? ContractId { get; set; }
        public string ContractCode { get; set; }
        public int? WorkOrderId { get; set; }
        public string WorkOrderCode { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string Reason { get; set; }
        public SubstituteStatus Status { get; set; }
        public int ProposedByUserId { get; set; }
        public string ProposedByName { get; set; }
        public int? DecidedByUserId { get; set; }
        public string DecidedByName { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
        public bool CanDecide { get; set; }
        public bool CanEdit { get; set; }
        public bool CanRevoke { get; set; }
    }

    public class SaveSubstituteDto
    {
        public int? Id { get; set; }
        public int ItemId { get; set; }
        public int SubstituteItemId { get; set; }
        public decimal Ratio { get; set; }
        public SubstituteScope Scope { get; set; }
        public int? WorkOrderId { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string Reason { get; set; }
    }

    public class SubstituteDecisionDto
    {
        public int Id { get; set; }

        /// <summary>approve, reject or revoke.</summary>
        public string Action { get; set; }
        public string Note { get; set; }
    }

    public class SubstituteQuery
    {
        public int? ItemId { get; set; }
        public SubstituteStatus? Status { get; set; }
    }

    // ------------------------------------------------------------------------------------------ settings and jobs

    public class BalanceSettingDto
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public string Title { get; set; }
        public string Source { get; set; }
    }

    public class SaveBalanceSettingDto
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public class KitRunResultDto
    {
        public int Activities { get; set; }
        /// <summary>Kits drafted for an activity whose date had already passed.</summary>
        public int Late { get; set; }
        public int Drafted { get; set; }
        public int Skipped { get; set; }
        public List<string> Requests { get; set; } = new List<string>();
        public List<string> Notes { get; set; } = new List<string>();
    }
}
