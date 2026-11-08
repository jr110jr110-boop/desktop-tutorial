using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class SpecialProcessesPC : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseGroupNo",
                table: "QaWps",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiameterMax",
                table: "QaWps",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiameterMin",
                table: "QaWps",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RangeDiameterMax",
                table: "QaQualifications",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RangeDiameterMin",
                table: "QaQualifications",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RangeFNos",
                table: "QaQualifications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RangeGroupNos",
                table: "QaQualifications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RangePNos",
                table: "QaQualifications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RangePositions",
                table: "QaQualifications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RangeThicknessMax",
                table: "QaQualifications",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RangeThicknessMin",
                table: "QaQualifications",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "QaMaterialCertificates",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EvaluatedAt",
                table: "QaMaterialCertificates",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Evaluation",
                table: "QaMaterialCertificates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EvaluationText",
                table: "QaMaterialCertificates",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Grade",
                table: "QaMaterialCertificates",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeatNumber",
                table: "QaMaterialCertificates",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InspectorName",
                table: "QaMaterialCertificates",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InspectorOrganisation",
                table: "QaMaterialCertificates",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InspectorStampNo",
                table: "QaMaterialCertificates",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManufacturerSignatory",
                table: "QaMaterialCertificates",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpecId",
                table: "QaMaterialCertificates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Standard",
                table: "QaMaterialCertificates",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSpecialProcess",
                table: "MfOperationStandards",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SpecialProcessTypeId",
                table: "MfOperationStandards",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSpecialProcess",
                table: "MfOperations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SpecialProcessTypeId",
                table: "MfOperations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QaCertificateValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CertificateId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Min = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Max = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaCertificateValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaCertificateValues_QaMaterialCertificates_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "QaMaterialCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaMaterialSpecs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Grade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Standard = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SpecKey = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RequiredCertificateType = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SupersedesId = table.Column<int>(type: "int", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaMaterialSpecs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaSpProcessTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequiresMachine = table.Column<bool>(type: "bit", nullable: false),
                    RequiresSurvey = table.Column<bool>(type: "bit", nullable: false),
                    RequiredInstrumentClass = table.Column<int>(type: "int", nullable: true),
                    ProcedureDeliverableId = table.Column<int>(type: "int", nullable: true),
                    ExistingControl = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSpProcessTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaMaterialSpecItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpecId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaMaterialSpecItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaMaterialSpecItems_QaMaterialSpecs_SpecId",
                        column: x => x.SpecId,
                        principalTable: "QaMaterialSpecs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaMaterialSpecLimits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpecId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Min = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Max = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Required = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaMaterialSpecLimits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaMaterialSpecLimits_QaMaterialSpecs_SpecId",
                        column: x => x.SpecId,
                        principalTable: "QaMaterialSpecs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaSpParameters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Min = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Max = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Required = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSpParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaSpParameters_QaSpProcessTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "QaSpProcessTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaSpQualifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProcedureDeliverableId = table.Column<int>(type: "int", nullable: false),
                    ProcedureRevisionId = table.Column<int>(type: "int", nullable: false),
                    ProcedureNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProcedureRevisionCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidedByUserId = table.Column<int>(type: "int", nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SupersedesId = table.Column<int>(type: "int", nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSpQualifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaSpQualifications_QaSpProcessTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "QaSpProcessTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaSpSupplierApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AccreditationNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EvidenceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AuditDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidedByUserId = table.Column<int>(type: "int", nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSpSupplierApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaSpSupplierApprovals_QaSpProcessTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "QaSpProcessTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaSpTypeChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSpTypeChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaSpTypeChanges_QaSpProcessTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "QaSpProcessTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaSpQualEquipments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QualificationId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: true),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    SurveyKind = table.Column<int>(type: "int", nullable: false),
                    SurveyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SurveyDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SurveyDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SurveyNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSpQualEquipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaSpQualEquipments_QaSpQualifications_QualificationId",
                        column: x => x.QualificationId,
                        principalTable: "QaSpQualifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaSpQualLimits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QualificationId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Min = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Max = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Required = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSpQualLimits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaSpQualLimits_QaSpQualifications_QualificationId",
                        column: x => x.QualificationId,
                        principalTable: "QaSpQualifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaSpQualOperators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QualificationId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSpQualOperators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaSpQualOperators_QaSpQualifications_QualificationId",
                        column: x => x.QualificationId,
                        principalTable: "QaSpQualifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaSpRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    QualificationId = table.Column<int>(type: "int", nullable: true),
                    IsSubcontract = table.Column<bool>(type: "bit", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    ReceiptDocumentId = table.Column<int>(type: "int", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    OperationId = table.Column<int>(type: "int", nullable: true),
                    PartOperationId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    SerialNumbers = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OperatorPersonId = table.Column<int>(type: "int", nullable: true),
                    MachineId = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChartDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChartFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    CertificateDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CertificateNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Result = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewStatus = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NcrId = table.Column<int>(type: "int", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidedByUserId = table.Column<int>(type: "int", nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResultText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSpRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaSpRuns_QaSpProcessTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "QaSpProcessTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaSpRuns_QaSpQualifications_QualificationId",
                        column: x => x.QualificationId,
                        principalTable: "QaSpQualifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaSpRunInstruments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSpRunInstruments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaSpRunInstruments_QaSpRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "QaSpRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaSpRunValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Min = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Max = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Pass = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaSpRunValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaSpRunValues_QaSpRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "QaSpRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QaCertificateValues_CertificateId_Kind_Name",
                table: "QaCertificateValues",
                columns: new[] { "CertificateId", "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaMaterialSpecItems_ItemId",
                table: "QaMaterialSpecItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QaMaterialSpecItems_SpecId_ItemId",
                table: "QaMaterialSpecItems",
                columns: new[] { "SpecId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaMaterialSpecLimits_SpecId_Kind_Name",
                table: "QaMaterialSpecLimits",
                columns: new[] { "SpecId", "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaMaterialSpecs_SpecKey_Version",
                table: "QaMaterialSpecs",
                columns: new[] { "SpecKey", "Version" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_QaMaterialSpecs_ApprovedPerKey",
                table: "QaMaterialSpecs",
                column: "SpecKey",
                unique: true,
                filter: "[Deleted] = 0 AND [Status] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpParameters_TypeId_Name",
                table: "QaSpParameters",
                columns: new[] { "TypeId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaSpProcessTypes_Code",
                table: "QaSpProcessTypes",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpQualEquipments_EquipmentId",
                table: "QaSpQualEquipments",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpQualEquipments_MachineId",
                table: "QaSpQualEquipments",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpQualEquipments_QualificationId",
                table: "QaSpQualEquipments",
                column: "QualificationId");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpQualifications_Number",
                table: "QaSpQualifications",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpQualifications_TypeId_Status",
                table: "QaSpQualifications",
                columns: new[] { "TypeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QaSpQualifications_ValidTo",
                table: "QaSpQualifications",
                column: "ValidTo");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpQualLimits_QualificationId_Name",
                table: "QaSpQualLimits",
                columns: new[] { "QualificationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaSpQualOperators_QualificationId_PersonId",
                table: "QaSpQualOperators",
                columns: new[] { "QualificationId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaSpRunInstruments_RunId_EquipmentId",
                table: "QaSpRunInstruments",
                columns: new[] { "RunId", "EquipmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaSpRuns_Number",
                table: "QaSpRuns",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpRuns_OperationId",
                table: "QaSpRuns",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpRuns_PartOperationId",
                table: "QaSpRuns",
                column: "PartOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpRuns_QualificationId",
                table: "QaSpRuns",
                column: "QualificationId");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpRuns_ReceiptDocumentId",
                table: "QaSpRuns",
                column: "ReceiptDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpRuns_TypeId_Status",
                table: "QaSpRuns",
                columns: new[] { "TypeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QaSpRunValues_RunId_Name",
                table: "QaSpRunValues",
                columns: new[] { "RunId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaSpSupplierApprovals_ProviderId_TypeId_Status",
                table: "QaSpSupplierApprovals",
                columns: new[] { "ProviderId", "TypeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QaSpSupplierApprovals_TypeId",
                table: "QaSpSupplierApprovals",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "UX_QaSpSupplierApprovals_PendingPerProcess",
                table: "QaSpSupplierApprovals",
                columns: new[] { "ProviderId", "TypeId" },
                unique: true,
                filter: "[Deleted] = 0 AND [Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaSpTypeChanges_TypeId_Status",
                table: "QaSpTypeChanges",
                columns: new[] { "TypeId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QaCertificateValues");

            migrationBuilder.DropTable(
                name: "QaMaterialSpecItems");

            migrationBuilder.DropTable(
                name: "QaMaterialSpecLimits");

            migrationBuilder.DropTable(
                name: "QaSpParameters");

            migrationBuilder.DropTable(
                name: "QaSpQualEquipments");

            migrationBuilder.DropTable(
                name: "QaSpQualLimits");

            migrationBuilder.DropTable(
                name: "QaSpQualOperators");

            migrationBuilder.DropTable(
                name: "QaSpRunInstruments");

            migrationBuilder.DropTable(
                name: "QaSpRunValues");

            migrationBuilder.DropTable(
                name: "QaSpSupplierApprovals");

            migrationBuilder.DropTable(
                name: "QaSpTypeChanges");

            migrationBuilder.DropTable(
                name: "QaMaterialSpecs");

            migrationBuilder.DropTable(
                name: "QaSpRuns");

            migrationBuilder.DropTable(
                name: "QaSpQualifications");

            migrationBuilder.DropTable(
                name: "QaSpProcessTypes");

            migrationBuilder.DropColumn(
                name: "BaseGroupNo",
                table: "QaWps");

            migrationBuilder.DropColumn(
                name: "DiameterMax",
                table: "QaWps");

            migrationBuilder.DropColumn(
                name: "DiameterMin",
                table: "QaWps");

            migrationBuilder.DropColumn(
                name: "RangeDiameterMax",
                table: "QaQualifications");

            migrationBuilder.DropColumn(
                name: "RangeDiameterMin",
                table: "QaQualifications");

            migrationBuilder.DropColumn(
                name: "RangeFNos",
                table: "QaQualifications");

            migrationBuilder.DropColumn(
                name: "RangeGroupNos",
                table: "QaQualifications");

            migrationBuilder.DropColumn(
                name: "RangePNos",
                table: "QaQualifications");

            migrationBuilder.DropColumn(
                name: "RangePositions",
                table: "QaQualifications");

            migrationBuilder.DropColumn(
                name: "RangeThicknessMax",
                table: "QaQualifications");

            migrationBuilder.DropColumn(
                name: "RangeThicknessMin",
                table: "QaQualifications");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "EvaluatedAt",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "Evaluation",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "EvaluationText",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "Grade",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "HeatNumber",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "InspectorName",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "InspectorOrganisation",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "InspectorStampNo",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "ManufacturerSignatory",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "SpecId",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "Standard",
                table: "QaMaterialCertificates");

            migrationBuilder.DropColumn(
                name: "IsSpecialProcess",
                table: "MfOperationStandards");

            migrationBuilder.DropColumn(
                name: "SpecialProcessTypeId",
                table: "MfOperationStandards");

            migrationBuilder.DropColumn(
                name: "IsSpecialProcess",
                table: "MfOperations");

            migrationBuilder.DropColumn(
                name: "SpecialProcessTypeId",
                table: "MfOperations");
        }
    }
}
