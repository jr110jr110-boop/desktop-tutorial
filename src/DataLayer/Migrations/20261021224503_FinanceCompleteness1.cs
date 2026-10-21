using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class FinanceCompleteness1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FnFiscalYears_Year",
                table: "FnFiscalYears");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CeoApprovedAt",
                table: "FnVouchers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CeoApprovedByUserId",
                table: "FnVouchers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsImported",
                table: "FnVouchers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsImported",
                table: "FnFiscalYears",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LegalEntityId",
                table: "FnFiscalYears",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "LegalEntityId",
                table: "FnBankAccounts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "LegalEntityId",
                table: "FinAccountingEvents",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "FnApprovalThresholds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinAmount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnApprovalThresholds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnAttachmentRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountId = table.Column<int>(type: "int", nullable: true),
                    VoucherType = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnAttachmentRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnAttachmentRules_FnAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnCloseChecklists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    OverrideReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OverriddenByUserId = table.Column<int>(type: "int", nullable: false),
                    OverriddenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RedItems = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnCloseChecklists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnCloseChecklists_FnPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "FnPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnDetailSepidarMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DetailAccountId = table.Column<int>(type: "int", nullable: false),
                    SepidarCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnDetailSepidarMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnDetailSepidarMaps_FnDetailAccounts_DetailAccountId",
                        column: x => x.DetailAccountId,
                        principalTable: "FnDetailAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnExportLayoutColumns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnExportLayoutColumns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnLegalEntities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Jurisdiction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BaseCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnLegalEntities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnMigrationAccountMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SepidarCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnMigrationAccountMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnMigrationAccountMaps_FnAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnParallelRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    IncludeDraft = table.Column<bool>(type: "bit", nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Matched = table.Column<int>(type: "int", nullable: false),
                    PmisOnly = table.Column<int>(type: "int", nullable: false),
                    SepidarOnly = table.Column<int>(type: "int", nullable: false),
                    Differs = table.Column<int>(type: "int", nullable: false),
                    Unexplained = table.Column<int>(type: "int", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SignedOffByUserId = table.Column<int>(type: "int", nullable: true),
                    SignedOffAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SignOffNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UnexplainedAtSignOff = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnParallelRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnParallelRuns_FnPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "FnPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnUserSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnUserSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnVoucherAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    AddedByUserId = table.Column<int>(type: "int", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnVoucherAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnVoucherAttachments_FnVouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "FnVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnVoucherExports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Vouchers = table.Column<int>(type: "int", nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    TotalDebit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    TotalCredit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExportedByUserId = table.Column<int>(type: "int", nullable: false),
                    ExportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SupersededByExportId = table.Column<int>(type: "int", nullable: true),
                    SupersedeReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnVoucherExports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnVoucherExports_FnPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "FnPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnVoucherTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DayOfMonth = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnVoucherTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FnIntercompanyPairs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EntityAId = table.Column<int>(type: "int", nullable: false),
                    EntityBId = table.Column<int>(type: "int", nullable: false),
                    AccountAId = table.Column<int>(type: "int", nullable: false),
                    AccountBId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnIntercompanyPairs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnIntercompanyPairs_FnAccounts_AccountAId",
                        column: x => x.AccountAId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnIntercompanyPairs_FnAccounts_AccountBId",
                        column: x => x.AccountBId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnIntercompanyPairs_FnLegalEntities_EntityAId",
                        column: x => x.EntityAId,
                        principalTable: "FnLegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnIntercompanyPairs_FnLegalEntities_EntityBId",
                        column: x => x.EntityBId,
                        principalTable: "FnLegalEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnParallelDifferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    AccountCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AccountTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DetailCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PmisDebit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    PmisCredit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    SepidarDebit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    SepidarCredit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExplainedByUserId = table.Column<int>(type: "int", nullable: true),
                    ExplainedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnParallelDifferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnParallelDifferences_FnParallelRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "FnParallelRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnTemplateInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnTemplateInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnTemplateInstances_FnVoucherTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "FnVoucherTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnVoucherTemplateLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Detail1Id = table.Column<int>(type: "int", nullable: true),
                    Detail2Id = table.Column<int>(type: "int", nullable: true),
                    Debit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnVoucherTemplateLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnVoucherTemplateLines_FnAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "FnAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnVoucherTemplateLines_FnVoucherTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "FnVoucherTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FnIntercompanyPosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PairId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ChargerEntityId = table.Column<int>(type: "int", nullable: false),
                    CounterAccountAId = table.Column<int>(type: "int", nullable: false),
                    CounterAccountBId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EventAId = table.Column<int>(type: "int", nullable: false),
                    EventBId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnIntercompanyPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnIntercompanyPosts_FnIntercompanyPairs_PairId",
                        column: x => x.PairId,
                        principalTable: "FnIntercompanyPairs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "Description", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "Name", "NormalizedName" },
                values: new object[] { 28, "00000028-0000-0000-0000-000000000000", null, null, null, null, "حسابرس", null, null, null, null, "AUDITOR", "AUDITOR" });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[] { 2800, "permission", "Warehouse", null, null, null, null, null, null, null, null, 28 });

            migrationBuilder.CreateIndex(
                name: "IX_FnFiscalYears_LegalEntityId_Year",
                table: "FnFiscalYears",
                columns: new[] { "LegalEntityId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnAttachmentRules_AccountId",
                table: "FnAttachmentRules",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnAuditLogs_At",
                table: "FnAuditLogs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_FnAuditLogs_EntityType_EntityId",
                table: "FnAuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_FnAuditLogs_UserId",
                table: "FnAuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FnCloseChecklists_PeriodId",
                table: "FnCloseChecklists",
                column: "PeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnDetailSepidarMaps_DetailAccountId",
                table: "FnDetailSepidarMaps",
                column: "DetailAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnExportLayoutColumns_Key",
                table: "FnExportLayoutColumns",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnIntercompanyPairs_AccountAId",
                table: "FnIntercompanyPairs",
                column: "AccountAId");

            migrationBuilder.CreateIndex(
                name: "IX_FnIntercompanyPairs_AccountBId",
                table: "FnIntercompanyPairs",
                column: "AccountBId");

            migrationBuilder.CreateIndex(
                name: "IX_FnIntercompanyPairs_EntityAId",
                table: "FnIntercompanyPairs",
                column: "EntityAId");

            migrationBuilder.CreateIndex(
                name: "IX_FnIntercompanyPairs_EntityBId",
                table: "FnIntercompanyPairs",
                column: "EntityBId");

            migrationBuilder.CreateIndex(
                name: "IX_FnIntercompanyPosts_Number",
                table: "FnIntercompanyPosts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnIntercompanyPosts_PairId",
                table: "FnIntercompanyPosts",
                column: "PairId");

            migrationBuilder.CreateIndex(
                name: "IX_FnLegalEntities_Code",
                table: "FnLegalEntities",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnMigrationAccountMaps_AccountId",
                table: "FnMigrationAccountMaps",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnMigrationAccountMaps_SepidarCode",
                table: "FnMigrationAccountMaps",
                column: "SepidarCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnParallelDifferences_RunId",
                table: "FnParallelDifferences",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_FnParallelRuns_LegalEntityId_PeriodId",
                table: "FnParallelRuns",
                columns: new[] { "LegalEntityId", "PeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_FnParallelRuns_PeriodId",
                table: "FnParallelRuns",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_FnTemplateInstances_TemplateId_PeriodId",
                table: "FnTemplateInstances",
                columns: new[] { "TemplateId", "PeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnUserSettings_UserId_Key",
                table: "FnUserSettings",
                columns: new[] { "UserId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnVoucherAttachments_VoucherId",
                table: "FnVoucherAttachments",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_FnVoucherExports_Number",
                table: "FnVoucherExports",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnVoucherExports_PeriodId_Status",
                table: "FnVoucherExports",
                columns: new[] { "PeriodId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FnVoucherTemplateLines_AccountId",
                table: "FnVoucherTemplateLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FnVoucherTemplateLines_TemplateId",
                table: "FnVoucherTemplateLines",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FnApprovalThresholds");

            migrationBuilder.DropTable(
                name: "FnAttachmentRules");

            migrationBuilder.DropTable(
                name: "FnAuditLogs");

            migrationBuilder.DropTable(
                name: "FnCloseChecklists");

            migrationBuilder.DropTable(
                name: "FnDetailSepidarMaps");

            migrationBuilder.DropTable(
                name: "FnExportLayoutColumns");

            migrationBuilder.DropTable(
                name: "FnIntercompanyPosts");

            migrationBuilder.DropTable(
                name: "FnMigrationAccountMaps");

            migrationBuilder.DropTable(
                name: "FnParallelDifferences");

            migrationBuilder.DropTable(
                name: "FnTemplateInstances");

            migrationBuilder.DropTable(
                name: "FnUserSettings");

            migrationBuilder.DropTable(
                name: "FnVoucherAttachments");

            migrationBuilder.DropTable(
                name: "FnVoucherExports");

            migrationBuilder.DropTable(
                name: "FnVoucherTemplateLines");

            migrationBuilder.DropTable(
                name: "FnIntercompanyPairs");

            migrationBuilder.DropTable(
                name: "FnParallelRuns");

            migrationBuilder.DropTable(
                name: "FnVoucherTemplates");

            migrationBuilder.DropTable(
                name: "FnLegalEntities");

            migrationBuilder.DropIndex(
                name: "IX_FnFiscalYears_LegalEntityId_Year",
                table: "FnFiscalYears");

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2800);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 28);

            migrationBuilder.DropColumn(
                name: "CeoApprovedAt",
                table: "FnVouchers");

            migrationBuilder.DropColumn(
                name: "CeoApprovedByUserId",
                table: "FnVouchers");

            migrationBuilder.DropColumn(
                name: "IsImported",
                table: "FnVouchers");

            migrationBuilder.DropColumn(
                name: "IsImported",
                table: "FnFiscalYears");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "FnFiscalYears");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "FnBankAccounts");

            migrationBuilder.DropColumn(
                name: "LegalEntityId",
                table: "FinAccountingEvents");

            migrationBuilder.CreateIndex(
                name: "IX_FnFiscalYears_Year",
                table: "FnFiscalYears",
                column: "Year",
                unique: true);
        }
    }
}
