using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Production;

namespace ViewModels.NasbNiro.ProductionDtos
{
    // Production release P2 part E: the guarded machining process (data link to the control, gates, start interlock, overrides, measurements, CAM evidence).

    public class MachineGuardDto
    {
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public string WorkCenterCode { get; set; }

        /// <summary>True when the data link is set: the guard gates apply to this machine.</summary>
        public bool Guarded { get; set; }

        public MfDataLinkKind DataLink { get; set; }
        public string DataHost { get; set; }
        public int? DataPort { get; set; }
        public string DataCredentialKey { get; set; }
        public string PlcMap { get; set; }
        public string ProgramFolder { get; set; }
        public string ResultFile { get; set; }
        public string Capabilities { get; set; }
        public string DataSeenAtText { get; set; }
        public int DataFailedPolls { get; set; }
        public string DataError { get; set; }
        public bool LinkHealthy { get; set; }
        public MfExecMode ExecMode { get; set; }
        public string ExecModeText { get; set; }
        public MfProgramState ProgramState { get; set; }
        public string ProgramStateText { get; set; }
        public int? FeedOverride { get; set; }
        public int? SpindleOverride { get; set; }
        public string SelectedProgram { get; set; }
        public int? ToolNumber { get; set; }
        public int? BlockNumber { get; set; }
        public bool InterlockEnabled { get; set; }
        public string InterlockHost { get; set; }
        public int? InterlockPort { get; set; }
        public int InterlockUnitId { get; set; } = 1;
        public int InterlockCoil { get; set; }
        public bool InterlockInvert { get; set; }
        public bool StartEnabled { get; set; }
        public string StartEnabledAtText { get; set; }
        public string StartBlockReason { get; set; }
        public string InterlockError { get; set; }
        public bool HasMonitor { get; set; }
    }

    public class SaveMachineGuardDto
    {
        public int MachineId { get; set; }
        public MfDataLinkKind DataLink { get; set; }
        public string DataHost { get; set; }
        public int? DataPort { get; set; }
        public string DataCredentialKey { get; set; }
        public string PlcMap { get; set; }
        public string ProgramFolder { get; set; }
        public string ResultFile { get; set; }
        public bool InterlockEnabled { get; set; }
        public string InterlockHost { get; set; }
        public int? InterlockPort { get; set; }
        public int InterlockUnitId { get; set; } = 1;
        public int InterlockCoil { get; set; }
        public bool InterlockInvert { get; set; }
    }

