using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_02_02_2217 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartDate",
                table: "WorkOrders",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PredecessorId",
                table: "EquipmentBoms",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssemblerDuration",
                table: "EquipmentAssemblys",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PredecessorId",
                table: "Boms",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActualEndDate",
                table: "Assemblies",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActualStartDate",
                table: "Assemblies",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EndDate",
                table: "Assemblies",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartDate",
                table: "Assemblies",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "aef8a3a4-b4d8-4d5a-a3cf-e7fbda569e69");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "7cac536a-da14-4b9d-bd58-7214bd3b0ddd");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "35c3a79e-698d-496a-b5e2-fa9be159cb01");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "ffee240c-a63c-4aea-862f-6eee89d8ffb7");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "d27cfda5-c1b4-4862-b1c6-2f5b54193fcf");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "bdccd499-7070-44d9-9281-45a5300a238d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "56f003e9-dd65-4287-8a60-ac3d05576ad4");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "d37acba8-ff82-4dc1-9c91-00da26dc83d9");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PredecessorId",
                table: "EquipmentBoms");

            migrationBuilder.DropColumn(
                name: "AssemblerDuration",
                table: "EquipmentAssemblys");

            migrationBuilder.DropColumn(
                name: "PredecessorId",
                table: "Boms");

            migrationBuilder.DropColumn(
                name: "ActualEndDate",
                table: "Assemblies");

            migrationBuilder.DropColumn(
                name: "ActualStartDate",
                table: "Assemblies");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Assemblies");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Assemblies");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "6d1cb1d5-b708-4541-84f5-79ce86cb3486");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "73f9d9d3-9d84-49c0-9758-eb5c2354e9d6");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "900e6692-98ae-4c1d-97a4-a2d49fcb2b17");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "ed11455b-14d7-4c82-9fbd-de31f6e907c0");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "70584f94-a8d5-4666-9611-31ac4c0b9608");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "3364ef04-ba74-4e8e-ad12-d130719b846a");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "e477b982-8c38-4d07-a804-5b15aa4b036b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "60ccd678-7250-42c9-a608-8ba9e0abcbc7");
        }
    }
}
