using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ProductionGuardE : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OverrideId",
                table: "MfNcRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProveOut",
                table: "MfNcRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ProveOutObserved",
                table: "MfNcRuns",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ToolsVerified",
                table: "MfNcRuns",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "MfNcRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedSha256",
                table: "MfNcRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CamSystem",
                table: "MfNcRevisions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PostProcessorId",
                table: "MfNcRevisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SimulationAttachmentId",
                table: "MfNcRevisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToolCount",
                table: "MfNcRevisions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Capabilities",
                table: "MfMachineMonitors",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataCredentialKey",
                table: "MfMachineMonitors",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataError",
                table: "MfMachineMonitors",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataFailedPolls",
                table: "MfMachineMonitors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DataHost",
                table: "MfMachineMonitors",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataLink",
                table: "MfMachineMonitors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DataPort",
                table: "MfMachineMonitors",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DataSeenAt",
                table: "MfMachineMonitors",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExecMode",
                table: "MfMachineMonitors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InterlockCoil",
                table: "MfMachineMonitors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "InterlockEnabled",
                table: "MfMachineMonitors",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InterlockError",
                table: "MfMachineMonitors",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InterlockHost",
                table: "MfMachineMonitors",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InterlockInvert",
                table: "MfMachineMonitors",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "InterlockPort",
                table: "MfMachineMonitors",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "InterlockUnitId",
                table: "MfMachineMonitors",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "LastBlockNumber",
                table: "MfMachineMonitors",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastProgramSha256",
                table: "MfMachineMonitors",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastProgramShaAt",
                table: "MfMachineMonitors",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastSpindleOverride",
                table: "MfMachineMonitors",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastToolNumber",
                table: "MfMachineMonitors",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlcMap",
                table: "MfMachineMonitors",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProgramFolder",
                table: "MfMachineMonitors",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProgramState",
                table: "MfMachineMonitors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResultFile",
                table: "MfMachineMonitors",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResultReadAt",
                table: "MfMachineMonitors",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartBlockReason",
                table: "MfMachineMonitors",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StartEnabled",
                table: "MfMachineMonitors",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartEnabledAt",
                table: "MfMachineMonitors",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Characteristic",
                table: "MfChecklistItems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeviceOnly",
                table: "MfChecklistItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "MeasurementId",
                table: "MfChecklistAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MfCamTools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RevisionId = table.Column<int>(type: "int", nullable: false),
                    ToolNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Diameter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Length = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfCamTools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfCamTools_MfNcRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "MfNcRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfGateOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<int>(type: "int", nullable: false),
                    Gate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ByUserId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfGateOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfGateOverrides_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfGateOverrides_MfOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfMachineActivities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfMachineActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMachineActivities_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMachineActivities_MfOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfMeasureStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AllowedAddresses = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MachineId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfMeasureStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMeasureStations_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfNcAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RevisionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfNcAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfNcAttachments_MfNcRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "MfNcRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfPostProcessors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CamSystem = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ControllerFamily = table.Column<int>(type: "int", nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ChangeNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_MfPostProcessors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfPostProcessors_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfMeasurements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<int>(type: "int", nullable: true),
                    MachineId = table.Column<int>(type: "int", nullable: true),
                    StationId = table.Column<int>(type: "int", nullable: true),
                    Characteristic = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Device = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMeasurements_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMeasurements_MfMeasureStations_StationId",
                        column: x => x.StationId,
                        principalTable: "MfMeasureStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMeasurements_MfOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MfNcRuns_OverrideId",
                table: "MfNcRuns",
                column: "OverrideId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcRevisions_PostProcessorId",
                table: "MfNcRevisions",
                column: "PostProcessorId");

            migrationBuilder.CreateIndex(
                name: "IX_MfCamTools_RevisionId_ToolNumber",
                table: "MfCamTools",
                columns: new[] { "RevisionId", "ToolNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfGateOverrides_MachineId_OperationId_Gate",
                table: "MfGateOverrides",
                columns: new[] { "MachineId", "OperationId", "Gate" });

            migrationBuilder.CreateIndex(
                name: "IX_MfGateOverrides_OperationId",
                table: "MfGateOverrides",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMachineActivities_MachineId_At",
                table: "MfMachineActivities",
                columns: new[] { "MachineId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_MfMachineActivities_OperationId_At",
                table: "MfMachineActivities",
                columns: new[] { "OperationId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_MfMeasurements_MachineId_At",
                table: "MfMeasurements",
                columns: new[] { "MachineId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_MfMeasurements_OperationId_Characteristic_At",
                table: "MfMeasurements",
                columns: new[] { "OperationId", "Characteristic", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_MfMeasurements_StationId",
                table: "MfMeasurements",
                column: "StationId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMeasureStations_Code",
                table: "MfMeasureStations",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfMeasureStations_MachineId",
                table: "MfMeasureStations",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_MfNcAttachments_RevisionId_Kind",
                table: "MfNcAttachments",
                columns: new[] { "RevisionId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_MfPostProcessors_Code_Version",
                table: "MfPostProcessors",
                columns: new[] { "Code", "Version" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfPostProcessors_MachineId",
                table: "MfPostProcessors",
                column: "MachineId");

            migrationBuilder.AddForeignKey(
                name: "FK_MfNcRevisions_MfPostProcessors_PostProcessorId",
                table: "MfNcRevisions",
                column: "PostProcessorId",
                principalTable: "MfPostProcessors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MfNcRuns_MfGateOverrides_OverrideId",
                table: "MfNcRuns",
                column: "OverrideId",
                principalTable: "MfGateOverrides",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MfNcRevisions_MfPostProcessors_PostProcessorId",
                table: "MfNcRevisions");

            migrationBuilder.DropForeignKey(
                name: "FK_MfNcRuns_MfGateOverrides_OverrideId",
                table: "MfNcRuns");

            migrationBuilder.DropTable(
                name: "MfCamTools");

            migrationBuilder.DropTable(
                name: "MfGateOverrides");

            migrationBuilder.DropTable(
                name: "MfMachineActivities");

            migrationBuilder.DropTable(
                name: "MfMeasurements");

            migrationBuilder.DropTable(
                name: "MfNcAttachments");

            migrationBuilder.DropTable(
                name: "MfPostProcessors");

            migrationBuilder.DropTable(
                name: "MfMeasureStations");

            migrationBuilder.DropIndex(
                name: "IX_MfNcRuns_OverrideId",
                table: "MfNcRuns");

            migrationBuilder.DropIndex(
                name: "IX_MfNcRevisions_PostProcessorId",
                table: "MfNcRevisions");

            migrationBuilder.DropColumn(
                name: "OverrideId",
                table: "MfNcRuns");

            migrationBuilder.DropColumn(
                name: "ProveOut",
                table: "MfNcRuns");

            migrationBuilder.DropColumn(
                name: "ProveOutObserved",
                table: "MfNcRuns");

            migrationBuilder.DropColumn(
                name: "ToolsVerified",
                table: "MfNcRuns");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "MfNcRuns");

            migrationBuilder.DropColumn(
                name: "VerifiedSha256",
                table: "MfNcRuns");

            migrationBuilder.DropColumn(
                name: "CamSystem",
                table: "MfNcRevisions");

            migrationBuilder.DropColumn(
                name: "PostProcessorId",
                table: "MfNcRevisions");

            migrationBuilder.DropColumn(
                name: "SimulationAttachmentId",
                table: "MfNcRevisions");

            migrationBuilder.DropColumn(
                name: "ToolCount",
                table: "MfNcRevisions");

            migrationBuilder.DropColumn(
                name: "Capabilities",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "DataCredentialKey",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "DataError",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "DataFailedPolls",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "DataHost",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "DataLink",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "DataPort",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "DataSeenAt",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "ExecMode",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "InterlockCoil",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "InterlockEnabled",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "InterlockError",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "InterlockHost",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "InterlockInvert",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "InterlockPort",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "InterlockUnitId",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "LastBlockNumber",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "LastProgramSha256",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "LastProgramShaAt",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "LastSpindleOverride",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "LastToolNumber",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "PlcMap",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "ProgramFolder",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "ProgramState",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "ResultFile",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "ResultReadAt",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "StartBlockReason",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "StartEnabled",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "StartEnabledAt",
                table: "MfMachineMonitors");

            migrationBuilder.DropColumn(
                name: "Characteristic",
                table: "MfChecklistItems");

            migrationBuilder.DropColumn(
                name: "DeviceOnly",
                table: "MfChecklistItems");

            migrationBuilder.DropColumn(
                name: "MeasurementId",
                table: "MfChecklistAnswers");
        }
    }
}
