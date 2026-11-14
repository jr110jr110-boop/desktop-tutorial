using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AgentLayerA1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentStepSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StepCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    GateDueHours = table.Column<int>(type: "int", nullable: false, defaultValue: 48),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentStepSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StepCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Station = table.Column<int>(type: "int", nullable: false),
                    AgentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    TargetType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    TargetNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ContractId = table.Column<int>(type: "int", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GateRoles = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    GateUserId = table.Column<int>(type: "int", nullable: true),
                    Link = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsFallback = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReturnedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OutputType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    OutputId = table.Column<int>(type: "int", nullable: true),
                    OutputNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReturnedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClosedReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentTaskEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FromState = table.Column<int>(type: "int", nullable: true),
                    ToState = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SeatCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTaskEvents_AgentTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentStepSettings_StepCode",
                table: "AgentStepSettings",
                column: "StepCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskEvents_TaskId_At",
                table: "AgentTaskEvents",
                columns: new[] { "TaskId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_ContractId_Station_State",
                table: "AgentTasks",
                columns: new[] { "ContractId", "Station", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_Open",
                table: "AgentTasks",
                columns: new[] { "StepCode", "TargetType", "TargetId" },
                unique: true,
                filter: "[State] < 10 AND [Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_State_AgentCode_NextAttemptAt",
                table: "AgentTasks",
                columns: new[] { "State", "AgentCode", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_TargetType_TargetId",
                table: "AgentTasks",
                columns: new[] { "TargetType", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentStepSettings");

            migrationBuilder.DropTable(
                name: "AgentTaskEvents");

            migrationBuilder.DropTable(
                name: "AgentTasks");
        }
    }
}
