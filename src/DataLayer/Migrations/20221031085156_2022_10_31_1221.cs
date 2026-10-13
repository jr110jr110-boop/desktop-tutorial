using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class _2022_10_31_1221 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateTable(
                name: "ContractGuaranties",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Deleted = table.Column<bool>(nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(nullable: false),
                    CreatorUserId = table.Column<int>(nullable: false),
                    ModifierUserId = table.Column<int>(nullable: false),
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
                    Description = table.Column<string>(nullable: true),
                    ContractEntityId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractGuaranties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractGuaranties_Contracts_ContractEntityId",
                        column: x => x.ContractEntityId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "9432bb92-1fa9-4486-8940-bd88e10c59b2");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "c4d376e1-ac12-41ee-abea-22da268e3479");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "3b7e9121-bf44-478c-a6cc-79f41903601b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "d21f96f3-c705-4f97-8170-cd118c07b299");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "f5482bb1-b1f7-4cb3-99f4-076b4561630b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "934f01b7-c68b-4268-ae41-2633b76d9342");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "78830416-eda8-43ca-a529-8969d3f5d1b7");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "35f5e645-4215-4b93-8575-6ef2129770cd");

            //migrationBuilder.CreateIndex(
            //    name: "IX_TenderGuaranties_TenderId",
            //    table: "TenderGuaranties",
            //    column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractGuaranties_ContractEntityId",
                table: "ContractGuaranties",
                column: "ContractEntityId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractGuaranties");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TenderGuaranties",
                table: "TenderGuaranties");

            migrationBuilder.DropIndex(
                name: "IX_TenderGuaranties_TenderId",
                table: "TenderGuaranties");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "TenderGuaranties",
                nullable: false,
                oldClrType: typeof(int))
                .OldAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_TenderGuaranties_Id",
                table: "TenderGuaranties",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TenderGuaranties",
                table: "TenderGuaranties",
                column: "TenderId");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "0b99201a-c061-4f06-9b3f-2301e7b38104");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "63f63e4d-4a53-4baa-9a5b-d4e8640e58e4");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "9071b611-f875-4286-8bfa-5246aebc19e7");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "4201aa8f-17e0-4576-b388-8ff3e9e23d33");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "dda3dbbb-9b68-4c4f-beae-fd70294285ba");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "fa0072b2-a726-465a-b4b9-1289ce9d403c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "ca4cec81-014a-4fb4-b937-16745a0c68f8");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "c397e77a-609e-481c-9bc4-b10f1423ebdf");
        }
    }
}
