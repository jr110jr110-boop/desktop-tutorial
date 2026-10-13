using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_01_04_1655 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
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

        protected override void Down(MigrationBuilder migrationBuilder)
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
                value: "65adbdee-024c-47d8-9fcf-4cd9d59f6fda");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "c6e0b03f-0bfb-4dda-b478-6e3a302036a3");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "fd7685e0-0b37-4a4f-96fe-262f5acd6f38");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "f1179220-e86a-487d-a044-8347cd951be4");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "084d2f7a-ca65-4314-a604-fd10504b8855");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "8f7bcfab-5ec4-48d8-921a-1222f517a54a");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "18993c30-7aa5-4944-b210-c00c3d0804bf");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "c26fd434-d0d8-4fe6-9a68-6a241da28b95");
        }
    }
}
