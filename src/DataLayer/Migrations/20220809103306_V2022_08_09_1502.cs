using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_08_09_1502 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EquipmentBomId",
                table: "Boms",
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_Boms_EquipmentBomId",
                table: "Boms",
                column: "EquipmentBomId");

            migrationBuilder.AddForeignKey(
                name: "FK_Boms_EquipmentBoms_EquipmentBomId",
                table: "Boms",
                column: "EquipmentBomId",
                principalTable: "EquipmentBoms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Boms_EquipmentBoms_EquipmentBomId",
                table: "Boms");

            migrationBuilder.DropIndex(
                name: "IX_Boms_EquipmentBomId",
                table: "Boms");

            migrationBuilder.DropColumn(
                name: "EquipmentBomId",
                table: "Boms");

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
    }
}
