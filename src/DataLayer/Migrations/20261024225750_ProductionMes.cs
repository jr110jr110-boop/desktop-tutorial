using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class ProductionMes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CostCenter",
                table: "InvDocuments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MfCostRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PostingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RunByUserId = table.Column<int>(type: "int", nullable: false),
                    BookingCount = table.Column<int>(type: "int", nullable: false),
                    LabourHours = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    MachineHours = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    LabourAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OverheadAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployeeCount = table.Column<int>(type: "int", nullable: false),
                    PayrollEmployeeCount = table.Column<int>(type: "int", nullable: false),
                    LabourProtected = table.Column<bool>(type: "bit", nullable: false),
                    AccountingEventId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfCostRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfCostRuns_FinAccountingEvents_AccountingEventId",
                        column: x => x.AccountingEventId,
                        principalTable: "FinAccountingEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfMaintenancePlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    Task = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IntervalKind = table.Column<int>(type: "int", nullable: false),
                    IntervalValue = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastDoneAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Checklist = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EstimatedHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_MfMaintenancePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMaintenancePlans_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfReasonCodes",
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
                    table.PrimaryKey("PK_MfReasonCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MfRoutingRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    ItemGroupId = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProductionApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ProductionApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EngineeringApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    EngineeringApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_MfRoutingRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfRoutingRevisions_InvItemGroups_ItemGroupId",
                        column: x => x.ItemGroupId,
                        principalTable: "InvItemGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfRoutingRevisions_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfScheduleRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AsOfDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RunByUserId = table.Column<int>(type: "int", nullable: false),
                    Orders = table.Column<int>(type: "int", nullable: false),
                    Operations = table.Column<int>(type: "int", nullable: false),
                    LateOrders = table.Column<int>(type: "int", nullable: false),
                    Unscheduled = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfScheduleRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MfWorkCenters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CalendarId = table.Column<int>(type: "int", nullable: true),
                    ShiftsPerDay = table.Column<int>(type: "int", nullable: false),
                    HoursPerShift = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    EfficiencyPercent = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    UtilisationPercent = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    ParallelResources = table.Column<int>(type: "int", nullable: false),
                    MachineRateRial = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    LabourRateRial = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CostCenter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    QueueDaysDefault = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_MfWorkCenters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfWorkCenters_PcCalendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "PcCalendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfProductionOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    ProcessId = table.Column<int>(type: "int", nullable: true),
                    BomId = table.Column<int>(type: "int", nullable: true),
                    ActivityId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SerialsPlanned = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RoutingRevisionId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlannedFinish = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NeedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsLate = table.Column<bool>(type: "bit", nullable: false),
                    DaysLate = table.Column<int>(type: "int", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    KitReadyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    KitShortage = table.Column<bool>(type: "bit", nullable: false),
                    ScheduleNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActualStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActualFinish = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AutoDrafted = table.Column<bool>(type: "bit", nullable: false),
                    EditedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReleaseOverride = table.Column<bool>(type: "bit", nullable: false),
                    ReleaseOverrideReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ShortageText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    KitRequestId = table.Column<int>(type: "int", nullable: true),
                    KitOverrideReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    KitOverrideByUserId = table.Column<int>(type: "int", nullable: true),
                    ProductionReceiptId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_MfProductionOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfProductionOrders_Boms_BomId",
                        column: x => x.BomId,
                        principalTable: "Boms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfProductionOrders_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfProductionOrders_InvDocuments_ProductionReceiptId",
                        column: x => x.ProductionReceiptId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfProductionOrders_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfProductionOrders_InvMaterialRequests_KitRequestId",
                        column: x => x.KitRequestId,
                        principalTable: "InvMaterialRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfProductionOrders_MfRoutingRevisions_RoutingRevisionId",
                        column: x => x.RoutingRevisionId,
                        principalTable: "MfRoutingRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfProductionOrders_PcActivities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "PcActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfProductionOrders_Processes_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "Processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfProductionOrders_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfCostPostings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    PostingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    WbsNodeId = table.Column<int>(type: "int", nullable: true),
                    WorkCenterId = table.Column<int>(type: "int", nullable: true),
                    BookingCount = table.Column<int>(type: "int", nullable: false),
                    LabourHours = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    MachineHours = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    LabourAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OverheadAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployeeCount = table.Column<int>(type: "int", nullable: false),
                    PayrollEmployeeCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfCostPostings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfCostPostings_MfCostRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "MfCostRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfCostPostings_MfWorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "MfWorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfCostPostings_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfMachineInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    WorkCenterId = table.Column<int>(type: "int", nullable: true),
                    AssetId = table.Column<int>(type: "int", nullable: true),
                    Maker = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Capabilities = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfMachineInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMachineInfos_FnAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "FnAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMachineInfos_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMachineInfos_MfWorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "MfWorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfOperationStandards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationTemplateId = table.Column<int>(type: "int", nullable: false),
                    WorkCenterId = table.Column<int>(type: "int", nullable: true),
                    SetupHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    RunHoursPerUnit = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MoveDays = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    QueueDays = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    IsOutside = table.Column<bool>(type: "bit", nullable: false),
                    DefaultSupplierId = table.Column<int>(type: "int", nullable: true),
                    OutsideLeadDays = table.Column<int>(type: "int", nullable: false),
                    WorkInstructionDocumentId = table.Column<int>(type: "int", nullable: true),
                    DrawingReferenceRule = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequiredQualificationKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RequiredQualificationProcess = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InspectionRequirement = table.Column<int>(type: "int", nullable: false),
                    ItpTestType = table.Column<int>(type: "int", nullable: true),
                    FirstArticleRequired = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfOperationStandards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfOperationStandards_InvOperationTemplates_OperationTemplateId",
                        column: x => x.OperationTemplateId,
                        principalTable: "InvOperationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperationStandards_MfWorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "MfWorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperationStandards_Providers_DefaultSupplierId",
                        column: x => x.DefaultSupplierId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperationStandards_QaDocuments_WorkInstructionDocumentId",
                        column: x => x.WorkInstructionDocumentId,
                        principalTable: "QaDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfOperations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    OperationTemplateId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    WorkCenterId = table.Column<int>(type: "int", nullable: true),
                    SetupHoursPlanned = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    RunHoursPlanned = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlannedFinish = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GoodQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ScrapQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReworkQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InspectionRequirement = table.Column<int>(type: "int", nullable: false),
                    ItpTestType = table.Column<int>(type: "int", nullable: true),
                    ItpActivityId = table.Column<int>(type: "int", nullable: true),
                    QcAccepted = table.Column<bool>(type: "bit", nullable: false),
                    QcRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    QcRecordId = table.Column<int>(type: "int", nullable: true),
                    NcrId = table.Column<int>(type: "int", nullable: true),
                    IsOutside = table.Column<bool>(type: "bit", nullable: false),
                    OutsideSupplierId = table.Column<int>(type: "int", nullable: true),
                    OutsideLeadDays = table.Column<int>(type: "int", nullable: false),
                    PurchaseRequestId = table.Column<int>(type: "int", nullable: true),
                    PurchaseRequestLineId = table.Column<int>(type: "int", nullable: true),
                    PurchaseOrderLineId = table.Column<int>(type: "int", nullable: true),
                    SubcontractIssueId = table.Column<int>(type: "int", nullable: true),
                    OutsideDrafted = table.Column<bool>(type: "bit", nullable: false),
                    IsRework = table.Column<bool>(type: "bit", nullable: false),
                    ReworkOfOperationId = table.Column<int>(type: "int", nullable: true),
                    SkipReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_MfOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfOperations_InvDocuments_SubcontractIssueId",
                        column: x => x.SubcontractIssueId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperations_InvNonConformances_NcrId",
                        column: x => x.NcrId,
                        principalTable: "InvNonConformances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperations_InvOperationTemplates_OperationTemplateId",
                        column: x => x.OperationTemplateId,
                        principalTable: "InvOperationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperations_MfOperations_ReworkOfOperationId",
                        column: x => x.ReworkOfOperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperations_MfProductionOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "MfProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MfOperations_MfWorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "MfWorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperations_ProcPurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "ProcPurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperations_ProcPurchaseRequestLines_PurchaseRequestLineId",
                        column: x => x.PurchaseRequestLineId,
                        principalTable: "ProcPurchaseRequestLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperations_ProcPurchaseRequests_PurchaseRequestId",
                        column: x => x.PurchaseRequestId,
                        principalTable: "ProcPurchaseRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperations_Providers_OutsideSupplierId",
                        column: x => x.OutsideSupplierId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperations_QaItpActivities_ItpActivityId",
                        column: x => x.ItpActivityId,
                        principalTable: "QaItpActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperations_QaRecords_QcRecordId",
                        column: x => x.QcRecordId,
                        principalTable: "QaRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfOrderComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfOrderComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfOrderComponents_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOrderComponents_MfProductionOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "MfProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfMaintenanceRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    WorkCenterId = table.Column<int>(type: "int", nullable: true),
                    OperationId = table.Column<int>(type: "int", nullable: true),
                    ReasonCodeId = table.Column<int>(type: "int", nullable: true),
                    ReportedByUserId = table.Column<int>(type: "int", nullable: false),
                    ReportedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReportedAtText = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfMaintenanceRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceRequests_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceRequests_MfOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceRequests_MfReasonCodes_ReasonCodeId",
                        column: x => x.ReasonCodeId,
                        principalTable: "MfReasonCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceRequests_MfWorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "MfWorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfOperationParts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<int>(type: "int", nullable: false),
                    PartOperationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfOperationParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfOperationParts_InvPartOperations_PartOperationId",
                        column: x => x.PartOperationId,
                        principalTable: "InvPartOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfOperationParts_MfOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfTimeBookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    GoodQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ScrapQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ScrapReasonCodeId = table.Column<int>(type: "int", nullable: true),
                    ReworkQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReasonCodeId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    SupervisorReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EditReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CostRunId = table.Column<int>(type: "int", nullable: true),
                    CostPostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfTimeBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfTimeBookings_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfTimeBookings_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfTimeBookings_MfCostRuns_CostRunId",
                        column: x => x.CostRunId,
                        principalTable: "MfCostRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfTimeBookings_MfOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "MfOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfTimeBookings_MfReasonCodes_ReasonCodeId",
                        column: x => x.ReasonCodeId,
                        principalTable: "MfReasonCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfTimeBookings_MfReasonCodes_ScrapReasonCodeId",
                        column: x => x.ScrapReasonCodeId,
                        principalTable: "MfReasonCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfMaintenanceOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MachineId = table.Column<int>(type: "int", nullable: false),
                    WorkCenterId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: true),
                    RequestId = table.Column<int>(type: "int", nullable: true),
                    Task = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Checklist = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EstimatedHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    DowntimeStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DowntimeEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Cause = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActionTaken = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CostCenter = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MaterialRequestId = table.Column<int>(type: "int", nullable: true),
                    IssueDocumentId = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedByUserId = table.Column<int>(type: "int", nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneByUserId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_MfMaintenanceOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceOrders_InvDocuments_IssueDocumentId",
                        column: x => x.IssueDocumentId,
                        principalTable: "InvDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceOrders_InvMaterialRequests_MaterialRequestId",
                        column: x => x.MaterialRequestId,
                        principalTable: "InvMaterialRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceOrders_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceOrders_MfMaintenancePlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "MfMaintenancePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceOrders_MfMaintenanceRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "MfMaintenanceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceOrders_MfWorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "MfWorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MfMaintenanceLabours",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    WorkDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfMaintenanceLabours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceLabours_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceLabours_MfMaintenanceOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "MfMaintenanceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MfMaintenanceParts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfMaintenanceParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceParts_InvItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "InvItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MfMaintenanceParts_MfMaintenanceOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "MfMaintenanceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "Description", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { 20, "00000020-0000-0000-0000-000000000000", null, null, null, null, "مدیر تولید", null, null, null, null, "PRODUCTIONMANAGER", "PRODUCTIONMANAGER" },
                    { 21, "00000021-0000-0000-0000-000000000000", null, null, null, null, "سرپرست کارگاه", null, null, null, null, "SHOPSUPERVISOR", "SHOPSUPERVISOR" },
                    { 22, "00000022-0000-0000-0000-000000000000", null, null, null, null, "اپراتور", null, null, null, null, "OPERATOR", "OPERATOR" },
                    { 23, "00000023-0000-0000-0000-000000000000", null, null, null, null, "نگهداری و تعمیرات", null, null, null, null, "MAINTENANCE", "MAINTENANCE" }
                });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 2000, "permission", "Warehouse", null, null, null, null, null, null, null, null, 20 },
                    { 2001, "permission", "Traceability", null, null, null, null, null, null, null, null, 20 },
                    { 2100, "permission", "Warehouse", null, null, null, null, null, null, null, null, 21 },
                    { 2101, "permission", "Traceability", null, null, null, null, null, null, null, null, 21 },
                    { 2200, "permission", "Traceability", null, null, null, null, null, null, null, null, 22 },
                    { 2300, "permission", "Warehouse", null, null, null, null, null, null, null, null, 23 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MfCostPostings_RunId",
                table: "MfCostPostings",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_MfCostPostings_WorkCenterId",
                table: "MfCostPostings",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_MfCostPostings_WorkOrderId_PostingDate",
                table: "MfCostPostings",
                columns: new[] { "WorkOrderId", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_MfCostRuns_AccountingEventId",
                table: "MfCostRuns",
                column: "AccountingEventId");

            migrationBuilder.CreateIndex(
                name: "IX_MfCostRuns_Number",
                table: "MfCostRuns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MfCostRuns_PostingDate",
                table: "MfCostRuns",
                column: "PostingDate");

            migrationBuilder.CreateIndex(
                name: "IX_MfMachineInfos_AssetId",
                table: "MfMachineInfos",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMachineInfos_MachineId",
                table: "MfMachineInfos",
                column: "MachineId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfMachineInfos_WorkCenterId",
                table: "MfMachineInfos",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceLabours_EmployeeId",
                table: "MfMaintenanceLabours",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceLabours_OrderId",
                table: "MfMaintenanceLabours",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceOrders_IssueDocumentId",
                table: "MfMaintenanceOrders",
                column: "IssueDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceOrders_MachineId_Status",
                table: "MfMaintenanceOrders",
                columns: new[] { "MachineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceOrders_MaterialRequestId",
                table: "MfMaintenanceOrders",
                column: "MaterialRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceOrders_Number",
                table: "MfMaintenanceOrders",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceOrders_PlanId",
                table: "MfMaintenanceOrders",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceOrders_RequestId",
                table: "MfMaintenanceOrders",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceOrders_WorkCenterId",
                table: "MfMaintenanceOrders",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceParts_ItemId",
                table: "MfMaintenanceParts",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceParts_OrderId",
                table: "MfMaintenanceParts",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenancePlans_MachineId",
                table: "MfMaintenancePlans",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceRequests_MachineId_Status",
                table: "MfMaintenanceRequests",
                columns: new[] { "MachineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceRequests_Number",
                table: "MfMaintenanceRequests",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceRequests_OperationId",
                table: "MfMaintenanceRequests",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceRequests_ReasonCodeId",
                table: "MfMaintenanceRequests",
                column: "ReasonCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MfMaintenanceRequests_WorkCenterId",
                table: "MfMaintenanceRequests",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperationParts_OperationId",
                table: "MfOperationParts",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperationParts_PartOperationId",
                table: "MfOperationParts",
                column: "PartOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_ItpActivityId",
                table: "MfOperations",
                column: "ItpActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_NcrId",
                table: "MfOperations",
                column: "NcrId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_OperationTemplateId",
                table: "MfOperations",
                column: "OperationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_OrderId_Sequence",
                table: "MfOperations",
                columns: new[] { "OrderId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_OutsideSupplierId",
                table: "MfOperations",
                column: "OutsideSupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_PurchaseOrderLineId",
                table: "MfOperations",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_PurchaseRequestId",
                table: "MfOperations",
                column: "PurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_PurchaseRequestLineId",
                table: "MfOperations",
                column: "PurchaseRequestLineId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_QcRecordId",
                table: "MfOperations",
                column: "QcRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_ReworkOfOperationId",
                table: "MfOperations",
                column: "ReworkOfOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_Status",
                table: "MfOperations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_SubcontractIssueId",
                table: "MfOperations",
                column: "SubcontractIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperations_WorkCenterId",
                table: "MfOperations",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperationStandards_DefaultSupplierId",
                table: "MfOperationStandards",
                column: "DefaultSupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperationStandards_OperationTemplateId",
                table: "MfOperationStandards",
                column: "OperationTemplateId",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperationStandards_WorkCenterId",
                table: "MfOperationStandards",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOperationStandards_WorkInstructionDocumentId",
                table: "MfOperationStandards",
                column: "WorkInstructionDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOrderComponents_ItemId",
                table: "MfOrderComponents",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MfOrderComponents_OrderId",
                table: "MfOrderComponents",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_ActivityId",
                table: "MfProductionOrders",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_BomId",
                table: "MfProductionOrders",
                column: "BomId");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_ContractId",
                table: "MfProductionOrders",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_ItemId",
                table: "MfProductionOrders",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_KitRequestId",
                table: "MfProductionOrders",
                column: "KitRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_Number",
                table: "MfProductionOrders",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_ProcessId",
                table: "MfProductionOrders",
                column: "ProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_ProductionReceiptId",
                table: "MfProductionOrders",
                column: "ProductionReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_RoutingRevisionId",
                table: "MfProductionOrders",
                column: "RoutingRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_Status",
                table: "MfProductionOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MfProductionOrders_WorkOrderId",
                table: "MfProductionOrders",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MfReasonCodes_Kind_Code",
                table: "MfReasonCodes",
                columns: new[] { "Kind", "Code" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MfRoutingRevisions_ItemGroupId_Revision",
                table: "MfRoutingRevisions",
                columns: new[] { "ItemGroupId", "Revision" },
                unique: true,
                filter: "[Deleted] = 0 AND [ItemGroupId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MfRoutingRevisions_ItemId_Revision",
                table: "MfRoutingRevisions",
                columns: new[] { "ItemId", "Revision" },
                unique: true,
                filter: "[Deleted] = 0 AND [ItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MfScheduleRuns_AsOfDate",
                table: "MfScheduleRuns",
                column: "AsOfDate");

            migrationBuilder.CreateIndex(
                name: "IX_MfTimeBookings_CostRunId",
                table: "MfTimeBookings",
                column: "CostRunId");

            migrationBuilder.CreateIndex(
                name: "IX_MfTimeBookings_EmployeeId_StartAt",
                table: "MfTimeBookings",
                columns: new[] { "EmployeeId", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MfTimeBookings_MachineId",
                table: "MfTimeBookings",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_MfTimeBookings_OneRunning",
                table: "MfTimeBookings",
                column: "EmployeeId",
                unique: true,
                filter: "[Deleted] = 0 AND [EndAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MfTimeBookings_OperationId",
                table: "MfTimeBookings",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_MfTimeBookings_ReasonCodeId",
                table: "MfTimeBookings",
                column: "ReasonCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MfTimeBookings_ScrapReasonCodeId",
                table: "MfTimeBookings",
                column: "ScrapReasonCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_MfWorkCenters_CalendarId",
                table: "MfWorkCenters",
                column: "CalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_MfWorkCenters_Code",
                table: "MfWorkCenters",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MfCostPostings");

            migrationBuilder.DropTable(
                name: "MfMachineInfos");

            migrationBuilder.DropTable(
                name: "MfMaintenanceLabours");

            migrationBuilder.DropTable(
                name: "MfMaintenanceParts");

            migrationBuilder.DropTable(
                name: "MfOperationParts");

            migrationBuilder.DropTable(
                name: "MfOperationStandards");

            migrationBuilder.DropTable(
                name: "MfOrderComponents");

            migrationBuilder.DropTable(
                name: "MfScheduleRuns");

            migrationBuilder.DropTable(
                name: "MfTimeBookings");

            migrationBuilder.DropTable(
                name: "MfMaintenanceOrders");

            migrationBuilder.DropTable(
                name: "MfCostRuns");

            migrationBuilder.DropTable(
                name: "MfMaintenancePlans");

            migrationBuilder.DropTable(
                name: "MfMaintenanceRequests");

            migrationBuilder.DropTable(
                name: "MfOperations");

            migrationBuilder.DropTable(
                name: "MfReasonCodes");

            migrationBuilder.DropTable(
                name: "MfProductionOrders");

            migrationBuilder.DropTable(
                name: "MfWorkCenters");

            migrationBuilder.DropTable(
                name: "MfRoutingRevisions");

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2000);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2001);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2100);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2101);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2200);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2300);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 22);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 23);

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "InvDocuments");
        }
    }
}
