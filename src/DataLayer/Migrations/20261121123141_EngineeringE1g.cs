using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class EngineeringE1g : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnMdlPermits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnMdlPermits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnMdlPermits_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnProjectMdls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CaseId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BasedOnListId = table.Column<int>(type: "int", nullable: true),
                    BasedOnCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    BasedOnRevision = table.Column<int>(type: "int", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ChangeNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CheckedByUserId = table.Column<int>(type: "int", nullable: true),
                    CheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IssuedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClientApprovalRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClientApprovalDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientApprovalRecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnProjectMdls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnProjectMdls_EnCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "EnCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EnProjectMdlLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectMdlId = table.Column<int>(type: "int", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Discipline = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    TenderStage = table.Column<bool>(type: "bit", nullable: false),
                    ClientSubmittal = table.Column<bool>(type: "bit", nullable: false),
                    StandardClause = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Units = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    Removed = table.Column<bool>(type: "bit", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    CreateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifyDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatorUserId = table.Column<int>(type: "int", nullable: false),
                    ModifierUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnProjectMdlLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnProjectMdlLines_EnProjectMdls_ProjectMdlId",
                        column: x => x.ProjectMdlId,
                        principalTable: "EnProjectMdls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnMdlPermits_Number",
                table: "EnMdlPermits",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_EnMdlPermits_OneLive",
                table: "EnMdlPermits",
                column: "CaseId",
                unique: true,
                filter: "[Status] IN (0, 1) AND [Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnProjectMdlLines_ProjectMdlId_Seq",
                table: "EnProjectMdlLines",
                columns: new[] { "ProjectMdlId", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_EnProjectMdls_CaseId_Revision",
                table: "EnProjectMdls",
                columns: new[] { "CaseId", "Revision" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_EnProjectMdls_Number",
                table: "EnProjectMdls",
                column: "Number",
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_EnProjectMdls_OneLive",
                table: "EnProjectMdls",
                column: "CaseId",
                unique: true,
                filter: "[Status] IN (0, 1, 2) AND [Deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnMdlPermits");

            migrationBuilder.DropTable(
                name: "EnProjectMdlLines");

            migrationBuilder.DropTable(
                name: "EnProjectMdls");
        }
    }
}
