using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class OfficeR2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContractId",
                table: "OfLetters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenderId",
                table: "OfLetters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CompanyRulesApplied",
                table: "OfAttendancePolicies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DelayPenaltyFactor",
                table: "OfAttendancePolicies",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 3m);

            migrationBuilder.AddColumn<int>(
                name: "MonthlyOvertimeCapMinutes",
                table: "OfAttendancePolicies",
                type: "int",
                nullable: false,
                defaultValue: 1320);

            migrationBuilder.AddColumn<int>(
                name: "DelayDeductionMinutes",
                table: "OfAttendanceDays",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OfClockImportProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MappingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfClockImportProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_ContractId",
                table: "OfLetters",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_TenderId",
                table: "OfLetters",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_OfClockImportProfiles_Name",
                table: "OfClockImportProfiles",
                column: "Name",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetters_Contracts_ContractId",
                table: "OfLetters",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetters_Tenders_TenderId",
                table: "OfLetters",
                column: "TenderId",
                principalTable: "Tenders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OfLetters_Contracts_ContractId",
                table: "OfLetters");

            migrationBuilder.DropForeignKey(
                name: "FK_OfLetters_Tenders_TenderId",
                table: "OfLetters");

            migrationBuilder.DropTable(
                name: "OfClockImportProfiles");

            migrationBuilder.DropIndex(
                name: "IX_OfLetters_ContractId",
                table: "OfLetters");

            migrationBuilder.DropIndex(
                name: "IX_OfLetters_TenderId",
                table: "OfLetters");

            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "OfLetters");

            migrationBuilder.DropColumn(
                name: "TenderId",
                table: "OfLetters");

            migrationBuilder.DropColumn(
                name: "CompanyRulesApplied",
                table: "OfAttendancePolicies");

            migrationBuilder.DropColumn(
                name: "DelayPenaltyFactor",
                table: "OfAttendancePolicies");

            migrationBuilder.DropColumn(
                name: "MonthlyOvertimeCapMinutes",
                table: "OfAttendancePolicies");

            migrationBuilder.DropColumn(
                name: "DelayDeductionMinutes",
                table: "OfAttendanceDays");
        }
    }
}
