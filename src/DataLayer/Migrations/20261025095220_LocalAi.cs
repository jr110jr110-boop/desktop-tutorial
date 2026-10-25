using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class LocalAi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiAuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SeatCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PromptVersionId = table.Column<int>(type: "int", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PromptHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiDocumentChunks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ChunkIndex = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Embedding = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Dim = table.Column<int>(type: "int", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDocumentChunks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiEvalCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Question = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ExpectedTools = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExpectedFacts = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiEvalCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiEvalRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Total = table.Column<int>(type: "int", nullable: false),
                    Passed = table.Column<int>(type: "int", nullable: false),
                    NumericFailures = table.Column<int>(type: "int", nullable: false),
                    P50Ms = table.Column<int>(type: "int", nullable: false),
                    P95Ms = table.Column<int>(type: "int", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiEvalRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiModelEndpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    LastHealthAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastHealthOk = table.Column<bool>(type: "bit", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModelEndpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiPromptVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActivatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPromptVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiSeats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ServiceUserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Roles = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Schedule = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptVersionCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    OutputKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSeats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToolName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Verified = table.Column<bool>(type: "bit", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiMessages_AiConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AiConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiSeatRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeatId = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Outputs = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AsOf = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSeatRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSeatRuns_AiSeats_SeatId",
                        column: x => x.SeatId,
                        principalTable: "AiSeats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiToolCallLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    MessageId = table.Column<int>(type: "int", nullable: true),
                    ToolName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ResultHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ResultChars = table.Column<int>(type: "int", nullable: false),
                    Allowed = table.Column<bool>(type: "bit", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiToolCallLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiToolCallLogs_AiConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AiConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiToolCallLogs_AiMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "AiMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAuditEvents_At",
                table: "AiAuditEvents",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AiAuditEvents_Kind_At",
                table: "AiAuditEvents",
                columns: new[] { "Kind", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_AiAuditEvents_UserId",
                table: "AiAuditEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_UserId_LastAt",
                table: "AiConversations",
                columns: new[] { "UserId", "LastAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDocumentChunks_SourceType_SourceId",
                table: "AiDocumentChunks",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiEvalCases_Code",
                table: "AiEvalCases",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiMessages_ConversationId_Id",
                table: "AiMessages",
                columns: new[] { "ConversationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AiModelEndpoints_Kind_Priority",
                table: "AiModelEndpoints",
                columns: new[] { "Kind", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_AiPromptVersions_Code",
                table: "AiPromptVersions",
                column: "Code",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AiPromptVersions_Code_Version",
                table: "AiPromptVersions",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiSeatRuns_SeatId_StartedAt",
                table: "AiSeatRuns",
                columns: new[] { "SeatId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiSeats_Code",
                table: "AiSeats",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiToolCallLogs_ConversationId",
                table: "AiToolCallLogs",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AiToolCallLogs_MessageId",
                table: "AiToolCallLogs",
                column: "MessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAuditEvents");

            migrationBuilder.DropTable(
                name: "AiDocumentChunks");

            migrationBuilder.DropTable(
                name: "AiEvalCases");

            migrationBuilder.DropTable(
                name: "AiEvalRuns");

            migrationBuilder.DropTable(
                name: "AiModelEndpoints");

            migrationBuilder.DropTable(
                name: "AiPromptVersions");

            migrationBuilder.DropTable(
                name: "AiSeatRuns");

            migrationBuilder.DropTable(
                name: "AiToolCallLogs");

            migrationBuilder.DropTable(
                name: "AiSeats");

            migrationBuilder.DropTable(
                name: "AiMessages");

            migrationBuilder.DropTable(
                name: "AiConversations");
        }
    }
}
