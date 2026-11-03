using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class PayrollR1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "OfShiftAssignments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedAddresses",
                table: "OfDevices",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "KeyRotatedAt",
                table: "OfDevices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KeyRotatedByUserId",
                table: "OfDevices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Approval",
                table: "OfClockEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "OfClockEvents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByUserId",
                table: "OfClockEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImportBatchId",
                table: "OfClockEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "OfClockEvents",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EarlyLeaveDeductionDisabled",
                table: "OfAttendancePolicies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MonthConfirmByHrPeer",
                table: "OfAttendancePolicies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAt",
                table: "OfAttendanceMonths",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfirmedByUserId",
                table: "OfAttendanceMonths",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyJson",
                table: "OfAttendanceMonths",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "OfAttendanceMonths",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotHash",
                table: "OfAttendanceMonths",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotJson",
                table: "OfAttendanceMonths",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "OfAttendanceMonths",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EarlyLeaveDeductionMinutes",
                table: "OfAttendanceDays",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnapprovedOvertimeMinutes",
                table: "OfAttendanceDays",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BaseSource",
                table: "FnTerminations",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankIban",
                table: "FnPayslips",
                type: "nvarchar(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IbanSnapshotted",
                table: "FnPayrollRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TriggerUserIds",
                table: "FnOutboxPackages",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "FnEmployees",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastEditedAt",
                table: "FnEmployees",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastEditedByUserId",
                table: "FnEmployees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PendingApproval",
                table: "FnEmployees",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ApprovalStatus",
                table: "FnEmployeeLoans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "FnEmployeeLoans",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByUserId",
                table: "FnEmployeeLoans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "FnEmployeeLoans",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastEditedAt",
                table: "FnEmployeeLoans",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastEditedByUserId",
                table: "FnEmployeeLoans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "FnEmployeeLoans",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIds",
                table: "FnAttendanceSheets",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Locked",
                table: "FnAttendanceSheets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "FnAttendanceSheets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FnAttendanceCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SheetId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    ProposedJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CurrentJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProposedByUserId = table.Column<int>(type: "int", nullable: false),
                    ProposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnAttendanceCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnAttendanceCorrections_FnAttendanceSheets_SheetId",
                        column: x => x.SheetId,
                        principalTable: "FnAttendanceSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnAttendanceCorrections_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnEmployeeChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProposedJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WageEffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProposedByUserId = table.Column<int>(type: "int", nullable: false),
                    ProposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnEmployeeChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnEmployeeChanges_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnLeaveOpenings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    CarriedDays = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EnteredByUserId = table.Column<int>(type: "int", nullable: false),
                    EnteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnLeaveOpenings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnLeaveOpenings_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfClockImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportedByUserId = table.Column<int>(type: "int", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DeviceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfClockImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfDevicePushLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: true),
                    DeviceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    Accepted = table.Column<int>(type: "int", nullable: false),
                    Duplicates = table.Column<int>(type: "int", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfDevicePushLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfDevicePushLogs_OfDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "OfDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FnEmployeeWages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DailyWage = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ChangeId = table.Column<int>(type: "int", nullable: true),
                    IsBaseline = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FnEmployeeWages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FnEmployeeWages_FnEmployeeChanges_ChangeId",
                        column: x => x.ChangeId,
                        principalTable: "FnEmployeeChanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FnEmployeeWages_FnEmployees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "FnEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfClockEvents_Approval",
                table: "OfClockEvents",
                column: "Approval");

            migrationBuilder.CreateIndex(
                name: "IX_OfClockEvents_ImportBatchId",
                table: "OfClockEvents",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FnAttendanceCorrections_EmployeeId",
                table: "FnAttendanceCorrections",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_FnAttendanceCorrections_SheetId",
                table: "FnAttendanceCorrections",
                column: "SheetId");

            migrationBuilder.CreateIndex(
                name: "IX_FnAttendanceCorrections_Status",
                table: "FnAttendanceCorrections",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FnEmployeeChanges_EmployeeId",
                table: "FnEmployeeChanges",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_FnEmployeeChanges_Status",
                table: "FnEmployeeChanges",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FnEmployeeWages_ChangeId",
                table: "FnEmployeeWages",
                column: "ChangeId");

            migrationBuilder.CreateIndex(
                name: "IX_FnEmployeeWages_EmployeeId_EffectiveFrom",
                table: "FnEmployeeWages",
                columns: new[] { "EmployeeId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FnLeaveOpenings_EmployeeId_Year_Month",
                table: "FnLeaveOpenings",
                columns: new[] { "EmployeeId", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_FnLeaveOpenings_Status",
                table: "FnLeaveOpenings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OfClockImportBatches_Status",
                table: "OfClockImportBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OfDevicePushLogs_DeviceId_ReceivedAt",
                table: "OfDevicePushLogs",
                columns: new[] { "DeviceId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OfDevicePushLogs_Outcome_ReceivedAt",
                table: "OfDevicePushLogs",
                columns: new[] { "Outcome", "ReceivedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_OfClockEvents_OfClockImportBatches_ImportBatchId",
                table: "OfClockEvents",
                column: "ImportBatchId",
                principalTable: "OfClockImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OfClockEvents_OfClockImportBatches_ImportBatchId",
                table: "OfClockEvents");

            migrationBuilder.DropTable(
                name: "FnAttendanceCorrections");

            migrationBuilder.DropTable(
                name: "FnEmployeeWages");

            migrationBuilder.DropTable(
                name: "FnLeaveOpenings");

            migrationBuilder.DropTable(
                name: "OfClockImportBatches");

            migrationBuilder.DropTable(
                name: "OfDevicePushLogs");

            migrationBuilder.DropTable(
                name: "FnEmployeeChanges");

            migrationBuilder.DropIndex(
                name: "IX_OfClockEvents_Approval",
                table: "OfClockEvents");

            migrationBuilder.DropIndex(
                name: "IX_OfClockEvents_ImportBatchId",
                table: "OfClockEvents");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "OfShiftAssignments");

            migrationBuilder.DropColumn(
                name: "AllowedAddresses",
                table: "OfDevices");

            migrationBuilder.DropColumn(
                name: "KeyRotatedAt",
                table: "OfDevices");

            migrationBuilder.DropColumn(
                name: "KeyRotatedByUserId",
                table: "OfDevices");

            migrationBuilder.DropColumn(
                name: "Approval",
                table: "OfClockEvents");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "OfClockEvents");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "OfClockEvents");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "OfClockEvents");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "OfClockEvents");

            migrationBuilder.DropColumn(
                name: "EarlyLeaveDeductionDisabled",
                table: "OfAttendancePolicies");

            migrationBuilder.DropColumn(
                name: "MonthConfirmByHrPeer",
                table: "OfAttendancePolicies");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "OfAttendanceMonths");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                table: "OfAttendanceMonths");

            migrationBuilder.DropColumn(
                name: "PolicyJson",
                table: "OfAttendanceMonths");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "OfAttendanceMonths");

            migrationBuilder.DropColumn(
                name: "SnapshotHash",
                table: "OfAttendanceMonths");

            migrationBuilder.DropColumn(
                name: "SnapshotJson",
                table: "OfAttendanceMonths");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "OfAttendanceMonths");

            migrationBuilder.DropColumn(
                name: "EarlyLeaveDeductionMinutes",
                table: "OfAttendanceDays");

            migrationBuilder.DropColumn(
                name: "UnapprovedOvertimeMinutes",
                table: "OfAttendanceDays");

            migrationBuilder.DropColumn(
                name: "BaseSource",
                table: "FnTerminations");

            migrationBuilder.DropColumn(
                name: "BankIban",
                table: "FnPayslips");

            migrationBuilder.DropColumn(
                name: "IbanSnapshotted",
                table: "FnPayrollRuns");

            migrationBuilder.DropColumn(
                name: "TriggerUserIds",
                table: "FnOutboxPackages");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "FnEmployees");

            migrationBuilder.DropColumn(
                name: "LastEditedAt",
                table: "FnEmployees");

            migrationBuilder.DropColumn(
                name: "LastEditedByUserId",
                table: "FnEmployees");

            migrationBuilder.DropColumn(
                name: "PendingApproval",
                table: "FnEmployees");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "FnEmployeeLoans");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "FnEmployeeLoans");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "FnEmployeeLoans");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "FnEmployeeLoans");

            migrationBuilder.DropColumn(
                name: "LastEditedAt",
                table: "FnEmployeeLoans");

            migrationBuilder.DropColumn(
                name: "LastEditedByUserId",
                table: "FnEmployeeLoans");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "FnEmployeeLoans");

            migrationBuilder.DropColumn(
                name: "EditorUserIds",
                table: "FnAttendanceSheets");

            migrationBuilder.DropColumn(
                name: "Locked",
                table: "FnAttendanceSheets");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "FnAttendanceSheets");
        }
    }
}
