using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class TasksP7a : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "PcDailyReports",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "PcDailyReports",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Weather",
                table: "PcDailyReports",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PcDailyHse",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PersonsInvolved = table.Column<int>(type: "int", nullable: false),
                    ActionTaken = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDailyHse", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDailyHse_PcDailyReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "PcDailyReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcDailyMachinery",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Ownership = table.Column<int>(type: "int", nullable: false),
                    Present = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<int>(type: "int", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDailyMachinery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDailyMachinery_PcDailyReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "PcDailyReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcDailyManpower",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ActivityType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Count = table.Column<int>(type: "int", nullable: false),
                    Present = table.Column<int>(type: "int", nullable: false),
                    Absent = table.Column<int>(type: "int", nullable: false),
                    PersonDays = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDailyManpower", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDailyManpower_PcDailyReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "PcDailyReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcDailyMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDailyMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDailyMaterials_PcDailyReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "PcDailyReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcDailyObstacles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    ImpactHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Resolved = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcDailyObstacles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcDailyObstacles_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcDailyObstacles_PcDailyReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "PcDailyReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    OwnerPositionId = table.Column<int>(type: "int", nullable: true),
                    RaisedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    DelayReasonCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcIssues_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcIssues_OfPositions_OwnerPositionId",
                        column: x => x.OwnerPositionId,
                        principalTable: "OfPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcIssues_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcLessons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Phase = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    IsPositive = table.Column<bool>(type: "bit", nullable: false),
                    WhatHappened = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Cause = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Recommendation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_PcLessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcLessons_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcPartyEvaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PartyKind = table.Column<int>(type: "int", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    PartyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EvaluationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    TotalScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EvaluatorUserId = table.Column<int>(type: "int", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcPartyEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcPartyEvaluations_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcPhaseEvaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    EvaluationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScheduleScore = table.Column<int>(type: "int", nullable: false),
                    CostScore = table.Column<int>(type: "int", nullable: false),
                    QualityScore = table.Column<int>(type: "int", nullable: false),
                    ScopeScore = table.Column<int>(type: "int", nullable: false),
                    OverallScore = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Achievements = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Problems = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Recommendation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_PcPhaseEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcPhaseEvaluations_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcProgressChainLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PositionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProgressChainLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcProgressChainLevels_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcProgressChainLevels_OfPositions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "OfPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcProjectDeliverables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    WbsNodeId = table.Column<int>(type: "int", nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    MdrDeliverableId = table.Column<int>(type: "int", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeliveredByUserId = table.Column<int>(type: "int", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptedByUserId = table.Column<int>(type: "int", nullable: true),
                    AcceptanceNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProjectDeliverables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcProjectDeliverables_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcProjectDeliverables_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcProjectDeliverables_PcEngineeringDeliverables_MdrDeliverableId",
                        column: x => x.MdrDeliverableId,
                        principalTable: "PcEngineeringDeliverables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcProjectDeliverables_PcWbsNodes_WbsNodeId",
                        column: x => x.WbsNodeId,
                        principalTable: "PcWbsNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcProjectPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TakenDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WbsNodeId = table.Column<int>(type: "int", nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    IsVoid = table.Column<bool>(type: "bit", nullable: false),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidedByUserId = table.Column<int>(type: "int", nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProjectPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcProjectPhotos_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcProjectPhotos_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcProjectPhotos_PcWbsNodes_WbsNodeId",
                        column: x => x.WbsNodeId,
                        principalTable: "PcWbsNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcRisks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Cause = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Event = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Effect = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    IsOpportunity = table.Column<bool>(type: "bit", nullable: false),
                    Probability = table.Column<int>(type: "int", nullable: false),
                    Impact = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    OwnerPositionId = table.Column<int>(type: "int", nullable: true),
                    Response = table.Column<int>(type: "int", nullable: true),
                    ResponsePlan = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResidualProbability = table.Column<int>(type: "int", nullable: true),
                    ResidualImpact = table.Column<int>(type: "int", nullable: true),
                    ResidualScore = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClosureNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcRisks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcRisks_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcRisks_OfPositions_OwnerPositionId",
                        column: x => x.OwnerPositionId,
                        principalTable: "OfPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcStakeholders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Organization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RoleTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Influence = table.Column<int>(type: "int", nullable: false),
                    Interest = table.Column<int>(type: "int", nullable: false),
                    EngagementCurrent = table.Column<int>(type: "int", nullable: false),
                    EngagementDesired = table.Column<int>(type: "int", nullable: false),
                    Expectations = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CommunicationNeed = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CommunicationFrequency = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OwnerPositionId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_PcStakeholders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcStakeholders_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcStakeholders_OfPositions_OwnerPositionId",
                        column: x => x.OwnerPositionId,
                        principalTable: "OfPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcWeeklyReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    WeekStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WeekEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    AutoJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NextWeekPlan = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastEditorUserId = table.Column<int>(type: "int", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcWeeklyReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcWeeklyReports_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcPartyEvaluationScores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EvaluationId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Criterion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcPartyEvaluationScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcPartyEvaluationScores_PcPartyEvaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "PcPartyEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TmTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Importance = table.Column<int>(type: "int", nullable: false),
                    Confidentiality = table.Column<int>(type: "int", nullable: false),
                    RequestType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Requester = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReferringOrganisation = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EstimatedHours = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    ActualHours = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ActualCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    PercentComplete = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ApprovedPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StatusBeforeSuspend = table.Column<int>(type: "int", nullable: true),
                    StatusReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true),
                    Recurrence = table.Column<int>(type: "int", nullable: false),
                    RecurrenceInterval = table.Column<int>(type: "int", nullable: false),
                    RecurrenceEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecurrenceSourceId = table.Column<int>(type: "int", nullable: true),
                    ReminderDays = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ParentTaskId = table.Column<int>(type: "int", nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    WbsNodeId = table.Column<int>(type: "int", nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    DecisionId = table.Column<int>(type: "int", nullable: true),
                    LetterId = table.Column<int>(type: "int", nullable: true),
                    RequestId = table.Column<int>(type: "int", nullable: true),
                    RiskId = table.Column<int>(type: "int", nullable: true),
                    IssueId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TmTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TmTasks_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TmTasks_OfDecisions_DecisionId",
                        column: x => x.DecisionId,
                        principalTable: "OfDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TmTasks_OfLetters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "OfLetters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TmTasks_OfRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "OfRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TmTasks_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TmTasks_PcIssues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "PcIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TmTasks_PcRisks_RiskId",
                        column: x => x.RiskId,
                        principalTable: "PcRisks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TmTasks_PcWbsNodes_WbsNodeId",
                        column: x => x.WbsNodeId,
                        principalTable: "PcWbsNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TmTasks_TmTasks_ParentTaskId",
                        column: x => x.ParentTaskId,
                        principalTable: "TmTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcProgressSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    Target = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    TaskId = table.Column<int>(type: "int", nullable: true),
                    Percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    PreviousPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ActualStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualFinish = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualCost = table.Column<decimal>(type: "decimal(22,2)", precision: 22, scale: 2, nullable: false),
                    CostKind = table.Column<int>(type: "int", nullable: false),
                    CostNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PercentSetByUserId = table.Column<int>(type: "int", nullable: true),
                    CostSetByUserId = table.Column<int>(type: "int", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    CurrentLevel = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: true),
                    ResultNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ManualProgressId = table.Column<int>(type: "int", nullable: true),
                    CostEntryId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProgressSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcProgressSubmissions_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcProgressSubmissions_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcProgressSubmissions_TmTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TmTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TmTaskAssignees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    PositionId = table.Column<int>(type: "int", nullable: true),
                    Role = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AssignedByUserId = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedByUserId = table.Column<int>(type: "int", nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TmTaskAssignees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TmTaskAssignees_OfPositions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "OfPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TmTaskAssignees_TmTaskAssignees_ParentId",
                        column: x => x.ParentId,
                        principalTable: "TmTaskAssignees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TmTaskAssignees_TmTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TmTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TmTaskChecklistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Done = table.Column<bool>(type: "bit", nullable: false),
                    DoneByUserId = table.Column<int>(type: "int", nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TmTaskChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TmTaskChecklistItems_TmTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TmTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TmTaskComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TmTaskComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TmTaskComments_TmTaskComments_ParentId",
                        column: x => x.ParentId,
                        principalTable: "TmTaskComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TmTaskComments_TmTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TmTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TmTaskHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    FromValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ToValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TmTaskHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TmTaskHistory_TmTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TmTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TmTaskWatchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TmTaskWatchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TmTaskWatchers_TmTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TmTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PcProgressApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmissionId = table.Column<int>(type: "int", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PositionId = table.Column<int>(type: "int", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProgressApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcProgressApprovals_OfPositions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "OfPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PcProgressApprovals_PcProgressSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "PcProgressSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PcProgressSubmissionHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmissionId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangesJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcProgressSubmissionHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcProgressSubmissionHistory_PcProgressSubmissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "PcProgressSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PcDailyHse_ReportId",
                table: "PcDailyHse",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDailyMachinery_ReportId",
                table: "PcDailyMachinery",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDailyManpower_ReportId",
                table: "PcDailyManpower",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDailyMaterials_ReportId",
                table: "PcDailyMaterials",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDailyObstacles_ActivityId",
                table: "PcDailyObstacles",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcDailyObstacles_ReportId",
                table: "PcDailyObstacles",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_PcIssues_ActivityId",
                table: "PcIssues",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcIssues_ContractId_Number",
                table: "PcIssues",
                columns: new[] { "ContractId", "Number" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcIssues_ContractId_Status",
                table: "PcIssues",
                columns: new[] { "ContractId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PcIssues_OwnerPositionId",
                table: "PcIssues",
                column: "OwnerPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_PcLessons_ContractId_Number",
                table: "PcLessons",
                columns: new[] { "ContractId", "Number" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcPartyEvaluations_ContractId_Number",
                table: "PcPartyEvaluations",
                columns: new[] { "ContractId", "Number" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcPartyEvaluationScores_EvaluationId",
                table: "PcPartyEvaluationScores",
                column: "EvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_PcPhaseEvaluations_ContractId_Phase",
                table: "PcPhaseEvaluations",
                columns: new[] { "ContractId", "Phase" });

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressApprovals_PositionId",
                table: "PcProgressApprovals",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressApprovals_SubmissionId_Round_Level",
                table: "PcProgressApprovals",
                columns: new[] { "SubmissionId", "Round", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressChainLevels_ContractId_Level",
                table: "PcProgressChainLevels",
                columns: new[] { "ContractId", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressChainLevels_PositionId",
                table: "PcProgressChainLevels",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressSubmissionHistory_SubmissionId_At",
                table: "PcProgressSubmissionHistory",
                columns: new[] { "SubmissionId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressSubmissions_ActivityId",
                table: "PcProgressSubmissions",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressSubmissions_ContractId_State",
                table: "PcProgressSubmissions",
                columns: new[] { "ContractId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressSubmissions_Number",
                table: "PcProgressSubmissions",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcProgressSubmissions_TaskId",
                table: "PcProgressSubmissions",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_PcProjectDeliverables_ActivityId",
                table: "PcProjectDeliverables",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcProjectDeliverables_ContractId_Status",
                table: "PcProjectDeliverables",
                columns: new[] { "ContractId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PcProjectDeliverables_MdrDeliverableId",
                table: "PcProjectDeliverables",
                column: "MdrDeliverableId");

            migrationBuilder.CreateIndex(
                name: "IX_PcProjectDeliverables_WbsNodeId",
                table: "PcProjectDeliverables",
                column: "WbsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PcProjectPhotos_ActivityId",
                table: "PcProjectPhotos",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PcProjectPhotos_ContractId_TakenDate",
                table: "PcProjectPhotos",
                columns: new[] { "ContractId", "TakenDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PcProjectPhotos_WbsNodeId",
                table: "PcProjectPhotos",
                column: "WbsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PcRisks_ContractId_Number",
                table: "PcRisks",
                columns: new[] { "ContractId", "Number" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PcRisks_ContractId_Status",
                table: "PcRisks",
                columns: new[] { "ContractId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PcRisks_OwnerPositionId",
                table: "PcRisks",
                column: "OwnerPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_PcStakeholders_ContractId",
                table: "PcStakeholders",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PcStakeholders_OwnerPositionId",
                table: "PcStakeholders",
                column: "OwnerPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_PcWeeklyReports_ContractId_WeekStart",
                table: "PcWeeklyReports",
                columns: new[] { "ContractId", "WeekStart" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TmTaskAssignees_ParentId",
                table: "TmTaskAssignees",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTaskAssignees_PositionId_State",
                table: "TmTaskAssignees",
                columns: new[] { "PositionId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_TmTaskAssignees_TaskId_State",
                table: "TmTaskAssignees",
                columns: new[] { "TaskId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_TmTaskAssignees_UserId_State",
                table: "TmTaskAssignees",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_TmTaskChecklistItems_TaskId",
                table: "TmTaskChecklistItems",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTaskComments_ParentId",
                table: "TmTaskComments",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTaskComments_TaskId",
                table: "TmTaskComments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTaskHistory_TaskId_At",
                table: "TmTaskHistory",
                columns: new[] { "TaskId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_ActivityId",
                table: "TmTasks",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_ContractId",
                table: "TmTasks",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_DecisionId",
                table: "TmTasks",
                column: "DecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_DueDate",
                table: "TmTasks",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_IssueId",
                table: "TmTasks",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_LetterId",
                table: "TmTasks",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_Number",
                table: "TmTasks",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_ParentTaskId",
                table: "TmTasks",
                column: "ParentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_RequestId",
                table: "TmTasks",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_RiskId",
                table: "TmTasks",
                column: "RiskId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_Status",
                table: "TmTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TmTasks_WbsNodeId",
                table: "TmTasks",
                column: "WbsNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TmTaskWatchers_TaskId_UserId",
                table: "TmTaskWatchers",
                columns: new[] { "TaskId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TmTaskWatchers_UserId",
                table: "TmTaskWatchers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PcDailyHse");

            migrationBuilder.DropTable(
                name: "PcDailyMachinery");

            migrationBuilder.DropTable(
                name: "PcDailyManpower");

            migrationBuilder.DropTable(
                name: "PcDailyMaterials");

            migrationBuilder.DropTable(
                name: "PcDailyObstacles");

            migrationBuilder.DropTable(
                name: "PcLessons");

            migrationBuilder.DropTable(
                name: "PcPartyEvaluationScores");

            migrationBuilder.DropTable(
                name: "PcPhaseEvaluations");

            migrationBuilder.DropTable(
                name: "PcProgressApprovals");

            migrationBuilder.DropTable(
                name: "PcProgressChainLevels");

            migrationBuilder.DropTable(
                name: "PcProgressSubmissionHistory");

            migrationBuilder.DropTable(
                name: "PcProjectDeliverables");

            migrationBuilder.DropTable(
                name: "PcProjectPhotos");

            migrationBuilder.DropTable(
                name: "PcStakeholders");

            migrationBuilder.DropTable(
                name: "PcWeeklyReports");

            migrationBuilder.DropTable(
                name: "TmTaskAssignees");

            migrationBuilder.DropTable(
                name: "TmTaskChecklistItems");

            migrationBuilder.DropTable(
                name: "TmTaskComments");

            migrationBuilder.DropTable(
                name: "TmTaskHistory");

            migrationBuilder.DropTable(
                name: "TmTaskWatchers");

            migrationBuilder.DropTable(
                name: "PcPartyEvaluations");

            migrationBuilder.DropTable(
                name: "PcProgressSubmissions");

            migrationBuilder.DropTable(
                name: "TmTasks");

            migrationBuilder.DropTable(
                name: "PcIssues");

            migrationBuilder.DropTable(
                name: "PcRisks");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "PcDailyReports");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "PcDailyReports");

            migrationBuilder.DropColumn(
                name: "Weather",
                table: "PcDailyReports");
        }
    }
}
