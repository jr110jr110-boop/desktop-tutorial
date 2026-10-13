using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DataLayer.Migrations
{
    public partial class V2021_12_22_1720 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Deleted = table.Column<bool>(nullable: false),
                    CreateDate = table.Column<DateTime>(nullable: false),
                    ModifyDate = table.Column<DateTime>(nullable: false),
                    CreatorUserId = table.Column<int>(nullable: false),
                    ModifierUserId = table.Column<int>(nullable: false),
                    Code = table.Column<string>(nullable: true),
                    Title = table.Column<string>(nullable: true),
                    Description = table.Column<string>(nullable: true),
                    Count = table.Column<int>(nullable: false),
                    ContractId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Assemblies",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Deleted = table.Column<bool>(nullable: false),
                    CreateDate = table.Column<DateTime>(nullable: false),
                    ModifyDate = table.Column<DateTime>(nullable: false),
                    CreatorUserId = table.Column<int>(nullable: false),
                    ModifierUserId = table.Column<int>(nullable: false),
                    Title = table.Column<string>(nullable: true),
                    Code = table.Column<string>(nullable: true),
                    WorkOrderId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assemblies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assemblies_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BuyOrders",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Deleted = table.Column<bool>(nullable: false),
                    CreateDate = table.Column<DateTime>(nullable: false),
                    ModifyDate = table.Column<DateTime>(nullable: false),
                    CreatorUserId = table.Column<int>(nullable: false),
                    ModifierUserId = table.Column<int>(nullable: false),
                    ExitCommodityCode = table.Column<string>(nullable: true),
                    EnterCommodityCode = table.Column<string>(nullable: true),
                    Count = table.Column<int>(nullable: false),
                    RegisterDate = table.Column<DateTime>(nullable: false),
                    InquiryDate = table.Column<DateTime>(nullable: false),
                    InquiryAcceptDate = table.Column<DateTime>(nullable: false),
                    FinanceAcceptDate = table.Column<DateTime>(nullable: false),
                    TempReceiveDate = table.Column<DateTime>(nullable: false),
                    ReceiveDate = table.Column<DateTime>(nullable: false),
                    ReserveCount = table.Column<int>(nullable: false),
                    BuyCount = table.Column<int>(nullable: false),
                    RealBuyCount = table.Column<int>(nullable: false),
                    FinanceType = table.Column<string>(nullable: true),
                    FinancePrice = table.Column<decimal>(nullable: false),
                    FinanceNumber = table.Column<string>(nullable: true),
                    FinanceDocDate = table.Column<DateTime>(nullable: false),
                    WorkOrderId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuyOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuyOrders_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Boms",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Deleted = table.Column<bool>(nullable: false),
                    CreateDate = table.Column<DateTime>(nullable: false),
                    ModifyDate = table.Column<DateTime>(nullable: false),
                    CreatorUserId = table.Column<int>(nullable: false),
                    ModifierUserId = table.Column<int>(nullable: false),
                    PositionNumber = table.Column<int>(nullable: false),
                    Title = table.Column<string>(nullable: true),
                    Count = table.Column<int>(nullable: false),
                    Unit = table.Column<string>(nullable: true),
                    Material = table.Column<string>(nullable: true),
                    Standard = table.Column<string>(nullable: true),
                    Diagonal = table.Column<decimal>(nullable: false),
                    Length = table.Column<decimal>(nullable: false),
                    Width = table.Column<decimal>(nullable: false),
                    Height = table.Column<decimal>(nullable: false),
                    NotWetWeight = table.Column<decimal>(nullable: false),
                    WetWeight = table.Column<decimal>(nullable: false),
                    Description = table.Column<string>(nullable: true),
                    Sort = table.Column<int>(nullable: false),
                    AssemblyId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Boms_Assemblies_AssemblyId",
                        column: x => x.AssemblyId,
                        principalTable: "Assemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Inquiries",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Deleted = table.Column<bool>(nullable: false),
                    CreateDate = table.Column<DateTime>(nullable: false),
                    ModifyDate = table.Column<DateTime>(nullable: false),
                    CreatorUserId = table.Column<int>(nullable: false),
                    ModifierUserId = table.Column<int>(nullable: false),
                    TenderId = table.Column<int>(nullable: false),
                    BuyOrderId = table.Column<int>(nullable: false),
                    Count = table.Column<int>(nullable: false),
                    Price = table.Column<decimal>(nullable: false),
                    IsAccept = table.Column<bool>(nullable: false),
                    TechnicalScore = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inquiries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Inquiries_BuyOrders_BuyOrderId",
                        column: x => x.BuyOrderId,
                        principalTable: "BuyOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Inquiries_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Processes",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Deleted = table.Column<bool>(nullable: false),
                    CreateDate = table.Column<DateTime>(nullable: false),
                    ModifyDate = table.Column<DateTime>(nullable: false),
                    CreatorUserId = table.Column<int>(nullable: false),
                    ModifierUserId = table.Column<int>(nullable: false),
                    ExitCommodityCode = table.Column<string>(nullable: true),
                    EnterCommodityCode = table.Column<string>(nullable: true),
                    MapDuration = table.Column<int>(nullable: false),
                    MasterDuration = table.Column<int>(nullable: false),
                    ProcessDuration = table.Column<int>(nullable: false),
                    Type = table.Column<int>(nullable: false),
                    Sort = table.Column<int>(nullable: false),
                    ProgramStartDate = table.Column<DateTime>(nullable: false),
                    ProgramEndDate = table.Column<DateTime>(nullable: false),
                    ActualStartDate = table.Column<DateTime>(nullable: false),
                    ActualEndDate = table.Column<DateTime>(nullable: false),
                    DocAcceptDate = table.Column<DateTime>(nullable: false),
                    MasterAcceptDate = table.Column<DateTime>(nullable: false),
                    ProgramPrice = table.Column<decimal>(nullable: false),
                    BomId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Processes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Processes_Boms_BomId",
                        column: x => x.BomId,
                        principalTable: "Boms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderProcesses",
                columns: table => new
                {
                    BuyOrderId = table.Column<int>(nullable: false),
                    ProcessId = table.Column<int>(nullable: false),
                    Attribute = table.Column<string>(nullable: true),
                    Deleted = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderProcesses", x => new { x.ProcessId, x.BuyOrderId });
                    table.ForeignKey(
                        name: "FK_OrderProcesses_BuyOrders_BuyOrderId",
                        column: x => x.BuyOrderId,
                        principalTable: "BuyOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderProcesses_Processes_ProcessId",
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
                name: "IX_Assemblies_WorkOrderId",
                table: "Assemblies",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Boms_AssemblyId",
                table: "Boms",
                column: "AssemblyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuyOrders_WorkOrderId",
                table: "BuyOrders",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Inquiries_BuyOrderId",
                table: "Inquiries",
                column: "BuyOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Inquiries_TenderId",
                table: "Inquiries",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderProcesses_BuyOrderId",
                table: "OrderProcesses",
                column: "BuyOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Processes_BomId",
                table: "Processes",
                column: "BomId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ContractId",
                table: "WorkOrders",
                column: "ContractId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Inquiries");

            migrationBuilder.DropTable(
                name: "OrderProcesses");

            migrationBuilder.DropTable(
                name: "BuyOrders");

            migrationBuilder.DropTable(
                name: "Processes");

            migrationBuilder.DropTable(
                name: "Boms");

            migrationBuilder.DropTable(
                name: "Assemblies");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 1,
                column: "ConcurrencyStamp",
                value: "3903fdef-4a5c-429e-a9a9-9491eda25f20");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 2,
                column: "ConcurrencyStamp",
                value: "d564f63b-a441-49ec-88a8-5642d60a5153");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 3,
                column: "ConcurrencyStamp",
                value: "3c09fe70-337a-4b67-83a9-83959e68dd8c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 4,
                column: "ConcurrencyStamp",
                value: "e36866bb-bddf-4cfa-be2c-8863f4e13169");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 5,
                column: "ConcurrencyStamp",
                value: "be375388-2826-4603-ba59-864b9079266d");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 6,
                column: "ConcurrencyStamp",
                value: "a2d90b0c-5deb-4eff-b625-191fba0aaabd");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 7,
                column: "ConcurrencyStamp",
                value: "d98dac6d-b671-4698-9cd3-fafa68d0120c");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 8,
                column: "ConcurrencyStamp",
                value: "8f602d24-5ccd-4157-8f3d-06c9697d99e9");
        }
    }
}
