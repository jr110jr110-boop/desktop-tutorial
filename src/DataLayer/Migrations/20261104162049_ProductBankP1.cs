using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ProductBankP1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PartItemId",
                table: "InvItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartNumber",
                table: "InvItems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StageCode2",
                table: "InvItems",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PbLegacyMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LegacyTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LegacyMaterial = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LegacyItemId = table.Column<int>(type: "int", nullable: true),
                    PartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StageCode2 = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    PartTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProposedByUserId = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbLegacyMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbLegacyMappings_InvItems_LegacyItemId",
                        column: x => x.LegacyItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PbSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PbStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    TitleFa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IsStockable = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbStages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PbBomHeaders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    AssemblyPartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProductItemId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IssueFor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChangeReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SupersedesId = table.Column<int>(type: "int", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CheckedByUserId = table.Column<int>(type: "int", nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    QcCheckedByUserId = table.Column<int>(type: "int", nullable: true),
                    QcCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ManufacturingCheckedByUserId = table.Column<int>(type: "int", nullable: true),
                    ManufacturingCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ImportedSigned = table.Column<bool>(type: "bit", nullable: false),
                    PaperSignatureNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReturnNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbBomHeaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbBomHeaders_InvItems_ProductItemId",
                        column: x => x.ProductItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbBomHeaders_PbBomHeaders_SupersedesId",
                        column: x => x.SupersedesId,
                        principalTable: "PbBomHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbBomHeaders_PbProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PbProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbOpcRoutings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    PartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProjectNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DocumentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChangeReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SupersedesId = table.Column<int>(type: "int", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    CheckedByUserId = table.Column<int>(type: "int", nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ImportedSigned = table.Column<bool>(type: "bit", nullable: false),
                    PublishedToMesAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbOpcRoutings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbOpcRoutings_PbOpcRoutings_SupersedesId",
                        column: x => x.SupersedesId,
                        principalTable: "PbOpcRoutings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbOpcRoutings_PbProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PbProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbWbsTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TotalDurationDays = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    ImportedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SecondApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    SecondApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbWbsTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbWbsTemplates_PbProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PbProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbBomLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BomHeaderId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ItemNumber = table.Column<int>(type: "int", nullable: true),
                    IsHeading = table.Column<bool>(type: "bit", nullable: false),
                    ParentLineId = table.Column<int>(type: "int", nullable: true),
                    PartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    PartName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Standard = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Material = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RawDiameter = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    RawLength = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    RawWidth = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    RawThickness = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    NetWeight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    GrossWeight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ScrapFactor = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    MakeOrBuy = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChildBomId = table.Column<int>(type: "int", nullable: true),
                    AlternativeOfLineId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbBomLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbBomLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbBomLines_PbBomHeaders_BomHeaderId",
                        column: x => x.BomHeaderId,
                        principalTable: "PbBomHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PbBomLines_PbBomHeaders_ChildBomId",
                        column: x => x.ChildBomId,
                        principalTable: "PbBomHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbBomLines_PbBomLines_AlternativeOfLineId",
                        column: x => x.AlternativeOfLineId,
                        principalTable: "PbBomLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbBomLines_PbBomLines_ParentLineId",
                        column: x => x.ParentLineId,
                        principalTable: "PbBomLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbOpcSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoutingId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    PartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PartName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    StageCode2 = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    WorkCenterId = table.Column<int>(type: "int", nullable: true),
                    ResourceClass = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StandardHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    IsQcPoint = table.Column<bool>(type: "bit", nullable: false),
                    RequiredEvidence = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsOutside = table.Column<bool>(type: "bit", nullable: false),
                    MakeOrBuy = table.Column<int>(type: "int", nullable: true),
                    StageItemId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbOpcSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbOpcSteps_InvItems_StageItemId",
                        column: x => x.StageItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbOpcSteps_MfWorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "MfWorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbOpcSteps_PbOpcRoutings_RoutingId",
                        column: x => x.RoutingId,
                        principalTable: "PbOpcRoutings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbProjectBaselines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    BomHeaderId = table.Column<int>(type: "int", nullable: false),
                    SourceBomCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    SourceBomRevision = table.Column<int>(type: "int", nullable: false),
                    WbsTemplateId = table.Column<int>(type: "int", nullable: true),
                    WbsTemplateVersion = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    CopiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    PcProductNodeId = table.Column<int>(type: "int", nullable: true),
                    ChangeRequestId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbProjectBaselines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbProjectBaselines_PbBomHeaders_BomHeaderId",
                        column: x => x.BomHeaderId,
                        principalTable: "PbBomHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbProjectBaselines_PbProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PbProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbProjectBaselines_PbWbsTemplates_WbsTemplateId",
                        column: x => x.WbsTemplateId,
                        principalTable: "PbWbsTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbProjectBaselines_PcChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "PcChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbProjectBaselines_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbWbsTemplateNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Uid = table.Column<int>(type: "int", nullable: false),
                    OutlineLevel = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PhaseCode = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: true),
                    DurationDays = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    StartOffsetDays = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: true),
                    PartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MakeOrBuy = table.Column<int>(type: "int", nullable: true),
                    ResourceClass = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbWbsTemplateNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbWbsTemplateNodes_PbWbsTemplateNodes_ParentId",
                        column: x => x.ParentId,
                        principalTable: "PbWbsTemplateNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbWbsTemplateNodes_PbWbsTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "PbWbsTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbProjectBomLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaselineId = table.Column<int>(type: "int", nullable: false),
                    SourceLineId = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ItemNumber = table.Column<int>(type: "int", nullable: true),
                    IsHeading = table.Column<bool>(type: "bit", nullable: false),
                    AssemblyPartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    PartName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Material = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MakeOrBuy = table.Column<int>(type: "int", nullable: false),
                    IsRemoved = table.Column<bool>(type: "bit", nullable: false),
                    ChangedByChangeId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbProjectBomLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbProjectBomLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbProjectBomLines_PbProjectBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "PbProjectBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbProjectRoutings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaselineId = table.Column<int>(type: "int", nullable: false),
                    OpcRoutingId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    PartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbProjectRoutings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbProjectRoutings_PbOpcRoutings_OpcRoutingId",
                        column: x => x.OpcRoutingId,
                        principalTable: "PbOpcRoutings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbProjectRoutings_PbProjectBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "PbProjectBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbWbsTemplateLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    FromNodeId = table.Column<int>(type: "int", nullable: false),
                    ToNodeId = table.Column<int>(type: "int", nullable: false),
                    LinkType = table.Column<int>(type: "int", nullable: false),
                    LagDays = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbWbsTemplateLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbWbsTemplateLinks_PbWbsTemplateNodes_FromNodeId",
                        column: x => x.FromNodeId,
                        principalTable: "PbWbsTemplateNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbWbsTemplateLinks_PbWbsTemplateNodes_ToNodeId",
                        column: x => x.ToNodeId,
                        principalTable: "PbWbsTemplateNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbWbsTemplateLinks_PbWbsTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "PbWbsTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbProjectBomChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaselineId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    TargetLineId = table.Column<int>(type: "int", nullable: true),
                    PartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    PartName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NewQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Material = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MakeOrBuy = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EngineeringApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    EngineeringApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PlanningApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    PlanningApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangeRequestId = table.Column<int>(type: "int", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AppliedByUserId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbProjectBomChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbProjectBomChanges_PbProjectBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "PbProjectBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbProjectBomChanges_PbProjectBomLines_TargetLineId",
                        column: x => x.TargetLineId,
                        principalTable: "PbProjectBomLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbProjectBomChanges_PcChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "PcChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvItems_PartItemId",
                table: "InvItems",
                column: "PartItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvItems_PartNumber_StageCode2",
                table: "InvItems",
                columns: new[] { "PartNumber", "StageCode2" },
                unique: true,
                filter: "[Deleted] = 0 AND [PartNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PbBomHeaders_Code_Revision",
                table: "PbBomHeaders",
                columns: new[] { "Code", "Revision" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbBomHeaders_ProductId",
                table: "PbBomHeaders",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PbBomHeaders_ProductItemId",
                table: "PbBomHeaders",
                column: "ProductItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PbBomHeaders_Status",
                table: "PbBomHeaders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PbBomHeaders_SupersedesId",
                table: "PbBomHeaders",
                column: "SupersedesId");

            migrationBuilder.CreateIndex(
                name: "IX_PbBomLines_AlternativeOfLineId",
                table: "PbBomLines",
                column: "AlternativeOfLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PbBomLines_BomHeaderId",
                table: "PbBomLines",
                column: "BomHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PbBomLines_ChildBomId",
                table: "PbBomLines",
                column: "ChildBomId");

            migrationBuilder.CreateIndex(
                name: "IX_PbBomLines_ItemId",
                table: "PbBomLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PbBomLines_ParentLineId",
                table: "PbBomLines",
                column: "ParentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PbBomLines_PartNumber",
                table: "PbBomLines",
                column: "PartNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PbLegacyMappings_LegacyCode",
                table: "PbLegacyMappings",
                column: "LegacyCode",
                unique: true,
                filter: "[Deleted] = 0 AND [Status] <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_PbLegacyMappings_LegacyItemId",
                table: "PbLegacyMappings",
                column: "LegacyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PbLegacyMappings_PartNumber",
                table: "PbLegacyMappings",
                column: "PartNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PbLegacyMappings_Status",
                table: "PbLegacyMappings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PbOpcRoutings_PartNumber_Revision",
                table: "PbOpcRoutings",
                columns: new[] { "PartNumber", "Revision" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbOpcRoutings_ProductId",
                table: "PbOpcRoutings",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PbOpcRoutings_SupersedesId",
                table: "PbOpcRoutings",
                column: "SupersedesId");

            migrationBuilder.CreateIndex(
                name: "IX_PbOpcSteps_RoutingId",
                table: "PbOpcSteps",
                column: "RoutingId");

            migrationBuilder.CreateIndex(
                name: "IX_PbOpcSteps_StageItemId",
                table: "PbOpcSteps",
                column: "StageItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PbOpcSteps_WorkCenterId",
                table: "PbOpcSteps",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProducts_Code",
                table: "PbProducts",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbProducts_EquipmentId",
                table: "PbProducts",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBaselines_BomHeaderId",
                table: "PbProjectBaselines",
                column: "BomHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBaselines_ChangeRequestId",
                table: "PbProjectBaselines",
                column: "ChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBaselines_ProductId",
                table: "PbProjectBaselines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBaselines_WbsTemplateId",
                table: "PbProjectBaselines",
                column: "WbsTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBaselines_WorkOrderId_Current",
                table: "PbProjectBaselines",
                column: "WorkOrderId",
                unique: true,
                filter: "[Deleted] = 0 AND [IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBomChanges_BaselineId",
                table: "PbProjectBomChanges",
                column: "BaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBomChanges_ChangeRequestId",
                table: "PbProjectBomChanges",
                column: "ChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBomChanges_Number",
                table: "PbProjectBomChanges",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBomChanges_TargetLineId",
                table: "PbProjectBomChanges",
                column: "TargetLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBomLines_BaselineId",
                table: "PbProjectBomLines",
                column: "BaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBomLines_ItemId",
                table: "PbProjectBomLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectBomLines_PartNumber",
                table: "PbProjectBomLines",
                column: "PartNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectRoutings_BaselineId",
                table: "PbProjectRoutings",
                column: "BaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_PbProjectRoutings_OpcRoutingId",
                table: "PbProjectRoutings",
                column: "OpcRoutingId");

            migrationBuilder.CreateIndex(
                name: "IX_PbSettings_Key",
                table: "PbSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PbStages_Code",
                table: "PbStages",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbWbsTemplateLinks_FromNodeId",
                table: "PbWbsTemplateLinks",
                column: "FromNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PbWbsTemplateLinks_TemplateId",
                table: "PbWbsTemplateLinks",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PbWbsTemplateLinks_ToNodeId",
                table: "PbWbsTemplateLinks",
                column: "ToNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PbWbsTemplateNodes_ParentId",
                table: "PbWbsTemplateNodes",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_PbWbsTemplateNodes_TemplateId",
                table: "PbWbsTemplateNodes",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PbWbsTemplates_ProductId_Version",
                table: "PbWbsTemplates",
                columns: new[] { "ProductId", "Version" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_InvItems_InvItems_PartItemId",
                table: "InvItems",
                column: "PartItemId",
                principalTable: "InvItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvItems_InvItems_PartItemId",
                table: "InvItems");

            migrationBuilder.DropTable(
                name: "PbBomLines");

            migrationBuilder.DropTable(
                name: "PbLegacyMappings");

            migrationBuilder.DropTable(
                name: "PbOpcSteps");

            migrationBuilder.DropTable(
                name: "PbProjectBomChanges");

            migrationBuilder.DropTable(
                name: "PbProjectRoutings");

            migrationBuilder.DropTable(
                name: "PbSettings");

            migrationBuilder.DropTable(
                name: "PbStages");

            migrationBuilder.DropTable(
                name: "PbWbsTemplateLinks");

            migrationBuilder.DropTable(
                name: "PbProjectBomLines");

            migrationBuilder.DropTable(
                name: "PbOpcRoutings");

            migrationBuilder.DropTable(
                name: "PbWbsTemplateNodes");

            migrationBuilder.DropTable(
                name: "PbProjectBaselines");

            migrationBuilder.DropTable(
                name: "PbBomHeaders");

            migrationBuilder.DropTable(
                name: "PbWbsTemplates");

            migrationBuilder.DropTable(
                name: "PbProducts");

            migrationBuilder.DropIndex(
                name: "IX_InvItems_PartItemId",
                table: "InvItems");

            migrationBuilder.DropIndex(
                name: "IX_InvItems_PartNumber_StageCode2",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "PartItemId",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "PartNumber",
                table: "InvItems");

            migrationBuilder.DropColumn(
                name: "StageCode2",
                table: "InvItems");
        }
    }
}
