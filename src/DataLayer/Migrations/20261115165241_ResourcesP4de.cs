using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ResourcesP4de : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PcCharterResources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcCharterResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcCharterResources_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcCharterResources_PcResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "PcResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcResourceCostProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    PlanVersion = table.Column<int>(type: "int", nullable: false),
                    ResourceClass = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ExistingAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ExcludedNoRate = table.Column<int>(type: "int", nullable: false),
                    ExcludedSensitive = table.Column<int>(type: "int", nullable: false),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedByAgent = table.Column<bool>(type: "bit", nullable: false),
                    CostLineId = table.Column<int>(type: "int", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcResourceCostProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcResourceCostProposals_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcResourceCostProposals_PcCostBaselineLines_CostLineId",
                        column: x => x.CostLineId,
                        principalTable: "PcCostBaselineLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PcResourceCostProposals_PcResourcePlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "PcResourcePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PcCharterResources_ContractId_ResourceId",
                table: "PcCharterResources",
                columns: new[] { "ContractId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcCharterResources_ResourceId",
                table: "PcCharterResources",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PcResourceCostProposals_ContractId_Status",
                table: "PcResourceCostProposals",
                columns: new[] { "ContractId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PcResourceCostProposals_CostLineId",
                table: "PcResourceCostProposals",
                column: "CostLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PcResourceCostProposals_PlanId_ResourceClass",
                table: "PcResourceCostProposals",
                columns: new[] { "PlanId", "ResourceClass" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PcCharterResources");

            migrationBuilder.DropTable(
                name: "PcResourceCostProposals");
        }
    }
}
