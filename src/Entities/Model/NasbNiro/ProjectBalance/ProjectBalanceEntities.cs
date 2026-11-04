using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.ProjectBalance
{
    // Package PB2: the automatic material balance of a project. The released product BOM copied into the project (PbProjectBomLine, with the approved deviations) is exploded into a requirement per item,
    // netted against the company stock, the reservations, the open requests and the open orders, and the shortage becomes draft purchase requests. Nothing here approves or submits anything.

    /// <summary>What a balance line asks for.</summary>
    public enum BalanceRequirementKind
    {
        /// <summary>A bought part (PL): the item of the part itself.</summary>
        Purchased = 1,

        /// <summary>A made part (MPL, subcontract): its raw material (the first stockable stage of the OPC chart, or the one raw item of the BOM material).</summary>
        RawOfMake = 2,

        /// <summary>The part (or its raw material) has no item code yet: it is listed and flagged, never ordered.</summary>
        NotCoded = 3,
    }

    public enum ReservationStatus
    {
        Active = 1,
        /// <summary>An approved release gave the stock back.</summary>
        Released = 2,
        /// <summary>Everything reserved has been issued to the project.</summary>
        FullyIssued = 3,
    }

    public enum SubstituteScope { Global = 1, Project = 2 }

    public enum SubstituteStatus
    {
        /// <summary>Proposed: waits for ENGINEERING (never the proposer).</summary>
        Proposed = 0,
        Approved = 1,
        Rejected = 2,
        Revoked = 3,
    }

    /// <summary>One run of the balance of one project baseline (work order). Re-runnable: every run is kept so two runs can be compared.</summary>
    public class ProjectMaterialBalance : BaseEntity
    {
        public int ContractId { get; set; }
        public int WorkOrderId { get; set; }

        /// <summary>The project BOM baseline (PbProjectBaseline) the run exploded.</summary>
        public int BaselineId { get; set; }

        public int BomHeaderId { get; set; }
        public string BomCode { get; set; }
        public int BomRevision { get; set; }

        /// <summary>1, 2, 3 ... per baseline.</summary>
        public int RunNumber { get; set; }

        public DateTimeOffset RunAt { get; set; }

        /// <summary>The person who ran it; 0 = a job.</summary>
        public int RunByUserId { get; set; }

        /// <summary>Manual, Instantiate, Deviation, WbsGenerated, Nightly.</summary>
        public string Trigger { get; set; }

        /// <summary>The settings the run used (JSON), so the figures can be explained later.</summary>
        public string SettingsJson { get; set; }

        /// <summary>SHA-256 of the figures of the lines; a nightly run with the same hash as the run before is not stored again.</summary>
        public string ContentHash { get; set; }

        public int? PreviousRunId { get; set; }
        public int LineCount { get; set; }
        public int ShortLineCount { get; set; }
        public int NotCodedCount { get; set; }
        public string Note { get; set; }
    }

    /// <summary>One item of one part (WBS node / procurement activity) of a run.</summary>
    public class ProjectMaterialBalanceLine : BaseEntity
    {
        public int BalanceId { get; set; }
        public int SortOrder { get; set; }
        public BalanceRequirementKind Kind { get; set; }

        public int? ItemId { get; set; }
        public string PartNumber { get; set; }
        public string PartName { get; set; }
        public string Unit { get; set; }

        /// <summary>1 MPL make, 2 PL purchase, 4 subcontract (PbMakeOrBuy).</summary>
        public int MakeOrBuy { get; set; }

        /// <summary>The part node of the project-control WBS and its procurement (P) activity.</summary>
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }

        /// <summary>The date the order must be placed by: the start of the construction (C) activity less the lead time.</summary>
        public DateTime? NeedDate { get; set; }

        /// <summary>The start of the construction (C) activity: the material must be in the plant by then.</summary>
        public DateTime? UseByDate { get; set; }

        public int LeadTimeDays { get; set; }
        public int? TotalFloat { get; set; }

        /// <summary>0 low, 1 normal, 2 high, 3 urgent (from the float of the procurement activity).</summary>
        public int Priority { get; set; }

        public decimal GrossQty { get; set; }
        public decimal ScrapQty { get; set; }
        public decimal RequiredQty { get; set; }

        /// <summary>Already issued to the work order (confirmed issues less returns).</summary>
        public decimal IssuedQty { get; set; }

        /// <summary>Held for this project after the run (the reservation it had plus what the run added).</summary>
        public decimal ReservedQty { get; set; }
        public decimal ReservedNewQty { get; set; }

        /// <summary>The company stock of the item in the warehouses that count, and what is free of other claims (same figure on every line of the item).</summary>
        public decimal OnHandQty { get; set; }
        public decimal AvailableQty { get; set; }

        public decimal OnOrderPrQty { get; set; }
        public decimal OnOrderPoQty { get; set; }

        /// <summary>Quantity of the substitute item (units of the substitute) that covers part of the shortage.</summary>
        public decimal SubstituteQty { get; set; }
        public int? SubstituteItemId { get; set; }

        /// <summary>The unit of the BOM line differs from the unit of the item and no conversion is defined: the quantity is not converted and the line must be checked by a person.</summary>
        public bool UnitMismatch { get; set; }

        public decimal ShortQty { get; set; }

        /// <summary>The draft purchase request and its line that buy the shortage (set when the drafts are made).</summary>
        public int? PurchaseRequestId { get; set; }
        public int? PurchaseRequestLineId { get; set; }

        public string Note { get; set; }
    }

    /// <summary>Stock held for one project: a hard allocation (nobody else may issue it) or a soft one (planning counts it as taken).</summary>
    public class ProjectReservation : BaseEntity
    {
        public int ContractId { get; set; }
        public int WorkOrderId { get; set; }
        public int BaselineId { get; set; }
        public int ItemId { get; set; }

        public decimal Quantity { get; set; }
        public decimal IssuedQuantity { get; set; }
        public ReservationStatus Status { get; set; }
        public bool IsHard { get; set; }

        public int? WarehouseId { get; set; }
        public int? LotId { get; set; }
        public int? SerialUnitId { get; set; }

        public DateTime? NeedDate { get; set; }
        public int? WbsNodeId { get; set; }
        public int? ActivityId { get; set; }

        /// <summary>When the reserved item is a substitute: the primary item whose shortage it covers.</summary>
        public int? SubstituteForItemId { get; set; }

        public int? SourceBalanceId { get; set; }
        public string Reason { get; set; }

        /// <summary>Release is a decision of two people: PLANNING asks (with a reason), another PLANNING / PMO approves.</summary>
        public int? ReleaseRequestedByUserId { get; set; }
        public DateTimeOffset? ReleaseRequestedAt { get; set; }
        public string ReleaseReason { get; set; }
        public int? ReleasedByUserId { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public decimal ReleasedQuantity { get; set; }

        /// <summary>PB2 (M6): set when a release is approved: the run does not reserve this item for this project again until a planner re-enables it with a reason.</summary>
        public bool AutoReserveBlocked { get; set; }
        public int? ReEnabledByUserId { get; set; }
        public DateTimeOffset? ReEnabledAt { get; set; }
        public string ReEnableReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>An item that may stand in for another one (ISO 10007: interchangeability is controlled and approved).</summary>
    public class ItemSubstitute : BaseEntity
    {
        public int ItemId { get; set; }
        public int SubstituteItemId { get; set; }

        /// <summary>Units of the substitute that replace ONE unit of the item.</summary>
        public decimal Ratio { get; set; }

        public SubstituteScope Scope { get; set; }
        public int? ContractId { get; set; }
        public int? WorkOrderId { get; set; }

        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string Reason { get; set; }

        public SubstituteStatus Status { get; set; }
        public int ProposedByUserId { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
    }
}
