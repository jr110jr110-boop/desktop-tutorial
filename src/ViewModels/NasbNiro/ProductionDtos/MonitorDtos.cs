using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Production;

namespace ViewModels.NasbNiro.ProductionDtos
{
    // Production release P2 part B: live machine monitoring. ISO dates in JSON; every "...Text" is Persian (Shamsi).

    public class MachineMonitorQuery : MfPageQuery
    {
        public int? WorkCenterId { get; set; }
    }

    public class MachineMonitorDto
    {
        public int? Id { get; set; }
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public string WorkCenterCode { get; set; }
        public MfMonitorSource Source { get; set; }
        public string Host { get; set; }
        public int? Port { get; set; }
        public int UnitId { get; set; }
        public MfModbusFunction Function { get; set; }
        public int StartAddress { get; set; }
        public int? RunInput { get; set; }
        public int? AlarmInput { get; set; }
        public int? PowerInput { get; set; }
        public int InvertMask { get; set; }
        public int? PollSeconds { get; set; }
        public bool AlarmCreatesRequest { get; set; }
        public int MismatchMinutes { get; set; }
        public bool IsActive { get; set; }
        public string Note { get; set; }
        public string LastPollAtText { get; set; }
        public string LastSeenAtText { get; set; }
        public int FailedPolls { get; set; }
        public string LastError { get; set; }

        /// <summary>A gateway push needs the edge-gateway link of the machine (its key authenticates the box).</summary>
        public bool HasGatewayLink { get; set; }

        public string RowVersion { get; set; }
    }

    public class SaveMachineMonitorDto
    {
        public int MachineId { get; set; }
        public MfMonitorSource Source { get; set; }
        public string Host { get; set; }
        public int? Port { get; set; }
        public int UnitId { get; set; } = 1;
        public MfModbusFunction Function { get; set; } = MfModbusFunction.DiscreteInputs;
        public int StartAddress { get; set; }
        public int? RunInput { get; set; }
        public int? AlarmInput { get; set; }
        public int? PowerInput { get; set; }
        public int InvertMask { get; set; }
        public int? PollSeconds { get; set; }
        public bool AlarmCreatesRequest { get; set; }
        public int MismatchMinutes { get; set; } = 15;
        public bool IsActive { get; set; } = true;
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class MonitorInputsTestDto
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public List<bool> Inputs { get; set; } = new List<bool>();
        public MfMachineState? State { get; set; }
    }

    public class StateMinutesDto
    {
        public double Running { get; set; }
        public double Idle { get; set; }
        public double Off { get; set; }
        public double Alarm { get; set; }
        public double Offline { get; set; }
    }

    /// <summary>One machine on the live board.</summary>
    public class LiveMachineDto
    {
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public string WorkCenterCode { get; set; }
        public MfMonitorSource Source { get; set; }

        /// <summary>Null: never observed yet.</summary>
        public MfMachineState? State { get; set; }

        public string SinceText { get; set; }
        public int MinutesInState { get; set; }
        public string LastSeenAtText { get; set; }
        public string AlarmCode { get; set; }
        public string AlarmText { get; set; }

        /// <summary>Reported by a gateway.</summary>
        public string ProgramName { get; set; }

        public int? SpindleRpm { get; set; }
        public int? FeedOverride { get; set; }
        public long? PartCount { get; set; }

        /// <summary>The program revision of the last start on this machine (DNC run record).</summary>
        public string LastRunProgram { get; set; }

        public int? LastRunRevision { get; set; }
        public int? OperationId { get; set; }
        public string OperationName { get; set; }

        /// <summary>Only for the roles that see names behind hours.</summary>
        public string OperatorName { get; set; }

        public bool BookingOpen { get; set; }
        public StateMinutesDto Today { get; set; } = new StateMinutesDto();
        public decimal? TodayRunPercent { get; set; }

        /// <summary>booked-idle, running-unbooked or null.</summary>
        public string Mismatch { get; set; }

        public string MismatchText { get; set; }
    }

    public class LiveBoardDto
    {
        public string AsOfText { get; set; }
        public List<LiveMachineDto> Machines { get; set; } = new List<LiveMachineDto>();
        public int Running { get; set; }
        public int Idle { get; set; }
        public int Off { get; set; }
        public int Alarm { get; set; }
        public int Offline { get; set; }
        public int Mismatches { get; set; }
    }

    public class StateEventQuery : MfPageQuery
    {
        public int? MachineId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public MfMachineState? State { get; set; }
    }

    public class StateEventDto
    {
        public long Id { get; set; }
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public MfMachineState State { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public string StartAtText { get; set; }
        public string EndAtText { get; set; }
        public double Minutes { get; set; }
        public MfMonitorSource Source { get; set; }
        public string AlarmCode { get; set; }
        public string AlarmText { get; set; }
        public int? MaintenanceRequestId { get; set; }
    }

    public class UtilisationQuery
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int? WorkCenterId { get; set; }
    }

    /// <summary>Measured time per state of a machine in a period, against the hours the operators booked on it (run and setup).</summary>
    public class MachineUtilisationDto
    {
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public string WorkCenterCode { get; set; }
        public StateMinutesDto Minutes { get; set; } = new StateMinutesDto();
        public decimal? RunPercent { get; set; }
        public int Alarms { get; set; }
        public decimal MeasuredRunHours { get; set; }
        public decimal BookedHours { get; set; }

        /// <summary>Booked minus measured, as a share of the measured hours (positive: more booked than the machine ran). Null when nothing was measured.</summary>
        public decimal? BookingGapPercent { get; set; }
    }

    public class MonitorPollResultDto
    {
        public int Polled { get; set; }
        public int Changed { get; set; }
        public int Offline { get; set; }
        public int Alarms { get; set; }
        public int RequestsOpened { get; set; }
        public int Mismatches { get; set; }
    }

    /// <summary>The state an edge box at the machine pushes (header X-Dnc-Machine / X-Dnc-Key, the key of the machine's edge-gateway link).</summary>
    public class GatewayStatusDto
    {
        public MfMachineState State { get; set; }
        public string AlarmCode { get; set; }
        public string AlarmText { get; set; }
        public string ProgramName { get; set; }
        public int? SpindleRpm { get; set; }
        public int? FeedOverride { get; set; }
        public long? PartCount { get; set; }
    }
}
