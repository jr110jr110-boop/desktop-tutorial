using System;
using System.Collections.Generic;
using Entities.Model.NasbNiro.Production;

namespace ViewModels.NasbNiro.ProductionDtos
{
    // Production release P2 part A: DNC. Dates in JSON are ISO; every text a person reads (…Text) is Persian (Shamsi).

    // ------------------------------------------------------------------------------------------ machine connections

    public class MachineConnectionQuery : MfPageQuery
    {
        public MfNcLinkKind? LinkKind { get; set; }
        public int? WorkCenterId { get; set; }
    }

    public class MachineConnectionDto
    {
        /// <summary>Null while the machine has no connection row yet (the list shows every machine).</summary>
        public int? Id { get; set; }

        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public bool MachineIsActive { get; set; }
        public int? WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public MfMachineStatus? MachineStatus { get; set; }
        public MfNcControllerFamily ControllerFamily { get; set; }
        public string ControllerModel { get; set; }
        public MfNcLinkKind LinkKind { get; set; }
        public string Host { get; set; }
        public int? Port { get; set; }
        public string RemotePath { get; set; }
        public string CredentialKey { get; set; }

        /// <summary>The server settings hold a user name for the credential key (the secret itself is never sent).</summary>
        public bool CredentialConfigured { get; set; }

        public string ProgramNameFormat { get; set; }
        public string FileExtension { get; set; }
        public MfNcLineEnding LineEnding { get; set; }
        public bool RequirePercent { get; set; }
        public int? MaxProgramBytes { get; set; }
        public string Axes { get; set; }
        public int? MaxSpindleRpm { get; set; }
        public int? MaxFeedMmPerMin { get; set; }
        public int? ToolCapacity { get; set; }
        public bool IsLathe { get; set; }
        public int? BaudRate { get; set; }
        public bool RequireConfirmedProgram { get; set; }
        public bool HasGatewayKey { get; set; }
        public string GatewayKeyRotatedAtText { get; set; }
        public string AllowedAddresses { get; set; }
        public string LastSeenAtText { get; set; }
        public bool IsActive { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class SaveMachineConnectionDto
    {
        public int MachineId { get; set; }
        public MfNcControllerFamily ControllerFamily { get; set; }
        public string ControllerModel { get; set; }
        public MfNcLinkKind LinkKind { get; set; }
        public string Host { get; set; }
        public int? Port { get; set; }
        public string RemotePath { get; set; }
        public string CredentialKey { get; set; }
        public string ProgramNameFormat { get; set; }
        public string FileExtension { get; set; }
        public MfNcLineEnding LineEnding { get; set; } = MfNcLineEnding.CrLf;
        public bool RequirePercent { get; set; }
        public int? MaxProgramBytes { get; set; }
        public string Axes { get; set; }
        public int? MaxSpindleRpm { get; set; }
        public int? MaxFeedMmPerMin { get; set; }
        public int? ToolCapacity { get; set; }
        public bool IsLathe { get; set; }
        public int? BaudRate { get; set; }
        public bool RequireConfirmedProgram { get; set; } = true;
        public string AllowedAddresses { get; set; }
        public bool IsActive { get; set; } = true;
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class MachineIdDto
    {
        public int MachineId { get; set; }
    }

    /// <summary>The new key of an edge gateway: shown once, never again.</summary>
    public class GatewayKeyDto
    {
        public int MachineId { get; set; }
        public string Key { get; set; }
        public string RotatedAtText { get; set; }
    }

    public class LinkTestResultDto
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
    }

    // ------------------------------------------------------------------------------------------ programs and revisions

    public class NcProgramQuery : MfPageQuery
    {
        public int? ItemId { get; set; }
        public int? OperationTemplateId { get; set; }
        public int? WorkCenterId { get; set; }
        public int? MachineId { get; set; }
        public MfNcProgramStatus? Status { get; set; }

        /// <summary>Only programs with a revision waiting for approval.</summary>
        public bool? Pending { get; set; }
    }

    public class NcProgramDto
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ItemId { get; set; }
        public string ItemCode { get; set; }
        public string ItemTitle { get; set; }
        public int? OperationTemplateId { get; set; }
        public string OperationTitle { get; set; }
        public int? WorkCenterId { get; set; }
        public string WorkCenterCode { get; set; }
        public int? MachineId { get; set; }
        public string MachineTitle { get; set; }
        public MfNcControllerFamily ControllerFamily { get; set; }
        public int? CurrentRevisionId { get; set; }
        public int? CurrentRevision { get; set; }
        public string CurrentApprovedAtText { get; set; }
        public int PendingRevisions { get; set; }
        public MfNcProgramStatus Status { get; set; }
        public string Note { get; set; }
        public string RowVersion { get; set; }

        /// <summary>Filled by the detail call only, newest first.</summary>
        public List<NcRevisionDto> Revisions { get; set; }
    }

