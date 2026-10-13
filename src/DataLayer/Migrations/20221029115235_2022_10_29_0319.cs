using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class _2022_10_29_0319 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreateDate",
                table: "TenderGuaranties",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "CreatorUserId",
                table: "TenderGuaranties",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Deleted",
                table: "TenderGuaranties",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ModifierUserId",
                table: "TenderGuaranties",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ModifyDate",
                table: "TenderGuaranties",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActualStartDate",
                table: "Contracts",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AnnounceDate",
                table: "Contracts",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContractDate",
                table: "Contracts",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliverDate",
                table: "Contracts",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "Duration",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployerContractNo",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployerOrderNo",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployerProjectNo",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GurantyDate",
                table: "Contracts",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "InternalContractNo",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalProjectNo",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PayDate",
                table: "Contracts",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Site",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubCode",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubEmployer",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TPA",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TempDate",
                table: "Contracts",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "TenderCode",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Contracts",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Contracts",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "ae0dc0f5-59bc-47d6-8c5e-9412ac61c777");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "4e7b0548-a63d-4be1-bd91-cb36cd7fff92");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "a89292ec-1c47-40ae-8095-1570fef36396");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "8c5ec1d7-5333-43f9-a877-f92fe6ec65a3");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "d9b19e69-4991-4c3a-b010-0190df1a4221");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "b183bcc4-c078-4b9b-8800-76bb405c6989");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "5e636d77-d15f-4f41-9833-5908b4a598e2");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "8b2d7bf6-f7f8-4ee0-8634-8f35720d2e95");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreateDate",
                table: "TenderGuaranties");

            migrationBuilder.DropColumn(
                name: "CreatorUserId",
                table: "TenderGuaranties");

            migrationBuilder.DropColumn(
                name: "Deleted",
                table: "TenderGuaranties");

            migrationBuilder.DropColumn(
                name: "ModifierUserId",
                table: "TenderGuaranties");

            migrationBuilder.DropColumn(
                name: "ModifyDate",
                table: "TenderGuaranties");

            migrationBuilder.DropColumn(
                name: "ActualStartDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "AnnounceDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ContractDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "DeliverDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "Duration",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "EmployerContractNo",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "EmployerOrderNo",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "EmployerProjectNo",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "GurantyDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "InternalContractNo",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "InternalProjectNo",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PayDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "Site",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SubCode",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SubEmployer",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "TPA",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "TempDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "TenderCode",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Contracts");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "981ffe5e-adac-494a-9962-8c72dcb92327");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "148b549b-3e27-4a59-a2d7-9dad6e013d37");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "41149285-206c-4a07-a820-b7b9d8ba20fc");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "e77ad7f0-ea95-41cf-8355-88764ae8238f");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "6aee1705-cb77-45e1-9de2-8c5a1afa3e5d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "02e499c1-1a7c-4701-8e81-e78dc257c593");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "020ab51b-b967-4ce8-b2c7-1b80a2131795");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "8dfb06e8-df74-4987-9d9b-89c3ddf9d1ff");
        }
    }
}
