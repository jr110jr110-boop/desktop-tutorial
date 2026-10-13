using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_08_15_1636 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyLevelPlannedProgress",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "CompanytLevelActualProgress",
                table: "ProgressReports");

            migrationBuilder.AddColumn<decimal>(
                name: "WeightPercent",
                table: "WorkOrders",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContractActualEndtDate",
                table: "ProgressReports",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ContractActualPrice",
                table: "ProgressReports",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContractActualStartDate",
                table: "ProgressReports",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ContractActualWeightPercent",
                table: "ProgressReports",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContractPlannedEndDate",
                table: "ProgressReports",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ContractPlannedPrice",
                table: "ProgressReports",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContractPlannedStartDate",
                table: "ProgressReports",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ContractPlannedWeightPercent",
                table: "ProgressReports",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContractRemainingDays",
                table: "ProgressReports",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "060fbf10-db64-4cdd-adeb-0a5f64afc0a1");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "df3e0f10-c692-4721-94af-d69ea6f1d8d4");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "54f7ff81-b2f7-4b17-af6b-59580a0d5100");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "9fc845c4-dfd9-4154-b3de-82b22c10807e");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "d2aff218-d5e9-4002-a5fc-e06f328d8f65");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "0eb57e78-c47d-4f9c-8868-befe425a2a60");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "781899c4-fcff-43d6-8924-831f116dffbf");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "d6b71ffb-3453-4dc0-848c-1047b0598eb8");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeightPercent",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ContractActualEndtDate",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "ContractActualPrice",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "ContractActualStartDate",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "ContractActualWeightPercent",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "ContractPlannedEndDate",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "ContractPlannedPrice",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "ContractPlannedStartDate",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "ContractPlannedWeightPercent",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "ContractRemainingDays",
                table: "ProgressReports");

            migrationBuilder.AddColumn<decimal>(
                name: "CompanyLevelPlannedProgress",
                table: "ProgressReports",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CompanytLevelActualProgress",
                table: "ProgressReports",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "fd97befc-b60a-4be6-a461-2cfd2b616d84");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "fa2a87e7-26b0-40b5-88c5-28387113fd0e");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "6b029ddf-3494-4eb4-919a-7898589a82dd");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "d68f063f-3e0d-4f4b-ba7f-ef3970601f13");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "f62e38f7-0a72-42b9-80ca-c41edbb29549");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "c963f8e8-aeee-4006-aaf2-f527f700e12b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "fe6ef169-9d26-49a5-824f-5864202f9250");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "aad119c0-c18a-4a90-8a7a-73269f3915d0");
        }
    }
}