    public class SaveNcProgramDto
    {
        public int? Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public int? ItemId { get; set; }
        public int? OperationTemplateId { get; set; }
        public int? WorkCenterId { get; set; }
        public int? MachineId { get; set; }
        public MfNcControllerFamily ControllerFamily { get; set; }
        public MfNcProgramStatus Status { get; set; } = MfNcProgramStatus.Active;
        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class NcFindingDto
    {
        /// <summary>"error" or "warning".</summary>
        public string Severity { get; set; }

        public int Line { get; set; }
        public string Code { get; set; }
        public string Text { get; set; }
    }

    public class NcRevisionDto
    {
        public int Id { get; set; }
        public int ProgramId { get; set; }
        public string ProgramCode { get; set; }
        public int Revision { get; set; }
        public MfNcRevisionStatus Status { get; set; }
        public string FileName { get; set; }
        public int SizeBytes { get; set; }
        public int LineCount { get; set; }
        public string Sha256 { get; set; }
        public string ProgramNumber { get; set; }
        public MfNcRevisionSource Source { get; set; }
        public int? FromTransferId { get; set; }
        public int? BaseRevisionId { get; set; }
        public int? BaseRevision { get; set; }
        public int DiffAdded { get; set; }
        public int DiffRemoved { get; set; }
        public string ChangeNote { get; set; }
        public List<NcFindingDto> Findings { get; set; } = new List<NcFindingDto>();
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public int AuthorUserId { get; set; }
        public string AuthorName { get; set; }
        public string CreatedAtText { get; set; }
        public string SubmittedByName { get; set; }
        public string SubmittedAtText { get; set; }
        public string ApprovedByName { get; set; }
        public string ApprovedAtText { get; set; }
        public string RejectedByName { get; set; }
        public string RejectedAtText { get; set; }
        public string RejectReason { get; set; }

        /// <summary>What the caller may do now: submit, approve, reject, withdraw.</summary>
        public List<string> AllowedActions { get; set; } = new List<string>();

        public string RowVersion { get; set; }
    }

    public class UploadNcRevisionDto
    {
        public int ProgramId { get; set; }
        public string FileName { get; set; }

        /// <summary>The file, base64 (at most 4 MB of file).</summary>
        public string ContentBase64 { get; set; }

        public string ChangeNote { get; set; }
    }

    public class NcRevisionActionDto
    {
        public int RevisionId { get; set; }

        /// <summary>submit, approve, reject (reason required), withdraw.</summary>
        public string Action { get; set; }

        public string Reason { get; set; }
        public string RowVersion { get; set; }
    }

    public class ValidateNcDto
    {
        public int RevisionId { get; set; }

        /// <summary>Null: every machine the program may go to.</summary>
        public int? MachineId { get; set; }
    }

    public class NcValidationDto
    {
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public string TargetName { get; set; }
        public List<NcFindingDto> Findings { get; set; } = new List<NcFindingDto>();
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
    }

    public class NcDiffDto
    {
        public int RevisionId { get; set; }
        public int? BaseRevisionId { get; set; }
        public int? BaseRevision { get; set; }
        public int Added { get; set; }
        public int Removed { get; set; }
        public List<string> Lines { get; set; } = new List<string>();
        public bool Truncated { get; set; }
        public bool Approximate { get; set; }
    }

    /// <summary>A file the API hands out (the controller turns it into a download).</summary>
    public class NcFileDto
    {
        public string FileName { get; set; }
        public byte[] Bytes { get; set; }
        public string Sha256 { get; set; }
    }

    // ------------------------------------------------------------------------------------------ transfers and runs

    public class NcTransferQuery : MfPageQuery
    {
        public int? MachineId { get; set; }
        public int? ProgramId { get; set; }
        public int? OperationId { get; set; }
        public MfNcTransferStatus? Status { get; set; }
        public MfNcTransferDirection? Direction { get; set; }
    }

