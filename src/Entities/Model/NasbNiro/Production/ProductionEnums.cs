namespace Entities.Model.NasbNiro.Production
{
    // Production / shop floor (MES), release P1 part A. Tables start with "Mf". Numbers are stored; never renumber.

    public enum MfWorkCenterKind { Machine = 1, ManualStation = 2, Welding = 3, AssemblyBay = 4, TestStand = 5, Painting = 6, Outside = 7 }

    public enum MfMachineStatus { Available = 1, Down = 2, UnderMaintenance = 3, Retired = 4 }

    /// <summary>What the operation needs before it counts as done: nothing, the operator's own check, a QC inspector, or an ITP point of the work order.</summary>
    public enum MfInspectionRequirement { None = 0, Self = 1, Qc = 2, ItpPoint = 3 }

    public enum MfRoutingStatus { Draft = 0, Approved = 1 }

    public enum MfOrderStatus { Draft = 0, Released = 1, InProgress = 2, Completed = 3, Closed = 4, Cancelled = 5 }

    public enum MfOperationStatus { Waiting = 0, Ready = 1, InProgress = 2, Paused = 3, Done = 4, QcHold = 5, Rejected = 6, Skipped = 7 }

    public enum MfBookingKind { Setup = 1, Run = 2, Rework = 3, Waiting = 4, Cleaning = 5 }

    public enum MfBookingSource { Terminal = 1, Supervisor = 2 }

    public enum MfReasonKind { Scrap = 1, Downtime = 2, Waiting = 3 }

    public enum MfMaintenanceRequestStatus { Open = 0, InProgress = 1, Done = 2, Cancelled = 3 }
}
