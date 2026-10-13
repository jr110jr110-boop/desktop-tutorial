using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_01_04_1701 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProcessFiles_Processes_ProcessId1",
                table: "ProcessFiles");

            migrationBuilder.DropIndex(
                name: "IX_ProcessFiles_ProcessId1",
                table: "ProcessFiles");

            migrationBuilder.DropColumn(
                name: "ProcessId1",
                table: "ProcessFiles");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "e9c781a8-d537-482e-8ade-a2ef9a966747");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "d13e448d-5d7d-472c-80e6-513b97b74286");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "d2f1018b-68b8-4139-bb4e-a1efabe1c348");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "0ba09201-6404-4042-854d-4952695b4895");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "7d02e4a1-a061-420d-94ca-fd375b167241");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "37fb8145-ffc7-4f96-b2c9-0d5ca28527d2");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "65d6cc08-9b98-4691-8866-f0d0dabd71f7");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "1c1b3745-7021-4fc9-8f58-8b617a5c9d63");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProcessId1",
                table: "ProcessFiles",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "73192720-ad53-483b-b7df-5baeda18ff45");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "24a21f83-e462-4ed3-90e8-598fba88cb7b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "a90aa58a-2252-4f19-be78-9b840ea64ec0");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "a71fb809-43d7-491b-b205-726842bbeabb");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "baf667e5-9d2c-4e32-8724-614b99ca0906");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "0c4ca03d-e550-4abd-9286-1b8ff2803106");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "307868f0-76e3-4552-af32-3e3cb5b92497");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "07d29924-ae26-4137-81ef-4be7e25a369c");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessFiles_ProcessId1",
                table: "ProcessFiles",
                column: "ProcessId1");

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessFiles_Processes_ProcessId1",
                table: "ProcessFiles",
                column: "ProcessId1",
                principalTable: "Processes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
