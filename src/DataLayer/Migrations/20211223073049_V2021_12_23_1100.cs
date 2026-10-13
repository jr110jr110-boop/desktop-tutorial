using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2021_12_23_1100 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContractFiles_Documents_DocumentId",
                table: "ContractFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_TenderFiles_Documents_DocumentId",
                table: "TenderFiles");

            migrationBuilder.DropIndex(
                name: "IX_TenderFiles_DocumentId",
                table: "TenderFiles");

            migrationBuilder.DropIndex(
                name: "IX_ContractFiles_DocumentId",
                table: "ContractFiles");

            migrationBuilder.DropColumn(
                name: "ContractFileId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TenderFileId",
                table: "Documents");

            migrationBuilder.CreateTable(
                name: "EquipmentProcessFiles",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(nullable: false),
                    EquipmentProcessId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentProcessFiles", x => new { x.EquipmentProcessId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_EquipmentProcessFiles_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentProcessFiles_EquipmentProcesses_EquipmentProcessId",
                        column: x => x.EquipmentProcessId,
                        principalTable: "EquipmentProcesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "418b0680-f420-4d44-b887-cd1de1534e32");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "40a486de-2922-47c6-b6e3-484daad34416");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "adcf2307-f53f-4b14-9ecc-cf7cc0ee16de");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "3d2a41fd-6db5-4d3a-8d51-ff3695302b38");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "f766a57e-c198-4127-8c97-5aab41d9e987");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "06b41eeb-5ba8-452a-8935-b705556b2bc5");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "8fb75d16-87ae-4ca8-b477-0d884a3fa283");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "df3f48e0-2f8b-4e0c-bdc6-3cb8436cfdf5");

            migrationBuilder.CreateIndex(
                name: "IX_TenderFiles_DocumentId",
                table: "TenderFiles",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractFiles_DocumentId",
                table: "ContractFiles",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentProcessFiles_DocumentId",
                table: "EquipmentProcessFiles",
                column: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ContractFiles_Documents_DocumentId",
                table: "ContractFiles",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TenderFiles_Documents_DocumentId",
                table: "TenderFiles",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContractFiles_Documents_DocumentId",
                table: "ContractFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_TenderFiles_Documents_DocumentId",
                table: "TenderFiles");

            migrationBuilder.DropTable(
                name: "EquipmentProcessFiles");

            migrationBuilder.DropIndex(
                name: "IX_TenderFiles_DocumentId",
                table: "TenderFiles");

            migrationBuilder.DropIndex(
                name: "IX_ContractFiles_DocumentId",
                table: "ContractFiles");

            migrationBuilder.AddColumn<int>(
                name: "ContractFileId",
                table: "Documents",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenderFileId",
                table: "Documents",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "b8f527e4-112c-4e4d-bca2-728a61d42e86");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "d1f661a1-8605-4c8c-be2c-4e678f47f913");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "5b7d4a9a-c0c8-4403-9e05-b51db3fbbef3");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "17811b9e-0a63-4743-b84f-65a6fdc5a23b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "75f128e2-b644-4010-9f57-305e69e75b3c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "1ed09229-cf26-4397-8d76-32171680f2a1");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "62945812-18ff-4f49-baa0-ed1e7fd2767a");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "a8161072-23d5-4d0e-b479-ee12fa1c6e01");

            migrationBuilder.CreateIndex(
                name: "IX_TenderFiles_DocumentId",
                table: "TenderFiles",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractFiles_DocumentId",
                table: "ContractFiles",
                column: "DocumentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ContractFiles_Documents_DocumentId",
                table: "ContractFiles",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenderFiles_Documents_DocumentId",
                table: "TenderFiles",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
