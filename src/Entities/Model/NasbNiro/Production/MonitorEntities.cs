using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model.NasbNiro.Production
{
    // Production release P2 part B: live machine monitoring. Numbers are stored; never renumber.

    /// <summary>Where the state of a machine comes from.</summary>
    public enum MfMonitorSource
    {
        None = 0,

        /// <summary>A network digital-input module (Modbus TCP) wired to the stack light / spindle relay / alarm relay of the machine; the server polls it.</summary>
        ModbusInputs = 1,

        /// <summary>An edge box at the machine pushes the state with the machine's gateway key (Elecio-like box).</summary>
        GatewayPush = 2,

        /// <summary>P2 part E: the state comes from the control itself through the data link (LSV2): running, stopped, error.</summary>
        ControlLink = 3,
    }

    /// <summary>P2 part E: the data link to the control itself.</summary>
    public enum MfDataLinkKind
    {
        None = 0,

        /// <summary>HEIDENHAIN LSV2 over TCP (port 19000): execution mode, program state, overrides, selected program, tool, PLC words and file read-back.</summary>
        Lsv2Tcp = 1,

        /// <summary>An agent or edge box at the machine (for example a FANUC FOCAS agent) pushes the same data with the machine's gateway key.</summary>
        GatewayPush = 2,
    }

    /// <summary>The operating mode the control reports.</summary>
    public enum MfExecMode { Unknown = 0, Manual = 1, Mdi = 2, SingleBlock = 3, Automatic = 4, Other = 5 }

    /// <summary>The state of the program on the control.</summary>
    public enum MfProgramState { Unknown = 0, Running = 1, Stopped = 2, Finished = 3, Interrupted = 4, Error = 5, Idle = 6 }

    public enum MfMachineState { Off = 0, Idle = 1, Running = 2, Alarm = 3, Offline = 4 }

    /// <summary>Modbus function used to read the inputs: 2 = read discrete inputs (usual for DI modules), 1 = read coils.</summary>
    public enum MfModbusFunction { Coils = 1, DiscreteInputs = 2 }

    /// <summary>How the state of one machine is read (1:1 side table of the legacy Machine).</summary>
    public class MfMachineMonitor : BaseEntity
    {
        public int MachineId { get; set; }
        public MfMonitorSource Source { get; set; }

        public string Host { get; set; }

        /// <summary>Null: 502.</summary>
        public int? Port { get; set; }

        public byte UnitId { get; set; } = 1;
        public MfModbusFunction Function { get; set; } = MfModbusFunction.DiscreteInputs;

        /// <summary>The first input address read (0-based); the inputs below are counted from it.</summary>
        public int StartAddress { get; set; }

        /// <summary>Input (0-based, from StartAddress) that is on while the machine runs a program (green light / spindle / cycle on).</summary>
        public int? RunInput { get; set; }

        /// <summary>Input that is on while the machine is in alarm (red light).</summary>
        public int? AlarmInput { get; set; }

        /// <summary>Input that is on while the machine has power / is ready (yellow light or control on). Null: a reachable machine that neither runs nor alarms is idle.</summary>
        public int? PowerInput { get; set; }

        /// <summary>Bit mask of the inputs that are active-low (bit n = input n inverted).</summary>
        public int InvertMask { get; set; }

        /// <summary>Seconds between two polls (Modbus) or the longest expected gap between two pushes (gateway); null: 10.</summary>
        public int? PollSeconds { get; set; }

        /// <summary>★ provisional: false. An alarm that starts opens a maintenance request (the machine status itself is left to maintenance).</summary>
        public bool AlarmCreatesRequest { get; set; }

        /// <summary>★ provisional: 15. Minutes of disagreement between the machine and the booking (idle while a run booking is open, or running with no booking) before an inbox alert.</summary>
        public int MismatchMinutes { get; set; } = 15;

        public bool IsActive { get; set; } = true;

        public DateTimeOffset? LastPollAt { get; set; }
        public DateTimeOffset? LastSeenAt { get; set; }
        public int FailedPolls { get; set; }
        public string LastError { get; set; }

        /// <summary>The last values a gateway reported (shown on the live board).</summary>
        public string LastProgramName { get; set; }

        public int? LastSpindleRpm { get; set; }
        public int? LastFeedOverride { get; set; }
        public long? LastPartCount { get; set; }
        public string Note { get; set; }

        /// <summary>The inbox alerts that are open now (so the poll touches the inbox only when an alert opens or closes).</summary>
        public bool AlarmAlertOpen { get; set; }

        public string MismatchAlertOpen { get; set; }

        // ------------------------------------------------------------------ P2 part E: the data link to the control

        public MfDataLinkKind DataLink { get; set; }
        public string DataHost { get; set; }

        /// <summary>Null: 19000 (LSV2).</summary>
        public int? DataPort { get; set; }

        /// <summary>The key of the PLC password in the server settings (Dnc:Credentials:&lt;key&gt;:Password); never the password itself.</summary>
        public string DataCredentialKey { get; set; }

        /// <summary>
        /// Where an older control keeps what LSV2 cannot ask directly, set by the machine builder's PLC: "exec=W100;state=W102;feed=W104;spindle=W106;tool=W108;run=M2000;alarm=M2001".
        /// W = PLC word, M = PLC marker. Empty: only what the control answers itself.
        /// </summary>
        public string PlcMap { get; set; }

        /// <summary>The folder of the programs on the control (for the read-back): "TNC:\" by default.</summary>
        public string ProgramFolder { get; set; }

        /// <summary>The file the measuring cycles write their results to (one "CODE=VALUE" a line), read after a program ends; empty: none.</summary>
        public string ResultFile { get; set; }

        /// <summary>What the last capability test found the control to answer (Persian, shown on the settings page).</summary>
        public string Capabilities { get; set; }

        public DateTimeOffset? DataSeenAt { get; set; }
        public int DataFailedPolls { get; set; }
        public string DataError { get; set; }
        public MfExecMode ExecMode { get; set; }
        public MfProgramState ProgramState { get; set; }
        public int? LastSpindleOverride { get; set; }
        public int? LastToolNumber { get; set; }
        public int? LastBlockNumber { get; set; }

        /// <summary>The fingerprint (canonical SHA-256, see MfGuardRules.Canonical) of the selected program, as an edge box at the machine pushed it.</summary>
        public string LastProgramSha256 { get; set; }

        public DateTimeOffset? LastProgramShaAt { get; set; }

        /// <summary>The time of the last result file read (a file is read once per change).</summary>
        public DateTimeOffset? ResultReadAt { get; set; }

        // ------------------------------------------------------------------ P2 part E: the start interlock

        /// <summary>A relay output wired by the machine's electrician into the NC-start enable of the PLC (never the emergency-stop chain): off = the start button does nothing.</summary>
        public bool InterlockEnabled { get; set; }

        public string InterlockHost { get; set; }
        public int? InterlockPort { get; set; }
        public byte InterlockUnitId { get; set; } = 1;

        /// <summary>The coil (0-based) of the relay output.</summary>
        public int InterlockCoil { get; set; }

        /// <summary>True when the relay must be off to allow a start (normally-closed wiring).</summary>
        public bool InterlockInvert { get; set; }

        /// <summary>What the server last wrote: true = start allowed.</summary>
        public bool StartEnabled { get; set; }

        public DateTimeOffset? StartEnabledAt { get; set; }

        /// <summary>Why the start is not allowed now (Persian), shown on the terminal and the live board.</summary>
        public string StartBlockReason { get; set; }

        public string InterlockError { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    /// <summary>One period of one state of a machine (the open period has no end).</summary>
    public class MfMachineStateEvent
    {
        public long Id { get; set; }
        public int MachineId { get; set; }
        public MfMachineState State { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public MfMonitorSource Source { get; set; }
        public string AlarmCode { get; set; }
        public string AlarmText { get; set; }

        /// <summary>The maintenance request this alarm opened (when the monitor is set to).</summary>
        public int? MaintenanceRequestId { get; set; }
    }
}
