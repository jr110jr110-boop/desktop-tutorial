using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ResourcesP4ab : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PcResourcePlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MakerUserId = table.Column<int>(type: "int", nullable: false),
                    PreparedByAgent = table.Column<bool>(type: "bit", nullable: false),
                    ChangeRequestId = table.Column<int>(type: "int", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReturnedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReturnedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcResourcePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcResourcePlans_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcResources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<int>(type: "int", nullable: false),
                    Capacity = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    CalendarId = table.Column<int>(type: "int", nullable: true),
                    ResponsibleUnit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    WorkCenterId = table.Column<int>(type: "int", nullable: true),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    RateResourceId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcResources_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcResources_MfWorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "MfWorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcResources_PcCalendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "PcCalendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcResources_PcResources_RateResourceId",
                        column: x => x.RateResourceId,
                        principalTable: "PcResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcResources_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcResourcePlanSkips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    MarkedByUserId = table.Column<int>(type: "int", nullable: false),
                    MarkedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcResourcePlanSkips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcResourcePlanSkips_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcResourcePlanSkips_PcResourcePlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "PcResourcePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcActivityResources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Distribution = table.Column<int>(type: "int", nullable: false),
                    ResponsibleRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    EditedByUserId = table.Column<int>(type: "int", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcActivityResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcActivityResources_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcActivityResources_PcResourcePlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "PcResourcePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PcActivityResources_PcResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "PcResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcBaselineResources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaselineId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Distribution = table.Column<int>(type: "int", nullable: false),
                    RateAtBaseline = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcBaselineResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcBaselineResources_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcBaselineResources_PcBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "PcBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PcBaselineResources_PcResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "PcResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcResourceRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MakerUserId = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_PcResourceRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcResourceRates_PcResources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "PcResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PcActivityResources_ActivityId",
                table: "PcActivityResources",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcActivityResources_PlanId_ActivityId_ResourceId",
                table: "PcActivityResources",
                columns: new[] { "PlanId", "ActivityId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcActivityResources_ResourceId",
                table: "PcActivityResources",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PcBaselineResources_ActivityId",
                table: "PcBaselineResources",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcBaselineResources_BaselineId_ActivityId_ResourceId",
                table: "PcBaselineResources",
                columns: new[] { "BaselineId", "ActivityId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcBaselineResources_ResourceId",
                table: "PcBaselineResources",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PcResourcePlans_ChangeRequestId",
                table: "PcResourcePlans",
                column: "ChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PcResourcePlans_ContractId_Version",
                table: "PcResourcePlans",
                columns: new[] { "ContractId", "Version" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcResourcePlans_OneOpenPerContract",
                table: "PcResourcePlans",
                column: "ContractId",
                unique: true,
                filter: "[Deleted] = 0 AND [Status] IN (0, 1, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_PcResourcePlanSkips_ActivityId",
                table: "PcResourcePlanSkips",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcResourcePlanSkips_PlanId_ActivityId",
                table: "PcResourcePlanSkips",
                columns: new[] { "PlanId", "ActivityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcResourceRates_OneLivePerDate",
                table: "PcResourceRates",
                columns: new[] { "ResourceId", "EffectiveFrom" },
                unique: true,
                filter: "[Deleted] = 0 AND [Status] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_PcResourceRates_ResourceId_Status_EffectiveFrom",
                table: "PcResourceRates",
                columns: new[] { "ResourceId", "Status", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PcResources_CalendarId",
                table: "PcResources",
                column: "CalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_PcResources_Code",
                table: "PcResources",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcResources_EmployeeId",
                table: "PcResources",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PcResources_Kind_IsActive",
                table: "PcResources",
                columns: new[] { "Kind", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PcResources_ProviderId",
                table: "PcResources",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_PcResources_RateResourceId",
                table: "PcResources",
                column: "RateResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PcResources_ResponsibleUnit",
                table: "PcResources",
                column: "ResponsibleUnit");

            migrationBuilder.CreateIndex(
                name: "IX_PcResources_WorkCenterId",
                table: "PcResources",
                column: "WorkCenterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PcActivityResources");

            migrationBuilder.DropTable(
                name: "PcBaselineResources");

            migrationBuilder.DropTable(
                name: "PcResourcePlanSkips");

            migrationBuilder.DropTable(
                name: "PcResourceRates");

            migrationBuilder.DropTable(
                name: "PcResourcePlans");

            migrationBuilder.DropTable(
                name: "PcResources");
        }
    }
}
