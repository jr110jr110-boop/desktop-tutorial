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
    }

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
