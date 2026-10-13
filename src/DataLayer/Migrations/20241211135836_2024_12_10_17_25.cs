using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class _2024_12_10_17_25 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EquipmentProcessFiles_Documents_DocumentId",
                table: "EquipmentProcessFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcessFiles_Documents_DocumentId",
                table: "ProcessFiles");

            migrationBuilder.DropIndex(
                name: "IX_ProcessFiles_DocumentId",
                table: "ProcessFiles");

            migrationBuilder.DropIndex(
                name: "IX_EquipmentProcessFiles_DocumentId",
                table: "EquipmentProcessFiles");

            migrationBuilder.DropColumn(
                name: "IsFinal",
                table: "EquipmentProcessFiles");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "ProcessFiles",
                newName: "Minor");

            migrationBuilder.RenameColumn(
                name: "ReVision",
                table: "ProcessFiles",
                newName: "Major");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "ProcessFiles",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Issue",
                table: "ProcessFiles",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "EquipmentProcessFiles",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegDate",
                table: "EquipmentProcessFiles",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "EquipmentProcessFiles",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "major",
                table: "EquipmentProcessFiles",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "minor",
                table: "EquipmentProcessFiles",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "bb651c0b-2c84-41a3-b42d-12f5c1542f9e");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "4e52a494-1457-4369-b3fb-730002031639");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "d44945bd-9c33-4d65-976b-6ab357e51073");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "2f006aea-88ba-4bba-a9e1-a575dc27b443");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "fc40b06f-a8b4-43e3-832e-d416ff19268c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "39382b4e-130f-48bd-a13c-2b11282b6c93");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "604a5cd5-3e33-4d09-b3fb-59b111aba73c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "7af23a01-7f3b-47d0-8e1e-f52543f5e35a");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Id",
                table: "ProcessFiles");

            migrationBuilder.DropColumn(
                name: "Issue",
                table: "ProcessFiles");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "EquipmentProcessFiles");

            migrationBuilder.DropColumn(
                name: "RegDate",
                table: "EquipmentProcessFiles");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "EquipmentProcessFiles");

            migrationBuilder.DropColumn(
                name: "major",
                table: "EquipmentProcessFiles");

            migrationBuilder.DropColumn(
                name: "minor",
                table: "EquipmentProcessFiles");

            migrationBuilder.RenameColumn(
                name: "Minor",
                table: "ProcessFiles",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "Major",
                table: "ProcessFiles",
                newName: "ReVision");

            migrationBuilder.AddColumn<bool>(
                name: "IsFinal",
                table: "EquipmentProcessFiles",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "98ecfc5c-7e04-4fc2-b14c-4304625b7685");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "3d6c1e8d-259c-49e8-a307-82cab5957f59");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "435adcf0-8fc2-4b3a-9846-667ee1b201a9");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "892d6e75-edc9-4688-99db-d6af62aa3fa7");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "a8fc6260-6f55-4056-8ad2-1aa39f5dc4c9");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "c70a853b-e8d0-4e65-8ac7-224722696cbd");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "e17fab6a-f6f8-40c3-8c90-64dc5292b0f5");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "e840f3d1-38a3-44ef-b955-e80b84e067fb");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessFiles_DocumentId",
                table: "ProcessFiles",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentProcessFiles_DocumentId",
                table: "EquipmentProcessFiles",
                column: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_EquipmentProcessFiles_Documents_DocumentId",
                table: "EquipmentProcessFiles",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessFiles_Documents_DocumentId",
                table: "ProcessFiles",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
