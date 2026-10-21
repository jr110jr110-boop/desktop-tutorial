using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class FinanceAssetsPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCapital",
                table: "ProcPurchaseOrderLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PayrollRunId",
                table: "FnPayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsProduction",
                table: "FinCostCenters",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FnAssetClasses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AssetAccountCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AccumulatedAccountCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Method = table.Column<int>(type: "int", nullable: false),
                    RatePercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    UsefulLifeYears = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    StartRule = table.Column<int>(type: "int", nullable: false),
                    IdleRatePercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IdleAfterMonths = table.Column<int>(type: "int", nullable: false),
                    SalvagePercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Verify = table.Column<bool>(type: "bit", nullable: false),
                    IsIntangible = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Jurisdiction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnAssetClasses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnDepreciationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    FiscalYearId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    AssetCount = table.Column<int>(type: "int", nullable: false),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AccountingEventId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnDepreciationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnDepreciationRuns_FnPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "FnPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnInsuranceWorkshops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmployerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsContractor = table.Column<bool>(type: "bit", nullable: false),
                    ContractRow = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RateSet = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnInsuranceWorkshops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnLeaveBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    CarriedForward = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Entitled = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Used = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    LastMonth = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnLeaveBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnOutboxPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: true),
                    Season = table.Column<int>(type: "int", nullable: true),
                    WorkshopId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    FileDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersededByPackageId = table.Column<int>(type: "int", nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnOutboxPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnPayrollItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Insurable = table.Column<bool>(type: "bit", nullable: false),
                    Taxable = table.Column<bool>(type: "bit", nullable: false),
                    Calculation = table.Column<int>(type: "int", nullable: false),
                    RateKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Variant = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ItemCodes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Formula = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Factor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Factor2 = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ProRata = table.Column<bool>(type: "bit", nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReducesTaxable = table.Column<bool>(type: "bit", nullable: false),
                    AccountRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Jurisdiction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Verify = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPayrollItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnPayrollRateTables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Verify = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPayrollRateTables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnPayrollRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    WorkshopId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedByUserId = table.Column<int>(type: "int", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EmployeeCount = table.Column<int>(type: "int", nullable: false),
                    TotalGross = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalInsurable = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalTaxable = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalEmployeeInsurance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalEmployerInsurance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalTax = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalOtherDeductions = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalNet = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalProvisions = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    AccountingEventId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPayrollRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnTaxBrackets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    FromAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ToAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: true),
                    RatePercent = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnTaxBrackets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ClassId = table.Column<int>(type: "int", nullable: true),
                    Serial = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Maker = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CustodianEmployeeId = table.Column<int>(type: "int", nullable: true),
                    CustodianTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    MachineId = table.Column<int>(type: "int", nullable: true),
                    AcquisitionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InUseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    SalvageValue = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    SourceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OffsetAccountCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OpeningAccumulated = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    AccumulatedDepreciation = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    DepreciatedThrough = table.Column<DateTime>(type: "datetime2", nullable: true),
                    YearStartAccumulated = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    YearStartYear = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DisposedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnAssets_FnAssetClasses_ClassId",
                        column: x => x.ClassId,
                        principalTable: "FnAssetClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnEmployees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonnelCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FatherName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NationalCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BirthDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BirthPlace = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Gender = table.Column<int>(type: "int", nullable: false),
                    MaritalStatus = table.Column<int>(type: "int", nullable: false),
                    ChildrenCount = table.Column<int>(type: "int", nullable: false),
                    InsuranceNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    InsuranceJobCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WorkshopId = table.Column<int>(type: "int", nullable: true),
                    HireDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmploymentType = table.Column<int>(type: "int", nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    DefaultWorkOrderId = table.Column<int>(type: "int", nullable: true),
                    BankIban = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: true),
                    DailyWage = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ExemptionFlags = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HardAndHarmful = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    QualifiedPersonId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnEmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnEmployees_FnInsuranceWorkshops_WorkshopId",
                        column: x => x.WorkshopId,
                        principalTable: "FnInsuranceWorkshops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnAssetDisposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProceedsAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ProceedsMethod = table.Column<int>(type: "int", nullable: false),
                    CashBoxId = table.Column<int>(type: "int", nullable: true),
                    BankAccountId = table.Column<int>(type: "int", nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    CatchUpDepreciation = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    AccumulatedAtDisposal = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    BookValue = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    GainLoss = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnAssetDisposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnAssetDisposals_FnAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "FnAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnAssetHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnAssetHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnAssetHistory_FnAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "FnAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnDepreciationLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    ClassId = table.Column<int>(type: "int", nullable: false),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    Months = table.Column<int>(type: "int", nullable: false),
                    IdleMonths = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    BookValueBefore = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    AccumulatedAfter = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ThroughMonth = table.Column<DateTime>(type: "datetime2", nullable: false),
                    YearStartYearAfter = table.Column<int>(type: "int", nullable: false),
                    YearStartAccumulatedAfter = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnDepreciationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnDepreciationLines_FnAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "FnAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnDepreciationLines_FnDepreciationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "FnDepreciationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnAttendanceSheets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    DaysWorked = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    OvertimeHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    NightHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    HolidayHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    ShiftType = table.Column<int>(type: "int", nullable: false),
                    PaidLeaveDays = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    UnpaidLeaveDays = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    AbsenceDays = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    MissionDays = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    InputsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnAttendanceSheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnAttendanceSheets_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnEmployeeLoans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Principal = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    InstallmentAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    FirstYear = table.Column<int>(type: "int", nullable: false),
                    FirstMonth = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnEmployeeLoans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnEmployeeLoans_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnPayslips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    WorkshopId = table.Column<int>(type: "int", nullable: true),
                    CostCenterId = table.Column<int>(type: "int", nullable: true),
                    DaysPaid = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    GrossEarnings = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    InsurableTotal = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TaxableTotal = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    EmployeeInsurance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    EmployerInsurance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    OtherDeductions = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Provisions = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Net = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPayslips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPayslips_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnPayslips_FnPayrollRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "FnPayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnAttendanceWorkOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SheetId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnAttendanceWorkOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnAttendanceWorkOrders_FnAttendanceSheets_SheetId",
                        column: x => x.SheetId,
                        principalTable: "FnAttendanceSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnPayslipAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayslipId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    Share = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPayslipAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPayslipAllocations_FnPayslips_PayslipId",
                        column: x => x.PayslipId,
                        principalTable: "FnPayslips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnPayslipLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayslipId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Base = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Insurable = table.Column<bool>(type: "bit", nullable: false),
                    Taxable = table.Column<bool>(type: "bit", nullable: false),
                    AccountRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnPayslipLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnPayslipLines_FnPayslips_PayslipId",
                        column: x => x.PayslipId,
                        principalTable: "FnPayslips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[] { 1800, "permission", "Warehouse", null, null, null, null, null, null, null, null, 18 });

            migrationBuilder.CreateIndex(
                name: "IX_FnPayments_PayrollRunId",
                table: "FnPayments",
                column: "PayrollRunId",
                filter: "[PayrollRunId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FnAssetClasses_Code",
                table: "FnAssetClasses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnAssetDisposals_AssetId",
                table: "FnAssetDisposals",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_FnAssetDisposals_Number",
                table: "FnAssetDisposals",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnAssetHistory_AssetId",
                table: "FnAssetHistory",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_FnAssets_ClassId",
                table: "FnAssets",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_FnAssets_Code",
                table: "FnAssets",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnAssets_Source_SourceId",
                table: "FnAssets",
                columns: new[] { "Source", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_FnAssets_Status",
                table: "FnAssets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FnAttendanceSheets_EmployeeId_Year_Month",
                table: "FnAttendanceSheets",
                columns: new[] { "EmployeeId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnAttendanceWorkOrders_SheetId_WorkOrderId",
                table: "FnAttendanceWorkOrders",
                columns: new[] { "SheetId", "WorkOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnDepreciationLines_AssetId",
                table: "FnDepreciationLines",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_FnDepreciationLines_RunId_AssetId",
                table: "FnDepreciationLines",
                columns: new[] { "RunId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnDepreciationRuns_Number",
                table: "FnDepreciationRuns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnDepreciationRuns_PeriodId",
                table: "FnDepreciationRuns",
                column: "PeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnEmployeeLoans_EmployeeId",
                table: "FnEmployeeLoans",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_FnEmployees_NationalCode",
                table: "FnEmployees",
                column: "NationalCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnEmployees_PersonnelCode",
                table: "FnEmployees",
                column: "PersonnelCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnEmployees_WorkshopId",
                table: "FnEmployees",
                column: "WorkshopId");

            migrationBuilder.CreateIndex(
                name: "IX_FnInsuranceWorkshops_Code",
                table: "FnInsuranceWorkshops",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnLeaveBalances_EmployeeId_Year",
                table: "FnLeaveBalances",
                columns: new[] { "EmployeeId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnOutboxPackages_Kind_Year_Month_Season_WorkshopId",
                table: "FnOutboxPackages",
                columns: new[] { "Kind", "Year", "Month", "Season", "WorkshopId" });

            migrationBuilder.CreateIndex(
                name: "IX_FnOutboxPackages_Number",
                table: "FnOutboxPackages",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnOutboxPackages_Status",
                table: "FnOutboxPackages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FnPayrollItems_Code",
                table: "FnPayrollItems",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnPayrollRateTables_Year_Key",
                table: "FnPayrollRateTables",
                columns: new[] { "Year", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnPayrollRuns_Number",
                table: "FnPayrollRuns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnPayrollRuns_Year_Month_WorkshopId",
                table: "FnPayrollRuns",
                columns: new[] { "Year", "Month", "WorkshopId" },
                unique: true,
                filter: "[WorkshopId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FnPayslipAllocations_PayslipId",
                table: "FnPayslipAllocations",
                column: "PayslipId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPayslipLines_PayslipId",
                table: "FnPayslipLines",
                column: "PayslipId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPayslips_EmployeeId",
                table: "FnPayslips",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_FnPayslips_RunId_EmployeeId",
                table: "FnPayslips",
                columns: new[] { "RunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnTaxBrackets_Year_FromAmount",
                table: "FnTaxBrackets",
                columns: new[] { "Year", "FromAmount" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FnAssetDisposals");

            migrationBuilder.DropTable(
                name: "FnAssetHistory");

            migrationBuilder.DropTable(
                name: "FnAttendanceWorkOrders");

            migrationBuilder.DropTable(
                name: "FnDepreciationLines");

            migrationBuilder.DropTable(
                name: "FnEmployeeLoans");

            migrationBuilder.DropTable(
                name: "FnLeaveBalances");

            migrationBuilder.DropTable(
                name: "FnOutboxPackages");

            migrationBuilder.DropTable(
                name: "FnPayrollItems");

            migrationBuilder.DropTable(
                name: "FnPayrollRateTables");

            migrationBuilder.DropTable(
                name: "FnPayslipAllocations");

            migrationBuilder.DropTable(
                name: "FnPayslipLines");

            migrationBuilder.DropTable(
                name: "FnTaxBrackets");

            migrationBuilder.DropTable(
                name: "FnAttendanceSheets");

            migrationBuilder.DropTable(
                name: "FnAssets");

            migrationBuilder.DropTable(
                name: "FnDepreciationRuns");

            migrationBuilder.DropTable(
                name: "FnPayslips");

            migrationBuilder.DropTable(
                name: "FnAssetClasses");

            migrationBuilder.DropTable(
                name: "FnEmployees");

            migrationBuilder.DropTable(
                name: "FnPayrollRuns");

            migrationBuilder.DropTable(
                name: "FnInsuranceWorkshops");

            migrationBuilder.DropIndex(
                name: "IX_FnPayments_PayrollRunId",
                table: "FnPayments");

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1800);

            migrationBuilder.DropColumn(
                name: "IsCapital",
                table: "ProcPurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "PayrollRunId",
                table: "FnPayments");

            migrationBuilder.DropColumn(
                name: "IsProduction",
                table: "FinCostCenters");
        }
    }
}
