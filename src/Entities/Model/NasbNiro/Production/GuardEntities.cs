using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Production
{
    // Production release P2 part E: the guarded machining process. What the machine and the measuring devices report, the post processors, the CAM tool lists and the
    // one-time overrides of the production manager. Numbers are stored; never renumber.

    public enum MfActivityKind
    {
        ExecMode = 1,
        ProgramState = 2,
        Override = 3,
        SelectedProgram = 4,
        Tool = 5,
        ProgramVerified = 6,
        ProgramMismatch = 7,
        ToolsVerified = 8,
        ToolsMismatch = 9,
        Measurement = 10,
        ProveOut = 11,
        Interlock = 12,
        GateRefused = 13,
        GateOverride = 14,
        LinkLost = 15,
        LinkBack = 16,
        UnapprovedProgram = 17,
    }

    /// <summary>One thing the machine did or the guard decided, on the time line of the machine (and of the operation when one was booked).</summary>
    public class MfMachineActivity
    {
        public long Id { get; set; }
        public int MachineId { get; set; }
        public int? OperationId { get; set; }
        public int? EmployeeId { get; set; }
        public DateTime At { get; set; }
        public MfActivityKind Kind { get; set; }

        /// <summary>The value in machine terms (e.g. "Automatic", "50", "1234.H").</summary>
        public string Value { get; set; }

        /// <summary>Persian.</summary>
        public string Text { get; set; }
    }

    /// <summary>The production manager lets one start (or one finish) through a gate, with a written reason; used once, then gone.</summary>
    public class MfGateOverride : BaseEntity
    {
        public int MachineId { get; set; }
        public int OperationId { get; set; }

        /// <summary>program, tools, prove-out, interlock, measurement.</summary>
        public string Gate { get; set; }

        public string Reason { get; set; }
        public int ByUserId { get; set; }
        public DateTimeOffset At { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? ConsumedAt { get; set; }
    }

    public enum MfPostStatus { Draft = 0, Submitted = 1, Approved = 2, Retired = 3, Rejected = 4 }

    /// <summary>A post processor file of a CAM system for one controller family (and optionally one machine), with a version and two-person approval.</summary>
    public class MfPostProcessor : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string CamSystem { get; set; }
        public MfNcControllerFamily ControllerFamily { get; set; }

        /// <summary>Null: any machine of the family.</summary>
        public int? MachineId { get; set; }

        public int Version { get; set; }
        public string FileName { get; set; }
        public string Sha256 { get; set; }
        public int SizeBytes { get; set; }
        public byte[] Content { get; set; }
        public MfPostStatus Status { get; set; }
        public string ChangeNote { get; set; }
        public int? SubmittedByUserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public string RejectReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum MfNcAttachmentKind { Simulation = 1, SetupSheet = 2 }

    /// <summary>A file kept with a program revision: the simulation report or the setup sheet.</summary>
    public class MfNcAttachment
    {
        public int Id { get; set; }
        public int RevisionId { get; set; }
        public MfNcAttachmentKind Kind { get; set; }
        public string FileName { get; set; }
        public string Sha256 { get; set; }
        public int SizeBytes { get; set; }
        public byte[] Content { get; set; }
        public int UploadedByUserId { get; set; }
        public DateTimeOffset UploadedAt { get; set; }
    }

    /// <summary>One tool of the CAM program (from the tool list the CAM system exports).</summary>
    public class MfCamTool
    {
        public int Id { get; set; }
        public int RevisionId { get; set; }
        public int ToolNumber { get; set; }
        public string Name { get; set; }
        public decimal? Diameter { get; set; }

        /// <summary>The length the program assumes (gauge length); null: not checked.</summary>
        public decimal? Length { get; set; }
    }

    public enum MfMeasurementSource { Probe = 1, Gauge = 2 }

    /// <summary>A value a measuring device reported (machine probe or digital gauge). Typed values are never stored here.</summary>
    public class MfMeasurement
    {
        public long Id { get; set; }
        public int? OperationId { get; set; }
        public int? MachineId { get; set; }
        public int? StationId { get; set; }
        public string Characteristic { get; set; }
        public decimal Value { get; set; }
        public MfMeasurementSource Source { get; set; }
        public string Device { get; set; }
        public DateTime At { get; set; }
    }

    /// <summary>A gauge interface (a box or a PC with the gauges' receiver) that pushes measurements with its own key.</summary>
    public class MfMeasureStation : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>SHA-256 of the station key (the key is shown once).</summary>
        public string KeyHash { get; set; }

        public string AllowedAddresses { get; set; }
        public int? MachineId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset? LastSeenAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }
}
