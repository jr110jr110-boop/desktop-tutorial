using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class _2022_20_27_1637 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenderGuaranties",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    TenderId = table.Column<int>(nullable: false),
                    Title = table.Column<string>(nullable: true),
                    Bank = table.Column<string>(nullable: true),
                    No = table.Column<string>(nullable: true),
                    RegDate = table.Column<DateTime>(nullable: true),
                    Price = table.Column<string>(nullable: true),
                    Deposite = table.Column<string>(nullable: true),
                    ChequeNo = table.Column<string>(nullable: true),
                    SentDate = table.Column<DateTime>(nullable: true),
                    CancelDate = table.Column<DateTime>(nullable: true),
                    Extention = table.Column<string>(nullable: true),
                    DueDate = table.Column<DateTime>(nullable: true),
                    Description = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderGuaranties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenderGuaranties_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_TenderGuaranties_TenderId",
                table: "TenderGuaranties",
                column: "TenderId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenderGuaranties");

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
    }
}