    public class GuardCapabilityDto
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public List<string> Lines { get; set; } = new List<string>();
    }

    public class GuardPollResultDto
    {
        public int Polled { get; set; }
        public int Failed { get; set; }
        public int Activities { get; set; }
        public int Unapproved { get; set; }
        public int Measurements { get; set; }
        public int InterlockWrites { get; set; }
    }

    public class MachineActivityQuery
    {
        public int? MachineId { get; set; }
        public int? OperationId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class MachineActivityDto
    {
        public long Id { get; set; }
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public int? OperationId { get; set; }
        public string EmployeeName { get; set; }
        public DateTime At { get; set; }
        public string AtText { get; set; }
        public MfActivityKind Kind { get; set; }
        public string KindText { get; set; }
        public string Value { get; set; }
        public string Text { get; set; }

        /// <summary>ok / warn / bad / info: the colour of the line on the time line.</summary>
        public string Tone { get; set; }
    }

    /// <summary>One gate of the guarded start, as the terminal shows it before the operator presses start.</summary>
    public class GuardGateDto
    {
        public string Code { get; set; }
        public string Title { get; set; }

        /// <summary>ok / blocked / skipped / pending.</summary>
        public string State { get; set; }

        public string Text { get; set; }
        public bool Overridden { get; set; }
    }

    public class TerminalGuardDto
    {
        public int MachineId { get; set; }
        public int OperationId { get; set; }
        public bool Guarded { get; set; }
        public bool CanStart { get; set; }
        public List<GuardGateDto> Gates { get; set; } = new List<GuardGateDto>();
        public MachineGuardDto Machine { get; set; }
        public List<GateOverrideDto> Overrides { get; set; } = new List<GateOverrideDto>();
    }

    public class CreateGateOverrideDto
    {
        public int MachineId { get; set; }
        public int OperationId { get; set; }
        public string Gate { get; set; }
        public string Reason { get; set; }
    }

    public class GateOverrideDto
    {
        public int Id { get; set; }
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public int OperationId { get; set; }
        public string Gate { get; set; }
        public string GateText { get; set; }
        public string Reason { get; set; }
        public string ByName { get; set; }
        public DateTimeOffset At { get; set; }
        public string AtText { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public string ExpiresAtText { get; set; }
        public string ConsumedAtText { get; set; }
        public bool Active { get; set; }
    }

    public class GateOverrideQuery
    {
        public int? MachineId { get; set; }
        public bool ActiveOnly { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    // ------------------------------------------------------------------------------------------ measurements

    public class MeasureStationDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string AllowedAddresses { get; set; }
        public int? MachineId { get; set; }
        public string MachineTitle { get; set; }
        public bool IsActive { get; set; } = true;
        public bool HasKey { get; set; }
        public string LastSeenAtText { get; set; }
        public string RowVersion { get; set; }
    }

    public class StationKeyDto
    {
        public int StationId { get; set; }

        /// <summary>Shown once; only its SHA-256 is stored.</summary>
        public string Key { get; set; }
    }

    public class MeasurementPushItemDto
    {
        public int? OperationId { get; set; }
        public int? MachineId { get; set; }
        public string Characteristic { get; set; }
        public decimal Value { get; set; }
        public string Device { get; set; }
    }

    public class MeasurementPushDto
    {
        public List<MeasurementPushItemDto> Items { get; set; } = new List<MeasurementPushItemDto>();
    }

    public class MeasurementPushResultDto
    {
        public int Accepted { get; set; }
    }

    public class MeasurementQuery
    {
        public int? OperationId { get; set; }
        public int? MachineId { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class MeasurementDto
    {
        public long Id { get; set; }
        public int? OperationId { get; set; }
        public int? MachineId { get; set; }
        public string MachineTitle { get; set; }
        public string StationCode { get; set; }
        public string Characteristic { get; set; }
        public decimal Value { get; set; }
        public MfMeasurementSource Source { get; set; }
        public string SourceText { get; set; }
        public string Device { get; set; }
        public DateTime At { get; set; }
        public string AtText { get; set; }
    }

    // ------------------------------------------------------------------------------------------ CAM evidence

    public class PostProcessorQuery
    {
        public string Contains { get; set; }
        public MfPostStatus? Status { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class PostProcessorDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string CamSystem { get; set; }
        public MfNcControllerFamily ControllerFamily { get; set; }
        public string ControllerFamilyText { get; set; }
        public int? MachineId { get; set; }
        public string MachineTitle { get; set; }
        public int Version { get; set; }
        public string FileName { get; set; }
        public string Sha256 { get; set; }
        public int SizeBytes { get; set; }
        public MfPostStatus Status { get; set; }
        public string StatusText { get; set; }
        public string ChangeNote { get; set; }
        public string CreatedByName { get; set; }
        public string SubmittedAtText { get; set; }
        public string ApprovedByName { get; set; }
        public string ApprovedAtText { get; set; }
        public string RejectReason { get; set; }
        public List<string> AllowedActions { get; set; } = new List<string>();
        public string RowVersion { get; set; }
    }

    public class SavePostProcessorDto
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string CamSystem { get; set; }
        public MfNcControllerFamily ControllerFamily { get; set; }
        public int? MachineId { get; set; }
        public string FileName { get; set; }
        public string ContentBase64 { get; set; }
        public string ChangeNote { get; set; }
    }

    public class PostProcessorActionDto
    {
        public int Id { get; set; }

        /// <summary>submit, approve, reject, retire, withdraw.</summary>
        public string Action { get; set; }

        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }

    public class CamToolDto
    {
        public int ToolNumber { get; set; }
        public string Name { get; set; }
        public decimal? Diameter { get; set; }
        public decimal? Length { get; set; }
    }

    public class NcAttachmentDto
    {
        public int Id { get; set; }
        public MfNcAttachmentKind Kind { get; set; }
        public string KindText { get; set; }
        public string FileName { get; set; }
        public string Sha256 { get; set; }
        public int SizeBytes { get; set; }
        public string UploadedByName { get; set; }
        public string UploadedAtText { get; set; }
    }

    public class RevisionCamDto
    {
        public int RevisionId { get; set; }
        public int? PostProcessorId { get; set; }
        public string PostProcessorText { get; set; }
        public string CamSystem { get; set; }
        public int? SimulationAttachmentId { get; set; }
        public List<NcAttachmentDto> Attachments { get; set; } = new List<NcAttachmentDto>();
        public List<CamToolDto> Tools { get; set; } = new List<CamToolDto>();

        /// <summary>What is still missing before the revision may be submitted (Persian; empty = complete).</summary>
        public List<string> Missing { get; set; } = new List<string>();

        public bool Editable { get; set; }
    }

    public class SetRevisionCamDto
    {
        public int RevisionId { get; set; }
        public int? PostProcessorId { get; set; }
        public string CamSystem { get; set; }
    }

    public class UploadNcAttachmentDto
    {
        public int RevisionId { get; set; }
        public MfNcAttachmentKind Kind { get; set; }
        public string FileName { get; set; }
        public string ContentBase64 { get; set; }
    }

    public class ImportCamToolsDto
    {
        public int RevisionId { get; set; }
        public string Csv { get; set; }
    }

    public class NcAttachmentFileDto
    {
        public string FileName { get; set; }
        public string ContentBase64 { get; set; }
    }
}
