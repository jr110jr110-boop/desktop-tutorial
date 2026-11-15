using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ResourcesP4c : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PcResourceOverloads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeakLoad = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PeakCapacity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptedByUserId = table.Column<int>(type: "int", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NoProposalAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcResourceOverloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcResourceOverloads_PcResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "PcResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcLevelingProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OverloadId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CurrentStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentFinish = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProposedStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProposedFinish = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShiftDays = table.Column<int>(type: "int", nullable: false),
                    CriticalImpactDays = table.Column<int>(type: "int", nullable: false),
                    NeedsChangeRequest = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedByAgent = table.Column<bool>(type: "bit", nullable: false),
                    MakerUserId = table.Column<int>(type: "int", nullable: false),
                    PriorConstraintType = table.Column<int>(type: "int", nullable: false),
                    PriorConstraintDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppliedConstraintDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApplierUserId = table.Column<int>(type: "int", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Reverted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcLevelingProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcLevelingProposals_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcLevelingProposals_PcResourceOverloads_OverloadId",
                        column: x => x.OverloadId,
                        principalTable: "PcResourceOverloads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PcLevelingProposals_ActivityId",
                table: "PcLevelingProposals",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcLevelingProposals_OverloadId_Status",
                table: "PcLevelingProposals",
                columns: new[] { "OverloadId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PcLevelingProposals_Status_ContractId",
                table: "PcLevelingProposals",
                columns: new[] { "Status", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_PcResourceOverloads_OneOpenPerStart",
                table: "PcResourceOverloads",
                columns: new[] { "ResourceId", "FromDate" },
                unique: true,
                filter: "[Deleted] = 0 AND [Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PcResourceOverloads_Status_FromDate",
                table: "PcResourceOverloads",
                columns: new[] { "Status", "FromDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PcLevelingProposals");

            migrationBuilder.DropTable(
                name: "PcResourceOverloads");
        }
    }
}
