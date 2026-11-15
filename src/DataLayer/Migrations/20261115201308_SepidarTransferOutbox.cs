using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class SepidarTransferOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpOutboxItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransferTypeCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    SourceNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceRowVersion = table.Column<byte[]>(type: "varbinary(8)", maxLength: 8, nullable: true),
                    IsCorrection = table.Column<bool>(type: "bit", nullable: false),
                    CorrectsItemId = table.Column<int>(type: "int", nullable: true),
                    SupersededByItemId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedByAgent = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PreparedByUserId = table.Column<int>(type: "int", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceMakerUserIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PayrollRestricted = table.Column<bool>(type: "bit", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    RetryBaseline = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpOutboxItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpTransferTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpTransferTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpOutboxAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SenderName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpOutboxAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpOutboxAttempts_SpOutboxItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "SpOutboxItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpOutboxAttempts_ItemId_AttemptNumber",
                table: "SpOutboxAttempts",
                columns: new[] { "ItemId", "AttemptNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_SpOutboxItems_Live",
                table: "SpOutboxItems",
                columns: new[] { "TransferTypeCode", "SourceType", "SourceId" },
                unique: true,
                filter: "[Status] IN (1, 2, 3, 4, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_SpOutboxItems_PreparedAt",
                table: "SpOutboxItems",
                column: "PreparedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SpOutboxItems_Status_NextAttemptAt",
                table: "SpOutboxItems",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SpOutboxItems_TransferTypeCode_SourceType_SourceId_Id",
                table: "SpOutboxItems",
                columns: new[] { "TransferTypeCode", "SourceType", "SourceId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SpTransferTypes_Code",
                table: "SpTransferTypes",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpOutboxAttempts");

            migrationBuilder.DropTable(
                name: "SpTransferTypes");

            migrationBuilder.DropTable(
                name: "SpOutboxItems");
        }
    }
}
