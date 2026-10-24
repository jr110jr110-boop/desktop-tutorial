using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class FinanceReviewFixes4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FnTerminations_EmployeeId",
                table: "FnTerminations");

            migrationBuilder.DropIndex(
                name: "IX_FnBonusRuns_Year",
                table: "FnBonusRuns");

            migrationBuilder.AddColumn<decimal>(
                name: "InsuranceAmount",
                table: "FnTerminations",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetPayable",
                table: "FnTerminations",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "FnTerminations",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            // terminations already posted before this release: the net payable is the whole amount (they had no tax or insurance lines)
            migrationBuilder.Sql("UPDATE [FnTerminations] SET [NetPayable] = [Total] WHERE [NetPayable] = 0");

            migrationBuilder.AddColumn<decimal>(
                name: "NonRecoverableVatAmount",
                table: "FnSupplierNotes",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "BonusRunId",
                table: "FnPayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TerminationId",
                table: "FnPayments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnTerminations_Employee_Active",
                table: "FnTerminations",
                column: "EmployeeId",
                unique: true,
                filter: "[Status] <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_FnPayments_BonusRunId",
                table: "FnPayments",
                column: "BonusRunId",
                filter: "[BonusRunId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FnPayments_TerminationId",
                table: "FnPayments",
                column: "TerminationId",
                filter: "[TerminationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FnBonusRuns_Year_Active",
                table: "FnBonusRuns",
                column: "Year",
                unique: true,
                filter: "[Status] <> 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FnTerminations_Employee_Active",
                table: "FnTerminations");

            migrationBuilder.DropIndex(
                name: "IX_FnPayments_BonusRunId",
                table: "FnPayments");

            migrationBuilder.DropIndex(
                name: "IX_FnPayments_TerminationId",
                table: "FnPayments");

            migrationBuilder.DropIndex(
                name: "IX_FnBonusRuns_Year_Active",
                table: "FnBonusRuns");

            migrationBuilder.DropColumn(
                name: "InsuranceAmount",
                table: "FnTerminations");

            migrationBuilder.DropColumn(
                name: "NetPayable",
                table: "FnTerminations");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "FnTerminations");

            migrationBuilder.DropColumn(
                name: "NonRecoverableVatAmount",
                table: "FnSupplierNotes");

            migrationBuilder.DropColumn(
                name: "BonusRunId",
                table: "FnPayments");

            migrationBuilder.DropColumn(
                name: "TerminationId",
                table: "FnPayments");

            migrationBuilder.CreateIndex(
                name: "IX_FnTerminations_EmployeeId",
                table: "FnTerminations",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBonusRuns_Year",
                table: "FnBonusRuns",
                column: "Year");
        }
    }
}
