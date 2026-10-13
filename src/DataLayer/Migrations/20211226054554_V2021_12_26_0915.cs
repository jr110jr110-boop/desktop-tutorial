using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2021_12_26_0915 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Inquiries_Tenders_TenderId",
                table: "Inquiries");

            migrationBuilder.RenameColumn(
                name: "TenderId",
                table: "Inquiries",
                newName: "ProviderId");

            migrationBuilder.RenameIndex(
                name: "IX_Inquiries_TenderId",
                table: "Inquiries",
                newName: "IX_Inquiries_ProviderId");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Processes",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ProcessFiles",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(nullable: false),
                    ProcessId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessFiles", x => new { x.ProcessId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_ProcessFiles_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessFiles_Processes_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "Processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "947b7f8b-c13c-451a-9026-b2c0ff9108d9");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "81d8b323-b691-45a0-a099-612e50d07c28");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "a5eda797-ca77-4110-b321-19596fc8cccc");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "2b3cb636-ca5b-4cea-ba67-3e844b962aa5");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "ef847258-26d5-4348-a80e-0acc6f812428");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "6b6b1d70-d70a-43b4-9440-daf7748d91a2");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "dfe53424-2060-4b14-98e0-53ff7520f5bc");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "134edbc4-7dd4-4452-a863-1ad5a095b69e");

            migrationBuilder.CreateIndex(
                name: "IX_Processes_UserId",
                table: "Processes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessFiles_DocumentId",
                table: "ProcessFiles",
                column: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Inquiries_Providers_ProviderId",
                table: "Inquiries",
                column: "ProviderId",
                principalTable: "Providers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Processes_AppUsers_UserId",
                table: "Processes",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Inquiries_Providers_ProviderId",
                table: "Inquiries");

            migrationBuilder.DropForeignKey(
                name: "FK_Processes_AppUsers_UserId",
                table: "Processes");

            migrationBuilder.DropTable(
                name: "ProcessFiles");

            migrationBuilder.DropIndex(
                name: "IX_Processes_UserId",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Processes");

            migrationBuilder.RenameColumn(
                name: "ProviderId",
                table: "Inquiries",
                newName: "TenderId");

            migrationBuilder.RenameIndex(
                name: "IX_Inquiries_ProviderId",
                table: "Inquiries",
                newName: "IX_Inquiries_TenderId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_Inquiries_Tenders_TenderId",
                table: "Inquiries",
                column: "TenderId",
                principalTable: "Tenders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
