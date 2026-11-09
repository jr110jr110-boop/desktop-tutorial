using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AuditSeatP9 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AusAcceptances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ObjectKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ObjectNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    QaUserId = table.Column<int>(type: "int", nullable: false),
                    QaAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PmoUserId = table.Column<int>(type: "int", nullable: true),
                    PmoAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CeoUserId = table.Column<int>(type: "int", nullable: true),
                    CeoAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefusalReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusAcceptances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AusAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProgramItemId = table.Column<int>(type: "int", nullable: true),
                    ProcessCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PlannedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeadAuditorUserId = table.Column<int>(type: "int", nullable: false),
                    AuditeeUserId = table.Column<int>(type: "int", nullable: true),
                    AuditeeUnit = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AusFindingEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FindingId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: false),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusFindingEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AusFindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    RuleCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Standard = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Clause = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Procedure = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ProcessCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    BaseSeverity = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ObjectType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ObjectId = table.Column<int>(type: "int", nullable: false),
                    ObjectKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ObjectNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Link = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EvidenceSha256 = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true),
                    OwnerRole = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    OwnerUnit = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RunId = table.Column<int>(type: "int", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SeenCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RootCause = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Correction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CorrectiveAction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CapaId = table.Column<int>(type: "int", nullable: true),
                    ActionByUserId = table.Column<int>(type: "int", nullable: true),
                    ActionAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecheckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecheckPassed = table.Column<bool>(type: "bit", nullable: true),
                    RecheckSha256 = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    QaUserId = table.Column<int>(type: "int", nullable: true),
                    QaAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    QaNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PmoUserId = table.Column<int>(type: "int", nullable: true),
                    PmoAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PmoNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CeoUserId = table.Column<int>(type: "int", nullable: true),
                    CeoAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CeoNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidQaUserId = table.Column<int>(type: "int", nullable: true),
                    VoidPmoUserId = table.Column<int>(type: "int", nullable: true),
                    VoidCeoUserId = table.Column<int>(type: "int", nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecurrenceOfId = table.Column<int>(type: "int", nullable: true),
                    RecurrenceCount = table.Column<int>(type: "int", nullable: false),
                    AuditId = table.Column<int>(type: "int", nullable: true),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AusPrograms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Criteria = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Objectives = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContentSha256 = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SupersedesId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusPrograms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AusReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PeriodFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Recipients = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Narrative = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NarrativeSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Sha256 = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GeneratedByUserId = table.Column<int>(type: "int", nullable: false),
                    RunId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AusRuleStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LastRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFullAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastDurationMs = table.Column<int>(type: "int", nullable: false),
                    LastHits = table.Column<int>(type: "int", nullable: false),
                    LastStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalRuns = table.Column<int>(type: "int", nullable: false),
                    CursorId = table.Column<int>(type: "int", nullable: false),
                    CursorSince = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusRuleStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AusRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RulesRun = table.Column<int>(type: "int", nullable: false),
                    RulesFailed = table.Column<int>(type: "int", nullable: false),
                    Hits = table.Column<int>(type: "int", nullable: false),
                    NewFindings = table.Column<int>(type: "int", nullable: false),
                    Recurrences = table.Column<int>(type: "int", nullable: false),
                    Rechecked = table.Column<int>(type: "int", nullable: false),
                    RecheckFailed = table.Column<int>(type: "int", nullable: false),
                    ResultsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AusAuditChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuditId = table.Column<int>(type: "int", nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    RuleCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Standard = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Clause = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Question = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OpenFindings = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FindingId = table.Column<int>(type: "int", nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CheckedByUserId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusAuditChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AusAuditChecks_AusAudits_AuditId",
                        column: x => x.AuditId,
                        principalTable: "AusAudits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AusProgramItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProgramId = table.Column<int>(type: "int", nullable: false),
                    ProcessCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Frequency = table.Column<int>(type: "int", nullable: false),
                    IntervalDays = table.Column<int>(type: "int", nullable: false),
                    Criteria = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FirstDue = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusProgramItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AusProgramItems_AusPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "AusPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AusAcceptances_RuleCode_ObjectKey",
                table: "AusAcceptances",
                columns: new[] { "RuleCode", "ObjectKey" });

            migrationBuilder.CreateIndex(
                name: "IX_AusAcceptances_Status_ExpiresOn",
                table: "AusAcceptances",
                columns: new[] { "Status", "ExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_AusAuditChecks_AuditId_Sort",
                table: "AusAuditChecks",
                columns: new[] { "AuditId", "Sort" });

            migrationBuilder.CreateIndex(
                name: "IX_AusAudits_Number",
                table: "AusAudits",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AusAudits_Status_PlannedDate",
                table: "AusAudits",
                columns: new[] { "Status", "PlannedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AusFindingEvents_FindingId_At",
                table: "AusFindingEvents",
                columns: new[] { "FindingId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AusFindings_DueDate",
                table: "AusFindings",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_AusFindings_Number",
                table: "AusFindings",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AusFindings_ProcessCode",
                table: "AusFindings",
                column: "ProcessCode");

            migrationBuilder.CreateIndex(
                name: "IX_AusFindings_RecurrenceOfId",
                table: "AusFindings",
                column: "RecurrenceOfId");

            migrationBuilder.CreateIndex(
                name: "IX_AusFindings_RuleCode_ObjectKey",
                table: "AusFindings",
                columns: new[] { "RuleCode", "ObjectKey" },
                unique: true,
                filter: "[Deleted] = 0 AND [Status] <> 6 AND [Status] <> 7");

            migrationBuilder.CreateIndex(
                name: "IX_AusFindings_Status_Severity",
                table: "AusFindings",
                columns: new[] { "Status", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_AusFindings_Year_Sequence",
                table: "AusFindings",
                columns: new[] { "Year", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AusProgramItems_ProgramId_ProcessCode",
                table: "AusProgramItems",
                columns: new[] { "ProgramId", "ProcessCode" });

            migrationBuilder.CreateIndex(
                name: "IX_AusPrograms_Status",
                table: "AusPrograms",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AusPrograms_Year_Version",
                table: "AusPrograms",
                columns: new[] { "Year", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_AusReports_Kind_PeriodFrom",
                table: "AusReports",
                columns: new[] { "Kind", "PeriodFrom" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AusReports_Kind_PeriodTo",
                table: "AusReports",
                columns: new[] { "Kind", "PeriodTo" });

            migrationBuilder.CreateIndex(
                name: "IX_AusRuleStates_RuleCode",
                table: "AusRuleStates",
                column: "RuleCode",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AusRuns_StartedAt",
                table: "AusRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AusRuns_Status",
                table: "AusRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AusAcceptances");

            migrationBuilder.DropTable(
                name: "AusAuditChecks");

            migrationBuilder.DropTable(
                name: "AusFindingEvents");

            migrationBuilder.DropTable(
                name: "AusFindings");

            migrationBuilder.DropTable(
                name: "AusProgramItems");

            migrationBuilder.DropTable(
                name: "AusReports");

            migrationBuilder.DropTable(
                name: "AusRuleStates");

            migrationBuilder.DropTable(
                name: "AusRuns");

            migrationBuilder.DropTable(
                name: "AusAudits");

            migrationBuilder.DropTable(
                name: "AusPrograms");
        }
    }
}
