using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class OfficeAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OfMeetingId",
                table: "QaManagementReviews",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OfAttendanceDays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    NonWorkingDay = table.Column<bool>(type: "bit", nullable: false),
                    FirstIn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastOut = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WorkedMinutes = table.Column<int>(type: "int", nullable: false),
                    DelayMinutes = table.Column<int>(type: "int", nullable: false),
                    EarlyLeaveMinutes = table.Column<int>(type: "int", nullable: false),
                    OvertimeMinutes = table.Column<int>(type: "int", nullable: false),
                    NightMinutes = table.Column<int>(type: "int", nullable: false),
                    HolidayMinutes = table.Column<int>(type: "int", nullable: false),
                    AbsenceDays = table.Column<int>(type: "int", nullable: false),
                    UnpaidLeaveDays = table.Column<int>(type: "int", nullable: false),
                    PaidLeaveDays = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MissionDays = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfAttendanceDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfAttendanceDays_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfAttendanceMonths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: false),
                    SheetCount = table.Column<int>(type: "int", nullable: false),
                    SkippedLocked = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfAttendanceMonths", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfAttendancePolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GraceMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxOvertimeMinutesPerDay = table.Column<int>(type: "int", nullable: true),
                    OvertimeNeedsPreApproval = table.Column<bool>(type: "bit", nullable: false),
                    RoundingMinutes = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfAttendancePolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfClockEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeviceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfClockEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfClockEvents_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApiKeyHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastPushAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfExternalParties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfExternalParties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfInboxItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipientPositionId = table.Column<int>(type: "int", nullable: true),
                    RecipientUserId = table.Column<int>(type: "int", nullable: true),
                    RecipientRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SourceModule = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    SourceKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Link = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfInboxItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfRequestTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovalChain = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TargetNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfRequestTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfShifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    StartMinute = table.Column<int>(type: "int", nullable: false),
                    EndMinute = table.Column<int>(type: "int", nullable: false),
                    BreakMinutes = table.Column<int>(type: "int", nullable: false),
                    NightStartMinute = table.Column<int>(type: "int", nullable: false),
                    NightEndMinute = table.Column<int>(type: "int", nullable: false),
                    WorkDaysMask = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfShifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfUserSignatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ImageDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfUserSignatures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    RequesterUserId = table.Column<int>(type: "int", nullable: false),
                    ValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_OfRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfRequests_OfRequestTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "OfRequestTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfShiftAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: false),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfShiftAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfShiftAssignments_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfShiftAssignments_OfShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "OfShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MeetingId = table.Column<int>(type: "int", nullable: false),
                    AgendaId = table.Column<int>(type: "int", nullable: true),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    OwnerPositionId = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DoneNote = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneByUserId = table.Column<int>(type: "int", nullable: true),
                    DoneOnBehalf = table.Column<bool>(type: "bit", nullable: false),
                    EvidenceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfDecisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfDelegations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PositionId = table.Column<int>(type: "int", nullable: false),
                    DeputyUserId = table.Column<int>(type: "int", nullable: false),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfDelegations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfLeaveRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromMinute = table.Column<int>(type: "int", nullable: true),
                    ToMinute = table.Column<int>(type: "int", nullable: true),
                    Hours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    Direction = table.Column<int>(type: "int", nullable: true),
                    Days = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AttachmentDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApproverPositionId = table.Column<int>(type: "int", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedOnBehalf = table.Column<bool>(type: "bit", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApproverComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfLeaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfLeaveRequests_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfLetterApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    PositionId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ActedByUserId = table.Column<int>(type: "int", nullable: true),
                    OnBehalf = table.Column<bool>(type: "bit", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfLetterApprovals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfLetterAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfLetterAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfLetterRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    PositionId = table.Column<int>(type: "int", nullable: true),
                    PartyId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfLetterRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfLetterRecipients_OfExternalParties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "OfExternalParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfLetters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NumberRuleId = table.Column<int>(type: "int", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SenderPositionId = table.Column<int>(type: "int", nullable: true),
                    SenderPartyId = table.Column<int>(type: "int", nullable: true),
                    Classification = table.Column<int>(type: "int", nullable: false),
                    Urgency = table.Column<int>(type: "int", nullable: false),
                    IncomingNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IncomingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SignerPositionId = table.Column<int>(type: "int", nullable: true),
                    ApprovalRound = table.Column<int>(type: "int", nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SignedByUserId = table.Column<int>(type: "int", nullable: true),
                    SignedOnBehalfOfPositionId = table.Column<int>(type: "int", nullable: true),
                    SignatureDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaperCopy = table.Column<bool>(type: "bit", nullable: false),
                    DeliveryMethod = table.Column<int>(type: "int", nullable: true),
                    SentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryReceipt = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DeliveryReceiptDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceiptDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplyToLetterId = table.Column<int>(type: "int", nullable: true),
                    FollowUpLetterId = table.Column<int>(type: "int", nullable: true),
                    TemplateId = table.Column<int>(type: "int", nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RegisteredByUserId = table.Column<int>(type: "int", nullable: true),
                    Imported = table.Column<bool>(type: "bit", nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfLetters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfLetters_OfExternalParties_SenderPartyId",
                        column: x => x.SenderPartyId,
                        principalTable: "OfExternalParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfLetters_OfLetters_FollowUpLetterId",
                        column: x => x.FollowUpLetterId,
                        principalTable: "OfLetters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfLetters_OfLetters_ReplyToLetterId",
                        column: x => x.ReplyToLetterId,
                        principalTable: "OfLetters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfLetterTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    LetterKind = table.Column<int>(type: "int", nullable: true),
                    HeaderHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FooterHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovalChain = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SignerPositionId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfLetterTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfMeetingAgenda",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MeetingId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Minutes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfMeetingAgenda", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfMeetingAttendees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MeetingId = table.Column<int>(type: "int", nullable: false),
                    PositionId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    ExternalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Present = table.Column<bool>(type: "bit", nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfMeetingAttendees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfMeetings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Place = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChairPositionId = table.Column<int>(type: "int", nullable: true),
                    SecretaryPositionId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MinutesApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MinutesApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    MinutesApprovedOnBehalf = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_OfMeetings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfRecordLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: true),
                    MeetingId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    RecordId = table.Column<int>(type: "int", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfRecordLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfRecordLinks_OfLetters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "OfLetters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfRecordLinks_OfMeetings_MeetingId",
                        column: x => x.MeetingId,
                        principalTable: "OfMeetings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfNumberCounters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Last = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfNumberCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfNumberingRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    Pattern = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfNumberingRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfPositions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    IsManager = table.Column<bool>(type: "bit", nullable: false),
                    CanSign = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfPositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfPositions_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfReferrals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LetterId = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    FromPositionId = table.Column<int>(type: "int", nullable: true),
                    ToPositionId = table.Column<int>(type: "int", nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Action = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    OnBehalf = table.Column<bool>(type: "bit", nullable: false),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneByUserId = table.Column<int>(type: "int", nullable: true),
                    DoneOnBehalf = table.Column<bool>(type: "bit", nullable: false),
                    DoneNote = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfReferrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfReferrals_OfLetters_LetterId",
                        column: x => x.LetterId,
                        principalTable: "OfLetters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfReferrals_OfPositions_FromPositionId",
                        column: x => x.FromPositionId,
                        principalTable: "OfPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfReferrals_OfPositions_ToPositionId",
                        column: x => x.ToPositionId,
                        principalTable: "OfPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfReferrals_OfReferrals_ParentId",
                        column: x => x.ParentId,
                        principalTable: "OfReferrals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfRequestApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<int>(type: "int", nullable: false),
                    PositionId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ActedByUserId = table.Column<int>(type: "int", nullable: true),
                    OnBehalf = table.Column<bool>(type: "bit", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfRequestApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfRequestApprovals_OfPositions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "OfPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfRequestApprovals_OfRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "OfRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    ManagerPositionId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfUnits_OfPositions_ManagerPositionId",
                        column: x => x.ManagerPositionId,
                        principalTable: "OfPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfUnits_OfUnits_ParentId",
                        column: x => x.ParentId,
                        principalTable: "OfUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfReferralEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReferralId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    OnBehalf = table.Column<bool>(type: "bit", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfReferralEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfReferralEntries_OfReferrals_ReferralId",
                        column: x => x.ReferralId,
                        principalTable: "OfReferrals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "Description", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { 26, "00000026-0000-0000-0000-000000000000", null, null, null, null, "دبیرخانه", null, null, null, null, "SECRETARIAT", "SECRETARIAT" },
                    { 27, "00000027-0000-0000-0000-000000000000", null, null, null, null, "اداری و منابع انسانی", null, null, null, null, "HR", "HR" }
                });

            migrationBuilder.InsertData(
                table: "AppRoleClaims",
                columns: new[] { "Id", "ClaimType", "ClaimValue", "CreatedByBrowserName", "CreatedByIp", "CreatedByUserId", "CreatedDateTime", "ModifiedByBrowserName", "ModifiedByIp", "ModifiedByUserId", "ModifiedDateTime", "RoleId" },
                values: new object[,]
                {
                    { 2600, "permission", "Warehouse", null, null, null, null, null, null, null, null, 26 },
                    { 2700, "permission", "Warehouse", null, null, null, null, null, null, null, null, 27 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_QaManagementReviews_OfMeetingId",
                table: "QaManagementReviews",
                column: "OfMeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_OfAttendanceDays_EmployeeId_Date",
                table: "OfAttendanceDays",
                columns: new[] { "EmployeeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfAttendanceMonths_Year_Month",
                table: "OfAttendanceMonths",
                columns: new[] { "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfClockEvents_EmployeeId_Timestamp",
                table: "OfClockEvents",
                columns: new[] { "EmployeeId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_OfDecisions_MeetingId",
                table: "OfDecisions",
                column: "MeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_OfDecisions_OwnerPositionId",
                table: "OfDecisions",
                column: "OwnerPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfDecisions_Status_DueDate",
                table: "OfDecisions",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OfDelegations_DeputyUserId",
                table: "OfDelegations",
                column: "DeputyUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OfDelegations_PositionId",
                table: "OfDelegations",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfDevices_Code",
                table: "OfDevices",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OfInboxItems_RecipientPositionId_Status",
                table: "OfInboxItems",
                columns: new[] { "RecipientPositionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OfInboxItems_RecipientRole_Status",
                table: "OfInboxItems",
                columns: new[] { "RecipientRole", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OfInboxItems_RecipientUserId_Status",
                table: "OfInboxItems",
                columns: new[] { "RecipientUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OfInboxItems_SourceKey",
                table: "OfInboxItems",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_OfLeaveRequests_ApproverPositionId",
                table: "OfLeaveRequests",
                column: "ApproverPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLeaveRequests_EmployeeId_FromDate",
                table: "OfLeaveRequests",
                columns: new[] { "EmployeeId", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OfLeaveRequests_Number",
                table: "OfLeaveRequests",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OfLeaveRequests_Status",
                table: "OfLeaveRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetterApprovals_LetterId",
                table: "OfLetterApprovals",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetterApprovals_PositionId",
                table: "OfLetterApprovals",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetterAttachments_LetterId",
                table: "OfLetterAttachments",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetterRecipients_LetterId",
                table: "OfLetterRecipients",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetterRecipients_PartyId",
                table: "OfLetterRecipients",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetterRecipients_PositionId",
                table: "OfLetterRecipients",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_Date",
                table: "OfLetters",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_FollowUpLetterId",
                table: "OfLetters",
                column: "FollowUpLetterId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_Kind_Status",
                table: "OfLetters",
                columns: new[] { "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_Number",
                table: "OfLetters",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0 AND [Number] IS NOT NULL AND [Imported] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_NumberRuleId",
                table: "OfLetters",
                column: "NumberRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_ReplyToLetterId",
                table: "OfLetters",
                column: "ReplyToLetterId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_SenderPartyId",
                table: "OfLetters",
                column: "SenderPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_SenderPositionId",
                table: "OfLetters",
                column: "SenderPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_SignerPositionId",
                table: "OfLetters",
                column: "SignerPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetters_TemplateId",
                table: "OfLetters",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_OfLetterTemplates_SignerPositionId",
                table: "OfLetterTemplates",
                column: "SignerPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfMeetingAgenda_MeetingId",
                table: "OfMeetingAgenda",
                column: "MeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_OfMeetingAttendees_MeetingId",
                table: "OfMeetingAttendees",
                column: "MeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_OfMeetingAttendees_PositionId",
                table: "OfMeetingAttendees",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfMeetings_ChairPositionId",
                table: "OfMeetings",
                column: "ChairPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfMeetings_Number",
                table: "OfMeetings",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OfMeetings_SecretaryPositionId",
                table: "OfMeetings",
                column: "SecretaryPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfMeetings_Status",
                table: "OfMeetings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OfNumberCounters_RuleId_Year",
                table: "OfNumberCounters",
                columns: new[] { "RuleId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfNumberingRules_UnitId",
                table: "OfNumberingRules",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_OfPositions_EmployeeId",
                table: "OfPositions",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_OfPositions_UnitId",
                table: "OfPositions",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_OfPositions_UserId",
                table: "OfPositions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OfRecordLinks_Kind_RecordId",
                table: "OfRecordLinks",
                columns: new[] { "Kind", "RecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_OfRecordLinks_LetterId",
                table: "OfRecordLinks",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_OfRecordLinks_MeetingId",
                table: "OfRecordLinks",
                column: "MeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_OfReferralEntries_ReferralId",
                table: "OfReferralEntries",
                column: "ReferralId");

            migrationBuilder.CreateIndex(
                name: "IX_OfReferrals_FromPositionId",
                table: "OfReferrals",
                column: "FromPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfReferrals_LetterId",
                table: "OfReferrals",
                column: "LetterId");

            migrationBuilder.CreateIndex(
                name: "IX_OfReferrals_ParentId",
                table: "OfReferrals",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_OfReferrals_ToPositionId",
                table: "OfReferrals",
                column: "ToPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfRequestApprovals_PositionId",
                table: "OfRequestApprovals",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfRequestApprovals_RequestId",
                table: "OfRequestApprovals",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_OfRequests_Number",
                table: "OfRequests",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OfRequests_RequesterUserId",
                table: "OfRequests",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OfRequests_TypeId",
                table: "OfRequests",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_OfRequestTypes_Title",
                table: "OfRequestTypes",
                column: "Title",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OfShiftAssignments_EmployeeId_FromDate",
                table: "OfShiftAssignments",
                columns: new[] { "EmployeeId", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OfShiftAssignments_ShiftId",
                table: "OfShiftAssignments",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_OfUnits_Code",
                table: "OfUnits",
                column: "Code",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OfUnits_ManagerPositionId",
                table: "OfUnits",
                column: "ManagerPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfUnits_ParentId",
                table: "OfUnits",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_OfUserSignatures_UserId",
                table: "OfUserSignatures",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QaManagementReviews_OfMeetings_OfMeetingId",
                table: "QaManagementReviews",
                column: "OfMeetingId",
                principalTable: "OfMeetings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfDecisions_OfMeetings_MeetingId",
                table: "OfDecisions",
                column: "MeetingId",
                principalTable: "OfMeetings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OfDecisions_OfPositions_OwnerPositionId",
                table: "OfDecisions",
                column: "OwnerPositionId",
                principalTable: "OfPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfDelegations_OfPositions_PositionId",
                table: "OfDelegations",
                column: "PositionId",
                principalTable: "OfPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLeaveRequests_OfPositions_ApproverPositionId",
                table: "OfLeaveRequests",
                column: "ApproverPositionId",
                principalTable: "OfPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetterApprovals_OfLetters_LetterId",
                table: "OfLetterApprovals",
                column: "LetterId",
                principalTable: "OfLetters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetterApprovals_OfPositions_PositionId",
                table: "OfLetterApprovals",
                column: "PositionId",
                principalTable: "OfPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetterAttachments_OfLetters_LetterId",
                table: "OfLetterAttachments",
                column: "LetterId",
                principalTable: "OfLetters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetterRecipients_OfLetters_LetterId",
                table: "OfLetterRecipients",
                column: "LetterId",
                principalTable: "OfLetters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetterRecipients_OfPositions_PositionId",
                table: "OfLetterRecipients",
                column: "PositionId",
                principalTable: "OfPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetters_OfLetterTemplates_TemplateId",
                table: "OfLetters",
                column: "TemplateId",
                principalTable: "OfLetterTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetters_OfNumberingRules_NumberRuleId",
                table: "OfLetters",
                column: "NumberRuleId",
                principalTable: "OfNumberingRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetters_OfPositions_SenderPositionId",
                table: "OfLetters",
                column: "SenderPositionId",
                principalTable: "OfPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetters_OfPositions_SignerPositionId",
                table: "OfLetters",
                column: "SignerPositionId",
                principalTable: "OfPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfLetterTemplates_OfPositions_SignerPositionId",
                table: "OfLetterTemplates",
                column: "SignerPositionId",
                principalTable: "OfPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfMeetingAgenda_OfMeetings_MeetingId",
                table: "OfMeetingAgenda",
                column: "MeetingId",
                principalTable: "OfMeetings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OfMeetingAttendees_OfMeetings_MeetingId",
                table: "OfMeetingAttendees",
                column: "MeetingId",
                principalTable: "OfMeetings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OfMeetingAttendees_OfPositions_PositionId",
                table: "OfMeetingAttendees",
                column: "PositionId",
                principalTable: "OfPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfMeetings_OfPositions_ChairPositionId",
                table: "OfMeetings",
                column: "ChairPositionId",
                principalTable: "OfPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfMeetings_OfPositions_SecretaryPositionId",
                table: "OfMeetings",
                column: "SecretaryPositionId",
                principalTable: "OfPositions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfNumberCounters_OfNumberingRules_RuleId",
                table: "OfNumberCounters",
                column: "RuleId",
                principalTable: "OfNumberingRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfNumberingRules_OfUnits_UnitId",
                table: "OfNumberingRules",
                column: "UnitId",
                principalTable: "OfUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfPositions_OfUnits_UnitId",
                table: "OfPositions",
                column: "UnitId",
                principalTable: "OfUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QaManagementReviews_OfMeetings_OfMeetingId",
                table: "QaManagementReviews");

            migrationBuilder.DropForeignKey(
                name: "FK_OfUnits_OfPositions_ManagerPositionId",
                table: "OfUnits");

            migrationBuilder.DropTable(
                name: "OfAttendanceDays");

            migrationBuilder.DropTable(
                name: "OfAttendanceMonths");

            migrationBuilder.DropTable(
                name: "OfAttendancePolicies");

            migrationBuilder.DropTable(
                name: "OfClockEvents");

            migrationBuilder.DropTable(
                name: "OfDecisions");

            migrationBuilder.DropTable(
                name: "OfDelegations");

            migrationBuilder.DropTable(
                name: "OfDevices");

            migrationBuilder.DropTable(
                name: "OfInboxItems");

            migrationBuilder.DropTable(
                name: "OfLeaveRequests");

            migrationBuilder.DropTable(
                name: "OfLetterApprovals");

            migrationBuilder.DropTable(
                name: "OfLetterAttachments");

            migrationBuilder.DropTable(
                name: "OfLetterRecipients");

            migrationBuilder.DropTable(
                name: "OfMeetingAgenda");

            migrationBuilder.DropTable(
                name: "OfMeetingAttendees");

            migrationBuilder.DropTable(
                name: "OfNumberCounters");

            migrationBuilder.DropTable(
                name: "OfRecordLinks");

            migrationBuilder.DropTable(
                name: "OfReferralEntries");

            migrationBuilder.DropTable(
                name: "OfRequestApprovals");

            migrationBuilder.DropTable(
                name: "OfShiftAssignments");

            migrationBuilder.DropTable(
                name: "OfUserSignatures");

            migrationBuilder.DropTable(
                name: "OfMeetings");

            migrationBuilder.DropTable(
                name: "OfReferrals");

            migrationBuilder.DropTable(
                name: "OfRequests");

            migrationBuilder.DropTable(
                name: "OfShifts");

            migrationBuilder.DropTable(
                name: "OfLetters");

            migrationBuilder.DropTable(
                name: "OfRequestTypes");

            migrationBuilder.DropTable(
                name: "OfExternalParties");

            migrationBuilder.DropTable(
                name: "OfLetterTemplates");

            migrationBuilder.DropTable(
                name: "OfNumberingRules");

            migrationBuilder.DropTable(
                name: "OfPositions");

            migrationBuilder.DropTable(
                name: "OfUnits");

            migrationBuilder.DropIndex(
                name: "IX_QaManagementReviews_OfMeetingId",
                table: "QaManagementReviews");

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2600);

            migrationBuilder.DeleteData(
                table: "AppRoleClaims",
                keyColumn: "Id",
                keyValue: 2700);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: 27);

            migrationBuilder.DropColumn(
                name: "OfMeetingId",
                table: "QaManagementReviews");
        }
    }
}
