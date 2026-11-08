using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class RecordsIntegrityPD : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntAuditChainRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Manual = table.Column<bool>(type: "bit", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FromSeq = table.Column<long>(type: "bigint", nullable: true),
                    ToSeq = table.Column<long>(type: "bigint", nullable: true),
                    EntriesChecked = table.Column<long>(type: "bigint", nullable: false),
                    HeadSeq = table.Column<long>(type: "bigint", nullable: true),
                    HeadHash = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    KeyConfigured = table.Column<bool>(type: "bit", nullable: false),
                    LegacyRows = table.Column<long>(type: "bigint", nullable: false),
                    BreakKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    BreakSeq = table.Column<long>(type: "bigint", nullable: true),
                    BreakRowId = table.Column<int>(type: "int", nullable: true),
                    BreakEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BreakAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BreakColumn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntAuditChainRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntChainBreakAcceptances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BreakRunId = table.Column<int>(type: "int", nullable: false),
                    BreakKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    BreakSeq = table.Column<long>(type: "bigint", nullable: true),
                    BreakRowId = table.Column<int>(type: "int", nullable: true),
                    BreakEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BreakColumn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    QaUserId = table.Column<int>(type: "int", nullable: true),
                    QaAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PmoUserId = table.Column<int>(type: "int", nullable: true),
                    PmoAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CeoUserId = table.Column<int>(type: "int", nullable: true),
                    CeoAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AnchorSeq = table.Column<long>(type: "bigint", nullable: true),
                    AnchorHash = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntChainBreakAcceptances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntRestoreTestResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    BackupFile = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    BackupTakenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MigrationsMatch = table.Column<bool>(type: "bit", nullable: true),
                    ChainOk = table.Column<bool>(type: "bit", nullable: true),
                    ChainEntriesChecked = table.Column<long>(type: "bigint", nullable: true),
                    ScratchDropped = table.Column<bool>(type: "bit", nullable: true),
                    RowCountsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChecksJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntRestoreTestResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntSafetySettingChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Store = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Weakening = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FlaggedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntSafetySettingChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntAuditChainRuns_StartedAt",
                table: "IntAuditChainRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IntAuditChainRuns_Status_Kind",
                table: "IntAuditChainRuns",
                columns: new[] { "Status", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_IntChainBreakAcceptances_BreakRunId",
                table: "IntChainBreakAcceptances",
                column: "BreakRunId");

            migrationBuilder.CreateIndex(
                name: "IX_IntChainBreakAcceptances_Status",
                table: "IntChainBreakAcceptances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IntRestoreTestResults_RunId",
                table: "IntRestoreTestResults",
                column: "RunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntRestoreTestResults_StartedAt",
                table: "IntRestoreTestResults",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IntSafetySettingChanges_Status",
                table: "IntSafetySettingChanges",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IntSafetySettingChanges_Store_Key_Status",
                table: "IntSafetySettingChanges",
                columns: new[] { "Store", "Key", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_IntSafetySettingChanges_OpenPerKey",
                table: "IntSafetySettingChanges",
                columns: new[] { "Store", "Key" },
                unique: true,
                filter: "[Status] IN (0, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntAuditChainRuns");

            migrationBuilder.DropTable(
                name: "IntChainBreakAcceptances");

            migrationBuilder.DropTable(
                name: "IntRestoreTestResults");

            migrationBuilder.DropTable(
                name: "IntSafetySettingChanges");
        }
    }
}
