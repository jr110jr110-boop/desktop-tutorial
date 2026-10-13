using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_01_04_1719 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "9973b2a6-3c4a-4d4c-b3ce-736e52ca0ecb");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "381c6291-cce8-4baf-b003-b78dba5f50b0");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "7b6cbeb8-2869-4849-b6f3-da7cdb330da8");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "92368f12-90dd-4fc4-ad34-30514e116547");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "246ac201-3e21-4eb1-b5d5-6339b5f8c261");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "fc63980c-28bc-4543-b12e-35d99aee2448");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "139e02fa-c93d-4ed9-b3b9-2be673510061");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "40b2877c-d297-413d-b299-6c8b616acc41");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BomId1",
                table: "Processes",
                nullable: true);

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
    }
}
