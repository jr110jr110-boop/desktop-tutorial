using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class OfficeReviewFixes1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureFileHash",
                table: "OfLetters",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "OfLetterAttachments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DecidedAt",
                table: "OfDelegations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DecidedByUserId",
                table: "OfDelegations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "OfDelegations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SickLeaveDays",
                table: "OfAttendanceDays",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SickLeaveDays",
                table: "FnAttendanceSheets",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureFileHash",
                table: "OfLetters");

            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "OfLetterAttachments");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                table: "OfDelegations");

            migrationBuilder.DropColumn(
                name: "DecidedByUserId",
                table: "OfDelegations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "OfDelegations");

            migrationBuilder.DropColumn(
                name: "SickLeaveDays",
                table: "OfAttendanceDays");

            migrationBuilder.DropColumn(
                name: "SickLeaveDays",
                table: "FnAttendanceSheets");
        }
    }
}
