using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Production
{
    // Production release P2 part A: DNC (the CNC program library, the controlled transfer to the machines and the record of what ran). Tables start with "MfNc" / "MfMachine". Numbers are stored; never renumber.

    /// <summary>The control family of a machine; the program checks and the program name on the control depend on it.</summary>
    public enum MfNcControllerFamily { Unknown = 0, Fanuc = 1, Siemens = 2, HeidenhainConversational = 3, HeidenhainIso = 4, Mitsubishi = 5, Haas = 6, Mazak = 7, Okuma = 8, Fagor = 9, Philips = 10, Other = 99 }

    /// <summary>How a program reaches the control.</summary>
    public enum MfNcLinkKind
    {
        /// <summary>The operator downloads the file and loads it by hand (USB stick, card); the download counts as the delivery.</summary>
        Manual = 1,

        /// <summary>The server writes the file into a folder the control (or its PC) reads, for example a UNC share.</summary>
        NetworkFolder = 2,

        /// <summary>The server stores the file on the FTP server of the control.</summary>
        Ftp = 3,

        /// <summary>A serial-to-Ethernet converter in TCP server mode (RS-232 / RS-422 of the control): the server streams the program at the baud rate of the control.</summary>
        SerialGateway = 4,

        /// <summary>An edge box at the machine fetches its jobs from the server with its own key (pull; the server never connects to it).</summary>
        EdgeGateway = 5,
    }

    public enum MfNcLineEnding { CrLf = 1, Lf = 2, Cr = 3 }

    public enum MfNcProgramStatus { Active = 1, Obsolete = 2 }

    public enum MfNcRevisionStatus { Draft = 0, Submitted = 1, Approved = 2, Superseded = 3, Rejected = 4, Withdrawn = 5 }

    public enum MfNcRevisionSource { Upload = 1, FromMachine = 2 }

    public enum MfNcTransferDirection { ToMachine = 1, FromMachine = 2 }

    public enum MfNcTransferStatus { Queued = 0, Sending = 1, Delivered = 2, Confirmed = 3, Failed = 4, Cancelled = 5, Received = 6 }

    /// <summary>How a machine talks to the DNC: one row per machine (1:1 side table of the legacy Machine, like <see cref="MfMachineInfo"/>).</summary>
    public class MfMachineConnection : BaseEntity
    {
        public int MachineId { get; set; }
        public MfNcControllerFamily ControllerFamily { get; set; }

        /// <summary>For example "TNC 426/430" or "Fanuc 18i-T".</summary>
        public string ControllerModel { get; set; }

        public MfNcLinkKind LinkKind { get; set; } = MfNcLinkKind.Manual;

        /// <summary>Host name or LAN address of the control, of the serial converter or of the folder server (NetworkFolder uses <see cref="RemotePath"/> only).</summary>
        public string Host { get; set; }

        public int? Port { get; set; }

        /// <summary>Folder on the FTP server or the UNC folder.</summary>
        public string RemotePath { get; set; }

        /// <summary>Name of the credential in the server settings (Dnc:Credentials:&lt;key&gt;:User / Password). The secret itself is never stored in the database.</summary>
        public string CredentialKey { get; set; }

        /// <summary>The program name on the control; "{number}" is the program number of the file, "{code}" the program code. Empty: the original file name.</summary>
        public string ProgramNameFormat { get; set; }

        /// <summary>For example ".H", ".I", ".NC" or empty.</summary>
        public string FileExtension { get; set; }

        public MfNcLineEnding LineEnding { get; set; } = MfNcLineEnding.CrLf;

        /// <summary>The program must start and end with "%" (Fanuc / ISO tape format).</summary>
        public bool RequirePercent { get; set; }

        /// <summary>The largest program the control takes, in bytes (null: no limit of the machine; the global limit still applies).</summary>
        public int? MaxProgramBytes { get; set; }

        /// <summary>The axis letters the machine has, for example "XYZ" or "XZ" (empty: not checked).</summary>
        public string Axes { get; set; }

        public int? MaxSpindleRpm { get; set; }
        public int? MaxFeedMmPerMin { get; set; }
        public int? ToolCapacity { get; set; }

        /// <summary>Lathe tool words (T0101: the first two digits are the tool).</summary>
        public bool IsLathe { get; set; }

        /// <summary>Baud rate of the serial line behind a converter; the stream is paced to it (null: 9600).</summary>
        public int? BaudRate { get; set; }

        /// <summary>
        /// ★ provisional decision: true. An operation whose routing step has an active program does not start (run or setup) on this machine before the current approved revision was sent to it and the operator confirmed it on the control.
        /// </summary>
        public bool RequireConfirmedProgram { get; set; } = true;

        /// <summary>SHA-256 (hex) of the key of an edge gateway; the key is shown once.</summary>
        public string GatewayKeyHash { get; set; }

        public DateTimeOffset? GatewayKeyRotatedAt { get; set; }
        public int? GatewayKeyRotatedByUserId { get; set; }

        /// <summary>LAN addresses the edge gateway may call from (IPv4 or IPv4/CIDR, comma separated). Empty: any address.</summary>
        public string AllowedAddresses { get; set; }

        public DateTimeOffset? LastSeenAt { get; set; }
        public bool IsActive { get; set; } = true;
        public string Note { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A CNC program of the library: what it makes (item, routing step) and where it may run (work centre, machine, control family).</summary>
    public class MfNcProgram : BaseEntity
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ItemId { get; set; }

        /// <summary>The routing step (OperationTemplate) the program machines; the terminal finds the program of an operation by it.</summary>
        public int? OperationTemplateId { get; set; }

        public int? WorkCenterId { get; set; }

        /// <summary>The only machine the program may go to (null: any machine of the work centre with the same control family).</summary>
        public int? MachineId { get; set; }

        public MfNcControllerFamily ControllerFamily { get; set; }

        /// <summary>The approved revision that is sent to the machines.</summary>
        public int? CurrentRevisionId { get; set; }

        public MfNcProgramStatus Status { get; set; } = MfNcProgramStatus.Active;
        public string Note { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>A revision of a program. The content is fixed when the revision is created (SHA-256); a change is a new revision. Approved by somebody other than its author.</summary>
    public class MfNcRevision : BaseEntity
    {
        public int ProgramId { get; set; }
        public int Revision { get; set; }
        public MfNcRevisionStatus Status { get; set; }
        public string FileName { get; set; }
        public int SizeBytes { get; set; }
        public int LineCount { get; set; }

        /// <summary>SHA-256 of the content (hex, upper case).</summary>
        public string Sha256 { get; set; }

        /// <summary>The program number or name found in the file (O1234, BEGIN PGM name).</summary>
        public string ProgramNumber { get; set; }

        public MfNcRevisionSource Source { get; set; } = MfNcRevisionSource.Upload;

        /// <summary>The transfer that brought the content back from the machine.</summary>
        public int? FromTransferId { get; set; }

        /// <summary>The revision the difference was counted against (the current one when this revision was made).</summary>
        public int? BaseRevisionId { get; set; }

        public int DiffAdded { get; set; }
        public int DiffRemoved { get; set; }
        public string ChangeNote { get; set; }

        /// <summary>The findings of the last check, one per line ("E|W line code text").</summary>
        public string Findings { get; set; }

        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }

        public int? SubmittedByUserId { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public int? RejectedByUserId { get; set; }
        public DateTimeOffset? RejectedAt { get; set; }
        public string RejectReason { get; set; }

        // ------------------------------------------------------------------ P2 part E: what the revision was made with and how it was proven

        /// <summary>The approved post processor the program was made with.</summary>
        public int? PostProcessorId { get; set; }

        /// <summary>The CAM system (PowerMill, Mastercam, NX ...), as the programmer named it.</summary>
        public string CamSystem { get; set; }

        /// <summary>The simulation report of the CAM system or of an independent G-code simulator (stored as an attachment).</summary>
        public int? SimulationAttachmentId { get; set; }

        /// <summary>The tools the CAM program uses (rows in MfCamTools).</summary>
        public int ToolCount { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>The bytes of a revision (kept apart so lists of revisions stay light).</summary>
    public class MfNcContent
    {
        public int Id { get; set; }
        public int RevisionId { get; set; }

        /// <summary>Lets the revision and its content be stored by one save (the key is filled by the save).</summary>
        public MfNcRevision Revision { get; set; }

        public byte[] Bytes { get; set; }
    }

    /// <summary>One transfer of a program to a machine, or of a program back from a machine.</summary>
    public class MfNcTransfer : BaseEntity
    {
        public MfNcTransferDirection Direction { get; set; }
        public int MachineId { get; set; }
        public int? ProgramId { get; set; }

        /// <summary>The revision sent (to the machine) or the revision made from what came back (from the machine).</summary>
        public int? RevisionId { get; set; }

        /// <summary>Lets a revision made from what came back and the transfer be stored by one save.</summary>
        public MfNcRevision Revision { get; set; }

        public int? OperationId { get; set; }

        /// <summary>The program name on the control.</summary>
        public string TargetName { get; set; }

        /// <summary>The link kind at the time of the request.</summary>
        public MfNcLinkKind LinkKind { get; set; }

        public MfNcTransferStatus Status { get; set; }
        public int Attempts { get; set; }
        public DateTimeOffset? NextAttemptAt { get; set; }
        public string LastError { get; set; }

        /// <summary>SHA-256 of exactly the bytes that went out (after the line endings of the machine).</summary>
        public string SentSha256 { get; set; }

        public int SentBytes { get; set; }

        /// <summary>Why the program is fetched from the machine (becomes the change note of the revision made from it).</summary>
        public string RequestNote { get; set; }

        public int RequestedByUserId { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public DateTimeOffset? DeliveredAt { get; set; }
        public int? ConfirmedByUserId { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public string ConfirmNote { get; set; }
        public int? CancelledByUserId { get; set; }
        public DateTimeOffset? CancelledAt { get; set; }
        public string CancelReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>What ran: the revision of a program that an operation started with on a machine (written when the operation starts on the terminal).</summary>
    public class MfNcRun
    {
        public int Id { get; set; }
        public int OperationId { get; set; }
        public int MachineId { get; set; }
        public int ProgramId { get; set; }
        public int RevisionId { get; set; }
        public int? TransferId { get; set; }
        public int? EmployeeId { get; set; }
        public MfBookingKind Kind { get; set; }
        public DateTime StartedAt { get; set; }
        public int RecordedByUserId { get; set; }

        // ------------------------------------------------------------------ P2 part E: what the machine itself confirmed at the start

        /// <summary>The fingerprint of the program read back from the control at the start (equal to the revision's, or the start was refused).</summary>
        public string VerifiedSha256 { get; set; }

        public DateTime? VerifiedAt { get; set; }

        /// <summary>The first run of this revision on this machine: it has to be proven (single block or reduced feed, seen by the data link).</summary>
        public bool ProveOut { get; set; }

        /// <summary>The data link saw the prove-out conditions during the run (null: not judged yet).</summary>
        public bool? ProveOutObserved { get; set; }

        /// <summary>The tool table of the control matched the CAM tool list at the start (null: no tool list or no link).</summary>
        public bool? ToolsVerified { get; set; }

        /// <summary>The one-time override of the production manager the start used (MfGateOverrides).</summary>
        public int? OverrideId { get; set; }
    }
}
