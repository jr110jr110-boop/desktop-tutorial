using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class FinanceReviewFixes2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FnWorkOrderCloses_WorkOrderId",
                table: "FnWorkOrderCloses");

            migrationBuilder.DropIndex(
                name: "IX_FnOverheadAllocations_LegalEntityId_PeriodId",
                table: "FnOverheadAllocations");

            migrationBuilder.DropIndex(
                name: "IX_FnFxRevaluations_LegalEntityId_PeriodId",
                table: "FnFxRevaluations");

            migrationBuilder.AddColumn<int>(
                name: "DeliveredQuantity",
                table: "FnWorkOrderCloses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "FgAmount",
                table: "FnWorkOrderCloses",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RelievedAmount",
                table: "FnWorkOrderCloses",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CostOfSales",
                table: "FnRevenueRecognitions",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentPortionReclassified",
                table: "FnLoans",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountA",
                table: "FnIntercompanyPosts",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountB",
                table: "FnIntercompanyPosts",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Rate",
                table: "FnIntercompanyPosts",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                table: "FnFxRevaluations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_FnWorkOrderCloses_ActiveClose",
                table: "FnWorkOrderCloses",
                column: "WorkOrderId",
                unique: true,
                filter: "[IsOpen] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_FnOverheadAllocations_LegalEntityId_PeriodId",
                table: "FnOverheadAllocations",
                columns: new[] { "LegalEntityId", "PeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnFxRevaluations_LegalEntityId_PeriodId_Sequence",
                table: "FnFxRevaluations",
                columns: new[] { "LegalEntityId", "PeriodId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FnWorkOrderCloses_ActiveClose",
                table: "FnWorkOrderCloses");

            migrationBuilder.DropIndex(
                name: "IX_FnOverheadAllocations_LegalEntityId_PeriodId",
                table: "FnOverheadAllocations");

            migrationBuilder.DropIndex(
                name: "IX_FnFxRevaluations_LegalEntityId_PeriodId_Sequence",
                table: "FnFxRevaluations");

            migrationBuilder.DropColumn(
                name: "DeliveredQuantity",
                table: "FnWorkOrderCloses");

            migrationBuilder.DropColumn(
                name: "FgAmount",
                table: "FnWorkOrderCloses");

            migrationBuilder.DropColumn(
                name: "RelievedAmount",
                table: "FnWorkOrderCloses");

            migrationBuilder.DropColumn(
                name: "CostOfSales",
                table: "FnRevenueRecognitions");

            migrationBuilder.DropColumn(
                name: "CurrentPortionReclassified",
                table: "FnLoans");

            migrationBuilder.DropColumn(
                name: "AmountA",
                table: "FnIntercompanyPosts");

            migrationBuilder.DropColumn(
                name: "AmountB",
                table: "FnIntercompanyPosts");

            migrationBuilder.DropColumn(
                name: "Rate",
                table: "FnIntercompanyPosts");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "FnFxRevaluations");

            migrationBuilder.CreateIndex(
                name: "IX_FnWorkOrderCloses_WorkOrderId",
                table: "FnWorkOrderCloses",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FnOverheadAllocations_LegalEntityId_PeriodId",
                table: "FnOverheadAllocations",
                columns: new[] { "LegalEntityId", "PeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_FnFxRevaluations_LegalEntityId_PeriodId",
                table: "FnFxRevaluations",
                columns: new[] { "LegalEntityId", "PeriodId" });
        }
    }
}
