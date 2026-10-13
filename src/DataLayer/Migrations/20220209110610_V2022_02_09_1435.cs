using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_02_09_1435 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "7be74a78-2ff8-4795-b289-a30832e3cc74");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "50e92be1-645f-4d49-a061-173d022ed5ac");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "d459233e-3f0a-4b9a-9541-09fd2144fb27");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "85e8b7e5-b158-4bc4-950f-4cc4ec14890d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "4fa46545-437c-448c-bc74-fed8c3f0210e");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "456a603a-4f33-49b0-91e8-f3b4b31e3bad");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "ca537670-940d-4f6f-a30b-5d45f863febd");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "faba7766-857a-4d92-9e0e-666dbf803bf2");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentBoms_PredecessorId",
                table: "EquipmentBoms",
                column: "PredecessorId",
                unique: true,
                filter: "[PredecessorId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Boms_PredecessorId",
                table: "Boms",
                column: "PredecessorId",
                unique: true,
                filter: "[PredecessorId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Boms_Boms_PredecessorId",
                table: "Boms",
                column: "PredecessorId",
                principalTable: "Boms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EquipmentBoms_EquipmentBoms_PredecessorId",
                table: "EquipmentBoms",
                column: "PredecessorId",
                principalTable: "EquipmentBoms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Boms_Boms_PredecessorId",
                table: "Boms");

            migrationBuilder.DropForeignKey(
                name: "FK_EquipmentBoms_EquipmentBoms_PredecessorId",
                table: "EquipmentBoms");

            migrationBuilder.DropIndex(
                name: "IX_EquipmentBoms_PredecessorId",
                table: "EquipmentBoms");

            migrationBuilder.DropIndex(
                name: "IX_Boms_PredecessorId",
                table: "Boms");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "ac8d2950-9768-4069-b9e4-2ef8c31a1f94");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "86488de5-b948-4ae1-963c-a85575301ea6");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "9055546f-0d69-477d-b0fc-30523a6e73f0");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "019a2263-37cf-439d-bf5d-e54cbf1230b0");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "f8e147c0-7adb-4dfc-9192-68ac5e0b1784");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "8eb67d3b-3451-4bc3-987f-01543fca260b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "b2ed91eb-b2df-497c-a253-fee171ce81a2");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "1ed99147-0942-44d2-83f0-86baf645d057");
        }
    }
}
