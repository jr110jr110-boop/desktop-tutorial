using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Constract;
using Entities.Model.NasbNiro.Contracts;

namespace Entities.Model.NasbNiro.Inventory
{
    /// <summary>
    /// A manufacturing stage defined for an item or a whole item group (for example cutting, machining,
    /// heat treatment, NDT, balancing). Applied to a serial or lot it becomes a <see cref="PartOperation"/>.
    /// </summary>
    public class OperationTemplate : BaseEntity
    {
        public int? ItemGroupId { get; set; }
        public ItemGroup ItemGroup { get; set; }

        public int? ItemId { get; set; }
        public InventoryItem Item { get; set; }

        public int Sequence { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool RequiresQc { get; set; }

        public int? MachineId { get; set; }
        public Machine Machine { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>A manufacturing stage carried out on one serial unit or one lot.</summary>
    public class PartOperation : BaseEntity
    {
        public int? SerialUnitId { get; set; }
        public SerialUnit SerialUnit { get; set; }

        public int? LotId { get; set; }
        public InventoryLot Lot { get; set; }

        public int? OperationTemplateId { get; set; }
        public OperationTemplate OperationTemplate { get; set; }

        public int Sequence { get; set; }
        public string Title { get; set; }
        public PartOperationStatus Status { get; set; }

        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }

        public string OperatorName { get; set; }
        public int? OperatorUserId { get; set; }

        public int? MachineId { get; set; }
        public Machine Machine { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        /// <summary>Measured values or the result of the stage.</summary>
        public string Result { get; set; }

        public QcStatus QcStatus { get; set; }
        public int? QcByUserId { get; set; }
        public DateTimeOffset? QcAt { get; set; }
        public string QcNote { get; set; }

        public string Notes { get; set; }

        public ICollection<PartOperationFile> Files { get; set; }
    }

    /// <summary>A file attached to an operation: report, photo, NDT result.</summary>
    public class PartOperationFile
    {
        public int Id { get; set; }

        public int PartOperationId { get; set; }
        public PartOperation PartOperation { get; set; }

        /// <summary>Uploaded through api/v1/Documents.</summary>
        public Guid DocumentId { get; set; }

        public string Title { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int CreatedByUserId { get; set; }
    }

    /// <summary>Which piece (or how much of a lot) went into which assembly, and when.</summary>
    public class AssemblyLink : BaseEntity
    {
        public int ParentSerialUnitId { get; set; }
        public SerialUnit ParentSerialUnit { get; set; }

        public int? ChildSerialUnitId { get; set; }
        public SerialUnit ChildSerialUnit { get; set; }

        // For lot-tracked components (for example bolts of one lot)
        public int? ChildLotId { get; set; }
        public InventoryLot ChildLot { get; set; }
        public decimal Quantity { get; set; }

        public DateTimeOffset InstalledAt { get; set; }
        public int InstalledByUserId { get; set; }
        public DateTimeOffset? RemovedAt { get; set; }
        public int? RemovedByUserId { get; set; }

        public int? WorkOrderId { get; set; }
        public WorkOrder WorkOrder { get; set; }

        public string Notes { get; set; }
    }
}
