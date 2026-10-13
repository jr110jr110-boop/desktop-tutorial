using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_04_05_1116 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PredecessorId",
                table: "EquipmentAssemblys",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PredecessorId",
                table: "Assemblies",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "04c990f6-dd55-4ba0-be09-7ebe094c8305");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "43e16245-9481-421f-995c-df06ac9548d8");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "86f3c131-976c-469f-bee9-56016a9d8564");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "041cf9d9-f8ac-4736-8532-ea51f0a7a63e");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "5b2754d1-743e-4dad-8ce0-2fbeb670b9b7");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "0d2e8d38-4ea2-4afd-807d-64d66be31165");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "86fc72a3-c57d-48ad-ae0e-f42b1d6beaa4");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "84ac1070-a984-412e-bfcd-ab4028384e0a");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentAssemblys_PredecessorId",
                table: "EquipmentAssemblys",
                column: "PredecessorId");

            migrationBuilder.CreateIndex(
                name: "IX_Assemblies_PredecessorId",
                table: "Assemblies",
                column: "PredecessorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assemblies_Assemblies_PredecessorId",
                table: "Assemblies",
                column: "PredecessorId",
                principalTable: "Assemblies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EquipmentAssemblys_EquipmentAssemblys_PredecessorId",
                table: "EquipmentAssemblys",
                column: "PredecessorId",
                principalTable: "EquipmentAssemblys",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assemblies_Assemblies_PredecessorId",
                table: "Assemblies");

            migrationBuilder.DropForeignKey(
                name: "FK_EquipmentAssemblys_EquipmentAssemblys_PredecessorId",
                table: "EquipmentAssemblys");

            migrationBuilder.DropIndex(
                name: "IX_EquipmentAssemblys_PredecessorId",
                table: "EquipmentAssemblys");

            migrationBuilder.DropIndex(
                name: "IX_Assemblies_PredecessorId",
                table: "Assemblies");

            migrationBuilder.DropColumn(
                name: "PredecessorId",
                table: "EquipmentAssemblys");

            migrationBuilder.DropColumn(
                name: "PredecessorId",
                table: "Assemblies");

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
        }
    }
}
