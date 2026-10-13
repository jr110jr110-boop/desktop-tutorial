using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_02_10_1531 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                value: "5c328356-4f5a-4c65-8d29-32e7129d172c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "c2c479b6-afdb-4edb-852e-89e549076443");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "eb922658-1276-4e51-87f6-b78cf822cc05");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "ef8aaaa4-3101-4d0f-b609-9227e2b7644e");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "d5526ece-44d9-4858-938a-e5a971baa31f");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "8e41ed53-4194-4367-af3d-50c6c2c6eb16");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "d59684d7-6214-4b19-afcd-6090bfcd657d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "3a1113d1-1b65-423f-9661-aa5009d7d37f");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentBoms_PredecessorId",
                table: "EquipmentBoms",
                column: "PredecessorId");

            migrationBuilder.CreateIndex(
                name: "IX_Boms_PredecessorId",
                table: "Boms",
                column: "PredecessorId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
