using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class FinanceCompleteness4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NonRecoverableVatAmount",
                table: "ProcSupplierInvoices",
                type: "decimal(22,2)",
                precision: 22,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "NonRecoverableVat",
                table: "ProcSupplierInvoiceLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "FnAssetDisposals",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatPercent",
                table: "FnAssetDisposals",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "FnBankGuaranteeLimits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Limit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBankGuaranteeLimits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnBankGuarantees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: true),
                    GuaranteeNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PartyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FeeAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    RegistrationVoucherId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClosedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBankGuarantees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnBankGuarantees_FnVouchers_RegistrationVoucherId",
                        column: x => x.RegistrationVoucherId,
                        principalTable: "FnVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnBonusRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EditorUserIds = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    PayDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BonusDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CapDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MinimumDailyWage = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalGross = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalTax = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalNet = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    FromProvision = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ExpensedNow = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBonusRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnBonusRuns_FnVouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "FnVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnConfirmationLetters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    AsOf = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LedgerBalance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    StatementBalance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    OpenDocuments = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ConfirmedAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    ResponseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponseNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnConfirmationLetters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnInsuranceClearances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    LetterNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnInsuranceClearances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnRetentionReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ClearanceId = table.Column<int>(type: "int", nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnRetentionReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnRetentionReleases_FnVouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "FnVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnSupplierNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EditorUserIds = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    SupplierInvoiceId = table.Column<int>(type: "int", nullable: true),
                    NoteNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnSupplierNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnSupplierNotes_FnVouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "FnVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnSupplierStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    AsOf = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TheirBalance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    OurBalance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Reconciled = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnSupplierStatements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnTerminations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EditorUserIds = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    HireDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TerminationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    YearsOfService = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MonthlyBase = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    DailyWage = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    SeveranceDaysPerYear = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnusedLeaveDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NoticeDays = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SeverancePay = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    LeavePay = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    NoticePay = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    FromSeveranceProvision = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    FromLeaveProvision = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnTerminations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnTerminations_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnTerminations_FnVouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "FnVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnWorkOrderEstimates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    Material = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Labour = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Overhead = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Subcontract = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnWorkOrderEstimates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnBankGuaranteeEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GuaranteeId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    OldExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NewExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    FeeAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBankGuaranteeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnBankGuaranteeEvents_FnBankGuarantees_GuaranteeId",
                        column: x => x.GuaranteeId,
                        principalTable: "FnBankGuarantees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FnBankGuaranteeEvents_FnVouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "FnVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnBonusLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    DailyWage = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    EmployedDays = table.Column<int>(type: "int", nullable: false),
                    YearDays = table.Column<int>(type: "int", nullable: false),
                    Gross = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    MonthlyTaxable = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Net = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnBonusLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnBonusLines_FnBonusRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "FnBonusRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnSupplierStatementLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatementId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Credit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    MatchedKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MatchedNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnSupplierStatementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnSupplierStatementLines_FnSupplierStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "FnSupplierStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FnBankGuaranteeEvents_GuaranteeId",
                table: "FnBankGuaranteeEvents",
                column: "GuaranteeId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBankGuaranteeEvents_VoucherId",
                table: "FnBankGuaranteeEvents",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBankGuaranteeLimits_BankName",
                table: "FnBankGuaranteeLimits",
                column: "BankName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnBankGuarantees_Direction_Status",
                table: "FnBankGuarantees",
                columns: new[] { "Direction", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FnBankGuarantees_Number",
                table: "FnBankGuarantees",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnBankGuarantees_RegistrationVoucherId",
                table: "FnBankGuarantees",
                column: "RegistrationVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBonusLines_RunId_EmployeeId",
                table: "FnBonusLines",
                columns: new[] { "RunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnBonusRuns_Number",
                table: "FnBonusRuns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnBonusRuns_VoucherId",
                table: "FnBonusRuns",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_FnBonusRuns_Year",
                table: "FnBonusRuns",
                column: "Year");

            migrationBuilder.CreateIndex(
                name: "IX_FnConfirmationLetters_CustomerId",
                table: "FnConfirmationLetters",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_FnConfirmationLetters_Number",
                table: "FnConfirmationLetters",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnInsuranceClearances_Number",
                table: "FnInsuranceClearances",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnInsuranceClearances_ProviderId_ValidUntil",
                table: "FnInsuranceClearances",
                columns: new[] { "ProviderId", "ValidUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_FnRetentionReleases_Number",
                table: "FnRetentionReleases",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnRetentionReleases_ProviderId",
                table: "FnRetentionReleases",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_FnRetentionReleases_VoucherId",
                table: "FnRetentionReleases",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_FnSupplierNotes_Number",
                table: "FnSupplierNotes",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnSupplierNotes_ProviderId_Date",
                table: "FnSupplierNotes",
                columns: new[] { "ProviderId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_FnSupplierNotes_VoucherId",
                table: "FnSupplierNotes",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_FnSupplierStatementLines_StatementId",
                table: "FnSupplierStatementLines",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_FnSupplierStatements_Number",
                table: "FnSupplierStatements",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnSupplierStatements_ProviderId",
                table: "FnSupplierStatements",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_FnTerminations_EmployeeId",
                table: "FnTerminations",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_FnTerminations_Number",
                table: "FnTerminations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnTerminations_VoucherId",
                table: "FnTerminations",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_FnWorkOrderEstimates_WorkOrderId",
                table: "FnWorkOrderEstimates",
                column: "WorkOrderId",
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FnBankGuaranteeEvents");

            migrationBuilder.DropTable(
                name: "FnBankGuaranteeLimits");

            migrationBuilder.DropTable(
                name: "FnBonusLines");

            migrationBuilder.DropTable(
                name: "FnConfirmationLetters");

            migrationBuilder.DropTable(
                name: "FnInsuranceClearances");

            migrationBuilder.DropTable(
                name: "FnRetentionReleases");

            migrationBuilder.DropTable(
                name: "FnSupplierNotes");

            migrationBuilder.DropTable(
                name: "FnSupplierStatementLines");

            migrationBuilder.DropTable(
                name: "FnTerminations");

            migrationBuilder.DropTable(
                name: "FnWorkOrderEstimates");

            migrationBuilder.DropTable(
                name: "FnBankGuarantees");

            migrationBuilder.DropTable(
                name: "FnBonusRuns");

            migrationBuilder.DropTable(
                name: "FnSupplierStatements");

            migrationBuilder.DropColumn(
                name: "NonRecoverableVatAmount",
                table: "ProcSupplierInvoices");

            migrationBuilder.DropColumn(
                name: "NonRecoverableVat",
                table: "ProcSupplierInvoiceLines");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "FnAssetDisposals");

            migrationBuilder.DropColumn(
                name: "VatPercent",
                table: "FnAssetDisposals");
        }
    }
}
