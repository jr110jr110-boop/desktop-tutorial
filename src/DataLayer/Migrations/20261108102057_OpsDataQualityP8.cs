using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class OpsDataQualityP8 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CertificateWeightKg",
                table: "InvDocumentLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeighedWeightKg",
                table: "InvDocumentLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OdExceptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Unit = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    LineId = table.Column<int>(type: "int", nullable: true),
                    RowNumber = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Field = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RuleCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    EnteredValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ReferenceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    DeviationPercent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsMoney = table.Column<bool>(type: "bit", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Justification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdExceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OdFindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Unit = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    LineId = table.Column<int>(type: "int", nullable: true),
                    RowNumber = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    ItemTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Field = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RuleCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MessageMasked = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EnteredValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ReferenceValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    DeviationPercent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsMoney = table.Column<bool>(type: "bit", nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EnteredByUserId = table.Column<int>(type: "int", nullable: true),
                    ExceptionId = table.Column<int>(type: "int", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrectiveDocumentId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OdReadinessExceptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ItemCodes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Justification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PmoUserId = table.Column<int>(type: "int", nullable: true),
                    PmoAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PmoNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CeoUserId = table.Column<int>(type: "int", nullable: true),
                    CeoAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CeoNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdReadinessExceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OdReadinessObligations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OwnerUnit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Hard = table.Column<bool>(type: "bit", nullable: false),
                    MissingCount = table.Column<int>(type: "int", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExceptionId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastNotifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdReadinessObligations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OdExceptions_DocumentType_DocumentId_RuleCode",
                table: "OdExceptions",
                columns: new[] { "DocumentType", "DocumentId", "RuleCode" });

            migrationBuilder.CreateIndex(
                name: "IX_OdExceptions_OnePending",
                table: "OdExceptions",
                columns: new[] { "DocumentType", "DocumentId", "LineId", "RowNumber", "ItemId", "RuleCode", "Field" },
                unique: true,
                filter: "[Deleted] = 0 AND [Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OdExceptions_RequestedByUserId",
                table: "OdExceptions",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OdExceptions_Unit_Status",
                table: "OdExceptions",
                columns: new[] { "Unit", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OdFindings_DetectedAt",
                table: "OdFindings",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OdFindings_DocumentType_DocumentId_LineId_RowNumber_ItemId_RuleCode_Field",
                table: "OdFindings",
                columns: new[] { "DocumentType", "DocumentId", "LineId", "RowNumber", "ItemId", "RuleCode", "Field" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OdFindings_ExceptionId",
                table: "OdFindings",
                column: "ExceptionId");

            migrationBuilder.CreateIndex(
                name: "IX_OdFindings_RuleCode",
                table: "OdFindings",
                column: "RuleCode");

            migrationBuilder.CreateIndex(
                name: "IX_OdFindings_Unit_Status",
                table: "OdFindings",
                columns: new[] { "Unit", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OdReadinessExceptions_WorkOrderId_Status",
                table: "OdReadinessExceptions",
                columns: new[] { "WorkOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OdReadinessObligations_Status_DueDate",
                table: "OdReadinessObligations",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OdReadinessObligations_WorkOrderId_ItemCode",
                table: "OdReadinessObligations",
                columns: new[] { "WorkOrderId", "ItemCode" },
                unique: true,
                filter: "[Deleted] = 0");

            // CEO decision D13: the four BOM signatures are four distinct persons; a stored "false" from the earlier default is switched to "true"
            migrationBuilder.Sql("UPDATE [PbSettings] SET [Value] = N'true' WHERE [Key] = N'BomSignatures:DistinctReviewers' AND [Value] <> N'true';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OdExceptions");

            migrationBuilder.DropTable(
                name: "OdFindings");

            migrationBuilder.DropTable(
                name: "OdReadinessExceptions");

            migrationBuilder.DropTable(
                name: "OdReadinessObligations");

            migrationBuilder.DropColumn(
                name: "CertificateWeightKg",
                table: "InvDocumentLines");

            migrationBuilder.DropColumn(
                name: "WeighedWeightKg",
                table: "InvDocumentLines");
        }
    }
}
