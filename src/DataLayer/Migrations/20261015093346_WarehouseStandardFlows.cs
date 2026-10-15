using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class WarehouseStandardFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceLotId",
                table: "InvSerialUnits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceSerialUnitId",
                table: "InvSerialUnits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "InvLots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCustomerProperty",
                table: "InvLots",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ParentLotId",
                table: "InvLots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseCode",
                table: "InvItems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupCode",
                table: "InvItems",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyCode",
                table: "InvItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MescCode",
                table: "InvItems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresIncomingInspection",
                table: "InvItems",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ShelfLifeDays",
                table: "InvItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StageCode",
                table: "InvItems",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                table: "InvDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepositReference",
                table: "InvDocuments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Destination",
                table: "InvDocuments",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverName",
                table: "InvDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExitCheckNote",
                table: "InvDocuments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExitCheckedAt",
                table: "InvDocuments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExitCheckedByUserId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InspectionId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaterialRequestId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MinutesNumber",
                table: "InvDocuments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NcrId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlanningApprovedAt",
                table: "InvDocuments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanningApprovedByUserId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QcLoadingApprovedAt",
                table: "InvDocuments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QcLoadingApprovedByUserId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiverName",
                table: "InvDocuments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockCountId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehiclePlate",
                table: "InvDocuments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WarehouseApprovedAt",
                table: "InvDocuments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseApprovedByUserId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WeighbridgeDocumentId",
                table: "InvDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Direction",
                table: "InvDocumentLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceRowNumber",
                table: "InvDocumentLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvCodingValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Scheme = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RangeFrom = table.Column<int>(type: "int", nullable: true),
                    RangeTo = table.Column<int>(type: "int", nullable: true),
                    IsStockable = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvCodingValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvItemRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    GroupCode = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ProposedCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TechnicalCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Material = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Standard = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MescCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TrackingMode = table.Column<int>(type: "int", nullable: false),
                    Specification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvItemRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvItemRequests_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvMaterialRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RequestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PlanningApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PlanningApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ManufacturingApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ManufacturingApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvMaterialRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvMaterialRequests_InvWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvMaterialRequests_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvNonConformances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SerialNumbers = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    InspectionId = table.Column<int>(type: "int", nullable: true),
                    ReceiptDocumentId = table.Column<int>(type: "int", nullable: true),
                    OwnedByContractor = table.Column<bool>(type: "bit", nullable: false),
                    Requirement = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CauseCategory = table.Column<int>(type: "int", nullable: false),
                    RootCause = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Disposition = table.Column<int>(type: "int", nullable: false),
                    DispositionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CommitteeMinutesNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CommitteeMembers = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    QualityApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    QualityApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    EngineeringApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EngineeringApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ManufacturingApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ManufacturingApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConcessionNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConcessionApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConcessionApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CustomerApprovalRequired = table.Column<bool>(type: "bit", nullable: false),
                    CustomerApprovalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TargetWarehouseId = table.Column<int>(type: "int", nullable: true),
                    ResultDocumentId = table.Column<int>(type: "int", nullable: true),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExecutedByUserId = table.Column<int>(type: "int", nullable: true),
                    CorrectiveAction = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CorrectiveActionOwner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CorrectiveActionDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectivenessCheck = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvNonConformances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvNonConformances_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvNonConformances_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvNonConformances_InvLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvNonConformances_InvWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvNonConformances_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvNonConformances_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvQcInspections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReceiptDocumentId = table.Column<int>(type: "int", nullable: false),
                    ReceiptLineId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SerialNumbers = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TargetWarehouseId = table.Column<int>(type: "int", nullable: true),
                    TargetLocationId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AcceptedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RejectedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RejectedSerialNumbers = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ReportNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReportDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InspectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InspectedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReleaseDocumentId = table.Column<int>(type: "int", nullable: true),
                    NcrId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvQcInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvQcInspections_InvDocuments_ReceiptDocumentId",
                        column: x => x.ReceiptDocumentId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvQcInspections_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvQcInspections_InvLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvQcInspections_InvWarehouses_TargetWarehouseId",
                        column: x => x.TargetWarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvQcInspections_InvWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvStockCounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CountDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    GroupCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SnapshotAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WarehouseApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WarehouseApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    PlanningApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PlanningApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedByUserId = table.Column<int>(type: "int", nullable: true),
                    IncreaseDocumentId = table.Column<int>(type: "int", nullable: true),
                    DecreaseDocumentId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvStockCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvStockCounts_InvWarehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "InvWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvMaterialRequestLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaterialRequestId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IssuedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvMaterialRequestLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvMaterialRequestLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvMaterialRequestLines_InvMaterialRequests_MaterialRequestId",
                        column: x => x.MaterialRequestId,
                        principalTable: "InvMaterialRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvStockCountLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockCountId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    SystemQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RecountedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CountedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvStockCountLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvStockCountLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvStockCountLines_InvLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "InvLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvStockCountLines_InvLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvStockCountLines_InvStockCounts_StockCountId",
                        column: x => x.StockCountId,
                        principalTable: "InvStockCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 402, "permission", "Warehouse", null, null, null, null, null, null, null, null, 8 },
                    { 801, "permission", "Warehouse", null, null, null, null, null, null, null, null, 7 }
                });

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "Description", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { 9, "00000009-0000-0000-0000-000000000000", null, null, null, null, "تولید", null, null, null, null, "CONSTRUCTION", "CONSTRUCTION" },
                    { 10, "00000010-0000-0000-0000-000000000000", null, null, null, null, "انتظامات (کنترل خروج کالا)", null, null, null, null, "SECURITY", "SECURITY" }
                });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 900, "permission", "Warehouse", null, null, null, null, null, null, null, null, 9 },
                    { 901, "permission", "Traceability", null, null, null, null, null, null, null, null, 9 },
                    { 1000, "permission", "Warehouse", null, null, null, null, null, null, null, null, 10 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvSerialUnits_SourceLotId",
                table: "InvSerialUnits",
                column: "SourceLotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvSerialUnits_SourceSerialUnitId",
                table: "InvSerialUnits",
                column: "SourceSerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvLots_ParentLotId",
                table: "InvLots",
                column: "ParentLotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvItems_BaseCode",
                table: "InvItems",
                column: "BaseCode");

            migrationBuilder.CreateIndex(
                name: "IX_InvItems_LegacyCode",
                table: "InvItems",
                column: "LegacyCode");

            migrationBuilder.CreateIndex(
                name: "IX_InvItems_MescCode",
                table: "InvItems",
                column: "MescCode");

            migrationBuilder.CreateIndex(
                name: "IX_InvItems_TechnicalCode",
                table: "InvItems",
                column: "TechnicalCode");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_MaterialRequestId",
                table: "InvDocuments",
                column: "MaterialRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_NcrId",
                table: "InvDocuments",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_InvCodingValues_Scheme_Model_Code",
                table: "InvCodingValues",
                columns: new[] { "Scheme", "Model", "Code" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvItemRequests_ItemId",
                table: "InvItemRequests",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvItemRequests_Number",
                table: "InvItemRequests",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvItemRequests_Status",
                table: "InvItemRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InvMaterialRequestLines_ItemId",
                table: "InvMaterialRequestLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvMaterialRequestLines_MaterialRequestId",
                table: "InvMaterialRequestLines",
                column: "MaterialRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_InvMaterialRequests_Number",
                table: "InvMaterialRequests",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvMaterialRequests_Status_RequestDate",
                table: "InvMaterialRequests",
                columns: new[] { "Status", "RequestDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InvMaterialRequests_WarehouseId",
                table: "InvMaterialRequests",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InvMaterialRequests_WorkOrderId",
                table: "InvMaterialRequests",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvNonConformances_ContractId",
                table: "InvNonConformances",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_InvNonConformances_ItemId",
                table: "InvNonConformances",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvNonConformances_LotId",
                table: "InvNonConformances",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvNonConformances_Number",
                table: "InvNonConformances",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvNonConformances_ProviderId",
                table: "InvNonConformances",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvNonConformances_Status_ReportDate",
                table: "InvNonConformances",
                columns: new[] { "Status", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InvNonConformances_WarehouseId",
                table: "InvNonConformances",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InvNonConformances_WorkOrderId",
                table: "InvNonConformances",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvQcInspections_ItemId",
                table: "InvQcInspections",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvQcInspections_LotId",
                table: "InvQcInspections",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvQcInspections_Number",
                table: "InvQcInspections",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvQcInspections_ReceiptDocumentId",
                table: "InvQcInspections",
                column: "ReceiptDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvQcInspections_Status_WarehouseId",
                table: "InvQcInspections",
                columns: new[] { "Status", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvQcInspections_TargetWarehouseId",
                table: "InvQcInspections",
                column: "TargetWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InvQcInspections_WarehouseId",
                table: "InvQcInspections",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockCountLines_ItemId",
                table: "InvStockCountLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockCountLines_LocationId",
                table: "InvStockCountLines",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockCountLines_LotId",
                table: "InvStockCountLines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockCountLines_StockCountId",
                table: "InvStockCountLines",
                column: "StockCountId");

            migrationBuilder.CreateIndex(
                name: "IX_InvStockCounts_Number",
                table: "InvStockCounts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvStockCounts_WarehouseId",
                table: "InvStockCounts",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvDocuments_InvMaterialRequests_MaterialRequestId",
                table: "InvDocuments",
                column: "MaterialRequestId",
                principalTable: "InvMaterialRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvDocuments_InvNonConformances_NcrId",
                table: "InvDocuments",
                column: "NcrId",
                principalTable: "InvNonConformances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvLots_InvLots_ParentLotId",
                table: "InvLots",
                column: "ParentLotId",
                principalTable: "InvLots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvSerialUnits_InvLots_SourceLotId",
                table: "InvSerialUnits",
                column: "SourceLotId",
                principalTable: "InvLots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvSerialUnits_InvSerialUnits_SourceSerialUnitId",
                table: "InvSerialUnits",
                column: "SourceSerialUnitId",
                principalTable: "InvSerialUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvDocuments_InvMaterialRequests_MaterialRequestId",
                table: "InvDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_InvDocuments_InvNonConformances_NcrId",
                table: "InvDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_InvLots_InvLots_ParentLotId",
                table: "InvLots");

            migrationBuilder.DropForeignKey(
                name: "FK_InvSerialUnits_InvLots_SourceLotId",
                table: "InvSerialUnits");

            migrationBuilder.DropForeignKey(
                name: "FK_InvSerialUnits_InvSerialUnits_SourceSerialUnitId",
                table: "InvSerialUnits");

            migrationBuilder.DropTable(
                name: "InvCodingValues");

            migrationBuilder.DropTable(
                name: "InvItemRequests");

            migrationBuilder.DropTable(
                name: "InvMaterialRequestLines");

            migrationBuilder.DropTable(
                name: "InvNonConformances");

            migrationBuilder.DropTable(
                name: "InvQcInspections");

            migrationBuilder.DropTable(
                name: "InvStockCountLines");

            migrationBuilder.DropTable(
                name: "InvMaterialRequests");

            migrationBuilder.DropTable(
                name: "InvStockCounts");

            migrationBuilder.DropIndex(
                name: "IX_InvSerialUnits_SourceLotId",
                table: "InvSerialUnits");

            migrationBuilder.DropIndex(
                name: "IX_InvSerialUnits_SourceSerialUnitId",
                table: "InvSerialUnits");

            migrationBuilder.DropIndex(
                name: "IX_InvLots_ParentLotId",
                table: "InvLots");

            migrationBuilder.DropIndex(
                name: "IX_InvItems_BaseCode",
                table: "InvItems");

            migrationBuilder.DropIndex(
                name: "IX_InvItems_LegacyCode",
                table: "InvItems");

            migrationBuilder.DropIndex(
                name: "IX_InvItems_MescCode",
                table: "InvItems");

            migrationBuilder.DropIndex(
                name: "IX_InvItems_TechnicalCode",
                table: "InvItems");

            migrationBuilder.DropIndex(
                name: "IX_InvDocuments_MaterialRequestId",
                table: "InvDocuments");

            migrationBuilder.DropIndex(
                name: "IX_InvDocuments_NcrId",
                table: "InvDocuments");

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 402);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 801);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 900);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 901);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1000);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DropColumn(
                name: "SourceLotId",
                table: "InvSerialUnits");

            migrationBuilder.DropColumn(
                name: "SourceSerialUnitId",
                table: "InvSerialUnits");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "InvLots");

            migrationBuilder.DropColumn(
                name: "IsCustomerProperty",
                table: "InvLots");

            migrationBuilder.DropColumn(
                name: "ParentLotId",
                table: "InvLots");

            migrationBuilder.DropColumn(
                name: "BaseCode",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "GroupCode",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "LegacyCode",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "MescCode",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "RequiresIncomingInspection",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "ShelfLifeDays",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "StageCode",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "BuyerName",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "DepositReference",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "Destination",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "DriverName",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "ExitCheckNote",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "ExitCheckedAt",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "ExitCheckedByUserId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "InspectionId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "MaterialRequestId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "MinutesNumber",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "NcrId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "PlanningApprovedAt",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "PlanningApprovedByUserId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "QcLoadingApprovedAt",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "QcLoadingApprovedByUserId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "ReceiverName",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "StockCountId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "VehiclePlate",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "WarehouseApprovedAt",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "WarehouseApprovedByUserId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "WeighbridgeDocumentId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "InvDocumentLines");

            migrationBuilder.DropColumn(
                name: "SourceRowNumber",
                table: "InvDocumentLines");
        }
    }
}
