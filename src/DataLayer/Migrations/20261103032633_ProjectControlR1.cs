using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ProjectControlR1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PcBaselineCostPeriods_BaselineId",
                table: "PcBaselineCostPeriods");

            migrationBuilder.AddColumn<bool>(
                name: "Scored",
                table: "ProcSupplierEvaluations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Basis",
                table: "ProcSupplierEvaluationLines",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TechnicalReviewedAt",
                table: "ProcQuotes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TechnicalReviewedByUserId",
                table: "ProcQuotes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuoteId",
                table: "ProcPurchaseOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevisedAt",
                table: "ProcPurchaseOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisedByUserId",
                table: "ProcPurchaseOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "ProcPurchaseOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RevisionReason",
                table: "ProcPurchaseOrders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeductionsConfirmedAt",
                table: "ProcPaymentRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeductionsConfirmedByUserId",
                table: "ProcPaymentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeductionsProposedAt",
                table: "ProcPaymentRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "PcWbsNodes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "PcVariationOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByRole",
                table: "PcVariationOrders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByUserId",
                table: "PcVariationOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientDocumentId",
                table: "PcVariationOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClientLetterId",
                table: "PcVariationOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionNote",
                table: "PcVariationOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmittedAt",
                table: "PcVariationOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmittedByUserId",
                table: "PcVariationOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TermsAppliedAt",
                table: "PcVariationOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CommissionedAt",
                table: "PcShipments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InventoryDocumentId",
                table: "PcShipments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PackedAt",
                table: "PcShipments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackedByUserId",
                table: "PcShipments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PackingCheckedAt",
                table: "PcShipments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackingCheckedByUserId",
                table: "PcShipments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "PcProjectPlans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFinal",
                table: "PcPaymentMilestones",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProgressDate",
                table: "PcManualProgress",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PenaltyBasis",
                table: "PcContractTerms",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TermsFrozenAt",
                table: "PcContractTerms",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarrantyMonthsAfterCommissioning",
                table: "PcContractTerms",
                type: "int",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<int>(
                name: "WarrantyMonthsAfterDelivery",
                table: "PcContractTerms",
                type: "int",
                nullable: false,
                defaultValue: 18);

            migrationBuilder.AddColumn<int>(
                name: "AmendmentId",
                table: "PcContractReviews",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CommercialAcceptedAt",
                table: "PcContractReviews",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommercialAcceptedByUserId",
                table: "PcContractReviews",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "PcContractReviews",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PmoAcceptedAt",
                table: "PcContractReviews",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PmoAcceptedByUserId",
                table: "PcContractReviews",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Stage",
                table: "PcContractReviews",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "VariationOrderId",
                table: "PcContractReviews",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TermsAppliedAt",
                table: "PcContractAmendments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ResetsProgress",
                table: "PcClientResponseCodes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<decimal>(
                name: "PlannedCumulative",
                table: "PcBaselineCostPeriods",
                type: "decimal(22,4)",
                precision: 22,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(22,2)",
                oldPrecision: 22,
                oldScale: 2);

            migrationBuilder.AddColumn<int>(
                name: "CalendarId",
                table: "PcBaselineCostPeriods",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkingOffset",
                table: "PcBaselineCostPeriods",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "EffectiveWeight",
                table: "PcBaselineActivities",
                type: "decimal(12,8)",
                precision: 12,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProductionForecastFinish",
                table: "PcActivities",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PcCostChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CostEntryId = table.Column<int>(type: "int", nullable: false),
                    CheckedByUserId = table.Column<int>(type: "int", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcCostChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcProgressEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProgressEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcShipmentLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    SerialUnitId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcShipmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcShipmentLines_PcShipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "PcShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcSnapshotRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    DataDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SnapshotId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcSnapshotRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcSnapshotRequests_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcPurchaseOrderRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StatusWhenFrozen = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FrozenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FrozenByUserId = table.Column<int>(type: "int", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcPurchaseOrderRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseOrderRevisions_ProcPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "ProcPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PcContractReviews_ContractId_Stage",
                table: "PcContractReviews",
                columns: new[] { "ContractId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_PcBaselineCostPeriods_BaselineId_CalendarId_PeriodStart",
                table: "PcBaselineCostPeriods",
                columns: new[] { "BaselineId", "CalendarId", "PeriodStart" },
                unique: true,
                filter: "[CalendarId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PcCostChecks_CostEntryId",
                table: "PcCostChecks",
                column: "CostEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressEvents_ActivityId_EventDate_Id",
                table: "PcProgressEvents",
                columns: new[] { "ActivityId", "EventDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressEvents_ContractId_EventDate",
                table: "PcProgressEvents",
                columns: new[] { "ContractId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PcShipmentLines_ShipmentId",
                table: "PcShipmentLines",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PcSnapshotRequests_ContractId_DataDate",
                table: "PcSnapshotRequests",
                columns: new[] { "ContractId", "DataDate" },
                unique: true,
                filter: "[Deleted] = 0 AND [State] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseOrderRevisions_PurchaseOrderId_Revision",
                table: "ProcPurchaseOrderRevisions",
                columns: new[] { "PurchaseOrderId", "Revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PcCostChecks");

            migrationBuilder.DropTable(
                name: "PcProgressEvents");

            migrationBuilder.DropTable(
                name: "PcShipmentLines");

            migrationBuilder.DropTable(
                name: "PcSnapshotRequests");

            migrationBuilder.DropTable(
                name: "ProcPurchaseOrderRevisions");

            migrationBuilder.DropIndex(
                name: "IX_PcContractReviews_ContractId_Stage",
                table: "PcContractReviews");

            migrationBuilder.DropIndex(
                name: "IX_PcBaselineCostPeriods_BaselineId_CalendarId_PeriodStart",
                table: "PcBaselineCostPeriods");

            migrationBuilder.DropColumn(
                name: "Scored",
                table: "ProcSupplierEvaluations");

            migrationBuilder.DropColumn(
                name: "Basis",
                table: "ProcSupplierEvaluationLines");

            migrationBuilder.DropColumn(
                name: "TechnicalReviewedAt",
                table: "ProcQuotes");

            migrationBuilder.DropColumn(
                name: "TechnicalReviewedByUserId",
                table: "ProcQuotes");

            migrationBuilder.DropColumn(
                name: "QuoteId",
                table: "ProcPurchaseOrders");

            migrationBuilder.DropColumn(
                name: "RevisedAt",
                table: "ProcPurchaseOrders");

            migrationBuilder.DropColumn(
                name: "RevisedByUserId",
                table: "ProcPurchaseOrders");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "ProcPurchaseOrders");

            migrationBuilder.DropColumn(
                name: "RevisionReason",
                table: "ProcPurchaseOrders");

            migrationBuilder.DropColumn(
                name: "DeductionsConfirmedAt",
                table: "ProcPaymentRequests");

            migrationBuilder.DropColumn(
                name: "DeductionsConfirmedByUserId",
                table: "ProcPaymentRequests");

            migrationBuilder.DropColumn(
                name: "DeductionsProposedAt",
                table: "ProcPaymentRequests");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "PcWbsNodes");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "PcVariationOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedByRole",
                table: "PcVariationOrders");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "PcVariationOrders");

            migrationBuilder.DropColumn(
                name: "ClientDocumentId",
                table: "PcVariationOrders");

            migrationBuilder.DropColumn(
                name: "ClientLetterId",
                table: "PcVariationOrders");

            migrationBuilder.DropColumn(
                name: "DecisionNote",
                table: "PcVariationOrders");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "PcVariationOrders");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "PcVariationOrders");

            migrationBuilder.DropColumn(
                name: "TermsAppliedAt",
                table: "PcVariationOrders");

            migrationBuilder.DropColumn(
                name: "CommissionedAt",
                table: "PcShipments");

            migrationBuilder.DropColumn(
                name: "InventoryDocumentId",
                table: "PcShipments");

            migrationBuilder.DropColumn(
                name: "PackedAt",
                table: "PcShipments");

            migrationBuilder.DropColumn(
                name: "PackedByUserId",
                table: "PcShipments");

            migrationBuilder.DropColumn(
                name: "PackingCheckedAt",
                table: "PcShipments");

            migrationBuilder.DropColumn(
                name: "PackingCheckedByUserId",
                table: "PcShipments");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "PcProjectPlans");

            migrationBuilder.DropColumn(
                name: "IsFinal",
                table: "PcPaymentMilestones");

            migrationBuilder.DropColumn(
                name: "ProgressDate",
                table: "PcManualProgress");

            migrationBuilder.DropColumn(
                name: "PenaltyBasis",
                table: "PcContractTerms");

            migrationBuilder.DropColumn(
                name: "TermsFrozenAt",
                table: "PcContractTerms");

            migrationBuilder.DropColumn(
                name: "WarrantyMonthsAfterCommissioning",
                table: "PcContractTerms");

            migrationBuilder.DropColumn(
                name: "WarrantyMonthsAfterDelivery",
                table: "PcContractTerms");

            migrationBuilder.DropColumn(
                name: "AmendmentId",
                table: "PcContractReviews");

            migrationBuilder.DropColumn(
                name: "CommercialAcceptedAt",
                table: "PcContractReviews");

            migrationBuilder.DropColumn(
                name: "CommercialAcceptedByUserId",
                table: "PcContractReviews");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "PcContractReviews");

            migrationBuilder.DropColumn(
                name: "PmoAcceptedAt",
                table: "PcContractReviews");

            migrationBuilder.DropColumn(
                name: "PmoAcceptedByUserId",
                table: "PcContractReviews");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "PcContractReviews");

            migrationBuilder.DropColumn(
                name: "VariationOrderId",
                table: "PcContractReviews");

            migrationBuilder.DropColumn(
                name: "TermsAppliedAt",
                table: "PcContractAmendments");

            migrationBuilder.DropColumn(
                name: "ResetsProgress",
                table: "PcClientResponseCodes");

            migrationBuilder.DropColumn(
                name: "CalendarId",
                table: "PcBaselineCostPeriods");

            migrationBuilder.DropColumn(
                name: "WorkingOffset",
                table: "PcBaselineCostPeriods");

            migrationBuilder.DropColumn(
                name: "EffectiveWeight",
                table: "PcBaselineActivities");

            migrationBuilder.DropColumn(
                name: "ProductionForecastFinish",
                table: "PcActivities");

            migrationBuilder.AlterColumn<decimal>(
                name: "PlannedCumulative",
                table: "PcBaselineCostPeriods",
                type: "decimal(22,2)",
                precision: 22,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(22,4)",
                oldPrecision: 22,
                oldScale: 4);

            migrationBuilder.CreateIndex(
                name: "IX_PcBaselineCostPeriods_BaselineId",
                table: "PcBaselineCostPeriods",
                column: "BaselineId");
        }
    }
}
