using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class FinanceReviewFixes3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurrencyAmount",
                table: "FnReceiptLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Rate",
                table: "FnReceiptLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrencyAmount",
                table: "FnPaymentLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Rate",
                table: "FnPaymentLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrencyAmount",
                table: "FnReceiptLines");

            migrationBuilder.DropColumn(
                name: "Rate",
                table: "FnReceiptLines");

            migrationBuilder.DropColumn(
                name: "CurrencyAmount",
                table: "FnPaymentLines");

            migrationBuilder.DropColumn(
                name: "Rate",
                table: "FnPaymentLines");
        }
    }
}
