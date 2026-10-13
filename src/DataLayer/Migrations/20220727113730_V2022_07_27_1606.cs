using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2022_07_27_1606 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProgressReports",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Deleted = table.Column<bool>(nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(nullable: false),
                    CreatorUserId = table.Column<int>(nullable: false),
                    ModifierUserId = table.Column<int>(nullable: false),
                    ProcessId = table.Column<int>(nullable: false),
                    ContractId = table.Column<int>(nullable: false),
                    WorkOrderId = table.Column<int>(nullable: false),
                    PlannedStartDate = table.Column<DateTimeOffset>(nullable: true),
                    ActualStartDate = table.Column<DateTimeOffset>(nullable: true),
                    PlannedEndDate = table.Column<DateTimeOffset>(nullable: true),
                    ActualEndDate = table.Column<DateTimeOffset>(nullable: true),
                    ReportDate = table.Column<DateTimeOffset>(nullable: false),
                    PlannedPrice = table.Column<decimal>(nullable: false),
                    ActualPrice = table.Column<decimal>(nullable: true),
                    ContractLevelPlannedProgress = table.Column<decimal>(nullable: false),
                    ContractLevelActualProgress = table.Column<decimal>(nullable: false),
                    WorkOrderLevelPlannedProgress = table.Column<decimal>(nullable: false),
                    WorkOrderLevelActualProgress = table.Column<decimal>(nullable: false),
                    WeightPercent = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgressReports_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProgressReports_Processes_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "Processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProgressReports_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "c9ebddf8-c053-4d33-9b8c-54827b53a766");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "25814e74-999f-4a52-9c9d-4b9098e3c5c6");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "df38c4cd-bf39-4fd4-aa81-a06505372c29");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "798a1872-461e-4045-9469-7f25735ce89c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "7a25a779-ac24-4fc1-9acc-f09c01af2b50");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "e248c70b-33f7-433f-b72d-716ea39c3e8a");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "6a937c11-3efb-492b-bbd3-624c19a16c4d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "554349ed-9b77-4e39-98e0-75d75651e532");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressReports_ContractId",
                table: "ProgressReports",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressReports_ProcessId",
                table: "ProgressReports",
                column: "ProcessId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgressReports_WorkOrderId",
                table: "ProgressReports",
                column: "WorkOrderId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProgressReports");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "09469d40-e8db-40df-b1b0-5de772a2a76b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "c518b502-766c-489d-8451-c41f6ae3ab1c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "d8db90ae-b947-4135-8184-061dd321d09b");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "664c5655-39b6-4ee9-8e2b-9857ddce557f");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "18835744-2d80-43ef-a187-402bd596f3d6");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "b7c0fb3e-ec17-4cba-8ea8-ca0ed0c485be");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "327b4a69-8271-449b-8ae1-8f27a8c847fd");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "dd0e73cb-3627-425a-afe6-803fffcd587a");
        }
    }
}
