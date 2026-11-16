using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class EngineeringE1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    InquiryId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    CustomerRequirementNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OpinionText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OpinionByUserId = table.Column<int>(type: "int", nullable: true),
                    OpinionAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HandoverListCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ApprovedGapHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Classification = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    PlatformWithChanges = table.Column<bool>(type: "bit", nullable: false),
                    ReferenceCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    StandardEvidence = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StandardDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangeNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProposedClassification = table.Column<int>(type: "int", nullable: false),
                    ProposedProductId = table.Column<int>(type: "int", nullable: true),
                    ProposedWithChanges = table.Column<bool>(type: "bit", nullable: false),
                    ProposedChangeNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProposalBasis = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ProposedByUserId = table.Column<int>(type: "int", nullable: true),
                    ProposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    EngConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    EngConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CeoApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CeoApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClassificationHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenderPackageId = table.Column<int>(type: "int", nullable: true),
                    TenderReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    TenderReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenderHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ConceptNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CharterReviewOutcome = table.Column<int>(type: "int", nullable: false),
                    CharterReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    CharterReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CharterHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CharterReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClosedReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnCases_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnCases_PbProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PbProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnCases_PcTenderPackages_TenderPackageId",
                        column: x => x.TenderPackageId,
                        principalTable: "PcTenderPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnCases_SlInquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "SlInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnReferenceLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    StandardSource = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    EditorUserIds = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnReferenceLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnCaseEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnCaseEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnCaseEvents_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnClarifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    FollowsId = table.Column<int>(type: "int", nullable: true),
                    Question = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    QuestionAttachmentIds = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AskedByUserId = table.Column<int>(type: "int", nullable: false),
                    AskedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SentToClientByUserId = table.Column<int>(type: "int", nullable: true),
                    SentToClientAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClientLetterRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Answer = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AnswerAttachmentIds = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AnswerRecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    AnswerRecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnClarifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnClarifications_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnClarifications_EnClarifications_FollowsId",
                        column: x => x.FollowsId,
                        principalTable: "EnClarifications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EnDeviations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    CaseId = table.Column<int>(type: "int", nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    StandardCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    StandardClause = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Requirement = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ManagerApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ManagerApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CeoApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CeoApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnDeviations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnDeviations_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EnDeviations_PbProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PbProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnPlatformRegistrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ReferenceCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    StandardBasis = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Rationale = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProposedByUserId = table.Column<int>(type: "int", nullable: false),
                    ProposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DossierId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnPlatformRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnPlatformRegistrations_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EnStandardGaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    BaseCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    TargetCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    BaseRevision = table.Column<int>(type: "int", nullable: false),
                    TargetRevision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AddedCount = table.Column<int>(type: "int", nullable: false),
                    RemovedCount = table.Column<int>(type: "int", nullable: false),
                    RequiredChangedCount = table.Column<int>(type: "int", nullable: false),
                    ReusableCount = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EngineeringImpactNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnStandardGaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnStandardGaps_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EnStandardGaps_PbProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PbProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnPlatformDossiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ReferenceListId = table.Column<int>(type: "int", nullable: true),
                    ReferenceCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    StandardBasis = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReferenceRevision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastEditedByUserId = table.Column<int>(type: "int", nullable: true),
                    EditorUserIds = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_EnPlatformDossiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnPlatformDossiers_EnReferenceLists_ReferenceListId",
                        column: x => x.ReferenceListId,
                        principalTable: "EnReferenceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnPlatformDossiers_PbProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PbProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnReferenceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ListId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Discipline = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    TenderStage = table.Column<bool>(type: "bit", nullable: false),
                    ClientSubmittal = table.Column<bool>(type: "bit", nullable: false),
                    StandardClause = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Units = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Voided = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnReferenceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnReferenceLines_EnReferenceLists_ListId",
                        column: x => x.ListId,
                        principalTable: "EnReferenceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnStandardGapLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GapId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Discipline = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BaseRequired = table.Column<bool>(type: "bit", nullable: false),
                    TargetRequired = table.Column<bool>(type: "bit", nullable: false),
                    ReusableFromDossierLineId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnStandardGapLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnStandardGapLines_EnStandardGaps_GapId",
                        column: x => x.GapId,
                        principalTable: "EnStandardGaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnPlatformDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DossierId = table.Column<int>(type: "int", nullable: false),
                    ReferenceLineId = table.Column<int>(type: "int", nullable: true),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Discipline = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    TenderStage = table.Column<bool>(type: "bit", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ControlledDocumentId = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    NotApplicableReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Units = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SystemProven = table.Column<bool>(type: "bit", nullable: false),
                    DeviationId = table.Column<int>(type: "int", nullable: true),
                    SuppliedByUserId = table.Column<int>(type: "int", nullable: true),
                    SuppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnPlatformDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnPlatformDocuments_EnPlatformDossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "EnPlatformDossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EnPlatformDocuments_EnReferenceLines_ReferenceLineId",
                        column: x => x.ReferenceLineId,
                        principalTable: "EnReferenceLines",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EnDistributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HandoverId = table.Column<int>(type: "int", nullable: false),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<int>(type: "int", nullable: false),
                    Roles = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ScopeNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DocumentCount = table.Column<int>(type: "int", nullable: false),
                    NotifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NoticeSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceiptConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnDistributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnDistributions_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EnDocumentDebts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WaiverId = table.Column<int>(type: "int", nullable: false),
                    HandoverLineId = table.Column<int>(type: "int", nullable: true),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SettledByUserId = table.Column<int>(type: "int", nullable: true),
                    SettledDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnDocumentDebts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnDocumentDebts_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EnHandoverLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HandoverId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Discipline = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Revision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PlatformDocumentId = table.Column<int>(type: "int", nullable: true),
                    DeliverableId = table.Column<int>(type: "int", nullable: true),
                    LinkPath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Units = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnHandoverLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnHandovers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    EditorUserIds = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Complete = table.Column<bool>(type: "bit", nullable: false),
                    MissingRequiredCount = table.Column<int>(type: "int", nullable: false),
                    WaiverId = table.Column<int>(type: "int", nullable: true),
                    SubmitConfirmedByUserId = table.Column<int>(type: "int", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptedByUserId = table.Column<int>(type: "int", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReturnedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReturnedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnHandovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnHandovers_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EnHandovers_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnInfoRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HandoverId = table.Column<int>(type: "int", nullable: true),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<int>(type: "int", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AskedByUserId = table.Column<int>(type: "int", nullable: false),
                    AskedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AnsweredByUserId = table.Column<int>(type: "int", nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnInfoRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnInfoRequests_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EnInfoRequests_EnHandovers_HandoverId",
                        column: x => x.HandoverId,
                        principalTable: "EnHandovers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EnWaivers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HandoverId = table.Column<int>(type: "int", nullable: false),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    MissingSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DebtHours = table.Column<int>(type: "int", nullable: false),
                    DebtDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnWaivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnWaivers_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EnWaivers_EnHandovers_HandoverId",
                        column: x => x.HandoverId,
                        principalTable: "EnHandovers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnCaseEvents_CaseId",
                table: "EnCaseEvents",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_EnCases_Number",
                table: "EnCases",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnCases_ProductId",
                table: "EnCases",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_EnCases_Status",
                table: "EnCases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EnCases_TenderPackageId",
                table: "EnCases",
                column: "TenderPackageId");

            migrationBuilder.CreateIndex(
                name: "UX_EnCases_Contract",
                table: "EnCases",
                column: "ContractId",
                unique: true,
                filter: "[ContractId] IS NOT NULL AND [Deleted] = 0 AND [Status] < 11");

            migrationBuilder.CreateIndex(
                name: "UX_EnCases_Inquiry",
                table: "EnCases",
                column: "InquiryId",
                unique: true,
                filter: "[InquiryId] IS NOT NULL AND [Deleted] = 0 AND [Status] < 11");

            migrationBuilder.CreateIndex(
                name: "IX_EnClarifications_CaseId_Status",
                table: "EnClarifications",
                columns: new[] { "CaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EnClarifications_FollowsId",
                table: "EnClarifications",
                column: "FollowsId");

            migrationBuilder.CreateIndex(
                name: "IX_EnClarifications_Number",
                table: "EnClarifications",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnDeviations_CaseId",
                table: "EnDeviations",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_EnDeviations_Number",
                table: "EnDeviations",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnDeviations_ProductId",
                table: "EnDeviations",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_EnDeviations_Status",
                table: "EnDeviations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EnDeviations_TargetType_TargetId_Status",
                table: "EnDeviations",
                columns: new[] { "TargetType", "TargetId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EnDistributions_CaseId",
                table: "EnDistributions",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_EnDistributions_HandoverId_Unit",
                table: "EnDistributions",
                columns: new[] { "HandoverId", "Unit" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnDocumentDebts_CaseId",
                table: "EnDocumentDebts",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_EnDocumentDebts_HandoverLineId",
                table: "EnDocumentDebts",
                column: "HandoverLineId");

            migrationBuilder.CreateIndex(
                name: "IX_EnDocumentDebts_Status_DueAt",
                table: "EnDocumentDebts",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EnDocumentDebts_WaiverId_HandoverLineId",
                table: "EnDocumentDebts",
                columns: new[] { "WaiverId", "HandoverLineId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnHandoverLines_HandoverId",
                table: "EnHandoverLines",
                column: "HandoverId");

            migrationBuilder.CreateIndex(
                name: "IX_EnHandovers_CaseId_Round",
                table: "EnHandovers",
                columns: new[] { "CaseId", "Round" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnHandovers_ContractId",
                table: "EnHandovers",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_EnHandovers_Number",
                table: "EnHandovers",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnHandovers_Status",
                table: "EnHandovers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EnHandovers_WaiverId",
                table: "EnHandovers",
                column: "WaiverId");

            migrationBuilder.CreateIndex(
                name: "IX_EnInfoRequests_CaseId_Status",
                table: "EnInfoRequests",
                columns: new[] { "CaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EnInfoRequests_HandoverId",
                table: "EnInfoRequests",
                column: "HandoverId");

            migrationBuilder.CreateIndex(
                name: "IX_EnInfoRequests_Number",
                table: "EnInfoRequests",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnInfoRequests_Status",
                table: "EnInfoRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EnPlatformDocuments_DossierId",
                table: "EnPlatformDocuments",
                column: "DossierId");

            migrationBuilder.CreateIndex(
                name: "IX_EnPlatformDocuments_ReferenceLineId",
                table: "EnPlatformDocuments",
                column: "ReferenceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_EnPlatformDossiers_ProductId_ReferenceCode",
                table: "EnPlatformDossiers",
                columns: new[] { "ProductId", "ReferenceCode" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnPlatformDossiers_ReferenceListId",
                table: "EnPlatformDossiers",
                column: "ReferenceListId");

            migrationBuilder.CreateIndex(
                name: "IX_EnPlatformDossiers_Status",
                table: "EnPlatformDossiers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EnPlatformRegistrations_CaseId",
                table: "EnPlatformRegistrations",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_EnPlatformRegistrations_Number",
                table: "EnPlatformRegistrations",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnPlatformRegistrations_ProductId_ReferenceCode_Status",
                table: "EnPlatformRegistrations",
                columns: new[] { "ProductId", "ReferenceCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EnPlatformRegistrations_Status",
                table: "EnPlatformRegistrations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EnReferenceLines_ListId",
                table: "EnReferenceLines",
                column: "ListId");

            migrationBuilder.CreateIndex(
                name: "IX_EnReferenceLists_Code_Revision",
                table: "EnReferenceLists",
                columns: new[] { "Code", "Revision" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnReferenceLists_Status",
                table: "EnReferenceLists",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_EnReferenceLists_OneApproved",
                table: "EnReferenceLists",
                column: "Code",
                unique: true,
                filter: "[Status] = 2 AND [Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnStandardGapLines_GapId",
                table: "EnStandardGapLines",
                column: "GapId");

            migrationBuilder.CreateIndex(
                name: "IX_EnStandardGaps_CaseId_Status",
                table: "EnStandardGaps",
                columns: new[] { "CaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EnStandardGaps_Number",
                table: "EnStandardGaps",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnStandardGaps_ProductId",
                table: "EnStandardGaps",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_EnWaivers_CaseId",
                table: "EnWaivers",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_EnWaivers_Number",
                table: "EnWaivers",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_EnWaivers_OneLive",
                table: "EnWaivers",
                column: "HandoverId",
                unique: true,
                filter: "[Status] IN (0, 1) AND [Deleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_EnDistributions_EnHandovers_HandoverId",
                table: "EnDistributions",
                column: "HandoverId",
                principalTable: "EnHandovers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EnDocumentDebts_EnHandoverLines_HandoverLineId",
                table: "EnDocumentDebts",
                column: "HandoverLineId",
                principalTable: "EnHandoverLines",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EnDocumentDebts_EnWaivers_WaiverId",
                table: "EnDocumentDebts",
                column: "WaiverId",
                principalTable: "EnWaivers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EnHandoverLines_EnHandovers_HandoverId",
                table: "EnHandoverLines",
                column: "HandoverId",
                principalTable: "EnHandovers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EnHandovers_EnWaivers_WaiverId",
                table: "EnHandovers",
                column: "WaiverId",
                principalTable: "EnWaivers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EnHandovers_EnCases_CaseId",
                table: "EnHandovers");

            migrationBuilder.DropForeignKey(
                name: "FK_EnWaivers_EnCases_CaseId",
                table: "EnWaivers");

            migrationBuilder.DropForeignKey(
                name: "FK_EnWaivers_EnHandovers_HandoverId",
                table: "EnWaivers");

            migrationBuilder.DropTable(
                name: "EnCaseEvents");

            migrationBuilder.DropTable(
                name: "EnClarifications");

            migrationBuilder.DropTable(
                name: "EnDeviations");

            migrationBuilder.DropTable(
                name: "EnDistributions");

            migrationBuilder.DropTable(
                name: "EnDocumentDebts");

            migrationBuilder.DropTable(
                name: "EnInfoRequests");

            migrationBuilder.DropTable(
                name: "EnPlatformDocuments");

            migrationBuilder.DropTable(
                name: "EnPlatformRegistrations");

            migrationBuilder.DropTable(
                name: "EnStandardGapLines");

            migrationBuilder.DropTable(
                name: "EnHandoverLines");

            migrationBuilder.DropTable(
                name: "EnPlatformDossiers");

            migrationBuilder.DropTable(
                name: "EnReferenceLines");

            migrationBuilder.DropTable(
                name: "EnStandardGaps");

            migrationBuilder.DropTable(
                name: "EnReferenceLists");

            migrationBuilder.DropTable(
                name: "EnCases");

            migrationBuilder.DropTable(
                name: "EnHandovers");

            migrationBuilder.DropTable(
                name: "EnWaivers");
        }
    }
}
