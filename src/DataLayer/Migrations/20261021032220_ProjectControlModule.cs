using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ProjectControlModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContractId",
                table: "QaDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkOrderId",
                table: "QaDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WbsNodeId",
                table: "FinCostEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PcApprovalAuthorities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CanDecide = table.Column<bool>(type: "bit", nullable: false),
                    MaxAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    MaxDelayDays = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcApprovalAuthorities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcBaselines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangeRequestId = table.Column<int>(type: "int", nullable: true),
                    ProjectStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProjectFinish = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Bac = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ManagementReserve = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcBaselines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcBaselines_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcCalendars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WorkingWeekdays = table.Column<int>(type: "int", nullable: false),
                    HoursPerDay = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    UseCompanyHolidays = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcCalendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Issue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Basis = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DaysQuantified = table.Column<int>(type: "int", nullable: true),
                    AmountQuantified = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsCrossClaim = table.Column<bool>(type: "bit", nullable: false),
                    PmoApproved = table.Column<bool>(type: "bit", nullable: false),
                    DesignManufacturingApproved = table.Column<bool>(type: "bit", nullable: false),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcClaims_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcClientResponseCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CountsAsApproved = table.Column<bool>(type: "bit", nullable: false),
                    RequiresResubmit = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcClientResponseCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcCodingTables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Table = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Family = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProductType = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcCodingTables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcContractAmendments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PriceDelta = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    DaysDelta = table.Column<int>(type: "int", nullable: true),
                    CeoApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CeoApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CustomerApprovalReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CustomerApprovalDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcContractAmendments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcContractAmendments_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcContractReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    TenderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    RequirementsDefined = table.Column<bool>(type: "bit", nullable: false),
                    DifferencesResolved = table.Column<bool>(type: "bit", nullable: false),
                    CapabilityChecked = table.Column<bool>(type: "bit", nullable: false),
                    DeliveryFeasible = table.Column<bool>(type: "bit", nullable: false),
                    StandardsItpAgreed = table.Column<bool>(type: "bit", nullable: false),
                    GuaranteesReviewed = table.Column<bool>(type: "bit", nullable: false),
                    PenaltiesReviewed = table.Column<bool>(type: "bit", nullable: false),
                    PaymentTermsReviewed = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyReviewed = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CapabilityCheckedByUserId = table.Column<int>(type: "int", nullable: true),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcContractReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcContractReviews_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcCostBaselineLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    BaselineId = table.Column<int>(type: "int", nullable: true),
                    WbsNodeId = table.Column<int>(type: "int", nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    CostClass = table.Column<int>(type: "int", nullable: false),
                    ResourceClass = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcCostBaselineLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcDailyReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDailyReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcEngineeringDeliverables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ClientSubmittal = table.Column<bool>(type: "bit", nullable: false),
                    StandardSource = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Discipline = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DocumentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    PlannedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IssueState = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TemplateLineId = table.Column<int>(type: "int", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ControlledDocumentId = table.Column<int>(type: "int", nullable: true),
                    LastTransmittalNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastTransmittalDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClientResponseCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ClientResponseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    IssuedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcEngineeringDeliverables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcEngineeringDeliverables_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcEngineeringDeliverables_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcGuaranteeInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceKind = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    GuaranteeType = table.Column<int>(type: "int", nullable: false),
                    AmountDecimal = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    IssuerKind = table.Column<int>(type: "int", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleaseTrigger = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ParseFailed = table.Column<bool>(type: "bit", nullable: false),
                    ParseNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcGuaranteeInfos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcMdrTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProductType = table.Column<int>(type: "int", nullable: false),
                    StandardSource = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcMdrTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcPaymentMilestones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Trigger = table.Column<int>(type: "int", nullable: false),
                    TriggerPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    PercentOfValue = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    PlannedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProvisionalStatementId = table.Column<int>(type: "int", nullable: true),
                    FinanceRef = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ClientLetterRef = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcPaymentMilestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcPaymentMilestones_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcPenaltyAssessments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    MilestoneId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActualDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LateDays = table.Column<int>(type: "int", nullable: false),
                    GraceDays = table.Column<int>(type: "int", nullable: false),
                    RatePercentPerWeek = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    CapPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    BaseAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecisionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcPenaltyAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcPenaltyAssessments_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcProgressSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    DataDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TakenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TakenByUserId = table.Column<int>(type: "int", nullable: false),
                    BaselineId = table.Column<int>(type: "int", nullable: true),
                    PhysicalPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    PlannedPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Bac = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Pv = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Ev = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Ac = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Cpi = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Spi = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ForecastFinish = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Hash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProgressSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcProgressSnapshots_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcProjectCharters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Overview = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ScopeDescription = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Requirements = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Deliverables = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Boundaries = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TimeBaselineEstimate = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CostEstimate = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AlternativesAnalysis = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Assumptions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Constraints = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Risks = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Stakeholders = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PreAssignedResources = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ManagerAuthority = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ManagerAcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProjectCharters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcProjectCharters_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcProjectInitiations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    CurrentStep = table.Column<int>(type: "int", nullable: false),
                    Completed = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProjectInitiations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcProjectInitiations_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcProjectPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Overview = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ScopeStatement = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    InScope = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OutOfScope = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Constraints = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Assumptions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Milestones = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LongLeadRules = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CommunicationPlan = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    MakeBuyPartner = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    QualityPlan = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ChangeManagement = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OtherSections = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProjectPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcProjectPlans_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcReportIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    DataDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GeneratedByUserId = table.Column<int>(type: "int", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ControlledDocumentId = table.Column<int>(type: "int", nullable: true),
                    ControlledRevision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcReportIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PcShipments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ReleaseNoteId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PackingList = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PackingVerification = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PackingPhotoDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShippingDocuments = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Waybill = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ShippedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryReceiptNumber = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SignedReceiptDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HandoverMinutesNumber = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    HandoverAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcShipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcShipments_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcShipments_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcTransmittals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    TransmittalDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResponseDue = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcTransmittals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcTransmittals_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcVariationOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ChangeRequestId = table.Column<int>(type: "int", nullable: true),
                    AmendmentId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PriceDelta = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    DaysDelta = table.Column<int>(type: "int", nullable: false),
                    ClientApprovalReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ClientApprovalDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcVariationOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcVariationOrders_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcWbsNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NodeType = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    AssemblyId = table.Column<int>(type: "int", nullable: true),
                    BomId = table.Column<int>(type: "int", nullable: true),
                    ProcessId = table.Column<int>(type: "int", nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    BudgetAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Dictionary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ResponsibleUnit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    IsGenerated = table.Column<bool>(type: "bit", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcWbsNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcWbsNodes_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcWbsNodes_PcWbsNodes_ParentId",
                        column: x => x.ParentId,
                        principalTable: "PcWbsNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcWorkOrderInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ProductType = table.Column<int>(type: "int", nullable: false),
                    EquipmentProjectCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TypeCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SizeCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OptionCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RepeatCounter = table.Column<int>(type: "int", nullable: false),
                    PlannedDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcWorkOrderInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcWorkOrderInfos_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcBaselineActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaselineId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    WbsNodeId = table.Column<int>(type: "int", nullable: true),
                    Phase = table.Column<int>(type: "int", nullable: true),
                    Start = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Finish = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Budget = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ProgressMethod = table.Column<int>(type: "int", nullable: false),
                    CalendarId = table.Column<int>(type: "int", nullable: true),
                    ResponsibleUnit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcBaselineActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcBaselineActivities_PcBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "PcBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcBaselineCostPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaselineId = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedCumulative = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcBaselineCostPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcBaselineCostPeriods_PcBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "PcBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcBaselineSignatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BaselineId = table.Column<int>(type: "int", nullable: false),
                    UnitRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcBaselineSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcBaselineSignatures_PcBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "PcBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcChangeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    RequesterUserId = table.Column<int>(type: "int", nullable: false),
                    ManagerAgreement = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Escalated = table.Column<bool>(type: "bit", nullable: false),
                    ScenarioJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestedPriceDelta = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    RequestedDaysDelta = table.Column<int>(type: "int", nullable: false),
                    ImpactJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImpactDeltaDays = table.Column<int>(type: "int", nullable: true),
                    ImpactDeltaCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    ImpactOnCriticalPath = table.Column<bool>(type: "bit", nullable: true),
                    AffectedPoLines = table.Column<int>(type: "int", nullable: false),
                    AffectedItpPoints = table.Column<int>(type: "int", nullable: false),
                    AnalysedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ImpactScope = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ImpactResources = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ImpactQuality = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ImpactRisk = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ImpactProcurement = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ImpactClientContract = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Recommendation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RecommendedByUserId = table.Column<int>(type: "int", nullable: true),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedByRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BaselineId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcChangeRequests_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcChangeRequests_PcBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "PcBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcContractTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ProjectCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Category = table.Column<int>(type: "int", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ValueExVat = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    VatPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    AdvancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    RetentionPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    LatePenaltyPercentPerWeek = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    PenaltyCapPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    GraceDays = table.Column<int>(type: "int", nullable: false),
                    ChangeProcedureAgreed = table.Column<bool>(type: "bit", nullable: false),
                    ChangeProcedureDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProjectManagerUserId = table.Column<int>(type: "int", nullable: true),
                    CalendarId = table.Column<int>(type: "int", nullable: true),
                    CurrentBaselineId = table.Column<int>(type: "int", nullable: true),
                    ClientReportDayOfMonth = table.Column<int>(type: "int", nullable: true),
                    DataDateWeekday = table.Column<int>(type: "int", nullable: false),
                    ScheduleStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContractDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClientReportShowsIndices = table.Column<bool>(type: "bit", nullable: false),
                    AccrualMode = table.Column<int>(type: "int", nullable: false),
                    ClientReviewDays = table.Column<int>(type: "int", nullable: false),
                    ManagementReserve = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    AutoSnapshot = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcContractTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcContractTerms_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcContractTerms_PcBaselines_CurrentBaselineId",
                        column: x => x.CurrentBaselineId,
                        principalTable: "PcBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcCalendarExceptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CalendarId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsWorking = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcCalendarExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcCalendarExceptions_PcCalendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "PcCalendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcAmendmentCopies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AmendmentId = table.Column<int>(type: "int", nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcAmendmentCopies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcAmendmentCopies_PcContractAmendments_AmendmentId",
                        column: x => x.AmendmentId,
                        principalTable: "PcContractAmendments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcDailyReportLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    EventText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    IsLate = table.Column<bool>(type: "bit", nullable: false),
                    DelayReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DelayReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PreventiveAction = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrectiveAction = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDailyReportLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDailyReportLines_PcDailyReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "PcDailyReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcGuaranteeEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GuaranteeInfoId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Fee = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcGuaranteeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcGuaranteeEvents_PcGuaranteeInfos_GuaranteeInfoId",
                        column: x => x.GuaranteeInfoId,
                        principalTable: "PcGuaranteeInfos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcMdrTemplateLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Discipline = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ClientSubmittal = table.Column<bool>(type: "bit", nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    StandardSource = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcMdrTemplateLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcMdrTemplateLines_PcMdrTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "PcMdrTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcProgressSnapshotLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SnapshotId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    PlannedPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Budget = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Ev = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Pv = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ForecastFinish = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProgressSnapshotLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcProgressSnapshotLines_PcProgressSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "PcProgressSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcInitiationSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InitiationId = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<int>(type: "int", nullable: false),
                    SlaDays = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneByUserId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcInitiationSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcInitiationSteps_PcProjectInitiations_InitiationId",
                        column: x => x.InitiationId,
                        principalTable: "PcProjectInitiations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcTransmittalLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransmittalId = table.Column<int>(type: "int", nullable: false),
                    DeliverableId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    ResponseCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ResponseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponseNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcTransmittalLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcTransmittalLines_PcEngineeringDeliverables_DeliverableId",
                        column: x => x.DeliverableId,
                        principalTable: "PcEngineeringDeliverables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcTransmittalLines_PcTransmittals_TransmittalId",
                        column: x => x.TransmittalId,
                        principalTable: "PcTransmittals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    WbsNodeId = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    CalendarId = table.Column<int>(type: "int", nullable: true),
                    Phase = table.Column<int>(type: "int", nullable: true),
                    ResponsibleUnit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ConstraintType = table.Column<int>(type: "int", nullable: false),
                    ConstraintDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EarlyStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EarlyFinish = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LateStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LateFinish = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalFloat = table.Column<int>(type: "int", nullable: true),
                    FreeFloat = table.Column<int>(type: "int", nullable: true),
                    IsCritical = table.Column<bool>(type: "bit", nullable: false),
                    ForecastStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ForecastFinish = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualFinish = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProgressMethod = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    BudgetAtCompletion = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OnHoldReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PurchaseOrderLineId = table.Column<int>(type: "int", nullable: true),
                    PurchaseRequestLineId = table.Column<int>(type: "int", nullable: true),
                    ItpActivityId = table.Column<int>(type: "int", nullable: true),
                    MilestoneEvent = table.Column<int>(type: "int", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SerialItemId = table.Column<int>(type: "int", nullable: true),
                    NeedByDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeadTimeDays = table.Column<int>(type: "int", nullable: false),
                    IsPackaging = table.Column<bool>(type: "bit", nullable: false),
                    IsLate = table.Column<bool>(type: "bit", nullable: false),
                    LateKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PercentCache = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcActivities_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcActivities_PcWbsNodes_WbsNodeId",
                        column: x => x.WbsNodeId,
                        principalTable: "PcWbsNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcChangeBoardMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChangeRequestId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Vote = table.Column<int>(type: "int", nullable: false),
                    VotedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcChangeBoardMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcChangeBoardMembers_PcChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "PcChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcDependencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    PredecessorId = table.Column<int>(type: "int", nullable: false),
                    SuccessorId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    LagDays = table.Column<int>(type: "int", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDependencies_PcActivities_PredecessorId",
                        column: x => x.PredecessorId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcDependencies_PcActivities_SuccessorId",
                        column: x => x.SuccessorId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcManualProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProposedByUserId = table.Column<int>(type: "int", nullable: false),
                    ProposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcManualProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcManualProgress_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "Description", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { 15, "00000015-0000-0000-0000-000000000000", null, null, null, null, "مدیر پروژه", null, null, null, null, "PROJECTMANAGER", "PROJECTMANAGER" },
                    { 16, "00000016-0000-0000-0000-000000000000", null, null, null, null, "کنترل مدارک پروژه", null, null, null, null, "DCC", "DCC" }
                });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 1500, "permission", "Contract", null, null, null, null, null, null, null, null, 15 },
                    { 1501, "permission", "WorkOrder", null, null, null, null, null, null, null, null, 15 },
                    { 1502, "permission", "Traceability", null, null, null, null, null, null, null, null, 15 },
                    { 1600, "permission", "Contract", null, null, null, null, null, null, null, null, 16 },
                    { 1601, "permission", "WorkOrder", null, null, null, null, null, null, null, null, 16 },
                    { 1602, "permission", "Traceability", null, null, null, null, null, null, null, null, 16 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PcActivities_ContractId_Code",
                table: "PcActivities",
                columns: new[] { "ContractId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_PcActivities_ItpActivityId",
                table: "PcActivities",
                column: "ItpActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcActivities_PurchaseOrderLineId",
                table: "PcActivities",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PcActivities_WbsNodeId",
                table: "PcActivities",
                column: "WbsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PcAmendmentCopies_AmendmentId",
                table: "PcAmendmentCopies",
                column: "AmendmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PcApprovalAuthorities_Role",
                table: "PcApprovalAuthorities",
                column: "Role",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcBaselineActivities_BaselineId_ActivityId",
                table: "PcBaselineActivities",
                columns: new[] { "BaselineId", "ActivityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcBaselineCostPeriods_BaselineId",
                table: "PcBaselineCostPeriods",
                column: "BaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_PcBaselines_ContractId_Number",
                table: "PcBaselines",
                columns: new[] { "ContractId", "Number" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcBaselineSignatures_BaselineId",
                table: "PcBaselineSignatures",
                column: "BaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_PcCalendarExceptions_CalendarId_Date",
                table: "PcCalendarExceptions",
                columns: new[] { "CalendarId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcChangeBoardMembers_ChangeRequestId",
                table: "PcChangeBoardMembers",
                column: "ChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PcChangeRequests_BaselineId",
                table: "PcChangeRequests",
                column: "BaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_PcChangeRequests_ContractId_Status",
                table: "PcChangeRequests",
                columns: new[] { "ContractId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PcChangeRequests_Number",
                table: "PcChangeRequests",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcClaims_ContractId",
                table: "PcClaims",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcClaims_Number",
                table: "PcClaims",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcClientResponseCodes_Code",
                table: "PcClientResponseCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcCodingTables_Table_Family_Code",
                table: "PcCodingTables",
                columns: new[] { "Table", "Family", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_PcContractAmendments_ContractId",
                table: "PcContractAmendments",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcContractAmendments_Number",
                table: "PcContractAmendments",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcContractReviews_ContractId",
                table: "PcContractReviews",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcContractTerms_ContractId",
                table: "PcContractTerms",
                column: "ContractId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcContractTerms_CurrentBaselineId",
                table: "PcContractTerms",
                column: "CurrentBaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_PcContractTerms_ProjectCode",
                table: "PcContractTerms",
                column: "ProjectCode",
                unique: true,
                filter: "[Deleted] = 0 AND [ProjectCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PcCostBaselineLines_ContractId",
                table: "PcCostBaselineLines",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDailyReportLines_ReportId",
                table: "PcDailyReportLines",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDailyReports_ContractId_Unit_ReportDate",
                table: "PcDailyReports",
                columns: new[] { "ContractId", "Unit", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PcDependencies_ContractId",
                table: "PcDependencies",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDependencies_PredecessorId_SuccessorId",
                table: "PcDependencies",
                columns: new[] { "PredecessorId", "SuccessorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcDependencies_SuccessorId",
                table: "PcDependencies",
                column: "SuccessorId");

            migrationBuilder.CreateIndex(
                name: "IX_PcEngineeringDeliverables_ContractId_DocumentNumber",
                table: "PcEngineeringDeliverables",
                columns: new[] { "ContractId", "DocumentNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PcEngineeringDeliverables_ContractId_WorkOrderId",
                table: "PcEngineeringDeliverables",
                columns: new[] { "ContractId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_PcEngineeringDeliverables_WorkOrderId",
                table: "PcEngineeringDeliverables",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PcGuaranteeEvents_GuaranteeInfoId",
                table: "PcGuaranteeEvents",
                column: "GuaranteeInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_PcGuaranteeInfos_ExpiryDate",
                table: "PcGuaranteeInfos",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_PcGuaranteeInfos_SourceKind_SourceId",
                table: "PcGuaranteeInfos",
                columns: new[] { "SourceKind", "SourceId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcInitiationSteps_InitiationId_Step",
                table: "PcInitiationSteps",
                columns: new[] { "InitiationId", "Step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcManualProgress_ActivityId",
                table: "PcManualProgress",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcMdrTemplateLines_TemplateId",
                table: "PcMdrTemplateLines",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PcMdrTemplates_Code",
                table: "PcMdrTemplates",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcPaymentMilestones_ContractId_Seq",
                table: "PcPaymentMilestones",
                columns: new[] { "ContractId", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_PcPenaltyAssessments_ContractId",
                table: "PcPenaltyAssessments",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressSnapshotLines_SnapshotId",
                table: "PcProgressSnapshotLines",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressSnapshots_ContractId_DataDate",
                table: "PcProgressSnapshots",
                columns: new[] { "ContractId", "DataDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcProjectCharters_ContractId",
                table: "PcProjectCharters",
                column: "ContractId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcProjectInitiations_ContractId",
                table: "PcProjectInitiations",
                column: "ContractId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcProjectPlans_ContractId",
                table: "PcProjectPlans",
                column: "ContractId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcReportIssues_ContractId_Kind_DataDate",
                table: "PcReportIssues",
                columns: new[] { "ContractId", "Kind", "DataDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PcReportIssues_Number",
                table: "PcReportIssues",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcSettings_Key",
                table: "PcSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcShipments_ContractId",
                table: "PcShipments",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcShipments_Number",
                table: "PcShipments",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcShipments_WorkOrderId",
                table: "PcShipments",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PcTransmittalLines_DeliverableId",
                table: "PcTransmittalLines",
                column: "DeliverableId");

            migrationBuilder.CreateIndex(
                name: "IX_PcTransmittalLines_TransmittalId",
                table: "PcTransmittalLines",
                column: "TransmittalId");

            migrationBuilder.CreateIndex(
                name: "IX_PcTransmittals_ContractId",
                table: "PcTransmittals",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcTransmittals_Number",
                table: "PcTransmittals",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcVariationOrders_ContractId",
                table: "PcVariationOrders",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcVariationOrders_Number",
                table: "PcVariationOrders",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcWbsNodes_ContractId_Code",
                table: "PcWbsNodes",
                columns: new[] { "ContractId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_PcWbsNodes_ParentId",
                table: "PcWbsNodes",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_PcWorkOrderInfos_EquipmentProjectCode",
                table: "PcWorkOrderInfos",
                column: "EquipmentProjectCode",
                unique: true,
                filter: "[Deleted] = 0 AND [EquipmentProjectCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PcWorkOrderInfos_WorkOrderId",
                table: "PcWorkOrderInfos",
                column: "WorkOrderId",
                unique: true,
                filter: "[Deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PcAmendmentCopies");

            migrationBuilder.DropTable(
                name: "PcApprovalAuthorities");

            migrationBuilder.DropTable(
                name: "PcBaselineActivities");

            migrationBuilder.DropTable(
                name: "PcBaselineCostPeriods");

            migrationBuilder.DropTable(
                name: "PcBaselineSignatures");

            migrationBuilder.DropTable(
                name: "PcCalendarExceptions");

            migrationBuilder.DropTable(
                name: "PcChangeBoardMembers");

            migrationBuilder.DropTable(
                name: "PcClaims");

            migrationBuilder.DropTable(
                name: "PcClientResponseCodes");

            migrationBuilder.DropTable(
                name: "PcCodingTables");

            migrationBuilder.DropTable(
                name: "PcContractReviews");

            migrationBuilder.DropTable(
                name: "PcContractTerms");

            migrationBuilder.DropTable(
                name: "PcCostBaselineLines");

            migrationBuilder.DropTable(
                name: "PcDailyReportLines");

            migrationBuilder.DropTable(
                name: "PcDependencies");

            migrationBuilder.DropTable(
                name: "PcGuaranteeEvents");

            migrationBuilder.DropTable(
                name: "PcInitiationSteps");

            migrationBuilder.DropTable(
                name: "PcManualProgress");

            migrationBuilder.DropTable(
                name: "PcMdrTemplateLines");

            migrationBuilder.DropTable(
                name: "PcPaymentMilestones");

            migrationBuilder.DropTable(
                name: "PcPenaltyAssessments");

            migrationBuilder.DropTable(
                name: "PcProgressSnapshotLines");

            migrationBuilder.DropTable(
                name: "PcProjectCharters");

            migrationBuilder.DropTable(
                name: "PcProjectPlans");

            migrationBuilder.DropTable(
                name: "PcReportIssues");

            migrationBuilder.DropTable(
                name: "PcSettings");

            migrationBuilder.DropTable(
                name: "PcShipments");

            migrationBuilder.DropTable(
                name: "PcTransmittalLines");

            migrationBuilder.DropTable(
                name: "PcVariationOrders");

            migrationBuilder.DropTable(
                name: "PcWorkOrderInfos");

            migrationBuilder.DropTable(
                name: "PcContractAmendments");

            migrationBuilder.DropTable(
                name: "PcCalendars");

            migrationBuilder.DropTable(
                name: "PcChangeRequests");

            migrationBuilder.DropTable(
                name: "PcDailyReports");

            migrationBuilder.DropTable(
                name: "PcGuaranteeInfos");

            migrationBuilder.DropTable(
                name: "PcProjectInitiations");

            migrationBuilder.DropTable(
                name: "PcActivities");

            migrationBuilder.DropTable(
                name: "PcMdrTemplates");

            migrationBuilder.DropTable(
                name: "PcProgressSnapshots");

            migrationBuilder.DropTable(
                name: "PcEngineeringDeliverables");

            migrationBuilder.DropTable(
                name: "PcTransmittals");

            migrationBuilder.DropTable(
                name: "PcBaselines");

            migrationBuilder.DropTable(
                name: "PcWbsNodes");

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1500);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1501);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1502);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1600);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1601);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1602);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "QaDocuments");

            migrationBuilder.DropColumn(
                name: "WorkOrderId",
                table: "QaDocuments");

            migrationBuilder.DropColumn(
                name: "WbsNodeId",
                table: "FinCostEntries");
        }
    }
}
