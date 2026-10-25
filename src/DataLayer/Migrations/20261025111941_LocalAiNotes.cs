using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class LocalAiNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsScheduled",
                table: "AiSeatRuns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AiNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeatCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: true),
                    RuleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Verified = table.Column<bool>(type: "bit", nullable: false),
                    SeatRunId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiNotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiSeatRuns_SeatId_Running",
                table: "AiSeatRuns",
                column: "SeatId",
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AiEvalRuns_Running",
                table: "AiEvalRuns",
                column: "FinishedAt",
                unique: true,
                filter: "[FinishedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiNotes_SeatCode_TargetType_TargetId_RuleCode",
                table: "AiNotes",
                columns: new[] { "SeatCode", "TargetType", "TargetId", "RuleCode" },
                unique: true,
                filter: "[TargetId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiNotes_Status_CreatedAt",
                table: "AiNotes",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiNotes_TargetType_TargetId",
                table: "AiNotes",
                columns: new[] { "TargetType", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiNotes");

            migrationBuilder.DropIndex(
                name: "IX_AiSeatRuns_SeatId_Running",
                table: "AiSeatRuns");

            migrationBuilder.DropIndex(
                name: "IX_AiEvalRuns_Running",
                table: "AiEvalRuns");

            migrationBuilder.DropColumn(
                name: "IsScheduled",
                table: "AiSeatRuns");
        }
    }
}