    public class NcTransferDto
    {
        public int Id { get; set; }
        public MfNcTransferDirection Direction { get; set; }
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public int? ProgramId { get; set; }
        public string ProgramCode { get; set; }
        public int? RevisionId { get; set; }
        public int? Revision { get; set; }
        public int? OperationId { get; set; }
        public string OperationName { get; set; }
        public string TargetName { get; set; }
        public MfNcLinkKind LinkKind { get; set; }
        public MfNcTransferStatus Status { get; set; }
        public int Attempts { get; set; }
        public string LastError { get; set; }
        public string SentSha256 { get; set; }
        public int SentBytes { get; set; }
        public string RequestedByName { get; set; }
        public string RequestedAtText { get; set; }
        public string DeliveredAtText { get; set; }
        public string ConfirmedByName { get; set; }
        public string ConfirmedAtText { get; set; }
        public string ConfirmNote { get; set; }
        public string CancelReason { get; set; }

        /// <summary>confirm, cancel, retry, download.</summary>
        public List<string> AllowedActions { get; set; } = new List<string>();

        public string RowVersion { get; set; }
    }

    public class SendNcDto
    {
        public int ProgramId { get; set; }
        public int MachineId { get; set; }

        /// <summary>The operation the program is sent for (the terminal sends it); optional from the library page.</summary>
        public int? OperationId { get; set; }
    }

    public class ReceiveNcDto
    {
        public int ProgramId { get; set; }
        public int MachineId { get; set; }

        /// <summary>The program name on the control (empty: the name the current revision has there).</summary>
        public string ProgramName { get; set; }

        public string ChangeNote { get; set; }
    }

    public class NcTransferActionDto
    {
        public int TransferId { get; set; }

        /// <summary>confirm (the operator checked the program on the control), cancel (reason required), retry.</summary>
        public string Action { get; set; }

        public string Note { get; set; }
        public string RowVersion { get; set; }
    }

    public class NcRunPendingResultDto
    {
        public int Processed { get; set; }
        public int Delivered { get; set; }
        public int Received { get; set; }
        public int Failed { get; set; }
        public List<string> Messages { get; set; } = new List<string>();
    }

    public class NcRunQuery : MfPageQuery
    {
        public int? OperationId { get; set; }
        public int? OrderId { get; set; }
        public int? MachineId { get; set; }
        public int? ProgramId { get; set; }
        public int? RevisionId { get; set; }
    }

    public class NcRunDto
    {
        public int Id { get; set; }
        public int OperationId { get; set; }
        public string OperationName { get; set; }
        public int MachineId { get; set; }
        public string MachineTitle { get; set; }
        public int ProgramId { get; set; }
        public string ProgramCode { get; set; }
        public int RevisionId { get; set; }
        public int Revision { get; set; }
        public string Sha256 { get; set; }
        public int? TransferId { get; set; }

        /// <summary>Only for the roles that see names behind hours (MfPrivacy.NamedRoles).</summary>
        public string EmployeeName { get; set; }

        public MfBookingKind Kind { get; set; }
        public string StartedAtText { get; set; }
    }

    /// <summary>What the terminal shows for an operation on a machine: the program, its current revision, the last transfer and whether the start is allowed.</summary>
    public class TerminalNcDto
    {
        public int OperationId { get; set; }
        public int MachineId { get; set; }
        public bool HasProgram { get; set; }
        public int? ProgramId { get; set; }
        public string ProgramCode { get; set; }
        public string ProgramTitle { get; set; }
        public int? CurrentRevisionId { get; set; }
        public int? CurrentRevision { get; set; }
        public bool Required { get; set; }
        public bool CanStart { get; set; }
        public string Problem { get; set; }
        public NcTransferDto LastTransfer { get; set; }
    }

    public class TerminalNcQuery
    {
        public int OperationId { get; set; }
        public int MachineId { get; set; }
    }

    // ------------------------------------------------------------------------------------------ edge gateway (anonymous, machine key)

    public class GatewayJobDto
    {
        public int TransferId { get; set; }
        public MfNcTransferDirection Direction { get; set; }
        public string TargetName { get; set; }
        public int SizeBytes { get; set; }
        public string Sha256 { get; set; }
    }

    public class GatewayResultDto
    {
        public int TransferId { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    public class GatewayUploadDto
    {
        public int TransferId { get; set; }
        public string ContentBase64 { get; set; }
    }
}
