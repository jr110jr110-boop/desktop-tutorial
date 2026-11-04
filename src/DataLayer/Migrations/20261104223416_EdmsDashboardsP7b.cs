using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class EdmsDashboardsP7b : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PcEngineeringDeliverables_ContractId_DocumentNumber",
                table: "PcEngineeringDeliverables");

            migrationBuilder.DropIndex(
                name: "IX_PcDependencies_PredecessorId_SuccessorId",
                table: "PcDependencies");

            migrationBuilder.AddColumn<int>(
                name: "RevisionId",
                table: "PcTransmittalLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckOutNote",
                table: "PcEngineeringDeliverables",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CheckedOutAt",
                table: "PcEngineeringDeliverables",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CheckedOutByUserId",
                table: "PcEngineeringDeliverables",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProgressWeight",
                table: "PcEngineeringDeliverables",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<bool>(
                name: "ReceivedFromClient",
                table: "PcEngineeringDeliverables",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Deleted",
                table: "PcDependencies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "PcDependencies",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletedByUserId",
                table: "PcDependencies",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PcDisciplineApprovers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Discipline = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    CanReview = table.Column<bool>(type: "bit", nullable: false),
                    CanApprove = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDisciplineApprovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDisciplineApprovers_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcDocumentDistributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Discipline = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDocumentDistributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDocumentDistributions_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcDocumentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliverableId = table.Column<int>(type: "int", nullable: false),
                    RevisionId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDocumentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDocumentEvents_PcEngineeringDeliverables_DeliverableId",
                        column: x => x.DeliverableId,
                        principalTable: "PcEngineeringDeliverables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcDocumentRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliverableId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewerUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ApproverUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SupersededByRevisionId = table.Column<int>(type: "int", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ObsoleteReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AsBuiltByUserId = table.Column<int>(type: "int", nullable: true),
                    AsBuiltAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ChangeImpact = table.Column<bool>(type: "bit", nullable: false),
                    ChangeRequestId = table.Column<int>(type: "int", nullable: true),
                    ReceivedFromClient = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDocumentRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDocumentRevisions_PcChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "PcChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcDocumentRevisions_PcEngineeringDeliverables_DeliverableId",
                        column: x => x.DeliverableId,
                        principalTable: "PcEngineeringDeliverables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcEquipmentTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcEquipmentTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcEquipmentTags_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcEquipmentTags_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcEquipmentTags_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcTenderPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TenderId = table.Column<int>(type: "int", nullable: true),
                    TenderCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    LinkedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LinkedByUserId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcTenderPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcTenderPackages_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcDocumentNotices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RevisionId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    NotifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDocumentNotices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDocumentNotices_PcDocumentRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "PcDocumentRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcDocumentQualityLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RevisionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDocumentQualityLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDocumentQualityLinks_PcDocumentRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "PcDocumentRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcDocumentRevisionFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RevisionId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RemoveReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDocumentRevisionFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDocumentRevisionFiles_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcDocumentRevisionFiles_PcDocumentRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "PcDocumentRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcDocumentLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliverableId = table.Column<int>(type: "int", nullable: true),
                    ControlledDocumentId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    BomLineId = table.Column<int>(type: "int", nullable: true),
                    PartNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    StageCode2 = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    WbsNodeId = table.Column<int>(type: "int", nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    TagId = table.Column<int>(type: "int", nullable: true),
                    OperationTemplateId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UnlinkReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UnlinkedByUserId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDocumentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDocumentLinks_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcDocumentLinks_PbBomLines_BomLineId",
                        column: x => x.BomLineId,
                        principalTable: "PbBomLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcDocumentLinks_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcDocumentLinks_PcEngineeringDeliverables_DeliverableId",
                        column: x => x.DeliverableId,
                        principalTable: "PcEngineeringDeliverables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcDocumentLinks_PcEquipmentTags_TagId",
                        column: x => x.TagId,
                        principalTable: "PcEquipmentTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcDocumentLinks_PcWbsNodes_WbsNodeId",
                        column: x => x.WbsNodeId,
                        principalTable: "PcWbsNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcDocumentLinks_QaDocuments_ControlledDocumentId",
                        column: x => x.ControlledDocumentId,
                        principalTable: "QaDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcTenderDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Discipline = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Revision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IssuedByUserId = table.Column<int>(type: "int", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcTenderDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcTenderDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcTenderDocuments_PcTenderPackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "PcTenderPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PcTransmittalLines_RevisionId",
                table: "PcTransmittalLines",
                column: "RevisionId");

            // the document number becomes unique per contract: an older duplicate (same contract and number, not deleted) keeps its row and gets the suffix "-D<id>" so the index can be built; the lowest id keeps the number
            migrationBuilder.Sql(@"WITH d AS (SELECT [Id], ROW_NUMBER() OVER (PARTITION BY [ContractId], [DocumentNumber] ORDER BY [Id]) AS rn FROM [PcEngineeringDeliverables] WHERE [Deleted] = 0 AND [DocumentNumber] IS NOT NULL)
UPDATE e SET e.[DocumentNumber] = LEFT(e.[DocumentNumber], 170) + N'-D' + CAST(e.[Id] AS nvarchar(20)) FROM [PcEngineeringDeliverables] e JOIN d ON d.[Id] = e.[Id] WHERE d.rn > 1;");

            migrationBuilder.CreateIndex(
                name: "IX_PcEngineeringDeliverables_ContractId_DocumentNumber",
                table: "PcEngineeringDeliverables",
                columns: new[] { "ContractId", "DocumentNumber" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcDependencies_PredecessorId_SuccessorId",
                table: "PcDependencies",
                columns: new[] { "PredecessorId", "SuccessorId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcDisciplineApprovers_ContractId",
                table: "PcDisciplineApprovers",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDisciplineApprovers_Discipline_UserId_ContractId",
                table: "PcDisciplineApprovers",
                columns: new[] { "Discipline", "UserId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentDistributions_ContractId",
                table: "PcDocumentDistributions",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentDistributions_Discipline_UserId_ContractId",
                table: "PcDocumentDistributions",
                columns: new[] { "Discipline", "UserId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentEvents_DeliverableId_At",
                table: "PcDocumentEvents",
                columns: new[] { "DeliverableId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentEvents_RevisionId",
                table: "PcDocumentEvents",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentLinks_ActivityId",
                table: "PcDocumentLinks",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentLinks_BomLineId",
                table: "PcDocumentLinks",
                column: "BomLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentLinks_ControlledDocumentId",
                table: "PcDocumentLinks",
                column: "ControlledDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentLinks_DeliverableId",
                table: "PcDocumentLinks",
                column: "DeliverableId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentLinks_ItemId",
                table: "PcDocumentLinks",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentLinks_PartNumber",
                table: "PcDocumentLinks",
                column: "PartNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentLinks_TagId",
                table: "PcDocumentLinks",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentLinks_WbsNodeId",
                table: "PcDocumentLinks",
                column: "WbsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentNotices_RevisionId_UserId",
                table: "PcDocumentNotices",
                columns: new[] { "RevisionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentNotices_UserId_AcknowledgedAt",
                table: "PcDocumentNotices",
                columns: new[] { "UserId", "AcknowledgedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentQualityLinks_Kind_TargetId",
                table: "PcDocumentQualityLinks",
                columns: new[] { "Kind", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentQualityLinks_RevisionId",
                table: "PcDocumentQualityLinks",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentRevisionFiles_DocumentId",
                table: "PcDocumentRevisionFiles",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentRevisionFiles_RevisionId",
                table: "PcDocumentRevisionFiles",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentRevisions_ChangeRequestId",
                table: "PcDocumentRevisions",
                column: "ChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentRevisions_DeliverableId_Sequence",
                table: "PcDocumentRevisions",
                columns: new[] { "DeliverableId", "Sequence" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcDocumentRevisions_OneReleased",
                table: "PcDocumentRevisions",
                column: "DeliverableId",
                unique: true,
                filter: "[Status] = 4 AND [Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcEquipmentTags_ContractId_Code",
                table: "PcEquipmentTags",
                columns: new[] { "ContractId", "Code" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcEquipmentTags_ItemId",
                table: "PcEquipmentTags",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PcEquipmentTags_WorkOrderId",
                table: "PcEquipmentTags",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PcTenderDocuments_DocumentId",
                table: "PcTenderDocuments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PcTenderDocuments_Number",
                table: "PcTenderDocuments",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcTenderDocuments_PackageId",
                table: "PcTenderDocuments",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_PcTenderPackages_Code",
                table: "PcTenderPackages",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcTenderPackages_ContractId",
                table: "PcTenderPackages",
                column: "ContractId");

            migrationBuilder.AddForeignKey(
                name: "FK_PcTransmittalLines_PcDocumentRevisions_RevisionId",
                table: "PcTransmittalLines",
                column: "RevisionId",
                principalTable: "PcDocumentRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PcTransmittalLines_PcDocumentRevisions_RevisionId",
                table: "PcTransmittalLines");

            migrationBuilder.DropTable(
                name: "PcDisciplineApprovers");

            migrationBuilder.DropTable(
                name: "PcDocumentDistributions");

            migrationBuilder.DropTable(
                name: "PcDocumentEvents");

            migrationBuilder.DropTable(
                name: "PcDocumentLinks");

            migrationBuilder.DropTable(
                name: "PcDocumentNotices");

            migrationBuilder.DropTable(
                name: "PcDocumentQualityLinks");

            migrationBuilder.DropTable(
                name: "PcDocumentRevisionFiles");

            migrationBuilder.DropTable(
                name: "PcTenderDocuments");

            migrationBuilder.DropTable(
                name: "PcEquipmentTags");

            migrationBuilder.DropTable(
                name: "PcDocumentRevisions");

            migrationBuilder.DropTable(
                name: "PcTenderPackages");

            migrationBuilder.DropIndex(
                name: "IX_PcTransmittalLines_RevisionId",
                table: "PcTransmittalLines");

            migrationBuilder.DropIndex(
                name: "IX_PcEngineeringDeliverables_ContractId_DocumentNumber",
                table: "PcEngineeringDeliverables");

            migrationBuilder.DropIndex(
                name: "IX_PcDependencies_PredecessorId_SuccessorId",
                table: "PcDependencies");

            migrationBuilder.DropColumn(
                name: "RevisionId",
                table: "PcTransmittalLines");

            migrationBuilder.DropColumn(
                name: "CheckOutNote",
                table: "PcEngineeringDeliverables");

            migrationBuilder.DropColumn(
                name: "CheckedOutAt",
                table: "PcEngineeringDeliverables");

            migrationBuilder.DropColumn(
                name: "CheckedOutByUserId",
                table: "PcEngineeringDeliverables");

            migrationBuilder.DropColumn(
                name: "ProgressWeight",
                table: "PcEngineeringDeliverables");

            migrationBuilder.DropColumn(
                name: "ReceivedFromClient",
                table: "PcEngineeringDeliverables");

            migrationBuilder.DropColumn(
                name: "Deleted",
                table: "PcDependencies");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "PcDependencies");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "PcDependencies");

            migrationBuilder.CreateIndex(
                name: "IX_PcEngineeringDeliverables_ContractId_DocumentNumber",
                table: "PcEngineeringDeliverables",
                columns: new[] { "ContractId", "DocumentNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PcDependencies_PredecessorId_SuccessorId",
                table: "PcDependencies",
                columns: new[] { "PredecessorId", "SuccessorId" },
                unique: true);
        }
    }
}
