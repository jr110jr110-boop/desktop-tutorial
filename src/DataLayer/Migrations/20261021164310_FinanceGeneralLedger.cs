using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class FinanceGeneralLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "ProcSupplierProfiles",
                type: "nvarchar(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "FinAccountMappings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrencyAmount",
                table: "FinAccountingEventLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "FinAccountingEventLines",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartyId",
                table: "FinAccountingEventLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyType",
                table: "FinAccountingEventLines",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Rate",
                table: "FinAccountingEventLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FnAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    Nature = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Temporary = table.Column<bool>(type: "bit", nullable: false),
                    DetailTypes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Detail2Types = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequiresDetail = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyAllowed = table.Column<bool>(type: "bit", nullable: false),
                    StatementLine = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SepidarCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Jurisdiction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnAccounts_FnAccounts_ParentId",
                        column: x => x.ParentId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnCashBoxes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustodianTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnCashBoxes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnCustomers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    EconomicCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NationalId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EmployerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnDetailAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    LinkedEntityId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnDetailAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnGuarantees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    PartyName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClosedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnGuarantees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnPaymentDeductions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentRequestId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Base = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    RatePercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPaymentDeductions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPaymentDeductions_ProcPaymentRequests_PaymentRequestId",
                        column: x => x.PaymentRequestId,
                        principalTable: "ProcPaymentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    PettyCashId = table.Column<int>(type: "int", nullable: true),
                    PayeeName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    DeductionAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedByUserId = table.Column<int>(type: "int", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IbanApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPayments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnPettyCashes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HolderUserId = table.Column<int>(type: "int", nullable: false),
                    HolderTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Ceiling = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPettyCashes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnTaxRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Verify = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Jurisdiction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnTaxRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnBankAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Iban = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBankAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnBankAccounts_FnAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    PayerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedByUserId = table.Column<int>(type: "int", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnReceipts_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnReceivedCheques",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SayadId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Serial = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccountHolder = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    ReceiptId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DepositBankAccountId = table.Column<int>(type: "int", nullable: true),
                    EndorsedProviderId = table.Column<int>(type: "int", nullable: true),
                    ReceivedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnReceivedCheques", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnReceivedCheques_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnSalesInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    MilestoneId = table.Column<int>(type: "int", nullable: true),
                    ProvisionalStatementId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreditedInvoiceId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    AdvanceRecovery = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    PerformanceRetentionPercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    PerformanceRetention = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    InsuranceRetentionPercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    InsuranceRetention = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    WithholdingPercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Withholding = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ReceivableAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TaxId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TaxSentStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnSalesInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnSalesInvoices_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnPaymentLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Instrument = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: true),
                    CashBoxId = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PayeeIban = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    IssuedChequeId = table.Column<int>(type: "int", nullable: true),
                    ChequeDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChequeSayadId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPaymentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPaymentLines_FnPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "FnPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnPaymentRequestLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    PaymentRequestId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Deductions = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPaymentRequestLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPaymentRequestLinks_FnPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "FnPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FnPaymentRequestLinks_ProcPaymentRequests_PaymentRequestId",
                        column: x => x.PaymentRequestId,
                        principalTable: "ProcPaymentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnPettyCashSettlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PettyCashId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPettyCashSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPettyCashSettlements_FnPettyCashes_PettyCashId",
                        column: x => x.PettyCashId,
                        principalTable: "FnPettyCashes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnBankReconciliations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    StatementId = table.Column<int>(type: "int", nullable: false),
                    AsOfDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BookBalance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    BankBalance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    DepositsInTransit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    OutstandingPayments = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    OutstandingCheques = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBankReconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnBankReconciliations_FnBankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "FnBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnBankStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ImportedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBankStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnBankStatements_FnBankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "FnBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnChequeBooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    Series = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FromSerial = table.Column<long>(type: "bigint", nullable: false),
                    ToSerial = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnChequeBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnChequeBooks_FnBankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "FnBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnReceiptLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Instrument = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: true),
                    CashBoxId = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceivedChequeId = table.Column<int>(type: "int", nullable: true),
                    ChequeSayadId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ChequeSerial = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ChequeBank = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChequeBranch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChequeHolder = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChequeDueDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnReceiptLines_FnReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "FnReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FnReceiptLines_FnReceivedCheques_ReceivedChequeId",
                        column: x => x.ReceivedChequeId,
                        principalTable: "FnReceivedCheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnReceivedChequeHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChequeId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AccountingEventId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnReceivedChequeHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnReceivedChequeHistories_FinAccountingEvents_AccountingEventId",
                        column: x => x.AccountingEventId,
                        principalTable: "FinAccountingEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnReceivedChequeHistories_FnReceivedCheques_ChequeId",
                        column: x => x.ChequeId,
                        principalTable: "FnReceivedCheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnReceiptAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnReceiptAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnReceiptAllocations_FnReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "FnReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FnReceiptAllocations_FnSalesInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "FnSalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnSalesInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VatExempt = table.Column<bool>(type: "bit", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnSalesInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnSalesInvoiceLines_FnSalesInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "FnSalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnPettyCashSettlementLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SettlementId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InventoryDocumentId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPettyCashSettlementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPettyCashSettlementLines_FnPettyCashSettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "FnPettyCashSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnBankStatementLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatementId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Withdrawal = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Deposit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Balance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    MatchKind = table.Column<int>(type: "int", nullable: false),
                    MatchedId = table.Column<int>(type: "int", nullable: true),
                    AccountingEventId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBankStatementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnBankStatementLines_FinAccountingEvents_AccountingEventId",
                        column: x => x.AccountingEventId,
                        principalTable: "FinAccountingEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnBankStatementLines_FnBankStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "FnBankStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnIssuedCheques",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChequeBookId = table.Column<int>(type: "int", nullable: false),
                    Serial = table.Column<long>(type: "bigint", nullable: false),
                    SayadId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PayeeName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    PaymentId = table.Column<int>(type: "int", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClearedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnIssuedCheques", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnIssuedCheques_FnChequeBooks_ChequeBookId",
                        column: x => x.ChequeBookId,
                        principalTable: "FnChequeBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnFiscalYears",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OpeningVoucherId = table.Column<int>(type: "int", nullable: true),
                    ClosingVoucherId = table.Column<int>(type: "int", nullable: true),
                    Jurisdiction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnFiscalYears", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FiscalYearId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LockedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPeriods_FnFiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalTable: "FnFiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FiscalYearId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: true),
                    TemporaryNumber = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    SourceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AccountingEventId = table.Column<int>(type: "int", nullable: true),
                    ReversesVoucherId = table.Column<int>(type: "int", nullable: true),
                    LatePosting = table.Column<bool>(type: "bit", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedByUserId = table.Column<int>(type: "int", nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinalizedByUserId = table.Column<int>(type: "int", nullable: true),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnVouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnVouchers_FnFiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalTable: "FnFiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnVouchers_FnVouchers_ReversesVoucherId",
                        column: x => x.ReversesVoucherId,
                        principalTable: "FnVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnPeriodLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPeriodLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPeriodLogs_FnPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "FnPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnVoucherLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Detail1Id = table.Column<int>(type: "int", nullable: true),
                    Detail2Id = table.Column<int>(type: "int", nullable: true),
                    Debit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CurrencyAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    SourceRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnVoucherLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnVoucherLines_FnAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnVoucherLines_FnDetailAccounts_Detail1Id",
                        column: x => x.Detail1Id,
                        principalTable: "FnDetailAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnVoucherLines_FnDetailAccounts_Detail2Id",
                        column: x => x.Detail2Id,
                        principalTable: "FnDetailAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnVoucherLines_FnVouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "FnVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "Description", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { 17, "00000017-0000-0000-0000-000000000000", null, null, null, null, "مدیر مالی", null, null, null, null, "FINANCEMANAGER", "FINANCEMANAGER" },
                    { 18, "00000018-0000-0000-0000-000000000000", null, null, null, null, "حقوق و دستمزد", null, null, null, null, "PAYROLL", "PAYROLL" },
                    { 19, "00000019-0000-0000-0000-000000000000", null, null, null, null, "خزانه‌داری", null, null, null, null, "TREASURY", "TREASURY" }
                });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 1700, "permission", "Warehouse", null, null, null, null, null, null, null, null, 17 },
                    { 1701, "permission", "SepidarLink", null, null, null, null, null, null, null, null, 17 },
                    { 1900, "permission", "Warehouse", null, null, null, null, null, null, null, null, 19 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinAccountMappings_AccountId",
                table: "FinAccountMappings",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnAccounts_Code",
                table: "FnAccounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnAccounts_ParentId",
                table: "FnAccounts",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBankAccounts_AccountId",
                table: "FnBankAccounts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBankReconciliations_BankAccountId",
                table: "FnBankReconciliations",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBankStatementLines_AccountingEventId",
                table: "FnBankStatementLines",
                column: "AccountingEventId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBankStatementLines_StatementId",
                table: "FnBankStatementLines",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBankStatements_BankAccountId",
                table: "FnBankStatements",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnChequeBooks_BankAccountId",
                table: "FnChequeBooks",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnCustomers_EmployerName",
                table: "FnCustomers",
                column: "EmployerName");

            migrationBuilder.CreateIndex(
                name: "IX_FnDetailAccounts_Code",
                table: "FnDetailAccounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnDetailAccounts_Type_LinkedEntityId",
                table: "FnDetailAccounts",
                columns: new[] { "Type", "LinkedEntityId" },
                unique: true,
                filter: "[LinkedEntityId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FnFiscalYears_ClosingVoucherId",
                table: "FnFiscalYears",
                column: "ClosingVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_FnFiscalYears_OpeningVoucherId",
                table: "FnFiscalYears",
                column: "OpeningVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_FnFiscalYears_Year",
                table: "FnFiscalYears",
                column: "Year",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnGuarantees_Number",
                table: "FnGuarantees",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnIssuedCheques_ChequeBookId_Serial",
                table: "FnIssuedCheques",
                columns: new[] { "ChequeBookId", "Serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnIssuedCheques_Status_DueDate",
                table: "FnIssuedCheques",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FnPaymentDeductions_PaymentRequestId",
                table: "FnPaymentDeductions",
                column: "PaymentRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPaymentLines_PaymentId",
                table: "FnPaymentLines",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPaymentRequestLinks_PaymentId",
                table: "FnPaymentRequestLinks",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPaymentRequestLinks_PaymentRequestId",
                table: "FnPaymentRequestLinks",
                column: "PaymentRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPayments_Number",
                table: "FnPayments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnPeriodLogs_PeriodId",
                table: "FnPeriodLogs",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPeriods_FiscalYearId_Number",
                table: "FnPeriods",
                columns: new[] { "FiscalYearId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashes_HolderUserId",
                table: "FnPettyCashes",
                column: "HolderUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashSettlementLines_InventoryDocumentId",
                table: "FnPettyCashSettlementLines",
                column: "InventoryDocumentId",
                unique: true,
                filter: "[InventoryDocumentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashSettlementLines_SettlementId",
                table: "FnPettyCashSettlementLines",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashSettlements_Number",
                table: "FnPettyCashSettlements",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnPettyCashSettlements_PettyCashId",
                table: "FnPettyCashSettlements",
                column: "PettyCashId");

            migrationBuilder.CreateIndex(
                name: "IX_FnReceiptAllocations_InvoiceId",
                table: "FnReceiptAllocations",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_FnReceiptAllocations_ReceiptId",
                table: "FnReceiptAllocations",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_FnReceiptLines_ReceiptId",
                table: "FnReceiptLines",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_FnReceiptLines_ReceivedChequeId",
                table: "FnReceiptLines",
                column: "ReceivedChequeId");

            migrationBuilder.CreateIndex(
                name: "IX_FnReceipts_CustomerId",
                table: "FnReceipts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_FnReceipts_Number",
                table: "FnReceipts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnReceivedChequeHistories_AccountingEventId",
                table: "FnReceivedChequeHistories",
                column: "AccountingEventId");

            migrationBuilder.CreateIndex(
                name: "IX_FnReceivedChequeHistories_ChequeId",
                table: "FnReceivedChequeHistories",
                column: "ChequeId");

            migrationBuilder.CreateIndex(
                name: "IX_FnReceivedCheques_CustomerId",
                table: "FnReceivedCheques",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_FnReceivedCheques_SayadId",
                table: "FnReceivedCheques",
                column: "SayadId",
                unique: true,
                filter: "[SayadId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FnReceivedCheques_Status_DueDate",
                table: "FnReceivedCheques",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FnSalesInvoiceLines_InvoiceId",
                table: "FnSalesInvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_FnSalesInvoices_CustomerId_Status",
                table: "FnSalesInvoices",
                columns: new[] { "CustomerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FnSalesInvoices_MilestoneId",
                table: "FnSalesInvoices",
                column: "MilestoneId",
                unique: true,
                filter: "[MilestoneId] IS NOT NULL AND [Status] <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_FnSalesInvoices_Number",
                table: "FnSalesInvoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnSettings_Key",
                table: "FnSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnTaxRates_Kind_EffectiveFrom",
                table: "FnTaxRates",
                columns: new[] { "Kind", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_FnVoucherLines_AccountId",
                table: "FnVoucherLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnVoucherLines_ContractId",
                table: "FnVoucherLines",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_FnVoucherLines_Detail1Id",
                table: "FnVoucherLines",
                column: "Detail1Id");

            migrationBuilder.CreateIndex(
                name: "IX_FnVoucherLines_Detail2Id",
                table: "FnVoucherLines",
                column: "Detail2Id");

            migrationBuilder.CreateIndex(
                name: "IX_FnVoucherLines_VoucherId",
                table: "FnVoucherLines",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_FnVouchers_AccountingEventId",
                table: "FnVouchers",
                column: "AccountingEventId",
                unique: true,
                filter: "[AccountingEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FnVouchers_Date",
                table: "FnVouchers",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_FnVouchers_FiscalYearId_Number",
                table: "FnVouchers",
                columns: new[] { "FiscalYearId", "Number" },
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FnVouchers_FiscalYearId_Status",
                table: "FnVouchers",
                columns: new[] { "FiscalYearId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FnVouchers_ReversesVoucherId",
                table: "FnVouchers",
                column: "ReversesVoucherId",
                unique: true,
                filter: "[ReversesVoucherId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_FinAccountMappings_FnAccounts_AccountId",
                table: "FinAccountMappings",
                column: "AccountId",
                principalTable: "FnAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FnFiscalYears_FnVouchers_ClosingVoucherId",
                table: "FnFiscalYears",
                column: "ClosingVoucherId",
                principalTable: "FnVouchers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FnFiscalYears_FnVouchers_OpeningVoucherId",
                table: "FnFiscalYears",
                column: "OpeningVoucherId",
                principalTable: "FnVouchers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinAccountMappings_FnAccounts_AccountId",
                table: "FinAccountMappings");

            migrationBuilder.DropForeignKey(
                name: "FK_FnFiscalYears_FnVouchers_ClosingVoucherId",
                table: "FnFiscalYears");

            migrationBuilder.DropForeignKey(
                name: "FK_FnFiscalYears_FnVouchers_OpeningVoucherId",
                table: "FnFiscalYears");

            migrationBuilder.DropTable(
                name: "FnBankReconciliations");

            migrationBuilder.DropTable(
                name: "FnBankStatementLines");

            migrationBuilder.DropTable(
                name: "FnCashBoxes");

            migrationBuilder.DropTable(
                name: "FnGuarantees");

            migrationBuilder.DropTable(
                name: "FnIssuedCheques");

            migrationBuilder.DropTable(
                name: "FnPaymentDeductions");

            migrationBuilder.DropTable(
                name: "FnPaymentLines");

            migrationBuilder.DropTable(
                name: "FnPaymentRequestLinks");

            migrationBuilder.DropTable(
                name: "FnPeriodLogs");

            migrationBuilder.DropTable(
                name: "FnPettyCashSettlementLines");

            migrationBuilder.DropTable(
                name: "FnReceiptAllocations");

            migrationBuilder.DropTable(
                name: "FnReceiptLines");

            migrationBuilder.DropTable(
                name: "FnReceivedChequeHistories");

            migrationBuilder.DropTable(
                name: "FnSalesInvoiceLines");

            migrationBuilder.DropTable(
                name: "FnSettings");

            migrationBuilder.DropTable(
                name: "FnTaxRates");

            migrationBuilder.DropTable(
                name: "FnVoucherLines");

            migrationBuilder.DropTable(
                name: "FnBankStatements");

            migrationBuilder.DropTable(
                name: "FnChequeBooks");

            migrationBuilder.DropTable(
                name: "FnPayments");

            migrationBuilder.DropTable(
                name: "FnPeriods");

            migrationBuilder.DropTable(
                name: "FnPettyCashSettlements");

            migrationBuilder.DropTable(
                name: "FnReceipts");

            migrationBuilder.DropTable(
                name: "FnReceivedCheques");

            migrationBuilder.DropTable(
                name: "FnSalesInvoices");

            migrationBuilder.DropTable(
                name: "FnDetailAccounts");

            migrationBuilder.DropTable(
                name: "FnBankAccounts");

            migrationBuilder.DropTable(
                name: "FnPettyCashes");

            migrationBuilder.DropTable(
                name: "FnCustomers");

            migrationBuilder.DropTable(
                name: "FnAccounts");

            migrationBuilder.DropTable(
                name: "FnVouchers");

            migrationBuilder.DropTable(
                name: "FnFiscalYears");

            migrationBuilder.DropIndex(
                name: "IX_FinAccountMappings_AccountId",
                table: "FinAccountMappings");

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1700);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1701);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1900);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DropColumn(
                name: "Iban",
                table: "ProcSupplierProfiles");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "FinAccountMappings");

            migrationBuilder.DropColumn(
                name: "CurrencyAmount",
                table: "FinAccountingEventLines");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "FinAccountingEventLines");

            migrationBuilder.DropColumn(
                name: "PartyId",
                table: "FinAccountingEventLines");

            migrationBuilder.DropColumn(
                name: "PartyType",
                table: "FinAccountingEventLines");

            migrationBuilder.DropColumn(
                name: "Rate",
                table: "FinAccountingEventLines");
        }
    }
}
