using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class TreasuryR1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastEditedAt",
                table: "FnVouchers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TriggeredByUserId",
                table: "FnVouchers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByUserId",
                table: "FnReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceAction",
                table: "FnReceipts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceData",
                table: "FnReceipts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceId",
                table: "FnReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "FnReceipts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceAction",
                table: "FnPayments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceData",
                table: "FnPayments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceId",
                table: "FnPayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "FnPayments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectsPackageId",
                table: "FnOutboxPackages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityCardNumber",
                table: "FnEmployees",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nationality",
                table: "FnEmployees",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExplainedAmount",
                table: "FnBankReconciliations",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ItemsByUserId",
                table: "FnBankReconciliations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hash",
                table: "FnAuditLogs",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrevHash",
                table: "FnAuditLogs",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Seq",
                table: "FnAuditLogs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "Documents",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FnBankReconciliationItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReconciliationId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CorrectingVoucherId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBankReconciliationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnBankReconciliationItems_FnBankReconciliations_ReconciliationId",
                        column: x => x.ReconciliationId,
                        principalTable: "FnBankReconciliations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FnBankReconciliationItems_FnVouchers_CorrectingVoucherId",
                        column: x => x.CorrectingVoucherId,
                        principalTable: "FnVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnCloseOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: true),
                    FiscalYearId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Items = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnCloseOverrides", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FnReceipts_SourceType_SourceId",
                table: "FnReceipts",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_FnPayments_SourceType_SourceId",
                table: "FnPayments",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_FnOutboxPackages_CorrectsPackageId",
                table: "FnOutboxPackages",
                column: "CorrectsPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_FnAuditLogs_Seq",
                table: "FnAuditLogs",
                column: "Seq",
                unique: true,
                filter: "[Seq] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FnBankReconciliationItems_CorrectingVoucherId",
                table: "FnBankReconciliationItems",
                column: "CorrectingVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBankReconciliationItems_ReconciliationId",
                table: "FnBankReconciliationItems",
                column: "ReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_FnCloseOverrides_Kind_FiscalYearId",
                table: "FnCloseOverrides",
                columns: new[] { "Kind", "FiscalYearId" });

            migrationBuilder.CreateIndex(
                name: "IX_FnCloseOverrides_Kind_PeriodId",
                table: "FnCloseOverrides",
                columns: new[] { "Kind", "PeriodId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FnBankReconciliationItems");

            migrationBuilder.DropTable(
                name: "FnCloseOverrides");

            migrationBuilder.DropIndex(
                name: "IX_FnReceipts_SourceType_SourceId",
                table: "FnReceipts");

            migrationBuilder.DropIndex(
                name: "IX_FnPayments_SourceType_SourceId",
                table: "FnPayments");

            migrationBuilder.DropIndex(
                name: "IX_FnOutboxPackages_CorrectsPackageId",
                table: "FnOutboxPackages");

            migrationBuilder.DropIndex(
                name: "IX_FnAuditLogs_Seq",
                table: "FnAuditLogs");

            migrationBuilder.DropColumn(
                name: "LastEditedAt",
                table: "FnVouchers");

            migrationBuilder.DropColumn(
                name: "TriggeredByUserId",
                table: "FnVouchers");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "FnReceipts");

            migrationBuilder.DropColumn(
                name: "SourceAction",
                table: "FnReceipts");

            migrationBuilder.DropColumn(
                name: "SourceData",
                table: "FnReceipts");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "FnReceipts");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "FnReceipts");

            migrationBuilder.DropColumn(
                name: "SourceAction",
                table: "FnPayments");

            migrationBuilder.DropColumn(
                name: "SourceData",
                table: "FnPayments");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "FnPayments");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "FnPayments");

            migrationBuilder.DropColumn(
                name: "CorrectsPackageId",
                table: "FnOutboxPackages");

            migrationBuilder.DropColumn(
                name: "IdentityCardNumber",
                table: "FnEmployees");

            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "FnEmployees");

            migrationBuilder.DropColumn(
                name: "ExplainedAmount",
                table: "FnBankReconciliations");

            migrationBuilder.DropColumn(
                name: "ItemsByUserId",
                table: "FnBankReconciliations");

            migrationBuilder.DropColumn(
                name: "Hash",
                table: "FnAuditLogs");

            migrationBuilder.DropColumn(
                name: "PrevHash",
                table: "FnAuditLogs");

            migrationBuilder.DropColumn(
                name: "Seq",
                table: "FnAuditLogs");

            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "Documents");
        }
    }
}
