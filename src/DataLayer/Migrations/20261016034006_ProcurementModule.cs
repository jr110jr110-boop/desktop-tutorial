using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ProcurementModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderLineId",
                table: "InvDocumentLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProcPurchaseRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    CostCenter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MaterialRequestId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EstimatedAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PlanningApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PlanningApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ManagementApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ManagementApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    BoardResolution = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_ProcPurchaseRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseRequests_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseRequests_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcSupplierEvaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    EvaluationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    Score = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Grade = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcSupplierEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcSupplierEvaluations_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcSupplierProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    Grade = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    EvaluatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextEvaluation = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Categories = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsStrategic = table.Column<bool>(type: "bit", nullable: false),
                    Blocked = table.Column<bool>(type: "bit", nullable: false),
                    BlockReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Warnings = table.Column<int>(type: "int", nullable: false),
                    SepidarCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NationalId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EconomicCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcSupplierProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcSupplierProfiles_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcInquiries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    InquiryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequiredQuotes = table.Column<int>(type: "int", nullable: false),
                    FewerQuotesReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SelectedQuoteId = table.Column<int>(type: "int", nullable: true),
                    SelectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SelectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SelectedByUserId = table.Column<int>(type: "int", nullable: true),
                    FinancialApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinancialApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CommercialApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CommercialApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    TechnicalApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TechnicalApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ManagementApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ManagementApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CommitteeMinutes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcInquiries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcInquiries_ProcPurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "ProcPurchaseRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcPurchaseRequestLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NeededBy = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EstimatedUnitPrice = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: true),
                    TechnicalRequirements = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    BuyOrderId = table.Column<int>(type: "int", nullable: true),
                    MaterialRequestLineId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcPurchaseRequestLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseRequestLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseRequestLines_ProcPurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "ProcPurchaseRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcSupplierEvaluationLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EvaluationId = table.Column<int>(type: "int", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcSupplierEvaluationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcSupplierEvaluationLines_ProcSupplierEvaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "ProcSupplierEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcPurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    InquiryId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: true),
                    Incoterms = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PaymentTerms = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VatPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    AdvancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    AdvanceGuarantee = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    AdvanceGuaranteeExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PerformanceGuarantee = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RetentionPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    ContractNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ContractSignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Requirements = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryPlace = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LatePenaltyPercentPerWeek = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_ProcPurchaseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseOrders_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseOrders_ProcInquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "ProcInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseOrders_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseOrders_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcQuotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InquiryId = table.Column<int>(type: "int", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    QuoteNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    QuoteDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryDays = table.Column<int>(type: "int", nullable: true),
                    PaymentTerms = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VatPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    TechnicallyAccepted = table.Column<bool>(type: "bit", nullable: false),
                    TechnicalScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    TechnicalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcQuotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcQuotes_ProcInquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "ProcInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcQuotes_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcPurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    RequestLineId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: false),
                    DeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InvoicedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LastReceiptDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcPurchaseOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseOrderLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseOrderLines_ProcPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "ProcPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcPurchaseOrderLines_ProcPurchaseRequestLines_RequestLineId",
                        column: x => x.RequestLineId,
                        principalTable: "ProcPurchaseRequestLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcSupplierInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsOfficial = table.Column<bool>(type: "bit", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    PriceVariance = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    MatchNote = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_ProcSupplierInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcSupplierInvoices_ProcPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "ProcPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcSupplierInvoices_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcQuoteLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuoteId = table.Column<int>(type: "int", nullable: false),
                    RequestLineId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcQuoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcQuoteLines_ProcPurchaseRequestLines_RequestLineId",
                        column: x => x.RequestLineId,
                        principalTable: "ProcPurchaseRequestLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcQuoteLines_ProcQuotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "ProcQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcServiceAcceptances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PurchaseOrderLineId = table.Column<int>(type: "int", nullable: false),
                    AcceptanceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcServiceAcceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcServiceAcceptances_ProcPurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "ProcPurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcPaymentRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    InvoiceId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Deductions = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    DeductionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    QcApproved = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidByUserId = table.Column<int>(type: "int", nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_ProcPaymentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcPaymentRequests_ProcPurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "ProcPurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcPaymentRequests_ProcSupplierInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "ProcSupplierInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcPaymentRequests_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcSupplierInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcSupplierInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcSupplierInvoiceLines_ProcPurchaseOrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "ProcPurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcSupplierInvoiceLines_ProcSupplierInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "ProcSupplierInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "Description", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "Name", "NormalizedName" },
                values: new object[] { 13, "00000013-0000-0000-0000-000000000000", null, null, null, null, "مدیرعامل", null, null, null, null, "CEO", "CEO" });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 1300, "permission", "Contract", null, null, null, null, null, null, null, null, 13 },
                    { 1301, "permission", "WorkOrder", null, null, null, null, null, null, null, null, 13 },
                    { 1302, "permission", "Warehouse", null, null, null, null, null, null, null, null, 13 },
                    { 1303, "permission", "Traceability", null, null, null, null, null, null, null, null, 13 },
                    { 1304, "permission", "SepidarLink", null, null, null, null, null, null, null, null, 13 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_PurchaseOrderId",
                table: "InvDocuments",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocumentLines_PurchaseOrderLineId",
                table: "InvDocumentLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcInquiries_Number",
                table: "ProcInquiries",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProcInquiries_PurchaseRequestId",
                table: "ProcInquiries",
                column: "PurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPaymentRequests_InvoiceId",
                table: "ProcPaymentRequests",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPaymentRequests_Number",
                table: "ProcPaymentRequests",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPaymentRequests_ProviderId",
                table: "ProcPaymentRequests",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPaymentRequests_PurchaseOrderId",
                table: "ProcPaymentRequests",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPaymentRequests_Status",
                table: "ProcPaymentRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseOrderLines_ItemId",
                table: "ProcPurchaseOrderLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseOrderLines_PurchaseOrderId",
                table: "ProcPurchaseOrderLines",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseOrderLines_RequestLineId",
                table: "ProcPurchaseOrderLines",
                column: "RequestLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseOrders_ContractId",
                table: "ProcPurchaseOrders",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseOrders_InquiryId",
                table: "ProcPurchaseOrders",
                column: "InquiryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseOrders_Number",
                table: "ProcPurchaseOrders",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseOrders_ProviderId",
                table: "ProcPurchaseOrders",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseOrders_Status",
                table: "ProcPurchaseOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseOrders_WorkOrderId",
                table: "ProcPurchaseOrders",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequestLines_ItemId",
                table: "ProcPurchaseRequestLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequestLines_PurchaseRequestId",
                table: "ProcPurchaseRequestLines",
                column: "PurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequests_ContractId",
                table: "ProcPurchaseRequests",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequests_Number",
                table: "ProcPurchaseRequests",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequests_Status",
                table: "ProcPurchaseRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProcPurchaseRequests_WorkOrderId",
                table: "ProcPurchaseRequests",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcQuoteLines_QuoteId",
                table: "ProcQuoteLines",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcQuoteLines_RequestLineId",
                table: "ProcQuoteLines",
                column: "RequestLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcQuotes_InquiryId",
                table: "ProcQuotes",
                column: "InquiryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcQuotes_ProviderId",
                table: "ProcQuotes",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcServiceAcceptances_Number",
                table: "ProcServiceAcceptances",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProcServiceAcceptances_PurchaseOrderLineId",
                table: "ProcServiceAcceptances",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcSettings_Key",
                table: "ProcSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcSupplierEvaluationLines_EvaluationId",
                table: "ProcSupplierEvaluationLines",
                column: "EvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcSupplierEvaluations_ProviderId",
                table: "ProcSupplierEvaluations",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcSupplierInvoiceLines_InvoiceId",
                table: "ProcSupplierInvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcSupplierInvoiceLines_OrderLineId",
                table: "ProcSupplierInvoiceLines",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcSupplierInvoices_Number",
                table: "ProcSupplierInvoices",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProcSupplierInvoices_ProviderId_InvoiceNumber",
                table: "ProcSupplierInvoices",
                columns: new[] { "ProviderId", "InvoiceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcSupplierInvoices_PurchaseOrderId",
                table: "ProcSupplierInvoices",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcSupplierProfiles_ProviderId",
                table: "ProcSupplierProfiles",
                column: "ProviderId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InvDocumentLines_ProcPurchaseOrderLines_PurchaseOrderLineId",
                table: "InvDocumentLines",
                column: "PurchaseOrderLineId",
                principalTable: "ProcPurchaseOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvDocuments_ProcPurchaseOrders_PurchaseOrderId",
                table: "InvDocuments",
                column: "PurchaseOrderId",
                principalTable: "ProcPurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvDocumentLines_ProcPurchaseOrderLines_PurchaseOrderLineId",
                table: "InvDocumentLines");

            migrationBuilder.DropForeignKey(
                name: "FK_InvDocuments_ProcPurchaseOrders_PurchaseOrderId",
                table: "InvDocuments");

            migrationBuilder.DropTable(
                name: "ProcPaymentRequests");

            migrationBuilder.DropTable(
                name: "ProcQuoteLines");

            migrationBuilder.DropTable(
                name: "ProcServiceAcceptances");

            migrationBuilder.DropTable(
                name: "ProcSettings");

            migrationBuilder.DropTable(
                name: "ProcSupplierEvaluationLines");

            migrationBuilder.DropTable(
                name: "ProcSupplierInvoiceLines");

            migrationBuilder.DropTable(
                name: "ProcSupplierProfiles");

            migrationBuilder.DropTable(
                name: "ProcQuotes");

            migrationBuilder.DropTable(
                name: "ProcSupplierEvaluations");

            migrationBuilder.DropTable(
                name: "ProcPurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "ProcSupplierInvoices");

            migrationBuilder.DropTable(
                name: "ProcPurchaseRequestLines");

            migrationBuilder.DropTable(
                name: "ProcPurchaseOrders");

            migrationBuilder.DropTable(
                name: "ProcInquiries");

            migrationBuilder.DropTable(
                name: "ProcPurchaseRequests");

            migrationBuilder.DropIndex(
                name: "IX_InvDocuments_PurchaseOrderId",
                table: "InvDocuments");

            migrationBuilder.DropIndex(
                name: "IX_InvDocumentLines_PurchaseOrderLineId",
                table: "InvDocumentLines");

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1300);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1301);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1302);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1303);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1304);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderLineId",
                table: "InvDocumentLines");
        }
    }
}
