using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ProductionDnc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SetupCount",
                table: "MfOperationStandards",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HrPerformanceEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Score = table.Column<int>(type: "int", nullable: true),
                    NcrId = table.Column<int>(type: "int", nullable: true),
                    OperationId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrPerformanceEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HrPerformanceEvents_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HrPerformanceEvents_InvNonConformances_NcrId",
                        column: x => x.NcrId,
                        principalTable: "InvNonConformances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HrPerformanceEvents_MfOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfCameras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: true),
                    WorkCenterId = table.Column<int>(type: "int", nullable: true),
                    SnapshotUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StreamUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CredentialKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfCameras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfCameras_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfCameras_MfWorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "MfWorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfChecklistTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationTemplateId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RequiredToFinish = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfChecklistTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfChecklistTemplates_InvOperationTemplates_OperationTemplateId",
                        column: x => x.OperationTemplateId,
                        principalTable: "InvOperationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfMachineConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    ControllerFamily = table.Column<int>(type: "int", nullable: false),
                    ControllerModel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LinkKind = table.Column<int>(type: "int", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: true),
                    RemotePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CredentialKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ProgramNameFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FileExtension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LineEnding = table.Column<int>(type: "int", nullable: false),
                    RequirePercent = table.Column<bool>(type: "bit", nullable: false),
                    MaxProgramBytes = table.Column<int>(type: "int", nullable: true),
                    Axes = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MaxSpindleRpm = table.Column<int>(type: "int", nullable: true),
                    MaxFeedMmPerMin = table.Column<int>(type: "int", nullable: true),
                    ToolCapacity = table.Column<int>(type: "int", nullable: true),
                    IsLathe = table.Column<bool>(type: "bit", nullable: false),
                    BaudRate = table.Column<int>(type: "int", nullable: true),
                    RequireConfirmedProgram = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    GatewayKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    GatewayKeyRotatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GatewayKeyRotatedByUserId = table.Column<int>(type: "int", nullable: true),
                    AllowedAddresses = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfMachineConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMachineConnections_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfMachineMonitors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Port = table.Column<int>(type: "int", nullable: true),
                    UnitId = table.Column<byte>(type: "tinyint", nullable: false),
                    Function = table.Column<int>(type: "int", nullable: false),
                    StartAddress = table.Column<int>(type: "int", nullable: false),
                    RunInput = table.Column<int>(type: "int", nullable: true),
                    AlarmInput = table.Column<int>(type: "int", nullable: true),
                    PowerInput = table.Column<int>(type: "int", nullable: true),
                    InvertMask = table.Column<int>(type: "int", nullable: false),
                    PollSeconds = table.Column<int>(type: "int", nullable: true),
                    AlarmCreatesRequest = table.Column<bool>(type: "bit", nullable: false),
                    MismatchMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastPollAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedPolls = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastProgramName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastSpindleRpm = table.Column<int>(type: "int", nullable: true),
                    LastFeedOverride = table.Column<int>(type: "int", nullable: true),
                    LastPartCount = table.Column<long>(type: "bigint", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AlarmAlertOpen = table.Column<bool>(type: "bit", nullable: false),
                    MismatchAlertOpen = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfMachineMonitors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMachineMonitors_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfMachineStateEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    AlarmCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AlarmText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MaintenanceRequestId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfMachineStateEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMachineStateEvents_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMachineStateEvents_MfMaintenanceRequests_MaintenanceRequestId",
                        column: x => x.MaintenanceRequestId,
                        principalTable: "MfMaintenanceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfChecklistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Nominal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Min = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Max = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Mandatory = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfChecklistItems_MfChecklistTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "MfChecklistTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfChecklistResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<int>(type: "int", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AllPassed = table.Column<bool>(type: "bit", nullable: false),
                    FilledByUserId = table.Column<int>(type: "int", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfChecklistResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfChecklistResults_MfChecklistTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "MfChecklistTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfChecklistResults_MfOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfChecklistAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResultId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    YesNo = table.Column<bool>(type: "bit", nullable: true),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Passed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfChecklistAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfChecklistAnswers_MfChecklistItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "MfChecklistItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfChecklistAnswers_MfChecklistResults_ResultId",
                        column: x => x.ResultId,
                        principalTable: "MfChecklistResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfNcContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RevisionId = table.Column<int>(type: "int", nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfNcContents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MfNcPrograms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    OperationTemplateId = table.Column<int>(type: "int", nullable: true),
                    WorkCenterId = table.Column<int>(type: "int", nullable: true),
                    MachineId = table.Column<int>(type: "int", nullable: true),
                    ControllerFamily = table.Column<int>(type: "int", nullable: false),
                    CurrentRevisionId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfNcPrograms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfNcPrograms_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcPrograms_InvOperationTemplates_OperationTemplateId",
                        column: x => x.OperationTemplateId,
                        principalTable: "InvOperationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcPrograms_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcPrograms_MfWorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "MfWorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfNcRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProgramId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProgramNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    FromTransferId = table.Column<int>(type: "int", nullable: true),
                    BaseRevisionId = table.Column<int>(type: "int", nullable: true),
                    DiffAdded = table.Column<int>(type: "int", nullable: false),
                    DiffRemoved = table.Column<int>(type: "int", nullable: false),
                    ChangeNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Findings = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    WarningCount = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfNcRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfNcRevisions_MfNcPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "MfNcPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcRevisions_MfNcRevisions_BaseRevisionId",
                        column: x => x.BaseRevisionId,
                        principalTable: "MfNcRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfNcTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    ProgramId = table.Column<int>(type: "int", nullable: true),
                    RevisionId = table.Column<int>(type: "int", nullable: true),
                    OperationId = table.Column<int>(type: "int", nullable: true),
                    TargetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LinkKind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SentSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SentBytes = table.Column<int>(type: "int", nullable: false),
                    RequestNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfNcTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfNcTransfers_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcTransfers_MfNcPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "MfNcPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcTransfers_MfNcRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "MfNcRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcTransfers_MfOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfNcRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<int>(type: "int", nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    ProgramId = table.Column<int>(type: "int", nullable: false),
                    RevisionId = table.Column<int>(type: "int", nullable: false),
                    TransferId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfNcRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfNcRuns_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcRuns_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcRuns_MfNcPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "MfNcPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcRuns_MfNcRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "MfNcRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcRuns_MfNcTransfers_TransferId",
                        column: x => x.TransferId,
                        principalTable: "MfNcTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfNcRuns_MfOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HrPerformanceEvents_EmployeeId_EventDate",
                table: "HrPerformanceEvents",
                columns: new[] { "EmployeeId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_HrPerformanceEvents_NcrId",
                table: "HrPerformanceEvents",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_HrPerformanceEvents_OperationId",
                table: "HrPerformanceEvents",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_MfCameras_MachineId",
                table: "MfCameras",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_MfCameras_WorkCenterId",
                table: "MfCameras",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_MfChecklistAnswers_ItemId",
                table: "MfChecklistAnswers",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MfChecklistAnswers_ResultId_ItemId",
                table: "MfChecklistAnswers",
                columns: new[] { "ResultId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfChecklistItems_TemplateId",
                table: "MfChecklistItems",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MfChecklistResults_OperationId",
                table: "MfChecklistResults",
                column: "OperationId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfChecklistResults_TemplateId",
                table: "MfChecklistResults",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MfChecklistTemplates_OperationTemplateId",
                table: "MfChecklistTemplates",
                column: "OperationTemplateId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfMachineConnections_MachineId",
                table: "MfMachineConnections",
                column: "MachineId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfMachineMonitors_MachineId",
                table: "MfMachineMonitors",
                column: "MachineId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfMachineStateEvents_MachineId_EndAt",
                table: "MfMachineStateEvents",
                columns: new[] { "MachineId", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MfMachineStateEvents_MachineId_StartAt",
                table: "MfMachineStateEvents",
                columns: new[] { "MachineId", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MfMachineStateEvents_MaintenanceRequestId",
                table: "MfMachineStateEvents",
                column: "MaintenanceRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcContents_RevisionId",
                table: "MfNcContents",
                column: "RevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfNcPrograms_Code",
                table: "MfNcPrograms",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcPrograms_CurrentRevisionId",
                table: "MfNcPrograms",
                column: "CurrentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcPrograms_ItemId",
                table: "MfNcPrograms",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcPrograms_MachineId",
                table: "MfNcPrograms",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcPrograms_OperationTemplateId",
                table: "MfNcPrograms",
                column: "OperationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcPrograms_WorkCenterId",
                table: "MfNcPrograms",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcRevisions_BaseRevisionId",
                table: "MfNcRevisions",
                column: "BaseRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcRevisions_ProgramId_Revision",
                table: "MfNcRevisions",
                columns: new[] { "ProgramId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfNcRuns_EmployeeId",
                table: "MfNcRuns",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcRuns_MachineId_StartedAt",
                table: "MfNcRuns",
                columns: new[] { "MachineId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MfNcRuns_OperationId",
                table: "MfNcRuns",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcRuns_ProgramId",
                table: "MfNcRuns",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcRuns_RevisionId",
                table: "MfNcRuns",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcRuns_TransferId",
                table: "MfNcRuns",
                column: "TransferId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcTransfers_MachineId_Status",
                table: "MfNcTransfers",
                columns: new[] { "MachineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MfNcTransfers_OperationId",
                table: "MfNcTransfers",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcTransfers_ProgramId",
                table: "MfNcTransfers",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcTransfers_RevisionId",
                table: "MfNcTransfers",
                column: "RevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_MfNcContents_MfNcRevisions_RevisionId",
                table: "MfNcContents",
                column: "RevisionId",
                principalTable: "MfNcRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MfNcPrograms_MfNcRevisions_CurrentRevisionId",
                table: "MfNcPrograms",
                column: "CurrentRevisionId",
                principalTable: "MfNcRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MfNcPrograms_MfNcRevisions_CurrentRevisionId",
                table: "MfNcPrograms");

            migrationBuilder.DropTable(
                name: "HrPerformanceEvents");

            migrationBuilder.DropTable(
                name: "MfCameras");

            migrationBuilder.DropTable(
                name: "MfChecklistAnswers");

            migrationBuilder.DropTable(
                name: "MfMachineConnections");

            migrationBuilder.DropTable(
                name: "MfMachineMonitors");

            migrationBuilder.DropTable(
                name: "MfMachineStateEvents");

            migrationBuilder.DropTable(
                name: "MfNcContents");

            migrationBuilder.DropTable(
                name: "MfNcRuns");

            migrationBuilder.DropTable(
                name: "MfChecklistItems");

            migrationBuilder.DropTable(
                name: "MfChecklistResults");

            migrationBuilder.DropTable(
                name: "MfNcTransfers");

            migrationBuilder.DropTable(
                name: "MfChecklistTemplates");

            migrationBuilder.DropTable(
                name: "MfNcRevisions");

            migrationBuilder.DropTable(
                name: "MfNcPrograms");

            migrationBuilder.DropColumn(
                name: "SetupCount",
                table: "MfOperationStandards");
        }
    }
}
