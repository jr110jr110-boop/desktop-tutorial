using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class GoLiveCutOverPG : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LegacyNote",
                table: "QaRecords",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DrawingCitedAt",
                table: "ProcPurchaseOrderLines",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DrawingDeliverableId",
                table: "ProcPurchaseOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DrawingNumber",
                table: "ProcPurchaseOrderLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DrawingRevision",
                table: "ProcPurchaseOrderLines",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DrawingRevisionId",
                table: "ProcPurchaseOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SupplierConfirmedAt",
                table: "ProcPurchaseOrderLines",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierConfirmedByUserId",
                table: "ProcPurchaseOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierConfirmedRef",
                table: "ProcPurchaseOrderLines",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierConfirmedRevision",
                table: "ProcPurchaseOrderLines",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierConfirmedRevisionId",
                table: "ProcPurchaseOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LegacyFreeText",
                table: "PcTransmittalLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GlActivations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Slot = table.Column<int>(type: "int", nullable: false),
                    DriftBaselineAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlActivations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlCharAdoptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliverableId = table.Column<int>(type: "int", nullable: false),
                    RevisionId = table.Column<int>(type: "int", nullable: false),
                    ListId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EvidenceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedByUserId = table.Column<int>(type: "int", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlCharAdoptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlConfigAdoptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    BomHeaderId = table.Column<int>(type: "int", nullable: false),
                    BaselineId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EvidenceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ComparisonNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LegacyLineCount = table.Column<int>(type: "int", nullable: false),
                    BankLineCount = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EngReviewerUserId = table.Column<int>(type: "int", nullable: true),
                    EngReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EngNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PmoReviewerUserId = table.Column<int>(type: "int", nullable: true),
                    PmoReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PmoNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlConfigAdoptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlLegacyUses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ObjectKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    RevisionId = table.Column<int>(type: "int", nullable: true),
                    Number = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    UsedByUserId = table.Column<int>(type: "int", nullable: true),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlLegacyUses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlPrintLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RevisionId = table.Column<int>(type: "int", nullable: false),
                    DeliverableId = table.Column<int>(type: "int", nullable: false),
                    SourceFile = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RevisionCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Controlled = table.Column<bool>(type: "bit", nullable: false),
                    CopyNumber = table.Column<int>(type: "int", nullable: false),
                    Header = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    PrintedByUserId = table.Column<int>(type: "int", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StampedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SourceSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OutputSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlPrintLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseOrderLines_DrawingDeliverableId",
                table: "ProcPurchaseOrderLines",
                column: "DrawingDeliverableId");

            migrationBuilder.CreateIndex(
                name: "UX_GlActivations_Slot",
                table: "GlActivations",
                column: "Slot",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GlCharAdoptions_DeliverableId",
                table: "GlCharAdoptions",
                column: "DeliverableId");

            migrationBuilder.CreateIndex(
                name: "UX_GlCharAdoptions_LivePerRevision",
                table: "GlCharAdoptions",
                column: "RevisionId",
                unique: true,
                filter: "[Deleted] = 0 AND [Status] IN (0, 2)");

            migrationBuilder.CreateIndex(
                name: "UX_GlConfigAdoptions_LivePerWorkOrder",
                table: "GlConfigAdoptions",
                column: "WorkOrderId",
                unique: true,
                filter: "[Deleted] = 0 AND [Status] IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_GlLegacyUses_Kind_UsedAt",
                table: "GlLegacyUses",
                columns: new[] { "Kind", "UsedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_GlLegacyUses_KindObject",
                table: "GlLegacyUses",
                columns: new[] { "Kind", "ObjectKey" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_GlPrintLogs_PrintedByUserId",
                table: "GlPrintLogs",
                column: "PrintedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_GlPrintLogs_RevisionCopy",
                table: "GlPrintLogs",
                columns: new[] { "RevisionId", "CopyNumber" },
                unique: true,
                filter: "[Controlled] = 1 AND [Deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlActivations");

            migrationBuilder.DropTable(
                name: "GlCharAdoptions");

            migrationBuilder.DropTable(
                name: "GlConfigAdoptions");

            migrationBuilder.DropTable(
                name: "GlLegacyUses");

            migrationBuilder.DropTable(
                name: "GlPrintLogs");

            migrationBuilder.DropIndex(
                name: "IX_ProcPurchaseOrderLines_DrawingDeliverableId",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "LegacyNote",
                table: "QaRecords");

            migrationBuilder.DropColumn(
                name: "DrawingCitedAt",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "DrawingDeliverableId",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "DrawingNumber",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "DrawingRevision",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "DrawingRevisionId",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "SupplierConfirmedAt",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "SupplierConfirmedByUserId",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "SupplierConfirmedRef",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "SupplierConfirmedRevision",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "SupplierConfirmedRevisionId",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "LegacyFreeText",
                table: "PcTransmittalLines");
        }
    }
}
