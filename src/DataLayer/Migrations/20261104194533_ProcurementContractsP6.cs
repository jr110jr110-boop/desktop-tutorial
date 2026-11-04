using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ProcurementContractsP6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "ProcPurchaseRequests",
                type: "int",
                nullable: false,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "FormType",
                table: "ProcPurchaseRequests",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PreviousRequestId",
                table: "ProcPurchaseRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelatedOrderId",
                table: "ProcPurchaseRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "ProcPurchaseRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnedAt",
                table: "ProcPurchaseRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnedByUserId",
                table: "ProcPurchaseRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "ProcPurchaseRequests",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SupersededByRequestId",
                table: "ProcPurchaseRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VehicleDate",
                table: "ProcPurchaseRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleDestination",
                table: "ProcPurchaseRequests",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleOrigin",
                table: "ProcPurchaseRequests",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleType",
                table: "ProcPurchaseRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleUnit",
                table: "ProcPurchaseRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BudgetOverrideAmount",
                table: "ProcInquiries",
                type: "decimal(22,2)",
                precision: 22,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BudgetOverrideAt",
                table: "ProcInquiries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BudgetOverrideByUserId",
                table: "ProcInquiries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BudgetOverrideNote",
                table: "ProcInquiries",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlanningApprovedAt",
                table: "ProcInquiries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanningApprovedByUserId",
                table: "ProcInquiries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlanningBudgetAmount",
                table: "ProcInquiries",
                type: "decimal(22,2)",
                precision: 22,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlanningDeliveryDays",
                table: "ProcInquiries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanningNote",
                table: "ProcInquiries",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousInquiryId",
                table: "ProcInquiries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "ProcInquiries",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnedAt",
                table: "ProcInquiries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnedByUserId",
                table: "ProcInquiries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "ProcInquiries",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SupersededByInquiryId",
                table: "ProcInquiries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CtCbsItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    WbsNodeId = table.Column<int>(type: "int", nullable: true),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtCbsItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CtCbsItems_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CtCbsItems_ProcPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "ProcPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CtDealRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FormKind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    FundingSource = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OrganisationalUnit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DealLevel = table.Column<int>(type: "int", nullable: false),
                    ContractTypeTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SelectionMethodTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EstimatedAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    PartyName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    EmployerRank = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmployerField = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Round = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtDealRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CtDealRequests_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CtHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnPettyCashRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PettyCashId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ControlledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ControlledByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ToppedUpAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ToppedUpByUserId = table.Column<int>(type: "int", nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_FnPettyCashRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPettyCashRequests_FnPettyCashes_PettyCashId",
                        column: x => x.PettyCashId,
                        principalTable: "FnPettyCashes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnPettyCashSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PettyCashId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttachmentsCount = table.Column<int>(type: "int", nullable: false),
                    PreviousBalance = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Charge = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    TotalInWords = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ControlledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ControlledByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    FinalAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinalByUserId = table.Column<int>(type: "int", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SettlementId = table.Column<int>(type: "int", nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPettyCashSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPettyCashSummaries_FnPettyCashes_PettyCashId",
                        column: x => x.PettyCashId,
                        principalTable: "FnPettyCashes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcAddenda",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AmountBefore = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    AmountAfter = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    DeliveryDateBefore = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryDateAfter = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxVolumeChangePercent = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    NeedsCeo = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OrderApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OrderApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CeoApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CeoApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_ProcAddenda", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcAddenda_ProcPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "ProcPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcArchive",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ArchivedByUserId = table.Column<int>(type: "int", nullable: false),
                    ArchiveReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RestoredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RestoredByUserId = table.Column<int>(type: "int", nullable: true),
                    RestoreReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Times = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcArchive", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcBuyOrderBridges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyOrderId = table.Column<int>(type: "int", nullable: false),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcBuyOrderBridges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcBuyOrderBridges_ProcPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "ProcPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcBuyOrderBridges_ProcPurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "ProcPurchaseRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcGuarantees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    GuaranteeNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Issuer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReleaseConditions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReleaseRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleaseRequestedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReleaseReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    LastAlertAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcGuarantees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcGuarantees_ProcPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "ProcPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcGuarantees_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcPaymentTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    DueDays = table.Column<int>(type: "int", nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PaymentRequestId = table.Column<int>(type: "int", nullable: true),
                    ConditionConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConditionConfirmedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcPaymentTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcPaymentTerms_ProcPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "ProcPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcRequestRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StatusWhenFrozen = table.Column<int>(type: "int", nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Hash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FrozenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FrozenByUserId = table.Column<int>(type: "int", nullable: false),
                    PlanningApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PlanningApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ManagementApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ManagementApprovedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcRequestRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcRequestRevisions_ProcPurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "ProcPurchaseRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PsCashPlan",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PaymentTermId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PsCashPlan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PsLegalRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    DeductionKind = table.Column<int>(type: "int", nullable: false),
                    BasisNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifiedByUserId = table.Column<int>(type: "int", nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PsLegalRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PsStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    ProvisionalStatementId = table.Column<int>(type: "int", nullable: true),
                    Serial = table.Column<int>(type: "int", nullable: false),
                    PeriodFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SendingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SupportingDocuments = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CounterpartyStatus = table.Column<int>(type: "int", nullable: false),
                    CounterpartyComments = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ContractAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    AddendaAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    PreviousPayments = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    NonconformityDeductions = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    LegalDeductions = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    GrossThisPeriod = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    PayableThisPeriod = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: true),
                    TechnicalApprovedAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    TechnicalApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TechnicalApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    SupervisionApprovedAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    SupervisionApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SupervisionApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    PaymentAuthorisedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaymentAuthorisedByUserId = table.Column<int>(type: "int", nullable: true),
                    PaymentRequestId = table.Column<int>(type: "int", nullable: true),
                    PaidDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PayMethod = table.Column<int>(type: "int", nullable: true),
                    ChequeNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    ReceivedAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    PaymentVariance = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    PaymentRecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaymentRecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReturnedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PsStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PsStatements_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PsStatements_ProcPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "ProcPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PsStatements_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PsStatements_PsStatements_ProvisionalStatementId",
                        column: x => x.ProvisionalStatementId,
                        principalTable: "PsStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CtDealSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DealRequestId = table.Column<int>(type: "int", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DesignatedUserId = table.Column<int>(type: "int", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtDealSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CtDealSteps_CtDealRequests_DealRequestId",
                        column: x => x.DealRequestId,
                        principalTable: "CtDealRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CtTenderRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DealRequestId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    GuaranteeType = table.Column<int>(type: "int", nullable: false),
                    GuaranteeAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    CallDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OpeningDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    WinnerParticipantId = table.Column<int>(type: "int", nullable: true),
                    WinnerReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    WinnerSelectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WinnerSelectedByUserId = table.Column<int>(type: "int", nullable: true),
                    WinnerConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WinnerConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtTenderRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CtTenderRuns_CtDealRequests_DealRequestId",
                        column: x => x.DealRequestId,
                        principalTable: "CtDealRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnPettyCashSummaryLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SummaryId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PrApprovalNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WarehouseReceiptNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ProjectCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    DocType = table.Column<int>(type: "int", nullable: false),
                    ExpenseKind = table.Column<int>(type: "int", nullable: false),
                    GoodsClass = table.Column<int>(type: "int", nullable: false),
                    PayKind = table.Column<int>(type: "int", nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SettlementLineId = table.Column<int>(type: "int", nullable: true),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: true),
                    ReceiptDocumentId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPettyCashSummaryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPettyCashSummaryLines_FnPettyCashSettlementLines_SettlementLineId",
                        column: x => x.SettlementLineId,
                        principalTable: "FnPettyCashSettlementLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnPettyCashSummaryLines_FnPettyCashSummaries_SummaryId",
                        column: x => x.SummaryId,
                        principalTable: "FnPettyCashSummaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FnPettyCashSummaryLines_InvDocuments_ReceiptDocumentId",
                        column: x => x.ReceiptDocumentId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnPettyCashSummaryLines_ProcPurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "ProcPurchaseRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcAddendumLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AddendumId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    QuantityBefore = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityAfter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPriceBefore = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: false),
                    UnitPriceAfter = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcAddendumLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcAddendumLines_ProcAddenda_AddendumId",
                        column: x => x.AddendumId,
                        principalTable: "ProcAddenda",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcAddendumLines_ProcPurchaseOrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "ProcPurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PsDeductions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatementId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ItemTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    Basis = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DeviationType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LetterNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NcrId = table.Column<int>(type: "int", nullable: true),
                    LegalRateId = table.Column<int>(type: "int", nullable: true),
                    Percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    BaseAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PsDeductions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PsDeductions_InvNonConformances_NcrId",
                        column: x => x.NcrId,
                        principalTable: "InvNonConformances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PsDeductions_PsLegalRates_LegalRateId",
                        column: x => x.LegalRateId,
                        principalTable: "PsLegalRates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PsDeductions_PsStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "PsStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PsPreviousPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatementId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    PaymentDocNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MethodTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaymentTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatementSerial = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsAuto = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PsPreviousPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PsPreviousPayments_PsStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "PsStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PsStatementLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatementId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SerialOrDrawing = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProjectCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EquipmentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    AdvanceAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    AfterDeliveryQcAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    GoodPerformanceAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    AfterFinalTestsAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ThisPeriodAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    IsAddendum = table.Column<bool>(type: "bit", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: true),
                    CbsItemId = table.Column<int>(type: "int", nullable: true),
                    ProgressPercent = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PsStatementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PsStatementLines_CtCbsItems_CbsItemId",
                        column: x => x.CbsItemId,
                        principalTable: "CtCbsItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PsStatementLines_ProcPurchaseOrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "ProcPurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PsStatementLines_PsStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "PsStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CtContractDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DealRequestId = table.Column<int>(type: "int", nullable: true),
                    TenderRunId = table.Column<int>(type: "int", nullable: true),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    CounterpartyName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ContractKindTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TemplateCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ContractAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationDays = table.Column<int>(type: "int", nullable: true),
                    AdvancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    RetentionPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    PerformanceGuaranteePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    PaymentTerms = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SpecialConditions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ContractNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SignedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SignedByUserId = table.Column<int>(type: "int", nullable: true),
                    AgreedByUserId = table.Column<int>(type: "int", nullable: true),
                    ExchangeRound = table.Column<int>(type: "int", nullable: false),
                    CounterpartyComments = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtContractDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CtContractDrafts_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CtContractDrafts_CtDealRequests_DealRequestId",
                        column: x => x.DealRequestId,
                        principalTable: "CtDealRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CtContractDrafts_CtTenderRuns_TenderRunId",
                        column: x => x.TenderRunId,
                        principalTable: "CtTenderRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CtContractDrafts_ProcPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "ProcPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CtContractDrafts_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CtTenderParticipants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderRunId = table.Column<int>(type: "int", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OfferAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    TechnicalScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Disqualified = table.Column<bool>(type: "bit", nullable: false),
                    DisqualifyReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtTenderParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CtTenderParticipants_CtTenderRuns_TenderRunId",
                        column: x => x.TenderRunId,
                        principalTable: "CtTenderRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CtTenderParticipants_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnPettyCashMiscForms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LineId = table.Column<int>(type: "int", nullable: false),
                    RequestingUnit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProjectCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WhyNoInvoice = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    UnitManagerUserId = table.Column<int>(type: "int", nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPettyCashMiscForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPettyCashMiscForms_FnPettyCashSummaryLines_LineId",
                        column: x => x.LineId,
                        principalTable: "FnPettyCashSummaryLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnPettyCashVehicleForms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LineId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VehicleType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Origin = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Destination = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPettyCashVehicleForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPettyCashVehicleForms_FnPettyCashSummaryLines_LineId",
                        column: x => x.LineId,
                        principalTable: "FnPettyCashSummaryLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequests_FormType",
                table: "ProcPurchaseRequests",
                column: "FormType");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequests_PreviousRequestId",
                table: "ProcPurchaseRequests",
                column: "PreviousRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequests_RelatedOrderId",
                table: "ProcPurchaseRequests",
                column: "RelatedOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequests_SupersededByRequestId",
                table: "ProcPurchaseRequests",
                column: "SupersededByRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcInquiries_PreviousInquiryId",
                table: "ProcInquiries",
                column: "PreviousInquiryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcInquiries_SupersededByInquiryId",
                table: "ProcInquiries",
                column: "SupersededByInquiryId");

            migrationBuilder.CreateIndex(
                name: "IX_CtCbsItems_ContractId_Code",
                table: "CtCbsItems",
                columns: new[] { "ContractId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_CtCbsItems_PurchaseOrderId_Code",
                table: "CtCbsItems",
                columns: new[] { "PurchaseOrderId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_CtContractDrafts_ContractId",
                table: "CtContractDrafts",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_CtContractDrafts_ContractNumber",
                table: "CtContractDrafts",
                column: "ContractNumber",
                unique: true,
                filter: "[Deleted] = 0 AND [ContractNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CtContractDrafts_DealRequestId",
                table: "CtContractDrafts",
                column: "DealRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CtContractDrafts_Number",
                table: "CtContractDrafts",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CtContractDrafts_ProviderId",
                table: "CtContractDrafts",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_CtContractDrafts_PurchaseOrderId",
                table: "CtContractDrafts",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CtContractDrafts_TenderRunId",
                table: "CtContractDrafts",
                column: "TenderRunId");

            migrationBuilder.CreateIndex(
                name: "IX_CtDealRequests_ContractId",
                table: "CtDealRequests",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_CtDealRequests_Number",
                table: "CtDealRequests",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CtDealRequests_Status",
                table: "CtDealRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CtDealSteps_DealRequestId",
                table: "CtDealSteps",
                column: "DealRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CtHistory_OwnerType_OwnerId",
                table: "CtHistory",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CtTenderParticipants_ProviderId",
                table: "CtTenderParticipants",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_CtTenderParticipants_TenderRunId",
                table: "CtTenderParticipants",
                column: "TenderRunId");

            migrationBuilder.CreateIndex(
                name: "IX_CtTenderRuns_DealRequestId",
                table: "CtTenderRuns",
                column: "DealRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CtTenderRuns_Number",
                table: "CtTenderRuns",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashMiscForms_LineId",
                table: "FnPettyCashMiscForms",
                column: "LineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashRequests_Number",
                table: "FnPettyCashRequests",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashRequests_PettyCashId",
                table: "FnPettyCashRequests",
                column: "PettyCashId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashSummaries_Number",
                table: "FnPettyCashSummaries",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashSummaries_PettyCashId_PeriodFrom",
                table: "FnPettyCashSummaries",
                columns: new[] { "PettyCashId", "PeriodFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashSummaryLines_PurchaseRequestId",
                table: "FnPettyCashSummaryLines",
                column: "PurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashSummaryLines_ReceiptDocumentId",
                table: "FnPettyCashSummaryLines",
                column: "ReceiptDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashSummaryLines_SettlementLineId",
                table: "FnPettyCashSummaryLines",
                column: "SettlementLineId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashSummaryLines_SummaryId",
                table: "FnPettyCashSummaryLines",
                column: "SummaryId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashVehicleForms_LineId",
                table: "FnPettyCashVehicleForms",
                column: "LineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcAddenda_Number",
                table: "ProcAddenda",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProcAddenda_PurchaseOrderId_Sequence",
                table: "ProcAddenda",
                columns: new[] { "PurchaseOrderId", "Sequence" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProcAddendumLines_AddendumId",
                table: "ProcAddendumLines",
                column: "AddendumId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcAddendumLines_OrderLineId",
                table: "ProcAddendumLines",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcArchive_OwnerType_OwnerId",
                table: "ProcArchive",
                columns: new[] { "OwnerType", "OwnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcBuyOrderBridges_BuyOrderId",
                table: "ProcBuyOrderBridges",
                column: "BuyOrderId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProcBuyOrderBridges_PurchaseOrderId",
                table: "ProcBuyOrderBridges",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcBuyOrderBridges_PurchaseRequestId",
                table: "ProcBuyOrderBridges",
                column: "PurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcGuarantees_ExpiryDate",
                table: "ProcGuarantees",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_ProcGuarantees_Number",
                table: "ProcGuarantees",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProcGuarantees_ProviderId",
                table: "ProcGuarantees",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcGuarantees_PurchaseOrderId",
                table: "ProcGuarantees",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPaymentTerms_PurchaseOrderId_RowNumber",
                table: "ProcPaymentTerms",
                columns: new[] { "PurchaseOrderId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcRequestRevisions_PurchaseRequestId_Revision",
                table: "ProcRequestRevisions",
                columns: new[] { "PurchaseRequestId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PsCashPlan_ContractId",
                table: "PsCashPlan",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PsCashPlan_PurchaseOrderId",
                table: "PsCashPlan",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PsDeductions_LegalRateId",
                table: "PsDeductions",
                column: "LegalRateId");

            migrationBuilder.CreateIndex(
                name: "IX_PsDeductions_NcrId",
                table: "PsDeductions",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_PsDeductions_StatementId",
                table: "PsDeductions",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_PsLegalRates_Code",
                table: "PsLegalRates",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PsPreviousPayments_StatementId",
                table: "PsPreviousPayments",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_PsStatementLines_CbsItemId",
                table: "PsStatementLines",
                column: "CbsItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PsStatementLines_OrderLineId",
                table: "PsStatementLines",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PsStatementLines_StatementId",
                table: "PsStatementLines",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_PsStatements_ContractId_Serial",
                table: "PsStatements",
                columns: new[] { "ContractId", "Serial" },
                filter: "[Deleted] = 0 AND [ContractId] IS NOT NULL AND [PurchaseOrderId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PsStatements_Number",
                table: "PsStatements",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PsStatements_PaymentRequestId",
                table: "PsStatements",
                column: "PaymentRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PsStatements_ProviderId",
                table: "PsStatements",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_PsStatements_ProvisionalStatementId",
                table: "PsStatements",
                column: "ProvisionalStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_PsStatements_PurchaseOrderId_Serial",
                table: "PsStatements",
                columns: new[] { "PurchaseOrderId", "Serial" },
                unique: true,
                filter: "[Deleted] = 0 AND [PurchaseOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PsStatements_Status",
                table: "PsStatements",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_ProcInquiries_ProcInquiries_SupersededByInquiryId",
                table: "ProcInquiries",
                column: "SupersededByInquiryId",
                principalTable: "ProcInquiries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProcPurchaseRequests_ProcPurchaseOrders_RelatedOrderId",
                table: "ProcPurchaseRequests",
                column: "RelatedOrderId",
                principalTable: "ProcPurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProcPurchaseRequests_ProcPurchaseRequests_SupersededByRequestId",
                table: "ProcPurchaseRequests",
                column: "SupersededByRequestId",
                principalTable: "ProcPurchaseRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Backfill of the rows that existed before Package 6 (idempotent): priority Normal, revision 1, form type derived from kind and contract.
            migrationBuilder.Sql("UPDATE [ProcPurchaseRequests] SET [Priority] = 2 WHERE [Priority] NOT IN (1, 2, 3, 4);");
            migrationBuilder.Sql("UPDATE [ProcPurchaseRequests] SET [Revision] = 1 WHERE [Revision] < 1;");
            migrationBuilder.Sql("UPDATE [ProcInquiries] SET [Revision] = 1 WHERE [Revision] < 1;");
            migrationBuilder.Sql("UPDATE [ProcPurchaseRequests] SET [FormType] = CASE WHEN [Kind] = 1 AND [ContractId] IS NOT NULL THEN 1 WHEN [Kind] = 1 THEN 2 WHEN [Kind] = 3 THEN 7 WHEN [ContractId] IS NOT NULL THEN 3 ELSE 4 END WHERE [PreviousRequestId] IS NULL AND [FormType] = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProcInquiries_ProcInquiries_SupersededByInquiryId",
                table: "ProcInquiries");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcPurchaseRequests_ProcPurchaseOrders_RelatedOrderId",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcPurchaseRequests_ProcPurchaseRequests_SupersededByRequestId",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropTable(
                name: "CtContractDrafts");

            migrationBuilder.DropTable(
                name: "CtDealSteps");

            migrationBuilder.DropTable(
                name: "CtHistory");

            migrationBuilder.DropTable(
                name: "CtTenderParticipants");

            migrationBuilder.DropTable(
                name: "FnPettyCashMiscForms");

            migrationBuilder.DropTable(
                name: "FnPettyCashRequests");

            migrationBuilder.DropTable(
                name: "FnPettyCashVehicleForms");

            migrationBuilder.DropTable(
                name: "ProcAddendumLines");

            migrationBuilder.DropTable(
                name: "ProcArchive");

            migrationBuilder.DropTable(
                name: "ProcBuyOrderBridges");

            migrationBuilder.DropTable(
                name: "ProcGuarantees");

            migrationBuilder.DropTable(
                name: "ProcPaymentTerms");

            migrationBuilder.DropTable(
                name: "ProcRequestRevisions");

            migrationBuilder.DropTable(
                name: "PsCashPlan");

            migrationBuilder.DropTable(
                name: "PsDeductions");

            migrationBuilder.DropTable(
                name: "PsPreviousPayments");

            migrationBuilder.DropTable(
                name: "PsStatementLines");

            migrationBuilder.DropTable(
                name: "CtTenderRuns");

            migrationBuilder.DropTable(
                name: "FnPettyCashSummaryLines");

            migrationBuilder.DropTable(
                name: "ProcAddenda");

            migrationBuilder.DropTable(
                name: "PsLegalRates");

            migrationBuilder.DropTable(
                name: "CtCbsItems");

            migrationBuilder.DropTable(
                name: "PsStatements");

            migrationBuilder.DropTable(
                name: "CtDealRequests");

            migrationBuilder.DropTable(
                name: "FnPettyCashSummaries");

            migrationBuilder.DropIndex(
                name: "IX_ProcPurchaseRequests_FormType",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropIndex(
                name: "IX_ProcPurchaseRequests_PreviousRequestId",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropIndex(
                name: "IX_ProcPurchaseRequests_RelatedOrderId",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropIndex(
                name: "IX_ProcPurchaseRequests_SupersededByRequestId",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropIndex(
                name: "IX_ProcInquiries_PreviousInquiryId",
                table: "ProcInquiries");

            migrationBuilder.DropIndex(
                name: "IX_ProcInquiries_SupersededByInquiryId",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "FormType",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "PreviousRequestId",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "RelatedOrderId",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "ReturnedByUserId",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "SupersededByRequestId",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "VehicleDate",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "VehicleDestination",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "VehicleOrigin",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "VehicleType",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "VehicleUnit",
                table: "ProcPurchaseRequests");

            migrationBuilder.DropColumn(
                name: "BudgetOverrideAmount",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "BudgetOverrideAt",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "BudgetOverrideByUserId",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "BudgetOverrideNote",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "PlanningApprovedAt",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "PlanningApprovedByUserId",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "PlanningBudgetAmount",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "PlanningDeliveryDays",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "PlanningNote",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "PreviousInquiryId",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "ReturnedByUserId",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "ProcInquiries");

            migrationBuilder.DropColumn(
                name: "SupersededByInquiryId",
                table: "ProcInquiries");

            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "ProcPurchaseRequests",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 2);
        }
    }
}
