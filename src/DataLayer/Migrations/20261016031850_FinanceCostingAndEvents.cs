using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class FinanceCostingAndEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostAmount",
                table: "InvStockTransactions",
                type: "decimal(22,2)",
                precision: 22,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "InvStockTransactions",
                type: "decimal(22,4)",
                precision: 22,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinAccountingEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    SourceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReversesEventId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinAccountingEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinAccountMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SepidarAccountCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinAccountMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinCostCenters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SepidarCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinCostCenters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinCostEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    CostCenter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    SourceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinCostEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinCostEntries_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinCostEntries_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinCostEntries_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinItemCosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    AverageCost = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: false),
                    LastPurchasePrice = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinItemCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinItemCosts_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinAccountingEventLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    AccountRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    CostCenter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinAccountingEventLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinAccountingEventLines_FinAccountingEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "FinAccountingEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinAccountingEventLines_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinAccountingEventLines_AccountRole",
                table: "FinAccountingEventLines",
                column: "AccountRole");

            migrationBuilder.CreateIndex(
                name: "IX_FinAccountingEventLines_EventId",
                table: "FinAccountingEventLines",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_FinAccountingEventLines_ProviderId",
                table: "FinAccountingEventLines",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_FinAccountingEvents_Key",
                table: "FinAccountingEvents",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinAccountingEvents_SourceType_SourceId",
                table: "FinAccountingEvents",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinAccountingEvents_Status_EventDate",
                table: "FinAccountingEvents",
                columns: new[] { "Status", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FinAccountMappings_Role",
                table: "FinAccountMappings",
                column: "Role",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinCostCenters_Code",
                table: "FinCostCenters",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinCostEntries_ContractId",
                table: "FinCostEntries",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_FinCostEntries_ItemId",
                table: "FinCostEntries",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FinCostEntries_SourceType_SourceId",
                table: "FinCostEntries",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinCostEntries_WorkOrderId",
                table: "FinCostEntries",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FinItemCosts_ItemId",
                table: "FinItemCosts",
                column: "ItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinAccountingEventLines");

            migrationBuilder.DropTable(
                name: "FinAccountMappings");

            migrationBuilder.DropTable(
                name: "FinCostCenters");

            migrationBuilder.DropTable(
                name: "FinCostEntries");

            migrationBuilder.DropTable(
                name: "FinItemCosts");

            migrationBuilder.DropTable(
                name: "FinAccountingEvents");

            migrationBuilder.DropColumn(
                name: "CostAmount",
                table: "InvStockTransactions");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "InvStockTransactions");
        }
    }
}
