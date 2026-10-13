using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_01_04_1554 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProgressLevel",
                table: "Processes",
                newName: "ProcessStatus");

            migrationBuilder.AddColumn<int>(
                name: "BomId1",
                table: "Processes",
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_Processes_BomId1",
                table: "Processes",
                column: "BomId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Processes_Boms_BomId1",
                table: "Processes",
                column: "BomId1",
                principalTable: "Boms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Processes_Boms_BomId1",
                table: "Processes");

            migrationBuilder.DropIndex(
                name: "IX_Processes_BomId1",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "BomId1",
                table: "Processes");

            migrationBuilder.RenameColumn(
                name: "ProcessStatus",
                table: "Processes",
                newName: "ProgressLevel");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "1ccdcf67-d629-4017-8a5c-5beadc119c7d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "5cc55895-48f4-4557-b7c0-0c0204331aab");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "1de9bc5e-5d9f-4c47-9af0-9ec9d290c4b4");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "db3a6534-0a85-4640-8923-98ca433972e6");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "766c33f7-1eeb-46b7-b930-9f3ceea668c8");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "7ca5df40-696c-490f-9c34-57d73f3413d9");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "52d8e2d9-97a7-4544-8a19-a7db02f7bb76");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "dd76e1a3-c50d-4d50-8d43-ac17ecbab677");
        }
    }
}
