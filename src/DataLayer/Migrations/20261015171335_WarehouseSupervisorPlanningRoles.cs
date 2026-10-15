using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class WarehouseSupervisorPlanningRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinanceApprovedAt",
                table: "InvDocuments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinanceApprovedByUserId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceQuotes",
                table: "InvDocuments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "Description", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { 11, "00000011-0000-0000-0000-000000000000", null, null, null, null, "برنامه‌ریزی و کنترل پروژه", null, null, null, null, "PLANNING", "PLANNING" },
                    { 12, "00000012-0000-0000-0000-000000000000", null, null, null, null, "سرپرست انبار", null, null, null, null, "WHSUPERVISOR", "WHSUPERVISOR" }
                });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 1100, "permission", "Warehouse", null, null, null, null, null, null, null, null, 11 },
                    { 1101, "permission", "Traceability", null, null, null, null, null, null, null, null, 11 },
                    { 1200, "permission", "Warehouse", null, null, null, null, null, null, null, null, 12 },
                    { 1201, "permission", "Traceability", null, null, null, null, null, null, null, null, 12 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1100);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1101);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1200);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1201);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DropColumn(
                name: "FinanceApprovedAt",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "FinanceApprovedByUserId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "PriceQuotes",
                table: "InvDocuments");
        }
    }
}
