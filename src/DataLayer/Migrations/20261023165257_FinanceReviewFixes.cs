using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class FinanceReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "FnVouchers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "FnSalesInvoices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "FnReceipts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "FnPettyCashSettlements",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "FnPayrollRuns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SeveranceBase",
                table: "FnPayrollItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByUserId",
                table: "FnPayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "FnPayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmployeeLoanId",
                table: "FnPayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisbursementPaymentId",
                table: "FnEmployeeLoans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "FnDepreciationRuns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "FnAssets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "FinAccountingEvents",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "FnVouchers");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "FnSalesInvoices");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "FnReceipts");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "FnPettyCashSettlements");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "FnPayrollRuns");

            migrationBuilder.DropColumn(
                name: "SeveranceBase",
                table: "FnPayrollItems");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "FnPayments");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "FnPayments");

            migrationBuilder.DropColumn(
                name: "EmployeeLoanId",
                table: "FnPayments");

            migrationBuilder.DropColumn(
                name: "DisbursementPaymentId",
                table: "FnEmployeeLoans");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "FnDepreciationRuns");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "FnAssets");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "FinAccountingEvents");
        }
    }
}
