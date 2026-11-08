using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class InspectionReleasePB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CharacteristicListId",
                table: "QaRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DrawingRevisionId",
                table: "QaRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CharacteristicId",
                table: "QaMeasurements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CharacteristicNo",
                table: "QaMeasurements",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QaAsBuiltRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ReleaseNoteId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    SerialUnitId = table.Column<int>(type: "int", nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProductCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BomText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SupersededByVersion = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaAsBuiltRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaCharacteristicLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliverableId = table.Column<int>(type: "int", nullable: false),
                    RevisionId = table.Column<int>(type: "int", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RevisionCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SupersedesListId = table.Column<int>(type: "int", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ReturnNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaCharacteristicLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaFirstArticles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    PartNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ProductionOrderId = table.Column<int>(type: "int", nullable: true),
                    Fingerprint = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    FingerprintText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CharacteristicListId = table.Column<int>(type: "int", nullable: true),
                    RoutingRevisionId = table.Column<int>(type: "int", nullable: true),
                    Trigger = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    QcRecordId = table.Column<int>(type: "int", nullable: true),
                    MeasuredByUserId = table.Column<int>(type: "int", nullable: true),
                    MeasuredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    QcAcceptedByUserId = table.Column<int>(type: "int", nullable: true),
                    QcAcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    QcAcceptNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    QaApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    QaApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    QaApproveNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_QaFirstArticles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaNoDrawingFlags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    PartNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedByUserId = table.Column<int>(type: "int", nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaNoDrawingFlags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaCharacteristics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ListId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    No = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Nominal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TolPlus = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TolMinus = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Class = table.Column<int>(type: "int", nullable: false),
                    InstrumentClass = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SamplePercent = table.Column<int>(type: "int", nullable: false),
                    OperationSeq = table.Column<int>(type: "int", nullable: true),
                    StageCode2 = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaCharacteristics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaCharacteristics_QaCharacteristicLists_ListId",
                        column: x => x.ListId,
                        principalTable: "QaCharacteristicLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QaRecords_CharacteristicListId",
                table: "QaRecords",
                column: "CharacteristicListId");

            migrationBuilder.CreateIndex(
                name: "IX_QaMeasurements_CharacteristicId",
                table: "QaMeasurements",
                column: "CharacteristicId");

            migrationBuilder.CreateIndex(
                name: "IX_QaAsBuiltRecords_ItemId_SerialNumber_Version",
                table: "QaAsBuiltRecords",
                columns: new[] { "ItemId", "SerialNumber", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_QaAsBuiltRecords_WorkOrderId_ReleaseNoteId",
                table: "QaAsBuiltRecords",
                columns: new[] { "WorkOrderId", "ReleaseNoteId" });

            migrationBuilder.CreateIndex(
                name: "UX_QaAsBuiltRecords_CurrentPerUnit",
                table: "QaAsBuiltRecords",
                columns: new[] { "WorkOrderId", "ItemId", "SerialNumber" },
                unique: true,
                filter: "[Deleted] = 0 AND [Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaCharacteristicLists_DeliverableId_Status",
                table: "QaCharacteristicLists",
                columns: new[] { "DeliverableId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QaCharacteristicLists_RevisionId_Version",
                table: "QaCharacteristicLists",
                columns: new[] { "RevisionId", "Version" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaCharacteristics_ListId_No",
                table: "QaCharacteristics",
                columns: new[] { "ListId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaFirstArticles_ItemId_Fingerprint_Status",
                table: "QaFirstArticles",
                columns: new[] { "ItemId", "Fingerprint", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QaFirstArticles_Number",
                table: "QaFirstArticles",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaFirstArticles_ProductionOrderId",
                table: "QaFirstArticles",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "UX_QaFirstArticles_LivePerConfiguration",
                table: "QaFirstArticles",
                columns: new[] { "ItemId", "Fingerprint" },
                unique: true,
                filter: "[Deleted] = 0 AND [Status] IN (0, 1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "UX_QaNoDrawingFlags_LivePerItem",
                table: "QaNoDrawingFlags",
                column: "ItemId",
                unique: true,
                filter: "[Deleted] = 0 AND [Status] IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QaAsBuiltRecords");

            migrationBuilder.DropTable(
                name: "QaCharacteristics");

            migrationBuilder.DropTable(
                name: "QaFirstArticles");

            migrationBuilder.DropTable(
                name: "QaNoDrawingFlags");

            migrationBuilder.DropTable(
                name: "QaCharacteristicLists");

            migrationBuilder.DropIndex(
                name: "IX_QaRecords_CharacteristicListId",
                table: "QaRecords");

            migrationBuilder.DropIndex(
                name: "IX_QaMeasurements_CharacteristicId",
                table: "QaMeasurements");

            migrationBuilder.DropColumn(
                name: "CharacteristicListId",
                table: "QaRecords");

            migrationBuilder.DropColumn(
                name: "DrawingRevisionId",
                table: "QaRecords");

            migrationBuilder.DropColumn(
                name: "CharacteristicId",
                table: "QaMeasurements");

            migrationBuilder.DropColumn(
                name: "CharacteristicNo",
                table: "QaMeasurements");
        }
    }
}
