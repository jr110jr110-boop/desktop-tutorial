using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_01_13_1534 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectCharterFileId",
                table: "Contracts",
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "634281e8-57d6-4210-a001-11207f647099");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "f00914d1-8d47-41bf-a7f9-e0c662f666ef");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "18538e41-8d91-4616-9b6f-a85b94e8b898");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "b5fdc6ac-05ca-4b69-8b25-ce6b73729b30");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "6c52408a-ab20-4223-b611-dbd9d204a554");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "c90a4e7e-fed2-40bf-b1fc-9fedf79834a8");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "a9ddfde7-1a46-4a11-937c-38b51594e512");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "6c7fca9d-eb01-48d5-b52e-5f70475e667a");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ProjectCharterFileId",
                table: "Contracts",
                column: "ProjectCharterFileId",
                unique: true,
                filter: "[ProjectCharterFileId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_Documents_ProjectCharterFileId",
                table: "Contracts",
                column: "ProjectCharterFileId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_Documents_ProjectCharterFileId",
                table: "Contracts");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_ProjectCharterFileId",
                table: "Contracts");

            migrationBuilder.AlterColumn<string>(
                name: "ProjectCharterFileId",
                table: "Contracts",
                nullable: true,
                oldClrType: typeof(Guid),
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "b63829e4-ba29-4b10-ad5c-ce5872c33274");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "44b903ae-9cb6-4d63-8119-b78b63e332c3");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "23b7287d-7de8-46d6-868a-e7abf6bc628c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "e519be9b-9224-47dd-9585-e54fa57c76e8");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "a78ae878-bb31-4cd8-8f7f-58a87bd5bdeb");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "53464f87-37dc-4355-b517-4f59cf0c9643");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "b5a229ee-a4b9-43f9-9e24-cf17ad515ca2");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "1a8a0af4-b559-44ae-8427-60c1aeb8210d");
        }
    }
}
