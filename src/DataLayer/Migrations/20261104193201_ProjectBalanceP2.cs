using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ProjectBalanceP2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BalanceGroupKey",
                table: "ProcPurchaseRequests",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "ProcPurchaseRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProjectBaselineId",
                table: "ProcPurchaseRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BalanceLineId",
                table: "ProcPurchaseRequestLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ManuallyEdited",
                table: "ProcPurchaseRequestLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WbsNodeId",
                table: "ProcPurchaseRequestLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentLineId",
                table: "PbProjectBomLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActivityId",
                table: "InvMaterialRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LinkObsolete",
                table: "AttRecordAttachments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LinkedFromAttachmentId",
                table: "AttRecordAttachments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PbalBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    BaselineId = table.Column<int>(type: "int", nullable: false),
                    BomHeaderId = table.Column<int>(type: "int", nullable: false),
                    BomCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    BomRevision = table.Column<int>(type: "int", nullable: false),
                    RunNumber = table.Column<int>(type: "int", nullable: false),
                    RunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RunByUserId = table.Column<int>(type: "int", nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PreviousRunId = table.Column<int>(type: "int", nullable: true),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    ShortLineCount = table.Column<int>(type: "int", nullable: false),
                    NotCodedCount = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbalBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbalBalances_PbProjectBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "PbProjectBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalBalances_PbalBalances_PreviousRunId",
                        column: x => x.PreviousRunId,
                        principalTable: "PbalBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalBalances_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbalItemSubstitutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    SubstituteItemId = table.Column<int>(type: "int", nullable: false),
                    Ratio = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProposedByUserId = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbalItemSubstitutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbalItemSubstitutes_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalItemSubstitutes_InvItems_SubstituteItemId",
                        column: x => x.SubstituteItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbalReservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    BaselineId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IssuedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsHard = table.Column<bool>(type: "bit", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    SerialUnitId = table.Column<int>(type: "int", nullable: true),
                    NeedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WbsNodeId = table.Column<int>(type: "int", nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    SubstituteForItemId = table.Column<int>(type: "int", nullable: true),
                    SourceBalanceId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReleaseRequestedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReleaseRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleaseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AutoReserveBlocked = table.Column<bool>(type: "bit", nullable: false),
                    ReEnabledByUserId = table.Column<int>(type: "int", nullable: true),
                    ReEnabledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReEnableReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbalReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbalReservations_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalReservations_InvItems_SubstituteForItemId",
                        column: x => x.SubstituteForItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalReservations_InvLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalReservations_InvSerialUnits_SerialUnitId",
                        column: x => x.SerialUnitId,
                        principalTable: "InvSerialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalReservations_InvWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalReservations_PbProjectBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "PbProjectBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalReservations_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalReservations_PcWbsNodes_WbsNodeId",
                        column: x => x.WbsNodeId,
                        principalTable: "PcWbsNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalReservations_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbalBalanceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BalanceId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    PartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PartName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MakeOrBuy = table.Column<int>(type: "int", nullable: false),
                    WbsNodeId = table.Column<int>(type: "int", nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    NeedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UseByDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeadTimeDays = table.Column<int>(type: "int", nullable: false),
                    TotalFloat = table.Column<int>(type: "int", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    GrossQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ScrapQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RequiredQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IssuedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReservedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReservedNewQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OnHandQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AvailableQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OnOrderPrQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OnOrderPoQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SubstituteQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SubstituteItemId = table.Column<int>(type: "int", nullable: true),
                    UnitMismatch = table.Column<bool>(type: "bit", nullable: false),
                    ShortQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: true),
                    PurchaseRequestLineId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbalBalanceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbalBalanceLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalBalanceLines_InvItems_SubstituteItemId",
                        column: x => x.SubstituteItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalBalanceLines_PbalBalances_BalanceId",
                        column: x => x.BalanceId,
                        principalTable: "PbalBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PbalBalanceLines_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbalBalanceLines_PcWbsNodes_WbsNodeId",
                        column: x => x.WbsNodeId,
                        principalTable: "PcWbsNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequests_ProjectBaselineId_BalanceGroupKey",
                table: "ProcPurchaseRequests",
                columns: new[] { "ProjectBaselineId", "BalanceGroupKey" },
                unique: true,
                filter: "[ProjectBaselineId] IS NOT NULL AND [Status] = 0 AND [Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequestLines_WbsNodeId",
                table: "ProcPurchaseRequestLines",
                column: "WbsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBomLines_ParentLineId",
                table: "PbProjectBomLines",
                column: "ParentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InvMaterialRequests_ActivityId",
                table: "InvMaterialRequests",
                column: "ActivityId",
                unique: true,
                filter: "[ActivityId] IS NOT NULL AND [Deleted] = 0 AND [Status] <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_AttRecordAttachments_LinkedFromAttachmentId",
                table: "AttRecordAttachments",
                column: "LinkedFromAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalBalanceLines_ActivityId",
                table: "PbalBalanceLines",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalBalanceLines_BalanceId",
                table: "PbalBalanceLines",
                column: "BalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalBalanceLines_ItemId",
                table: "PbalBalanceLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalBalanceLines_SubstituteItemId",
                table: "PbalBalanceLines",
                column: "SubstituteItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalBalanceLines_WbsNodeId",
                table: "PbalBalanceLines",
                column: "WbsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalBalances_BaselineId_RunNumber",
                table: "PbalBalances",
                columns: new[] { "BaselineId", "RunNumber" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbalBalances_ContractId",
                table: "PbalBalances",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalBalances_PreviousRunId",
                table: "PbalBalances",
                column: "PreviousRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalBalances_WorkOrderId",
                table: "PbalBalances",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalItemSubstitutes_ItemId_Status",
                table: "PbalItemSubstitutes",
                columns: new[] { "ItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PbalItemSubstitutes_SubstituteItemId",
                table: "PbalItemSubstitutes",
                column: "SubstituteItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalReservations_ActivityId",
                table: "PbalReservations",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalReservations_BaselineId",
                table: "PbalReservations",
                column: "BaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalReservations_BaselineId_ItemId_SubstituteForItemId_WarehouseId_LotId",
                table: "PbalReservations",
                columns: new[] { "BaselineId", "ItemId", "SubstituteForItemId", "WarehouseId", "LotId" },
                unique: true,
                filter: "[Status] = 1 AND [Deleted] = 0 AND [SerialUnitId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PbalReservations_ContractId",
                table: "PbalReservations",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalReservations_ItemId_Status",
                table: "PbalReservations",
                columns: new[] { "ItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PbalReservations_LotId",
                table: "PbalReservations",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalReservations_SerialUnitId",
                table: "PbalReservations",
                column: "SerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalReservations_SubstituteForItemId",
                table: "PbalReservations",
                column: "SubstituteForItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalReservations_WarehouseId",
                table: "PbalReservations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalReservations_WbsNodeId",
                table: "PbalReservations",
                column: "WbsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PbalReservations_WorkOrderId",
                table: "PbalReservations",
                column: "WorkOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvMaterialRequests_PcActivities_ActivityId",
                table: "InvMaterialRequests",
                column: "ActivityId",
                principalTable: "PcActivities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PbProjectBomLines_PbProjectBomLines_ParentLineId",
                table: "PbProjectBomLines",
                column: "ParentLineId",
                principalTable: "PbProjectBomLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProcPurchaseRequestLines_PcWbsNodes_WbsNodeId",
                table: "ProcPurchaseRequestLines",
                column: "WbsNodeId",
                principalTable: "PcWbsNodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProcPurchaseRequests_PbProjectBaselines_ProjectBaselineId",
                table: "ProcPurchaseRequests",
                column: "ProjectBaselineId",
                principalTable: "PbProjectBaselines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvMaterialRequests_PcActivities_ActivityId",
                table: "InvMaterialRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_PbProjectBomLines_PbProjectBomLines_ParentLineId",
                table: "PbProjectBomLines");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcPurchaseRequestLines_PcWbsNodes_WbsNodeId",
                table: "ProcPurchaseRequestLines");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcPurchaseRequests_PbProjectBaselines_ProjectBaselineId",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropTable(
                name: "PbalBalanceLines");

            migrationBuilder.DropTable(
                name: "PbalItemSubstitutes");

            migrationBuilder.DropTable(
                name: "PbalReservations");

            migrationBuilder.DropTable(
                name: "PbalBalances");

            migrationBuilder.DropIndex(
                name: "IX_ProcPurchaseRequests_ProjectBaselineId_BalanceGroupKey",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropIndex(
                name: "IX_ProcPurchaseRequestLines_WbsNodeId",
                table: "ProcPurchaseRequestLines");

            migrationBuilder.DropIndex(
                name: "IX_PbProjectBomLines_ParentLineId",
                table: "PbProjectBomLines");

            migrationBuilder.DropIndex(
                name: "IX_InvMaterialRequests_ActivityId",
                table: "InvMaterialRequests");

            migrationBuilder.DropIndex(
                name: "IX_AttRecordAttachments_LinkedFromAttachmentId",
                table: "AttRecordAttachments");

            migrationBuilder.DropColumn(
                name: "BalanceGroupKey",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "ProjectBaselineId",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "BalanceLineId",
                table: "ProcPurchaseRequestLines");

            migrationBuilder.DropColumn(
                name: "ManuallyEdited",
                table: "ProcPurchaseRequestLines");

            migrationBuilder.DropColumn(
                name: "WbsNodeId",
                table: "ProcPurchaseRequestLines");

            migrationBuilder.DropColumn(
                name: "ParentLineId",
                table: "PbProjectBomLines");

            migrationBuilder.DropColumn(
                name: "ActivityId",
                table: "InvMaterialRequests");

            migrationBuilder.DropColumn(
                name: "LinkObsolete",
                table: "AttRecordAttachments");

            migrationBuilder.DropColumn(
                name: "LinkedFromAttachmentId",
                table: "AttRecordAttachments");
        }
    }
}
