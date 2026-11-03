using System;
using System.ComponentModel.DataAnnotations;
using Entities.Model.NasbNiro.Contracts;

namespace Entities.Model.NasbNiro.ProjectControl
{
    // Project control, remediation R1c (migration ProjectControlR1): new tables. The columns added to the older tables are marked "R1c" in their own files.

    /// <summary>
    /// H4: one dated observation of the physical percent of an activity (append only). Earned value at a data date is read from these rows
    /// (the latest event on or before the date), never from the module data of today.
    /// </summary>
    public class PcProgressEvent
    {
        public int Id { get; set; }
        public int ContractId { get; set; }
        public int ActivityId { get; set; }

        /// <summary>The day the percent is valid from (never in the future).</summary>
        public DateTime EventDate { get; set; }

        public decimal Percent { get; set; }
        public DateTimeOffset RecordedAt { get; set; }

        /// <summary>0 = the progress engine / the server job.</summary>
        public int RecordedByUserId { get; set; }

        /// <summary>engine, manual or backfill.</summary>
        public string Source { get; set; }
    }

    public enum PcSnapshotRequestState { Requested = 0, Approved = 1, Rejected = 2, Used = 3 }

    /// <summary>
    /// H4: a snapshot with a data date in the past is taken only after the PMO approved a request of the planning unit (with the reason).
    /// The approval is used once; the snapshot it produced is stored on the request.
    /// </summary>
    public class PcSnapshotRequest : BaseEntity
    {
        public int ContractId { get; set; }
        public ContractEntity Contract { get; set; }
        public DateTime DataDate { get; set; }
        public string Reason { get; set; }
        public PcSnapshotRequestState State { get; set; }
        public int? DecidedByUserId { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string DecisionNote { get; set; }
        public int? SnapshotId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>M8: what is on a shipment (item, serial unit, quantity). A work order counts as delivered when the delivered lines add up to its ordered quantity.</summary>
    public class PcShipmentLine
    {
        public int Id { get; set; }
        public int ShipmentId { get; set; }
        public PcShipment Shipment { get; set; }
        public int? ItemId { get; set; }
        public int? SerialUnitId { get; set; }
        public decimal Quantity { get; set; }
        public string Description { get; set; }
    }

    /// <summary>M5: the second-person check of a cost entry written by hand in project control; an unchecked manual entry does not count as actual cost.</summary>
    public class PcCostCheck
    {
        public int Id { get; set; }
        public int CostEntryId { get; set; }
        public int CheckedByUserId { get; set; }
        public DateTimeOffset CheckedAt { get; set; }
        public string Note { get; set; }
    }
}
