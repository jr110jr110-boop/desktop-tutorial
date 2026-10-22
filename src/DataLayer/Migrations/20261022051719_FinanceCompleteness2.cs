using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class FinanceCompleteness2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoReversalVoucherId",
                table: "FnVouchers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReverseNextPeriod",
                table: "FnVouchers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMonetaryForeign",
                table: "FnAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FnAllocationKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromCostCenterId = table.Column<int>(type: "int", nullable: false),
                    ToCostCenterId = table.Column<int>(type: "int", nullable: false),
                    Basis = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnAllocationKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnBudgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    FiscalYearId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CommitMode = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ManagerApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ManagerApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CeoApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CeoApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SupersededByBudgetId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnBudgets_FnFiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalTable: "FnFiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnContractRevenues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    ContractPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    ProgressSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProgressMeasure = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnContractRevenues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnCurrencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Decimals = table.Column<int>(type: "int", nullable: false),
                    IsBase = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnCurrencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnDeferrals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BalanceAccountId = table.Column<int>(type: "int", nullable: false),
                    ResultAccountId = table.Column<int>(type: "int", nullable: false),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    GuaranteeId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnDeferrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnDeferrals_FnAccounts_BalanceAccountId",
                        column: x => x.BalanceAccountId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnDeferrals_FnAccounts_ResultAccountId",
                        column: x => x.ResultAccountId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnExchangeRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Buy = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Sell = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Average = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    RateType = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnFxRevaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    RateType = table.Column<int>(type: "int", nullable: false),
                    Reverse = table.Column<bool>(type: "bit", nullable: false),
                    TotalGain = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalLoss = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    ReversalVoucherId = table.Column<int>(type: "int", nullable: true),
                    Warning = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnFxRevaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnFxSettlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CurrencyAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BookRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SettleRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    BookBase = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    SettledBase = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnFxSettlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnLettersOfCredit",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LcNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OpenDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MarginPercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MarginPaid = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    FeesPaid = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    ForeignInvoiceRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SettlementRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    SettledBase = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    PaidDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnLettersOfCredit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnOverheadAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    TotalAllocated = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalAbsorbed = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Variance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    AllocationVoucherId = table.Column<int>(type: "int", nullable: true),
                    AbsorptionVoucherId = table.Column<int>(type: "int", nullable: true),
                    VarianceVoucherId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnOverheadAllocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnOverheadRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CostCenterId = table.Column<int>(type: "int", nullable: false),
                    Base = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Verify = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnOverheadRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnProvisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Balance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ReviewDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExpenseAccountId = table.Column<int>(type: "int", nullable: false),
                    LiabilityAccountId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnProvisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnProvisions_FnAccounts_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnProvisions_FnAccounts_LiabilityAccountId",
                        column: x => x.LiabilityAccountId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnRevenueRecognitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ProgressPercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ContractPrice = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    EarnedToDate = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    RecognisedBefore = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    RevenueThisRun = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    BilledToDate = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ContractAsset = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ContractLiability = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    LossProvision = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnRevenueRecognitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnStandardCosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnStandardCosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnWorkOrderBases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    CostCenterId = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    Base = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnWorkOrderBases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnWorkOrderCloses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    Material = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Labour = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Overhead = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Subcontract = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Variance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    ReversalVoucherId = table.Column<int>(type: "int", nullable: true),
                    ReopenReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnWorkOrderCloses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnBudgetLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BudgetId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: true),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    Period = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    IsCapital = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBudgetLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnBudgetLines_FnBudgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "FnBudgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnDeferralEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeferralId = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    VoucherId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnDeferralEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnDeferralEntries_FnDeferrals_DeferralId",
                        column: x => x.DeferralId,
                        principalTable: "FnDeferrals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnFxRevaluationLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Detail1Id = table.Column<int>(type: "int", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CurrencyBalance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BookBalance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    NewBalance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnFxRevaluationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnFxRevaluationLines_FnFxRevaluations_RunId",
                        column: x => x.RunId,
                        principalTable: "FnFxRevaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnLcPostings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LcId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnLcPostings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnLcPostings_FnLettersOfCredit_LcId",
                        column: x => x.LcId,
                        principalTable: "FnLettersOfCredit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnProvisionMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProvisionId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnProvisionMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnProvisionMovements_FnProvisions_ProvisionId",
                        column: x => x.ProvisionId,
                        principalTable: "FnProvisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FnAllocationKeys_FromCostCenterId_ValidFrom",
                table: "FnAllocationKeys",
                columns: new[] { "FromCostCenterId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_FnBudgetLines_BudgetId",
                table: "FnBudgetLines",
                column: "BudgetId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBudgets_FiscalYearId_Version",
                table: "FnBudgets",
                columns: new[] { "FiscalYearId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnBudgets_Number",
                table: "FnBudgets",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnContractRevenues_ContractId",
                table: "FnContractRevenues",
                column: "ContractId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnCurrencies_Code",
                table: "FnCurrencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnDeferralEntries_DeferralId_PeriodId",
                table: "FnDeferralEntries",
                columns: new[] { "DeferralId", "PeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnDeferrals_BalanceAccountId",
                table: "FnDeferrals",
                column: "BalanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnDeferrals_Number",
                table: "FnDeferrals",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnDeferrals_ResultAccountId",
                table: "FnDeferrals",
                column: "ResultAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnExchangeRates_CurrencyCode_Date_RateType",
                table: "FnExchangeRates",
                columns: new[] { "CurrencyCode", "Date", "RateType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnFxRevaluationLines_RunId",
                table: "FnFxRevaluationLines",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_FnFxRevaluations_LegalEntityId_PeriodId",
                table: "FnFxRevaluations",
                columns: new[] { "LegalEntityId", "PeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_FnFxRevaluations_Number",
                table: "FnFxRevaluations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnFxSettlements_Number",
                table: "FnFxSettlements",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnLcPostings_LcId",
                table: "FnLcPostings",
                column: "LcId");

            migrationBuilder.CreateIndex(
                name: "IX_FnLettersOfCredit_Number",
                table: "FnLettersOfCredit",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnOverheadAllocations_LegalEntityId_PeriodId",
                table: "FnOverheadAllocations",
                columns: new[] { "LegalEntityId", "PeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_FnOverheadAllocations_Number",
                table: "FnOverheadAllocations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnOverheadRates_CostCenterId_ValidFrom",
                table: "FnOverheadRates",
                columns: new[] { "CostCenterId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_FnProvisionMovements_ProvisionId",
                table: "FnProvisionMovements",
                column: "ProvisionId");

            migrationBuilder.CreateIndex(
                name: "IX_FnProvisions_ExpenseAccountId",
                table: "FnProvisions",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnProvisions_LiabilityAccountId",
                table: "FnProvisions",
                column: "LiabilityAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnProvisions_Number",
                table: "FnProvisions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnRevenueRecognitions_ContractId_PeriodId",
                table: "FnRevenueRecognitions",
                columns: new[] { "ContractId", "PeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_FnStandardCosts_ItemId_ValidFrom",
                table: "FnStandardCosts",
                columns: new[] { "ItemId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_FnWorkOrderBases_PeriodId_WorkOrderId_CostCenterId_Base",
                table: "FnWorkOrderBases",
                columns: new[] { "PeriodId", "WorkOrderId", "CostCenterId", "Base" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnWorkOrderCloses_Number",
                table: "FnWorkOrderCloses",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnWorkOrderCloses_WorkOrderId",
                table: "FnWorkOrderCloses",
                column: "WorkOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FnAllocationKeys");

            migrationBuilder.DropTable(
                name: "FnBudgetLines");

            migrationBuilder.DropTable(
                name: "FnContractRevenues");

            migrationBuilder.DropTable(
                name: "FnCurrencies");

            migrationBuilder.DropTable(
                name: "FnDeferralEntries");

            migrationBuilder.DropTable(
                name: "FnExchangeRates");

            migrationBuilder.DropTable(
                name: "FnFxRevaluationLines");

            migrationBuilder.DropTable(
                name: "FnFxSettlements");

            migrationBuilder.DropTable(
                name: "FnLcPostings");

            migrationBuilder.DropTable(
                name: "FnOverheadAllocations");

            migrationBuilder.DropTable(
                name: "FnOverheadRates");

            migrationBuilder.DropTable(
                name: "FnProvisionMovements");

            migrationBuilder.DropTable(
                name: "FnRevenueRecognitions");

            migrationBuilder.DropTable(
                name: "FnStandardCosts");

            migrationBuilder.DropTable(
                name: "FnWorkOrderBases");

            migrationBuilder.DropTable(
                name: "FnWorkOrderCloses");

            migrationBuilder.DropTable(
                name: "FnBudgets");

            migrationBuilder.DropTable(
                name: "FnDeferrals");

            migrationBuilder.DropTable(
                name: "FnFxRevaluations");

            migrationBuilder.DropTable(
                name: "FnLettersOfCredit");

            migrationBuilder.DropTable(
                name: "FnProvisions");

            migrationBuilder.DropColumn(
                name: "AutoReversalVoucherId",
                table: "FnVouchers");

            migrationBuilder.DropColumn(
                name: "ReverseNextPeriod",
                table: "FnVouchers");

            migrationBuilder.DropColumn(
                name: "IsMonetaryForeign",
                table: "FnAccounts");
        }
    }
}
