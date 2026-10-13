using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class _2024_09_19_10_45 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFinal",
                table: "ProcessFiles");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ProcessFiles",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReVision",
                table: "ProcessFiles",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegDate",
                table: "ProcessFiles",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "ProcessFiles",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "ProcessFiles",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DocumentCode",
                table: "Processes",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentCode",
                table: "EquipmentProcesses",
                nullable: true);

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
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "ProcessFiles");

            migrationBuilder.DropColumn(
                name: "ReVision",
                table: "ProcessFiles");

            migrationBuilder.DropColumn(
                name: "RegDate",
                table: "ProcessFiles");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ProcessFiles");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ProcessFiles");

            migrationBuilder.DropColumn(
                name: "DocumentCode",
                table: "Processes");

            migrationBuilder.DropColumn(
                name: "DocumentCode",
                table: "EquipmentProcesses");

            migrationBuilder.AddColumn<bool>(
                name: "IsFinal",
                table: "ProcessFiles",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "71339211-f16c-4495-abb0-fe650785e0f0");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "18d0f311-0186-4de1-99cc-2bdef744bf41");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "43bb59c0-62aa-45e0-a2da-abc65e5e6461");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "5d5324cb-136f-4b9e-a9fb-92142eb41ebf");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "56351971-4128-436f-853d-f23a331240cd");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "5cb40d04-12f5-468c-a471-5eb31b0b1e46");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "44d675af-8c9d-4a82-95e6-4e9913275254");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "ae1da22c-c8d3-4eb0-a22e-f009b672f9d2");
        }
    }
}
