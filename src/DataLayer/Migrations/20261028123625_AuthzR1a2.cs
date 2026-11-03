using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AuthzR1a2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseRequestedAt",
                table: "PcGuaranteeInfos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseRequestedByUserId",
                table: "PcGuaranteeInfos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseRequestedDate",
                table: "PcGuaranteeInfos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseRequestedKind",
                table: "PcGuaranteeInfos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseRequestedNote",
                table: "PcGuaranteeInfos",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DesignApprovedAt",
                table: "PcClaims",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DesignApprovedByUserId",
                table: "PcClaims",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastEditedByUserId",
                table: "PcClaims",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PmoApprovedAt",
                table: "PcClaims",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PmoApprovedByUserId",
                table: "PcClaims",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuantifiedByUserId",
                table: "PcClaims",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReleaseRequestedAt",
                table: "PcGuaranteeInfos");

            migrationBuilder.DropColumn(
                name: "ReleaseRequestedByUserId",
                table: "PcGuaranteeInfos");

            migrationBuilder.DropColumn(
                name: "ReleaseRequestedDate",
                table: "PcGuaranteeInfos");

            migrationBuilder.DropColumn(
                name: "ReleaseRequestedKind",
                table: "PcGuaranteeInfos");

            migrationBuilder.DropColumn(
                name: "ReleaseRequestedNote",
                table: "PcGuaranteeInfos");

            migrationBuilder.DropColumn(
                name: "DesignApprovedAt",
                table: "PcClaims");

            migrationBuilder.DropColumn(
                name: "DesignApprovedByUserId",
                table: "PcClaims");

            migrationBuilder.DropColumn(
                name: "LastEditedByUserId",
                table: "PcClaims");

            migrationBuilder.DropColumn(
                name: "PmoApprovedAt",
                table: "PcClaims");

            migrationBuilder.DropColumn(
                name: "PmoApprovedByUserId",
                table: "PcClaims");

            migrationBuilder.DropColumn(
                name: "QuantifiedByUserId",
                table: "PcClaims");
        }
    }
}
