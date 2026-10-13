using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_01_30_1232 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Boms_Assemblies_AssemblyId",
                table: "Boms");

            migrationBuilder.AddColumn<decimal>(
                name: "ProgressLevel",
                table: "Processes",
                nullable: false,
                computedColumnSql: @"
                    case [ProcessStatus]
		            
                    when 0 then 0.0
		            when 100 then 100.0
		            else 50.0
		            end
            ");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "f982c651-f932-4da6-9431-2f053acd5c44");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "201ecff7-b2b7-4d5e-ba7a-0658a53633c3");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "9cc85d90-04d4-482e-a6bd-19f0e9414ef0");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "f035c667-2d9f-4fff-a73a-d14fffa9d047");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "b8c0d80e-f2ea-447d-8117-93fad231b381");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "26f3cc2b-e0db-437f-b0a4-b61e644011d0");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "2d08a22d-fd06-453c-9100-1f2fe14253ae");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "1b529f3c-0903-4d05-bb58-6118fed75757");

            migrationBuilder.AddForeignKey(
                name: "FK_Boms_Assemblies_AssemblyId",
                table: "Boms",
                column: "AssemblyId",
                principalTable: "Assemblies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Boms_Assemblies_AssemblyId",
                table: "Boms");

            migrationBuilder.DropColumn(
                name: "ProgressLevel",
                table: "Processes");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "54f181a3-668f-43c2-937f-e9da83b9b48c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "7aead557-8669-4153-bf4a-f2e37c54a877");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "ff10848c-2d2e-4080-87de-cbdae046e4e0");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "4b57f920-3337-42ae-ba5c-21f92d462c12");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "674f2cee-2ec6-4ec9-98b3-e774e446f350");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "827e5cb0-0a31-4c2a-a27e-02f50d66a739");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "f077dc49-e5f8-4734-98ee-e3011e54f157");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "64fd21df-2eda-4d5f-b64f-cad487309b35");

            migrationBuilder.AddForeignKey(
                name: "FK_Boms_Assemblies_AssemblyId",
                table: "Boms",
                column: "AssemblyId",
                principalTable: "Assemblies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
