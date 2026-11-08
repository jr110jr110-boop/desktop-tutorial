using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class DesignControlEngChangePA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceOpcRoutingId",
                table: "MfRoutingRevisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComponentNote",
                table: "MfProductionOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComponentSource",
                table: "MfProductionOrders",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EcRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Classification = table.Column<int>(type: "int", nullable: false),
                    Urgency = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_EcRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignActivations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbDesignActivations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignAdoptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BomHeaderId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    ProductRevision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EvidenceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedByUserId = table.Column<int>(type: "int", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbDesignAdoptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbDesignAdoptions_PbBomHeaders_BomHeaderId",
                        column: x => x.BomHeaderId,
                        principalTable: "PbBomHeaders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignInputs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductRevision = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Statement = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AcceptanceCriterion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    StandardEdition = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PreviousDesignRef = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RefDeliverableId = table.Column<int>(type: "int", nullable: true),
                    RefRevisionId = table.Column<int>(type: "int", nullable: true),
                    RefText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RefRevisionText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureConsequence = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ConflictWithInputId = table.Column<int>(type: "int", nullable: true),
                    ConflictNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConflictMarkedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConflictMarkedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConflictResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ConflictResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConflictResolution = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SupersedesInputId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_PbDesignInputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbDesignInputs_PbProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PbProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductRevision = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ValidationRequired = table.Column<bool>(type: "bit", nullable: false),
                    ValidationWaiverReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReturnNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InputsCompleteByUserId = table.Column<int>(type: "int", nullable: true),
                    InputsCompleteAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InputsCompleteNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NotApplicableKinds = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupersedesPlanId = table.Column<int>(type: "int", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbDesignPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbDesignPlans_PbProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PbProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EcOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EffectiveFromOrderNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    EffectiveFromSerial = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    EffectiveFromDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectivityNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AffectedParts = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ImpactJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImpactSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ImpactAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ImpactByUserId = table.Column<int>(type: "int", nullable: true),
                    ContentSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EngineeringApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    EngineeringApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EngineeringNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    QaApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    QaApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    QaNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_EcOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EcOrders_EcRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "EcRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EcRequestTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PartNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    BomHeaderId = table.Column<int>(type: "int", nullable: true),
                    OpcRoutingId = table.Column<int>(type: "int", nullable: true),
                    DeliverableId = table.Column<int>(type: "int", nullable: true),
                    RoutingRevisionId = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EcRequestTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EcRequestTargets_EcRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "EcRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignInterfaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    FromGroup = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ToGroup = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ResponsibleRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ResponsibleUserId = table.Column<int>(type: "int", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbDesignInterfaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbDesignInterfaces_PbDesignPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "PbDesignPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductRevision = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ScheduledDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    HeldDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HeldByUserId = table.Column<int>(type: "int", nullable: true),
                    HeldAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Minutes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    OutcomeNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PreviousReviewId = table.Column<int>(type: "int", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ValidationMethod = table.Column<int>(type: "int", nullable: false),
                    ValidationEvidence = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CustomerAcceptanceRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CustomerAcceptanceNotApplicable = table.Column<bool>(type: "bit", nullable: false),
                    CustomerAcceptanceNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbDesignReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbDesignReviews_PbDesignPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "PbDesignPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ResponsibleRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ResponsibleUserId = table.Column<int>(type: "int", nullable: true),
                    PlannedStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlannedEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ActualEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbDesignStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbDesignStages_PbDesignPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "PbDesignPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EcDispositions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    ImpactKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    PartNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Disposition = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Stale = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EcDispositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EcDispositions_EcOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "EcOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EcOrderChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    OldTargetId = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PartNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    OldRevision = table.Column<int>(type: "int", nullable: true),
                    NewRevision = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EcOrderChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EcOrderChanges_EcOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "EcOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignFindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RaisedByUserId = table.Column<int>(type: "int", nullable: false),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CloseNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbDesignFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbDesignFindings_PbDesignReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "PbDesignReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignReviewInputs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewId = table.Column<int>(type: "int", nullable: false),
                    InputId = table.Column<int>(type: "int", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbDesignReviewInputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbDesignReviewInputs_PbDesignReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "PbDesignReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignReviewOutputs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    DeliverableId = table.Column<int>(type: "int", nullable: true),
                    RevisionId = table.Column<int>(type: "int", nullable: true),
                    BomHeaderId = table.Column<int>(type: "int", nullable: true),
                    OpcRoutingId = table.Column<int>(type: "int", nullable: true),
                    Label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RevisionText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbDesignReviewOutputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbDesignReviewOutputs_PbDesignReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "PbDesignReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignReviewParticipants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Independent = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbDesignReviewParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbDesignReviewParticipants_PbDesignReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "PbDesignReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PbDesignTraces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewId = table.Column<int>(type: "int", nullable: false),
                    InputId = table.Column<int>(type: "int", nullable: false),
                    OutputId = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<int>(type: "int", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EvidenceRef = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    JudgedByUserId = table.Column<int>(type: "int", nullable: true),
                    JudgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbDesignTraces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PbDesignTraces_PbDesignReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "PbDesignReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EcTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    DispositionItemId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    Disposition = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneByUserId = table.Column<int>(type: "int", nullable: true),
                    DoneNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifiedByUserId = table.Column<int>(type: "int", nullable: true),
                    VerifyNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EcTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EcTasks_EcDispositions_DispositionItemId",
                        column: x => x.DispositionItemId,
                        principalTable: "EcDispositions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EcTasks_EcOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "EcOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EcDispositions_OrderId_ImpactKey",
                table: "EcDispositions",
                columns: new[] { "OrderId", "ImpactKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EcOrderChanges_Kind_TargetId",
                table: "EcOrderChanges",
                columns: new[] { "Kind", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_EcOrderChanges_OrderId",
                table: "EcOrderChanges",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_EcOrders_Number",
                table: "EcOrders",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EcOrders_RequestId",
                table: "EcOrders",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_EcOrders_Status",
                table: "EcOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EcRequests_Number",
                table: "EcRequests",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EcRequests_Status",
                table: "EcRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EcRequestTargets_PartNumber",
                table: "EcRequestTargets",
                column: "PartNumber");

            migrationBuilder.CreateIndex(
                name: "IX_EcRequestTargets_RequestId",
                table: "EcRequestTargets",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_EcTasks_DispositionItemId",
                table: "EcTasks",
                column: "DispositionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_EcTasks_OrderId_Status",
                table: "EcTasks",
                columns: new[] { "OrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EcTasks_OwnerUserId_Status",
                table: "EcTasks",
                columns: new[] { "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignAdoptions_ProductId_ProductRevision",
                table: "PbDesignAdoptions",
                columns: new[] { "ProductId", "ProductRevision" });

            migrationBuilder.CreateIndex(
                name: "UX_PbDesignAdoptions_LivePerBom",
                table: "PbDesignAdoptions",
                column: "BomHeaderId",
                unique: true,
                filter: "[Deleted] = 0 AND [Status] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignFindings_OwnerUserId_Status",
                table: "PbDesignFindings",
                columns: new[] { "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignFindings_ReviewId_Seq",
                table: "PbDesignFindings",
                columns: new[] { "ReviewId", "Seq" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignInputs_Code",
                table: "PbDesignInputs",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignInputs_ProductId_ProductRevision_Status",
                table: "PbDesignInputs",
                columns: new[] { "ProductId", "ProductRevision", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignInterfaces_PlanId",
                table: "PbDesignInterfaces",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignPlans_Number",
                table: "PbDesignPlans",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignPlans_ProductId_ProductRevision_Version",
                table: "PbDesignPlans",
                columns: new[] { "ProductId", "ProductRevision", "Version" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_PbDesignPlans_OpenPerRevision",
                table: "PbDesignPlans",
                columns: new[] { "ProductId", "ProductRevision" },
                unique: true,
                filter: "[Deleted] = 0 AND [Status] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignReviewInputs_InputId",
                table: "PbDesignReviewInputs",
                column: "InputId");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignReviewInputs_ReviewId_InputId",
                table: "PbDesignReviewInputs",
                columns: new[] { "ReviewId", "InputId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignReviewOutputs_Kind_BomHeaderId",
                table: "PbDesignReviewOutputs",
                columns: new[] { "Kind", "BomHeaderId" });

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignReviewOutputs_Kind_RevisionId",
                table: "PbDesignReviewOutputs",
                columns: new[] { "Kind", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignReviewOutputs_ReviewId_Kind_RevisionId_BomHeaderId_OpcRoutingId",
                table: "PbDesignReviewOutputs",
                columns: new[] { "ReviewId", "Kind", "RevisionId", "BomHeaderId", "OpcRoutingId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignReviewParticipants_ReviewId_UserId",
                table: "PbDesignReviewParticipants",
                columns: new[] { "ReviewId", "UserId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignReviews_Number",
                table: "PbDesignReviews",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignReviews_PlanId",
                table: "PbDesignReviews",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignReviews_ProductId_ProductRevision_Type_Status",
                table: "PbDesignReviews",
                columns: new[] { "ProductId", "ProductRevision", "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignStages_PlanId_Kind",
                table: "PbDesignStages",
                columns: new[] { "PlanId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignStages_PlanId_Seq",
                table: "PbDesignStages",
                columns: new[] { "PlanId", "Seq" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PbDesignTraces_ReviewId_InputId_OutputId",
                table: "PbDesignTraces",
                columns: new[] { "ReviewId", "InputId", "OutputId" },
                unique: true,
                filter: "[Deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EcOrderChanges");

            migrationBuilder.DropTable(
                name: "EcRequestTargets");

            migrationBuilder.DropTable(
                name: "EcTasks");

            migrationBuilder.DropTable(
                name: "PbDesignActivations");

            migrationBuilder.DropTable(
                name: "PbDesignAdoptions");

            migrationBuilder.DropTable(
                name: "PbDesignFindings");

            migrationBuilder.DropTable(
                name: "PbDesignInputs");

            migrationBuilder.DropTable(
                name: "PbDesignInterfaces");

            migrationBuilder.DropTable(
                name: "PbDesignReviewInputs");

            migrationBuilder.DropTable(
                name: "PbDesignReviewOutputs");

            migrationBuilder.DropTable(
                name: "PbDesignReviewParticipants");

            migrationBuilder.DropTable(
                name: "PbDesignStages");

            migrationBuilder.DropTable(
                name: "PbDesignTraces");

            migrationBuilder.DropTable(
                name: "EcDispositions");

            migrationBuilder.DropTable(
                name: "PbDesignReviews");

            migrationBuilder.DropTable(
                name: "EcOrders");

            migrationBuilder.DropTable(
                name: "PbDesignPlans");

            migrationBuilder.DropTable(
                name: "EcRequests");

            migrationBuilder.DropColumn(
                name: "SourceOpcRoutingId",
                table: "MfRoutingRevisions");

            migrationBuilder.DropColumn(
                name: "ComponentNote",
                table: "MfProductionOrders");

            migrationBuilder.DropColumn(
                name: "ComponentSource",
                table: "MfProductionOrders");
        }
    }
}
