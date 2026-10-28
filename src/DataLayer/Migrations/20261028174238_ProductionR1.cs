using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ProductionR1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SvServiceOrders_LabourPendingPeriod",
                table: "SvServiceOrders");

            migrationBuilder.AddColumn<decimal>(
                name: "LabourPayrollCost",
                table: "SvServiceOrders",
                type: "decimal(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LabourPooledAmount",
                table: "SvServiceOrders",
                type: "decimal(22,2)",
                precision: 22,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnsweredByUserId",
                table: "SlSurveys",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ComplaintId",
                table: "SlSurveys",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordedByRole",
                table: "SlSurveys",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkipReason",
                table: "SlSurveys",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Verified",
                table: "SlSurveys",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerifiedAt",
                table: "SlSurveys",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerifiedByUserId",
                table: "SlSurveys",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifyNote",
                table: "SlSurveys",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresMachine",
                table: "MfWorkCenters",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AbsorbedLabour",
                table: "MfTimeBookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CorrectionApprovedAt",
                table: "MfTimeBookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectionApprovedByUserId",
                table: "MfTimeBookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectsBookingId",
                table: "MfTimeBookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCorrection",
                table: "MfTimeBookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LabourAggregatedAt",
                table: "MfTimeBookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LabourPendingPeriod",
                table: "MfTimeBookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PayrollCost",
                table: "MfTimeBookings",
                type: "decimal(28,10)",
                precision: 28,
                scale: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RateFromPayroll",
                table: "MfTimeBookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReplacesOrderId",
                table: "MfProductionOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WrittenOffQty",
                table: "MfProductionOrders",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsWelding",
                table: "MfOperationStandards",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WpsId",
                table: "MfOperationStandards",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptedByConcession",
                table: "MfOperations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsWelding",
                table: "MfOperations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "QcAcceptedGoodQty",
                table: "MfOperations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QcAcceptedReworkQty",
                table: "MfOperations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QcAcceptedScrapQty",
                table: "MfOperations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeldLogId",
                table: "MfOperations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WpsId",
                table: "MfOperations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LabourProtected",
                table: "FnVoucherLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LabourProtected",
                table: "FinCostEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LabourProtected",
                table: "FinAccountingEventLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MfLabourPools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Module = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    PoolKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    People = table.Column<int>(type: "int", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Absorbed = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Variance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Protected = table.Column<bool>(type: "bit", nullable: false),
                    Items = table.Column<int>(type: "int", nullable: false),
                    AccountingEventId = table.Column<int>(type: "int", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfLabourPools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfLabourPools_FinAccountingEvents_AccountingEventId",
                        column: x => x.AccountingEventId,
                        principalTable: "FinAccountingEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_LabourPendingPeriod",
                table: "SvServiceOrders",
                column: "LabourPendingPeriod",
                filter: "[LabourPendingPeriod] IS NOT NULL AND [LabourAggregatedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SlSurveys_ComplaintId",
                table: "SlSurveys",
                column: "ComplaintId",
                unique: true,
                filter: "[ComplaintId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MfTimeBookings_CorrectsBookingId",
                table: "MfTimeBookings",
                column: "CorrectsBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_MfTimeBookings_LabourPendingPeriod",
                table: "MfTimeBookings",
                column: "LabourPendingPeriod",
                filter: "[LabourPendingPeriod] IS NOT NULL AND [LabourAggregatedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_ReplacesOrderId",
                table: "MfProductionOrders",
                column: "ReplacesOrderId",
                filter: "[ReplacesOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperationStandards_WpsId",
                table: "MfOperationStandards",
                column: "WpsId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_WeldLogId",
                table: "MfOperations",
                column: "WeldLogId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_WpsId",
                table: "MfOperations",
                column: "WpsId");

            migrationBuilder.CreateIndex(
                name: "IX_InvNonConformances_ReworkOperationId",
                table: "InvNonConformances",
                column: "ReworkOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_MfLabourPools_AccountingEventId",
                table: "MfLabourPools",
                column: "AccountingEventId");

            migrationBuilder.CreateIndex(
                name: "IX_MfLabourPools_Module_Period_PoolKey",
                table: "MfLabourPools",
                columns: new[] { "Module", "Period", "PoolKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InvNonConformances_MfOperations_ReworkOperationId",
                table: "InvNonConformances",
                column: "ReworkOperationId",
                principalTable: "MfOperations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MfOperations_QaWeldLogs_WeldLogId",
                table: "MfOperations",
                column: "WeldLogId",
                principalTable: "QaWeldLogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MfOperations_QaWps_WpsId",
                table: "MfOperations",
                column: "WpsId",
                principalTable: "QaWps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MfOperationStandards_QaWps_WpsId",
                table: "MfOperationStandards",
                column: "WpsId",
                principalTable: "QaWps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MfProductionOrders_MfProductionOrders_ReplacesOrderId",
                table: "MfProductionOrders",
                column: "ReplacesOrderId",
                principalTable: "MfProductionOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MfTimeBookings_MfTimeBookings_CorrectsBookingId",
                table: "MfTimeBookings",
                column: "CorrectsBookingId",
                principalTable: "MfTimeBookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SlSurveys_QaComplaints_ComplaintId",
                table: "SlSurveys",
                column: "ComplaintId",
                principalTable: "QaComplaints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvNonConformances_MfOperations_ReworkOperationId",
                table: "InvNonConformances");

            migrationBuilder.DropForeignKey(
                name: "FK_MfOperations_QaWeldLogs_WeldLogId",
                table: "MfOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_MfOperations_QaWps_WpsId",
                table: "MfOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_MfOperationStandards_QaWps_WpsId",
                table: "MfOperationStandards");

            migrationBuilder.DropForeignKey(
                name: "FK_MfProductionOrders_MfProductionOrders_ReplacesOrderId",
                table: "MfProductionOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_MfTimeBookings_MfTimeBookings_CorrectsBookingId",
                table: "MfTimeBookings");

            migrationBuilder.DropForeignKey(
                name: "FK_SlSurveys_QaComplaints_ComplaintId",
                table: "SlSurveys");

            migrationBuilder.DropTable(
                name: "MfLabourPools");

            migrationBuilder.DropIndex(
                name: "IX_SvServiceOrders_LabourPendingPeriod",
                table: "SvServiceOrders");

            migrationBuilder.DropIndex(
                name: "IX_SlSurveys_ComplaintId",
                table: "SlSurveys");

            migrationBuilder.DropIndex(
                name: "IX_MfTimeBookings_CorrectsBookingId",
                table: "MfTimeBookings");

            migrationBuilder.DropIndex(
                name: "IX_MfTimeBookings_LabourPendingPeriod",
                table: "MfTimeBookings");

            migrationBuilder.DropIndex(
                name: "IX_MfProductionOrders_ReplacesOrderId",
                table: "MfProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_MfOperationStandards_WpsId",
                table: "MfOperationStandards");

            migrationBuilder.DropIndex(
                name: "IX_MfOperations_WeldLogId",
                table: "MfOperations");

            migrationBuilder.DropIndex(
                name: "IX_MfOperations_WpsId",
                table: "MfOperations");

            migrationBuilder.DropIndex(
                name: "IX_InvNonConformances_ReworkOperationId",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "LabourPayrollCost",
                table: "SvServiceOrders");

            migrationBuilder.DropColumn(
                name: "LabourPooledAmount",
                table: "SvServiceOrders");

            migrationBuilder.DropColumn(
                name: "AnsweredByUserId",
                table: "SlSurveys");

            migrationBuilder.DropColumn(
                name: "ComplaintId",
                table: "SlSurveys");

            migrationBuilder.DropColumn(
                name: "RecordedByRole",
                table: "SlSurveys");

            migrationBuilder.DropColumn(
                name: "SkipReason",
                table: "SlSurveys");

            migrationBuilder.DropColumn(
                name: "Verified",
                table: "SlSurveys");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "SlSurveys");

            migrationBuilder.DropColumn(
                name: "VerifiedByUserId",
                table: "SlSurveys");

            migrationBuilder.DropColumn(
                name: "VerifyNote",
                table: "SlSurveys");

            migrationBuilder.DropColumn(
                name: "RequiresMachine",
                table: "MfWorkCenters");

            migrationBuilder.DropColumn(
                name: "AbsorbedLabour",
                table: "MfTimeBookings");

            migrationBuilder.DropColumn(
                name: "CorrectionApprovedAt",
                table: "MfTimeBookings");

            migrationBuilder.DropColumn(
                name: "CorrectionApprovedByUserId",
                table: "MfTimeBookings");

            migrationBuilder.DropColumn(
                name: "CorrectsBookingId",
                table: "MfTimeBookings");

            migrationBuilder.DropColumn(
                name: "IsCorrection",
                table: "MfTimeBookings");

            migrationBuilder.DropColumn(
                name: "LabourAggregatedAt",
                table: "MfTimeBookings");

            migrationBuilder.DropColumn(
                name: "LabourPendingPeriod",
                table: "MfTimeBookings");

            migrationBuilder.DropColumn(
                name: "PayrollCost",
                table: "MfTimeBookings");

            migrationBuilder.DropColumn(
                name: "RateFromPayroll",
                table: "MfTimeBookings");

            migrationBuilder.DropColumn(
                name: "ReplacesOrderId",
                table: "MfProductionOrders");

            migrationBuilder.DropColumn(
                name: "WrittenOffQty",
                table: "MfProductionOrders");

            migrationBuilder.DropColumn(
                name: "IsWelding",
                table: "MfOperationStandards");

            migrationBuilder.DropColumn(
                name: "WpsId",
                table: "MfOperationStandards");

            migrationBuilder.DropColumn(
                name: "AcceptedByConcession",
                table: "MfOperations");

            migrationBuilder.DropColumn(
                name: "IsWelding",
                table: "MfOperations");

            migrationBuilder.DropColumn(
                name: "QcAcceptedGoodQty",
                table: "MfOperations");

            migrationBuilder.DropColumn(
                name: "QcAcceptedReworkQty",
                table: "MfOperations");

            migrationBuilder.DropColumn(
                name: "QcAcceptedScrapQty",
                table: "MfOperations");

            migrationBuilder.DropColumn(
                name: "WeldLogId",
                table: "MfOperations");

            migrationBuilder.DropColumn(
                name: "WpsId",
                table: "MfOperations");

            migrationBuilder.DropColumn(
                name: "LabourProtected",
                table: "FnVoucherLines");

            migrationBuilder.DropColumn(
                name: "LabourProtected",
                table: "FinCostEntries");

            migrationBuilder.DropColumn(
                name: "LabourProtected",
                table: "FinAccountingEventLines");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_LabourPendingPeriod",
                table: "SvServiceOrders",
                column: "LabourPendingPeriod",
                filter: "[LabourPendingPeriod] IS NOT NULL");
        }
    }
}
