using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class QualityR1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DoneAt",
                table: "QaReviewDecisions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DoneByUserId",
                table: "QaReviewDecisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoneNote",
                table: "QaReviewDecisions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoneRequestNote",
                table: "QaReviewDecisions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DoneRequestedAt",
                table: "QaReviewDecisions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DoneRequestedByUserId",
                table: "QaReviewDecisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "QaReviewDecisions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "QaReleaseNotes",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReinspectionKind",
                table: "QaOotItems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReinspectionRecordId",
                table: "QaOotItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "QaItps",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceDocumentId",
                table: "QaItpActivities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceNote",
                table: "QaItpActivities",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotApplicableLetterId",
                table: "QaItpActivities",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotApplicablePending",
                table: "QaItpActivities",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NotApplicableReference",
                table: "QaItpActivities",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotificationLetterId",
                table: "QaItpActivities",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CalibrationWaivedAt",
                table: "QaEquipment",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CalibrationWaivedByUserId",
                table: "QaEquipment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalibrationWaiverReason",
                table: "QaEquipment",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalibrationWaiverRequestReason",
                table: "QaEquipment",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CalibrationWaiverRequestedAt",
                table: "QaEquipment",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CalibrationWaiverRequestedByUserId",
                table: "QaEquipment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "QaCapas",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "QaCapas",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledByUserId",
                table: "QaCapas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "QaCapas",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EscalatedAt",
                table: "QaCapas",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EscalationLevel",
                table: "QaCapas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Iteration",
                table: "QaCapas",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "Mandatory",
                table: "QaCapas",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Iteration",
                table: "QaCapaActions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QualityApprovedAt",
                table: "InvStockCounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualityApprovedByUserId",
                table: "InvStockCounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EquipmentIds",
                table: "InvQcInspections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeasurementsJson",
                table: "InvQcInspections",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReinspectionOfNcrId",
                table: "InvQcInspections",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "InvNonConformances",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "InvNonConformances",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledByUserId",
                table: "InvNonConformances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "InvNonConformances",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReinspectionKind",
                table: "InvNonConformances",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReinspectionRecordId",
                table: "InvNonConformances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReworkOperationId",
                table: "InvNonConformances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScrapDocumentId",
                table: "InvNonConformances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScrappedSerialUnitId",
                table: "InvNonConformances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WriteOffNcrId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ZeroPriceApprovedAt",
                table: "InvDocuments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZeroPriceApprovedByUserId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UploadedByUserId",
                table: "Documents",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvItemQualityChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    RequiresIncomingInspection = table.Column<bool>(type: "bit", nullable: false),
                    RequiredCertificateType = table.Column<int>(type: "int", nullable: true),
                    ShelfLifeDays = table.Column<int>(type: "int", nullable: true),
                    OldRequiresIncomingInspection = table.Column<bool>(type: "bit", nullable: false),
                    OldRequiredCertificateType = table.Column<int>(type: "int", nullable: true),
                    OldShelfLifeDays = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvItemQualityChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvItemQualityChanges_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaReleaseNoteLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReleaseNoteId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SerialNumbers = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaReleaseNoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaReleaseNoteLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaReleaseNoteLines_QaReleaseNotes_ReleaseNoteId",
                        column: x => x.ReleaseNoteId,
                        principalTable: "QaReleaseNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QaItpActivities_EvidenceDocumentId",
                table: "QaItpActivities",
                column: "EvidenceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvQcInspections_ReinspectionOfNcrId",
                table: "InvQcInspections",
                column: "ReinspectionOfNcrId");

            migrationBuilder.CreateIndex(
                name: "IX_InvItemQualityChanges_ItemId_Status",
                table: "InvItemQualityChanges",
                columns: new[] { "ItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QaReleaseNoteLines_ItemId",
                table: "QaReleaseNoteLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QaReleaseNoteLines_ReleaseNoteId",
                table: "QaReleaseNoteLines",
                column: "ReleaseNoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvItemQualityChanges");

            migrationBuilder.DropTable(
                name: "QaReleaseNoteLines");

            migrationBuilder.DropIndex(
                name: "IX_QaItpActivities_EvidenceDocumentId",
                table: "QaItpActivities");

            migrationBuilder.DropIndex(
                name: "IX_InvQcInspections_ReinspectionOfNcrId",
                table: "InvQcInspections");

            migrationBuilder.DropColumn(
                name: "DoneAt",
                table: "QaReviewDecisions");

            migrationBuilder.DropColumn(
                name: "DoneByUserId",
                table: "QaReviewDecisions");

            migrationBuilder.DropColumn(
                name: "DoneNote",
                table: "QaReviewDecisions");

            migrationBuilder.DropColumn(
                name: "DoneRequestNote",
                table: "QaReviewDecisions");

            migrationBuilder.DropColumn(
                name: "DoneRequestedAt",
                table: "QaReviewDecisions");

            migrationBuilder.DropColumn(
                name: "DoneRequestedByUserId",
                table: "QaReviewDecisions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "QaReviewDecisions");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "QaReleaseNotes");

            migrationBuilder.DropColumn(
                name: "ReinspectionKind",
                table: "QaOotItems");

            migrationBuilder.DropColumn(
                name: "ReinspectionRecordId",
                table: "QaOotItems");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "QaItps");

            migrationBuilder.DropColumn(
                name: "EvidenceDocumentId",
                table: "QaItpActivities");

            migrationBuilder.DropColumn(
                name: "EvidenceNote",
                table: "QaItpActivities");

            migrationBuilder.DropColumn(
                name: "NotApplicableLetterId",
                table: "QaItpActivities");

            migrationBuilder.DropColumn(
                name: "NotApplicablePending",
                table: "QaItpActivities");

            migrationBuilder.DropColumn(
                name: "NotApplicableReference",
                table: "QaItpActivities");

            migrationBuilder.DropColumn(
                name: "NotificationLetterId",
                table: "QaItpActivities");

            migrationBuilder.DropColumn(
                name: "CalibrationWaivedAt",
                table: "QaEquipment");

            migrationBuilder.DropColumn(
                name: "CalibrationWaivedByUserId",
                table: "QaEquipment");

            migrationBuilder.DropColumn(
                name: "CalibrationWaiverReason",
                table: "QaEquipment");

            migrationBuilder.DropColumn(
                name: "CalibrationWaiverRequestReason",
                table: "QaEquipment");

            migrationBuilder.DropColumn(
                name: "CalibrationWaiverRequestedAt",
                table: "QaEquipment");

            migrationBuilder.DropColumn(
                name: "CalibrationWaiverRequestedByUserId",
                table: "QaEquipment");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "QaCapas");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "QaCapas");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "QaCapas");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "QaCapas");

            migrationBuilder.DropColumn(
                name: "EscalatedAt",
                table: "QaCapas");

            migrationBuilder.DropColumn(
                name: "EscalationLevel",
                table: "QaCapas");

            migrationBuilder.DropColumn(
                name: "Iteration",
                table: "QaCapas");

            migrationBuilder.DropColumn(
                name: "Mandatory",
                table: "QaCapas");

            migrationBuilder.DropColumn(
                name: "Iteration",
                table: "QaCapaActions");

            migrationBuilder.DropColumn(
                name: "QualityApprovedAt",
                table: "InvStockCounts");

            migrationBuilder.DropColumn(
                name: "QualityApprovedByUserId",
                table: "InvStockCounts");

            migrationBuilder.DropColumn(
                name: "EquipmentIds",
                table: "InvQcInspections");

            migrationBuilder.DropColumn(
                name: "MeasurementsJson",
                table: "InvQcInspections");

            migrationBuilder.DropColumn(
                name: "ReinspectionOfNcrId",
                table: "InvQcInspections");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "ReinspectionKind",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "ReinspectionRecordId",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "ReworkOperationId",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "ScrapDocumentId",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "ScrappedSerialUnitId",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "WriteOffNcrId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "ZeroPriceApprovedAt",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "ZeroPriceApprovedByUserId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "UploadedByUserId",
                table: "Documents");
        }
    }
}
