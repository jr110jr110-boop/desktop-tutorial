using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class SalesService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ServiceRequestId",
                table: "QaComplaints",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesOrderId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServiceOrderId",
                table: "InvDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesOrderLineId",
                table: "InvDocumentLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SlContacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlContacts_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlCustomerLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    TenderId = table.Column<int>(type: "int", nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    LinkedByUserId = table.Column<int>(type: "int", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlCustomerLinks", x => x.Id);
                    table.CheckConstraint("CK_SlCustomerLinks_OneSource", "([ContractId] IS NULL AND [TenderId] IS NOT NULL) OR ([ContractId] IS NOT NULL AND [TenderId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_SlCustomerLinks_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlCustomerLinks_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlCustomerLinks_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlCustomerProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: true),
                    Industry = table.Column<int>(type: "int", nullable: true),
                    Country = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true),
                    CreditLimitRial = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    CreditSetByUserId = table.Column<int>(type: "int", nullable: true),
                    CreditSetAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaymentTermDays = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlCustomerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlCustomerProfiles_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlPriceLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlPriceLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SlReasonCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlReasonCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SlSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SlSites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CoordinatesText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlSites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlSites_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SvContracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    VisitIntervalMonths = table.Column<int>(type: "int", nullable: false),
                    PriceRial = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    NextVisitDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActivatedByUserId = table.Column<int>(type: "int", nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_SvContracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SvContracts_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SvSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SvSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SvSlaRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ResponseHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    ResolutionHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SvSlaRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SlInquiries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReceivedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    ContactId = table.Column<int>(type: "int", nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CustomerReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ProductType = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RequiredDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProposalDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttachmentIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TenderId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_SlInquiries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlInquiries_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlInquiries_SlContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "SlContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlInquiries_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlPriceListLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PriceListId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: false),
                    MinQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlPriceListLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlPriceListLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlPriceListLines_SlPriceLists_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "SlPriceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SvInstalledUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tag = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SerialUnitId = table.Column<int>(type: "int", nullable: true),
                    ProductType = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    SiteId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ShipmentId = table.Column<int>(type: "int", nullable: true),
                    HandoverDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CommissioningDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WarrantyMonths = table.Column<int>(type: "int", nullable: true),
                    WarrantyStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WarrantyEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WarrantySource = table.Column<int>(type: "int", nullable: false),
                    RunningHours = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RunningHoursAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Decommissioned = table.Column<bool>(type: "bit", nullable: false),
                    DecommissionedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_SvInstalledUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SvInstalledUnits_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvInstalledUnits_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvInstalledUnits_InvSerialUnits_SerialUnitId",
                        column: x => x.SerialUnitId,
                        principalTable: "InvSerialUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvInstalledUnits_PcShipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "PcShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvInstalledUnits_SlSites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "SlSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvInstalledUnits_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlBidDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InquiryId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TechnicalCapabilityOk = table.Column<bool>(type: "bit", nullable: true),
                    TechnicalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CapacityOverloadWeeks = table.Column<int>(type: "int", nullable: true),
                    CapacityCheckedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CapacityCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CapacityNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CommercialRiskReviewed = table.Column<bool>(type: "bit", nullable: false),
                    CommercialRiskNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PaymentTermsReviewed = table.Column<bool>(type: "bit", nullable: false),
                    PaymentTermsNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GuaranteesReviewed = table.Column<bool>(type: "bit", nullable: false),
                    GuaranteesNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SanctionCheck = table.Column<int>(type: "int", nullable: false),
                    SanctionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExpectedMarginPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlBidDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlBidDecisions_SlInquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "SlInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlEstimates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InquiryId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OverheadPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    ContingencyPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    TargetMarginPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    GuaranteeAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    GuaranteeMonths = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    GuaranteeRatePercentPerMonth = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    FinancingAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    FinancingMonths = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    FinancingRatePercentPerMonth = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    MaterialCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    LabourCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    EngineeringCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    SubcontractCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    PackingTransportCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    DirectCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    GuaranteeCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    FinancingCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    RiskCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ContingencyCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    CostBeforeOverhead = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    OverheadCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    MissingCostLines = table.Column<int>(type: "int", nullable: false),
                    MissingInputs = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FinalizedByUserId = table.Column<int>(type: "int", nullable: true),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlEstimates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlEstimates_SlInquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "SlInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SvContractUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SvContractUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SvContractUnits_SvContracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "SvContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SvContractUnits_SvInstalledUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "SvInstalledUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SvServiceRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    ContactId = table.Column<int>(type: "int", nullable: true),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ProductNonconformity = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResponseDue = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionDue = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignedToUserId = table.Column<int>(type: "int", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FirstResponseAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_SvServiceRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SvServiceRequests_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvServiceRequests_SlContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "SlContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvServiceRequests_SvInstalledUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "SvInstalledUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SvUnitSpares",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SvUnitSpares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SvUnitSpares_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvUnitSpares_SvInstalledUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "SvInstalledUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SlEstimateEquipments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EstimateId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlEstimateEquipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlEstimateEquipments_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlEstimateEquipments_SlEstimates_EstimateId",
                        column: x => x.EstimateId,
                        principalTable: "SlEstimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SlEstimateLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EstimateId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    EquipmentBomId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    WorkCenterId = table.Column<int>(type: "int", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: true),
                    CostSource = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlEstimateLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlEstimateLines_EquipmentBoms_EquipmentBomId",
                        column: x => x.EquipmentBomId,
                        principalTable: "EquipmentBoms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlEstimateLines_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlEstimateLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlEstimateLines_MfWorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "MfWorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlEstimateLines_SlEstimates_EstimateId",
                        column: x => x.EstimateId,
                        principalTable: "SlEstimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SlEstimateRisks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EstimateId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ProbabilityPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ImpactAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ExpectedCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlEstimateRisks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlEstimateRisks_SlEstimates_EstimateId",
                        column: x => x.EstimateId,
                        principalTable: "SlEstimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SlProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    InquiryId = table.Column<int>(type: "int", nullable: false),
                    EstimateId = table.Column<int>(type: "int", nullable: false),
                    TechnicalDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CommercialDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ValidityDays = table.Column<int>(type: "int", nullable: true),
                    Price = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    DeliveryWeeks = table.Column<int>(type: "int", nullable: true),
                    PaymentTermsText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GuaranteesText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DeviationsText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    VatPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    AdvancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    RetentionPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    LatePenaltyPercentPerWeek = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    PenaltyCapPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    ClientReviewDays = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EstimateTotalCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    BelowCost = table.Column<bool>(type: "bit", nullable: false),
                    EditorUserIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    CustomerReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ResultAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResultReasonId = table.Column<int>(type: "int", nullable: true),
                    Competitor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CompetitorPrice = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    LessonsLearned = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlProposals_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlProposals_SlEstimates_EstimateId",
                        column: x => x.EstimateId,
                        principalTable: "SlEstimates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlProposals_SlInquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "SlInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlProposals_SlReasonCodes_ResultReasonId",
                        column: x => x.ResultReasonId,
                        principalTable: "SlReasonCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SvServiceOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: true),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    LtsaContractId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlannedEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TravelNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CostCenterCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    CauseIsMisuse = table.Column<bool>(type: "bit", nullable: false),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GoodwillRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    GoodwillRequestedByUserId = table.Column<int>(type: "int", nullable: true),
                    GoodwillReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    WarrantyTestedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReportDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Findings = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Recommendations = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RunningHoursReading = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    LabourHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LabourAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    LabourEmployeeCount = table.Column<int>(type: "int", nullable: false),
                    LabourPayrollEmployeeCount = table.Column<int>(type: "int", nullable: false),
                    PartsAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    CostPostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WarrantyEventId = table.Column<int>(type: "int", nullable: true),
                    LabourPendingPeriod = table.Column<int>(type: "int", nullable: true),
                    LabourAggregatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BillableLabourRateRial = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    InvoiceId = table.Column<int>(type: "int", nullable: true),
                    DraftNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_SvServiceOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SvServiceOrders_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvServiceOrders_FinAccountingEvents_WarrantyEventId",
                        column: x => x.WarrantyEventId,
                        principalTable: "FinAccountingEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvServiceOrders_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvServiceOrders_FnSalesInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "FnSalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvServiceOrders_SvContracts_LtsaContractId",
                        column: x => x.LtsaContractId,
                        principalTable: "SvContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvServiceOrders_SvInstalledUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "SvInstalledUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvServiceOrders_SvServiceRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "SvServiceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    ContactId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ActivityDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NextActionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InquiryId = table.Column<int>(type: "int", nullable: true),
                    ProposalId = table.Column<int>(type: "int", nullable: true),
                    ServiceRequestId = table.Column<int>(type: "int", nullable: true),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlActivities_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlActivities_SlContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "SlContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlActivities_SlInquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "SlInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlActivities_SlProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "SlProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlActivities_SvServiceRequests_ServiceRequestId",
                        column: x => x.ServiceRequestId,
                        principalTable: "SvServiceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlSurveys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    ShipmentId = table.Column<int>(type: "int", nullable: true),
                    ServiceOrderId = table.Column<int>(type: "int", nullable: true),
                    DraftedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Comments = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AverageScore = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlSurveys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlSurveys_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlSurveys_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlSurveys_PcShipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "PcShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlSurveys_SvServiceOrders_ServiceOrderId",
                        column: x => x.ServiceOrderId,
                        principalTable: "SvServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SvMeasurements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(22,6)", precision: 22, scale: 6, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SvMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SvMeasurements_SvServiceOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "SvServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SvOrderEngineers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SvOrderEngineers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SvOrderEngineers_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvOrderEngineers_SvServiceOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "SvServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SvOrderTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsDone = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SvOrderTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SvOrderTasks_SvServiceOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "SvServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SvTimeEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    WorkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RateFromPayroll = table.Column<bool>(type: "bit", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SvTimeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SvTimeEntries_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SvTimeEntries_SvServiceOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "SvServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SlSurveyAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlSurveyAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlSurveyAnswers_SlSurveys_SurveyId",
                        column: x => x.SurveyId,
                        principalTable: "SlSurveys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SlOrderDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesOrderId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: true),
                    DraftNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Reversed = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlOrderDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlOrderDeliveries_FnSalesInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "FnSalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlOrderDeliveries_InvDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlQuotationLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuotationId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlQuotationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlQuotationLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlQuotations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    QuotationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    ContactId = table.Column<int>(type: "int", nullable: true),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PriceListId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    SalesOrderId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlQuotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlQuotations_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlQuotations_SlContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "SlContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlQuotations_SlPriceLists_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "SlPriceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlSalesOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    ContactId = table.Column<int>(type: "int", nullable: true),
                    QuotationId = table.Column<int>(type: "int", nullable: true),
                    CustomerOrderNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequestedDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    CreditApprovalRequired = table.Column<bool>(type: "bit", nullable: false),
                    CreditReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreditExcess = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    CreditApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreditApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreditApprovedNetAmount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: true),
                    CreditApprovalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_SlSalesOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlSalesOrders_FnCustomers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "FnCustomers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlSalesOrders_SlContacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "SlContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlSalesOrders_SlQuotations_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "SlQuotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlSalesOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(22,4)", precision: 22, scale: 4, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DeliveredQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InvoicedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlSalesOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlSalesOrderLines_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlSalesOrderLines_SlSalesOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "SlSalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "Description", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { 24, "00000024-0000-0000-0000-000000000000", null, null, null, null, "فروش و بازاریابی", null, null, null, null, "SALES", "SALES" },
                    { 25, "00000025-0000-0000-0000-000000000000", null, null, null, null, "خدمات پس از فروش", null, null, null, null, "SERVICE", "SERVICE" }
                });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 2400, "permission", "Warehouse", null, null, null, null, null, null, null, null, 24 },
                    { 2401, "permission", "Contract", null, null, null, null, null, null, null, null, 24 },
                    { 2500, "permission", "Warehouse", null, null, null, null, null, null, null, null, 25 },
                    { 2501, "permission", "Traceability", null, null, null, null, null, null, null, null, 25 },
                    { 2502, "permission", "Contract", null, null, null, null, null, null, null, null, 25 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_QaComplaints_ServiceRequestId",
                table: "QaComplaints",
                column: "ServiceRequestId",
                unique: true,
                filter: "[ServiceRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_SalesOrderId",
                table: "InvDocuments",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocuments_ServiceOrderId",
                table: "InvDocuments",
                column: "ServiceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvDocumentLines_SalesOrderLineId",
                table: "InvDocumentLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SlActivities_ContactId",
                table: "SlActivities",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_SlActivities_CustomerId",
                table: "SlActivities",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SlActivities_InquiryId",
                table: "SlActivities",
                column: "InquiryId");

            migrationBuilder.CreateIndex(
                name: "IX_SlActivities_ProposalId",
                table: "SlActivities",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_SlActivities_ServiceRequestId",
                table: "SlActivities",
                column: "ServiceRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_SlBidDecisions_InquiryId",
                table: "SlBidDecisions",
                column: "InquiryId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlBidDecisions_Status",
                table: "SlBidDecisions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SlContacts_CustomerId",
                table: "SlContacts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SlCustomerLinks_ContractId",
                table: "SlCustomerLinks",
                column: "ContractId",
                unique: true,
                filter: "[Deleted] = 0 AND [ContractId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SlCustomerLinks_CustomerId",
                table: "SlCustomerLinks",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SlCustomerLinks_TenderId",
                table: "SlCustomerLinks",
                column: "TenderId",
                unique: true,
                filter: "[Deleted] = 0 AND [TenderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SlCustomerProfiles_CustomerId",
                table: "SlCustomerProfiles",
                column: "CustomerId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlCustomerProfiles_OwnerUserId",
                table: "SlCustomerProfiles",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SlEstimateEquipments_EquipmentId",
                table: "SlEstimateEquipments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SlEstimateEquipments_EstimateId",
                table: "SlEstimateEquipments",
                column: "EstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_SlEstimateLines_EquipmentBomId",
                table: "SlEstimateLines",
                column: "EquipmentBomId");

            migrationBuilder.CreateIndex(
                name: "IX_SlEstimateLines_EquipmentId",
                table: "SlEstimateLines",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SlEstimateLines_EstimateId",
                table: "SlEstimateLines",
                column: "EstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_SlEstimateLines_ItemId",
                table: "SlEstimateLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SlEstimateLines_WorkCenterId",
                table: "SlEstimateLines",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_SlEstimateRisks_EstimateId",
                table: "SlEstimateRisks",
                column: "EstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_SlEstimates_InquiryId_Revision",
                table: "SlEstimates",
                columns: new[] { "InquiryId", "Revision" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlEstimates_Status",
                table: "SlEstimates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SlInquiries_ContactId",
                table: "SlInquiries",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_SlInquiries_CustomerId",
                table: "SlInquiries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SlInquiries_Number",
                table: "SlInquiries",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlInquiries_Status",
                table: "SlInquiries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SlInquiries_TenderId",
                table: "SlInquiries",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_SlOrderDeliveries_DocumentId",
                table: "SlOrderDeliveries",
                column: "DocumentId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlOrderDeliveries_InvoiceId",
                table: "SlOrderDeliveries",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SlOrderDeliveries_SalesOrderId",
                table: "SlOrderDeliveries",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SlPriceListLines_ItemId",
                table: "SlPriceListLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SlPriceListLines_PriceListId",
                table: "SlPriceListLines",
                column: "PriceListId");

            migrationBuilder.CreateIndex(
                name: "IX_SlPriceLists_Number",
                table: "SlPriceLists",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlProposals_ContractId",
                table: "SlProposals",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_SlProposals_EstimateId",
                table: "SlProposals",
                column: "EstimateId");

            migrationBuilder.CreateIndex(
                name: "IX_SlProposals_InquiryId",
                table: "SlProposals",
                column: "InquiryId");

            migrationBuilder.CreateIndex(
                name: "IX_SlProposals_Number_Revision",
                table: "SlProposals",
                columns: new[] { "Number", "Revision" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlProposals_ResultReasonId",
                table: "SlProposals",
                column: "ResultReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_SlProposals_Status",
                table: "SlProposals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SlQuotationLines_ItemId",
                table: "SlQuotationLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SlQuotationLines_QuotationId",
                table: "SlQuotationLines",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_SlQuotations_ContactId",
                table: "SlQuotations",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_SlQuotations_CustomerId",
                table: "SlQuotations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SlQuotations_Number",
                table: "SlQuotations",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlQuotations_PriceListId",
                table: "SlQuotations",
                column: "PriceListId");

            migrationBuilder.CreateIndex(
                name: "IX_SlQuotations_SalesOrderId",
                table: "SlQuotations",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SlQuotations_Status",
                table: "SlQuotations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SlReasonCodes_Kind_Code",
                table: "SlReasonCodes",
                columns: new[] { "Kind", "Code" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlSalesOrderLines_ItemId",
                table: "SlSalesOrderLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SlSalesOrderLines_OrderId",
                table: "SlSalesOrderLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SlSalesOrders_ContactId",
                table: "SlSalesOrders",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_SlSalesOrders_CustomerId",
                table: "SlSalesOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SlSalesOrders_Number",
                table: "SlSalesOrders",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlSalesOrders_QuotationId",
                table: "SlSalesOrders",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_SlSalesOrders_Status",
                table: "SlSalesOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SlSettings_Key",
                table: "SlSettings",
                column: "Key",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlSites_CustomerId",
                table: "SlSites",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SlSurveyAnswers_SurveyId",
                table: "SlSurveyAnswers",
                column: "SurveyId");

            migrationBuilder.CreateIndex(
                name: "IX_SlSurveys_ContractId",
                table: "SlSurveys",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_SlSurveys_CustomerId",
                table: "SlSurveys",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SlSurveys_Number",
                table: "SlSurveys",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SlSurveys_ServiceOrderId",
                table: "SlSurveys",
                column: "ServiceOrderId",
                unique: true,
                filter: "[Deleted] = 0 AND [ServiceOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SlSurveys_ShipmentId",
                table: "SlSurveys",
                column: "ShipmentId",
                unique: true,
                filter: "[Deleted] = 0 AND [ShipmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SlSurveys_Status",
                table: "SlSurveys",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SvContracts_CustomerId",
                table: "SvContracts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SvContracts_Number",
                table: "SvContracts",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SvContracts_Status",
                table: "SvContracts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SvContractUnits_ContractId_UnitId",
                table: "SvContractUnits",
                columns: new[] { "ContractId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SvContractUnits_UnitId",
                table: "SvContractUnits",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SvInstalledUnits_ContractId",
                table: "SvInstalledUnits",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_SvInstalledUnits_CustomerId",
                table: "SvInstalledUnits",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SvInstalledUnits_SerialUnitId",
                table: "SvInstalledUnits",
                column: "SerialUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SvInstalledUnits_ShipmentId",
                table: "SvInstalledUnits",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SvInstalledUnits_SiteId",
                table: "SvInstalledUnits",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_SvInstalledUnits_Tag",
                table: "SvInstalledUnits",
                column: "Tag",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SvInstalledUnits_WorkOrderId",
                table: "SvInstalledUnits",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SvMeasurements_OrderId",
                table: "SvMeasurements",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SvOrderEngineers_EmployeeId",
                table: "SvOrderEngineers",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SvOrderEngineers_OrderId_EmployeeId",
                table: "SvOrderEngineers",
                columns: new[] { "OrderId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SvOrderTasks_OrderId",
                table: "SvOrderTasks",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_ContractId",
                table: "SvServiceOrders",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_CustomerId",
                table: "SvServiceOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_InvoiceId",
                table: "SvServiceOrders",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_LabourPendingPeriod",
                table: "SvServiceOrders",
                column: "LabourPendingPeriod",
                filter: "[LabourPendingPeriod] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_LtsaContractId_UnitId_PlannedStart",
                table: "SvServiceOrders",
                columns: new[] { "LtsaContractId", "UnitId", "PlannedStart" },
                unique: true,
                filter: "[Deleted] = 0 AND [LtsaContractId] IS NOT NULL AND [Status] <> 5");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_Number",
                table: "SvServiceOrders",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_RequestId",
                table: "SvServiceOrders",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_Status",
                table: "SvServiceOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_UnitId",
                table: "SvServiceOrders",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceOrders_WarrantyEventId",
                table: "SvServiceOrders",
                column: "WarrantyEventId");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceRequests_ContactId",
                table: "SvServiceRequests",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceRequests_CustomerId",
                table: "SvServiceRequests",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceRequests_Number",
                table: "SvServiceRequests",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceRequests_Status",
                table: "SvServiceRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SvServiceRequests_UnitId",
                table: "SvServiceRequests",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SvSettings_Key",
                table: "SvSettings",
                column: "Key",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SvSlaRules_Priority",
                table: "SvSlaRules",
                column: "Priority",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SvTimeEntries_EmployeeId",
                table: "SvTimeEntries",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SvTimeEntries_OrderId",
                table: "SvTimeEntries",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SvUnitSpares_ItemId",
                table: "SvUnitSpares",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SvUnitSpares_UnitId",
                table: "SvUnitSpares",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvDocumentLines_SlSalesOrderLines_SalesOrderLineId",
                table: "InvDocumentLines",
                column: "SalesOrderLineId",
                principalTable: "SlSalesOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvDocuments_SlSalesOrders_SalesOrderId",
                table: "InvDocuments",
                column: "SalesOrderId",
                principalTable: "SlSalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvDocuments_SvServiceOrders_ServiceOrderId",
                table: "InvDocuments",
                column: "ServiceOrderId",
                principalTable: "SvServiceOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QaComplaints_SvServiceRequests_ServiceRequestId",
                table: "QaComplaints",
                column: "ServiceRequestId",
                principalTable: "SvServiceRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SlOrderDeliveries_SlSalesOrders_SalesOrderId",
                table: "SlOrderDeliveries",
                column: "SalesOrderId",
                principalTable: "SlSalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SlQuotationLines_SlQuotations_QuotationId",
                table: "SlQuotationLines",
                column: "QuotationId",
                principalTable: "SlQuotations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SlQuotations_SlSalesOrders_SalesOrderId",
                table: "SlQuotations",
                column: "SalesOrderId",
                principalTable: "SlSalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvDocumentLines_SlSalesOrderLines_SalesOrderLineId",
                table: "InvDocumentLines");

            migrationBuilder.DropForeignKey(
                name: "FK_InvDocuments_SlSalesOrders_SalesOrderId",
                table: "InvDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_InvDocuments_SvServiceOrders_ServiceOrderId",
                table: "InvDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_QaComplaints_SvServiceRequests_ServiceRequestId",
                table: "QaComplaints");

            migrationBuilder.DropForeignKey(
                name: "FK_SlQuotations_SlContacts_ContactId",
                table: "SlQuotations");

            migrationBuilder.DropForeignKey(
                name: "FK_SlSalesOrders_SlContacts_ContactId",
                table: "SlSalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_SlQuotations_SlSalesOrders_SalesOrderId",
                table: "SlQuotations");

            migrationBuilder.DropTable(
                name: "SlActivities");

            migrationBuilder.DropTable(
                name: "SlBidDecisions");

            migrationBuilder.DropTable(
                name: "SlCustomerLinks");

            migrationBuilder.DropTable(
                name: "SlCustomerProfiles");

            migrationBuilder.DropTable(
                name: "SlEstimateEquipments");

            migrationBuilder.DropTable(
                name: "SlEstimateLines");

            migrationBuilder.DropTable(
                name: "SlEstimateRisks");

            migrationBuilder.DropTable(
                name: "SlOrderDeliveries");

            migrationBuilder.DropTable(
                name: "SlPriceListLines");

            migrationBuilder.DropTable(
                name: "SlQuotationLines");

            migrationBuilder.DropTable(
                name: "SlSalesOrderLines");

            migrationBuilder.DropTable(
                name: "SlSettings");

            migrationBuilder.DropTable(
                name: "SlSurveyAnswers");

            migrationBuilder.DropTable(
                name: "SvContractUnits");

            migrationBuilder.DropTable(
                name: "SvMeasurements");

            migrationBuilder.DropTable(
                name: "SvOrderEngineers");

            migrationBuilder.DropTable(
                name: "SvOrderTasks");

            migrationBuilder.DropTable(
                name: "SvSettings");

            migrationBuilder.DropTable(
                name: "SvSlaRules");

            migrationBuilder.DropTable(
                name: "SvTimeEntries");

            migrationBuilder.DropTable(
                name: "SvUnitSpares");

            migrationBuilder.DropTable(
                name: "SlProposals");

            migrationBuilder.DropTable(
                name: "SlSurveys");

            migrationBuilder.DropTable(
                name: "SlEstimates");

            migrationBuilder.DropTable(
                name: "SlReasonCodes");

            migrationBuilder.DropTable(
                name: "SvServiceOrders");

            migrationBuilder.DropTable(
                name: "SlInquiries");

            migrationBuilder.DropTable(
                name: "SvContracts");

            migrationBuilder.DropTable(
                name: "SvServiceRequests");

            migrationBuilder.DropTable(
                name: "SvInstalledUnits");

            migrationBuilder.DropTable(
                name: "SlSites");

            migrationBuilder.DropTable(
                name: "SlContacts");

            migrationBuilder.DropTable(
                name: "SlSalesOrders");

            migrationBuilder.DropTable(
                name: "SlQuotations");

            migrationBuilder.DropTable(
                name: "SlPriceLists");

            migrationBuilder.DropIndex(
                name: "IX_QaComplaints_ServiceRequestId",
                table: "QaComplaints");

            migrationBuilder.DropIndex(
                name: "IX_InvDocuments_SalesOrderId",
                table: "InvDocuments");

            migrationBuilder.DropIndex(
                name: "IX_InvDocuments_ServiceOrderId",
                table: "InvDocuments");

            migrationBuilder.DropIndex(
                name: "IX_InvDocumentLines_SalesOrderLineId",
                table: "InvDocumentLines");

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2400);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2401);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2500);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2501);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2502);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 25);

            migrationBuilder.DropColumn(
                name: "ServiceRequestId",
                table: "QaComplaints");

            migrationBuilder.DropColumn(
                name: "SalesOrderId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "ServiceOrderId",
                table: "InvDocuments");

            migrationBuilder.DropColumn(
                name: "SalesOrderLineId",
                table: "InvDocumentLines");
        }
    }
}
