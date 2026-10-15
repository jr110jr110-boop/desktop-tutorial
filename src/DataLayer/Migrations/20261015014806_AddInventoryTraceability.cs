using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvItemGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvItemGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvItemGroups_InvItemGroups_ParentId",
                        column: x => x.ParentId,
                        principalTable: "InvItemGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvNumberSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvNumberSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvSepidarMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    SepidarCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvSepidarMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvWarehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SepidarCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvWarehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ItemGroupId = table.Column<int>(type: "int", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TrackingMode = table.Column<int>(type: "int", nullable: false),
                    TechnicalCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Material = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Standard = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Specification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    MinStock = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MaxStock = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SepidarCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvItems_InvItemGroups_ItemGroupId",
                        column: x => x.ItemGroupId,
                        principalTable: "InvItemGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    DestinationWarehouseId = table.Column<int>(type: "int", nullable: true),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    BuyOrderId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SepidarStatus = table.Column<int>(type: "int", nullable: false),
                    SepidarReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SepidarExportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvDocuments_BuyOrders_BuyOrderId",
                        column: x => x.BuyOrderId,
                        principalTable: "BuyOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvDocuments_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvDocuments_InvWarehouses_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvDocuments_InvWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvDocuments_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvDocuments_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvLocations_InvWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvLots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    LotNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HeatNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CertificateNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CertificateType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CertificateDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    ProductionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvLots_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvLots_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvOperationTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemGroupId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequiresQc = table.Column<bool>(type: "bit", nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvOperationTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvOperationTemplates_InvItemGroups_ItemGroupId",
                        column: x => x.ItemGroupId,
                        principalTable: "InvItemGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvOperationTemplates_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvOperationTemplates_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvDocumentLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    LotNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HeatNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CertificateNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    DestinationLocationId = table.Column<int>(type: "int", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvDocumentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvDocumentLines_InvDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvDocumentLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvDocumentLines_InvLocations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "InvLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvDocumentLines_InvLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "InvLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvDocumentLines_InvLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvSerialUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    ParentSerialUnitId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvSerialUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvSerialUnits_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvSerialUnits_InvLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "InvLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvSerialUnits_InvLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvSerialUnits_InvSerialUnits_ParentSerialUnitId",
                        column: x => x.ParentSerialUnitId,
                        principalTable: "InvSerialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvSerialUnits_InvWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvSerialUnits_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvStockBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvStockBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvStockBalances_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvStockBalances_InvLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "InvLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvStockBalances_InvLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvStockBalances_InvWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvAssemblyLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentSerialUnitId = table.Column<int>(type: "int", nullable: false),
                    ChildSerialUnitId = table.Column<int>(type: "int", nullable: true),
                    ChildLotId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InstalledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InstalledByUserId = table.Column<int>(type: "int", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RemovedByUserId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvAssemblyLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvAssemblyLinks_InvLots_ChildLotId",
                        column: x => x.ChildLotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvAssemblyLinks_InvSerialUnits_ChildSerialUnitId",
                        column: x => x.ChildSerialUnitId,
                        principalTable: "InvSerialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvAssemblyLinks_InvSerialUnits_ParentSerialUnitId",
                        column: x => x.ParentSerialUnitId,
                        principalTable: "InvSerialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvAssemblyLinks_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvDocumentLineSerials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LineId = table.Column<int>(type: "int", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SerialUnitId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvDocumentLineSerials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvDocumentLineSerials_InvDocumentLines_LineId",
                        column: x => x.LineId,
                        principalTable: "InvDocumentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvDocumentLineSerials_InvSerialUnits_SerialUnitId",
                        column: x => x.SerialUnitId,
                        principalTable: "InvSerialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvPartOperations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SerialUnitId = table.Column<int>(type: "int", nullable: true),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    OperationTemplateId = table.Column<int>(type: "int", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OperatorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OperatorUserId = table.Column<int>(type: "int", nullable: true),
                    MachineId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    QcStatus = table.Column<int>(type: "int", nullable: false),
                    QcByUserId = table.Column<int>(type: "int", nullable: true),
                    QcAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    QcNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvPartOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvPartOperations_InvLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvPartOperations_InvOperationTemplates_OperationTemplateId",
                        column: x => x.OperationTemplateId,
                        principalTable: "InvOperationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvPartOperations_InvSerialUnits_SerialUnitId",
                        column: x => x.SerialUnitId,
                        principalTable: "InvSerialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvPartOperations_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvPartOperations_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvStockTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    SerialUnitId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    DocumentLineId = table.Column<int>(type: "int", nullable: true),
                    IsReversal = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvStockTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvStockTransactions_InvDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvStockTransactions_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvStockTransactions_InvLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "InvLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvStockTransactions_InvLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvStockTransactions_InvSerialUnits_SerialUnitId",
                        column: x => x.SerialUnitId,
                        principalTable: "InvSerialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvStockTransactions_InvWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvPartOperationFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartOperationId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvPartOperationFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvPartOperationFiles_InvPartOperations_PartOperationId",
                        column: x => x.PartOperationId,
                        principalTable: "InvPartOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 112, "permission", "Warehouse", null, null, null, null, null, null, null, null, 1 },
                    { 113, "permission", "Traceability", null, null, null, null, null, null, null, null, 1 },
                    { 114, "permission", "SepidarLink", null, null, null, null, null, null, null, null, 1 },
                    { 202, "permission", "Warehouse", null, null, null, null, null, null, null, null, 2 },
                    { 308, "permission", "Warehouse", null, null, null, null, null, null, null, null, 4 },
                    { 309, "permission", "Traceability", null, null, null, null, null, null, null, null, 4 },
                    { 401, "permission", "Traceability", null, null, null, null, null, null, null, null, 8 },
                    { 500, "permission", "Warehouse", null, null, null, null, null, null, null, null, 3 },
                    { 501, "permission", "SepidarLink", null, null, null, null, null, null, null, null, 3 },
                    { 600, "permission", "Traceability", null, null, null, null, null, null, null, null, 5 },
                    { 601, "permission", "Warehouse", null, null, null, null, null, null, null, null, 5 },
                    { 700, "permission", "Warehouse", null, null, null, null, null, null, null, null, 6 },
                    { 701, "permission", "Traceability", null, null, null, null, null, null, null, null, 6 },
                    { 800, "permission", "Traceability", null, null, null, null, null, null, null, null, 7 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvAssemblyLinks_ChildLotId",
                table: "InvAssemblyLinks",
                column: "ChildLotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvAssemblyLinks_ChildSerialUnitId",
                table: "InvAssemblyLinks",
                column: "ChildSerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvAssemblyLinks_ParentSerialUnitId",
                table: "InvAssemblyLinks",
                column: "ParentSerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvAssemblyLinks_WorkOrderId",
                table: "InvAssemblyLinks",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocumentLines_DestinationLocationId",
                table: "InvDocumentLines",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocumentLines_DocumentId",
                table: "InvDocumentLines",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocumentLines_ItemId",
                table: "InvDocumentLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocumentLines_LocationId",
                table: "InvDocumentLines",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocumentLines_LotId",
                table: "InvDocumentLines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocumentLineSerials_LineId",
                table: "InvDocumentLineSerials",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocumentLineSerials_SerialUnitId",
                table: "InvDocumentLineSerials",
                column: "SerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_BuyOrderId",
                table: "InvDocuments",
                column: "BuyOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_ContractId",
                table: "InvDocuments",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_DestinationWarehouseId",
                table: "InvDocuments",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_Number",
                table: "InvDocuments",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_ProviderId",
                table: "InvDocuments",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_SepidarStatus",
                table: "InvDocuments",
                column: "SepidarStatus");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_Type_Status_DocumentDate",
                table: "InvDocuments",
                columns: new[] { "Type", "Status", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_WarehouseId",
                table: "InvDocuments",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_WorkOrderId",
                table: "InvDocuments",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvItemGroups_Code",
                table: "InvItemGroups",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvItemGroups_ParentId",
                table: "InvItemGroups",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvItems_Code",
                table: "InvItems",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvItems_ItemGroupId",
                table: "InvItems",
                column: "ItemGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_InvItems_Title",
                table: "InvItems",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_InvLocations_WarehouseId_Code",
                table: "InvLocations",
                columns: new[] { "WarehouseId", "Code" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvLots_HeatNumber",
                table: "InvLots",
                column: "HeatNumber");

            migrationBuilder.CreateIndex(
                name: "IX_InvLots_ItemId_LotNumber",
                table: "InvLots",
                columns: new[] { "ItemId", "LotNumber" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvLots_ProviderId",
                table: "InvLots",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvNumberSequences_Type_Year",
                table: "InvNumberSequences",
                columns: new[] { "Type", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvOperationTemplates_ItemGroupId_Sequence",
                table: "InvOperationTemplates",
                columns: new[] { "ItemGroupId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_InvOperationTemplates_ItemId_Sequence",
                table: "InvOperationTemplates",
                columns: new[] { "ItemId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_InvOperationTemplates_MachineId",
                table: "InvOperationTemplates",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_InvPartOperationFiles_PartOperationId",
                table: "InvPartOperationFiles",
                column: "PartOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvPartOperations_LotId",
                table: "InvPartOperations",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvPartOperations_MachineId",
                table: "InvPartOperations",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_InvPartOperations_OperationTemplateId",
                table: "InvPartOperations",
                column: "OperationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_InvPartOperations_QcStatus_Status",
                table: "InvPartOperations",
                columns: new[] { "QcStatus", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InvPartOperations_SerialUnitId",
                table: "InvPartOperations",
                column: "SerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvPartOperations_WorkOrderId",
                table: "InvPartOperations",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvSepidarMappings_EntityType_EntityId",
                table: "InvSepidarMappings",
                columns: new[] { "EntityType", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvSerialUnits_ItemId_SerialNumber",
                table: "InvSerialUnits",
                columns: new[] { "ItemId", "SerialNumber" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvSerialUnits_LocationId",
                table: "InvSerialUnits",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvSerialUnits_LotId",
                table: "InvSerialUnits",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvSerialUnits_ParentSerialUnitId",
                table: "InvSerialUnits",
                column: "ParentSerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvSerialUnits_SerialNumber",
                table: "InvSerialUnits",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_InvSerialUnits_WarehouseId",
                table: "InvSerialUnits",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InvSerialUnits_WorkOrderId",
                table: "InvSerialUnits",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockBalances_ItemId_WarehouseId_LocationId_LotId",
                table: "InvStockBalances",
                columns: new[] { "ItemId", "WarehouseId", "LocationId", "LotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvStockBalances_LocationId",
                table: "InvStockBalances",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockBalances_LotId",
                table: "InvStockBalances",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockBalances_WarehouseId_ItemId",
                table: "InvStockBalances",
                columns: new[] { "WarehouseId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvStockTransactions_DocumentId",
                table: "InvStockTransactions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockTransactions_ItemId_WarehouseId_TransactionDate",
                table: "InvStockTransactions",
                columns: new[] { "ItemId", "WarehouseId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InvStockTransactions_LocationId",
                table: "InvStockTransactions",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockTransactions_LotId",
                table: "InvStockTransactions",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockTransactions_SerialUnitId",
                table: "InvStockTransactions",
                column: "SerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockTransactions_WarehouseId",
                table: "InvStockTransactions",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InvWarehouses_Code",
                table: "InvWarehouses",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvAssemblyLinks");

            migrationBuilder.DropTable(
                name: "InvDocumentLineSerials");

            migrationBuilder.DropTable(
                name: "InvNumberSequences");

            migrationBuilder.DropTable(
                name: "InvPartOperationFiles");

            migrationBuilder.DropTable(
                name: "InvSepidarMappings");

            migrationBuilder.DropTable(
                name: "InvStockBalances");

            migrationBuilder.DropTable(
                name: "InvStockTransactions");

            migrationBuilder.DropTable(
                name: "InvDocumentLines");

            migrationBuilder.DropTable(
                name: "InvPartOperations");

            migrationBuilder.DropTable(
                name: "InvDocuments");

            migrationBuilder.DropTable(
                name: "InvOperationTemplates");

            migrationBuilder.DropTable(
                name: "InvSerialUnits");

            migrationBuilder.DropTable(
                name: "InvLocations");

            migrationBuilder.DropTable(
                name: "InvLots");

            migrationBuilder.DropTable(
                name: "InvWarehouses");

            migrationBuilder.DropTable(
                name: "InvItems");

            migrationBuilder.DropTable(
                name: "InvItemGroups");

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 112);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 113);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 114);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 202);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 308);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 309);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 401);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 500);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 501);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 600);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 601);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 700);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 701);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 800);
        }
    }
}
