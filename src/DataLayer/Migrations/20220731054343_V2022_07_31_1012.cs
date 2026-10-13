using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_07_31_1012 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WeightPercent",
                table: "ProgressReports",
                newName: "PlannedWeightPercent");

            migrationBuilder.AddColumn<decimal>(
                name: "ActualWeightPercent",
                table: "ProgressReports",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ProcessTitle",
                table: "ProgressReports",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkOrderRemainingDays",
                table: "ProgressReports",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WorkOrderTitle",
                table: "ProgressReports",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "6758a03e-249d-4950-86cf-8a90c5a317b7");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "5ef3b584-8630-4bcb-8f71-67bcc7d3f033");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "e939693e-6885-4b00-99a8-8eeae8359a46");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "9122bdc7-60d5-4cdf-a16b-b93f06edd949");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "ad431add-3231-4b48-9238-68ba473ae01c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "14e53ac9-7612-490b-982e-09c45463b3be");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "f3b91215-978d-4405-b597-d85f3bdc12ec");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "59f6979c-369a-48ab-89c3-8271ff4568d4");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualWeightPercent",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "ProcessTitle",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "WorkOrderRemainingDays",
                table: "ProgressReports");

            migrationBuilder.DropColumn(
                name: "WorkOrderTitle",
                table: "ProgressReports");

            migrationBuilder.RenameColumn(
                name: "PlannedWeightPercent",
                table: "ProgressReports",
                newName: "WeightPercent");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "79f325a8-a4a0-42e4-95b0-f709fe60fdc2");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "7cf1551b-e921-4e4f-8c82-fc687c56a5ea");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "fb7d2caa-b21e-4fa8-b88c-44bbec55f27b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "447a5911-44e8-442f-a5cf-455e5c34b4b5");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "a8a9339f-c5ac-4aea-b8e8-9d0cd535435d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "00cd4049-6812-4e78-a8da-e14dcfff793e");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "14836446-2060-4718-a652-b3d0590cc432");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "952a75b7-bea1-4b18-8033-8d9bff18d465");
        }
    }
}
