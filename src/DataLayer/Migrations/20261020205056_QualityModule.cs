using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class QualityModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AffectedEquipmentId",
                table: "InvNonConformances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CapaId",
                table: "InvNonConformances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CostImpact",
                table: "InvNonConformances",
                type: "decimal(22,2)",
                precision: 22,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerReturnRef",
                table: "InvNonConformances",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "InvNonConformances",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RootCauseMethod",
                table: "InvNonConformances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceRecordNumber",
                table: "InvNonConformances",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeImpactDays",
                table: "InvNonConformances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredCertificateType",
                table: "InvItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QaAuditPrograms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaAuditPrograms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaCalibrationIntervals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentType = table.Column<int>(type: "int", nullable: false),
                    Months = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaCalibrationIntervals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaCapas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    Problem = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Containment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RootCauseMethod = table.Column<int>(type: "int", nullable: false),
                    RootCauseAnalysis = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EffectivenessCriteria = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EffectivenessDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectivenessResult = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ImplementedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    CloseNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AutoDrafted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaCapas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Department = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CurrentRevision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetentionClass = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ResponsibleUnit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaEquipment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Maker = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OwnerUnit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Range = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Accuracy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequiresCalibration = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OutOfServiceReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IntervalMonths = table.Column<int>(type: "int", nullable: false),
                    LastCalibrated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextDue = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CorrectionTableDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_QaEquipment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaItpTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ProductType = table.Column<int>(type: "int", nullable: false),
                    ApplicableCodes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Revision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaItpTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaManagementReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attendees = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MinutesDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaManagementReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaMaterialCertificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LotId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Result = table.Column<int>(type: "int", nullable: false),
                    VerifiedByUserId = table.Column<int>(type: "int", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerificationNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaMaterialCertificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaMaterialCertificates_InvLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaPersons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PersonnelCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Employer = table.Column<int>(type: "int", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    StampId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaPersons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaPersons_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaPqrs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CodeEdition = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Process = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ResultsDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Approved = table.Column<bool>(type: "bit", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaPqrs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaReleaseNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ItemsText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ChecksSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CertificateOfConformityNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_QaReleaseNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaReleaseNotes_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaRetentionRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecordType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Years = table.Column<int>(type: "int", nullable: true),
                    Permanent = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaRetentionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaSettings",
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
                    table.PrimaryKey("PK_QaSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaWps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Revision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Process = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BasePNo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FillerFNo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FillerANo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ThicknessMin = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    ThicknessMax = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    Positions = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PreheatMinC = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    InterpassMaxC = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    Pwht = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ShieldingGas = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    QcApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    QcApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    EngineeringApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EngineeringApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaWps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QaAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProgramId = table.Column<int>(type: "int", nullable: true),
                    AuditedUnit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Criteria = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PlannedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeadAuditorUserId = table.Column<int>(type: "int", nullable: false),
                    TeamUserIds = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReportDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaAudits_QaAuditPrograms_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "QaAuditPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaCapaActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CapaId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DoneAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EvidenceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EvidenceNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaCapaActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaCapaActions_QaCapas_CapaId",
                        column: x => x.CapaId,
                        principalTable: "QaCapas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaComplaints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ReceivedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    ResponseDue = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    NcrId = table.Column<int>(type: "int", nullable: true),
                    CapaId = table.Column<int>(type: "int", nullable: true),
                    Response = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaComplaints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaComplaints_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaComplaints_QaCapas_CapaId",
                        column: x => x.CapaId,
                        principalTable: "QaCapas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaComplaints_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaDocumentRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ChangeSummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    FileDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaDocumentRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaDocumentRevisions_QaDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "QaDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaCalibrationEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LabName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LabAccredited = table.Column<bool>(type: "bit", nullable: false),
                    CertificateNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CertificateDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TraceabilityStatement = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AsFoundPass = table.Column<bool>(type: "bit", nullable: false),
                    AsLeftPass = table.Column<bool>(type: "bit", nullable: false),
                    Adjusted = table.Column<bool>(type: "bit", nullable: false),
                    IntervalMonths = table.Column<int>(type: "int", nullable: false),
                    NextDue = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaCalibrationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaCalibrationEvents_QaEquipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "QaEquipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaItps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClientApprovalReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ClientApprovalDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    NoticeDays = table.Column<int>(type: "int", nullable: false),
                    AutoDrafted = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_QaItps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaItps_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaItps_QaItpTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "QaItpTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaItps_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaItpTemplateActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    ItemNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Stage = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Activity = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    TestType = table.Column<int>(type: "int", nullable: false),
                    AcceptanceReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PointVendor = table.Column<int>(type: "int", nullable: false),
                    PointTpi = table.Column<int>(type: "int", nullable: false),
                    PointClient = table.Column<int>(type: "int", nullable: false),
                    DocumentProduced = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RequiredReportType = table.Column<int>(type: "int", nullable: true),
                    Remark = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GatesRelease = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaItpTemplateActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaItpTemplateActivities_QaItpTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "QaItpTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaReviewDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ResponsibleUserId = table.Column<int>(type: "int", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CapaId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaReviewDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaReviewDecisions_QaCapas_CapaId",
                        column: x => x.CapaId,
                        principalTable: "QaCapas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaReviewDecisions_QaManagementReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "QaManagementReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaReviewInputs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AutoDrafted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaReviewInputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaReviewInputs_QaManagementReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "QaManagementReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaQualifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Scheme = table.Column<int>(type: "int", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CertificateNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CertificateDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IssuedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Revoked = table.Column<bool>(type: "bit", nullable: false),
                    RevokeReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Superseded = table.Column<bool>(type: "bit", nullable: false),
                    RenewsQualificationId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaQualifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaQualifications_QaPersons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "QaPersons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaWpsPqrs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WpsId = table.Column<int>(type: "int", nullable: false),
                    PqrId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaWpsPqrs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaWpsPqrs_QaPqrs_PqrId",
                        column: x => x.PqrId,
                        principalTable: "QaPqrs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaWpsPqrs_QaWps_WpsId",
                        column: x => x.WpsId,
                        principalTable: "QaWps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaAuditFindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuditId = table.Column<int>(type: "int", nullable: false),
                    Clause = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CapaId = table.Column<int>(type: "int", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaAuditFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaAuditFindings_QaAudits_AuditId",
                        column: x => x.AuditId,
                        principalTable: "QaAudits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaAuditFindings_QaCapas_CapaId",
                        column: x => x.CapaId,
                        principalTable: "QaCapas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaDocumentDistributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RevisionId = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<int>(type: "int", nullable: true),
                    RecipientUnit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcknowledgedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaDocumentDistributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaDocumentDistributions_QaDocumentRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "QaDocumentRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaOotCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    CalibrationEventId = table.Column<int>(type: "int", nullable: false),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaOotCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaOotCases_QaCalibrationEvents_CalibrationEventId",
                        column: x => x.CalibrationEventId,
                        principalTable: "QaCalibrationEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaOotCases_QaEquipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "QaEquipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaItpActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItpId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    ItemNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Stage = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Activity = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    TestType = table.Column<int>(type: "int", nullable: false),
                    AcceptanceReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PointVendor = table.Column<int>(type: "int", nullable: false),
                    PointTpi = table.Column<int>(type: "int", nullable: false),
                    PointClient = table.Column<int>(type: "int", nullable: false),
                    DocumentProduced = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RequiredReportType = table.Column<int>(type: "int", nullable: true),
                    Remark = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GatesRelease = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PlannedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NotificationSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneByUserId = table.Column<int>(type: "int", nullable: true),
                    StatusNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaItpActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaItpActivities_QaItps_ItpId",
                        column: x => x.ItpId,
                        principalTable: "QaItps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaItpRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItpId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClientApprovalReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ClientApprovalDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Snapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaItpRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaItpRevisions_QaItps_ItpId",
                        column: x => x.ItpId,
                        principalTable: "QaItps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaItpTemplateParameters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Min = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Max = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequiresLimits = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaItpTemplateParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaItpTemplateParameters_QaItpTemplateActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "QaItpTemplateActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaWeldLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WeldDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    JointNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WpsId = table.Column<int>(type: "int", nullable: false),
                    QualificationId = table.Column<int>(type: "int", nullable: false),
                    FillerBatch = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PreheatC = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    InterpassC = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    Vt = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaWeldLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaWeldLogs_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaWeldLogs_QaQualifications_QualificationId",
                        column: x => x.QualificationId,
                        principalTable: "QaQualifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaWeldLogs_QaWps_WpsId",
                        column: x => x.WpsId,
                        principalTable: "QaWps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaWeldLogs_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaOotItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    SourceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Assessment = table.Column<int>(type: "int", nullable: false),
                    AssessmentNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NcrId = table.Column<int>(type: "int", nullable: true),
                    AssessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AssessedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaOotItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaOotItems_QaOotCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "QaOotCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaItpActivityParameters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Min = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Max = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequiresLimits = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaItpActivityParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaItpActivityParameters_QaItpActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "QaItpActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaItpPointReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    Party = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    PersonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Organisation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    WaivedByNotice = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaItpPointReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaItpPointReleases_QaItpActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "QaItpActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ItpActivityId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DrawingNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DrawingRevision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InspectorUserId = table.Column<int>(type: "int", nullable: false),
                    SelfInspection = table.Column<bool>(type: "bit", nullable: false),
                    RecordDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Result = table.Column<int>(type: "int", nullable: false),
                    NcrId = table.Column<int>(type: "int", nullable: true),
                    AttachmentDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaRecords_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaRecords_InvLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InvLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaRecords_InvNonConformances_NcrId",
                        column: x => x.NcrId,
                        principalTable: "InvNonConformances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaRecords_QaItpActivities_ItpActivityId",
                        column: x => x.ItpActivityId,
                        principalTable: "QaItpActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaRecords_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaTestReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    ItpActivityId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProcedureRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProcedureRevision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CodeEdition = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExaminerUserId = table.Column<int>(type: "int", nullable: false),
                    ExaminerQualificationId = table.Column<int>(type: "int", nullable: true),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NcrId = table.Column<int>(type: "int", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaTestReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaTestReports_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaTestReports_InvNonConformances_NcrId",
                        column: x => x.NcrId,
                        principalTable: "InvNonConformances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaTestReports_QaItpActivities_ItpActivityId",
                        column: x => x.ItpActivityId,
                        principalTable: "QaItpActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaTestReports_QaQualifications_ExaminerQualificationId",
                        column: x => x.ExaminerQualificationId,
                        principalTable: "QaQualifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaTestReports_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QaMeasurements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecordId = table.Column<int>(type: "int", nullable: false),
                    Characteristic = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Nominal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TolPlus = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TolMinus = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Measured = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EquipmentId = table.Column<int>(type: "int", nullable: true),
                    Pass = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaMeasurements_QaEquipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "QaEquipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaMeasurements_QaRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "QaRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaTestReportEquipment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaTestReportEquipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaTestReportEquipment_QaEquipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "QaEquipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaTestReportEquipment_QaTestReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "QaTestReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaTestReportIndications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Size = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Orientation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Accepted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaTestReportIndications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaTestReportIndications_QaTestReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "QaTestReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaTestReportValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    Parameter = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Min = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Max = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Measured = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Pass = table.Column<bool>(type: "bit", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequiresLimits = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaTestReportValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaTestReportValues_QaTestReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "QaTestReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaTestReportWitnesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    Party = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WitnessDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaTestReportWitnesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaTestReportWitnesses_QaTestReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "QaTestReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QaWeldLogReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WeldLogId = table.Column<int>(type: "int", nullable: false),
                    TestReportId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QaWeldLogReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QaWeldLogReports_QaTestReports_TestReportId",
                        column: x => x.TestReportId,
                        principalTable: "QaTestReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QaWeldLogReports_QaWeldLogs_WeldLogId",
                        column: x => x.WeldLogId,
                        principalTable: "QaWeldLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "Description", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "Name", "NormalizedName" },
                values: new object[] { 14, "00000014-0000-0000-0000-000000000000", null, null, null, null, "تضمین کیفیت", null, null, null, null, "QA", "QA" });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 1400, "permission", "Traceability", null, null, null, null, null, null, null, null, 14 },
                    { 1401, "permission", "Warehouse", null, null, null, null, null, null, null, null, 14 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvNonConformances_CapaId",
                table: "InvNonConformances",
                column: "CapaId");

            migrationBuilder.CreateIndex(
                name: "IX_QaAuditFindings_AuditId",
                table: "QaAuditFindings",
                column: "AuditId");

            migrationBuilder.CreateIndex(
                name: "IX_QaAuditFindings_CapaId",
                table: "QaAuditFindings",
                column: "CapaId");

            migrationBuilder.CreateIndex(
                name: "IX_QaAudits_Number",
                table: "QaAudits",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaAudits_ProgramId",
                table: "QaAudits",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_QaCalibrationEvents_EquipmentId_EventDate",
                table: "QaCalibrationEvents",
                columns: new[] { "EquipmentId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_QaCalibrationIntervals_EquipmentType",
                table: "QaCalibrationIntervals",
                column: "EquipmentType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaCapaActions_CapaId",
                table: "QaCapaActions",
                column: "CapaId");

            migrationBuilder.CreateIndex(
                name: "IX_QaCapas_Number",
                table: "QaCapas",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaCapas_Status",
                table: "QaCapas",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_QaComplaints_CapaId",
                table: "QaComplaints",
                column: "CapaId");

            migrationBuilder.CreateIndex(
                name: "IX_QaComplaints_ContractId",
                table: "QaComplaints",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_QaComplaints_Number",
                table: "QaComplaints",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaComplaints_Status",
                table: "QaComplaints",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_QaComplaints_WorkOrderId",
                table: "QaComplaints",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_QaDocumentDistributions_RevisionId",
                table: "QaDocumentDistributions",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_QaDocumentRevisions_DocumentId_Revision",
                table: "QaDocumentRevisions",
                columns: new[] { "DocumentId", "Revision" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaDocuments_Code",
                table: "QaDocuments",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaEquipment_Code",
                table: "QaEquipment",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaEquipment_NextDue",
                table: "QaEquipment",
                column: "NextDue");

            migrationBuilder.CreateIndex(
                name: "IX_QaItpActivities_ItpId_Status",
                table: "QaItpActivities",
                columns: new[] { "ItpId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QaItpActivityParameters_ActivityId",
                table: "QaItpActivityParameters",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_QaItpPointReleases_ActivityId",
                table: "QaItpPointReleases",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_QaItpRevisions_ItpId",
                table: "QaItpRevisions",
                column: "ItpId");

            migrationBuilder.CreateIndex(
                name: "IX_QaItps_ContractId",
                table: "QaItps",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_QaItps_Number",
                table: "QaItps",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaItps_Status",
                table: "QaItps",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_QaItps_TemplateId",
                table: "QaItps",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_QaItps_WorkOrderId",
                table: "QaItps",
                column: "WorkOrderId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaItpTemplateActivities_TemplateId",
                table: "QaItpTemplateActivities",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_QaItpTemplateParameters_ActivityId",
                table: "QaItpTemplateParameters",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_QaItpTemplates_Code",
                table: "QaItpTemplates",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaMaterialCertificates_LotId",
                table: "QaMaterialCertificates",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_QaMeasurements_EquipmentId",
                table: "QaMeasurements",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_QaMeasurements_RecordId",
                table: "QaMeasurements",
                column: "RecordId");

            migrationBuilder.CreateIndex(
                name: "IX_QaOotCases_CalibrationEventId",
                table: "QaOotCases",
                column: "CalibrationEventId");

            migrationBuilder.CreateIndex(
                name: "IX_QaOotCases_EquipmentId",
                table: "QaOotCases",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_QaOotCases_Number",
                table: "QaOotCases",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaOotCases_Status",
                table: "QaOotCases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_QaOotItems_CaseId",
                table: "QaOotItems",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_QaPersons_PersonnelCode",
                table: "QaPersons",
                column: "PersonnelCode",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaPersons_ProviderId",
                table: "QaPersons",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_QaPqrs_Number",
                table: "QaPqrs",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaQualifications_PersonId_Method",
                table: "QaQualifications",
                columns: new[] { "PersonId", "Method" });

            migrationBuilder.CreateIndex(
                name: "IX_QaRecords_ItemId",
                table: "QaRecords",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QaRecords_ItpActivityId",
                table: "QaRecords",
                column: "ItpActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_QaRecords_LotId",
                table: "QaRecords",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_QaRecords_NcrId",
                table: "QaRecords",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_QaRecords_Number",
                table: "QaRecords",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaRecords_WorkOrderId",
                table: "QaRecords",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_QaReleaseNotes_Number",
                table: "QaReleaseNotes",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaReleaseNotes_WorkOrderId_Status",
                table: "QaReleaseNotes",
                columns: new[] { "WorkOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QaRetentionRules_RecordType",
                table: "QaRetentionRules",
                column: "RecordType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaReviewDecisions_CapaId",
                table: "QaReviewDecisions",
                column: "CapaId");

            migrationBuilder.CreateIndex(
                name: "IX_QaReviewDecisions_ReviewId",
                table: "QaReviewDecisions",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_QaReviewInputs_ReviewId",
                table: "QaReviewInputs",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_QaSettings_Key",
                table: "QaSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReportEquipment_EquipmentId",
                table: "QaTestReportEquipment",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReportEquipment_ReportId",
                table: "QaTestReportEquipment",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReportIndications_ReportId",
                table: "QaTestReportIndications",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReports_ExaminerQualificationId",
                table: "QaTestReports",
                column: "ExaminerQualificationId");

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReports_ItemId",
                table: "QaTestReports",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReports_ItpActivityId",
                table: "QaTestReports",
                column: "ItpActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReports_NcrId",
                table: "QaTestReports",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReports_Number",
                table: "QaTestReports",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReports_Status",
                table: "QaTestReports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReports_WorkOrderId",
                table: "QaTestReports",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReportValues_ReportId",
                table: "QaTestReportValues",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_QaTestReportWitnesses_ReportId",
                table: "QaTestReportWitnesses",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_QaWeldLogReports_TestReportId",
                table: "QaWeldLogReports",
                column: "TestReportId");

            migrationBuilder.CreateIndex(
                name: "IX_QaWeldLogReports_WeldLogId",
                table: "QaWeldLogReports",
                column: "WeldLogId");

            migrationBuilder.CreateIndex(
                name: "IX_QaWeldLogs_ItemId",
                table: "QaWeldLogs",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_QaWeldLogs_QualificationId",
                table: "QaWeldLogs",
                column: "QualificationId");

            migrationBuilder.CreateIndex(
                name: "IX_QaWeldLogs_WorkOrderId",
                table: "QaWeldLogs",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_QaWeldLogs_WpsId",
                table: "QaWeldLogs",
                column: "WpsId");

            migrationBuilder.CreateIndex(
                name: "IX_QaWps_Number_Revision",
                table: "QaWps",
                columns: new[] { "Number", "Revision" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_QaWpsPqrs_PqrId",
                table: "QaWpsPqrs",
                column: "PqrId");

            migrationBuilder.CreateIndex(
                name: "IX_QaWpsPqrs_WpsId_PqrId",
                table: "QaWpsPqrs",
                columns: new[] { "WpsId", "PqrId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InvNonConformances_QaCapas_CapaId",
                table: "InvNonConformances",
                column: "CapaId",
                principalTable: "QaCapas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvNonConformances_QaCapas_CapaId",
                table: "InvNonConformances");

            migrationBuilder.DropTable(
                name: "QaAuditFindings");

            migrationBuilder.DropTable(
                name: "QaCalibrationIntervals");

            migrationBuilder.DropTable(
                name: "QaCapaActions");

            migrationBuilder.DropTable(
                name: "QaComplaints");

            migrationBuilder.DropTable(
                name: "QaDocumentDistributions");

            migrationBuilder.DropTable(
                name: "QaItpActivityParameters");

            migrationBuilder.DropTable(
                name: "QaItpPointReleases");

            migrationBuilder.DropTable(
                name: "QaItpRevisions");

            migrationBuilder.DropTable(
                name: "QaItpTemplateParameters");

            migrationBuilder.DropTable(
                name: "QaMaterialCertificates");

            migrationBuilder.DropTable(
                name: "QaMeasurements");

            migrationBuilder.DropTable(
                name: "QaOotItems");

            migrationBuilder.DropTable(
                name: "QaReleaseNotes");

            migrationBuilder.DropTable(
                name: "QaRetentionRules");

            migrationBuilder.DropTable(
                name: "QaReviewDecisions");

            migrationBuilder.DropTable(
                name: "QaReviewInputs");

            migrationBuilder.DropTable(
                name: "QaSettings");

            migrationBuilder.DropTable(
                name: "QaTestReportEquipment");

            migrationBuilder.DropTable(
                name: "QaTestReportIndications");

            migrationBuilder.DropTable(
                name: "QaTestReportValues");

            migrationBuilder.DropTable(
                name: "QaTestReportWitnesses");

            migrationBuilder.DropTable(
                name: "QaWeldLogReports");

            migrationBuilder.DropTable(
                name: "QaWpsPqrs");

            migrationBuilder.DropTable(
                name: "QaAudits");

            migrationBuilder.DropTable(
                name: "QaDocumentRevisions");

            migrationBuilder.DropTable(
                name: "QaItpTemplateActivities");

            migrationBuilder.DropTable(
                name: "QaRecords");

            migrationBuilder.DropTable(
                name: "QaOotCases");

            migrationBuilder.DropTable(
                name: "QaCapas");

            migrationBuilder.DropTable(
                name: "QaManagementReviews");

            migrationBuilder.DropTable(
                name: "QaTestReports");

            migrationBuilder.DropTable(
                name: "QaWeldLogs");

            migrationBuilder.DropTable(
                name: "QaPqrs");

            migrationBuilder.DropTable(
                name: "QaAuditPrograms");

            migrationBuilder.DropTable(
                name: "QaDocuments");

            migrationBuilder.DropTable(
                name: "QaCalibrationEvents");

            migrationBuilder.DropTable(
                name: "QaItpActivities");

            migrationBuilder.DropTable(
                name: "QaQualifications");

            migrationBuilder.DropTable(
                name: "QaWps");

            migrationBuilder.DropTable(
                name: "QaEquipment");

            migrationBuilder.DropTable(
                name: "QaItps");

            migrationBuilder.DropTable(
                name: "QaPersons");

            migrationBuilder.DropTable(
                name: "QaItpTemplates");

            migrationBuilder.DropIndex(
                name: "IX_InvNonConformances_CapaId",
                table: "InvNonConformances");

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1400);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 1401);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DropColumn(
                name: "AffectedEquipmentId",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "CapaId",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "CostImpact",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "CustomerReturnRef",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "RootCauseMethod",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "SourceRecordNumber",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "TimeImpactDays",
                table: "InvNonConformances");

            migrationBuilder.DropColumn(
                name: "RequiredCertificateType",
                table: "InvItems");
        }
    }
}
