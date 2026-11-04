using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class WarehouseCompletionP5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "InvWarehouses",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkOrderId",
                table: "InvWarehouses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EquipmentType",
                table: "InvItems",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IranCode",
                table: "InvItems",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeadTimeDays",
                table: "InvItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "InvItems",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReorderQuantity",
                table: "InvItems",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValveModel",
                table: "InvItems",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultLeadTimeDays",
                table: "InvItemGroups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultMaxStock",
                table: "InvItemGroups",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultMinStock",
                table: "InvItemGroups",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultReorderQuantity",
                table: "InvItemGroups",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillOfLadingNumber",
                table: "InvDocuments",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsignmentDueDate",
                table: "InvDocuments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConsignmentOfDocumentId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectsDocumentId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoodsType",
                table: "InvDocuments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PriceApprovedAt",
                table: "InvDocuments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriceApprovedByUserId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "InvDocuments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                table: "InvDocuments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByUserId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tag = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    SerialUnitId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PurchaseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CustodianUnit = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvAssets_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvAssets_InvLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "InvLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvAssets_InvSerialUnits_SerialUnitId",
                        column: x => x.SerialUnitId,
                        principalTable: "InvSerialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvAssets_InvWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvConsumptionCeilings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    MaxQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvConsumptionCeilings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvConsumptionCeilings_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvConsumptionCeilings_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvFreights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReceiptDocumentId = table.Column<int>(type: "int", nullable: false),
                    FreightDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VehiclePlate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GoodsType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillOfLadingNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CarrierProviderId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedByUserId = table.Column<int>(type: "int", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvFreights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvFreights_InvDocuments_ReceiptDocumentId",
                        column: x => x.ReceiptDocumentId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvFreights_Providers_CarrierProviderId",
                        column: x => x.CarrierProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvItemUnitConversions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    FromUnit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ToUnit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvItemUnitConversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvItemUnitConversions_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvPackingLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    BillOfLadingNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PackageNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    PackageCount = table.Column<int>(type: "int", nullable: true),
                    GrossWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvPackingLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvPackingLines_InvDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvPackingLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvFreightAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FreightId = table.Column<int>(type: "int", nullable: false),
                    DocumentLineId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    BasisAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StockAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RevaluationAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvFreightAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvFreightAllocations_InvDocumentLines_DocumentLineId",
                        column: x => x.DocumentLineId,
                        principalTable: "InvDocumentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvFreightAllocations_InvFreights_FreightId",
                        column: x => x.FreightId,
                        principalTable: "InvFreights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvFreightAllocations_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvWarehouses_WorkOrderId",
                table: "InvWarehouses",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvItems_IranCode",
                table: "InvItems",
                column: "IranCode");

            migrationBuilder.CreateIndex(
                name: "IX_InvItems_Platform",
                table: "InvItems",
                column: "Platform");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_ConsignmentDueDate",
                table: "InvDocuments",
                column: "ConsignmentDueDate");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_ConsignmentOfDocumentId",
                table: "InvDocuments",
                column: "ConsignmentOfDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_CorrectsDocumentId",
                table: "InvDocuments",
                column: "CorrectsDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvAssets_ItemId",
                table: "InvAssets",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvAssets_LocationId",
                table: "InvAssets",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvAssets_SerialUnitId",
                table: "InvAssets",
                column: "SerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvAssets_Tag",
                table: "InvAssets",
                column: "Tag",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvAssets_WarehouseId",
                table: "InvAssets",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InvConsumptionCeilings_ItemId_WorkOrderId",
                table: "InvConsumptionCeilings",
                columns: new[] { "ItemId", "WorkOrderId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvConsumptionCeilings_WorkOrderId",
                table: "InvConsumptionCeilings",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvFreightAllocations_DocumentLineId",
                table: "InvFreightAllocations",
                column: "DocumentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InvFreightAllocations_FreightId",
                table: "InvFreightAllocations",
                column: "FreightId");

            migrationBuilder.CreateIndex(
                name: "IX_InvFreightAllocations_ItemId",
                table: "InvFreightAllocations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvFreights_CarrierProviderId",
                table: "InvFreights",
                column: "CarrierProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvFreights_Number",
                table: "InvFreights",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvFreights_ReceiptDocumentId",
                table: "InvFreights",
                column: "ReceiptDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvItemUnitConversions_ItemId_FromUnit_ToUnit",
                table: "InvItemUnitConversions",
                columns: new[] { "ItemId", "FromUnit", "ToUnit" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvPackingLines_DocumentId_BillOfLadingNumber",
                table: "InvPackingLines",
                columns: new[] { "DocumentId", "BillOfLadingNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_InvPackingLines_ItemId",
                table: "InvPackingLines",
                column: "ItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvDocuments_InvDocuments_ConsignmentOfDocumentId",
                table: "InvDocuments",
                column: "ConsignmentOfDocumentId",
                principalTable: "InvDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvDocuments_InvDocuments_CorrectsDocumentId",
                table: "InvDocuments",
                column: "CorrectsDocumentId",
                principalTable: "InvDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvWarehouses_WorkOrders_WorkOrderId",
                table: "InvWarehouses",
                column: "WorkOrderId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvDocuments_InvDocuments_ConsignmentOfDocumentId",
                table: "InvDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_InvDocuments_InvDocuments_CorrectsDocumentId",
                table: "InvDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_InvWarehouses_WorkOrders_WorkOrderId",
                table: "InvWarehouses");

            migrationBuilder.DropTable(
                name: "InvAssets");

            migrationBuilder.DropTable(
                name: "InvConsumptionCeilings");

            migrationBuilder.DropTable(
                name: "InvFreightAllocations");

            migrationBuilder.DropTable(
                name: "InvItemUnitConversions");

            migrationBuilder.DropTable(
                name: "InvPackingLines");

            migrationBuilder.DropTable(
                name: "InvFreights");

            migrationBuilder.DropIndex(
                name: "IX_InvWarehouses_WorkOrderId",
                table: "InvWarehouses");

            migrationBuilder.DropIndex(
                name: "IX_InvItems_IranCode",
                table: "InvItems");

            migrationBuilder.DropIndex(
                name: "IX_InvItems_Platform",
                table: "InvItems");

            migrationBuilder.DropIndex(
                name: "IX_InvDocuments_ConsignmentDueDate",
                table: "InvDocuments");

            migrationBuilder.DropIndex(
                name: "IX_InvDocuments_ConsignmentOfDocumentId",
                table: "InvDocuments");

            migrationBuilder.DropIndex(
                name: "IX_InvDocuments_CorrectsDocumentId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "InvWarehouses");

            migrationBuilder.DropColumn(
                name: "WorkOrderId",
                table: "InvWarehouses");

            migrationBuilder.DropColumn(
                name: "EquipmentType",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "IranCode",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "LeadTimeDays",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "ReorderQuantity",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "ValveModel",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "DefaultLeadTimeDays",
                table: "InvItemGroups");

            migrationBuilder.DropColumn(
                name: "DefaultMaxStock",
                table: "InvItemGroups");

            migrationBuilder.DropColumn(
                name: "DefaultMinStock",
                table: "InvItemGroups");

            migrationBuilder.DropColumn(
                name: "DefaultReorderQuantity",
                table: "InvItemGroups");

            migrationBuilder.DropColumn(
                name: "BillOfLadingNumber",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "ConsignmentDueDate",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "ConsignmentOfDocumentId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "CorrectsDocumentId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "GoodsType",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "PriceApprovedAt",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "PriceApprovedByUserId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "InvDocuments");
        }
    }
}
