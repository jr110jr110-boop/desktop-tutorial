using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class _2022_20_27_1010 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TenderDocsDate",
                table: "Tenders",
                newName: "TechSendDate");

            migrationBuilder.AddColumn<string>(
                name: "CallResult",
                table: "Tenders",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Count",
                table: "Tenders",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerCode",
                table: "Tenders",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerMobile",
                table: "Tenders",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "Tenders",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerResult",
                table: "Tenders",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerRole",
                table: "Tenders",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerTel",
                table: "Tenders",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinanceSendDate",
                table: "Tenders",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PmrNo",
                table: "Tenders",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReciveDate",
                table: "Tenders",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "SubCompany",
                table: "Tenders",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "3087eaff-a7ac-4be3-b29b-3f04d69b60af");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "4171e3a4-848a-497a-a6d6-c2c73ae8856a");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "ea5e9cac-9dc5-4a02-a95a-e0b26934b128");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "92543747-cc8b-4171-ae67-23fe9dfd8138");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "5c42e549-c11f-445f-ac26-8aad029e3e0b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "3c9bbf2b-5fc0-4928-9ebc-7d8514502eb6");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "db5c6eaf-1a25-4d7c-a354-c1b60af2eba4");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "5b127af2-7497-4a5b-89b1-38e1d570dc70");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CallResult",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "Count",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "CustomerCode",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "CustomerMobile",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "CustomerResult",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "CustomerRole",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "CustomerTel",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "FinanceSendDate",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "PmrNo",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ReciveDate",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "SubCompany",
                table: "Tenders");

            migrationBuilder.RenameColumn(
                name: "TechSendDate",
                table: "Tenders",
                newName: "TenderDocsDate");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "28b73393-dab8-442a-8abf-c39675f55d51");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "76d50f74-bfb6-46ea-8c5f-157c98430fa8");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "8300286c-0ab7-4b66-a414-0d5716aa7442");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "062a6e2c-25d0-4f1e-b9dc-fb78f0466afa");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "899f8672-e539-4e9f-842f-6a6ed0d64f60");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "c3a547f8-6743-4111-a6cb-3062db7264e2");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "8c3ed7dc-01e3-47e2-a643-cc202839ffd5");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "1228c54c-e56a-4421-8084-385c58a7f138");
        }
    }
}
